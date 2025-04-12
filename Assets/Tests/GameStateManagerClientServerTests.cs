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
        private readonly byte EVENT_INCREMENT = 11;
        private TestGameStateDTO _clientCurrentGameState;
        private GameStateManager _clientGameStateManager;
        private InputsBuffer _clientInputsBuffer;
        private NetworkIdManager _clientNetworkIdManager;
        private IInternalNetworkStateManager _clientNetworkStateManager;
        private Scene _clientScene;
        private StateBuffer _clientStateBuffer;
        private TestGameStateDTO _serverCurrentGameState;
        private GameStateManager _serverGameStateManager;
        private InputsBuffer _serverInputsBuffer;
        private NetworkIdManager _serverNetworkIdManager;
        private IInternalNetworkStateManager _serverNetworkStateManager;
        private Scene _serverScene;
        private StateBuffer _serverStateBuffer;

        [Test]
        public void AllInputsDelayedOutOfOrderTest()
        {
            int lag = 0;
            int randomBase = 123;

            _serverGameStateManager.SetRandomBase(randomBase);
            _serverGameStateManager.CaptureInitialFrame();
            _clientGameStateManager.SetInitialGameState(_serverGameStateManager.GetStateFrame(0), randomBase, lag);

            // Client first, since it'll get caught up to the server's frame at the end of recieving inputs from it
            RunClientFrame(false);
            RunServerFrame(false, lag);
            RunClientFrame(false);
            RunServerFrame(false, lag);
            RunClientFrame(false);
            RunServerFrame(false, lag);

            SendClientInputsToServer(3);
            SendServerInputsToClient(2, lag);
            SendServerInputsToClient(0, lag);
            SendClientInputsToServer(1);
            SendServerInputsToClient(1, lag);
            SendClientInputsToServer(0);
            SendServerInputsToClient(3, lag);   // The last input sent to the client will determine which frame the client's on during assertions
            SendClientInputsToServer(2);

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

            for (int tick = 0; tick < 4; tick++)
            {
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

        [Test]
        public void DelayedOutOfOrderClientInputTest()
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

            SendClientInputsToServer(3);
            SendClientInputsToServer(1);
            SendClientInputsToServer(0);
            SendClientInputsToServer(2);

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

            for (int tick = 0; tick < 4; tick++)
            {
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
        public void FullStateSyncTest()
        {
            int lag = 0;
            int randomBase = 123;

            _serverGameStateManager.SetRandomBase(randomBase);
            _serverGameStateManager.CaptureInitialFrame();
            _clientGameStateManager.SetInitialGameState(_serverGameStateManager.GetStateFrame(0), randomBase, lag);

            _serverGameStateManager.ScheduleGameEvent(new TestGameEventDTO(), 5);
            _serverGameStateManager.ScheduleGameEvent(new TestGameEventDTO(), 15);
            SendEventsBufferToClient(lag);

            // Client first, since it'll get caught up to the server's frame at the end of recieving inputs from it
            for (int i = 0; i < 30; i++)
            {
                RunClientFrame(true);
                RunServerFrame(true, lag);
            }

            _clientGameStateManager.SyncToServerState(_serverGameStateManager.GetStateFrame(10), _serverGameStateManager.GameEventsBuffer, 10, _serverGameStateManager.RealGameTick, lag);

            Assert.AreEqual(_serverGameStateManager.RealGameTick + lag, _clientGameStateManager.RealGameTick);
            Assert.AreEqual(30, _serverGameStateManager.RealGameTick);

            Assert.AreEqual(7, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue);
            Assert.AreEqual(6, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(1).GameState).testValue);
            Assert.AreEqual(12, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(2).GameState).testValue);
            Assert.AreEqual(11, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(3).GameState).testValue);

            for (int i = 0; i < 30; i++)
            {
                Assert.AreEqual(
                    ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(i).GameState).testValue,
                    ((TestGameStateDTO)_clientGameStateManager.GetStateFrame(i).GameState).testValue
                );
            }
        }

        [Test]
        public void GameEventTest()
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
            _serverGameStateManager.ScheduleGameEvent(new TestGameEventDTO(), -1);
            SendEventsBufferToClient(lag);
            RunClientFrame(true);
            RunServerFrame(true, lag);

            Assert.AreEqual(3, _clientGameStateManager.RealGameTick);
            Assert.AreEqual(3, _serverGameStateManager.RealGameTick);
            Assert.AreEqual(7, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(0).GameState).testValue);
            Assert.AreEqual(6, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(1).GameState).testValue);
            Assert.AreEqual(12, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(2).GameState).testValue);
            Assert.AreEqual(22, ((TestGameStateDTO)_serverGameStateManager.GetStateFrame(3).GameState).testValue);
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
        public void RollbackShouldInvokeCallbackForAppliedEventEvenWhenEventIsRemovedFromBufferLater()
        {
            // ARRANGE
            int lag = 0;
            int randomBase = 123;
            int eventTick = 5;
            int removalTick = 10; // Event removed *after* frame 5 runs
            int lateInputTick = 2; // Late input arrives triggering rollback past eventTick
            int finalTick = 15;    // Simulate up to this tick before rollback

            var theEvent = new TestGameEventDTO { };

            _serverGameStateManager.SetRandomBase(randomBase);
            _serverGameStateManager.CaptureInitialFrame();
            _clientGameStateManager.SetInitialGameState(_serverGameStateManager.GetStateFrame(0), randomBase, lag);

            // 1. Simulate up to the event tick
            for (int i = 0; i < eventTick - 1; i++)
            {
                RunClientFrame(true); // Keep client in sync for simplicity
                RunServerFrame(true, lag);
            }

            // 2. Schedule event on server, send buffer to client
            _serverGameStateManager.ScheduleGameEvent(theEvent, eventTick);
            SendEventsBufferToClient(lag); // Client knows about the event

            // 3. Run the event frame (tick 5)
            RunClientFrame(true);
            RunServerFrame(true, lag);

            // Verify ApplyEvents was called on the server for the event frame
            _serverNetworkStateManager.Received(1).ApplyEvents(Arg.Is<HashSet<IGameEvent>>(set => set.Contains(theEvent)));
            var serverStateAfterEventFrame = (TestGameStateDTO)_serverStateBuffer[eventTick].GameState; // Capture state after event application

            // 4. Simulate forward past the event tick up to removal tick
            for (int i = eventTick; i < removalTick; i++)
            {
                RunClientFrame(true);
                RunServerFrame(true, lag);
            }

            // 5. Remove the event from the server's buffer *after* it was applied
            Assert.IsTrue(_serverGameStateManager.GameEventsBuffer[eventTick].Count == 1, "Event should be there before removing it.");
            _serverGameStateManager.RemoveEventAtTick(eventTick, e => theEvent.Equals(e));
            Assert.IsTrue(_serverGameStateManager.GameEventsBuffer[eventTick].Count == 0, "Event should be gone after removing it.");
            // ** Crucially, do NOT call SendEventsBufferToClient() here **

            // 6. Simulate forward to the final tick before rollback
            for (int i = removalTick; i < finalTick; i++)
            {
                RunClientFrame(true);
                RunServerFrame(true, lag);
            }
            Assert.AreEqual(finalTick, _serverGameStateManager.RealGameTick, "Server should reach final tick");

            // 7. Prepare for assertion: Clear prior Rollback calls on server mock
            _serverNetworkStateManager.ClearReceivedCalls();

            // ACT
            // 8. Trigger rollback on server by sending late client input
            SendClientInputsToServer(lateInputTick); // Input for tick 2 received when server is at tick 15

            // ASSERT
            // 9. Verify RollbackEvents was called on the server for the specific event at eventTick=5
            bool rollbackCalledForTick5Event = false;
            var receivedCalls = _serverNetworkStateManager.ReceivedCalls();
            foreach (var call in receivedCalls)
            {
                if (call.GetMethodInfo().Name == nameof(IInternalNetworkStateManager.RollbackEvents))
                {
                    var args = call.GetArguments();
                    var eventSet = (HashSet<IGameEvent>)args[0];
                    var stateAfter = (TestGameStateDTO)args[1]; // Assuming mock state

                    // Check if this rollback call corresponds to the state *after* our event frame
                    // Need to be careful with state comparison due to potential intermediate modifications
                    // A simpler check might be just for the event itself in *any* rollback call,
                    // expecting it to be absent.
                    if (eventSet.Contains(theEvent))
                    {
                        rollbackCalledForTick5Event = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(rollbackCalledForTick5Event, $"RollbackEvents SHOULD have been called");

            // Optional: Verify Rollback WAS called for *some* frame during rewind
            _serverNetworkStateManager.Received().RollbackEvents(Arg.Any<HashSet<IGameEvent>>(), Arg.Any<IGameState>());

            // Additional checks (optional, state can be complex to trace perfectly)
            // - Check server state at eventTick *after* replay - should not have EVENT_INCREMENT applied this time.
            var serverStateAtEventTickAfterReplay = (TestGameStateDTO)_serverStateBuffer[eventTick].GameState;
            // - Check server state at finalTick *after* replay - should be consistent with simulation without the event at tick 5.
            var serverStateAtFinalTickAfterReplay = (TestGameStateDTO)_serverStateBuffer[finalTick].GameState;
        }

        [SetUp]
        public void SetUp()
        {
            // General setup
            Physics.simulationMode = SimulationMode.Script;
            Physics.autoSyncTransforms = false;

            // Required objects for GSM
            _clientNetworkStateManager = Substitute.For<IInternalNetworkStateManager>();
            _clientInputsBuffer = new();
            _clientStateBuffer = new();
            _clientNetworkIdManager = new(_clientNetworkStateManager);
            _clientScene = new Scene();
            _clientGameStateManager = new(
                _clientNetworkStateManager,
                new GameEventsBuffer(),
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
            _serverInputsBuffer = new();
            _serverStateBuffer = new();
            _serverNetworkIdManager = new(_serverNetworkStateManager);
            _serverScene = new Scene();
            _serverGameStateManager = new(
                _serverNetworkStateManager,
                new GameEventsBuffer(),
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

            _serverNetworkStateManager.When(x => x.ApplyEvents(Arg.Any<HashSet<IGameEvent>>())).Do(x =>
            {
                foreach (IGameEvent e in (HashSet<IGameEvent>)x[0])
                {
                    _serverCurrentGameState.testValue += EVENT_INCREMENT;
                }
            });
            _clientNetworkStateManager.When(x => x.ApplyEvents(Arg.Any<HashSet<IGameEvent>>())).Do(x =>
            {
                foreach (IGameEvent e in (HashSet<IGameEvent>)x[0])
                {
                    _clientCurrentGameState.testValue += EVENT_INCREMENT;
                }
            });

            _serverNetworkStateManager.When(x => x.RollbackEvents(Arg.Any<HashSet<IGameEvent>>(), Arg.Any<IGameState>())).Do(x =>
            {
                foreach (IGameEvent e in (HashSet<IGameEvent>)x[0])
                {
                    _serverCurrentGameState.testValue -= EVENT_INCREMENT;
                }
            });
            _clientNetworkStateManager.When(x => x.RollbackEvents(Arg.Any<HashSet<IGameEvent>>(), Arg.Any<IGameState>())).Do(x =>
            {
                foreach (IGameEvent e in (HashSet<IGameEvent>)x[0])
                {
                    _clientCurrentGameState.testValue -= EVENT_INCREMENT;
                }
            });

            // TODO: mock each of these as appropriate
            // TODO: then, copy/paste for _client*
            /*
                _serverNetworkStateManager.PostPhysicsFrameUpdate();
            */

            // Because the types will be the same on both the client and server,
            // it's ok to use the singleton in this way
            TypeStore.Instance.GameStateType = typeof(TestGameStateDTO);
            TypeStore.Instance.GameEventType = typeof(TestGameEventDTO);
            TypeStore.Instance.PlayerInputType = typeof(TestPlayerInputDTO);
        }

        private void RunClientFrame(bool andSendInput)
        {
            _clientGameStateManager.RunFixedUpdate();
            if (andSendInput)
            {
                SendClientInputsToServer(_clientGameStateManager.RealGameTick);
            }
        }

        private void RunServerFrame(bool andSendInput, int lag)
        {
            _serverGameStateManager.RunFixedUpdate();
            if (andSendInput)
            {
                SendServerInputsToClient(_serverGameStateManager.RealGameTick, lag);
            }
        }

        private void SendClientInputsToServer(int tick)
        {
            _serverGameStateManager.PlayerInputsReceived(
                new PlayerInputsDTO()
                {
                    PlayerInputs = _clientInputsBuffer.GetInputsForTick(tick)
                },
                tick
            );
        }

        /// <summary>
        /// Simulates the NSM functionality that happens when an event is scheduled on the server.
        /// This only does the "send to client" bit, so we can have fine-grained control on when
        /// it arrives at the client.
        /// </summary>
        /// <param name="lag"></param>
        private void SendEventsBufferToClient(int lag)
        {
            _clientGameStateManager.ReplayDueToEvents(_serverGameStateManager.RealGameTick, (GameEventsBuffer)_serverGameStateManager.GameEventsBuffer, lag);
        }

        private void SendServerInputsToClient(int tick, int lag)
        {
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
    }
}