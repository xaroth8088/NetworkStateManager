using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace NSM
{
    public class NetworkStateManager : NetworkBehaviour
    {
        // TODO: specify whether you want a ring buffer or a complete buffer
        // TODO: flags for toggling which transform and rigidbody state needs to be sync'd, or what's safe to skip
        // TODO: a variety of bandwidth-saving options, including things like crunching down floats to bytes
        // TODO: a variety of strategies for predicting the input of other players
        // TODO: the ability to arbitrarily play back a section of history
        // TODO: the ability to spread out the catch-up frames when replaying, so that the client doesn't need to stop completely to replay to present
        // TODO: some sort of configurable amount of events into the past that we'll hang onto (for clients to be able to undo during replay) so that we're not trying to sync the entire history of all events each time

        #region NetworkStateManager configuration

        [Tooltip("1 frame = 20ms, 50 frames = 1s")]
        public int sendStateDeltaEveryNFrames = 10;
        [Tooltip("1 frame = 20ms, 50 frames = 1s")]
        public int sendFullStateEveryNFrames = 100;
        [Tooltip("1 frame = 20ms, 50 frames = 1s")]
        public int requestFullFrameIfNoUpdateInNFrames = 40;

        public bool verboseLogging = false;

        #endregion NetworkStateManager configuration

        #region Runtime state

        [Header("Runtime state: Time")]
        [SerializeField]
        private int realGameTick = 0;   // This is the internal game tick, which keeps track of "now"
        public int gameTick = 0;    // Users of the library will get the tick associated with whatever frame is currently being processed, which might include frames that are being replayed
        public bool isReplaying = false;
        public int lastAuthoritativeTick = 0;

        [Header("Runtime state: Buffers")]
        public StateBuffer stateBuffer;
        public GameEventsBuffer gameEventsBuffer;

        [SerializeField]
        private bool isRunning = false;

        public NetworkIdManager networkIdManager;

        private int randomSeedBase;

        #endregion Runtime state

        #region Lifecycle event delegates and wrappers

        /// <summary>
        /// Delegate declaration for the OnApplyEvents event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnApplyEvents"/>
        /// </summary>
        /// <param name="events">A list of game events to apply in the current frame.  Remember to cast back to the event type you started NetworkStateManager with!</param>
        public delegate void ApplyEventsDelegateHandler(HashSet<IGameEvent> events);

        /// <summary>
        /// Delegate declaration for the OnRollbackEvents event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnRollbackEvents"/>
        /// </summary>
        /// <param name="events">A list of game events to roll back.  Remember to cast back to the event type you started NetworkStateManager with!</param>
        public delegate void RollbackEventsDelegateHandler(HashSet<IGameEvent> events, IGameState stateAfterEvent);

        /// <summary>
        /// Delegate declaration for the OnApplyInputs event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnApplyInputs"/>
        /// </summary>
        /// <param name="state">An object containing all the information required to apply the player inputs in your game.  This must be the same type as what you started NetworkStateManager with.</param>
        public delegate void ApplyInputsDelegateHandler(Dictionary<byte, IPlayerInput> playerInputs);

        /// <summary>
        /// Delegate declaration for the OnApplyState event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnApplyState"/>
        /// </summary>
        /// <param name="state">An object containing all the information required to apply the state to your game.  This must be the same type as what you started NetworkStateManager with.</param>
        public delegate void ApplyStateDelegateHandler(IGameState state);

        /// <summary>
        /// Delegate declaration for the OnGetGameState event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnGetGameState"/>
        /// </summary>
        public delegate void OnGetGameStateDelegateHandler(ref IGameState state);

        /// <summary>
        /// Delegate declaration for the OnGetInputs event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnGetInputs"/>
        /// </summary>
        public delegate void OnGetInputsDelegateHandler(ref Dictionary<byte, IPlayerInput> playerInputs);

        /// <summary>
        /// Delegate declaration for the OnPostPhysicsFrameUpdate event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnPostPhysicsFrameUpdate"/>
        /// </summary>
        public delegate void OnPostPhysicsFrameUpdateDelegateHandler();

        /// <summary>
        /// Delegate declaration for the OnPrePhysicsFrameUpdate event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnPrePhysicsFrameUpdate"/>
        /// </summary>
        public delegate void OnPrePhysicsFrameUpdateDelegateHandler();

        /// <summary>
        /// This event fires when a given set of events needs to be applied to your game.
        /// Primarily, this will happen at the beginning of the server
        /// reconciliation process (or when otherwise beginning a replay).
        /// <br/>
        /// See also: <br/>
        /// <seealso cref="ApplyEventsDelegateHandler"/> and
        /// <seealso cref="StateFrameDTO"/>
        /// </summary>
        public event ApplyEventsDelegateHandler OnApplyEvents;

        /// <summary>
        /// This event fires when a given set of events need to be rolled back in your game.
        /// When the server attempts to rollback game state in order to apply new state,
        /// any events that've had side-effects on the world (e.g. starting an animation,
        /// spawning new game objects, etc.) will need to be reversed in order to ensure
        /// a consistent state once everything's said and done.
        /// <br/>
        /// NOTE: The game state will be restored to the state just before the event originally
        /// fired, NOT the game state immediately after the event fired (as would be the case
        /// for a strict rewinding of time).  This way, your rollback handler has access to the
        /// state that originally triggered the event.
        /// See also: <br/>
        /// <seealso cref="RollbackEventsDelegateHandler"/> and
        /// <seealso cref="StateFrameDTO"/>
        /// </summary>
        public event RollbackEventsDelegateHandler OnRollbackEvents;

        /// <summary>
        /// This event fires when a given input needs to be applied to your game.
        /// Primarily, this will happen at the beginning of the server
        /// reconciliation process (or when otherwise beginning a replay).
        /// <br/>
        /// See also: <br/>
        /// <seealso cref="ApplyInputsDelegateHandler"/> and
        /// <seealso cref="StateFrameDTO"/>
        /// </summary>
        public event ApplyInputsDelegateHandler OnApplyInputs;

        /// <summary>
        /// This event fires when a given state needs to be applied to your game.
        /// Primarily, this will happen at the beginning of the server
        /// reconciliation process (or when otherwise beginning a replay).
        /// 
        /// Because NSM doesn't fully manage instantiating GameObjects (yet?), your game
        /// will need to be able to detect when a GameObject is missing from the scene but
        /// present in the game state, or if any other meaningful discrepancy exists, and
        /// then correct it.
        /// <br/>
        /// See also: <br/>
        /// <seealso cref="ApplyStateDelegateHandler"/> and
        /// <seealso cref="StateFrameDTO"/>
        /// </summary>
        public event ApplyStateDelegateHandler OnApplyState;

        /// <summary>
        /// This event fires at the end of each frame, and is required to return
        /// a fully-populated GameStateObject with the game's state as of that
        /// frame.
        /// <br/>
        /// See also: <br/>
        /// <seealso cref="OnGetGameStateDelegateHandler"/>
        /// </summary>
        public event OnGetGameStateDelegateHandler OnGetGameState;

        /// <summary>
        /// This event fires at the start of each frame in FixedUpdate, and is required to
        /// return a dictionary that's populated with { playerId, IPlayerInput } for
        /// all inputs that this instance is responsible for.
        ///
        /// IMPORTANT: Input should be gathered by your game during Update, and coalesced
        /// until your callback is called.  It is up to your game to decide what to do
        /// with frames that don't have exactly 1 Update in-between the FixedUpdates.
        ///
        /// If a player has no inputs for a given call, simply exclude them from the
        /// Dictionary.
        /// <br/>
        /// See also: <br/>
        /// <seealso cref="OnGetInputsDelegateHandler"/>
        /// </summary>
        public event OnGetInputsDelegateHandler OnGetInputs;

        /// <summary>
        /// This event fires each frame, after the physics engine is run for this
        /// frame.  This has no direct analog to a Unity lifecycle event, though
        /// the closest would be Update()
        /// <br/>
        /// See also: <br/>
        /// <seealso cref="OnPostPhysicsFrameUpdateDelegateHandler"/>
        /// </summary>
        public event OnPostPhysicsFrameUpdateDelegateHandler OnPostPhysicsFrameUpdate;

        /// <summary>
        /// This event fires each frame, before the physics engine is run for this
        /// frame.  This is the equivalent of FixedUpdate().
        /// <br/>
        /// See also: <br/>
        /// <seealso cref="OnPrePhysicsFrameUpdateDelegateHandler"/>
        /// </summary>
        public event OnPrePhysicsFrameUpdateDelegateHandler OnPrePhysicsFrameUpdate;

        private void ApplyEvents(HashSet<IGameEvent> events)
        {
            int count = events.Count;

            if (count == 0)
            {
                return;
            }

            VerboseLog("Applying " + count + " events");

            OnApplyEvents?.Invoke(events);
        }

        private void RollbackEvents(HashSet<IGameEvent> events, IGameState stateAfterEvent)
        {
            int count = events.Count;

            if (count == 0)
            {
                return;
            }

            VerboseLog("Rolling back " + events.Count + " events");
            OnRollbackEvents?.Invoke(events, stateAfterEvent);
        }

        private void ApplyInputs(Dictionary<byte, IPlayerInput> playerInputs)
        {
            int count = playerInputs.Count;

            if (count == 0)
            {
                return;
            }

            VerboseLog("Applying " + count + " player inputs");

            OnApplyInputs?.Invoke(playerInputs);
        }

        private void ApplyState(IGameState gameState)
        {
            if (gameState == null)
            {
                return;
            }

            VerboseLog("Applying game state");
            OnApplyState?.Invoke(gameState);
        }

        private void GetGameState(ref IGameState gameState)
        {
            VerboseLog("Capturing game state");
            OnGetGameState?.Invoke(ref gameState);
        }

        private void GetInputs(ref Dictionary<byte, IPlayerInput> inputs)
        {
            VerboseLog("Capturing player inputs");
            OnGetInputs?.Invoke(ref inputs);
        }

        private void PostPhysicsFrameUpdate()
        {
            VerboseLog("Running post-physics frame update");
            OnPostPhysicsFrameUpdate?.Invoke();
        }

        private void PrePhysicsFrameUpdate()
        {
            VerboseLog("Running pre-physics frame update");
            OnPrePhysicsFrameUpdate?.Invoke();
        }

        #endregion Lifecycle event delegates and wrappers

        #region Public Interface

        /// <summary>
        /// Schedules a game event for some time in the future.  Note that events scheduled by a client will be silently ignored because
        /// the server is the source of truth for which game events can happen and when.
        /// </summary>
        /// <param name="gameEvent">The event you'd like clients to act on.</param>
        /// <param name="eventTick">The game tick when the event should fire.  Leave empty to fire on the next game tick.</param>
        public void ScheduleGameEvent(IGameEvent gameEvent, int eventTick = -1)
        {
            if(!IsHost)
            {
                // Events need to be server-authoritative in all cases, to prevent problems with the client erroneously scheduling them
                // based on incorrect predictions
                return;
            }

            if (eventTick == -1)
            {
                eventTick = gameTick + 1;
            }

            if (eventTick <= gameTick)
            {
                Debug.LogWarning("Game event scheduled for the past - will not be replayed on clients");
            }

            VerboseLog("Game event scheduled for tick " + eventTick);
            gameEventsBuffer[eventTick].Add(gameEvent);

            if(!IsHost)
            {
                return;
            }

            // Let everyone know that an event is happening
            // TODO: see if there's some way to send this to all non-Host clients (instead of _all_ clients), to avoid some server overhead
            SyncGameEventsToClientsClientRpc(gameTick, gameEventsBuffer);
        }

        /// <summary>
        /// Given a tick and a predicate that can find which event you want to remove, de-schedule that event at that tick.
        /// </summary>
        /// <param name="willSpawnAtTick">The game tick the event was previously scheduled to fire on.</param>
        /// <param name="gameEventPredicate">If this function returns true for a given event, that event will be de-scheduled.</param>
        public void RemoveEventAtTick(int willSpawnAtTick, Predicate<IGameEvent> gameEventPredicate)
        {
            // TODO: since this is only ever happening during a rollback of some sort, do we even need to resend the new events state to the clients?
            gameEventsBuffer[willSpawnAtTick].RemoveWhere(gameEventPredicate);
        }

        #endregion Public Interface

        #region Initialization code

        public void StartNetworkStateManager(Type gameStateType, Type playerInputType, Type gameEventType)
        {
            VerboseLog("Network State Manager starting up");

            // TODO: If NetworkManager isn't ready yet, we should throw an error and refuse to start up.

            networkIdManager = new(this);

            TypeStore.Instance.GameStateType = gameStateType;
            TypeStore.Instance.PlayerInputType = playerInputType;
            TypeStore.Instance.GameEventType = gameEventType;

            VerboseLog("Setting up network ids for scene objects that need them");
            networkIdManager.SetupInitialNetworkIds(gameObject.scene);

            stateBuffer = new();
            gameEventsBuffer = new();

            isRunning = false;
            isReplaying = false;
            lastAuthoritativeTick = 0;
            gameTick = 0;
            realGameTick = 0;

            if (!IsHost)
            {
                return;
            }

            // Server-only from here down
            isRunning = true;

            // If we don't explicitly seed UnityEngine.Random, then it won't have a reproducible state until after
            // the first random number is requested of it.
            Random.InitState(new System.Random().Next(int.MinValue, int.MaxValue));

            // Capture the initial game state
            stateBuffer[0] = CaptureStateFrame(0);

            randomSeedBase = Random.Range(int.MinValue, int.MaxValue);

            // Ensure clients are starting from the same view of the world
            // TODO: see if there's some way to send this to all non-Host clients (instead of _all_ clients), to avoid some server overhead
            VerboseLog("Sending initial state");
            StartGameClientRpc(stateBuffer[0], randomSeedBase);
        }

        private void Awake()
        {
            isRunning = false;

            // In order for NSM to work, we'll need to fully control physics (Muahahaha)
            Physics.simulationMode = SimulationMode.Script;
            Physics.autoSyncTransforms = false;
        }

        #endregion Initialization code

        #region Server-side only code

        private void HostFixedUpdate()
        {
            // Since it's impossible for us to have the inputs for other clients at this point,
            // we'll need to start by predicting them forward and then overwrite with
            // any that are server-authoritative (i.e. that come from a Host)
            Dictionary<byte, IPlayerInput> playerInputs = PredictInputs(stateBuffer[realGameTick - 1].PlayerInputs);
            Dictionary<byte, IPlayerInput> localInputs = new();
            GetInputs(ref localInputs);
            foreach (KeyValuePair<byte, IPlayerInput> entry in localInputs)
            {
                playerInputs[entry.Key] = entry.Value;

                Type playerInputType = TypeStore.Instance.PlayerInputType;
                IPlayerInput defaultInput = (IPlayerInput)Activator.CreateInstance(playerInputType);

                // Send to clients, if they wouldn't have predicted this value
                IPlayerInput predictedInput = PredictInput(stateBuffer[realGameTick - 1].PlayerInputs.GetValueOrDefault(entry.Key, defaultInput));

                if (!predictedInput.Equals(entry.Value))
                {
                    PlayerInputDTO playerInputDTO = new()
                    {
                        input = entry.Value
                    };
                    // TODO: see if there's some way to send this to all non-Host clients (instead of _all_ clients), to avoid some server overhead
                    ForwardPlayerInputClientRpc(entry.Key, playerInputDTO, realGameTick, realGameTick);
                }
            }

            // Actually simulate the frame
            stateBuffer[realGameTick] = RunSingleGameFrame(realGameTick, playerInputs, gameEventsBuffer[realGameTick]);

            // (Maybe) send the new state to the clients for reconciliation
            if (realGameTick % sendFullStateEveryNFrames == 0)
            {
                VerboseLog("Sending full state to clients");

                // To avoid problems later with applying diffs, go back to the last time we would've sent out a
                // frame delta normally.
                int requestedGameTick = realGameTick - (realGameTick % sendStateDeltaEveryNFrames);

                ProcessFullStateUpdateClientRpc(stateBuffer[requestedGameTick], gameEventsBuffer, realGameTick);
            }
            else if (realGameTick % sendStateDeltaEveryNFrames == 0)
            {
                VerboseLog("Sending delta - base frame comes from tick " + (realGameTick - sendStateDeltaEveryNFrames));

                // TODO: there's an opportunity to be slightly more aggressive by skipping sending anything if the entire
                //       state frame is exactly the same (except for the realGameTick, of course).
                StateFrameDeltaDTO delta = stateBuffer[realGameTick - sendStateDeltaEveryNFrames].GenerateDelta(stateBuffer[realGameTick]);
                VerboseLog("Sending delta with diff size of " + delta._gameStateDiffBytes?.Length);

                // TODO: see if there's some way to send this to all non-Host clients (instead of _all_ clients), to avoid some server overhead
                ProcessStateDeltaUpdateClientRpc(delta, gameEventsBuffer);
            }
        }

        // TODO: prevent a client from sending input for a player they shouldn't be sending input for
        // NOTE: Rpc's are processed at the _end_ of each frame
        [ServerRpc(RequireOwnership = false)]
        private void SetInputServerRpc(byte playerId, PlayerInputDTO value, int clientTimeTick)
        {
            if (!isRunning)
            {
                // RPC's can arrive before this component has started, so skip out if it's too early
                VerboseLog("Input received for player " + playerId + ", but we haven't started yet so ignoring.");
                return;
            }

            VerboseLog("Input received for player " + playerId + " at " + clientTimeTick);

            if (clientTimeTick > realGameTick)
            {
                // The server slowed down enough for the clients to get ahead of it.  For small deltas,
                // this isn't usually an issue.
                // TODO: figure out a strategy for inputs that have far-future inputs
                // TODO: figure out a strategy for detecting cheating that's happening (vs. normal slowdowns)
                // For now, just apply to the current timestamp.
                // TODO: NOTE: this could cause issues for client-side prediction, esp. if clients are
                //             filtering out their own inputs.
                Debug.LogWarning("Client input is coming from server's future.  Server time: " + realGameTick + " Client time: " + clientTimeTick);
                clientTimeTick = realGameTick;
            }

            // Set the input in our buffer
            SetPlayerInputAtTickAndPredictForward(playerId, value.input, clientTimeTick);

            // Forward the input to all clients so they can do the same
            // TODO: see if there's some way to send this to all non-Host clients (instead of _all_ clients), to avoid some server overhead
            ForwardPlayerInputClientRpc(playerId, value, clientTimeTick, realGameTick);
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [ServerRpc(RequireOwnership = false)]
        private void RequestFullStateUpdateServerRpc(ServerRpcParams serverRpcParams = default)
        {
            VerboseLog("Received request for full state update");

            // Send this back to only the client that requested it
            ClientRpcParams clientRpcParams = new()
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { serverRpcParams.Receive.SenderClientId }
                }
            };

            // To avoid problems later with applying diffs, go back to the last time we would've sent out a
            // frame delta normally.
            int requestedGameTick = realGameTick - (realGameTick % sendStateDeltaEveryNFrames);
            VerboseLog("Full frame requested for " + requestedGameTick);

            ProcessFullStateUpdateClientRpc(stateBuffer[requestedGameTick], gameEventsBuffer, realGameTick, clientRpcParams);
        }

        #endregion Server-side only code

        #region Client-side only code

        private void ClientFixedUpdate()
        {
            if (realGameTick > lastAuthoritativeTick + requestFullFrameIfNoUpdateInNFrames)
            {
                Debug.LogWarning("Haven't heard from the server since " + lastAuthoritativeTick);
                // TODO: figure out what we want to do here, since we don't want to DoS the server just because we haven't heard from it in a while
            }

            // Make a guess about what all players' inputs will be for the next frame
            Dictionary<byte, IPlayerInput> playerInputs = PredictInputs(stateBuffer[realGameTick - 1].PlayerInputs);

            // Gather the local inputs to override the prediction,
            // but track them separately since we'll need to forward to the server via RPC
            Dictionary<byte, IPlayerInput> localInputs = new();
            GetInputs(ref localInputs);
            foreach (KeyValuePair<byte, IPlayerInput> entry in localInputs)
            {
                playerInputs[entry.Key] = entry.Value;
            }

            // Actually simulate the frame (this is the client-side "prediction" of what'll happen)
            stateBuffer[realGameTick] = RunSingleGameFrame(realGameTick, playerInputs, gameEventsBuffer[realGameTick]);

            // Send our local inputs to the server, if needed
            // TODO: consolidate these into a single RPC call, instead of sending one per player
            // TODO: this includes a duplicative input prediction that could be avoided
            foreach (KeyValuePair<byte, IPlayerInput> entry in localInputs)
            {
                Type playerInputType = TypeStore.Instance.PlayerInputType;
                IPlayerInput defaultInput = (IPlayerInput)Activator.CreateInstance(playerInputType);

                IPlayerInput predictedInput = PredictInput(stateBuffer[realGameTick - 1].PlayerInputs.GetValueOrDefault(entry.Key, defaultInput));

                // If it's the same as the predicted tick, then don't bother sending
                if (predictedInput.Equals(entry.Value))
                {
                    continue;
                }

                PlayerInputDTO playerInputDTO = new()
                {
                    input = entry.Value
                };

                SetInputServerRpc(entry.Key, playerInputDTO, realGameTick);
            }
        }

        private void TimeTravelToEndOf(int targetTick, GameEventsBuffer newGameEventsBuffer)
        {
            VerboseLog("Time traveling from " + realGameTick + " to " + targetTick);

            // If the target is in the past, rewind time until we get to just before serverTick (rolling back any events along the way)
            // If it's in the future, simulate until we get to just before serverTick (playing any events along the way)
            if (targetTick == realGameTick)
            {
                VerboseLog("Already there, so do nothing");
                // No need to do anything, we're already here!
                gameEventsBuffer = newGameEventsBuffer;
                return;
            }
            else if (targetTick < realGameTick)
            {
                RewindTimeUntilEndOfFrame(targetTick);
                gameEventsBuffer = newGameEventsBuffer;
                return;
            }
            else
            {
                gameEventsBuffer = newGameEventsBuffer;
                SimulateUntilEndOfFrame(targetTick);
                return;
            }
        }

        private void SyncToServerState(StateFrameDTO serverState, GameEventsBuffer newGameEventsBuffer, int serverTick)
        {
            // NOTE: when we get here, we'll be at the _end_ of frame realGameTick

            VerboseLog("Resync with server.  Server sent state from the end of tick " + serverState.gameTick + " at server tick " + serverTick);

            int serverStateTick = serverState.gameTick;
            TimeTravelToEndOf(serverStateTick - 1, newGameEventsBuffer);

            VerboseLog("realGameTick is now1:" + realGameTick);

            // Using serverState, reproduce what we'd do if we were running the frame normally, but use the server authoritative frame to do this
            SimulateAuthoritativeFrame(serverState);

            VerboseLog("realGameTick is now2:" + realGameTick);

            CatchUpToServer(serverTick);
            VerboseLog("realGameTick is now3:" + realGameTick);

            // Set our last authoritative tick
            lastAuthoritativeTick = serverStateTick;
        }

        private void CatchUpToServer(int serverTick)
        {
            VerboseLog("CatchUpToServer at server tick:" + serverTick);
            // Simulate until we've just finished running frame (serverTick + estimated lag), which will align us to the server again.
            //  Because lag is never negative, we don't have to worry about going backwards again here.
            int targetTick = serverTick + GetEstimatedLag();
            SimulateUntilEndOfFrame(targetTick);
        }

        private void SimulateAuthoritativeFrame(StateFrameDTO serverFrame)
        {
            VerboseLog("SimulateAuthoritativeFrame");
            int serverTick = serverFrame.gameTick;
            gameTick = serverTick;

            ApplyEvents(gameEventsBuffer[serverTick]);
            ApplyInputs(serverFrame.PlayerInputs);
            ApplyState(serverFrame.GameState);

            serverFrame.authoritative = true;

            stateBuffer[serverTick] = serverFrame;

            realGameTick = serverTick;
        }

        private bool ShouldClientRunRpcs()
        {
            if (!isRunning)
            {
                // RPC's can arrive before this component has started, so skip out if it's too early
                // TODO: is this the right thing to do?  Seems like maybe no?
                VerboseLog("Server RPC arrived before we've started, so skipping");
                return false;
            }

            if (IsHost)
            {
                // If we're the host, we explicitly do NOT want to do client-side stuff
                return false;
            }

            return true;
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [ClientRpc]
        private void ForwardPlayerInputClientRpc(byte playerId, PlayerInputDTO value, int clientTimeTick, int serverTick)
        {
            if (!ShouldClientRunRpcs())
            {
                return;
            }

            // TODO: optimization: it seems like there should be some way to say "this is just the reflection
            //       of our own input, and we're authoritative for our own input, so we can ignore this
            //       and/or don't even send it to us in the first place."

            // If this happened before our last authoritative tick, we can safely ignore it
            if (clientTimeTick < lastAuthoritativeTick || serverTick < lastAuthoritativeTick)
            {
                VerboseLog("Client input for player id " + playerId + " arrived from before our last authoritative tick, so ignoring");
                return;
            }

            VerboseLog("Replaying due to input for player id " + playerId + " at client time " + clientTimeTick);

            // Set the input in our buffer
            RewindTimeUntilEndOfFrame(clientTimeTick - 1);
            SetPlayerInputAtTickAndPredictForward(playerId, value.input, clientTimeTick);
            CatchUpToServer(serverTick);
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [ClientRpc]
        private void SyncGameEventsToClientsClientRpc(int serverTimeTick, GameEventsBuffer newGameEventsBuffer)
        {
            if (!ShouldClientRunRpcs())
            {
                return;
            }

            if( serverTimeTick < lastAuthoritativeTick )
            {
                // We'll already have the most up-to-date events reflected from whatever sent us the last authoritative
                // data, and we don't want to worry about accidentally mangling the server state during replay.
                return;
            }

            VerboseLog("Updating upcoming game events, taking effect on tick " + serverTimeTick);

            TimeTravelToEndOf(serverTimeTick - 1, newGameEventsBuffer);
            CatchUpToServer(serverTimeTick);
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [ClientRpc]
        private void StartGameClientRpc(StateFrameDTO initialStateFrame, int _randomSeedBase)
        {
            // TODO: figure out what to do if the initial game state never arrives
            if (IsHost)
            {
                return;
            }

            VerboseLog("Initial game state received from server.");

            // Store the state
            stateBuffer[0] = initialStateFrame.Duplicate();
            randomSeedBase = _randomSeedBase;

            // Start things off!
            isRunning = true;
            SyncToServerState(stateBuffer[0], gameEventsBuffer, 0);
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [ClientRpc]
        private void ProcessStateDeltaUpdateClientRpc(StateFrameDeltaDTO serverGameStateDelta, GameEventsBuffer newGameEventsBuffer)
        {
            if (!ShouldClientRunRpcs())
            {
                return;
            }

            VerboseLog("Server state delta received.  Server time:" + serverGameStateDelta.gameTick);

            if (serverGameStateDelta.gameTick < lastAuthoritativeTick)
            {
                VerboseLog("Server state delta was from before last authoritative, so drop it.  Last authoritative tick: " + lastAuthoritativeTick);
                return;
            }

            // Did the state arrive out of order?  If so, panic.
            if (serverGameStateDelta.gameTick != (lastAuthoritativeTick + sendStateDeltaEveryNFrames))
            {
                // TODO: maybe this should be an exception thrown from trying to apply the deltas (do the check there), and then do the full
                //       state request here if caught?
                VerboseLog(
                    "Server snapshot arrived out of order!  Requesting full state refresh.  Server state tick: " + serverGameStateDelta.gameTick +
                    " expected: " + lastAuthoritativeTick + " + " + sendStateDeltaEveryNFrames + " = " + (lastAuthoritativeTick + sendStateDeltaEveryNFrames)
                );

                // We can't just process this as-is since we need that prior frame in order to properly apply the delta.
                // As such, request a full state sync instead of the delta
                RequestFullStateUpdateServerRpc();
                return;
            }

            // Reconstitute the state from our delta
            StateFrameDTO serverGameState;
            try
            {
                VerboseLog("Applying delta against frame " + (serverGameStateDelta.gameTick - sendStateDeltaEveryNFrames));
                serverGameState = stateBuffer[serverGameStateDelta.gameTick - sendStateDeltaEveryNFrames].Duplicate();
                serverGameState.ApplyDelta(serverGameStateDelta);
            } catch (Exception e)
            {
                VerboseLog("Something went wrong when reconstituting game state from diff: " + e.Message);
                RequestFullStateUpdateServerRpc();
                return;
            }

            SyncToServerState(serverGameState, newGameEventsBuffer, serverGameStateDelta.gameTick);
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        /// <summary>
        /// 
        /// </summary>
        /// <param name="serverGameState"></param>
        /// <param name="serverGameEventsBuffer"></param>
        /// <param name="serverTick">This is needed because the server will only ever send full frames that are aligned to the delta tick frequency</param>
        /// <param name="clientRpcParams"></param>
        [ClientRpc]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Needed for server to send to specific clients")]
        private void ProcessFullStateUpdateClientRpc(StateFrameDTO serverGameState, GameEventsBuffer serverGameEventsBuffer, int serverTick, ClientRpcParams clientRpcParams = default)
        {
            if (!ShouldClientRunRpcs())
            {
                return;
            }

            VerboseLog("Received full state update from server");
            if(serverGameState.gameTick < lastAuthoritativeTick)
            {
                VerboseLog("Received full frame update from before the last authoritative, so drop it.  Server tick: " + serverTick + " Last authoritative tick: " + lastAuthoritativeTick);
                return;
            }

            // Get us back in sync
            SyncToServerState(serverGameState, serverGameEventsBuffer, serverTick);
        }

        #endregion Client-side only code

        #region Core simulation functionality

        private void ApplyPhysicsState(PhysicsStateDTO physicsState)
        {
            VerboseLog("Applying physics state");

            if (physicsState == null)
            {
                // No rigidbodies in the state yet
                return;
            }

            // Set each object into the world
            foreach ((byte networkId, RigidBodyStateDTO rigidBodyState) in physicsState.RigidBodyStates)
            {
                GameObject networkedGameObject = networkIdManager.GetGameObjectByNetworkId(networkId);
                if (networkedGameObject == null || networkedGameObject.activeInHierarchy == false)
                {
                    // This object no longer exists in the scene
                    Debug.LogError("Attempted to restore state to a GameObject that no longer exists");
                    // TODO: this seems like it'll lead to some bugs later with objects that disappeared recently
                    continue;
                }

                rigidBodyState.ApplyState(networkedGameObject.GetComponentInChildren<Rigidbody>());
            }
        }

        private IPlayerInput PredictInput(IPlayerInput lastKnownPlayerInput)
        {
            // For now, do a simple "the next frame will always be the same as the previous frame".
            // TODO: an event callback to predict the next input
            return lastKnownPlayerInput;
        }

        private Dictionary<byte, IPlayerInput> PredictInputs(Dictionary<byte, IPlayerInput> lastKnownPlayerInputs)
        {
            VerboseLog("Predicting all players' inputs");

            Dictionary<byte, IPlayerInput> newInputs = new();

            foreach (KeyValuePair<byte, IPlayerInput> entry in lastKnownPlayerInputs)
            {
                newInputs[entry.Key] = PredictInput(entry.Value);
            }

            return newInputs;
        }

        private void SimulateUntilEndOfFrame(int targetTick)
        {
            VerboseLog("Running frames from (end of)" + realGameTick + " to (end of)" + targetTick);

            gameTick = realGameTick;

            while (gameTick < targetTick)
            {
                gameTick++;

                VerboseLog("Simulating for tick " + gameTick);

                stateBuffer[gameTick] = RunSingleGameFrame(gameTick, stateBuffer[gameTick].PlayerInputs, gameEventsBuffer[gameTick]);
            }

            realGameTick = gameTick;
        }

        // TODO: I think player inputs need a way to distinguish between "this frame's remote inputs came from the server, and therefore don't need to be predicted"
        //       and "this frame's remote inputs were predicted before, and need to be re-predicted now".  Because we may have server-authoritative inputs
        //       for some of the frames that are being replayed when we're replaying them.

        private void RewindTimeUntilEndOfFrame(int targetTick)
        {
            // We want to end this function as though the frame at targetTick just ran

            // Safety
            if (targetTick < 0)
            {
                targetTick = 0;
            }

            VerboseLog("Rewinding time until " + targetTick);
            // For each frame moving backward (using gameTick as our iterator)
            isReplaying = true;
            gameTick = realGameTick;
            while( gameTick > targetTick )
            {
                // We can skip restoring gamestate at all whenever there are no events inside of a frame to roll back
                if(gameEventsBuffer[gameTick].Count == 0)
                {
                    gameTick--;
                    continue;
                }

                VerboseLog("Undoing events at tick " + gameTick + " (setting state to the moment before the events were originally run)");

                // Apply the frame state just prior to gameTick
                StateFrameDTO previousFrameState = stateBuffer[Math.Max(0, gameTick - 1)];

                ApplyInputs(previousFrameState.PlayerInputs);
                ApplyPhysicsState(previousFrameState.PhysicsState);
                ApplyState(previousFrameState.GameState);

                // Apply the random state for gameTick
                ResetRandom(gameTick);

                // Rewind any events present in gameTick
                RollbackEvents(gameEventsBuffer[gameTick], stateBuffer[gameTick].GameState);

                gameTick--;
            }

            // We may escape the loop without doing any events (and therefore never applying state), so apply the state for the end of targetTick
            VerboseLog("Applying final state from tick " + targetTick);
            StateFrameDTO frameToRestore = stateBuffer[targetTick];

            ResetRandom(targetTick);
            ApplyInputs(frameToRestore.PlayerInputs);
            ApplyPhysicsState(frameToRestore.PhysicsState);
            ApplyState(frameToRestore.GameState);

            realGameTick = targetTick;
            isReplaying = false;

            VerboseLog("Done rewinding");
        }

        private void ResetRandom(int tick)
        {
            Random.InitState(randomSeedBase + tick);
        }

        private StateFrameDTO RunSingleGameFrame(int tick, Dictionary<byte, IPlayerInput> playerInputs, HashSet<IGameEvent> events)
        {
            VerboseLog("Running single frame for tick " + tick);
            gameTick = tick;

            ResetRandom(tick);

            // Simulate the frame
            ApplyEvents(events);
            ApplyInputs(playerInputs);
            Physics.SyncTransforms();
            PrePhysicsFrameUpdate();
            SimulatePhysics();
            PostPhysicsFrameUpdate();

            // Capture the state from the scene/game
            StateFrameDTO newFrame = CaptureStateFrame(tick);
            newFrame.PlayerInputs = playerInputs;

            return newFrame;
        }

        private StateFrameDTO CaptureStateFrame(int tick)
        {
            StateFrameDTO newFrame = new()
            {
                gameTick = tick,
                PhysicsState = new PhysicsStateDTO()
            };

            IGameState newGameState = TypeStore.Instance.CreateBlankGameState();
            GetGameState(ref newGameState);
            newFrame.GameState = newGameState ?? throw new Exception("GetGameState failed to return a valid IGameState object");

            // Did something fail during serialization?
            if( newFrame.GameState == null)
            {
                throw new Exception("Gamestate serialization to bytes failed");
            }

            newFrame.PhysicsState.TakeSnapshot(GetNetworkedRigidbodies());

            return newFrame;
        }

        private void SetPlayerInputAtTickAndPredictForward(byte playerId, IPlayerInput value, int tick)
        {
            VerboseLog("Predicting player id " + playerId + "'s input from now through " + tick);

            // Set at tick
            stateBuffer[tick].PlayerInputs[playerId] = value;

            // ...and predict forward
            for (int i = tick + 1; i <= realGameTick; i++)
            {
                stateBuffer[i].PlayerInputs[playerId] = PredictInput(stateBuffer[i - 1].PlayerInputs[playerId]);
            }
        }

        private void SimulatePhysics()
        {
            VerboseLog("Simulating physics for frame");
            Physics.Simulate(Time.fixedDeltaTime);
        }

        private List<Rigidbody> GetNetworkedRigidbodies()
        {
            List<Rigidbody> rigidbodies = new();
            foreach (GameObject gameObject in networkIdManager.GetAllNetworkIdGameObjects())
            {
                if (!gameObject.TryGetComponent(out Rigidbody rigidbody))
                {
                    continue;
                }

                rigidbodies.Add(rigidbody);
            }

            return rigidbodies;
        }

        private int GetEstimatedLag()
        {
            if(IsHost)
            {
                // Host has the authoritative time, but because of when in the flow this is calculated we have to pretend we're 1 frame "behind" now.
                return 1;
            }

            int framesOfLag = NetworkManager.LocalTime.Tick - NetworkManager.ServerTime.Tick;
            if(framesOfLag < 0)
            {
                throw new Exception("Client is somehow ahead of server, which Unity's library shouldn't ever permit to happen.");
            }

            VerboseLog("Client is about " + framesOfLag + " frames ahead of the server");

            return framesOfLag;
        }

        #endregion Core simulation functionality

        private void FixedUpdate()
        {
            if (!isRunning)
            {
                return;
            }

            // Start a new frame
            realGameTick++;
            gameTick = realGameTick;
            VerboseLog("---- NEW FRAME ----");

            ResetRandom(realGameTick);

            if (IsHost)
            {
                HostFixedUpdate();
            }
            else if (IsClient)
            {
                ClientFixedUpdate();
            }
            VerboseLog("---- END FRAME ----");
        }

        public void VerboseLog(string message)
        {
#if UNITY_EDITOR
            // TODO: abstract this into its own thing, use everywhere
            if (!verboseLogging)
            {
                return;
            }

            StackTrace stackTrace = new();
            StackFrame[] stackFrames = stackTrace.GetFrames();
            List<string> methodNames = new();
            foreach (StackFrame stackFrame in stackFrames)
            {
                methodNames.Add(stackFrame.GetMethod().Name);
            }

            string log = "";


            if (realGameTick != gameTick)
            {
                log += "** ";
            }

            log += realGameTick + "";
            if(realGameTick != gameTick)
            {
                log += " (" + gameTick + ")";
            }
            log += ": ";
            if (isReplaying)
            {
                log += "**REPLAY** ";
            }

            log += string.Join(" < ", methodNames.GetRange(1, Math.Min(methodNames.Count - 1, 3))) + ": ";

            log += message;

            Debug.Log(log);
#endif
        }
    }
}