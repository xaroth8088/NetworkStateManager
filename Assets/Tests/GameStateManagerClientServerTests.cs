using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NSM.Tests
{
    [TestFixture]
    public class GameStateManagerClientServerTests
    {
        private TestGameStateDTO _clientCurrentGameState;
        private GameEventsBuffer _clientGameEventsBuffer;
        private GameStateManager _clientGameStateManager;
        private InputsBuffer _clientInputsBuffer;
        private NetworkIdManager _clientNetworkIdManager;
        private IInternalNetworkStateManager _clientNetworkStateManager;
        private Scene _clientScene;
        private StateBuffer _clientStateBuffer;
        private TestGameStateDTO _serverCurrentGameState;
        private GameEventsBuffer _serverGameEventsBuffer;
        private GameStateManager _serverGameStateManager;
        private InputsBuffer _serverInputsBuffer;
        private NetworkIdManager _serverNetworkIdManager;
        private IInternalNetworkStateManager _serverNetworkStateManager;
        private Scene _serverScene;
        private StateBuffer _serverStateBuffer;

        [Test]
        public void BasicSetupTest()
        {
            int lag = 3;
            int randomBase = 123;

            _serverGameStateManager.SetRandomBase(randomBase);
            _serverGameStateManager.CaptureInitialFrame();
            _clientGameStateManager.SetInitialGameState(_serverGameStateManager.GetStateFrame(0), randomBase, lag);

            Assert.AreEqual(0, _serverGameStateManager.RealGameTick);
            Assert.AreEqual(3, _clientGameStateManager.RealGameTick);
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(0).GameState).testValue
            );
        }

        [Test]
        public void FirstFewFramesTest()
        {
            int lag = 3;
            int randomBase = 123;

            _serverGameStateManager.SetRandomBase(randomBase);
            _serverGameStateManager.CaptureInitialFrame();
            _clientGameStateManager.SetInitialGameState(_serverGameStateManager.GetStateFrame(0), randomBase, lag);

            RunServerFrame(true, lag);
            RunServerFrame(true, lag);
            RunServerFrame(true, lag);

            Assert.AreEqual(6, _clientGameStateManager.RealGameTick);   // 6, because of lag compensation
            Assert.AreEqual(3, _serverGameStateManager.RealGameTick);
            Assert.AreEqual(7, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue);
            Assert.AreEqual(8, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(1).GameState).testValue);
            Assert.AreEqual(14, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(2).GameState).testValue);
            Assert.AreEqual(15, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(3).GameState).testValue);
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(0).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(1).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(1).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(2).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(2).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(3).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(3).GameState).testValue
            );
        }

       [Test]
        public void DelayedServerToClientInputTest()
        {
            int lag = 3;
            int randomBase = 123;

            _serverGameStateManager.SetRandomBase(randomBase);
            _serverGameStateManager.CaptureInitialFrame();
            _clientGameStateManager.SetInitialGameState(_serverGameStateManager.GetStateFrame(0), randomBase, lag);

            RunServerFrame(false, lag);
            RunServerFrame(false, lag);
            RunServerFrame(false, lag);

            for(int tick = 0; tick < 4; tick++) {
                SendServerInputsToClient(tick, lag);
            }

            Assert.AreEqual(6, _clientGameStateManager.RealGameTick);   // 6, because of lag compensation
            Assert.AreEqual(3, _serverGameStateManager.RealGameTick);
            Assert.AreEqual(7, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue);
            Assert.AreEqual(8, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(1).GameState).testValue);
            Assert.AreEqual(14, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(2).GameState).testValue);
            Assert.AreEqual(15, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(3).GameState).testValue);
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(0).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(1).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(1).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(2).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(2).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(3).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(3).GameState).testValue
            );
        }

        private void RunServerFrame(bool andSendInput, int lag) {
            _serverGameStateManager.RunFixedUpdate();
            if(andSendInput) {
                SendServerInputsToClient(_serverGameStateManager.RealGameTick, lag);
            }
        }

        private void RunClientFrame(bool andSendInput) {
            _clientGameStateManager.RunFixedUpdate();
            if (andSendInput) {
                SendClientInputsToServer(_clientGameStateManager.RealGameTick);
            }
        }

        private void SendServerInputsToClient(int tick, int lag) {
            _clientGameStateManager.ReplayDueToInputs(
                new PlayerInputsDTO()
                {
                    PlayerInputs = _serverInputsBuffer.GetInputsForTick(tick)
                },
                tick,
                tick,
                lag
            );
        }

        private void SendClientInputsToServer(int tick) {
            _serverGameStateManager.PlayerInputsReceived(
                new PlayerInputsDTO()
                {
                    PlayerInputs = _clientInputsBuffer.GetInputsForTick(tick)
                },
                tick
            );
        }

        [Test]
        public void ClientAndServerRunningInSyncTest()
        {
            int lag = 0;
            int randomBase = 123;

            _serverGameStateManager.SetRandomBase(randomBase);
            _serverGameStateManager.CaptureInitialFrame();
            _clientGameStateManager.SetInitialGameState(_serverGameStateManager.GetStateFrame(0), randomBase, lag);

            // Client first, since it'll get caught up to the server's frame at the end of recieving inputs from it
            RunClientFrame(true);
            RunServerFrame(true, lag);
            RunClientFrame(true);
            RunServerFrame(true, lag);
            RunClientFrame(true);
            RunServerFrame(true, lag);

            Assert.AreEqual(3, _clientGameStateManager.RealGameTick);
            Assert.AreEqual(3, _serverGameStateManager.RealGameTick);
            Assert.AreEqual(7, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue);
            Assert.AreEqual(6, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(1).GameState).testValue);
            Assert.AreEqual(12, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(2).GameState).testValue);
            Assert.AreEqual(11, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(3).GameState).testValue);
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(0).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(1).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(1).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(2).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(2).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(3).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(3).GameState).testValue
            );
        }

        [Test]
        public void DelayedClientInputTest()
        {
            int lag = 0;
            int randomBase = 123;

            _serverGameStateManager.SetRandomBase(randomBase);
            _serverGameStateManager.CaptureInitialFrame();
            _clientGameStateManager.SetInitialGameState(_serverGameStateManager.GetStateFrame(0), randomBase, lag);

            // Client first, since it'll get caught up to the server's frame at the end of recieving inputs from it
            RunClientFrame(false);
            RunServerFrame(true, lag);
            RunClientFrame(false);
            RunServerFrame(true, lag);
            RunClientFrame(false);
            RunServerFrame(true, lag);

            for(int tick = 0; tick < 4; tick++) {
                SendClientInputsToServer(tick);
            }

            Assert.AreEqual(3, _clientGameStateManager.RealGameTick);
            Assert.AreEqual(3, _serverGameStateManager.RealGameTick);
            Assert.AreEqual(7, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue);
            Assert.AreEqual(6, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(1).GameState).testValue);
            Assert.AreEqual(12, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(2).GameState).testValue);
            Assert.AreEqual(11, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(3).GameState).testValue);
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(0).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(1).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(1).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(2).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(2).GameState).testValue
            );
            Assert.AreEqual(
                ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(3).GameState).testValue,
                ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(3).GameState).testValue
            );
        }

        [SetUp]
        public void SetUp()
        {
            // General setup
            Physics.simulationMode = SimulationMode.Script;
            Physics.autoSyncTransforms = false;

            // Required objects for GSM
            _clientNetworkStateManager = Substitute.For<IInternalNetworkStateManager>();
            _clientGameEventsBuffer = new();
            _clientInputsBuffer = new();
            _clientStateBuffer = new();
            _clientNetworkIdManager = new(_clientNetworkStateManager);
            _clientScene = new Scene();
            _clientGameStateManager = new(
                _clientNetworkStateManager,
                _clientGameEventsBuffer,
                _clientInputsBuffer,
                _clientStateBuffer,
                _clientNetworkIdManager,
                _clientScene
            );
            _clientCurrentGameState = new()
            {
                testValue = 7
            };

            _serverNetworkStateManager = Substitute.For<IInternalNetworkStateManager>();
            _serverGameEventsBuffer = new();
            _serverInputsBuffer = new();
            _serverStateBuffer = new();
            _serverNetworkIdManager = new(_serverNetworkStateManager);
            _serverScene = new Scene();
            _serverGameStateManager = new(
                _serverNetworkStateManager,
                _serverGameEventsBuffer,
                _serverInputsBuffer,
                _serverStateBuffer,
                _serverNetworkIdManager,
                _serverScene
            );
            _serverCurrentGameState = new()
            {
                testValue = 7
            };

            // Mocks
            // Player 0 is on the server, player 1 is on the client
            // Player 0 presses the button on even-numbered ticks, player 1 on the odds
            _serverNetworkStateManager
                .When(x => x.GetInputs(ref Arg.Any<Dictionary<byte, IPlayerInput>>()))
                .Do(x =>
                {
                    Dictionary<byte, IPlayerInput> mockServerInput = new()
                    {
                        [0] = new TestPlayerInputDTO()
                        {
                            buttonWasPressed = _serverGameStateManager.GameTick % 2 == 0
                        }
                    };
                    x[0] = mockServerInput;
                });

            _clientNetworkStateManager
                .When(x => x.GetInputs(ref Arg.Any<Dictionary<byte, IPlayerInput>>()))
                .Do(x =>
                {
                    Dictionary<byte, IPlayerInput> mockClientInput = new()
                    {
                        [1] = new TestPlayerInputDTO()
                        {
                            buttonWasPressed = _clientGameStateManager.GameTick % 2 != 0
                        }
                    };
                    x[0] = mockClientInput;
                });

            _serverNetworkStateManager.When(x => x.ApplyState(Arg.Any<IGameState>())).Do(x =>
            {
                _serverCurrentGameState = (TestGameStateDTO)x[0];
            });
            _clientNetworkStateManager.When(x => x.ApplyState(Arg.Any<IGameState>())).Do(x =>
            {
                _clientCurrentGameState = (TestGameStateDTO)x[0];
            });

            _serverNetworkStateManager.When(x => x.ApplyInputs(Arg.Any<Dictionary<byte, IPlayerInput>>())).Do(x =>
            {
                Dictionary<byte, IPlayerInput> playerInputs = (Dictionary<byte, IPlayerInput>)x[0];
                if (playerInputs.TryGetValue(0, out IPlayerInput input))
                {
                    if (((TestPlayerInputDTO)input).buttonWasPressed)
                    {
                        _serverCurrentGameState.testValue += 5;
                    }
                }
                if (playerInputs.TryGetValue(1, out input))
                {
                    if (((TestPlayerInputDTO)input).buttonWasPressed)
                    {
                        _serverCurrentGameState.testValue -= 2;
                    }
                }
            });
            _clientNetworkStateManager.When(x => x.ApplyInputs(Arg.Any<Dictionary<byte, IPlayerInput>>())).Do(x =>
            {
                Dictionary<byte, IPlayerInput> playerInputs = (Dictionary<byte, IPlayerInput>)x[0];
                if (playerInputs.TryGetValue(0, out IPlayerInput input))
                {
                    if (((TestPlayerInputDTO)input).buttonWasPressed)
                    {
                        _clientCurrentGameState.testValue += 5;
                    }
                }
                if (playerInputs.TryGetValue(1, out input))
                {
                    if (((TestPlayerInputDTO)input).buttonWasPressed)
                    {
                        _clientCurrentGameState.testValue -= 2;
                    }
                }
            });

            _serverNetworkStateManager.When(x => x.GetGameState(ref Arg.Any<IGameState>())).Do(x =>
            {
                x[0] = _serverCurrentGameState;
            });
            _clientNetworkStateManager.When(x => x.GetGameState(ref Arg.Any<IGameState>())).Do(x =>
            {
                x[0] = _clientCurrentGameState;
            });

            _serverNetworkStateManager.When(x => x.PrePhysicsFrameUpdate()).Do(x =>
            {
                _serverCurrentGameState.testValue++;
            });
            _clientNetworkStateManager.When(x => x.PrePhysicsFrameUpdate()).Do(x =>
            {
                _clientCurrentGameState.testValue++;
            });

            // TODO: mock each of these as appropriate
            // TODO: then, copy/paste for _client*
            /*
                _serverNetworkStateManager.RollbackEvents();
                _serverNetworkStateManager.ApplyEvents();
                _serverNetworkStateManager.PostPhysicsFrameUpdate();
            */

            // Because the types will be the same on both the client and server,
            // it's ok to use the singleton in this way
            TypeStore.Instance.GameStateType = typeof(TestGameStateDTO);
            TypeStore.Instance.GameEventType = typeof(TestGameEventDTO);
            TypeStore.Instance.PlayerInputType = typeof(TestPlayerInputDTO);
        }
    }
}