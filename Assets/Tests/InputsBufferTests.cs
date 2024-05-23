using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;

namespace NSM.Tests
{
    public class InputsBufferTests
    {
        private InputsBuffer _inputsBuffer;
        private IPlayerInput _mockPlayerInput;
        private IPlayerInput _mockBlankPlayerInput;

        [SetUp]
        public void SetUp()
        {
            _inputsBuffer = new InputsBuffer();
            _mockPlayerInput = Substitute.For<IPlayerInput>();
            _mockBlankPlayerInput = new TestPlayerInputDTO();

            TypeStore.Instance.GameStateType = typeof(TestGameStateDTO);
            TypeStore.Instance.PlayerInputType = typeof(TestPlayerInputDTO);
            TypeStore.Instance.GameEventType = typeof(TestGameEventDTO);
        }

        [TearDown]
        public void TearDown()
        {
            TypeStore.Instance.ResetTypeStore();
        }

        [Test]
        public void GetInputsForTick_ReturnsExpectedInputs()
        {
            // Arrange
            var localInputs = new Dictionary<byte, IPlayerInput> { { 1, _mockPlayerInput } };
            _inputsBuffer.SetLocalInputs(localInputs, 1);

            // Act
            var inputs = _inputsBuffer.GetInputsForTick(1);

            // Assert
            Assert.AreEqual(1, inputs.Count);
            Assert.AreEqual(_mockPlayerInput, inputs[1]);
        }

        [Test]
        public void PredictInput_ReturnsLastAuthoritativeInput()
        {
            // Arrange
            var localInputs = new Dictionary<byte, IPlayerInput> { { 1, _mockPlayerInput } };
            _inputsBuffer.SetLocalInputs(localInputs, 1);

            // Act
            var predictedInput = _inputsBuffer.PredictInput(1, 2);

            // Assert
            Assert.AreEqual(_mockPlayerInput, predictedInput);
        }

        [Test]
        public void PredictInput_NoAuthoritativeInput_ReturnsBlankInput()
        {
            // Act
            var predictedInput = _inputsBuffer.PredictInput(1, 2);

            // Assert
            Assert.AreEqual(_mockBlankPlayerInput, predictedInput);
        }

        [Test]
        public void SetLocalInputs_SetsInputsCorrectly()
        {
            // Arrange
            var localInputs = new Dictionary<byte, IPlayerInput> { { 1, _mockPlayerInput } };

            // Act
            _inputsBuffer.SetLocalInputs(localInputs, 1);
            var inputs = _inputsBuffer.GetInputsForTick(1);

            // Assert
            Assert.AreEqual(1, inputs.Count);
            Assert.AreEqual(_mockPlayerInput, inputs[1]);
        }

        [Test]
        public void GetMinimalInputsDiff_ReturnsChangedInputs()
        {
            // Arrange
            var localInputs1 = new Dictionary<byte, IPlayerInput> { { 1, _mockPlayerInput } };
            var localInputs2 = new Dictionary<byte, IPlayerInput> { { 1, Substitute.For<IPlayerInput>() } };
            _inputsBuffer.SetLocalInputs(localInputs1, 1);
            _inputsBuffer.SetLocalInputs(localInputs2, 2);

            // Act
            var inputsDiff = _inputsBuffer.GetMinimalInputsDiff(2);

            // Assert
            Assert.AreEqual(1, inputsDiff.Count);
            Assert.AreEqual(localInputs2[1], inputsDiff[1]);
        }

        [Test]
        public void SetPlayerInputsAtTick_SetsInputsCorrectly()
        {
            // Arrange
            var playerInputsDto = new PlayerInputsDTO
            {
                PlayerInputs = new Dictionary<byte, IPlayerInput> { { 1, _mockPlayerInput } }
            };

            // Act
            _inputsBuffer.SetPlayerInputsAtTick(playerInputsDto, 1);
            var inputs = _inputsBuffer.GetInputsForTick(1);

            // Assert
            Assert.AreEqual(1, inputs.Count);
            Assert.AreEqual(_mockPlayerInput, inputs[1]);
        }
    }
}
