using System;
using System.Collections.Generic;
using UnityEngine;

namespace NSM
{
    /// <summary>
    /// Manages the state of the game, including networking states, input buffering, and state synchronization.
    /// </summary>
    internal class GameStateManager
    {
        private readonly IInputsBuffer _inputsBuffer;
        private readonly IInternalNetworkStateManager _networkStateManager;
        private readonly IStateBuffer _stateBuffer;

        /// <summary>
        /// Initializes the GameStateManager with its parent NetworkStateManager and the scene for setting up network IDs
        /// </summary>
        /// <param name="networkStateManager">From this class's perspective, NetworkStateManager is the gateway to the game's code, and to Unity more broadly</param>
        /// <param name="scene">Scene to set up initial network IDs.</param>
        internal GameStateManager(
            IInternalNetworkStateManager networkStateManager,
            IGameEventsBuffer gameEventsBuffer,
            IInputsBuffer inputsBuffer,
            IStateBuffer stateBuffer,
            INetworkIdManager networkIdManager,
            UnityEngine.SceneManagement.Scene scene
        )
        {
            _networkStateManager = networkStateManager ?? throw new ArgumentNullException(nameof(networkStateManager));
            GameEventsBuffer = gameEventsBuffer ?? throw new ArgumentNullException(nameof(gameEventsBuffer));
            _inputsBuffer = inputsBuffer ?? throw new ArgumentNullException(nameof(inputsBuffer));
            _stateBuffer = stateBuffer ?? throw new ArgumentNullException(nameof(stateBuffer));
            NetworkIdManager = networkIdManager ?? throw new ArgumentNullException(nameof(networkIdManager));

            NetworkIdManager.SetupInitialNetworkIds(scene);
        }

        internal IGameEventsBuffer GameEventsBuffer { get; private set; }
        internal int GameTick { get; private set; } = 0;    // The apparent game time, as seen during rollback or simulations
        internal bool IsReplaying { get; private set; } = false;
        internal int LastAuthoritativeTick { get; private set; } = 0;
        internal INetworkIdManager NetworkIdManager { get; private set; }
        internal RandomManager Random { get; private set; }
        internal int RealGameTick { get; private set; } = 0;   // The actual game time (may get synchronized with the server sometimes)

        /// <summary>
        /// Advances the game time by one tick.
        /// </summary>
        internal void AdvanceTime()
        {
            RealGameTick++;
            GameTick = RealGameTick;
        }

        /// <summary>
        /// Captures the initial frame of the game state.
        /// </summary>
        internal void CaptureInitialFrame() => _stateBuffer[0] = CaptureStateFrame(0);

        /// <summary>
        /// Gets the set of players' inputs that have changed from the previous frame
        /// </summary>
        /// <returns>A dictionary mapping player IDs to their respective inputs. Player IDs without a change are not included.</returns>
        internal Dictionary<byte, IPlayerInput> GetMinimalInputsDiffForCurrentFrame() => _inputsBuffer.GetMinimalInputsDiff(RealGameTick);

        /// <summary>
        /// Retrieves a state frame from the buffer for a specified tick.
        /// </summary>
        /// <param name="tick">The tick for which to retrieve the state frame.</param>
        /// <returns>The state frame at the specified tick.</returns>
        internal StateFrameDTO GetStateFrame(int tick) => _stateBuffer[tick];

        /// <summary>
        /// Processes received player inputs, adjusting for any discrepancies in client-server time synchronization.
        /// </summary>
        /// <param name="playerInputs">DTO containing player inputs.</param>
        /// <param name="clientTimeTick">The tick count reported by the client.</param>
        internal void PlayerInputsReceived(PlayerInputsDTO playerInputs, int clientTimeTick)
        {
            if (clientTimeTick > RealGameTick)
            {
                // The server slowed down enough for the clients to get ahead of it.  For small deltas,
                // this isn't usually an issue.
                // TODO: figure out a strategy for inputs that have far-future inputs
                // TODO: figure out a strategy for detecting cheating that's happening (vs. normal slowdowns)
                Debug.LogWarning($"Client inputs are coming from server's future.  Server time: {RealGameTick} Client time: {clientTimeTick}");
            }

            _inputsBuffer.SetPlayerInputsAtTick(playerInputs, clientTimeTick);

            ReplayDueToInputs(playerInputs, clientTimeTick, RealGameTick, 0);
        }

        /// <summary>
        /// Predicts player input for a given player at a specified game tick.
        /// </summary>
        /// <param name="playerId">The ID of the player for whom to predict input.</param>
        /// <param name="gameTick">The game tick at which to predict input.</param>
        /// <returns>The predicted input for the player.</returns>
        internal IPlayerInput PredictedInputForPlayer(byte playerId, int gameTick) => _inputsBuffer.PredictInput(playerId, gameTick);

        /// <summary>
        /// Receives a state delta from the server and synchronizes to that state
        /// </summary>
        /// <param name="serverGameStateDelta">The state delta received from the server.</param>
        /// <param name="newGameEventsBuffer">Buffer for new game events received with the state delta.</param>
        /// <param name="serverTick">The server tick when the state delta was generated.  Effectively, this is the target time to replay to.</param>
        /// <param name="estimatedLag">The estimated network lag in ticks.</param>
        /// <param name="sendStateDeltaEveryNFrames">The interval at which state deltas are sent from the server.</param>
        internal void ProcessStateDeltaReceived(StateFrameDeltaDTO serverGameStateDelta, GameEventsBuffer newGameEventsBuffer, int serverTick, int estimatedLag, int sendStateDeltaEveryNFrames)
        {
            // Did the state arrive out of order?  If so, panic.
            if (serverTick != (LastAuthoritativeTick + sendStateDeltaEveryNFrames))
            {
                throw new InvalidOperationException(
                    $"Server snapshot arrived out of order!  Server state tick: {serverTick} expected: {LastAuthoritativeTick} + {sendStateDeltaEveryNFrames} = {LastAuthoritativeTick + sendStateDeltaEveryNFrames}"
                );
            }

            // Reconstitute the state from our delta
            _networkStateManager.VerboseLog($"Applying delta against frame {serverTick - sendStateDeltaEveryNFrames}");
            StateFrameDTO serverGameState = serverGameStateDelta.ApplyTo(_stateBuffer[serverTick - sendStateDeltaEveryNFrames]);
            serverGameState.authoritative = true;

            SyncToServerState(serverGameState, newGameEventsBuffer, serverTick, estimatedLag);
        }

        /// <summary>
        /// Removes game events at a specified tick based on a predicate condition.
        /// </summary>
        /// <param name="eventTick">The tick at which to remove events.</param>
        /// <param name="gameEventPredicate">A predicate to determine which events to remove.</param>
        // TODO: since this is only ever happening during a rollback of some sort, do we even need to resend the new events state to the clients?
        internal void RemoveEventAtTick(int eventTick, Predicate<IGameEvent> gameEventPredicate) => GameEventsBuffer[eventTick].RemoveWhere(gameEventPredicate);

        /// <summary>
        /// Initiates a replay process due to new game events
        /// </summary>
        /// <param name="serverTimeTick">The server tick that triggered the replay.</param>
        /// <param name="newGameEventsBuffer">The new buffer containing game events.</param>
        /// <param name="estimatedLag">Estimated network lag in ticks.</param>
        internal void ReplayDueToEvents(int serverTimeTick, GameEventsBuffer newGameEventsBuffer, int estimatedLag)
        {
            _networkStateManager.VerboseLog("Updating upcoming game events, taking effect on tick " + serverTimeTick);

            // Probably rewinding time.
            // In either event, the new events buffer will be in place after this first call.
            TimeTravelToEndOf(serverTimeTick - 1, newGameEventsBuffer);

            // Now, get caught up to where the server is
            TimeTravelToEndOf(serverTimeTick + estimatedLag, newGameEventsBuffer);
        }

        /// <summary>
        /// Replays game state due to new player inputs
        /// </summary>
        /// <param name="playerInputs">DTO containing player inputs that triggered the replay.</param>
        /// <param name="clientTimeTick">The client-reported tick at which the inputs were recorded.</param>
        /// <param name="serverTick">The current server tick for synchronization purposes.</param>
        /// <param name="estimatedLag">Estimated network lag in ticks.</param>
        internal void ReplayDueToInputs(PlayerInputsDTO playerInputs, int clientTimeTick, int serverTick, int estimatedLag)
        {
            _networkStateManager.VerboseLog("Replaying due to player inputs at client time " + clientTimeTick);

            // Rewind, set & predict, get caught up again
            TimeTravelToEndOf(clientTimeTick - 1, GameEventsBuffer);
            _inputsBuffer.SetPlayerInputsAtTick(playerInputs, clientTimeTick);
            TimeTravelToEndOf(serverTick + estimatedLag, GameEventsBuffer);
        }

        /// <summary>
        /// Performs the game's fixed update cycle, processing inputs and simulating the next game frame.
        /// </summary>
        internal void RunFixedUpdate()
        {
            AdvanceTime();

            Dictionary<byte, IPlayerInput> localInputs = new();
            _networkStateManager.GetInputs(ref localInputs);
            _inputsBuffer.SetLocalInputs(localInputs, RealGameTick);

            // Actually simulate the frame
            _stateBuffer[RealGameTick] = RunSingleGameFrame(RealGameTick);
        }

        /// <summary>
        /// Runs a single game frame, applying inputs and events, and capturing the resulting game state.
        /// </summary>
        /// <param name="tick">The tick at which the game frame should be simulated.  This may not be the same as realGameTick if we're currently replaying.</param>
        /// <returns>A DTO representing the state after the game frame has been run.</returns>
        internal StateFrameDTO RunSingleGameFrame(int tick)
        {
            Dictionary<byte, IPlayerInput> playerInputs = _inputsBuffer.GetInputsForTick(tick);
            HashSet<IGameEvent> events = GameEventsBuffer[tick];

            _networkStateManager.VerboseLog($"Running single frame for tick {tick}");
            GameTick = tick;

            Random.ResetRandom(tick);

            // Simulate the frame
            _networkStateManager.ApplyEvents(events);
            _networkStateManager.ApplyInputs(playerInputs);
            PhysicsManager.SyncTransforms();
            _networkStateManager.PrePhysicsFrameUpdate();
            PhysicsManager.SimulatePhysics(Time.fixedDeltaTime);
            _networkStateManager.PostPhysicsFrameUpdate();

            // Capture the state from the scene/game
            return CaptureStateFrame(tick);
        }

        /// <summary>
        /// Schedules a game event to occur at a specified future tick.
        /// </summary>
        /// <param name="gameEvent">The game event to schedule.</param>
        /// <param name="eventTick">The tick at which the event should occur. If set to -1, schedules for the next tick.</param>
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

            _networkStateManager.VerboseLog("Game event scheduled for tick " + eventTick);
            GameEventsBuffer[eventTick].Add(gameEvent);
        }

        /// <summary>
        /// Sets the initial game state from the server's initial state, and initializes the random number generator with a specified seed.
        /// </summary>
        /// <param name="initialStateFrame">The initial state frame to set.</param>
        /// <param name="randomSeedBase">The seed for the random number generator.</param>
        /// <param name="estimatedLag">Estimated network lag in ticks.</param>
        internal void SetInitialGameState(StateFrameDTO initialStateFrame, int randomSeedBase, int estimatedLag)
        {
            // Store the state (the frame will be marked as authoritative when we're in SyncToServerState)
            StateFrameDTO stateFrame = (StateFrameDTO)initialStateFrame.Clone();
            _stateBuffer[0] = stateFrame;
            Random = new(randomSeedBase);
            SyncToServerState(_stateBuffer[0], GameEventsBuffer, 0, estimatedLag);
        }

        /// <summary>
        /// Sets the base seed for the game's random number generator.
        /// </summary>
        /// <param name="randomSeedBase">The seed value to set.</param>
        internal void SetRandomBase(int randomSeedBase) => Random = new(randomSeedBase);

        /// <summary>
        /// Synchronizes the local game state to match a received server state
        /// </summary>
        /// <param name="serverState">The authoritative state to apply</param>
        /// <param name="newGameEventsBuffer">The authoritative set of game events</param>
        /// <param name="serverTick">What time is it on the server at time of sending?</param>
        /// <param name="estimatedLag">How long do we think it took to get from the server to us?</param>
        internal void SyncToServerState(StateFrameDTO serverState, IGameEventsBuffer newGameEventsBuffer, int serverTick, int estimatedLag)
        {
            // NOTE: when we get here, we'll be at the _end_ of frame realGameTick, and when we leave we'll be at the end of (serverTick + lag)

            if (serverTick < LastAuthoritativeTick)
            {
                _networkStateManager.VerboseLog($"Asked to synchronize to before the last authoritative frame, so drop it.  Server tick: {serverTick} Last authoritative tick: {LastAuthoritativeTick}");
                return;
            }

            // TODO: if the server's frame is exactly the same as our frame in that spot, we may not need to do any rollback simulation at all,
            //       and can maybe just adjust to the server lag instead (or even do nothing if it's within some tolerance)
            _networkStateManager.VerboseLog($"Resync with server.  Server sent state from the end of tick {serverState.gameTick} at server tick {serverTick}");

            TimeTravelToEndOf(serverState.gameTick - 1, newGameEventsBuffer);
            SimulateAuthoritativeFrame(serverState);
            TimeTravelToEndOf(serverTick + estimatedLag, newGameEventsBuffer);

            // Set our last authoritative tick
            LastAuthoritativeTick = serverState.gameTick;
        }

        /// <summary>
        /// Captures the current state of the game at a specific tick, including physics and game state data.
        /// </summary>
        /// <param name="tick">The game tick for which to capture the state.</param>
        /// <returns>A DTO containing the captured state.</returns>
        private StateFrameDTO CaptureStateFrame(int tick)
        {
            StateFrameDTO newFrame = new()
            {
                gameTick = tick,
                PhysicsState = new PhysicsStateDTO()
            };

            IGameState newGameState = TypeStore.Instance.CreateBlankGameState();
            _networkStateManager.GetGameState(ref newGameState);
            newFrame.GameState = newGameState ?? throw new InvalidOperationException("GetGameState failed to return a valid IGameState object");
            newFrame.PhysicsState.TakeSnapshot(PhysicsManager.GetNetworkedRigidbodies(NetworkIdManager));

            return newFrame;
        }

        /// <summary>
        /// Rewinds the game state to the end of a specified target tick. This is used to rollback to a previous state
        /// in response to synchronization issues or received game events.
        /// </summary>
        /// <param name="targetTick">The tick to which the game state should be rewound.</param>
        private void RewindTimeUntilEndOfFrame(int targetTick)
        {
            // We want to end this function as though the frame at targetTick just ran

            // Safety
            if (targetTick < 0)
            {
                targetTick = 0;
            }

            _networkStateManager.VerboseLog($"Rewinding time until {targetTick}");
            // For each frame moving backward (using gameTick as our iterator)
            IsReplaying = true;
            GameTick = RealGameTick;
            while (GameTick > targetTick)
            {
                // We can skip restoring gamestate at all whenever there are no events inside of a frame to roll back
                if (GameEventsBuffer[GameTick].Count == 0)
                {
                    GameTick--;
                    continue;
                }

                _networkStateManager.VerboseLog($"Undoing events at tick {GameTick} (setting state to the moment before the events were originally run)");

                // Apply the frame state just prior to gameTick
                int prevTick = Math.Max(0, GameTick - 1);
                StateFrameDTO previousFrameState = _stateBuffer[prevTick];
                Dictionary<byte, IPlayerInput> previousFrameInputs = _inputsBuffer.GetInputsForTick(prevTick);

                _networkStateManager.ApplyInputs(previousFrameInputs);
                PhysicsManager.ApplyPhysicsState(previousFrameState.PhysicsState, NetworkIdManager);
                _networkStateManager.ApplyState(previousFrameState.GameState);

                Random.ResetRandom(GameTick);

                // Rewind any events present in gameTick
                _networkStateManager.RollbackEvents(GameEventsBuffer[GameTick], _stateBuffer[GameTick].GameState);

                GameTick--;
            }

            // We may escape the loop without doing any events (and therefore never applying state), so apply the state for the end of targetTick
            // TODO: minor optimization: detect when this happens, and skip applying state here
            _networkStateManager.VerboseLog("Applying final state from tick " + targetTick);
            StateFrameDTO frameToRestore = _stateBuffer[targetTick];
            Dictionary<byte, IPlayerInput> inputsFromFrameToRestore = _inputsBuffer.GetInputsForTick(targetTick);

            _networkStateManager.ApplyInputs(inputsFromFrameToRestore);
            PhysicsManager.ApplyPhysicsState(frameToRestore.PhysicsState, NetworkIdManager);
            _networkStateManager.ApplyState(frameToRestore.GameState);

            RealGameTick = targetTick;
            IsReplaying = false;

            _networkStateManager.VerboseLog("Done rewinding");
        }

        /// <summary>
        /// Simulates the game state for an authoritative server frame. This includes applying the game state, physics state, and any necessary inputs and events.
        /// </summary>
        /// <param name="serverFrame">The state frame to populate with state data.</param>
        private void SimulateAuthoritativeFrame(StateFrameDTO serverFrame)
        {
            _networkStateManager.VerboseLog("SimulateAuthoritativeFrame");
            int serverTick = serverFrame.gameTick;
            GameTick = serverTick;
            Random.ResetRandom(GameTick);

            _networkStateManager.ApplyEvents(GameEventsBuffer[serverTick]);
            _networkStateManager.ApplyInputs(_inputsBuffer.GetInputsForTick(serverTick));
            PhysicsManager.ApplyPhysicsState(serverFrame.PhysicsState, NetworkIdManager);
            _networkStateManager.ApplyState(serverFrame.GameState);

            serverFrame.authoritative = true;

            _stateBuffer[serverTick] = serverFrame;

            RealGameTick = serverTick;
        }

        /// <summary>
        /// Continuously simulates game frames until reaching a specific target tick. This is used for fast-forwarding
        /// the game state to catch up to a current or future state.
        /// </summary>
        /// <param name="targetTick">The tick to simulate up to.</param>
        private void SimulateUntilEndOfFrame(int targetTick)
        {
            _networkStateManager.VerboseLog("Running frames from (end of)" + RealGameTick + " to (end of)" + targetTick);

            GameTick = RealGameTick;

            while (GameTick < targetTick)
            {
                GameTick++;

                _networkStateManager.VerboseLog("Simulating for tick " + GameTick);
                _stateBuffer[GameTick] = RunSingleGameFrame(GameTick);
            }

            RealGameTick = GameTick;
        }

        /// <summary>
        /// Manages time travel within the game state, either by rewinding to a past state or simulating forward
        /// to a future state, based on the target tick. This function ensures that the game state is consistent
        /// with either past events or anticipated future events.
        /// </summary>
        /// <param name="targetTick">The target game tick to reach.</param>
        /// <param name="newGameEventsBuffer">The updated authoritative list of future and past game events</param>
        private void TimeTravelToEndOf(int targetTick, IGameEventsBuffer newGameEventsBuffer)
        {
            _networkStateManager.VerboseLog("Time traveling from end of " + RealGameTick + " until end of " + targetTick);

            // If the target is in the past, rewind time until we get to just before serverTick (rolling back any events along the way)
            // If it's in the future, simulate until we get to just before serverTick (playing any events along the way)
            if (targetTick == RealGameTick)
            {
                _networkStateManager.VerboseLog("Already there, so do nothing");
                GameEventsBuffer = newGameEventsBuffer;
                return;
            }
            else if (targetTick < RealGameTick)
            {
                _networkStateManager.VerboseLog("Rewinding time");
                RewindTimeUntilEndOfFrame(targetTick);
                GameEventsBuffer = newGameEventsBuffer;
                return;
            }
            else
            {
                _networkStateManager.VerboseLog("Fast-forwarding time");
                GameEventsBuffer = newGameEventsBuffer;
                SimulateUntilEndOfFrame(targetTick);
                return;
            }
        }
    }
}