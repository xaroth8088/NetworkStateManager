using System;
using System.Collections.Generic;

namespace NSM
{
    public enum InputRejection
    {
        None, NotReady, InvalidTick, InvalidCount, UnknownPlayer, WrongOwner,
        InvalidValue, Duplicate, RateLimited, ReplayBudgetExceeded, QueueFull
    }

    /// <summary>
    /// Server-owned admission policy. Unknown players and a missing value validator are rejected.
    /// A client may own multiple player IDs; IDs have no relationship to NGO object ownership.
    /// </summary>
    public sealed class InputAdmission
    {
        private readonly SimulationLimits limits;
        private readonly Dictionary<byte, ulong> owners = new();
        private readonly Dictionary<ulong, int> requestCounts = new();
        private readonly SortedDictionary<int, HashSet<byte>> accepted = new();
        private int updateTick = -1;
        private int replayTicks;
        private int requests;

        public InputAdmission(SimulationLimits limits)
        {
            this.limits = (limits ?? throw new ArgumentNullException(nameof(limits))).ValidatedCopy();
        }

        /// <summary>Validate game-specific values, including finite/ranged axes and allowed flags.</summary>
        public Func<byte, IPlayerInput, bool> ValidateValue { get; set; }

        public void AssignPlayer(byte playerId, ulong clientId) => owners[playerId] = clientId;
        public void RemovePlayer(byte playerId) => owners.Remove(playerId);
        internal void ResetSession() { accepted.Clear(); requestCounts.Clear(); updateTick = -1; requests = replayTicks = 0; }

        public void RemoveClient(ulong clientId)
        {
            var removed = new List<byte>();
            foreach (var pair in owners)
                if (pair.Value == clientId) removed.Add(pair.Key);
            foreach (byte id in removed) owners.Remove(id);
            requestCounts.Remove(clientId);
        }

        internal void BeginUpdate(int currentTick)
        {
            // Called exactly once by the transport for each FixedUpdate, even if simulation pauses.
            updateTick = currentTick;
            requests = replayTicks = 0;
            requestCounts.Clear();
            var expired = new List<int>();
            foreach (int tick in accepted.Keys)
            {
                if ((long)tick <= (long)currentTick - limits.historyTicks) expired.Add(tick);
                else break;
            }
            foreach (int tick in expired) accepted.Remove(tick);
        }

        internal InputRejection TryAccept(ulong sender, IReadOnlyDictionary<byte, IPlayerInput> inputs,
            int tick, int currentTick, int oldestRestorableTick, Type inputType)
        {
            if (updateTick < 0) return InputRejection.NotReady;
            requestCounts.TryGetValue(sender, out int count);
            // Bound attacker-created bookkeeping too: only budgeted senders get an entry.
            if (++requests > limits.maxMessagesPerUpdate || count >= limits.maxMessagesPerClientPerUpdate)
                return InputRejection.RateLimited;
            requestCounts[sender] = count + 1;
            if (tick <= 0 || tick <= oldestRestorableTick ||
                (long)tick <= (long)currentTick - limits.historyTicks ||
                (long)tick > (long)currentTick + limits.futureInputTicks)
                return InputRejection.InvalidTick;
            if (inputs == null || inputs.Count == 0 || inputs.Count > limits.maxInputsPerMessage)
                return InputRejection.InvalidCount;

            accepted.TryGetValue(tick, out var playersAtTick);
            foreach (var pair in inputs)
            {
                if (!owners.TryGetValue(pair.Key, out ulong owner)) return InputRejection.UnknownPlayer;
                if (owner != sender) return InputRejection.WrongOwner;
                if (playersAtTick != null && playersAtTick.Contains(pair.Key)) return InputRejection.Duplicate;
                if (pair.Value == null || pair.Value.GetType() != inputType || ValidateValue == null)
                    return InputRejection.InvalidValue;
                try
                {
                    if (!ValidateValue(pair.Key, pair.Value)) return InputRejection.InvalidValue;
                }
                catch (Exception) { return InputRejection.InvalidValue; }
            }

            int replayCost = tick > currentTick ? 0 : 2 * (currentTick - tick + 1);
            if ((long)replayTicks + replayCost > limits.maxReplayTicksPerUpdate)
                return InputRejection.ReplayBudgetExceeded;
            replayTicks += replayCost;
            if (playersAtTick == null) accepted[tick] = playersAtTick = new HashSet<byte>();
            foreach (byte player in inputs.Keys) playersAtTick.Add(player);
            return InputRejection.None;
        }
    }
}
