using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using UnityEngine.TestTools;
using System.Collections;

namespace NSM.Tests
{
    public class IntegrationTests
    {
        private NetworkStateManager networkStateManager;
        private NetworkManager networkManager;
        private byte score;
        private GameObject player0GO;
        private GameObject player1GO;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
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

            GameObject nsmContainer = new();
            nsmContainer.AddComponent<NetworkObject>();
            networkStateManager = nsmContainer.AddComponent<NetworkStateManager>();
            networkStateManager.verboseLogging = true;

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
            string prefabPath = "Assets/Tests/NetworkManager.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, "NetworkManager Prefab could not be loaded.");

            // Instantiate the NetworkManager prefab
            GameObject networkManagerContainer = Object.Instantiate(prefab);
            Assert.IsNotNull(networkManagerContainer, "Prefab instantiation failed.");

            // Wait a frame to allow any initialization logic to run
            yield return null;

            networkManager = networkManagerContainer.GetComponent<NetworkManager>();
            networkManager.StartHost();

            // Wait for the network manager to finish initialization
            while (networkStateManager.IsHost == false)
            {
                yield return null;
            }

            score = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameObject.DestroyImmediate(networkStateManager.gameObject);
            networkStateManager = null;
            GameObject.DestroyImmediate(player0GO);
            player0GO = null;
            GameObject.DestroyImmediate(player1GO);
            player1GO = null;
            score = 0;

            networkManager.Shutdown();

            while (networkManager.IsHost) { yield return null; }
            GameObject.DestroyImmediate(networkManager.gameObject);
            networkManager = null;
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

        #region Tests

        [UnityTest]
        public IEnumerator SingleFrameExecutes()
        {
            networkStateManager.StartNetworkStateManager(typeof(IntegrationTestGameStateDTO), typeof(IntegrationTestPlayerInputDTO), typeof(IntegrationTestGameEventDTO));

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
            networkStateManager.StartNetworkStateManager(typeof(IntegrationTestGameStateDTO), typeof(IntegrationTestPlayerInputDTO), typeof(IntegrationTestGameEventDTO));

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
