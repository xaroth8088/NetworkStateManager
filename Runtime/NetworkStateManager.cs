using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
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
        /*
            TODO: Possible bandwidth optimization ideas:
             * When an individual field hasn't changed from the previous frame, don't serialize it (also may be fun for replaying to handle all edge cases)
                 * Maybe something like take in the previous frame to compare against and create a bitmap of which fields have changed.  Use this
                   bitmap to decide which fields can safely be skipped when sending to the clients.  Then, on the client side, reconstitute
                   the full state by taking in the previous frame and copying over values that didn't change.
             * Compression of data to be sent across the wire?  Is this already done automatically?
        */

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

        #endregion NetworkStateManager configuration

        #region Runtime state
        [Header("Runtime state")]
        private int realGameTick = 0;   // This is the internal game tick, which keeps track of "now"

        public int gameTick = 0;    // Users of the library will get the tick associated with whatever frame is currently being processed, which might include frames that are being replayed

        public bool isReplaying = false;
        public int lastAuthoritativeTick = 0;
        public StateBuffer stateBuffer;

        [SerializeField]
        private bool isRunning = false;

        [SerializeField]
        private int replayFromTick = -1;    // anything negative is a flag value, meaning "don't replay anything"...TODO: make this an explicit bool instead of the flag value

        public NetworkIdManager networkIdManager = new();

        #endregion Runtime state

        #region Lifecycle event delegates and wrappers

        /// <summary>
        /// Delegate declaration for the OnApplyEvents event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnApplyEvents"/>
        /// </summary>
        /// <param name="events">A list of game events to apply in the current frame.  Remember to cast back to the event type you started NetworkStateManager with!</param>
        public delegate void ApplyEventsDelegateHandler(List<IGameEvent> events);

        /// <summary>
        /// Delegate declaration for the OnRollbackEvents event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnRollbackEvents"/>
        /// </summary>
        /// <param name="events">A list of game events to roll back.  Remember to cast back to the event type you started NetworkStateManager with!</param>
        public delegate void RollbackEventsDelegateHandler(List<IGameEvent> events);

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
        /// Delegate declaration for the OnPostPhysicsFrameUpdateDelegateHandler event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnPostPhysicsFrameUpdate"/>
        /// </summary>
        public delegate void OnPostPhysicsFrameUpdateDelegateHandler();

        /// <summary>
        /// Delegate declaration for the OnPrePhysicsFrameUpdateDelegateHandler event.<br/>
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

        private void ApplyEvents(List<IGameEvent> events)
        {
            int count = events.Count;
            
            if(count == 0)
            {
                return;
            }

            VerboseLog("Applying " + count + " events");

            OnApplyEvents?.Invoke(events);
        }

        private void RollbackEvents(List<IGameEvent> events)
        {
            int count = events.Count;

            if (count == 0)
            {
                return;
            }

            VerboseLog("Rolling back " + events.Count + " events");
            OnRollbackEvents?.Invoke(events);
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
        /// <param name="tick">The game tick when the event should fire.  Leave empty to fire on the next game tick.</param>
        public void ScheduleGameEvent(IGameEvent gameEvent, int tick = -1)
        {
            if (!NetworkManager.Singleton.IsHost)
            {
                return;
            }

            if (isReplaying)
            {
                return;
            }

            if (tick == -1)
            {
                tick = realGameTick + 1;
            }

            if (tick <= realGameTick)
            {
                Debug.LogWarning("Game event scheduled for the past - will not be replayed on clients");
            }

            VerboseLog("Game event scheduled for tick " + tick);

            // Let everyone know that an event is happening
            SendGameEventToClientsClientRpc(tick, new GameEventDTO
            {
                gameEvent = gameEvent
            });
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

            SetupInitialNetworkIds();
            stateBuffer = new StateBuffer();

            isRunning = false;
            isReplaying = false;
            lastAuthoritativeTick = 0;
            gameTick = 0;
            realGameTick = 0;

            if (!NetworkManager.IsHost)
            {
                return;
            }

            // Server-only from here down
            isRunning = true;

            // If we don't explicitly seed UnityEngine.Random, then it won't have a reproducible state until after
            // the first random number is requested of it.
            Random.InitState(new System.Random().Next(int.MinValue, int.MaxValue));

            // Capture the initial game state
            StateFrameDTO newFrame = new()
            {
                gameTick = 0,
            };
            newFrame.randomState.State = Random.state;
            GetGameState(ref newFrame.gameState);
            newFrame.PhysicsState = new PhysicsStateDTO();
            newFrame.PhysicsState.TakeSnapshot(GetNetworkedRigidbodies());

            stateBuffer[0] = newFrame;

            // Ensure clients are starting from the same view of the world
            StartGameClientRpc(stateBuffer[0]);
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
                VerboseLog("DEBUG: rolling back " + debugNumFramesToRollback + " frames");
                ScheduleStateReplay((int)(realGameTick - debugNumFramesToRollback));
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
                    ForwardPlayerInputClientRpc(entry.Key, playerInputDTO, realGameTick);
                }
            }

            // Actually simulate the frame
            stateBuffer[realGameTick] = RunSingleGameFrame(realGameTick, playerInputs, stateBuffer[realGameTick].Events);

            // (Maybe) send the new state to the clients for reconciliation
            if (realGameTick % sendStateEveryNFrames == 0)
            {
                VerboseLog("Sending delta - base frame comes from tick " + (realGameTick - sendStateEveryNFrames));

                // TODO: there's an opportunity to be slightly more aggressive by skipping sending anything if the entire
                //       state frame is exactly the same (except for the realGameTick, of course).
                StateFrameDTO delta = stateBuffer[realGameTick - sendStateEveryNFrames].GenerateDelta(stateBuffer[realGameTick]);

                UpdateGameStateClientRpc(delta);
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
                clientTimeTick = realGameTick;
            }

            // Set the input in our buffer
            SetPlayerInputAtTickAndPredictForward(playerId, value.input, clientTimeTick);

            // Schedule a replay from this point
            VerboseLog("Input received.  Will replay from: " + clientTimeTick + " to our time");
            ScheduleStateReplay(clientTimeTick);

            // Forward the input to all clients so they can do the same
            ForwardPlayerInputClientRpc(playerId, value, clientTimeTick);
        }

        #endregion Server-side only code

        #region Client-side only code

        private void ClientFixedUpdate()
        {
            RunScheduledStateReplay();

            if (realGameTick > lastAuthoritativeTick + (sendStateEveryNFrames * 2))
            {
                // TODO: is this the best thing we can do here?
                VerboseLog("Client is too far ahead of the last server frame, so pause until we get caught up.");
                return;
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
            VerboseLog("Normal frame prediction");

            stateBuffer[realGameTick] = RunSingleGameFrame(realGameTick, playerInputs, stateBuffer[realGameTick].Events);

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

            // Schedule a replay from this point
            ScheduleStateReplay(clientTimeTick);
        }

        [ClientRpc]
        private void SendGameEventToClientsClientRpc(int serverTimeTick, GameEventDTO gameEventDTO)
        {
            VerboseLog("Received and scheduling game event for tick " + serverTimeTick);

            stateBuffer[serverTimeTick].Events.Add(gameEventDTO.gameEvent);

            ScheduleStateReplay(serverTimeTick);
        }

        [ClientRpc]
        private void StartGameClientRpc(StateFrameDTO serverGameState)
        {
            if (NetworkManager.IsHost)
            {
                return;
            }

            VerboseLog("Initial game state received from server");

            // Store the state
            stateBuffer[0] = serverGameState;

            // Set our 'now' to (server tick + estimated lag)
            lastAuthoritativeTick = 0;

            // Start things off!
            isRunning = true;
            ScheduleStateReplay(0);
        }

        [ClientRpc]
        private void UpdateGameStateClientRpc(StateFrameDTO serverGameStateDelta)
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
            if ( serverGameStateDelta.gameTick != lastAuthoritativeTick + sendStateEveryNFrames)
            {
                // TODO: decide what to do about this case, since we need that prior frame in order to properly apply the delta
                // Probably, make an RPC to request a full state sync instead of the delta
                throw new Exception("Server snapshot arrived out of order!  Game state will be indeterminate from here on out.");
            }

            // Is the server too far in the future or past?  If so, log a message.
            if (Math.Abs(realGameTick - serverGameStateDelta.gameTick) > (sendStateEveryNFrames * 2))
            {
                // TODO: decide to handle this, esp. in light of a hacker that sends forged server state from out-of-tolerance timestamps.
                Debug.Log("Received server state is greatly out of tolerance.  Client may experience slowdown or jumping.");
            }

            // Reconstitute the state from our delta
            StateFrameDTO serverGameState = stateBuffer[serverGameStateDelta.gameTick - sendStateEveryNFrames].Duplicate();
            serverGameState.ApplyDelta(serverGameStateDelta);

            // Apply the delta to our history at the server's timestamp
            lastAuthoritativeTick = serverGameState.gameTick;
            stateBuffer[serverGameState.gameTick] = serverGameState;

            // TODO: if we're fast-forwarding here, do we need to run any scheduled events that we know about between then and now?

            // Set our 'now' to (server tick + estimated lag)
            int framesOfLag = NetworkManager.LocalTime.Tick - NetworkManager.ServerTime.Tick;
            realGameTick = serverGameState.gameTick + framesOfLag;

            // Schedule a replay for (server tick)
            ScheduleStateReplay(serverGameState.gameTick);
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

        private void ReplayHistoryFromTick(int tickToReplayFrom)
        {
            // For each tick from then to now...
            // Go through the rest of the frames, advancing physics and updating the buffer to match the revised history
            int tick = tickToReplayFrom;

            if (tick > realGameTick)
            {
                return;
            }

            VerboseLog("Replaying history from " + tick);

            while (tick < realGameTick)
            {
                VerboseLog("Replaying for tick " + tick);

                if (tick <= 0)
                {
                    VerboseLog("Replaying tick " + tick + ", which just means 'reset to frame 0'");
                    // To "replay" frame 0 means to just reset the world to that state.  There's no actual simulation in frame 0.
                    StateFrameDTO frameZero = stateBuffer[0];

                    ApplyPhysicsState(frameZero.PhysicsState);
                    Random.state = frameZero.randomState.State;
                    ApplyState(frameZero.gameState);
                    ApplyInputs(frameZero.PlayerInputs);
                    ApplyEvents(frameZero.Events);
                    tick++;
                    continue;
                }

                // Get the new frame and put it in place
                StateFrameDTO stateFrame = RunSingleGameFrame(tick, stateBuffer[tick].PlayerInputs, stateBuffer[tick].Events);
                stateBuffer[tick] = stateFrame;

                tick++;
            }
        }

        private void RunScheduledStateReplay()
        {
            VerboseLog("Checking scheduled state replay.  Currently " + replayFromTick);
            // TODO: it seems like when we roll things back (esp. on the server), any events that were scheduled
            //       during a replayed frame should _also_ be rolled back.  But then, how to sync those with clients
            //       again?  What if the events scheduled during the replay don't change?
            if (replayFromTick < 0)
            {
                return;
            }

            // TODO: should we also do something about replaying events from the replay time,
            // regardless of what the last authoritative state's timestamp was?
            // I'm thinking about animations that may need to be synchronized in time,
            // but which wouldn't be able to have that done if it was already in progress
            // when this replay happens.

            // Replay from either the requested frame OR the last server-authoritative state, whichever's more recent
            int frameToActuallyReplayFrom = Math.Max(0, Math.Max(replayFromTick, lastAuthoritativeTick));

            // Don't replay from the future
            if (frameToActuallyReplayFrom >= realGameTick)
            {
                VerboseLog("Was scheduled to replay at " + replayFromTick + ", but this is in the future so skipping replay");
                return;
            }

            // Rewind time to that point in the buffer
            VerboseLog("######################### REPLAY #########################");
            VerboseLog("Beginning scheduled replay from frame " + frameToActuallyReplayFrom);

            VerboseLog("Rewinding events");
            for (int tick = realGameTick - 1; tick >= 0 && tick >= frameToActuallyReplayFrom; tick--)
            {
                VerboseLog("Undoing events at tick " + tick);

                gameTick = tick;
                RollbackEvents(stateBuffer[tick].Events);
            }

            // Set the state to the requested frame
            VerboseLog("Resetting state to start of tick " + frameToActuallyReplayFrom);
            StateFrameDTO frameToReplayFrom = stateBuffer[frameToActuallyReplayFrom];

            ApplyInputs(frameToReplayFrom.PlayerInputs);
            ApplyPhysicsState(frameToReplayFrom.PhysicsState);
            Random.state = frameToReplayFrom.randomState.State;
            ApplyState(frameToReplayFrom.gameState);
            ApplyEvents(frameToReplayFrom.Events);

            // Replay history until we're back to 'now'
            isReplaying = true;
            ReplayHistoryFromTick(frameToActuallyReplayFrom + 1);

            // Reset to "nothing scheduled"
            replayFromTick = -1;
            isReplaying = false;
            gameTick = realGameTick;

            VerboseLog("######################### END REPLAY #########################");
        }

        private StateFrameDTO RunSingleGameFrame(int tick, Dictionary<byte, IPlayerInput> playerInputs, List<IGameEvent> events)
        {
            VerboseLog("Running single frame for tick " + tick);
            gameTick = tick;

            StateFrameDTO newFrame = new()
            {
                PlayerInputs = playerInputs,
                Events = events,
                gameTick = tick,
                PhysicsState = new PhysicsStateDTO()
            };

            // Simulate the frame
            ApplyInputs(playerInputs);
            Physics.SyncTransforms();
            PrePhysicsFrameUpdate();
            SimulatePhysics();
            PostPhysicsFrameUpdate();

            // Capture the state from the scene/game
            newFrame.randomState.State = Random.state;
            GetGameState(ref newFrame.gameState);
            newFrame.PhysicsState.TakeSnapshot(GetNetworkedRigidbodies());

            // Events come last
            ApplyEvents(events);

            return newFrame;
        }

        private void ScheduleStateReplay(int tick)
        {
            // Don't worry about replaying something from the future
            if (tick > realGameTick)
            {
                VerboseLog("Ignoring replay requested for future or present tick " + tick);
                return;
            }

            VerboseLog("Replay requested for tick " + tick);

            // we want to replay from the earliest time requested
            // (with a negative value of replayFromTick meaning "nothing's requested for this frame yet")
            if (replayFromTick < 0 || tick < replayFromTick)
            {
                replayFromTick = tick;
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

            if (IsHost)
            {
                HostFixedUpdate();
            }
            else if (IsClient)
            {
                ClientFixedUpdate();
            }
        }

        private void VerboseLog(string message)
        {
#if UNITY_EDITOR
            // TODO: abstract this into its own thing, use everywhere
            if (!verboseLogging)
            {
                return;
            }

            string log = realGameTick + ": ";
            if (isReplaying)
            {
                log += "**REPLAY** ";
            }
            log += message;

            Debug.Log(log);
#endif
        }
    }
}