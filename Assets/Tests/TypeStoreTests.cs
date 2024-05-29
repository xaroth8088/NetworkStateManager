using NUnit.Framework;
using NSubstitute;
using System;
using NSM;

namespace NSM.Tests {
    public struct InvalidGameState { }

    public struct InvalidPlayerInput { }

    public struct InvalidGameEvent { }

    [TestFixture]
    public class TypeStoreTests
    {
        [SetUp]
        public void Setup()
        {
            // Reset the singleton instance before each test
            TypeStore.Instance.ResetTypeStore();
        }

        [Test]
        public void GameStateType_SetValidType_Success()
        {
            var typeStore = TypeStore.Instance;
            typeStore.GameStateType = typeof(TestGameStateDTO);
            Assert.AreEqual(typeof(TestGameStateDTO), typeStore.GameStateType);
        }

        [Test]
        public void GameStateType_SetInvalidType_ThrowsException()
        {
            var typeStore = TypeStore.Instance;
            Assert.Throws<Exception>(() => typeStore.GameStateType = typeof(InvalidGameState));
        }

        [Test]
        public void PlayerInputType_SetValidType_Success()
        {
            var typeStore = TypeStore.Instance;
            typeStore.PlayerInputType = typeof(TestPlayerInputDTO);
            Assert.AreEqual(typeof(TestPlayerInputDTO), typeStore.PlayerInputType);
        }

        [Test]
        public void PlayerInputType_SetInvalidType_ThrowsException()
        {
            var typeStore = TypeStore.Instance;
            Assert.Throws<Exception>(() => typeStore.PlayerInputType = typeof(InvalidPlayerInput));
        }

        [Test]
        public void GameEventType_SetValidType_Success()
        {
            var typeStore = TypeStore.Instance;
            typeStore.GameEventType = typeof(TestGameEventDTO);
            Assert.AreEqual(typeof(TestGameEventDTO), typeStore.GameEventType);
        }

        [Test]
        public void GameEventType_SetInvalidType_ThrowsException()
        {
            var typeStore = TypeStore.Instance;
            Assert.Throws<Exception>(() => typeStore.GameEventType = typeof(InvalidGameEvent));
        }

        [Test]
        public void CreateBlankGameState_ReturnsInstance()
        {
            var typeStore = TypeStore.Instance;
            typeStore.GameStateType = typeof(TestGameStateDTO);
            var instance = typeStore.CreateBlankGameState();
            Assert.IsInstanceOf<TestGameStateDTO>(instance);
        }

        [Test]
        public void CreateBlankPlayerInput_ReturnsInstance()
        {
            var typeStore = TypeStore.Instance;
            typeStore.PlayerInputType = typeof(TestPlayerInputDTO);
            var instance = typeStore.CreateBlankPlayerInput();
            Assert.IsInstanceOf<TestPlayerInputDTO>(instance);
        }

        [Test]
        public void SingletonInstance_ReturnsSameInstance()
        {
            var instance1 = TypeStore.Instance;
            var instance2 = TypeStore.Instance;
            Assert.AreSame(instance1, instance2);
        }

        [Test]
        public void ResetTypeStore_ResetsSingletonInstance()
        {
            var instance1 = TypeStore.Instance;
            TypeStore.Instance.ResetTypeStore();
            var instance2 = TypeStore.Instance;
            Assert.AreNotSame(instance1, instance2);
        }
    }
}