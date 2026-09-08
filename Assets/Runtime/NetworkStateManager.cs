using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NSM
{
    public class NetworkStateManager : NetworkBehaviour, IInternalNetworkStateManager
    {
        #region NetworkStateManager configuration

        [Tooltip("1 frame = 20ms, 50 frames = 1s")]
        public int sendStateDeltaEveryNFrames = 10;
        [Tooltip("1 frame = 20ms, 50 frames = 1s")]
        public int sendFullStateEveryNFrames = 100;
        [Tooltip("1 frame = 20ms, 50 frames = 1s")]
        public int maxFramesWithoutHearingFromServer = 40;

        public bool verboseLogging = false;
        public SimulationLimits simulationLimits = new();
        private SimulationLimits activeLimits;
        public InputAdmission InputPolicy { get; private set; }
        public event Action<ulong, InputRejection> OnInputRejected;
        /// <summary>Called once when a frame leaves the mutable rollback window. Use for irreversible effects.</summary>
        public event Action<int, StateFrameDTO> OnFrameConfirmed;
        public event Action<Exception> OnSimulationFault;
        /// <summary>Rebuild the world and discard reversible effects from missing history. Missing frames are not confirmed individually.</summary>
        public event Action<int, StateFrameDTO> OnHistoryReset;
        private bool startRequested;
        private bool faulted;
        private float nextBaselineRequest;
        private readonly Dictionary<ulong, int> pendingRequests = new();
        public int ConfirmedTick => gameStateManager?.ConfirmedTick ?? 0;
        private StateFrameDTO lastSentState;
        private int lastSentTick;

        public void ConfigureInputPolicy(Action<InputAdmission> configure)
        {
            if (startRequested || IsRunning) throw new InvalidOperationException("Configure the initial input policy before starting NSM.");
            activeLimits = simulationLimits.ValidatedCopy();
            InputPolicy = new InputAdmission(activeLimits);
            configure?.Invoke(InputPolicy);
        }

        void IInternalNetworkStateManager.ConfirmFrame(int tick, StateFrameDTO frame) => OnFrameConfirmed?.Invoke(tick, (StateFrameDTO)frame.Clone());
        bool IInternalNetworkStateManager.RestoreBaseline(int previousConfirmedTick, StateFrameDTO frame)
        {
            if (OnHistoryReset == null) return false;
            OnHistoryReset(previousConfirmedTick, frame);
            return true;
        }

        #endregion NetworkStateManager configuration

        #region Runtime state

        [SerializeProperty]
        private int RealGameTick { get => gameStateManager?.RealGameTick ?? -1; }    // This is the internal game tick, which keeps track of "now"

        [SerializeProperty]
        public int GameTick { get => gameStateManager?.GameTick ?? -1; }    // Users of the library will get the tick associated with whatever frame is currently being processed, which might include frames that are being replayed

        [SerializeProperty]
        public bool isReplaying { get => gameStateManager?.IsReplaying ?? false; }

        public bool IsRunning { get; private set; } = false;    // Have the server given us our initial state, so we're now good to go?

        public NetworkIdManager NetworkIdManager { get => (NetworkIdManager)gameStateManager.NetworkIdManager; }

        [SerializeField]
        private GameStateManager gameStateManager;

        public RandomManager Random { get => gameStateManager.Random; }

        private readonly Queue<RPCQueueJob> rpcQueue = new();

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
        /// The callback observes the exact post-frame game and physics state of the frame
        /// being undone. That state is also passed as an argument. NSM restores the prior
        /// frame after the callback. isReplaying is true throughout rewind and replay.
        /// Keep effects reversible, or defer irreversible effects to OnFrameConfirmed.
        /// NOTE: The RNG's state will be what it was just before the events were originally run.
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
            if (!IsServer)
            {
                // Events need to be server-authoritative in all cases, to prevent problems with the client erroneously scheduling them
                // based on incorrect predictions
                return;
            }

            gameStateManager.ScheduleGameEvent(gameEvent, eventTick);

            // Let everyone know that an event is happening
            if (IsSpawned)
                SyncGameEventsToClientsClientRpc(GameTick, (GameEventsBuffer)gameStateManager.GameEventsBuffer);
        }

        /// <summary>
        /// Given a tick and a predicate that can find which event you want to remove, de-schedule that event at that tick.
        /// </summary>
        /// <param name="eventTick">The game tick the event was previously scheduled to fire on.</param>
        /// <param name="gameEventPredicate">If this function returns true for a given event, that event will be de-scheduled.</param>
        public void RemoveEventAtTick(int eventTick, Predicate<IGameEvent> gameEventPredicate)
        {
            if (!IsServer)
            {
                // Clients cannot remove events authoritatively
                Debug.LogWarning("Client attempted to remove a game event.");
                return;
            }

            // Store count before removal for comparison
            int initialCount = gameStateManager.GameEventsBuffer[eventTick].Count;

            gameStateManager.RemoveEventAtTick(eventTick, gameEventPredicate);

            // If an event was actually removed, notify clients
            if (IsSpawned && gameStateManager.GameEventsBuffer[eventTick].Count < initialCount)
            {
                VerboseLog($"Event removed at tick {eventTick}, synchronizing event buffer.");
                SyncGameEventsToClientsClientRpc(GameTick, (GameEventsBuffer)gameStateManager.GameEventsBuffer);
            }
        }

        /// <summary>
        /// When applying inputs, some player id's may be omitted.  In this case, you should call this function to fill in input
        /// predictions for those players.
        /// </summary>
        /// <param name="playerId"></param>
        /// <returns>A predicted IPlayerInput</returns>
        public IPlayerInput PredictInputForPlayer(byte playerId)
        {
            return gameStateManager.PredictedInputForPlayer(playerId, GameTick);
        }

        /// <summary>
        /// Get the player inputs from a given tick.  Useful if you need to detect input sequences, full button presses, etc.
        /// </summary>
        /// <param name="tick">Which tick you need data for</param>
        /// <returns>A Dictionary mapping player id to IPlayerInput</returns>
        public Dictionary<byte, IPlayerInput> GetInputsForTick(int tick)
        {
            return gameStateManager.GetInputsForTick(tick);
        }

        #endregion Public Interface

        #region Initialization code

        public async Awaitable StartNetworkStateManager(Type gameStateType, Type playerInputType, Type gameEventType)
        {
            if (startRequested || IsRunning || faulted) throw new InvalidOperationException("NSM has already started. Despawn before starting a new session.");
            if (!IsSpawned) throw new InvalidOperationException("Spawn NSM before starting the simulation.");
            activeLimits ??= simulationLimits.ValidatedCopy();
            if (sendStateDeltaEveryNFrames <= 0 || sendFullStateEveryNFrames <= 0 ||
                sendStateDeltaEveryNFrames >= activeLimits.historyTicks)
                throw new InvalidOperationException("Snapshot cadence must be positive and fit inside retained history.");
            InputPolicy ??= new InputAdmission(activeLimits);
            InputPolicy.ResetSession();
            startRequested = true;
            nextBaselineRequest = 0;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            // I don't trust NGO to have sent the correct readiness signals, so give a little buffer for things to settle
            // before sending the initial gamestate
            // TODO: maybe NGO 2.x will make this simpler?
            await Awaitable.NextFrameAsync();
            await Awaitable.MainThreadAsync();

            VerboseLog("Network State Manager starting up");

            TypeStore.Instance.GameStateType = gameStateType;
            TypeStore.Instance.PlayerInputType = playerInputType;
            TypeStore.Instance.GameEventType = gameEventType;

            VerboseLog("Setting up network ids for scene objects that need them");

            gameStateManager = new(
                this,
                new GameEventsBuffer(),
                new InputsBuffer(),
                new StateBuffer(),
                new NetworkIdManager(this),
                gameObject.scene,
                activeLimits,
                IsServer
            );

            if (!IsServer)
            {
                return;
            }

            // Server-only from here down
            int randomSeedBase = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            gameStateManager.SetRandomBase(randomSeedBase);

            // Capture the initial game state
            gameStateManager.CaptureInitialFrame();
            lastSentState = gameStateManager.GetStateFrame(0);
            lastSentTick = 0;

            // Ensure clients are starting from the same view of the world
            VerboseLog("Sending initial state");
            StartGameClientRpc(gameStateManager.GetStateFrame(0), randomSeedBase);
            IsRunning = true;
        }

        private void Awake()
        {
            IsRunning = false;

            PhysicsManager.InitPhysics();
        }

        #endregion Initialization code

        #region Server-side only code

        private void HostFixedUpdate()
        {
            gameStateManager.RunFixedUpdate();

            // Send inputs for the frame
            Dictionary<byte, IPlayerInput> inputsToSend = gameStateManager.GetMinimalInputsDiffForCurrentFrame();
            if (inputsToSend.Count > 0)
            {
                PlayerInputsDTO playerInputsDTO = new()
                {
                    PlayerInputs = inputsToSend
                };

                ForwardPlayerInputsClientRpc(playerInputsDTO, RealGameTick, RealGameTick, RpcTarget.NotServer);
            }

            // (Maybe) send the new state to the clients for reconciliation
            if (RealGameTick % sendFullStateEveryNFrames == 0)
            {
                VerboseLog("Sending full state to clients");

                // To avoid problems later with applying diffs, go back to the last time we would've sent out a
                // frame delta normally.
                int requestedGameTick = RealGameTick - (RealGameTick % sendStateDeltaEveryNFrames);

                ProcessFullStateUpdateClientRpc(
                    requestedGameTick == RealGameTick ? gameStateManager.GetStateFrame(requestedGameTick) : lastSentState,
                    (GameEventsBuffer)gameStateManager.GameEventsBuffer,
                    requestedGameTick,
                    RealGameTick,
                    RpcTarget.NotServer
                );
            }
            else if (RealGameTick % sendStateDeltaEveryNFrames == 0)
            {
                VerboseLog($"Sending delta - base frame comes from tick {RealGameTick - sendStateDeltaEveryNFrames}");

                StateFrameDeltaDTO delta = new(lastSentState, gameStateManager.GetStateFrame(RealGameTick));

                ProcessStateDeltaUpdateClientRpc(delta, (GameEventsBuffer)gameStateManager.GameEventsBuffer, RealGameTick);
            }
            if (RealGameTick % sendStateDeltaEveryNFrames == 0)
            {
                lastSentTick = RealGameTick;
                lastSentState = gameStateManager.GetStateFrame(lastSentTick);
            }
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SetPlayerInputsServerRpc(PlayerInputsDTO playerInputs, int clientTimeTick, RpcParams rpcParams = default)
        {
            if (!IsRunning || InputPolicy == null) return;
            TryEnqueue(new RPCQueueJobSetPlayerInputsServerRpc(
                playerInputs,
                clientTimeTick,
                rpcParams
            ), rpcParams.Receive.SenderClientId);
        }

        private void RunJobSetPlayerInputsServer(PlayerInputsDTO playerInputs, int clientTimeTick, RpcParams rpcParams)
        {
            var rejection = InputPolicy.TryAccept(rpcParams.Receive.SenderClientId, playerInputs.PlayerInputs,
                clientTimeTick, RealGameTick, gameStateManager.OldestRetainedTick, TypeStore.Instance.PlayerInputType);
            if (rejection != InputRejection.None)
            {
                OnInputRejected?.Invoke(rpcParams.Receive.SenderClientId, rejection);
                return;
            }
            VerboseLog($"Player inputs received at {clientTimeTick}");

            // Set the input in our buffer and replay to include the input
            if (!gameStateManager.PlayerInputsReceived(playerInputs, clientTimeTick)) return;

            // Forward the input to all other non-host clients so they can do the same
            ulong[] clientIds = new ulong[2];
            clientIds[0] = NetworkManager.LocalClientId;    // Don't send to the host
            clientIds[1] = rpcParams.Receive.SenderClientId;  // Don't send back to the client that sent this to us

            ForwardPlayerInputsClientRpc(playerInputs, clientTimeTick, RealGameTick, RpcTarget.Not(clientIds, RpcTargetUse.Temp));
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestFullStateUpdateServerRpc(RpcParams rpcParams = default)
        {
            if (!IsRunning) return;
            TryEnqueue(new RPCQueueJobRequestFullStateUpdateServerRpc(rpcParams), rpcParams.Receive.SenderClientId);
        }

        private void RunJobRequestFullStateUpdateServer(RpcParams rpcParams)
        {
            VerboseLog("Received request for full state update");

            // To avoid problems later with applying diffs, go back to the last time we would've sent out a
            // frame delta normally.
            int requestedGameTick = lastSentTick;
            VerboseLog($"Full frame requested for {requestedGameTick}");

            // Send this back to only the client that requested it
            ProcessFullStateUpdateClientRpc(
                lastSentState,
                (GameEventsBuffer)gameStateManager.GameEventsBuffer,
                requestedGameTick,
                RealGameTick,
                RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp)
            );
        }

        #endregion Server-side only code

        #region Client-side only code

        private void ClientFixedUpdate()
        {
            // Stop prediction before a disconnected server can cause unbounded retained history.
            if ((long)RealGameTick - gameStateManager.LastAuthoritativeTick >= activeLimits.historyTicks)
            {
                RequestBaseline();
                return;
            }
            if (RealGameTick > gameStateManager.LastAuthoritativeTick + maxFramesWithoutHearingFromServer)
            {
                Debug.LogWarning($"Haven't heard from the server since {gameStateManager.LastAuthoritativeTick}");
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

                SetPlayerInputsServerRpc(playerInputsDTO, RealGameTick);
            }
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ForwardPlayerInputsClientRpc(PlayerInputsDTO playerInputs, int clientTimeTick, int serverTick, RpcParams _)
        {
            TryEnqueue(new RPCQueueJobForwardPlayerInputsClientRpc(playerInputs, clientTimeTick, serverTick));
        }

        private void RunJobForwardPlayerInputsClient(PlayerInputsDTO playerInputs, int clientTimeTick, int serverTick)
        {
            if ((long)serverTick - RealGameTick > activeLimits.historyTicks) { RequestBaseline(); return; }
            // If this happened before our last authoritative tick, we can safely ignore it
            if (clientTimeTick <= gameStateManager.ConfirmedTick || serverTick < gameStateManager.ConfirmedTick)
            {
                VerboseLog("Client inputs arrived from before our last authoritative tick, so ignoring");
                return;
            }

            gameStateManager.ReplayDueToInputs(playerInputs, clientTimeTick, serverTick, GetEstimatedLag());
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void SyncGameEventsToClientsClientRpc(int serverTimeTick, GameEventsBuffer newGameEventsBuffer)
        {
            TryEnqueue(new RPCQueueJobSyncGameEventsToClientsClientRpc(serverTimeTick, newGameEventsBuffer));
        }

        private void RunJobSyncGameEventsToClientsClient(int serverTimeTick, GameEventsBuffer newGameEventsBuffer)
        {
            if ((long)serverTimeTick - RealGameTick > activeLimits.historyTicks) { RequestBaseline(); return; }
            if (serverTimeTick < gameStateManager.LastAuthoritativeTick)
            {
                // We'll already have the most up-to-date events reflected from whatever sent us the last authoritative
                // data, and we don't want to worry about accidentally mangling the server state during replay.
                return;
            }

            gameStateManager.ReplayDueToEvents(serverTimeTick, newGameEventsBuffer, GetEstimatedLag());
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void StartGameClientRpc(StateFrameDTO initialStateFrame, int randomSeedBase)
        {
            if (!IsRunning) TryEnqueue(new RPCQueueJobStartGameClientRpc(initialStateFrame, randomSeedBase));
        }

        private void RunJobStartGameClient(StateFrameDTO initialStateFrame, int randomSeedBase)
        {
            VerboseLog("Initial game state received from server.");

            gameStateManager.SetInitialGameState(initialStateFrame, randomSeedBase, GetEstimatedLag());
            IsRunning = true;
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ProcessStateDeltaUpdateClientRpc(StateFrameDeltaDTO serverGameStateDelta, GameEventsBuffer newGameEventsBuffer, int serverTick)
        {
            TryEnqueue(new RPCQueueJobProcessStateDeltaUpdateClientRpc(serverGameStateDelta, newGameEventsBuffer, serverTick));
        }

        private void RunJobProcessStateDeltaUpdateClient(StateFrameDeltaDTO serverGameStateDelta, GameEventsBuffer newGameEventsBuffer, int serverTick)
        {
            VerboseLog("Server state delta received.");

            if (serverTick <= gameStateManager.LastAuthoritativeTick) return;
            if ((long)serverTick != (long)gameStateManager.LastAuthoritativeTick + sendStateDeltaEveryNFrames)
            {
                RequestBaseline();
                return;
            }
            try { gameStateManager.ProcessStateDeltaReceived(serverGameStateDelta, newGameEventsBuffer, serverTick, GetEstimatedLag(), sendStateDeltaEveryNFrames); }
            catch (StateDeltaDecodeException) { RequestBaseline(); }
        }

        // NOTE: Rpc's are processed at the _end_ of each frame
        /// <summary>
        /// 
        /// </summary>
        /// <param name="serverGameState"></param>
        /// <param name="serverGameEventsBuffer"></param>
        /// <param name="serverNow">This is needed because the server will only ever send full frames that are aligned to the delta tick frequency</param>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ProcessFullStateUpdateClientRpc(StateFrameDTO serverGameState, GameEventsBuffer serverGameEventsBuffer, int frameTick, int serverNow, RpcParams _)
        {
            TryEnqueue(new RPCQueueJobProcessFullStateUpdateClientRpc(serverGameState, serverGameEventsBuffer, frameTick, serverNow));
        }

        private void RunJobProcessFullStateUpdateClient(StateFrameDTO serverGameState, GameEventsBuffer serverGameEventsBuffer, int frameTick, int serverNow)
        {
            VerboseLog("Received full state update from server");

            // Get us back in sync
            gameStateManager.SyncToServerState(serverGameState, serverGameEventsBuffer, frameTick, serverNow, GetEstimatedLag());
        }

        private int GetEstimatedLag()
        {
            if (IsServer)
            {
                throw new Exception("The host shouldn't ever call this");
            }

            // NGO's tick rate can differ from Unity's fixed simulation timestep.
            int framesOfLag = EstimateLagTicks(NetworkManager.LocalTime.Time - NetworkManager.ServerTime.Time, Time.fixedDeltaTime);

            VerboseLog($"Client is about {framesOfLag} frames behind the server");

            return framesOfLag;
        }

        internal static int EstimateLagTicks(double seconds, double fixedStep)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 ||
                double.IsNaN(fixedStep) || double.IsInfinity(fixedStep) || fixedStep <= 0 || seconds / fixedStep > int.MaxValue)
                throw new InvalidOperationException("Invalid network clock offset or simulation timestep.");
            return checked((int)Math.Ceiling(seconds / fixedStep));
        }

        private void RequestBaseline()
        {
            if (!IsSpawned || Time.unscaledTime < nextBaselineRequest) return;
            nextBaselineRequest = Time.unscaledTime + 1;
            RequestFullStateUpdateServerRpc();
        }

        #endregion Client-side only code

        #region Internal interface
        public void ApplyEvents(HashSet<IGameEvent> events)
        {
            if (events.Count == 0)
            {
                return;
            }

            VerboseLog($"Applying {events.Count} events");
            OnApplyEvents?.Invoke(events);
        }

        public void RollbackEvents(HashSet<IGameEvent> events, IGameState stateAfterEvent)
        {
            if (events.Count == 0)
            {
                return;
            }

            VerboseLog($"Rolling back {events.Count} events");
            OnRollbackEvents?.Invoke(events, stateAfterEvent);
        }

        public void ApplyInputs(Dictionary<byte, IPlayerInput> playerInputs)
        {
            if (playerInputs.Count == 0)
            {
                return;
            }

            VerboseLog($"Applying {playerInputs.Count} player inputs");
            OnApplyInputs?.Invoke(playerInputs);
        }

        public void ApplyState(IGameState gameState)
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

        public void GetGameState(ref IGameState gameState)
        {
            VerboseLog("Capturing game state");
            OnGetGameState?.Invoke(ref gameState);
        }

        public void GetInputs(ref Dictionary<byte, IPlayerInput> inputs)
        {
            VerboseLog("Capturing player inputs");
            OnGetInputs?.Invoke(ref inputs);
        }

        public void PostPhysicsFrameUpdate()
        {
            VerboseLog("Running post-physics frame update");
            OnPostPhysicsFrameUpdate?.Invoke();
        }

        public void PrePhysicsFrameUpdate()
        {
            VerboseLog("Running pre-physics frame update");
            OnPrePhysicsFrameUpdate?.Invoke();
        }

        #endregion Internal interface

        private void FixedUpdate()
        {
            try { RunFixedUpdate(); }
            catch (Exception error)
            {
                faulted = true;
                IsRunning = false;
                rpcQueue.Clear();
                pendingRequests.Clear();
                Debug.LogException(error);
                OnSimulationFault?.Invoke(error);
            }
        }

        private void OnClientDisconnected(ulong clientId) => InputPolicy?.RemoveClient(clientId);

        public override void OnNetworkDespawn()
        {
            IsRunning = false;
            rpcQueue.Clear();
            pendingRequests.Clear();
            startRequested = faulted = false;
            gameStateManager = null;
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            base.OnNetworkDespawn();
        }

        private void RunFixedUpdate()
        {
            if(gameStateManager == null || faulted)
            {
                // We're not initialized yet
                return;
            }

            // Enqueued received RPC's should be thought of as having arrived "at the end of the previous frame"
            ProcessRPCQueue();

            if(!IsRunning)
            {
                return;
            }

            if (IsServer)
            {
                HostFixedUpdate();
            }
            else if (IsClient)
            {
                ClientFixedUpdate();
            }

            VerboseLog("---- END FRAME ----");
        }

        internal bool TryEnqueue(RPCQueueJob job, ulong? sender = null)
        {
            if (faulted) return false;
            int queueLimit = activeLimits?.maxQueuedMessages ?? 128;
            int peerLimit = activeLimits?.maxMessagesPerClientPerUpdate ?? 4;
            int pending = 0;
            if (sender.HasValue) pendingRequests.TryGetValue(sender.Value, out pending);
            if (rpcQueue.Count >= queueLimit || (sender.HasValue && pending >= peerLimit))
            {
                if (sender.HasValue) OnInputRejected?.Invoke(sender.Value, InputRejection.QueueFull);
                else if (IsRunning) RequestBaseline();
                return false;
            }
            if (sender.HasValue) pendingRequests[sender.Value] = pending + 1;
            rpcQueue.Enqueue(job);
            return true;
        }

        private void ReleasePending(ulong sender)
        {
            if (!pendingRequests.TryGetValue(sender, out int count)) return;
            if (count <= 1) pendingRequests.Remove(sender);
            else pendingRequests[sender] = count - 1;
        }

        internal void ProcessRPCQueue()
        {
            if (faulted) return;
            gameStateManager?.BeginUpdate();
            InputPolicy?.BeginUpdate(RealGameTick);
            if (!IsRunning)
            {
                // See if we have the RPCQueueJobStartGameClientRpc job in our queue.
                // If so, run it (this will set IsRunning to true) and remove it. Else, basically do nothing.

                // We'll do this by copying the (almost certainly empty) queue, running through each,
                // and either processing the start game or putting the job back into the queue.
                // In theory, this can probably be made more efficient.  In practice... meh.
                List<RPCQueueJob> tempQueue = rpcQueue.ToList();
                rpcQueue.Clear();
                foreach (RPCQueueJob job in tempQueue)
                {
                    // Anything not starting the game can go back in the queue
                    if (job.GetType() != typeof(RPCQueueJobStartGameClientRpc))
                    {
                        rpcQueue.Enqueue(job);
                        continue;
                    }

                    RPCQueueJobStartGameClientRpc jobParams = (RPCQueueJobStartGameClientRpc)job;
                    if (!IsRunning) RunJobStartGameClient(jobParams.initialStateFrame, jobParams.randomSeedBase);
                    // Intentionally don't put this job back in the queue
                }

                return;
            }

            int remaining = activeLimits?.maxMessagesPerUpdate ?? 16;
            // Reserve enough for one worst-case reconciliation; leave excess work queued.
            while (remaining-- > 0 &&
                (gameStateManager?.ReplayTicksThisUpdate ?? 0) <= (activeLimits?.maxReplayTicksPerUpdate ?? 1024) - 4 * (activeLimits?.historyTicks ?? 256) &&
                rpcQueue.TryDequeue(out RPCQueueJob job))
            {
                switch(job)
                {
                    case RPCQueueJobForwardPlayerInputsClientRpc jobParams:
                        RunJobForwardPlayerInputsClient(jobParams.playerInputs, jobParams.clientTimeTick, jobParams.serverTick);
                        break;
                    case RPCQueueJobProcessFullStateUpdateClientRpc jobParams:
                        RunJobProcessFullStateUpdateClient(jobParams.serverGameState, jobParams.serverGameEventsBuffer, jobParams.frameTick, jobParams.serverNow);
                        break;
                    case RPCQueueJobProcessStateDeltaUpdateClientRpc jobParams:
                        RunJobProcessStateDeltaUpdateClient(jobParams.serverGameStateDelta, jobParams.newGameEventsBuffer, jobParams.serverTick);
                        break;
                    case RPCQueueJobRequestFullStateUpdateServerRpc jobParams:
                        ReleasePending(jobParams.rpcParams.Receive.SenderClientId);
                        RunJobRequestFullStateUpdateServer(jobParams.rpcParams);
                        break;
                    case RPCQueueJobSetPlayerInputsServerRpc jobParams:
                        ReleasePending(jobParams.rpcParams.Receive.SenderClientId);
                        RunJobSetPlayerInputsServer(jobParams.playerInputs, jobParams.clientTimeTick, jobParams.rpcParams);
                        break;
                    case RPCQueueJobSyncGameEventsToClientsClientRpc jobParams:
                        RunJobSyncGameEventsToClientsClient(jobParams.serverTimeTick, jobParams.newGameEventsBuffer);
                        break;
                    default:
                        Debug.LogError($"Unknown RPC job type: {job.GetType()}");
                        break;
                }
            }
        }

        public void VerboseLog(string message)
        {
#if UNITY_EDITOR
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


            if (RealGameTick != GameTick)
            {
                log += "** ";
            }

            log += RealGameTick + "";
            if (RealGameTick != GameTick)
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
