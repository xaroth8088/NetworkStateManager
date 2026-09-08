using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using UnityEngine.TestTools;
using System.Collections;
using System.Reflection;
using System.Linq;

namespace NSM.Tests
{
    public class IntegrationTests
    {
        private NetworkStateManager networkStateManager;
        private NetworkManager networkManager;
        private byte score;
        private GameObject player0GO;
        private GameObject player1GO;
        private NetworkManager remoteClient;
        private ushort hostPort;
        private SimulationMode previousPhysicsMode;
        private bool previousAutoSync;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousPhysicsMode = Physics.simulationMode;
            previousAutoSync = Physics.autoSyncTransforms;
            // A simple scene with two rigidbodies (one per player).  One is affected by gravity, the other is kinematic.
            // The game state only has one value - the score - which increases monotonically per frame.
            // The only game event contains a value that'll be added to the score when triggered.
            // A player's input has one bool.  If true, the y-value of the associated object will be increased by 10.

            // Proceed with remaining initialization
            player0GO = new();
            player0GO.AddComponent<Rigidbody>();
            player0GO.AddComponent<NetworkId>();
            player0GO.transform.SetPositionAndRotation(new Vector3(10, 100, 20), Quaternion.identity);

            player1GO = new();
            player1GO.AddComponent<Rigidbody>();
            player1GO.GetComponent<Rigidbody>().isKinematic = true;
            player1GO.AddComponent<NetworkId>();
            player1GO.transform.SetPositionAndRotation(new Vector3(40, 200, 50), Quaternion.identity);

            var prefab = Resources.Load<GameObject>("NSMIntegrationFixture");
            Assert.That(prefab, Is.Not.Null);
            GameObject nsmContainer = Object.Instantiate(prefab);
            networkStateManager = nsmContainer.GetComponent<NetworkStateManager>();
            networkStateManager.verboseLogging = false;

            networkStateManager.OnApplyEvents += NetworkStateManager_OnApplyEvents;
            networkStateManager.OnApplyInputs += NetworkStateManager_OnApplyInputs;
            networkStateManager.OnApplyState += NetworkStateManager_OnApplyState;
            networkStateManager.OnGetGameState += NetworkStateManager_OnGetGameState;
            networkStateManager.OnGetInputs += NetworkStateManager_OnGetInputs;
            networkStateManager.OnPostPhysicsFrameUpdate += NetworkStateManager_OnPostPhysicsFrameUpdate;
            networkStateManager.OnPrePhysicsFrameUpdate += NetworkStateManager_OnPrePhysicsFrameUpdate;
            networkStateManager.OnRollbackEvents += NetworkStateManager_OnRollbackEvents;

            // Start us off
            // Setup the NetworkManager
            GameObject networkManagerContainer = new("NSM Test Host");
            networkManager = networkManagerContainer.AddComponent<NetworkManager>();
            networkManager.NetworkConfig = new NetworkConfig();
            var transport = networkManagerContainer.AddComponent<UnityTransport>();
            using (var reservation = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
                hostPort = (ushort)((System.Net.IPEndPoint)reservation.Client.LocalEndPoint).Port;
            transport.SetConnectionData("127.0.0.1", hostPort, "127.0.0.1");
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.EnableSceneManagement = false;
            networkManager.AddNetworkPrefab(prefab);
            Assert.That(networkManager.StartHost(), Is.True);
            nsmContainer.SetActive(true);
            Assert.That(nsmContainer.GetComponent<NetworkObject>().IsSpawned, Is.True);
            yield return null;
            Assert.That(networkStateManager.IsServer, Is.True);

            score = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (remoteClient != null)
            {
                var objects = remoteClient.SpawnManager.SpawnedObjects.Values.Select(obj => obj.gameObject).ToArray();
                remoteClient.Shutdown();
                float timeout = Time.realtimeSinceStartup + 10;
                while (remoteClient.IsListening && Time.realtimeSinceStartup < timeout) yield return null;
                Assert.That(remoteClient.IsListening, Is.False);
                foreach (var obj in objects) if (obj != null) Object.DestroyImmediate(obj);
                Object.DestroyImmediate(remoteClient.gameObject);
                remoteClient = null;
            }
            GameObject.DestroyImmediate(networkStateManager.gameObject);
            networkStateManager = null;
            GameObject.DestroyImmediate(player0GO);
            player0GO = null;
            GameObject.DestroyImmediate(player1GO);
            player1GO = null;
            score = 0;

            networkManager.Shutdown();

            float deadline = Time.realtimeSinceStartup + 10;
            while (networkManager.IsHost && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.That(networkManager.IsHost, Is.False, "Host shutdown timed out");
            GameObject.DestroyImmediate(networkManager.gameObject);
            networkManager = null;
            Physics.simulationMode = previousPhysicsMode;
            Physics.autoSyncTransforms = previousAutoSync;
        }

        #region Callbacks
        private void NetworkStateManager_OnRollbackEvents(HashSet<IGameEvent> events, IGameState stateAfterEvent)
        {
            foreach (IGameEvent gameEvent in events)
            {
                score -= ((IntegrationTestGameEventDTO)gameEvent).scoreBonus;
            }
        }

        private void NetworkStateManager_OnPrePhysicsFrameUpdate()
        {
            score++;
        }

        private void NetworkStateManager_OnPostPhysicsFrameUpdate()
        {
        }

        private void NetworkStateManager_OnGetInputs(ref Dictionary<byte, IPlayerInput> playerInputs)
        {
            // Player 0 jumps on even numbered frames, and Player 1 jumps on odd-numbered frames

            playerInputs[0] = new IntegrationTestPlayerInputDTO()
            {
                IsJumping = networkStateManager.GameTick % 2 == 0
            };

            playerInputs[1] = new IntegrationTestPlayerInputDTO()
            {
                IsJumping = networkStateManager.GameTick % 2 == 1
            };

            Debug.Log($"p0 jump: {((IntegrationTestPlayerInputDTO)playerInputs[0]).IsJumping} p1 jump: {((IntegrationTestPlayerInputDTO)playerInputs[1]).IsJumping}");
        }

        private void NetworkStateManager_OnGetGameState(ref IGameState state)
        {
            IntegrationTestGameStateDTO stateDTO = (IntegrationTestGameStateDTO)state;
            stateDTO.score = score;
        }

        private void NetworkStateManager_OnApplyState(IGameState state)
        {
            IntegrationTestGameStateDTO stateDTO = (IntegrationTestGameStateDTO)state;
            score = stateDTO.score;
        }

        private void NetworkStateManager_OnApplyInputs(Dictionary<byte, IPlayerInput> playerInputs)
        {
            if (playerInputs.TryGetValue(0, out IPlayerInput input1))
            {
                if (((IntegrationTestPlayerInputDTO)input1).IsJumping)
                {
                    player0GO.transform.SetPositionAndRotation(new Vector3(0, 10f, 0) + player0GO.transform.position, player0GO.transform.rotation);
                }
            }
            if (playerInputs.TryGetValue(1, out IPlayerInput input2))
            {
                if (((IntegrationTestPlayerInputDTO)input2).IsJumping)
                {
                    player1GO.transform.SetPositionAndRotation(new Vector3(0, 10f, 0) + player1GO.transform.position, player1GO.transform.rotation);
                }
            }
        }

        private void NetworkStateManager_OnApplyEvents(HashSet<IGameEvent> events)
        {
            foreach (IGameEvent gameEvent in events)
            {
                score += ((IntegrationTestGameEventDTO)gameEvent).scoreBonus;
            }
        }
        #endregion Callbacks

        #region DTOs
        struct IntegrationTestGameEventDTO : IGameEvent
        {
            public byte scoreBonus;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref scoreBonus);
            }
        }

        struct IntegrationTestGameStateDTO : IGameState
        {
            public byte score;

            public byte[] GetBinaryRepresentation()
            {
                return new byte[1] { score };
            }

            public void RestoreFromBinaryRepresentation(byte[] bytes)
            {
                score = bytes[0];
            }
        }

        struct IntegrationTestPlayerInputDTO : IPlayerInput
        {
            public bool IsJumping;

            public bool Equals(IPlayerInput other)
            {
                return IsJumping == ((IntegrationTestPlayerInputDTO)other).IsJumping;
            }

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref IsJumping);
            }
        }

        #endregion DTOs

        private IEnumerator StartSimulation()
        {
            var operation = networkStateManager.StartNetworkStateManager(typeof(IntegrationTestGameStateDTO), typeof(IntegrationTestPlayerInputDTO), typeof(IntegrationTestGameEventDTO));
            var awaiter = operation.GetAwaiter();
            float deadline = Time.realtimeSinceStartup + 10;
            while (!awaiter.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "NSM startup timed out");
            awaiter.GetResult();
        }

        #region Tests

        [UnityTest]
        public IEnumerator RemoteInputRpcEnforcesOwnershipValuesAndDuplicateTicks()
        {
            var clientObject = new GameObject("NSM remote test peer");
            remoteClient = clientObject.AddComponent<NetworkManager>();
            remoteClient.NetworkConfig = new NetworkConfig { EnableSceneManagement = false };
            var transport = clientObject.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", hostPort);
            remoteClient.NetworkConfig.NetworkTransport = transport;
            remoteClient.AddNetworkPrefab(Resources.Load<GameObject>("NSMIntegrationFixture"));
            Assert.That(remoteClient.StartClient(), Is.True);
            yield return WaitFor(() => remoteClient.IsConnectedClient && remoteClient.SpawnManager.SpawnedObjects.Count > 0);
            var peer = remoteClient.SpawnManager.SpawnedObjects.Values.Select(obj => obj.GetComponent<NetworkStateManager>()).First(obj => obj != null);
            Assert.That(peer.IsServer, Is.False);
            var forgeEvents = typeof(NetworkStateManager).GetMethod("SyncGameEventsToClientsClientRpc", BindingFlags.NonPublic | BindingFlags.Instance);
            var unauthorized = Assert.Throws<TargetInvocationException>(() => forgeEvents.Invoke(peer, new object[] { 0, new GameEventsBuffer() }));
            Assert.That(unauthorized.InnerException, Is.TypeOf<RpcException>());
            var rejections = new List<InputRejection>();
            networkStateManager.OnInputRejected += (_, reason) => rejections.Add(reason);
            networkStateManager.ConfigureInputPolicy(policy =>
            {
                policy.AssignPlayer(1, networkManager.LocalClientId);
                policy.AssignPlayer(5, remoteClient.LocalClientId);
                policy.ValidateValue = (_, input) => input is IntegrationTestPlayerInputDTO value && value.IsJumping;
            });
            yield return StartSimulation();
            yield return new WaitForFixedUpdate();
            int tick = networkStateManager.GameTick;
            var send = typeof(NetworkStateManager).GetMethod("SetPlayerInputsServerRpc", BindingFlags.NonPublic | BindingFlags.Instance);
            void Send(byte player, bool jumping, int inputTick) => send.Invoke(peer, new object[]
            {
                new PlayerInputsDTO { PlayerInputs = new() { [player] = new IntegrationTestPlayerInputDTO { IsJumping = jumping } } },
                inputTick, default(RpcParams)
            });
            Send(1, true, tick);
            yield return WaitFor(() => rejections.Contains(InputRejection.WrongOwner));
            tick = networkStateManager.GameTick;
            Send(5, false, tick);
            yield return WaitFor(() => rejections.Contains(InputRejection.InvalidValue));
            tick = networkStateManager.GameTick;
            Send(5, true, tick);
            yield return WaitFor(() => networkStateManager.GetInputsForTick(tick).ContainsKey(5));
            Assert.That(((IntegrationTestPlayerInputDTO)networkStateManager.GetInputsForTick(tick)[5]).IsJumping, Is.True);
            Send(5, true, tick);
            yield return WaitFor(() => rejections.Contains(InputRejection.Duplicate));
            Assert.That(networkStateManager.IsRunning, Is.True);
        }

        private static IEnumerator WaitFor(System.Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Network condition timed out");
        }

        [UnityTest]
        public IEnumerator SingleFrameExecutes()
        {
            yield return StartSimulation();

            float originalY1 = player1GO.transform.position.y;

            // Run a frame
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(1, score);
            Assert.That(player0GO.transform.position.y, Is.EqualTo(99.99608f).Within(0.00001));
            Assert.AreEqual(originalY1 + 10f, player1GO.transform.position.y);  // +10, because this player will jump during this frame
        }

        [UnityTest]
        public IEnumerator TwentyFramesExecute()
        {
            yield return StartSimulation();

            float originalY1 = player1GO.transform.position.y;

            // Run 20 frames
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(20, score);
            Assert.That(player0GO.transform.position.y, Is.EqualTo(199.176f).Within(0.001));
            Assert.AreEqual(originalY1 + 100f, player1GO.transform.position.y);  // +100, because this player will jump 10x during this run
        }


        #endregion Tests
    }
}
