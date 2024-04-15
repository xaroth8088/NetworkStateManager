using System;
using System.Collections.Generic;
using UnityEngine;

namespace NSM
{
    public class GameStateManager
    {
        internal StateBuffer stateBuffer = new();   // TODO: change this to private, and also realGameTick, if we can
        internal GameEventsBuffer gameEventsBuffer = new(); // TODO: change this to private
        internal InputsBuffer inputsBuffer = new(); // TODO: change this to private
        internal int GameTick { get; private set; } = 0;  // The apparent game time, as seen during rollback or simulations
        internal int realGameTick { get; private set; } = 0;   // The actual game time (may get synchronized with the server sometimes)
        internal RandomManager Random { get; private set; }
        internal int lastAuthoritativeTick = 0;
        public bool isReplaying = false;

        // From this class's perspective, NetworkStateManager is the gateway to the game's code, and to Unity more broadly
        private readonly NetworkStateManager networkStateManager;

        public GameStateManager(NetworkStateManager _networkStateManager)
        {
            networkStateManager = _networkStateManager;
        }

        public StateFrameDTO CaptureStateFrame(int tick, NetworkIdManager networkIdManager)
        {
            StateFrameDTO newFrame = new()
            {
                gameTick = tick,
                PhysicsState = new PhysicsStateDTO()
            };

            IGameState newGameState = TypeStore.Instance.CreateBlankGameState();
            networkStateManager.GetGameState(ref newGameState);
            newFrame.GameState = newGameState ?? throw new Exception("GetGameState failed to return a valid IGameState object");
            newFrame.PhysicsState.TakeSnapshot(PhysicsManager.GetNetworkedRigidbodies(networkIdManager));

            return newFrame;
        }

        internal void CaptureInitialFrame(NetworkIdManager networkIdManager)
        {
            stateBuffer[0] = CaptureStateFrame(0, networkIdManager);
        }

        internal StateFrameDTO RunSingleGameFrame(int tick, Dictionary<byte, IPlayerInput> playerInputs, HashSet<IGameEvent> events, NetworkIdManager networkIdManager)
        {
            networkStateManager.VerboseLog("Running single frame for tick " + tick);
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
            StateFrameDTO newFrame = CaptureStateFrame(tick, networkIdManager);

            return newFrame;
        }

        private void SimulateAuthoritativeFrame(StateFrameDTO serverFrame, NetworkIdManager networkIdManager)
        {
            networkStateManager.VerboseLog("SimulateAuthoritativeFrame");
            int serverTick = serverFrame.gameTick;
            GameTick = serverTick;
            Random.ResetRandom(GameTick);

            networkStateManager.ApplyEvents(gameEventsBuffer[serverTick]);
            networkStateManager.ApplyInputs(inputsBuffer.GetInputsForTick(serverTick));
            PhysicsManager.ApplyPhysicsState(serverFrame.PhysicsState, networkIdManager);
            networkStateManager.ApplyState(serverFrame.GameState);

            serverFrame.authoritative = true;

            stateBuffer[serverTick] = serverFrame;

            realGameTick = serverTick;
        }

        internal void SyncToServerState(StateFrameDTO serverState, GameEventsBuffer newGameEventsBuffer, int serverTick, int estimatedLag, NetworkIdManager networkIdManager)
        {
            // NOTE: when we get here, we'll be at the _end_ of frame realGameTick, and when we leave we'll be at the end of (serverTick + lag)

            // TODO: if the server's frame is exactly the same as our frame in that spot, we may not need to do any rollback simulation at all,
            //       and can maybe just adjust to the server lag instead (or even do nothing if it's within some tolerance)
            networkStateManager.VerboseLog("Resync with server.  Server sent state from the end of tick " + serverState.gameTick + " at server tick " + serverTick);

            TimeTravelToEndOf(serverState.gameTick - 1, newGameEventsBuffer, networkIdManager);
            SimulateAuthoritativeFrame(serverState, networkIdManager);
            TimeTravelToEndOf(serverTick + estimatedLag, newGameEventsBuffer, networkIdManager);

            // Set our last authoritative tick
            lastAuthoritativeTick = serverState.gameTick;
        }

        internal void SetInitialGameState(StateFrameDTO initialStateFrame, int randomSeedBase, int estimatedLag, NetworkIdManager networkIdManager)
        {
            // Store the state (the frame will be marked as authoritative when we're in SyncToServerState)
            StateFrameDTO stateFrame = (StateFrameDTO)initialStateFrame.Clone();
            stateBuffer[0] = stateFrame;
            Random = new(randomSeedBase);
            SyncToServerState(stateBuffer[0], gameEventsBuffer, 0, estimatedLag, networkIdManager);
        }

        private void SimulateUntilEndOfFrame(int targetTick, NetworkIdManager networkIdManager)
        {
            networkStateManager.VerboseLog("Running frames from (end of)" + realGameTick + " to (end of)" + targetTick);

            GameTick = realGameTick;

            while (GameTick < targetTick)
            {
                GameTick++;

                networkStateManager.VerboseLog("Simulating for tick " + GameTick);
                stateBuffer[GameTick] = RunSingleGameFrame(GameTick, inputsBuffer.GetInputsForTick(GameTick), gameEventsBuffer[GameTick], networkIdManager);
            }

            realGameTick = GameTick;
        }

        private void RewindTimeUntilEndOfFrame(int targetTick, NetworkIdManager networkIdManager)
        {
            // We want to end this function as though the frame at targetTick just ran

            // Safety
            if (targetTick < 0)
            {
                targetTick = 0;
            }

            networkStateManager.VerboseLog("Rewinding time until " + targetTick);
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

                networkStateManager.VerboseLog("Undoing events at tick " + GameTick + " (setting state to the moment before the events were originally run)");

                // Apply the frame state just prior to gameTick
                int prevTick = Math.Max(0, GameTick - 1);
                StateFrameDTO previousFrameState = stateBuffer[prevTick];
                Dictionary<byte, IPlayerInput> previousFrameInputs = inputsBuffer.GetInputsForTick(prevTick);

                networkStateManager.ApplyInputs(previousFrameInputs);
                PhysicsManager.ApplyPhysicsState(previousFrameState.PhysicsState, networkIdManager);
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
            PhysicsManager.ApplyPhysicsState(frameToRestore.PhysicsState, networkIdManager);
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

        private void TimeTravelToEndOf(int targetTick, GameEventsBuffer newGameEventsBuffer, NetworkIdManager networkIdManager)
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
                RewindTimeUntilEndOfFrame(targetTick, networkIdManager);
                gameEventsBuffer = newGameEventsBuffer;
                return;
            }
            else
            {
                networkStateManager.VerboseLog("Fast-forwarding time");
                gameEventsBuffer = newGameEventsBuffer;
                SimulateUntilEndOfFrame(targetTick, networkIdManager);
                return;
            }
        }

        internal void ReplayDueToInputs(PlayerInputsDTO playerInputs, int clientTimeTick, int serverTick, int estimatedLag, NetworkIdManager networkIdManager)
        {
            networkStateManager.VerboseLog("Replaying due to player inputs at client time " + clientTimeTick);

            // Rewind, set & predict, get caught up again
            TimeTravelToEndOf(clientTimeTick - 1, gameEventsBuffer, networkIdManager);
            inputsBuffer.SetPlayerInputsAtTick(playerInputs, clientTimeTick);
            TimeTravelToEndOf(serverTick + estimatedLag, gameEventsBuffer, networkIdManager);
        }

        internal void ReplayDueToEvents(int serverTimeTick, GameEventsBuffer newGameEventsBuffer, int estimatedLag, NetworkIdManager networkIdManager)
        {
            networkStateManager.VerboseLog("Updating upcoming game events, taking effect on tick " + serverTimeTick);

            // Probably rewinding time.
            // In either event, the new events buffer will be in place after this first call.
            TimeTravelToEndOf(serverTimeTick - 1, newGameEventsBuffer, networkIdManager);

            // Now, get caught up to where the server is
            TimeTravelToEndOf(serverTimeTick + estimatedLag, newGameEventsBuffer, networkIdManager);
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
    }
}
