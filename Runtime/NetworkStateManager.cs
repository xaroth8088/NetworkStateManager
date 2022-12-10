using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

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

        public bool verboseLogging = false;

        public int gameTick = 0;

        public int lastAuthoritativeTick = 0;

        // Multiply these number by Time.fixedDeltaTime (20ms/frame) to know how much
        // lag we'll permit beyond what Unity's networking system thinks the lag is.
        public int maxPastTolerance = 5;

        public int sendStateEveryNFrames = 10;

        public StateBuffer stateBuffer;

        [SerializeField]
        private bool isRunning = false;

        [SerializeField]
        private byte networkIdCounter = 0;

        private readonly Dictionary<byte, GameObject> networkIdGameObjectCache = new();

        [SerializeField]
        private int replayFromTick = -1;    // anything negative is a flag value, meaning "don't replay anything"...TODO: make this an explicit bool instead of the flag value

        private readonly List<Rigidbody> rigidbodies = new();

        /// <summary>
        /// Delegate declaration for the OnApplyEvents event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnApplyEvents"/>
        /// </summary>
        /// <param name="state">An object containing all the information required to apply the events this frame to your game.  This must be the same type as what you started NetworkStateManager with.</param>
        public delegate void ApplyEventsDelegateHandler(List<IGameEvent> events);

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
        /// Delegate declaration for the OnGetInputs event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnGetInputs"/>
        /// </summary>
        public delegate void OnGetInputsDelegateHandler(ref Dictionary<byte, IPlayerInput> playerInputs);

        /// <summary>
        /// Delegate declaration for the OnGetGameState event.<br/>
        /// See also: <br/>
        /// <seealso cref="OnGetGameState"/>
        /// </summary>
        public delegate void OnGetGameStateDelegateHandler(ref IGameState state);

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
        /// frame.
        /// <br/>
        /// See also: <br/>
        /// <seealso cref="OnPostPhysicsFrameUpdateDelegateHandler"/>
        /// </summary>
        public event OnPrePhysicsFrameUpdateDelegateHandler OnPostPhysicsFrameUpdate;

        /// <summary>
        /// This event fires each frame, before the physics engine is run for this
        /// frame.
        /// <br/>
        /// See also: <br/>
        /// <seealso cref="OnPrePhysicsFrameUpdateDelegateHandler"/>
        /// </summary>
        public event OnPrePhysicsFrameUpdateDelegateHandler OnPrePhysicsFrameUpdate;

        public void DeregisterRigidbody(GameObject gameObject)
        {
            rigidbodies.Remove(gameObject.GetComponent<Rigidbody>());
        }

        public Dictionary<byte, GameObject>.ValueCollection GetAllNetworkIdGameObjects()
        {
            return networkIdGameObjectCache.Values;
        }

        public GameObject GetGameObjectByNetworkId(byte networkId)
        {
            return networkIdGameObjectCache[networkId];
        }

        public void RegisterNewRigidbody(GameObject gameObject)
        {
            Rigidbody rigidbody = gameObject.GetComponent<Rigidbody>();
            if (rigidbody)
            {
                rigidbodies.Add(rigidbody);
            }
        }

        public void ReplaceObjectWithNetworkId(byte networkId, GameObject gameObject)
        {
            networkIdGameObjectCache[networkId] = gameObject;
        }

        public byte RequestAndApplyNetworkId(GameObject gameObject)
        {
            NetworkId networkId = gameObject.GetComponent<NetworkId>();
            if (networkId == null)
            {
                throw new Exception("Game object passed to request a network id doesn't have that component on it.");
            }

            if (networkIdCounter == 255)
            {
                throw new Exception("Out of network ids!");
            }

            networkIdCounter++; // Yes, this means that 0 can't be used, but that's ok - we need it as a flag to mean "hasn't been assigned one yet"

            networkIdGameObjectCache[networkIdCounter] = gameObject;
            networkId.networkId = networkIdCounter;

            return networkIdCounter;
        }

        public void ScheduleGameEvent(IGameEvent gameEvent, int tick = -1)
        {
            if (tick == -1)
            {
                tick = gameTick;
            }

            stateBuffer[tick].Events.Append(gameEvent);
            GameEventDTO gameEventDTO = new GameEventDTO();
            gameEventDTO.gameEvent = gameEvent;

            SendGameEventToClientsClientRpc(tick, gameEventDTO);
        }

        public void ScheduleStateReplay(int tick)
        {
            // Don't worry about replaying something from the future
            if (tick >= gameTick)
            {
                return;
            }

            // we want to replay from the earliest time requested
            // (with a negative value of replayFromTick meaning "nothing's requested for this frame yet")
            if (replayFromTick < 0 || tick < replayFromTick)
            {
                replayFromTick = tick;
            }
        }

        // TODO: prevent a client from sending input for a player they shouldn't be sending input for
        [ServerRpc(RequireOwnership = false)]
        private void SetInputServerRpc(byte playerId, PlayerInputDTO value, int clientTimeTick)
        {
            if (!isRunning)
            {
                // RPC's can arrive before this component has run Start(), so skip out if it's too early
                return;
            }

            if (clientTimeTick > gameTick)
            {
                // The server slowed down enough for the clients to get ahead of it.  For small deltas,
                // this isn't usually an issue.
                // TODO: figure out a strategy for inputs that have far-future inputs
                // TODO: figure out a strategy for detecting cheating that's happening (vs. normal slowdowns)
                // For now, just apply to the current timestamp.
                // TODO: NOTE: this could cause issues for client-side prediction, esp. if clients are
                //             filtering out their own inputs.
                clientTimeTick = gameTick;
            }

            // Set the input in our buffer
            SetPlayerInputAtTickAndPredictForward(playerId, value.input, clientTimeTick);

            // Schedule a replay from this point
            if (verboseLogging)
            {
                Debug.Log("Input received.  Will replay from: " + clientTimeTick + " to our time: " + gameTick);
            }
            ScheduleStateReplay(clientTimeTick);

            // Forward the input to all clients so they can do the same
            ForwardPlayerInputClientRpc(playerId, value, clientTimeTick);
        }

        public void StartNetworkStateManager(Type gameStateType, Type playerInputType, Type gameEventType)
        {
            TypeStore.Instance.GameStateType = gameStateType;
            TypeStore.Instance.PlayerInputType = playerInputType;
            TypeStore.Instance.GameEventType = gameEventType;

            SetupInitialNetworkIds();
            isRunning = true;
            stateBuffer = new StateBuffer();
        }

        private StateFrameDTO RunSingleGameFrame(int tick, Dictionary<byte, IPlayerInput> playerInputs, List<IGameEvent> events)
        {
            StateFrameDTO newFrame = new();
            newFrame.PlayerInputs = playerInputs;
            newFrame.Events = events;

            // Simulate the frame
            OnApplyInputs?.Invoke(playerInputs);
            OnApplyEvents?.Invoke(events);
            OnPrePhysicsFrameUpdate?.Invoke();
            Physics.Simulate(Time.fixedDeltaTime);
            OnPostPhysicsFrameUpdate?.Invoke();

            // Capture the state from the scene/game
            newFrame.gameTick = tick;
            OnGetGameState?.Invoke(ref newFrame.gameState);
            newFrame.PhysicsState = new PhysicsStateDTO();
            newFrame.PhysicsState.TakeSnapshot(rigidbodies);

            return newFrame;
        }

        private void ApplyPhysicsState(PhysicsStateDTO physicsState)
        {
            if (physicsState == null)
            {
                // No rigidbodies in the state yet
                return;
            }

            // Set each object into the world
            foreach (KeyValuePair<byte, RigidBodyStateDTO> item in physicsState.RigidBodyStates)
            {
                GameObject gameObject;
                try
                {
                    gameObject = GetGameObjectByNetworkId(item.Value.networkId);
                }
                catch (KeyNotFoundException)
                {
                    Debug.Log("Skipping network object id: " + item.Value.networkId);

                    // The network object simply hasn't spawned yet, so ignore it for now.
                    continue;
                }

                item.Value.ApplyState(gameObject);
            }
        }

        private void Awake()
        {
            Physics.autoSimulation = false;
        }

        private void ClientFixedUpdate()
        {
            RunScheduledStateReplay();

            if (gameTick > lastAuthoritativeTick + (sendStateEveryNFrames * 2))
            {
                // TODO: is this the best thing we can do here?
                if (verboseLogging)
                {
                    Debug.Log("Client is too far ahead of the last server frame, so pause until we get caught up.");
                }
                return;
            }

            gameTick++;

            Dictionary<byte, IPlayerInput> playerInputs = PredictInputs(stateBuffer[gameTick - 1].PlayerInputs);
            // Gather the local inputs separately, since we'll need to forward these to the server via RPC
            Dictionary<byte, IPlayerInput> localInputs = new();
            OnGetInputs?.Invoke(ref localInputs);
            foreach (KeyValuePair<byte, IPlayerInput> entry in localInputs)
            {
                playerInputs[entry.Key] = entry.Value;
            }

            // Actually simulate the frame (this is the client-side "prediction" of what'll happen)
            if (verboseLogging)
            {
                Debug.Log("Normal frame prediction for tick:" + gameTick);
            }
            stateBuffer[gameTick] = RunSingleGameFrame(gameTick, playerInputs, stateBuffer[gameTick].Events);

            // Send our inputs to the server, if needed
            // TODO: consolidate these into a single RPC call, instead of sending one per player
            foreach (KeyValuePair<byte, IPlayerInput> entry in localInputs)
            {
                Type playerInputType = TypeStore.Instance.PlayerInputType;
                IPlayerInput defaultInput = (IPlayerInput)Activator.CreateInstance(playerInputType);

                IPlayerInput predictedInput = PredictInput(stateBuffer[gameTick - 1].PlayerInputs.GetValueOrDefault(entry.Key, defaultInput));

                // If it's the same as the predicted tick, then don't bother sending
                if (predictedInput.Equals(entry.Value))
                {
                    continue;
                }

                PlayerInputDTO playerInputDTO = new();
                playerInputDTO.input = entry.Value;

                SetInputServerRpc(entry.Key, playerInputDTO, gameTick);
            }
        }

        private void FixedUpdate()
        {
            if (!isRunning)
            {
                return;
            }

            if (IsHost)
            {
                HostFixedUpdate();
            }
            else if (IsClient)
            {
                ClientFixedUpdate();
            }
        }

        [ClientRpc]
        private void ForwardPlayerInputClientRpc(byte playerId, PlayerInputDTO value, int clientTimeTick)
        {
            if (!isRunning)
            {
                // RPC's can arrive before this component has run Start(), so skip out if it's too early
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
                return;
            }

            // Set the input in our buffer
            SetPlayerInputAtTickAndPredictForward(playerId, value.input, clientTimeTick);

            // Schedule a replay from this point
            if (verboseLogging)
            {
                Debug.Log("Replaying due to input.  Client time:" + clientTimeTick + " Our time:" + gameTick);
            }
            ScheduleStateReplay(clientTimeTick);
        }

        private Dictionary<byte, IPlayerInput> PredictInputs(Dictionary<byte, IPlayerInput> lastKnownPlayerInputs)
        {
            Dictionary<byte, IPlayerInput> newInputs = new();

            foreach (KeyValuePair<byte, IPlayerInput> entry in lastKnownPlayerInputs)
            {
                newInputs[entry.Key] = PredictInput(entry.Value);
            }

            return newInputs;
        }

        private IPlayerInput PredictInput(IPlayerInput lastKnownPlayerInput)
        {
            // For now, do a simple "the next frame will always be the same as the previous frame".
            // TODO: an event callback to predict the next input
            return lastKnownPlayerInput;
        }

        private void ReplayHistoryFromTick(int tickToReplayFrom)
        {
            // For each tick from then to now... (Skipping the oldest frame since we're not altering that frame of history)
            // Go through the rest of the frames, advancing physics and updating the buffer to match the revised history
            int tick = tickToReplayFrom + 1;
            while (tick <= gameTick)
            {
                if (verboseLogging)
                {
                    Debug.Log("Replaying for tick:" + tick);
                }

                // Get the new frame and put it in place
                StateFrameDTO stateFrame = RunSingleGameFrame(tick, stateBuffer[tick].PlayerInputs, stateBuffer[tick].Events);
                stateBuffer[tick] = stateFrame;

                tick++;
            }
        }

        private void RunScheduledStateReplay()
        {
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
            // Also, guard against underflow
            int frameToActuallyReplayFrom = Math.Max(0, Math.Max(replayFromTick, lastAuthoritativeTick));
            if (frameToActuallyReplayFrom > gameTick)
            {
                // NOTE: this isn't >= gameTick because sometimes the server's authoritative state can arrive for the
                //       frame that the client just happens to be on, and we'll need to set the state accordingly
                return;
            }

            // Rewind time to that point in the buffer
            if (verboseLogging)
            {
                Debug.Log("Beginning scheduled replay.  Applying state from frame:" + frameToActuallyReplayFrom);
            }
            StateFrameDTO gameStateObject = stateBuffer[frameToActuallyReplayFrom];

            ApplyPhysicsState(gameStateObject.PhysicsState);
            OnApplyState?.Invoke(gameStateObject.gameState);
            OnApplyInputs?.Invoke(gameStateObject.PlayerInputs);
            OnApplyEvents?.Invoke(gameStateObject.Events);

            // Replay history until we're back to 'now'
            ReplayHistoryFromTick(frameToActuallyReplayFrom);

            // Reset to "nothing scheduled"
            replayFromTick = -1;
        }

        [ClientRpc]
        private void SendGameEventToClientsClientRpc(int serverTimeTick, GameEventDTO gameEventDTO)
        {
            stateBuffer[serverTimeTick].Events.Append(gameEventDTO.gameEvent);
            ScheduleStateReplay(serverTimeTick);
        }

        private void HostFixedUpdate()
        {
            RunScheduledStateReplay();

            // Start a new frame
            gameTick++;

            if (verboseLogging)
            {
                Debug.Log("Normal frame run for tick: " + gameTick);
            }

            // Since it's impossible for us to have the inputs for other clients at this point,
            // we'll need to start by predicting them forward and then overwrite with
            // any that are server-authoritative (i.e. that come from a Host)
            Dictionary<byte, IPlayerInput> playerInputs = PredictInputs(stateBuffer[gameTick - 1].PlayerInputs);
            Dictionary<byte, IPlayerInput> localInputs = new();
            OnGetInputs?.Invoke(ref localInputs);
            foreach (KeyValuePair<byte, IPlayerInput> entry in localInputs)
            {
                playerInputs[entry.Key] = entry.Value;

                Type playerInputType = TypeStore.Instance.PlayerInputType;
                IPlayerInput defaultInput = (IPlayerInput)Activator.CreateInstance(playerInputType);

                // Send to clients, if they wouldn't have predicted this value
                IPlayerInput predictedInput = PredictInput(stateBuffer[gameTick - 1].PlayerInputs.GetValueOrDefault(entry.Key, defaultInput));

                if (!predictedInput.Equals(entry.Value))
                {
                    PlayerInputDTO playerInputDTO = new();
                    playerInputDTO.input = entry.Value;
                    ForwardPlayerInputClientRpc(entry.Key, playerInputDTO, gameTick);
                }
            }

            // Actually simulate the frame
            stateBuffer[gameTick] = RunSingleGameFrame(gameTick, playerInputs, stateBuffer[gameTick].Events);

            // (Maybe) send the new state to the clients for reconciliation
            if ((int)gameTick % sendStateEveryNFrames == 0)
            {
                if (verboseLogging)
                {
                    Debug.Log("Sending delta - old frame time: " + (gameTick - sendStateEveryNFrames) + " new frame time:" + gameTick);
                }
                // TODO: there's an opportunity to be slightly more aggressive by skipping sending anything if the entire
                //       state frame is exactly the same (except for the gameTick, of course).
                StateFrameDTO delta = stateBuffer[gameTick - sendStateEveryNFrames].GenerateDelta(stateBuffer[gameTick]);

                UpdateGameStateClientRpc(delta);
            }
        }

        private void SetPlayerInputAtTickAndPredictForward(byte playerId, IPlayerInput value, int tick)
        {
            // Set at tick
            stateBuffer[tick].PlayerInputs[playerId] = value;

            // ...and predict forward
            for (int i = tick + 1; i <= gameTick; i++)
            {
                stateBuffer[i].PlayerInputs[playerId] = PredictInput(stateBuffer[i - 1].PlayerInputs[playerId]);
            }
        }

        private void SetupInitialNetworkIds()
        {
            // Basically, we can't know what order everything's going to load in, so we can't know whether all clients will
            // get the same network id's on instantiation.
            // So instead, when the scene's ready we'll:
            //  * reset the counter
            //  * go through all the game objects that need a network id (in hierarchy order)
            //  * regenerate the network ids
            // In theory, the client and server should agree on the objects in the hierarchy at this point in time, so it should
            // be ok to use as a deterministic ordering mechanism.

            networkIdCounter = 0;
            List<GameObject> gameObjects = gameObject.scene.GetRootGameObjects().ToList();
            gameObjects.Sort((a, b) => a.transform.GetSiblingIndex() - b.transform.GetSiblingIndex());
            foreach (GameObject go in gameObjects)
            {
                SetupNetworkIdsForChildren(go.transform);
            }
        }

        private void SetupNetworkIdsForChildren(Transform node)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                NetworkId networkId = child.gameObject.GetComponent<NetworkId>();
                if (networkId != null)
                {
                    networkIdCounter += 1;
                    networkId.networkId = networkIdCounter;
                }

                if (child.childCount > 0)
                {
                    SetupNetworkIdsForChildren(child);
                }
            }
        }

        private void ResetNowToServerState(StateFrameDTO serverGameState)
        {
            // The server's timestamp may be in the future, in which case we'll want to
            // backfill the state, up to and including the server's tick
            for (int i = gameTick + 1; i <= serverGameState.gameTick; i++)
            {
                stateBuffer[i] = serverGameState;
            }

            // Apply the new "now" server state
            ApplyPhysicsState(serverGameState.PhysicsState);
            OnApplyState?.Invoke(serverGameState.gameState);
            OnApplyInputs?.Invoke(serverGameState.PlayerInputs);
            OnApplyEvents?.Invoke(serverGameState.Events);

            stateBuffer[serverGameState.gameTick] = serverGameState;

            // Advance a number of frames equal to the estimated lag from the server,
            // to get us approximately back in sync
            int framesOfLag = NetworkManager.LocalTime.Tick - NetworkManager.ServerTime.Tick;
            if (framesOfLag < 0)
            {
                throw new Exception("We are somehow ahead of the server, but this should never ever be the case.");
            }
            gameTick = serverGameState.gameTick + framesOfLag;
            ScheduleStateReplay(serverGameState.gameTick);
        }

        [ClientRpc]
        private void UpdateGameStateClientRpc(StateFrameDTO serverGameStateDelta)
        {
            if (!isRunning)
            {
                // RPC's can arrive before this component has run Start(), so skip out if it's too early
                return;
            }

            if (IsHost)
            {
                return;
            }

            if (verboseLogging)
            {
                Debug.Log("Server state received.  Server time:" + serverGameStateDelta.gameTick + " Our time:" + gameTick);
            }
            StateFrameDTO serverGameState = stateBuffer[serverGameStateDelta.gameTick - sendStateEveryNFrames].Duplicate();
            serverGameState.ApplyDelta(serverGameStateDelta);

            // If the state arrives out of order, we can safely ignore older states
            if (serverGameState.gameTick < lastAuthoritativeTick)
            {
                Debug.Log("Server sent state arrived out-of-order, so dropping");
                return;
            }

            lastAuthoritativeTick = serverGameState.gameTick;

            // According to Unity networking, we're this far ahead of when the server sent this data
            int framesOfLag = NetworkManager.LocalTime.Tick - NetworkManager.ServerTime.Tick;
            if (framesOfLag > 0)
            {
                framesOfLag += maxPastTolerance;
            }

            // ...so, if the timestamp for the server is more than that behind "now" (with a little
            // wiggle room), then we should reset to that time instead of trying to weave it into
            // our client-side prediction.  (The game state will "jump" to this newly received state)
            if (serverGameState.gameTick < gameTick - framesOfLag)
            {
                if (verboseLogging)
                {
                    Debug.Log(
                        "Server gave state for tick " +
                        serverGameState.gameTick +
                        " but it's outside of our acceptable lag range; resetting time so we can get back in sync." +
                        " Acceptable range starts at:" + (gameTick - framesOfLag)
                    );
                }
                ResetNowToServerState(serverGameState);
                return;
            }

            // TODO: the tricky part about checking if the packet is too far in the future is figuring out if
            //          a) we're legit that far behind the server (in which case we can just skip ahead)
            //          b) it's a forged packet from a cheater (in which case we want to just drop it since
            //             it'll cause all sorts of other headaches to be desync'd from the server like this)
            //       So, for now, just assume it's always legit.
            if (serverGameState.gameTick > gameTick)
            {
                // Server sent state from the future, so move to then and get caught up
                if (verboseLogging)
                {
                    Debug.Log(
                        "The server sent a state from the near future, so catch up to then.  Fast forward from:" +
                        gameTick + " to: " + serverGameState.gameTick
                    );
                }

                ResetNowToServerState(serverGameState);
                return;
            }

            // TODO: probably we want a way to only predict forwards for players that are _not_ local to this client
            foreach (KeyValuePair<byte, IPlayerInput> item in stateBuffer[serverGameState.gameTick - 1].PlayerInputs)
            {
                serverGameState.PlayerInputs[item.Key] = PredictInput(item.Value);
            }

            ScheduleStateReplay(serverGameState.gameTick);
            stateBuffer[serverGameState.gameTick] = serverGameState;
        }
    }
}