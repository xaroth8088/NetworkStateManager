using NUnit.Framework;
using System;

namespace NSM.Tests
{
    public class RandomManagerTests
    {
        private RandomManager randomManager;
        private int randomSeedBase;

        [SetUp]
        public void SetUp()
        {
            randomSeedBase = 42;
            randomManager = new RandomManager(randomSeedBase);
        }

        [Test]
        public void ResetRandom_ShouldInitializeRandom()
        {
            // Arrange
            int tick = 1;

            // Act
            randomManager.ResetRandom(tick);

            // Assert
            Assert.DoesNotThrow(() => randomManager.GetRandomNext());
        }

        [Test]
        public void GetRandomNext_ShouldThrowExceptionIfNotInitialized()
        {
            // Act & Assert
            var ex = Assert.Throws<Exception>(() => randomManager.GetRandomNext());
            Assert.AreEqual("GetRandomNext() was called before StartNetworkStateManager(), which is not allowed", ex.Message);
        }

        [Test]
        public void GetRandomRange_Float_ShouldThrowExceptionIfNotInitialized()
        {
            // Act & Assert
            var ex = Assert.Throws<Exception>(() => randomManager.GetRandomRange(0f, 1f));
            Assert.AreEqual("GetRandomRange() was called before StartNetworkStateManager(), which is not allowed", ex.Message);
        }

        [Test]
        public void GetRandomRange_Int_ShouldThrowExceptionIfNotInitialized()
        {
            // Act & Assert
            var ex = Assert.Throws<Exception>(() => randomManager.GetRandomRange(0, 10));
            Assert.AreEqual("GetRandomRange() was called before StartNetworkStateManager(), which is not allowed", ex.Message);
        }

        [Test]
        public void GetRandomNext_ShouldReturnRandomNumber()
        {
            // Arrange
            int tick = 1;
            randomManager.ResetRandom(tick);

            // Act
            int randomNumber = randomManager.GetRandomNext();

            // Assert
            Assert.IsInstanceOf<int>(randomNumber);
        }

        [Test]
        public void GetRandomRange_Float_ShouldReturnRandomNumberInRange()
        {
            // Arrange
            int tick = 1;
            randomManager.ResetRandom(tick);

            // Act
            float randomNumber = randomManager.GetRandomRange(0f, 1f);

            // Assert
            Assert.That(randomNumber, Is.InRange(0f, 1f));
        }

        [Test]
        public void GetRandomRange_Int_ShouldReturnRandomNumberInRange()
        {
            // Arrange
            int tick = 1;
            randomManager.ResetRandom(tick);

            // Act
            int randomNumber = randomManager.GetRandomRange(0, 10);

            // Assert
            Assert.That(randomNumber, Is.InRange(0, 10));
        }

        [Test]
        public void RandomNumbers_ShouldBeDeterministic()
        {
            // Arrange
            int tick = 1;
            randomManager.ResetRandom(tick);

            // Act
            int[] firstRun = new int[5];
            for (int i = 0; i < 5; i++)
            {
                firstRun[i] = randomManager.GetRandomNext();
            }

            // Reinitialize to check for determinism
            randomManager.ResetRandom(tick);
            int[] secondRun = new int[5];
            for (int i = 0; i < 5; i++)
            {
                secondRun[i] = randomManager.GetRandomNext();
            }

            // Assert
            Assert.AreEqual(firstRun, secondRun);
        }

        [Test]
        public void RandomRanges_ShouldBeDeterministic()
        {
            // Arrange
            int tick = 1;
            randomManager.ResetRandom(tick);

            // Act
            float[] firstRunFloats = new float[5];
            for (int i = 0; i < 5; i++)
            {
                firstRunFloats[i] = randomManager.GetRandomRange(0f, 1f);
            }

            int[] firstRunInts = new int[5];
            for (int i = 0; i < 5; i++)
            {
                firstRunInts[i] = randomManager.GetRandomRange(0, 10);
            }

            // Reinitialize to check for determinism
            randomManager.ResetRandom(tick);
            float[] secondRunFloats = new float[5];
            for (int i = 0; i < 5; i++)
            {
                secondRunFloats[i] = randomManager.GetRandomRange(0f, 1f);
            }

            int[] secondRunInts = new int[5];
            for (int i = 0; i < 5; i++)
            {
                secondRunInts[i] = randomManager.GetRandomRange(0, 10);
            }

            // Assert
            Assert.AreEqual(firstRunFloats, secondRunFloats);
            Assert.AreEqual(firstRunInts, secondRunInts);
        }
    }
}