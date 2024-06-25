using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
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

        [Test]
        public void Client_SyncToServerState_FastForward_NoNewEvents()
        {
            // Client has state A, then receives a server state from the future
            // It should:
            //  * move time to the server's time
            //  * apply the new state to the scene
            //  * move time to account for lag estimate
            // Assert:
            //  * has applied existing events in the buffer both before and after the server's time (but after the client's initial time)
            //  * realGameTick matches server time + lag
            //  * current frame's state matches server's state, were server state also advanced to lag time

            var _gameStateManager = new GameStateManager(
                _networkStateManager,
                _gameEventsBuffer,
                _inputsBuffer,
                _stateBuffer,
                _networkIdManager,
                _scene
            );
            _gameStateManager.SetRandomBase(123);

            // Arrange
            var initialTick = 5;
            var serverTick = 10;
            var estimatedLag = 2;

            // Initial frame
            var initialState = new StateFrameDTO
            {
                gameTick = 0,
                PhysicsState = new PhysicsStateDTO
                {
                    RigidBodyStates = new()
                },
                GameState = new TestGameStateDTO
                {
                    testValue = 123,
                }
            };
            _stateBuffer[initialTick].Returns(initialState);
            _inputsBuffer.GetInputsForTick(initialTick).Returns(new Dictionary<byte, IPlayerInput>());

            // Server's frame
            var serverState = new StateFrameDTO {
                gameTick = initialTick,
                PhysicsState = new PhysicsStateDTO {
                    RigidBodyStates = new()
                },
                GameState = new TestGameStateDTO
                {
                    testValue = 45,
                }
            };
            _stateBuffer[serverTick].Returns(serverState);

            var newGameEventsBuffer = Substitute.For<IGameEventsBuffer>();

            _inputsBuffer.GetInputsForTick(serverTick + estimatedLag).Returns(new Dictionary<byte, IPlayerInput>());

            // Act
            _gameStateManager.SyncToServerState(serverState, newGameEventsBuffer, serverTick, estimatedLag);

            // Assert
            // Ensure that the state has been updated correctly
            Assert.AreEqual(serverTick + estimatedLag, _gameStateManager.RealGameTick);
            _stateBuffer.Received()[serverTick] = serverState;

            // Validate events have been applied before and after the server's time
            _networkStateManager.Received().ApplyEvents(Arg.Any<HashSet<IGameEvent>>());
            _networkStateManager.Received().ApplyInputs(Arg.Any<Dictionary<byte, IPlayerInput>>());

            // Check that the state of the current frame matches the server's state advanced to lag time
            var currentFrameState = _stateBuffer[serverTick + estimatedLag];
            Assert.AreEqual(serverState, currentFrameState);  // TODO: this needs to take into account what happens during the simulation time on those lag frames
        }

        [Test]
        public void Client_SyncToServerState_FastForward_NewEvents()
        {
            // Client has state A, then receives a server state from the future
            // It should:
            //  * move time to the server's time
            //  * apply the new state to the scene
            //  * move time to account for lag estimate
            // Assert:
            //  * has NOT applied existing events in the buffer both before and after the server's time (but after the client's initial time)
            //    * the test should give a wholly different events buffer, so that we can test for this
            //  * has applied new events in the buffer both before and after the server's time (but after the client's initial time)
            //  * realGameTick matches server time + lag
            //  * current frame's state matches server's state, were server state also advanced to lag time
            Assert.IsFalse(true);
        }

        [Test]
        public void Client_SyncToServerState_SameTime_NoNewEvents()
        {
            // Client has state A, then receives a server state with the same tick as the client
            // It should:
            //  * apply the new state to the scene
            //  * move time to account for lag estimate
            // Assert:
            //  * has applied new events in the buffer during the lag window
            //  * realGameTick matches server time + lag
            //  * current frame's state matches server's state, were server state also advanced to lag time
            Assert.IsFalse(true);
        }

        [Test]
        public void Client_SyncToServerState_SameTime_NewEvents()
        {
            // Client has state A, then receives a server state with the same tick as the client
            // It should:
            //  * apply the new state to the scene
            //  * move time to account for lag estimate
            // Assert:
            //  * has NOT applied existing events in the buffer after the client's initial time
            //    * the test should give a wholly different events buffer, so that we can test for this
            //  * has applied new events in the buffer during the lag window
            //  * realGameTick matches server time + lag
            //  * current frame's state matches server's state, were server state also advanced to lag time
            Assert.IsFalse(true);
        }

        [Test]
        public void Client_SyncToServerState_Past_BeforeLastAuthoritative_NoNewEvents()
        {
            Assert.IsFalse(true);
        }

        [Test]
        public void Client_SyncToServerState_Past_BeforeLastAuthoritative_NewEvents()
        {
            Assert.IsFalse(true);
        }

        [Test]
        public void Client_SyncToServerState_Past_AfterLastAuthoritative_NoNewEvents()
        {
            Assert.IsFalse(true);
        }

        [Test]
        public void Client_SyncToServerState_Past_AfterLastAuthoritative_NewEvents()
        {
            Assert.IsFalse(true);
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

            TypeStore.Instance.GameStateType = typeof(TestGameStateDTO);
            TypeStore.Instance.GameEventType = typeof(TestGameEventDTO);
            TypeStore.Instance.PlayerInputType = typeof(TestPlayerInputDTO);
        }
    }
}