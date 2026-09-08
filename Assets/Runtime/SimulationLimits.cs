using System;

namespace NSM
{
    /// <summary>Resource budgets in simulation ticks, independent of a game's player count.</summary>
    [Serializable]
    public sealed class SimulationLimits
    {
        public int historyTicks = 256;
        public int futureInputTicks = 8;
        public int maxInputsPerMessage = 256;
        public int maxQueuedMessages = 128;
        public int maxMessagesPerUpdate = 16;
        public int maxMessagesPerClientPerUpdate = 4;
        public int maxReplayTicksPerUpdate = 1024;
        public int maxFutureEventTicks = 4096;
        public int maxEventsPerTick = 256;
        public int maxBufferedEvents = 4096;

        internal SimulationLimits ValidatedCopy()
        {
            Range(historyTicks, 2, 4096, nameof(historyTicks));
            Range(futureInputTicks, 0, historyTicks, nameof(futureInputTicks));
            Range(maxInputsPerMessage, 1, 256, nameof(maxInputsPerMessage));
            Range(maxQueuedMessages, 1, 4096, nameof(maxQueuedMessages));
            Range(maxMessagesPerUpdate, 1, maxQueuedMessages, nameof(maxMessagesPerUpdate));
            Range(maxMessagesPerClientPerUpdate, 1, maxMessagesPerUpdate, nameof(maxMessagesPerClientPerUpdate));
            Range(maxReplayTicksPerUpdate, 4 * historyTicks, 65536, nameof(maxReplayTicksPerUpdate));
            Range(maxFutureEventTicks, 1, 65536, nameof(maxFutureEventTicks));
            Range(maxEventsPerTick, 1, 4096, nameof(maxEventsPerTick));
            Range(maxBufferedEvents, maxEventsPerTick, 65536, nameof(maxBufferedEvents));
            return (SimulationLimits)MemberwiseClone();
        }

        private static void Range(int value, int min, int max, string name)
        {
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(name, value, $"Expected {min} through {max}.");
        }
    }
}
