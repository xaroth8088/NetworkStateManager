using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NSM
{
    public class NetworkStateManager : NetworkBehaviour
    {
        // TODO: specify whether you want a ring buffer or a complete buffer
        // TODO: flags for toggling which transform and rigidbody state needs to be sync'd, or what's safe to skip
        // TODO: a variety of bandwidth-saving options, including things like crunching down doubles to floats, or floats to bytes, etc.
        // TODO: the ability to arbitrarily play back a section of history
        // TODO: the ability to spread out the catch-up frames when replaying, so that the client doesn't need to stop completely to
        //       replay to present
        // TODO: some sort of configurable amount of events into the past that we'll hang onto (for clients to be able to undo during
        //       replay) so that we're not trying to sync the entire history of all events each time
        // TODO: the server can maybe calculate a diff for the events buffer whenever something happens to make it change, and only send
        //       over those (instead of the whole history each time).  This would need to be aware of state replays, though, so that it
        //       doesn't send a bunch of "add this event, no just kidding delete it, ok no really add it." sort of noise.
        // TODO: some sort of configurable amount of player inputs into the past that we'll hang onto, to conserve runtime memory

        #region NetworkStateManager configuration

        [Tooltip("1 frame = 20ms, 50 frames = 1s")]
        public int sendStateDeltaEveryNFrames = 10;
        [Tooltip("1 frame = 20ms, 50 frames = 1s")]
        public int sendFullStateEveryNFrames = 100;
        [Tooltip("1 frame = 20ms, 50 frames = 1s")]
        public int maxFramesWithoutHearingFromServer = 40;

        public bool verboseLogging = false;

        #endregion NetworkStateManager configuration

        #region Runtime state

        [Header("Runtime state: Time")]
        [SerializeField]
        private int realGameTick { get => gameStateManager.RealGameTick; }   // This is the internal game tick, which keeps track of "now"
        public int GameTick { get => gameStateManager.GameTick; }    // Users of the library will get the tick associated with whatever frame is currently being processed, which might include frames that are being replayed
        public bool isReplaying { get => gameStateManager.isReplaying; }

        [SerializeField]
        private bool isRunning = false;

        public NetworkIdManager NetworkIdManager { get => gameStateManager.NetworkIdManager; }

        [SerializeField]
        private GameStateManager gameStateManager;

        // TODO: add documentation about the hows and whys on the custom Random handling
        public RandomManager Random { get => gameStateManager.Random; }

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
        /// state that originally triggered the event.  The GameState from after the event fires is
        /// passed in as a parameter to your callback just in case, though.
        /// See also: <br/>
        /// <seealso cref="RollbackEventsDelegateHandler"/> and
        /// <seealso cref="StateFrameDTO"/>
        /// </summary>
        public event RollbackEventsDelegateHandler OnRollbackEvents;

        /// <summary>
        /// This event fires when a given input needs to be applied to your game.
        /// Primarily, this will happen at the beginning of the server
        /// reconciliation process (or when otherwise beginning a replay).
        /// 
        /// NOTE: Because NetworkStateManager doesn't know anything about how
        /// many players you have, you'll need to iterate through all your
        /// player id's and ask for a prediction for any inputs not included
        /// in playerInputs
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
        /// 
        /// Note that if you only ever create/destroy objects during game Events and their
        /// rollbacks, it's unlikely that you'll encounter this as a problem.
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

        internal void ApplyEvents(HashSet<IGameEvent> events)
        {
            if (events.Count == 0)
            {
                return;
            }

            VerboseLog($"Applying {events.Count} events");
            OnApplyEvents?.Invoke(events);
        }

        internal void RollbackEvents(HashSet<IGameEvent> events, IGameState stateAfterEvent)
        {
            if (events.Count == 0)
            {
                return;
            }

            VerboseLog($"Rolling back {events.Count} events");
            OnRollbackEvents?.Invoke(events, stateAfterEvent);
        }

        internal void ApplyInputs(Dictionary<byte, IPlayerInput> playerInputs)
        {
            if (playerInputs.Count == 0)
            {
                return;
            }

            VerboseLog($"Applying {playerInputs.Count} player inputs");
            OnApplyInputs?.Invoke(playerInputs);
        }

        internal void ApplyState(IGameState gameState)
        {
            if (gameState == null)
            {
                return;
            }

            VerboseLog("Applying game state");
            PhysicsManager.SyncTransforms();
            OnApplyState?.Invoke(gameState);
            PhysicsManager.SyncTransforms();
        }

        internal void GetGameState(ref IGameState gameState)
        {
            VerboseLog("Capturing game state");
            OnGetGameState?.Invoke(ref gameState);
        }

        internal void GetInputs(ref Dictionary<byte, IPlayerInput> inputs)
        {
            VerboseLog("Capturing player inputs");
            OnGetInputs?.Invoke(ref inputs);
        }

        internal void PostPhysicsFrameUpdate()
        {
            VerboseLog("Running post-physics frame update");
            OnPostPhysicsFrameUpdate?.Invoke();
        }

        internal void PrePhysicsFrameUpdate()
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

            gameStateManager.ScheduleGameEvent(gameEvent, eventTick);

            // Let everyone know that an event is happening
            SyncGameEventsToClientsClientRpc(GameTick, gameStateManager.gameEventsBuffer);
        }

        /// <summary>
        /// Given a tick and a predicate that can find which event you want to remove, de-schedule that event at that tick.
        /// </summary>
        /// <param name="eventTick">The game tick the event was previously scheduled to fire on.</param>
        /// <param name="gameEventPredicate">If this function returns true for a given event, that event will be de-scheduled.</param>
        public void RemoveEventAtTick(int eventTick, Predicate<IGameEvent> gameEventPredicate)
        {
            // TODO: If an event is being de-scheduled during a rollback when processing a client's input data, do the clients need to know about the de-scheduling?
            gameStateManager.RemoveEventAtTick(eventTick, gameEventPredicate);
        }

        /// <summary>
        /// When applying inputs, some player id's may be omitted.  In this case, you should call this function to fill in input
        /// predictions for those players.
        /// </summary>
        /// <param name="playerId"></param>
        /// <returns>A predicted IPlayerInput</returns>
        public IPlayerInput PredictInputForPlayer(byte playerId)
        {
            // TODO: calling this function is an awkward thing to ask implementers to do; see if this can be improved
            return gameStateManager.PredictedInputForPlayer(playerId, GameTick);
        }

        #endregion Public Interface

        #region Initialization code

        public void StartNetworkStateManager(Type gameStateType, Type playerInputType, Type gameEventType)
        {
            VerboseLog("Network State Manager starting up");

            // TODO: If NetworkManager isn't ready yet, we should throw an error and refuse to start up.

            TypeStore.Instance.GameStateType = gameStateType;
            TypeStore.Instance.PlayerInputType = playerInputType;
            TypeStore.Instance.GameEventType = gameEventType;

            VerboseLog("Setting up network ids for scene objects that need them");

            gameStateManager = new(this, gameObject.scene);

            isRunning = false;

            if (!IsHost)
            {
                return;
            }

            // Server-only from here down
            isRunning = true;

            int randomSeedBase = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            gameStateManager.SetRandomBase(randomSeedBase);

            // Capture the initial game state
            gameStateManager.CaptureInitialFrame();

            // Ensure clients are starting from the same view of the world
            // TODO: see if there's some way to send this to all non-Host clients (instead of _all_ clients), to avoid some server overhead
            VerboseLog("Sending initial state");
            StartGameClientRpc(gameStateManager.GetStateFrame(0), randomSeedBase);
        }

        private void Awake()
        {
            isRunning = false;

            PhysicsManager.InitPhysics();
        }

        #endregion Initialization code

        #region Server-side only code

        private void HostFixedUpdate()
        {
            gameStateManager.RunFixedUpdate();

            // Send inputs for the frame
            Dictionary<byte, IPlayerInput> inputsToSend = gameStateManager.GetMinimalInputsDiffForCurrentFrame();
            if( inputsToSend.Count > 0)
            {
                PlayerInputsDTO playerInputsDTO = new()
                {
                    PlayerInputs = inputsToSend
                };

                ForwardPlayerInputsClientRpc(playerInputsDTO, realGameTick, realGameTick);
            }

            // (Maybe) send the new state to the clients for reconciliation
            // TODO: If we're sending a frame update AND sending inputs, we can consolidate those into a single RPC instead of doing two of them.
            if (realGameTick % sendFullStateEveryNFrames == 0)
            {
                VerboseLog("Sending full state to clients");

                // To avoid problems later with applying diffs, go back to the last time we would've sent out a
                // frame delta normally.
                int requestedGameTick = realGameTick - (realGameTick % sendStateDeltaEveryNFrames);
                
                ProcessFullStateUpdateClientRpc(gameStateManager.GetStateFrame(requestedGameTick), gameStateManager.gameEventsBuffer, realGameTick, RpcTarget.NotServer);
            }
            else if (realGameTick % sendStateDeltaEveryNFrames == 0)
            {
                VerboseLog("Sending delta - base frame comes from tick " + (realGameTick - sendStateDeltaEveryNFrames));

                // TODO: there's an opportunity to be slightly more aggressive by skipping sending anything if the entire
                //       state frame is exactly the same (except for the realGameTick, of course).
                StateFrameDeltaDTO delta = new(gameStateManager.GetStateFrame(realGameTick - sendStateDeltaEveryNFrames), gameStateManager.GetStateFrame(realGameTick));

                // TODO: send this to all non-Host clients (instead of _all_ clients), to avoid some server overhead
                ProcessStateDeltaUpdateClientRpc(delta, gameStateManager.gameEventsBuffer, realGameTick);
            }
        }

        // TODO: prevent a client from sending input for a player they shouldn't be sending input for
        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void SetPlayerInputsServerRpc(PlayerInputsDTO playerInputs, int clientTimeTick)
        {
            if (!IsReadyForRpcs())
            {
                // RPC's can arrive before this component has started, so skip out if it's too early
                VerboseLog("Player inputs received, but we haven't started yet so ignoring.");
                return;
            }

            VerboseLog("Player inputs received at " + clientTimeTick);

            // Set the input in our buffer
            gameStateManager.PlayerInputsReceived(playerInputs, clientTimeTick);

            // Forward the input to all other non-host clients so they can do the same
            // TODO: maybe exclude the client that sent this to us, since they've already got it
            // TODO: maybe gather up all incoming inputs over some span of time and then send them in batches, to reduce RPC calls
            ForwardPlayerInputsClientRpc(playerInputs, clientTimeTick, realGameTick);
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestFullStateUpdateServerRpc(RpcParams rpcParams = default)
        {
            VerboseLog("Received request for full state update");

            // To avoid problems later with applying diffs, go back to the last time we would've sent out a
            // frame delta normally.
            int requestedGameTick = realGameTick - (realGameTick % sendStateDeltaEveryNFrames);
            VerboseLog($"Full frame requested for {requestedGameTick}");

            // Send this back to only the client that requested it
            ProcessFullStateUpdateClientRpc(gameStateManager.GetStateFrame(requestedGameTick), gameStateManager.gameEventsBuffer, realGameTick, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        #endregion Server-side only code

        #region Client-side only code

        private void ClientFixedUpdate()
        {
            if (realGameTick > gameStateManager.lastAuthoritativeTick + maxFramesWithoutHearingFromServer)
            {
                Debug.LogWarning($"Haven't heard from the server since {gameStateManager.lastAuthoritativeTick}");
                // TODO: figure out what we want to do here, since we don't want to DoS the server just because we haven't heard from it in a while
            }

            gameStateManager.RunFixedUpdate();

            // Send our local inputs to the server, if they changed from the previous frame
            Dictionary<byte, IPlayerInput> inputsToSend = gameStateManager.GetMinimalInputsDiffForCurrentFrame();

            if (inputsToSend.Count > 0)
            {
                PlayerInputsDTO playerInputsDTO = new()
                {
                    PlayerInputs = inputsToSend
                };

                SetPlayerInputsServerRpc(playerInputsDTO, realGameTick);
            }
        }

        private bool IsReadyForRpcs()
        {
            if (isRunning)
            {
                return true;
            }

            // RPC's can arrive before this component has started, so skip out if it's too early
            // TODO: is this the right thing to do?  Seems like maybe no?
            VerboseLog("Server RPC arrived before we've started, so skipping");
            return false;
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.NotServer)]
        private void ForwardPlayerInputsClientRpc(PlayerInputsDTO playerInputs, int clientTimeTick, int serverTick)
        {
            if (!IsReadyForRpcs())
            {
                return;
            }

            // If this happened before our last authoritative tick, we can safely ignore it
            if (clientTimeTick < gameStateManager.lastAuthoritativeTick || serverTick < gameStateManager.lastAuthoritativeTick)
            {
                VerboseLog("Client inputs arrived from before our last authoritative tick, so ignoring");
                return;
            }

            gameStateManager.ReplayDueToInputs(playerInputs, clientTimeTick, serverTick, GetEstimatedLag());
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.NotServer)]
        private void SyncGameEventsToClientsClientRpc(int serverTimeTick, GameEventsBuffer newGameEventsBuffer)
        {
            if (!IsReadyForRpcs())
            {
                return;
            }

            if( serverTimeTick < gameStateManager.lastAuthoritativeTick )
            {
                // We'll already have the most up-to-date events reflected from whatever sent us the last authoritative
                // data, and we don't want to worry about accidentally mangling the server state during replay.
                return;
            }

            gameStateManager.ReplayDueToEvents(serverTimeTick, newGameEventsBuffer, GetEstimatedLag());
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.NotServer)]
        private void StartGameClientRpc(StateFrameDTO initialStateFrame, int randomSeedBase)
        {
            // TODO: figure out what to do if the initial game state never arrives

            VerboseLog("Initial game state received from server.");

            gameStateManager.SetInitialGameState(initialStateFrame, randomSeedBase, GetEstimatedLag());

            // Start things off!
            isRunning = true;
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.NotServer)]
        private void ProcessStateDeltaUpdateClientRpc(StateFrameDeltaDTO serverGameStateDelta, GameEventsBuffer newGameEventsBuffer, int serverTick)
        {
            if (!IsReadyForRpcs())
            {
                return;
            }

            VerboseLog("Server state delta received.");

            try
            {
                gameStateManager.ProcessStateDeltaReceived(serverGameStateDelta, newGameEventsBuffer, serverTick, GetEstimatedLag(), sendStateDeltaEveryNFrames);
            }
            catch(Exception e)
            {
                VerboseLog($"Something went wrong when reconstituting game state from diff: {e.Message}");
                RequestFullStateUpdateServerRpc();
            }
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        /// <summary>
        /// 
        /// </summary>
        /// <param name="serverGameState"></param>
        /// <param name="serverGameEventsBuffer"></param>
        /// <param name="serverTick">This is needed because the server will only ever send full frames that are aligned to the delta tick frequency</param>
        [Rpc(SendTo.SpecifiedInParams)]
        private void ProcessFullStateUpdateClientRpc(StateFrameDTO serverGameState, GameEventsBuffer serverGameEventsBuffer, int serverTick, RpcParams _)
        {
            if (!IsReadyForRpcs())
            {
                return;
            }

            VerboseLog("Received full state update from server");

            // Get us back in sync
            gameStateManager.SyncToServerState(serverGameState, serverGameEventsBuffer, serverTick, GetEstimatedLag());
        }

        private int GetEstimatedLag()
        {
            if (IsHost)
            {
                throw new Exception("The host shouldn't ever call this");
            }

            int framesOfLag = (NetworkManager.LocalTime - NetworkManager.ServerTime).Tick;
            if (framesOfLag < 0)
            {
                throw new Exception("Client is somehow ahead of server, which Unity's library shouldn't ever permit to happen.");
            }

            VerboseLog("Client is about " + framesOfLag + " frames ahead of the server");

            return framesOfLag;
        }

        #endregion Client-side only code

        private void FixedUpdate()
        {
            if (!isRunning)
            {
                return;
            }

            // Start a new frame
            gameStateManager.AdvanceTime();
            VerboseLog("---- NEW FRAME ----");

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


            if (realGameTick != GameTick)
            {
                log += "** ";
            }

            log += realGameTick + "";
            if(realGameTick != GameTick)
            {
                log += " (" + GameTick + ")";
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