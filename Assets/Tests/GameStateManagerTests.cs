using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Linq;

namespace NSM.Tests
{
    [TestFixture]
    public class GameStateManagerTests
    {
        private IGameEventsBuffer _gameEventsBuffer;
        private IInputsBuffer _inputsBuffer;
        private INetworkIdManager _networkIdManager;
        private IInternalNetworkStateManager _networkStateManager;
        private Scene _scene;
        private IStateBuffer _stateBuffer;

        [Test]
        public void AdvanceTime_IncrementsTicksCorrectly()
        {
            var gameStateManager = new GameStateManager(
                _networkStateManager,
                _gameEventsBuffer,
                _inputsBuffer,
                _stateBuffer,
                _networkIdManager,
                _scene
            );

            gameStateManager.AdvanceTime();

            Assert.AreEqual(1, gameStateManager.RealGameTick);
            Assert.AreEqual(1, gameStateManager.GameTick);
        }

        [Test(Description = "Capture the initial frame")]
        public void CaptureInitialFrame_CapturesCorrectState()
        {
            var gameStateManager = new GameStateManager(
                _networkStateManager,
                _gameEventsBuffer,
                _inputsBuffer,
                _stateBuffer,
                _networkIdManager,
                _scene
            );

            TypeStore.Instance.GameStateType = typeof(TestGameStateDTO);
            TypeStore.Instance.PlayerInputType = typeof(TestPlayerInputDTO);
            TypeStore.Instance.GameEventType = typeof(TestGameEventDTO);

            _networkStateManager.When(x => x.GetGameState(ref Arg.Any<IGameState>()))
                .Do(callInfo =>
                {
                    callInfo[0] = new TestGameStateDTO() { testValue = 123 };
                });

            gameStateManager.CaptureInitialFrame();

            // Was the assignment to the buffer called exactly once, at position 0?
            _stateBuffer.Received(1)[0] = Arg.Any<StateFrameDTO>();

            // Did the game state that was requested by NSM match what was stored in the buffer?
            StateFrameDTO capturedFrame = (StateFrameDTO)_stateBuffer.ReceivedCalls().First().GetArguments()[1];
            Assert.AreEqual(123, ((TestGameStateDTO)capturedFrame.GameState).testValue);

            TypeStore.Instance.ResetTypeStore();
        }

        [Test(Description = "Fails if TypeStore hasn't been initialized correctly")]
        public void CaptureInitialFrame_FailFrameCaptureWithoutTypeStoreInit()
        {
            var stateFrame = new StateFrameDTO();
            _stateBuffer[0] = stateFrame;
            var gameStateManager = new GameStateManager(
                _networkStateManager,
                _gameEventsBuffer,
                _inputsBuffer,
                _stateBuffer,
                _networkIdManager,
                _scene
            );

            Assert.Throws<ArgumentNullException>(() => gameStateManager.CaptureInitialFrame());
        }

        [Test]
        public void Constructor_InitializesCorrectly()
        {
            var gameStateManager = new GameStateManager(
                _networkStateManager,
                _gameEventsBuffer,
                _inputsBuffer,
                _stateBuffer,
                _networkIdManager,
                _scene
            );

            Assert.AreEqual(_gameEventsBuffer, gameStateManager.GameEventsBuffer);
            Assert.AreEqual(_networkIdManager, gameStateManager.NetworkIdManager);
            _networkIdManager.Received(1).SetupInitialNetworkIds(_scene);
        }

        [Test]
        public void Constructor_ThrowsException_WhenGameEventsBufferIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new GameStateManager(
                _networkStateManager,
                null,
                _inputsBuffer,
                _stateBuffer,
                _networkIdManager,
                _scene
            ));
        }

        [Test]
        public void Constructor_ThrowsException_WhenNetworkStateManagerIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new GameStateManager(
                null,
                _gameEventsBuffer,
                _inputsBuffer,
                _stateBuffer,
                _networkIdManager,
                _scene
            ));
        }

        [Test]
        public void GetMinimalInputsDiffForCurrentFrame_CallsInputsBufferCorrectly()
        {
            var inputsDiff = new Dictionary<byte, IPlayerInput>();
            _inputsBuffer.GetMinimalInputsDiff(Arg.Any<int>()).Returns(inputsDiff);
            var gameStateManager = new GameStateManager(
                _networkStateManager,
                _gameEventsBuffer,
                _inputsBuffer,
                _stateBuffer,
                _networkIdManager,
                _scene
            );

            var result = gameStateManager.GetMinimalInputsDiffForCurrentFrame();

            Assert.AreEqual(inputsDiff, result);
            _inputsBuffer.Received(1).GetMinimalInputsDiff(Arg.Any<int>());
        }

        [Test]
        public void PlayerInputsReceived_SetsInputsCorrectly()
        {
            var playerInputs = new PlayerInputsDTO();
            var gameStateManager = new GameStateManager(
                _networkStateManager,
                _gameEventsBuffer,
                _inputsBuffer,
                _stateBuffer,
                _networkIdManager,
                _scene
            );

            gameStateManager.PlayerInputsReceived(playerInputs, 5);

            _inputsBuffer.Received(1).SetPlayerInputsAtTick(playerInputs, 5);
        }

        [SetUp]
        public void SetUp()
        {
            _networkStateManager = Substitute.For<IInternalNetworkStateManager>();
            _gameEventsBuffer = Substitute.For<IGameEventsBuffer>();
            _inputsBuffer = Substitute.For<IInputsBuffer>();
            _stateBuffer = Substitute.For<IStateBuffer>();
            _networkIdManager = Substitute.For<INetworkIdManager>();
            _scene = new Scene();
        }

        private struct TestGameEventDTO : IGameEvent
        {
            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                throw new NotImplementedException();
            }
        }

        private struct TestGameStateDTO : IGameState
        {
            public byte testValue;

            public byte[] GetBinaryRepresentation()
            {
                byte[] retval = new byte[1];
                retval[0] = testValue;

                return retval;
            }

            public void RestoreFromBinaryRepresentation(byte[] bytes)
            {
                testValue = bytes[0];
            }
        }

        private struct TestPlayerInputDTO : IPlayerInput
        {
            public bool Equals(IPlayerInput other)
            {
                throw new NotImplementedException();
            }

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                throw new NotImplementedException();
            }
        }
    }
}