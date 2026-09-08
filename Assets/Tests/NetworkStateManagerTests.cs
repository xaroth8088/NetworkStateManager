using System;
using System.Collections.Generic;
using System.Reflection;
using NSubstitute;
using NSubstitute.Extensions;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NSM.Tests
{
    public class NetworkStateManagerTests
    {
        private NetworkStateManager _networkStateManager;
        private GameStateManager _gameStateManagerMock;

        private void InitializeNetworkStateManager(bool isHost)
        {
            if (_networkStateManager != null) UnityEngine.Object.DestroyImmediate(_networkStateManager.gameObject);
            // Create a new GameObject
            var gameObject = new GameObject();

            // Prep to mock IsHost

            PropertyInfo IsHostProperty = typeof(NetworkBehaviour).GetProperty(
                "IsHost",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
            );


            // Add NetworkStateManager component to the GameObject
            _networkStateManager = gameObject.AddComponent<NetworkStateManager>();

            // Mock IsHost
            IsHostProperty.SetValue(_networkStateManager, isHost);
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(_networkStateManager, isHost);

            // Mock the GameStateManager
            _gameStateManagerMock = new GameStateManager(
                _networkStateManager,
                new GameEventsBuffer(),
                new InputsBuffer(),
                new StateBuffer(),
                new NetworkIdManager(_networkStateManager),
                SceneManager.GetActiveScene()
            );

            // Use reflection to set the private gameStateManager field in the NetworkStateManager instance
            var field = typeof(NetworkStateManager).GetField(
                "gameStateManager",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            field.SetValue(_networkStateManager, _gameStateManagerMock);
        }

        [TearDown]
        public void TearDown()
        {
            if (_networkStateManager != null) UnityEngine.Object.DestroyImmediate(_networkStateManager.gameObject);
            TypeStore.Instance.ResetTypeStore();
        }

        [SetUp]
        public void SetUp()
        {
            InitializeNetworkStateManager(true);

            TypeStore.Instance.GameEventType = typeof(TestGameEventDTO);
            TypeStore.Instance.GameStateType = typeof(TestGameStateDTO);
            TypeStore.Instance.PlayerInputType = typeof(TestPlayerInputDTO);
        }

        [Test]
        public void ScheduleGameEvent_IsHost_CallsGameStateManager()
        {
            var gameEventMock = Substitute.For<IGameEvent>();
            int eventTick = 10;

            _networkStateManager.ScheduleGameEvent(gameEventMock, eventTick);

            Assert.That(_gameStateManagerMock.GameEventsBuffer[eventTick], Does.Contain(gameEventMock));
        }

        [Test]
        public void ScheduleGameEvent_IsNotHost_DoesNotCallGameStateManager()
        {
            InitializeNetworkStateManager(false);

            var gameEventMock = Substitute.For<IGameEvent>();
            int eventTick = 10;

            _networkStateManager.ScheduleGameEvent(gameEventMock, eventTick);

            Assert.That(_gameStateManagerMock.GameEventsBuffer[eventTick], Is.Empty);
        }

        [Test]
        public void RemoveEventAtTick_CallsGameStateManager()
        {
            int eventTick = 10;
            Predicate<IGameEvent> gameEventPredicate = Substitute.For<Predicate<IGameEvent>>();

            _networkStateManager.RemoveEventAtTick(eventTick, gameEventPredicate);

            Assert.That(_gameStateManagerMock.GameEventsBuffer[eventTick], Is.Empty);
        }

        [Test]
        public void RemoveEventAtTick_IsNotHost_DoesNotCallGameStateManager()
        {
            InitializeNetworkStateManager(false);

            int eventTick = 10;
            Predicate<IGameEvent> gameEventPredicate = Substitute.For<Predicate<IGameEvent>>();

            _networkStateManager.RemoveEventAtTick(eventTick, gameEventPredicate);

            Assert.That(_gameStateManagerMock.GameEventsBuffer[eventTick], Is.Empty);
        }

        [Test]
        public void PredictInputForPlayer_CallsGameStateManager()
        {
            byte playerId = 1;

            _networkStateManager.PredictInputForPlayer(playerId);

            Assert.That(_networkStateManager.PredictInputForPlayer(playerId), Is.TypeOf<TestPlayerInputDTO>());
        }

        [Test]
        public void PredictInputForPlayer_NoPreviousInput_ReturnsBlankInput()
        {
            // Ensure no prior inputs for the player
            byte playerId = 2;
            // Predict input when there's no input history
            IPlayerInput predictedInput = _networkStateManager.PredictInputForPlayer(playerId);

            // It should return a blank input of the correct type
            Assert.IsInstanceOf<TestPlayerInputDTO>(predictedInput);
        }

        [Test]
        public void ApplyEvents_RaisesOnApplyEvents()
        {
            var events = new HashSet<IGameEvent> { new TestGameEventDTO() };
            var onApplyEventsMock =
                Substitute.For<NetworkStateManager.ApplyEventsDelegateHandler>();

            _networkStateManager.OnApplyEvents += onApplyEventsMock;

            _networkStateManager.ApplyEvents(events);

            onApplyEventsMock.Received().Invoke(events);
        }

        [Test]
        public void ApplyEvents_DoesNotRaiseOnApplyEventsWithEmptySet()
        {
            var events = new HashSet<IGameEvent>();
            var onApplyEventsMock =
                Substitute.For<NetworkStateManager.ApplyEventsDelegateHandler>();

            _networkStateManager.OnApplyEvents += onApplyEventsMock;

            _networkStateManager.ApplyEvents(events);

            onApplyEventsMock.DidNotReceiveWithAnyArgs().Invoke(events);
        }

        [Test]
        public void RollbackEvents_DoesNotRaiseOnRollbackEventsWhenNoEvents()
        {
            var events = new HashSet<IGameEvent>();
            var gameStateMock = Substitute.For<IGameState>();
            var onRollbackEventsMock =
                Substitute.For<NetworkStateManager.RollbackEventsDelegateHandler>();

            _networkStateManager.OnRollbackEvents += onRollbackEventsMock;

            _networkStateManager.RollbackEvents(events, gameStateMock);

            onRollbackEventsMock.DidNotReceive().Invoke(events, gameStateMock);
        }

        [Test]
        public void RollbackEvents_RaisesOnRollbackEvents()
        {
            var events = new HashSet<IGameEvent> { new TestGameEventDTO() };
            var gameStateMock = Substitute.For<IGameState>();
            var onRollbackEventsMock =
                Substitute.For<NetworkStateManager.RollbackEventsDelegateHandler>();

            _networkStateManager.OnRollbackEvents += onRollbackEventsMock;

            _networkStateManager.RollbackEvents(events, gameStateMock);

            onRollbackEventsMock.Received().Invoke(events, gameStateMock);
        }

        [Test]
        public void ApplyInputs_RaisesOnApplyInputs()
        {
            var playerInputs = new Dictionary<byte, IPlayerInput>
            {
                { 123, new TestPlayerInputDTO() },
            };
            var onApplyInputsMock =
                Substitute.For<NetworkStateManager.ApplyInputsDelegateHandler>();

            _networkStateManager.OnApplyInputs += onApplyInputsMock;

            _networkStateManager.ApplyInputs(playerInputs);

            onApplyInputsMock.Received().Invoke(playerInputs);
        }

        [Test]
        public void ApplyInputs_DoesNotRaiseOnApplyInputsWithEmptySet()
        {
            var playerInputs = new Dictionary<byte, IPlayerInput>();
            var onApplyInputsMock =
                Substitute.For<NetworkStateManager.ApplyInputsDelegateHandler>();

            _networkStateManager.OnApplyInputs += onApplyInputsMock;

            _networkStateManager.ApplyInputs(playerInputs);

            onApplyInputsMock.DidNotReceiveWithAnyArgs().Invoke(playerInputs);
        }

        [Test]
        public void ApplyState_RaisesOnApplyState()
        {
            var gameStateMock = Substitute.For<IGameState>();
            var onApplyStateMock = Substitute.For<NetworkStateManager.ApplyStateDelegateHandler>();

            _networkStateManager.OnApplyState += onApplyStateMock;

            _networkStateManager.ApplyState(gameStateMock);

            onApplyStateMock.Received().Invoke(gameStateMock);
        }

        [Test]
        public void GetGameState_RaisesOnGetGameState()
        {
            var gameStateMock = Substitute.For<IGameState>();
            var onGetGameStateMock =
                Substitute.For<NetworkStateManager.OnGetGameStateDelegateHandler>();

            _networkStateManager.OnGetGameState += onGetGameStateMock;

            _networkStateManager.GetGameState(ref gameStateMock);

            onGetGameStateMock.Received().Invoke(ref gameStateMock);
        }

        [Test]
        public void GetInputs_RaisesOnGetInputs()
        {
            var inputs = new Dictionary<byte, IPlayerInput>();
            var onGetInputsMock = Substitute.For<NetworkStateManager.OnGetInputsDelegateHandler>();

            _networkStateManager.OnGetInputs += onGetInputsMock;

            _networkStateManager.GetInputs(ref inputs);

            onGetInputsMock.Received().Invoke(ref inputs);
        }

        [Test]
        public void PostPhysicsFrameUpdate_RaisesOnPostPhysicsFrameUpdate()
        {
            var onPostPhysicsFrameUpdateMock =
                Substitute.For<NetworkStateManager.OnPostPhysicsFrameUpdateDelegateHandler>();

            _networkStateManager.OnPostPhysicsFrameUpdate += onPostPhysicsFrameUpdateMock;

            _networkStateManager.PostPhysicsFrameUpdate();

            onPostPhysicsFrameUpdateMock.Received().Invoke();
        }

        [Test]
        public void PrePhysicsFrameUpdate_RaisesOnPrePhysicsFrameUpdate()
        {
            var onPrePhysicsFrameUpdateMock =
                Substitute.For<NetworkStateManager.OnPrePhysicsFrameUpdateDelegateHandler>();

            _networkStateManager.OnPrePhysicsFrameUpdate += onPrePhysicsFrameUpdateMock;

            _networkStateManager.PrePhysicsFrameUpdate();

            onPrePhysicsFrameUpdateMock.Received().Invoke();
        }

        [Test]
        public void StateBuffer_OverwritingAuthoritativeFrame_ReplacesFrame()
        {
            var stateBuffer = new StateBuffer();
            // Create two state frames for the same tick
            StateFrameDTO frameAuthoritative = new StateFrameDTO
            {
                gameTick = 5,
                authoritative = true,
                PhysicsState = new PhysicsStateDTO(),
            };
            StateFrameDTO framePredicted = new StateFrameDTO
            {
                gameTick = 5,
                authoritative = false,
                PhysicsState = new PhysicsStateDTO(),
            };

            // Store the authoritative frame first
            stateBuffer[5] = frameAuthoritative;
            StateFrameDTO storedFrame1 = stateBuffer[5];
            // Overwrite with a predicted frame at the same tick
            stateBuffer[5] = framePredicted;
            StateFrameDTO storedFrame2 = stateBuffer[5];

            // The first stored frame should be marked authoritative
            Assert.IsTrue(storedFrame1.authoritative);
            // After overwriting, the stored frame should be the new one (non-authoritative)
            Assert.IsFalse(storedFrame2.authoritative);
        }
    }
}
