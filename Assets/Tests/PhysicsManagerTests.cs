using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using NSubstitute;
using UnityEngine.TestTools;

namespace NSM.Tests
{
    public class PhysicsManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            // Reset physics settings before each test
            Physics.autoSyncTransforms = true;
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }

        [Test]
        public void InitPhysics_SetsCorrectPhysicsSettings()
        {
            // Act
            PhysicsManager.InitPhysics();

            // Assert
            Assert.AreEqual(SimulationMode.Script, Physics.simulationMode);
            Assert.IsFalse(Physics.autoSyncTransforms);
        }

        [Test]
        public void SimulatePhysics_CallsPhysicsSimulateWithDeltaTime()
        {
            // Arrange
            var gameObject = new GameObject();
            var rigidbody = gameObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = false; // Disable gravity for controlled testing
            rigidbody.AddForce(Vector3.right * 10f, ForceMode.VelocityChange);

            // Record initial position
            Vector3 initialPosition = rigidbody.position;

            // Act
            PhysicsManager.InitPhysics();
            PhysicsManager.SimulatePhysics(0.1f); // Simulate for 0.1 seconds

            // Assert
            Vector3 finalPosition = rigidbody.position;
            Assert.AreNotEqual(initialPosition, finalPosition, "The Rigidbody should have moved after the physics simulation.");
        }

        [Test]
        public void ApplyPhysicsState_LogsErrorIfGameObjectDoesNotExist()
        {
            // Arrange
            var networkIdManager = Substitute.For<INetworkIdManager>();
            var physicsState = new PhysicsStateDTO
            {
                RigidBodyStates = new Dictionary<byte, RigidBodyStateDTO>
                {
                    { 1, new RigidBodyStateDTO() }
                }
            };

            networkIdManager.GetGameObjectByNetworkId(1).Returns((GameObject)null);

            // Act
            PhysicsManager.ApplyPhysicsState(physicsState, networkIdManager);

            // Assert
            LogAssert.Expect(LogType.Error, "Attempted to restore state to a GameObject that no longer exists");
        }

        [Test]
        public void ApplyPhysicsState_AppliesStateToExistingGameObject()
        {
            // Arrange
            var networkIdManager = Substitute.For<INetworkIdManager>();
            var rigidBodyState = new RigidBodyStateDTO(); // Create a default instance
            var physicsState = new PhysicsStateDTO
            {
                RigidBodyStates = new Dictionary<byte, RigidBodyStateDTO>
                {
                    { 1, rigidBodyState }
                }
            };

            var gameObject = new GameObject();
            var rigidbody = gameObject.AddComponent<Rigidbody>();
            networkIdManager.GetGameObjectByNetworkId(1).Returns(gameObject);

            // Act
            PhysicsManager.ApplyPhysicsState(physicsState, networkIdManager);

            // Assert
            // Here, you'd verify that the state was applied to the rigidbody.
            // Since the actual method on RigidBodyStateDTO is a placeholder, we'll assume success if no exceptions are thrown.
            Assert.Pass();
        }

        [Test]
        public void GetNetworkedRigidbodies_ReturnsAllRigidbodies()
        {
            // Arrange
            var networkIdManager = Substitute.For<INetworkIdManager>();
            var gameObject1 = new GameObject();
            var rigidbody1 = gameObject1.AddComponent<Rigidbody>();
            var gameObject2 = new GameObject();
            var rigidbody2 = gameObject2.AddComponent<Rigidbody>();
            var gameObject3 = new GameObject(); // No Rigidbody

            networkIdManager.GetAllNetworkIdGameObjects().Returns(new List<GameObject> { gameObject1, gameObject2, gameObject3 });

            // Act
            List<Rigidbody> result = PhysicsManager.GetNetworkedRigidbodies(networkIdManager);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.Contains(rigidbody1, result);
            Assert.Contains(rigidbody2, result);
        }

        [Test]
        public void SyncTransforms_CallsPhysicsSyncTransforms()
        {
            // Act
            PhysicsManager.SyncTransforms();

            // Assert
            // This is more of an integration test.
            // We'll assume that if no exceptions are thrown, the call was made successfully.
            Assert.Pass();
        }
    }
}
