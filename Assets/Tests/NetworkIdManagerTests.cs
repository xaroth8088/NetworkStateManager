using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using NSubstitute;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.SceneManagement;

namespace NSM.Tests
{
    public class NetworkIdManagerTests
    {
        private NetworkIdManager _networkIdManager;
        private IInternalNetworkStateManager _networkStateManager;
        private readonly List<GameObject> createdObjects = new();
        private readonly List<Scene> createdScenes = new();
        private GameObject NewObject(string name)
        {
            var obj = new GameObject(name);
            createdObjects.Add(obj);
            return obj;
        }

        [SetUp]
        public void Setup()
        {
            _networkStateManager = Substitute.For<IInternalNetworkStateManager>();
            _networkIdManager = new NetworkIdManager(_networkStateManager);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in createdObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            createdObjects.Clear();
            foreach (var scene in createdScenes) if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            createdScenes.Clear();
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
            var gameObject = NewObject("TestObject");

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
            var gameObject = NewObject("TestObject");
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
            var gameObject1 = NewObject("TestObject1");
            var gameObject2 = NewObject("TestObject2");

            _networkIdManager.RegisterGameObject(gameObject1);
            _networkIdManager.RegisterGameObject(gameObject2);

            var allGameObjects = _networkIdManager.GetAllNetworkIdGameObjects();

            Assert.Contains(gameObject1, allGameObjects.ToList());
            Assert.Contains(gameObject2, allGameObjects.ToList());
        }

        [Test]
        public void SetupInitialNetworkIds_ShouldResetAndSetupNetworkIds()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            createdScenes.Add(scene);

            var rootParent = NewObject("RootParent");
            SceneManager.MoveGameObjectToScene(rootParent, scene);

            var childObject1 = NewObject("Child1");
            var childObject2 = NewObject("Child2");
            childObject1.transform.SetParent(rootParent.transform);
            childObject2.transform.SetParent(rootParent.transform);

            childObject1.AddComponent<NetworkId>();
            childObject2.AddComponent<NetworkId>();

            _networkIdManager.SetupInitialNetworkIds(scene);

            var allGameObjects = _networkIdManager.GetAllNetworkIdGameObjects().ToList();

            Assert.AreEqual(2, allGameObjects.Count);
            Assert.Contains(childObject1, allGameObjects);
            Assert.Contains(childObject2, allGameObjects);


        }

        [Test]
        public void SetupInitialNetworkIds_ShouldResetAndSetupNetworkIdsIncludingRootObjects()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            createdScenes.Add(scene);

            var rootObject1 = NewObject("Root1");
            var rootObject2 = NewObject("Root2");
            SceneManager.MoveGameObjectToScene(rootObject1, scene);
            SceneManager.MoveGameObjectToScene(rootObject2, scene);

            rootObject1.AddComponent<NetworkId>();
            rootObject2.AddComponent<NetworkId>();

            _networkIdManager.SetupInitialNetworkIds(scene);

            var allGameObjects = _networkIdManager.GetAllNetworkIdGameObjects().ToList();

            Assert.AreEqual(2, allGameObjects.Count);
            Assert.Contains(rootObject1, allGameObjects);
            Assert.Contains(rootObject2, allGameObjects);


        }
    }
}
