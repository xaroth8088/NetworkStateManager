using NUnit.Framework;
using UnityEngine;

namespace NSM.Tests
{
    public class NetworkIdTests
    {
        private NetworkId _networkId;

        [SetUp]
        public void SetUp()
        {
            // Create a GameObject and add the NetworkId component
            var gameObject = new GameObject();
            _networkId = gameObject.AddComponent<NetworkId>();
        }

        [TearDown]
        public void TearDown()
        {
            // Destroy the GameObject after each test to clean up
            Object.DestroyImmediate(_networkId.gameObject);
        }

        [Test]
        public void DefaultNetworkIdIsZero()
        {
            // Assert the default value of networkId
            Assert.AreEqual(0, _networkId.networkId);
        }

        [Test]
        public void CanSetNetworkId()
        {
            // Arrange
            byte expectedNetworkId = 5;

            // Act
            _networkId.networkId = expectedNetworkId;

            // Assert
            Assert.AreEqual(expectedNetworkId, _networkId.networkId);
        }
    }
}
