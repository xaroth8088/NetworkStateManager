using System;
using NUnit.Framework;
using NSubstitute;
using Unity.Netcode;
using NSM;

namespace NSM.Tests
{
    [TestFixture]
    public class PlayerInputDTOTests
    {
        private IPlayerInput mockInput;

        [SetUp]
        public void SetUp()
        {
            mockInput = new TestPlayerInputDTO();
            TypeStore.Instance.PlayerInputType = mockInput.GetType();
        }

        [Test]
        public void PlayerInputDTO_WithInput_AssignsInputCorrectly()
        {
            // Arrange & Act
            var dto = new PlayerInputDTO { input = mockInput };

            // Assert
            Assert.AreEqual(mockInput, dto.input);
        }

        [Test]
        public void PlayerInputDTO_WithoutInput_DoesNotInitializeInput()
        {
            // Arrange
            var dto = new PlayerInputDTO();

            // Act & Assert
            Assert.IsNull(dto.input);
        }

        [Test]
        public void PlayerInputDTO_CreatesInstanceWithCorrectType()
        {
            // Arrange
            var playerInputType = TypeStore.Instance.PlayerInputType;

            // Act
            var createdInstance = Activator.CreateInstance(playerInputType);

            // Assert
            Assert.IsNotNull(createdInstance);
            Assert.IsInstanceOf(playerInputType, createdInstance);
        }
    }
}
