using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using System.Collections.Generic;

namespace NSM.Tests
{
    public class StateBufferTests
    {
        [Test]
        public void Indexer_Get_NegativeIndex_LogsWarningAndThrowsKeyNotFoundException()
        {
            // Arrange
            var stateBuffer = new StateBuffer();

            // Act & Assert
            var ex = Assert.Throws<KeyNotFoundException>(() =>
            {
                var result = stateBuffer[-1];
            });

            LogAssert.Expect(LogType.Warning, "State for a negative game tick was requested to be read.");
        }

        [Test]
        public void Indexer_Set_NegativeIndex_LogsWarning()
        {
            // Arrange
            var stateBuffer = new StateBuffer();

            // Act
            stateBuffer[-1] = new StateFrameDTO();

            // Assert
            LogAssert.Expect(LogType.Warning, "State for a negative game tick was requested to be written.");
        }

        [Test]
        public void Indexer_Set_OverwriteAuthoritativeFrame_LogsWarning()
        {
            // Arrange
            var stateBuffer = new StateBuffer();
            var frame = new StateFrameDTO { authoritative = true };
            stateBuffer[0] = frame;

            // Act
            stateBuffer[0] = new StateFrameDTO();

            // Assert
            LogAssert.Expect(LogType.Warning, "Tried to overwrite an authoritative frame at tick 0.  This can happen in certain edge-cases, and is probably not a cause for concern.");
        }
    }
}