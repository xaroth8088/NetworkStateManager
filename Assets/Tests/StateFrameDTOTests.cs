using NUnit.Framework;
using UnityEngine;

namespace NSM.Tests
{
    [TestFixture]
    public class StateFrameDTOTests
    {
        private TestGameStateDTO _gameStateMock;

        [SetUp]
        public void Setup()
        {
            _gameStateMock = new TestGameStateDTO();
            TypeStore.Instance.GameStateType = typeof(TestGameStateDTO);
            TypeStore.Instance.PlayerInputType = typeof(TestPlayerInputDTO);
            TypeStore.Instance.GameEventType = typeof(TestGameEventDTO);
        }

        [TearDown]
        public void Teardown()
        {
            TypeStore.Instance.ResetTypeStore();
        }

        [Test]
        public void GetBinaryRepresentation_ReturnsNonNullByteArray()
        {
            StateFrameDTO stateFrame = new() { gameTick = 123, PhysicsState = new PhysicsStateDTO() };

            byte[] binaryRepresentation = stateFrame.GetBinaryRepresentation();

            Assert.IsNotNull(binaryRepresentation);
            Assert.IsNotEmpty(binaryRepresentation);
        }

        [Test]
        public void RestoreFromBinaryRepresentation_SetsPropertiesCorrectly()
        {
            StateFrameDTO originalStateFrame = new() { gameTick = 123, PhysicsState = new PhysicsStateDTO() };
            byte[] binaryRepresentation = originalStateFrame.GetBinaryRepresentation();

            StateFrameDTO newStateFrame = new();
            newStateFrame.RestoreFromBinaryRepresentation(binaryRepresentation);

            Assert.AreEqual(originalStateFrame.gameTick, newStateFrame.gameTick);
            Assert.AreEqual(originalStateFrame.PhysicsState, newStateFrame.PhysicsState);
        }

        [Test]
        public void Clone_ReturnsCorrectCopy()
        {
            StateFrameDTO originalStateFrame = new() { gameTick = 123, PhysicsState = new PhysicsStateDTO() };

            StateFrameDTO clonedStateFrame = (StateFrameDTO)originalStateFrame.Clone();

            Assert.AreEqual(originalStateFrame.gameTick, clonedStateFrame.gameTick);
            Assert.AreEqual(originalStateFrame.PhysicsState, clonedStateFrame.PhysicsState);
        }

        [Test]
        public void GameState_Getter_ReturnsCorrectGameState()
        {
            byte[] gameStateBytes = new byte[] { 0x01, 0x02, 0x03 };
            _gameStateMock.RestoreFromBinaryRepresentation(gameStateBytes);

            StateFrameDTO stateFrame = new();
            stateFrame.RestoreFromBinaryRepresentation(stateFrame.GetBinaryRepresentation());
            stateFrame.GameState = _gameStateMock;

            IGameState result = stateFrame.GameState;

            Assert.AreEqual(_gameStateMock, result);
        }

        [Test]
        public void GameState_Setter_SetsCorrectly()
        {
            _gameStateMock.testValue = 123;
            _gameStateMock.GetBinaryRepresentation();

            StateFrameDTO stateFrame = new()
            {
                GameState = _gameStateMock
            };

            Assert.AreEqual(new byte[] { 123 }, stateFrame.GameState.GetBinaryRepresentation());
        }

        [Test]
        public void GameState_Setter_WhenAuthoritative_LogsError()
        {
            StateFrameDTO stateFrame = new()
            {
                authoritative = true,
                GameState = _gameStateMock
            };

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "Tried to write game state to an authoritative frame");
        }
    }
}