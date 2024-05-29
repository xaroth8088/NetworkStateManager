using NUnit.Framework;
using System;

namespace NSM.Tests
{
    [TestFixture]
    public class StateFrameDeltaDTOTests
    {
        private StateFrameDTO _baseStateMock;
        private StateFrameDTO _targetStateMock;

        [SetUp]
        public void Setup()
        {
            _baseStateMock = new StateFrameDTO
            {
                gameTick = 123
            };
            _targetStateMock = new StateFrameDTO
            {
                gameTick = 456
            };
        }

        [Test]
        public void Constructor_EmptyState_CreatesEmptyStateDiffBytes()
        {
            var dto = new StateFrameDeltaDTO();
            Assert.IsNotNull(dto);
        }

        [Test]
        public void Constructor_WithBaseAndTargetStates_CreatesStateDiffBytes()
        {
            var dto = new StateFrameDeltaDTO(_baseStateMock, _targetStateMock);

            Assert.IsNotNull(dto);
        }

        [Test]
        public void ApplyTo_ValidBaseState_AppliesPatchCorrectly()
        {
            var dto = new StateFrameDeltaDTO(_baseStateMock, _targetStateMock);

            var patchedState = dto.ApplyTo(_baseStateMock);

            Assert.IsNotNull(patchedState);
        }

        [Test]
        public void ApplyTo_InvalidCrc_ThrowsException()
        {
            // Arrange
            // The CRC isn't going to be correct, because the base won't match
            var dto = new StateFrameDeltaDTO(_baseStateMock, _targetStateMock);

            // Act
            var otherBaseStateMock = new StateFrameDTO() {
                gameTick = 987
            };

            // Assert
            Assert.Throws<Exception>(() => dto.ApplyTo(otherBaseStateMock));
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, "Incoming game state delta failed CRC check when applied");
        }
    }
}