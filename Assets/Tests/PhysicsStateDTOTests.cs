using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using NSM;

namespace NSM.Tests {
    public class PhysicsStateDTOTests
    {
        [Test]
        public void TakeSnapshot_ShouldPopulateRigidBodyStates_WhenRigidbodyHasNetworkId()
        {
            // Arrange
            var body1 = new GameObject();
            var rigidbody1 = body1.AddComponent<Rigidbody>();
            var networkIdComponent1 = body1.AddComponent<NetworkId>();
            networkIdComponent1.networkId = 1;

            var body2 = new GameObject();
            var rigidbody2 = body2.AddComponent<Rigidbody>();
            var networkIdComponent2 = body2.AddComponent<NetworkId>();
            networkIdComponent2.networkId = 2;

            var rigidbodies = new List<Rigidbody> { rigidbody1, rigidbody2 };

            var physicsStateDTO = new PhysicsStateDTO();

            // Act
            physicsStateDTO.TakeSnapshot(rigidbodies);

            // Assert
            Assert.IsNotNull(physicsStateDTO.RigidBodyStates);
            Assert.AreEqual(2, physicsStateDTO.RigidBodyStates.Count);
            Assert.IsTrue(physicsStateDTO.RigidBodyStates.ContainsKey(1));
            Assert.IsTrue(physicsStateDTO.RigidBodyStates.ContainsKey(2));
        }

        [Test]
        public void TakeSnapshot_ShouldNotAddRigidbodyWithoutNetworkId()
        {
            // Arrange
            var body1 = new GameObject();
            var rigidbody1 = body1.AddComponent<Rigidbody>();
            var networkIdComponent1 = body1.AddComponent<NetworkId>();
            networkIdComponent1.networkId = 1;

            var gameObjectWithoutNetworkId = new GameObject();
            var rigidbody2 = gameObjectWithoutNetworkId.AddComponent<Rigidbody>();

            var rigidbodies = new List<Rigidbody> { rigidbody1, rigidbody2 };

            var physicsStateDTO = new PhysicsStateDTO();

            // Act
            physicsStateDTO.TakeSnapshot(rigidbodies);

            // Assert
            Assert.IsNotNull(physicsStateDTO.RigidBodyStates);
            Assert.AreEqual(1, physicsStateDTO.RigidBodyStates.Count);
            Assert.IsTrue(physicsStateDTO.RigidBodyStates.ContainsKey(1));
            Assert.IsFalse(physicsStateDTO.RigidBodyStates.ContainsKey(0));
        }

        [Test]
        public void TakeSnapshot_ShouldHandleEmptyList()
        {
            // Arrange
            var rigidbodies = new List<Rigidbody>();

            var physicsStateDTO = new PhysicsStateDTO();

            // Act
            physicsStateDTO.TakeSnapshot(rigidbodies);

            // Assert
            Assert.IsNotNull(physicsStateDTO.RigidBodyStates);
            Assert.AreEqual(0, physicsStateDTO.RigidBodyStates.Count);
        }
    }
}