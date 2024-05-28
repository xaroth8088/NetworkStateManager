using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using Unity.Netcode;

namespace NSM.Tests
{
    [TestFixture]
    public class PlayerInputsDTOTests
    {
        private PlayerInputsDTO _playerInputsDTO;
        private IPlayerInput _mockPlayerInput;
        private Dictionary<byte, IPlayerInput> _expectedDictionary;

        [SetUp]
        public void SetUp()
        {
            _playerInputsDTO = new PlayerInputsDTO();
            _mockPlayerInput = new TestPlayerInputDTO();
            _expectedDictionary = new Dictionary<byte, IPlayerInput>
            {
                { 1, _mockPlayerInput }
            };
        }

        [Test]
        public void PlayerInputs_Get_WhenNotInitialized_ShouldReturnEmptyDictionary()
        {
            // Act
            var playerInputs = _playerInputsDTO.PlayerInputs;

            // Assert
            Assert.IsNotNull(playerInputs);
            Assert.IsInstanceOf<Dictionary<byte, IPlayerInput>>(playerInputs);
            Assert.AreEqual(0, playerInputs.Count);
        }

        [Test]
        public void PlayerInputs_SetAndGet_ShouldReturnSetDictionary()
        {
            // Act
            _playerInputsDTO.PlayerInputs = _expectedDictionary;
            var actualDictionary = _playerInputsDTO.PlayerInputs;

            // Assert
            Assert.AreEqual(_expectedDictionary, actualDictionary);
            Assert.AreEqual(1, actualDictionary.Count);
            Assert.AreEqual(_mockPlayerInput, actualDictionary[1]);
        }

        [Test]
        public void PlayerInputs_Set_ShouldOverridePreviousDictionary()
        {
            // Arrange
            var newMockPlayerInput = new TestPlayerInputDTO();
            var newDictionary = new Dictionary<byte, IPlayerInput>
            {
                { 2, newMockPlayerInput }
            };

            // Act
            _playerInputsDTO.PlayerInputs = _expectedDictionary;
            _playerInputsDTO.PlayerInputs = newDictionary;
            var actualDictionary = _playerInputsDTO.PlayerInputs;

            // Assert
            Assert.AreEqual(newDictionary, actualDictionary);
            Assert.AreEqual(1, actualDictionary.Count);
            Assert.AreEqual(newMockPlayerInput, actualDictionary[2]);
        }
    }
}