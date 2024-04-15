using System;
using System.Collections.Generic;
using UnityEngine;

namespace NSM
{
    public class GameStateManager
    {
        private StateBuffer stateBuffer = new();
        internal GameEventsBuffer gameEventsBuffer = new();
        private readonly InputsBuffer inputsBuffer = new();
        internal int GameTick { get; private set; } = 0;  // The apparent game time, as seen during rollback or simulations
        internal int realGameTick { get; private set; } = 0;   // The actual game time (may get synchronized with the server sometimes)
        internal RandomManager Random { get; private set; }
        internal int lastAuthoritativeTick = 0;
        public bool isReplaying = false;
        public NetworkIdManager NetworkIdManager { get; private set; }

        private readonly NetworkStateManager networkStateManager;   // From this class's perspective, NetworkStateManager is the gateway to the game's code, and to Unity more broadly

        public GameStateManager(NetworkStateManager _networkStateManager, UnityEngine.SceneManagement.Scene scene)
        {
            networkStateManager = _networkStateManager;
            NetworkIdManager = new(networkStateManager);
            NetworkIdManager.SetupInitialNetworkIds(scene);
        }

        public StateFrameDTO CaptureStateFrame(int tick)
        {
            StateFrameDTO newFrame = new()
            {
                gameTick = tick,
                PhysicsState = new PhysicsStateDTO()
            };

            IGameState newGameState = TypeStore.Instance.CreateBlankGameState();
            networkStateManager.GetGameState(ref newGameState);
            newFrame.GameState = newGameState ?? throw new Exception("GetGameState failed to return a valid IGameState object");
            newFrame.PhysicsState.TakeSnapshot(PhysicsManager.GetNetworkedRigidbodies(NetworkIdManager));

            return newFrame;
        }

        internal void CaptureInitialFrame()
        {
            stateBuffer[0] = CaptureStateFrame(0);
        }

        internal StateFrameDTO RunSingleGameFrame(int tick)
        {
            Dictionary<byte, IPlayerInput> playerInputs = inputsBuffer.GetInputsForTick(tick);
            HashSet<IGameEvent> events = gameEventsBuffer[tick];

            networkStateManager.VerboseLog($"Running single frame for tick {tick}");
            GameTick = tick;

            Random.ResetRandom(tick);

            // Simulate the frame
            networkStateManager.ApplyEvents(events);
            networkStateManager.ApplyInputs(playerInputs);
            PhysicsManager.SyncTransforms();
            networkStateManager.PrePhysicsFrameUpdate();
            PhysicsManager.SimulatePhysics(Time.fixedDeltaTime);
            networkStateManager.PostPhysicsFrameUpdate();

            // Capture the state from the scene/game
            StateFrameDTO newFrame = CaptureStateFrame(tick);

            return newFrame;
        }

        private void SimulateAuthoritativeFrame(StateFrameDTO serverFrame)
        {
            networkStateManager.VerboseLog("SimulateAuthoritativeFrame");
            int serverTick = serverFrame.gameTick;
            GameTick = serverTick;
            Random.ResetRandom(GameTick);

            networkStateManager.ApplyEvents(gameEventsBuffer[serverTick]);
            networkStateManager.ApplyInputs(inputsBuffer.GetInputsForTick(serverTick));
            PhysicsManager.ApplyPhysicsState(serverFrame.PhysicsState, NetworkIdManager);
            networkStateManager.ApplyState(serverFrame.GameState);

            serverFrame.authoritative = true;

            stateBuffer[serverTick] = serverFrame;

            realGameTick = serverTick;
        }

        /// <summary>
        /// Given an authoritative server state and a target frame to catch up to based on network conditions,
        /// rewind the state to the given authoritative frame, apply the state, then fast-forward back to the estimate of where the server is
        /// </summary>
        /// <param name="serverState">The authoritative state to apply</param>
        /// <param name="newGameEventsBuffer">The authoritative set of game events</param>
        /// <param name="serverTick">What time is it on the server at time of sending?</param>
        /// <param name="estimatedLag">How long do we think it took to get from the server to us?</param>
        internal void SyncToServerState(StateFrameDTO serverState, GameEventsBuffer newGameEventsBuffer, int serverTick, int estimatedLag)
        {
            // NOTE: when we get here, we'll be at the _end_ of frame realGameTick, and when we leave we'll be at the end of (serverTick + lag)

            if (serverTick < lastAuthoritativeTick)
            {
                networkStateManager.VerboseLog($"Asked to synchronize to before the last authoritative frame, so drop it.  Server tick: {serverTick} Last authoritative tick: {lastAuthoritativeTick}");
                return;
            }

            // TODO: if the server's frame is exactly the same as our frame in that spot, we may not need to do any rollback simulation at all,
            //       and can maybe just adjust to the server lag instead (or even do nothing if it's within some tolerance)
            networkStateManager.VerboseLog($"Resync with server.  Server sent state from the end of tick {serverState.gameTick} at server tick {serverTick}");

            TimeTravelToEndOf(serverState.gameTick - 1, newGameEventsBuffer);
            SimulateAuthoritativeFrame(serverState);
            TimeTravelToEndOf(serverTick + estimatedLag, newGameEventsBuffer);

            // Set our last authoritative tick
            lastAuthoritativeTick = serverState.gameTick;
        }

        internal void SetInitialGameState(StateFrameDTO initialStateFrame, int randomSeedBase, int estimatedLag)
        {
            // Store the state (the frame will be marked as authoritative when we're in SyncToServerState)
            StateFrameDTO stateFrame = (StateFrameDTO)initialStateFrame.Clone();
            stateBuffer[0] = stateFrame;
            Random = new(randomSeedBase);
            SyncToServerState(stateBuffer[0], gameEventsBuffer, 0, estimatedLag);
        }

        private void SimulateUntilEndOfFrame(int targetTick)
        {
            networkStateManager.VerboseLog("Running frames from (end of)" + realGameTick + " to (end of)" + targetTick);

            GameTick = realGameTick;

            while (GameTick < targetTick)
            {
                GameTick++;

                networkStateManager.VerboseLog("Simulating for tick " + GameTick);
                stateBuffer[GameTick] = RunSingleGameFrame(GameTick);
            }

            realGameTick = GameTick;
        }

        private void RewindTimeUntilEndOfFrame(int targetTick)
        {
            // We want to end this function as though the frame at targetTick just ran

            // Safety
            if (targetTick < 0)
            {
                targetTick = 0;
            }

            networkStateManager.VerboseLog($"Rewinding time until {targetTick}");
            // For each frame moving backward (using gameTick as our iterator)
            isReplaying = true;
            GameTick = realGameTick;
            while (GameTick > targetTick)
            {
                // We can skip restoring gamestate at all whenever there are no events inside of a frame to roll back
                if (gameEventsBuffer[GameTick].Count == 0)
                {
                    GameTick--;
                    continue;
                }

                networkStateManager.VerboseLog($"Undoing events at tick {GameTick} (setting state to the moment before the events were originally run)");

                // Apply the frame state just prior to gameTick
                int prevTick = Math.Max(0, GameTick - 1);
                StateFrameDTO previousFrameState = stateBuffer[prevTick];
                Dictionary<byte, IPlayerInput> previousFrameInputs = inputsBuffer.GetInputsForTick(prevTick);

                networkStateManager.ApplyInputs(previousFrameInputs);
                PhysicsManager.ApplyPhysicsState(previousFrameState.PhysicsState, NetworkIdManager);
                networkStateManager.ApplyState(previousFrameState.GameState);

                Random.ResetRandom(GameTick);

                // Rewind any events present in gameTick
                networkStateManager.RollbackEvents(gameEventsBuffer[GameTick], stateBuffer[GameTick].GameState);

                GameTick--;
            }

            // We may escape the loop without doing any events (and therefore never applying state), so apply the state for the end of targetTick
            // TODO: minor optimization: detect when this happens, and skip applying state here
            networkStateManager.VerboseLog("Applying final state from tick " + targetTick);
            StateFrameDTO frameToRestore = stateBuffer[targetTick];
            Dictionary<byte, IPlayerInput> inputsFromFrameToRestore = inputsBuffer.GetInputsForTick(targetTick);

            networkStateManager.ApplyInputs(inputsFromFrameToRestore);
            PhysicsManager.ApplyPhysicsState(frameToRestore.PhysicsState, NetworkIdManager);
            networkStateManager.ApplyState(frameToRestore.GameState);

            realGameTick = targetTick;
            isReplaying = false;

            networkStateManager.VerboseLog("Done rewinding");
        }

        internal void AdvanceTime()
        {
            realGameTick++;
            GameTick = realGameTick;
        }

        private void TimeTravelToEndOf(int targetTick, GameEventsBuffer newGameEventsBuffer)
        {
            networkStateManager.VerboseLog("Time traveling from end of " + realGameTick + " until end of " + targetTick);

            // If the target is in the past, rewind time until we get to just before serverTick (rolling back any events along the way)
            // If it's in the future, simulate until we get to just before serverTick (playing any events along the way)
            if (targetTick == realGameTick)
            {
                networkStateManager.VerboseLog("Already there, so do nothing");
                gameEventsBuffer = newGameEventsBuffer;
                return;
            }
            else if (targetTick < realGameTick)
            {
                networkStateManager.VerboseLog("Rewinding time");
                RewindTimeUntilEndOfFrame(targetTick);
                gameEventsBuffer = newGameEventsBuffer;
                return;
            }
            else
            {
                networkStateManager.VerboseLog("Fast-forwarding time");
                gameEventsBuffer = newGameEventsBuffer;
                SimulateUntilEndOfFrame(targetTick);
                return;
            }
        }

        internal void ReplayDueToInputs(PlayerInputsDTO playerInputs, int clientTimeTick, int serverTick, int estimatedLag)
        {
            networkStateManager.VerboseLog("Replaying due to player inputs at client time " + clientTimeTick);

            // Rewind, set & predict, get caught up again
            TimeTravelToEndOf(clientTimeTick - 1, gameEventsBuffer);
            inputsBuffer.SetPlayerInputsAtTick(playerInputs, clientTimeTick);
            TimeTravelToEndOf(serverTick + estimatedLag, gameEventsBuffer);
        }

        internal void ReplayDueToEvents(int serverTimeTick, GameEventsBuffer newGameEventsBuffer, int estimatedLag)
        {
            networkStateManager.VerboseLog("Updating upcoming game events, taking effect on tick " + serverTimeTick);

            // Probably rewinding time.
            // In either event, the new events buffer will be in place after this first call.
            TimeTravelToEndOf(serverTimeTick - 1, newGameEventsBuffer);

            // Now, get caught up to where the server is
            TimeTravelToEndOf(serverTimeTick + estimatedLag, newGameEventsBuffer);
        }

        internal void ScheduleGameEvent(IGameEvent gameEvent, int eventTick)
        {
            if (eventTick == -1)
            {
                eventTick = GameTick + 1;
            }

            if (eventTick <= GameTick)
            {
                Debug.LogWarning("Game event scheduled for the past - will not be replayed on clients");
            }

            networkStateManager.VerboseLog("Game event scheduled for tick " + eventTick);
            gameEventsBuffer[eventTick].Add(gameEvent);
        }

        internal void RemoveEventAtTick(int eventTick, Predicate<IGameEvent> gameEventPredicate)
        {
            // TODO: since this is only ever happening during a rollback of some sort, do we even need to resend the new events state to the clients?
            gameEventsBuffer[eventTick].RemoveWhere(gameEventPredicate);
        }

        internal void SetRandomBase(int randomSeedBase)
        {
            Random = new(randomSeedBase);
        }

        internal void RunFixedUpdate()
        {
            Dictionary<byte, IPlayerInput> localInputs = new();
            networkStateManager.GetInputs(ref localInputs);
            inputsBuffer.SetLocalInputs(localInputs, realGameTick);

            // Actually simulate the frame
            stateBuffer[realGameTick] = RunSingleGameFrame(realGameTick);
        }

        internal IPlayerInput PredictedInputForPlayer(byte playerId, int gameTick)
        {
            return inputsBuffer.PredictInput(playerId, gameTick);
        }

        internal void PlayerInputsReceived(PlayerInputsDTO playerInputs, int clientTimeTick)
        {
            if (clientTimeTick > realGameTick)
            {
                // The server slowed down enough for the clients to get ahead of it.  For small deltas,
                // this isn't usually an issue.
                // TODO: figure out a strategy for inputs that have far-future inputs
                // TODO: figure out a strategy for detecting cheating that's happening (vs. normal slowdowns)
                Debug.LogWarning($"Client inputs are coming from server's future.  Server time: {realGameTick} Client time: {clientTimeTick}");
            }

            inputsBuffer.SetPlayerInputsAtTick(playerInputs, clientTimeTick);
        }

        internal void ProcessStateDeltaReceived(StateFrameDeltaDTO serverGameStateDelta, GameEventsBuffer newGameEventsBuffer, int serverTick, int estimatedLag, int sendStateDeltaEveryNFrames)
        {
            // Did the state arrive out of order?  If so, panic.
            if (serverTick != (lastAuthoritativeTick + sendStateDeltaEveryNFrames))
            {
                throw new Exception(
                    $"Server snapshot arrived out of order!  Requesting full state refresh.  Server state tick: {serverTick} expected: {lastAuthoritativeTick} + {sendStateDeltaEveryNFrames} = {lastAuthoritativeTick + sendStateDeltaEveryNFrames}"
                );
            }

            // Reconstitute the state from our delta
            networkStateManager.VerboseLog($"Applying delta against frame {serverTick - sendStateDeltaEveryNFrames}");
            StateFrameDTO serverGameState;
            serverGameState = serverGameStateDelta.ApplyTo(stateBuffer[serverTick - sendStateDeltaEveryNFrames]);
            serverGameState.authoritative = true;

            SyncToServerState(serverGameState, newGameEventsBuffer, serverTick, estimatedLag);
        }

        internal Dictionary<byte, IPlayerInput> GetMinimalInputsDiffForCurrentFrame()
        {
            return inputsBuffer.GetMinimalInputsDiff(realGameTick);
        }

        internal StateFrameDTO GetStateFrame(int tick)
        {
            return stateBuffer[tick];
        }
    }
}
