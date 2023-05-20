using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Netcode;
using UnityEditor.PackageManager;
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

        #region NetworkStateManager configuration

        // Multiply these number by Time.fixedDeltaTime (20ms/frame) to know how much
        // lag we'll permit beyond what Unity's networking system thinks the lag is.
        public int maxPastTolerance = 5;

        public int sendStateEveryNFrames = 10;
        public bool verboseLogging = false;

        [Header("Debug - Rollback")]
        public bool debugRollback = false;

        public uint debugRollbackEveryNFrames = 4;
        public uint debugNumFramesToRollback = 8;
        public bool debugRandomizeNumFramesToRollback = false;

        #endregion NetworkStateManager configuration

        #region Runtime state

        [Header("Runtime state")]
        private int realGameTick = 0;   // This is the internal game tick, which keeps track of "now"

        public int gameTick = 0;    // Users of the library will get the tick associated with whatever frame is currently being processed, which might include frames that are being replayed

        public bool isReplaying = false;
        public int lastAuthoritativeTick = 0;
        public StateBuffer stateBuffer;
        public GameEventsBuffer gameEventsBuffer; // TODO: some sort of configurable amount of events into the past that we'll hang onto (for clients to be able to undo during replay) so that we're not trying to sync the entire history of all events each time
        private GameEventsBuffer _pendingGameEventsBuffer;
        private bool _hasPendingGameEventsBuffer = false;

        [SerializeField]
        private bool isRunning = false;

        [SerializeField]
        private int replayFromTick = -1;    // anything negative is a flag value, meaning "don't replay anything"...TODO: make this an explicit bool instead of the flag value

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
        /// <exception cref="NotImplementedException"></exception>
        public void RemoveEventAtTick(int willSpawnAtTick, Predicate<IGameEvent> gameEventPredicate)
        {
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

            SetupInitialNetworkIds();
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
            StateFrameDTO blankFrame = new()
            {
                gameTick = 0,
            };

            stateBuffer[0] = CaptureStateFrame(0);

            randomSeedBase = Random.Range(int.MinValue, int.MaxValue);

            // Ensure clients are starting from the same view of the world
            // TODO: see if there's some way to send this to all non-Host clients (instead of _all_ clients), to avoid some server overhead
            StateFrameDTO delta = blankFrame.GenerateDelta(stateBuffer[0]);
            VerboseLog("Sending initial state with delta size:" + delta._gameStateDiffBytes.Length);
            StartGameClientRpc(delta, randomSeedBase);
        }

        private void Awake()
        {
            isRunning = false;

            // In order for NSM to work, we'll need to fully control physics (Muahahaha)
            Physics.simulationMode = SimulationMode.Script;
            Physics.autoSyncTransforms = false;
        }

        private void SetupInitialNetworkIds()
        {
            VerboseLog("Setting up network ids for scene objects that need them");

            // Basically, we can't know what order everything's going to load in, so we can't know whether all clients will
            // get the same network id's on instantiation.
            // So instead, when the scene's ready we'll:
            //  * reset the counter
            //  * go through all the game objects that need a network id (in hierarchy order)
            //  * regenerate the network ids
            // In theory, the client and server should agree on the objects in the hierarchy at this point in time, so it should
            // be ok to use as a deterministic ordering mechanism.

            // TODO: [bug] if a game object is at the root level, it won't be found by this and won't get a network id

            networkIdManager.Reset();
            List<GameObject> gameObjects = gameObject.scene.GetRootGameObjects().ToList();
            gameObjects.Sort((a, b) => a.transform.GetSiblingIndex() - b.transform.GetSiblingIndex());
            foreach (GameObject gameObject in gameObjects)
            {
                SetupNetworkIdsForChildren(gameObject.transform);
            }
        }

        private void SetupNetworkIdsForChildren(Transform node)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                if (child.gameObject.TryGetComponent(out NetworkId _))
                {
                    networkIdManager.RegisterGameObject(child.gameObject);
                }

                if (child.childCount > 0)
                {
                    SetupNetworkIdsForChildren(child);
                }
            }
        }

        #endregion Initialization code

        #region Server-side only code

        private void HostFixedUpdate()
        {
            if (debugRollback == true && realGameTick % debugRollbackEveryNFrames == 0 && realGameTick >= debugNumFramesToRollback)
            {
                uint framesToRollback = debugNumFramesToRollback;
                if (debugRandomizeNumFramesToRollback)
                {
                    // Use System.Random so that we don't interfere with Unity's Random and the playback we do there
                    System.Random rand = new();
                    framesToRollback = (uint)rand.Next((int)debugNumFramesToRollback);
                }
                VerboseLog("DEBUG: rolling back " + framesToRollback + " frames");
                ScheduleStateReplay((int)(realGameTick - framesToRollback));
            }

            RunScheduledStateReplay();

            VerboseLog("Normal frame run");

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
                    ForwardPlayerInputClientRpc(entry.Key, playerInputDTO, realGameTick);
                }
            }

            // Actually simulate the frame
            stateBuffer[realGameTick] = RunSingleGameFrame(realGameTick, playerInputs, gameEventsBuffer[realGameTick]);

            // (Maybe) send the new state to the clients for reconciliation
            if (realGameTick % sendStateEveryNFrames == 0)
            {
                VerboseLog("Sending delta - base frame comes from tick " + (realGameTick - sendStateEveryNFrames));

                // TODO: there's an opportunity to be slightly more aggressive by skipping sending anything if the entire
                //       state frame is exactly the same (except for the realGameTick, of course).
                StateFrameDTO delta = stateBuffer[realGameTick - sendStateEveryNFrames].GenerateDelta(stateBuffer[realGameTick]);
                VerboseLog("Sending delta with diff size of " + delta._gameStateDiffBytes?.Length);

                // TODO: see if there's some way to send this to all non-Host clients (instead of _all_ clients), to avoid some server overhead
                UpdateGameStateClientRpc(delta, gameEventsBuffer);
            }
        }

        // TODO: prevent a client from sending input for a player they shouldn't be sending input for
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

            // Schedule a replay from this point
            VerboseLog("Input received.  Will replay from: " + clientTimeTick + " to our time");
            ScheduleStateReplay(clientTimeTick);

            // Forward the input to all clients so they can do the same
            // TODO: see if there's some way to send this to all non-Host clients (instead of _all_ clients), to avoid some server overhead
            ForwardPlayerInputClientRpc(playerId, value, clientTimeTick);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestFullStateUpdateServerRpc(ServerRpcParams serverRpcParams = default)
        {
            VerboseLog("Received request for full state update");

            ClientRpcParams clientRpcParams = new()
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { serverRpcParams.Receive.SenderClientId }
                }
            };

            // To avoid problems later with applying diffs, go back to the last time we would've sent out a
            // frame update normally.
            int requestedGameTick = realGameTick - (realGameTick % sendStateEveryNFrames);

            ProcessFullStateUpdateClientRpc(stateBuffer[requestedGameTick], gameEventsBuffer, clientRpcParams);
        }

        #endregion Server-side only code

        #region Client-side only code

        private void ClientFixedUpdate()
        {
            RunScheduledStateReplay();

            if (realGameTick > lastAuthoritativeTick + (sendStateEveryNFrames * 4)) // TODO: explicitly configurable, instead of implicitly
            {
                // TODO: is this the best thing we can do here?
                VerboseLog("Client is too far ahead of the last server frame, so pause until we get caught up.");
                return;
            }

            VerboseLog("Normal frame prediction");

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

        [ClientRpc]
        private void ForwardPlayerInputClientRpc(byte playerId, PlayerInputDTO value, int clientTimeTick)
        {
            if (!isRunning)
            {
                // RPC's can arrive before this component has started, so skip out if it's too early
                VerboseLog("Client input for player id " + playerId + " has arrived before we've started, so ignoring");
                return;
            }

            if (IsHost)
            {
                // We've already applied this to ourselves if we're the host
                return;
            }

            // TODO: optimization: it seems like there should be some way to say "this is just the reflection
            //       of our own input, and we're authoritative for our own input, so we can ignore this
            //       and/or don't even send it to us in the first place."

            // If this happened before our last authoritative tick, we can safely ignore it
            if (clientTimeTick < lastAuthoritativeTick)
            {
                VerboseLog("Client input for player id " + playerId + " arrived from before our last authoritative tick, so ignoring");
                return;
            }

            VerboseLog("Replaying due to input for player id " + playerId + " at client time " + clientTimeTick);

            // Set the input in our buffer
            SetPlayerInputAtTickAndPredictForward(playerId, value.input, clientTimeTick);

            ScheduleStateReplay(clientTimeTick);
        }

        [ClientRpc]
        private void SyncGameEventsToClientsClientRpc(int serverTimeTick, GameEventsBuffer newGameEventsBuffer)
        {
            if(IsHost)
            {
                return;
            }

            VerboseLog("Updating upcoming game events, taking effect on tick " + serverTimeTick);
            ScheduleGameEventsSwap(newGameEventsBuffer);

            ScheduleStateReplay(serverTimeTick);
        }

        [ClientRpc]
        private void StartGameClientRpc(StateFrameDTO initialStateFrameDelta, int _randomSeedBase)
        {
            if (IsHost)
            {
                return;
            }

            VerboseLog("Initial game state received from server.  Diff size:" + initialStateFrameDelta._gameStateDiffBytes.Length);

            // Store the state
            StateFrameDTO initialFrame = new()
            {
                gameTick = 0,
            };
            initialFrame.ApplyDelta(initialStateFrameDelta);

            stateBuffer[0] = initialFrame;

            // Set our 'now' to (server tick + estimated lag)
            lastAuthoritativeTick = 0;

            randomSeedBase = _randomSeedBase;

            // Start things off!
            isRunning = true;
            ResyncWithServer(initialFrame);
        }

        [ClientRpc]
        private void UpdateGameStateClientRpc(StateFrameDTO serverGameStateDelta, GameEventsBuffer newGameEventsBuffer)
        {
            if (!isRunning)
            {
                // RPC's can arrive before this component has started, so skip out if it's too early
                // TODO: is this the right thing to do?  Seems like maybe no?
                VerboseLog("Server game state delta arrived before we've started, so skipping");
                return;
            }

            if (IsHost)
            {
                return;
            }

            VerboseLog("Server state received.  Server time:" + serverGameStateDelta.gameTick);

            // Did the state arrive out of order?  If so, panic.
            if (serverGameStateDelta.gameTick != (lastAuthoritativeTick + sendStateEveryNFrames))
            {
                VerboseLog(
                    "xxx Server snapshot arrived out of order!  Requesting full state refresh.  Server state tick: " + serverGameStateDelta.gameTick +
                    " expected: " + lastAuthoritativeTick + " + " + sendStateEveryNFrames + " = " + (lastAuthoritativeTick + sendStateEveryNFrames)
                );

                // We can't just process this as-is since we need that prior frame in order to properly apply the delta.
                // As such, request a full state sync instead of the delta
                RequestFullStateUpdateServerRpc();
                return;
            }

            // Is the server too far in the future or past?  If so, get back in sync.
            if (Math.Abs(realGameTick - serverGameStateDelta.gameTick) > (sendStateEveryNFrames * 2))
            {
                // TODO: decide to handle a hacker that sends forged server state from out-of-tolerance timestamps.
                Debug.LogWarning("xxx Received server state is greatly out of tolerance.  Client may experience slowdown, jumping, or other bad behavior.");
                RequestFullStateUpdateServerRpc();
                return;
            }

            // Schedule the scheduled events swap
            ScheduleGameEventsSwap(newGameEventsBuffer);

            // Reconstitute the state from our delta
            StateFrameDTO serverGameState;
            try
            {
                serverGameState = stateBuffer[serverGameStateDelta.gameTick - sendStateEveryNFrames].Duplicate();
                VerboseLog("Applying delta against frame " + (serverGameStateDelta.gameTick - sendStateEveryNFrames));
                serverGameState.ApplyDelta(serverGameStateDelta);
            } catch (Exception e)
            {
                VerboseLog("xxx Something went wrong when reconstituting game state from diff: " + e.Message);
                RequestFullStateUpdateServerRpc();
                return;
            }

            lastAuthoritativeTick = serverGameState.gameTick;
            ResyncWithServer(serverGameState);
        }

        [ClientRpc]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Needed for server to send to specific clients")]
        private void ProcessFullStateUpdateClientRpc(StateFrameDTO serverGameState, GameEventsBuffer serverGameEventsBuffer, ClientRpcParams clientRpcParams = default)
        {
            VerboseLog("xxx Received full state update from server");

            // Set the time to match the server (so that replays won't be dropped)
            gameTick = serverGameState.gameTick;
            realGameTick = serverGameState.gameTick;

            // Get us back in sync
            ScheduleGameEventsSwap(serverGameEventsBuffer);
            ResyncWithServer(serverGameState);
        }

        private void ResyncWithServer(StateFrameDTO serverState)
        {
            int serverTick = serverState.gameTick;

            VerboseLog("Resync with server, server tick is " + serverTick);

            // Apply the delta to our history at the server's timestamp
            stateBuffer[serverTick] = serverState.Duplicate();

            ScheduleStateReplay(serverTick);
        }

        #endregion Client-side only code

        #region Core simulation functionality

        private void ReplayFrames(int startTick, int targetTick)
        {
            VerboseLog("Replaying frames " + startTick + " until " + targetTick + " (non-inclusive)");

            isReplaying = true;

            // Get our state matched to the state the server had at the frame we received
            if (startTick > realGameTick)
            {
                // "client is lagging behind, and needs to catch up"
                VerboseLog("Client needs to catch up to server, so fast-forward to server time");

                // Fast-forward, so that we trigger any relevant events
                LoadPendingEventsBuffer();
                SimulateFrames(realGameTick, startTick - 1);   // TODO: add a flag that's like "skip any frames without events", maybe?
            }
            else
            {
                // "normal operation, needs rewind and replay"
                VerboseLog("Client is ahead of server frame (the normal case), so rewind, apply, and replay to now");

                RewindTime(startTick);
                LoadPendingEventsBuffer();
            }

            // Go forward until we get to the synchronized "now"
            ApplyFullStateFrame(stateBuffer[Math.Max(0, startTick)]);
            SimulateFrames(startTick + 1, targetTick);
            realGameTick = targetTick;
            gameTick = realGameTick;

            isReplaying = false;
        }

        private void ScheduleGameEventsSwap(GameEventsBuffer newGameEventsBuffer)
        {
            // Game events need to be swapped in like this because we first need to roll back against the client's view of the events list
            // before we then play back events according to what the server's view of the events list is.

            _pendingGameEventsBuffer = newGameEventsBuffer; // TODO: do we need to make a copy of this?  Is there an immutable version we can use instead?
            _hasPendingGameEventsBuffer = true;
        }

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

        private void SimulateFrames(int startTick, int endTick)
        {
            if( startTick < 0)
            {
                startTick = 0;
            }

            if( endTick <= startTick )
            {
                return;
            }

            VerboseLog("Running frames from " + startTick + " to " + endTick + " (non-inclusive)");

            // For each tick from then to now...
            // Go through the rest of the frames, advancing physics and updating the buffer to match the revised history
            gameTick = startTick;

            while (gameTick < endTick)
            {
                VerboseLog("Replaying for tick " + gameTick);

                if (gameTick <= 0)
                {
                    VerboseLog("Replaying tick " + gameTick + ", which just means 'reset to frame 0'");

                    // To "replay" frame 0 means to just reset the world to that state.  There's no actual simulation in frame 0.
                    ApplyFullStateFrame(stateBuffer[0]);

                    gameTick++;
                    continue;
                }

                // Get the new frame and put it in place
                stateBuffer[gameTick] = RunSingleGameFrame(gameTick, stateBuffer[gameTick].PlayerInputs, gameEventsBuffer[gameTick]);

                gameTick++;
            }
        }

        private void RunScheduledStateReplay()
        {
            VerboseLog("Checking scheduled state replay.  Currently " + replayFromTick);

            if (replayFromTick < 0)
            {
                LoadPendingEventsBuffer();
                return;
            }

            int targetTick = replayFromTick + GetEstimatedLag();

            ReplayFrames(replayFromTick, targetTick);
            realGameTick = targetTick;
            gameTick = realGameTick;

            // Reset to "nothing scheduled"
            replayFromTick = -1;
        }

        // TODO: I think player inputs need a way to distinguish between "this frame's remote inputs came from the server, and therefore don't need to be predicted"
        //       and "this frame's remote inputs were predicted before, and need to be re-predicted now".  Because we may have server-authoritative inputs
        //       for some of the frames that are being replayed when we're replaying them.

        private void RewindTime(int rewindToTick)
        {
            if(rewindToTick < 0)
            {
                rewindToTick = 0;
            }

            VerboseLog("Rewinding time");

            for (gameTick = realGameTick - 1; gameTick >= rewindToTick; gameTick--)
            {
                // TODO: We can probably skip restoring gamestate at all whenever there are no events inside of a frame to roll back

                VerboseLog("Undoing events at tick " + gameTick + " (setting state to the moment before the events were originally run)");
                ApplyState(stateBuffer[Math.Max(0, gameTick - 1)].GameState);

                RollbackEvents(gameEventsBuffer[gameTick], stateBuffer[gameTick].GameState);
            }
            VerboseLog("Done rewinding");
        }

        private void ApplyFullStateFrame(StateFrameDTO stateFrame)
        {
            VerboseLog("Applying full state frame");

            ResetRandom(stateFrame.gameTick);

            ApplyEvents(gameEventsBuffer[stateFrame.gameTick]);
            ApplyInputs(stateFrame.PlayerInputs);
            ApplyPhysicsState(stateFrame.PhysicsState);
            ApplyState(stateFrame.GameState);
        }

        private void ResetRandom(int tick)
        {
            Random.InitState(randomSeedBase + tick);
        }

        private void LoadPendingEventsBuffer()
        {
            if (_hasPendingGameEventsBuffer)
            {
                VerboseLog("Replacing local view of events with server's view of events");
                gameEventsBuffer = _pendingGameEventsBuffer;
                _hasPendingGameEventsBuffer = false;
            }
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
            newFrame.GameState = newGameState;
            newFrame.PhysicsState.TakeSnapshot(GetNetworkedRigidbodies());

            return newFrame;
        }

        private void ScheduleStateReplay(int startTick)
        {
            // Don't worry about replaying something from the future
            if (startTick > realGameTick)
            {
                VerboseLog("Ignoring replay requested for future or present tick " + startTick);
                return;
            }

            VerboseLog("Replay requested for tick " + startTick);

            // we want to replay from the earliest time requested
            // (with a negative value of replayFromTick meaning "nothing's requested for this frame yet")
            if (replayFromTick < 0 || startTick < replayFromTick)
            {
                replayFromTick = startTick;
            }
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