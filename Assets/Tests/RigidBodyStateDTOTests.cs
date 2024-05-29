using NUnit.Framework;
using UnityEngine;

namespace NSM.Tests
{
    [TestFixture]
    public class RigidBodyStateDTOTests
    {
        private Rigidbody mockRigidbody;
        private Transform mockTransform;
        private GameObject mockGameObject;
        private NetworkId mockNetworkId;

        [SetUp]
        public void SetUp()
        {
            mockGameObject = new GameObject();
            mockTransform = mockGameObject.transform;
            mockTransform.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(45, 90, 0));

            mockRigidbody = mockGameObject.AddComponent<Rigidbody>();
            mockRigidbody.linearVelocity = new Vector3(4, 5, 6);
            mockRigidbody.angularVelocity = new Vector3(7, 8, 9);

            mockNetworkId = mockGameObject.AddComponent<NetworkId>();
            mockNetworkId.networkId = 10;
        }

        [Test]
        public void Constructor_SetsPropertiesFromRigidbody()
        {
            // Act
            var state = new RigidBodyStateDTO(mockRigidbody);

            // Assert
            Assert.AreEqual(new Vector3(1, 2, 3), state.position);
            AssertEqualQuaternions(Quaternion.Euler(45, 90, 0), state.rotation);
            Assert.AreEqual(new Vector3(4, 5, 6), state.velocity);
            Assert.AreEqual(new Vector3(7, 8, 9), state.angularVelocity);
            Assert.IsFalse(state.isSleeping);
            Assert.AreEqual(10, state.networkId);
        }


        [Test]
        public void Constructor_SetsPropertiesFromSleepingRigidbody()
        {
            // Arrange
            mockRigidbody.Sleep();

            // Act
            var state = new RigidBodyStateDTO(mockRigidbody);

            // Assert
            Assert.AreEqual(new Vector3(1, 2, 3), state.position);
            AssertEqualQuaternions(Quaternion.Euler(45, 90, 0), state.rotation);
            Assert.AreEqual(new Vector3(0, 0, 0), state.velocity);
            Assert.AreEqual(new Vector3(0, 0, 0), state.angularVelocity);
            Assert.IsTrue(state.isSleeping);
            Assert.AreEqual(10, state.networkId);
        }

        [Test]
        public void Constructor_HandlesMissingNetworkIdComponent()
        {
            // Arrange
            mockGameObject = new GameObject();
            mockTransform = mockGameObject.transform;
            mockTransform.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(45, 90, 0));

            mockRigidbody = mockGameObject.AddComponent<Rigidbody>();
            mockRigidbody.linearVelocity = new Vector3(4, 5, 6);
            mockRigidbody.angularVelocity = new Vector3(7, 8, 9);

            // Act
            var state = new RigidBodyStateDTO(mockRigidbody);
            UnityEngine.TestTools.LogAssert.Expect("Found a rigidbody that doesn't have a NetworkId: New Game Object (UnityEngine.GameObject)");

            // Assert
            Assert.AreEqual(0, state.networkId);
        }

        [Test]
        public void ApplyState_SetsRigidbodyPropertiesForSleepingBody()
        {
            // Arrange
            var state = new RigidBodyStateDTO
            {
                position = new Vector3(1, 2, 3),
                rotation = Quaternion.Euler(45, 90, 0),
                velocity = new Vector3(4, 5, 6),
                angularVelocity = new Vector3(7, 8, 9),
                isSleeping = true,
                networkId = 10
            };

            mockRigidbody.isKinematic = false;

            // Act
            state.ApplyState(mockRigidbody);

            // Assert
            Assert.AreEqual(new Vector3(1, 2, 3), mockTransform.transform.position);
            AssertEqualQuaternions(Quaternion.Euler(45, 90, 0), mockTransform.rotation);
            Assert.IsTrue(mockRigidbody.IsSleeping());
            Assert.AreEqual(new Vector3(0, 0, 0), mockRigidbody.linearVelocity);    // NOTE: a sleeping rigidbody gets its velocities set to 0
            Assert.AreEqual(new Vector3(0, 0, 0), mockRigidbody.angularVelocity);
        }

        [Test]
        public void ApplyState_SetsRigidbodyPropertiesForNonSleepingBody()
        {
            // Arrange
            var state = new RigidBodyStateDTO
            {
                position = new Vector3(1, 2, 3),
                rotation = Quaternion.Euler(45, 90, 0),
                velocity = new Vector3(4, 5, 6),
                angularVelocity = new Vector3(7, 8, 9),
                isSleeping = false,
                networkId = 10
            };

            mockRigidbody.isKinematic = false;

            // Act
            state.ApplyState(mockRigidbody);

            // Assert
            Assert.AreEqual(new Vector3(1, 2, 3), mockTransform.transform.position);
            AssertEqualQuaternions(Quaternion.Euler(45, 90, 0), mockTransform.rotation);
            Assert.IsFalse(mockRigidbody.IsSleeping());
            Assert.AreEqual(new Vector3(4, 5, 6), mockRigidbody.linearVelocity);
            Assert.AreEqual(new Vector3(7, 8, 9), mockRigidbody.angularVelocity);
        }

        private void AssertEqualQuaternions(Quaternion expected, Quaternion actual)
        {
            float epsilon = 0.00001f;
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(epsilon));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(epsilon));
        }

        [Test]
        public void ApplyState_WakesUpRigidbodyIfNotSleeping()
        {
            // Arrange
            var state = new RigidBodyStateDTO
            {
                position = new Vector3(1, 2, 3),
                rotation = Quaternion.Euler(45, 90, 0),
                velocity = new Vector3(4, 5, 6),
                angularVelocity = new Vector3(7, 8, 9),
                isSleeping = false,
                networkId = 10
            };

            mockRigidbody.isKinematic = false;

            // Act
            state.ApplyState(mockRigidbody);

            // Assert
            Assert.AreEqual(new Vector3(1, 2, 3), mockTransform.transform.position);
            AssertEqualQuaternions(Quaternion.Euler(45, 90, 0), mockTransform.rotation);
            Assert.IsFalse(mockRigidbody.IsSleeping());
            Assert.AreEqual(new Vector3(4, 5, 6), mockRigidbody.linearVelocity);
            Assert.AreEqual(new Vector3(7, 8, 9), mockRigidbody.angularVelocity);
        }
    }
}