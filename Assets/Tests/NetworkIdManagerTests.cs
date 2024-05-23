using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using NSubstitute;
using System.Linq;
using System.Threading.Tasks;

namespace NSM.Tests
{
    public class NetworkIdManagerTests
    {
        private NetworkIdManager _networkIdManager;
        private NetworkStateManager _networkStateManager;

        [SetUp]
        public void Setup()
        {
            _networkStateManager = Substitute.For<NetworkStateManager>();
            _networkIdManager = new NetworkIdManager(_networkStateManager);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Reset_ShouldResetNetworkIdCaches()
        {
            _networkIdManager.Reset();

            var gameObjectCache = _networkIdManager.GetAllNetworkIdGameObjects();
            var reservedNetworkIds = _networkIdManager.GetType()
                                                      .GetField("reservedNetworkIds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                                      .GetValue(_networkIdManager) as bool[];

            Assert.IsEmpty(gameObjectCache);
            Assert.That(reservedNetworkIds.All(id => id == false));
        }

        [Test]
        public void RegisterGameObject_ShouldAssignNetworkIdAndCacheGameObject()
        {
            var gameObject = new GameObject("TestObject");

            _networkIdManager.RegisterGameObject(gameObject);

            var assignedNetworkId = gameObject.GetComponent<NetworkId>().networkId;

            Assert.AreNotEqual(0, assignedNetworkId);
            Assert.AreEqual(gameObject, _networkIdManager.GetGameObjectByNetworkId(assignedNetworkId));
        }

        [Test]
        public void ReserveNetworkId_ShouldReturnUniqueId()
        {
            var networkId = _networkIdManager.ReserveNetworkId();
            var networkId2 = _networkIdManager.ReserveNetworkId();
            var networkId3 = _networkIdManager.ReserveNetworkId();

            Assert.AreNotEqual(0, networkId);
            Assert.AreNotEqual(0, networkId2);
            Assert.AreNotEqual(0, networkId3);
            Assert.AreNotEqual(networkId, networkId2);
            Assert.AreNotEqual(networkId, networkId3);
            Assert.AreNotEqual(networkId2, networkId3);
        }

        [Test]
        public void ReleaseNetworkId_ShouldReleaseReservedId()
        {
            var gameObject = new GameObject("TestObject");
            _networkIdManager.RegisterGameObject(gameObject);
            var networkId = gameObject.GetComponent<NetworkId>().networkId;

            _networkIdManager.ReleaseNetworkId(networkId);

            var reservedNetworkIds = _networkIdManager.GetType()
                                                      .GetField("reservedNetworkIds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                                      .GetValue(_networkIdManager) as bool[];

            Assert.IsFalse(reservedNetworkIds[networkId]);
        }

        [Test]
        public void GetAllNetworkIdGameObjects_ShouldReturnAllRegisteredGameObjects()
        {
            var gameObject1 = new GameObject("TestObject1");
            var gameObject2 = new GameObject("TestObject2");

            _networkIdManager.RegisterGameObject(gameObject1);
            _networkIdManager.RegisterGameObject(gameObject2);

            var allGameObjects = _networkIdManager.GetAllNetworkIdGameObjects();

            Assert.Contains(gameObject1, allGameObjects.ToList());
            Assert.Contains(gameObject2, allGameObjects.ToList());
        }

        [Test]
        public async Task SetupInitialNetworkIds_ShouldResetAndSetupNetworkIds()
        {
            var scene = SceneManager.CreateScene("TestScene");

            var rootParent = new GameObject("RootParent");
            SceneManager.MoveGameObjectToScene(rootParent, scene);

            var childObject1 = new GameObject("Child1");
            var childObject2 = new GameObject("Child2");
            childObject1.transform.SetParent(rootParent.transform);
            childObject2.transform.SetParent(rootParent.transform);

            childObject1.AddComponent<NetworkId>();
            childObject2.AddComponent<NetworkId>();

            _networkIdManager.SetupInitialNetworkIds(scene);

            var allGameObjects = _networkIdManager.GetAllNetworkIdGameObjects().ToList();

            Assert.AreEqual(2, allGameObjects.Count);
            Assert.Contains(childObject1, allGameObjects);
            Assert.Contains(childObject2, allGameObjects);

            await SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public async Task SetupInitialNetworkIds_ShouldResetAndSetupNetworkIdsIncludingRootObjects()
        {
            var scene = SceneManager.CreateScene("TestScene");

            var rootObject1 = new GameObject("Root1");
            var rootObject2 = new GameObject("Root2");
            SceneManager.MoveGameObjectToScene(rootObject1, scene);
            SceneManager.MoveGameObjectToScene(rootObject2, scene);

            rootObject1.AddComponent<NetworkId>();
            rootObject2.AddComponent<NetworkId>();

            _networkIdManager.SetupInitialNetworkIds(scene);

            var allGameObjects = _networkIdManager.GetAllNetworkIdGameObjects().ToList();

            Assert.AreEqual(2, allGameObjects.Count);
            Assert.Contains(rootObject1, allGameObjects);
            Assert.Contains(rootObject2, allGameObjects);

            await SceneManager.UnloadSceneAsync(scene);
        }
    }
}
