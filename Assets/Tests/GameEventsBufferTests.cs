using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;

namespace NSM.Tests
{
    public class GameEventsBufferTests
    {
        private GameEventsBuffer _buffer;

        [SetUp]
        public void Setup()
        {
            _buffer = new GameEventsBuffer();
        }

        [Test]
        public void Indexer_Get_ReturnsEmptyHashSetWhenNoEvents()
        {
            var events = _buffer[0];
            Assert.IsNotNull(events);
            Assert.IsEmpty(events);
        }

        [Test]
        public void Indexer_Set_StoresEvents()
        {
            var eventSet = new HashSet<IGameEvent>
            {
                new TestGameEventDTO(),
                new TestGameEventDTO()
            };

            _buffer[1] = eventSet;

            var retrievedEvents = _buffer[1];

            Assert.AreEqual(eventSet.Count, retrievedEvents.Count);
            CollectionAssert.AreEquivalent(eventSet, retrievedEvents);
        }

        [Test]
        public void Indexer_Get_ReturnsStoredEvents()
        {
            var eventSet = new HashSet<IGameEvent>
            {
                new TestGameEventDTO(),
                new TestGameEventDTO()
            };

            _buffer[2] = eventSet;

            var retrievedEvents = _buffer[2];

            Assert.AreEqual(eventSet.Count, retrievedEvents.Count);
            CollectionAssert.AreEquivalent(eventSet, retrievedEvents);
        }

        [Test]
        public void Vacuum_RemovesEmptyEventLists()
        {
            _buffer[3] = new HashSet<IGameEvent>();
            _buffer[4] = new HashSet<IGameEvent> { new TestGameEventDTO() };

            _buffer.InvokePrivateMethod("Vacuum");

            Assert.IsFalse(_buffer.ContainsKey(3));
            Assert.IsTrue(_buffer.ContainsKey(4));
        }

        [Test]
        public void Vacuum_DoesNotRemoveNonEmptyEventLists()
        {
            _buffer[5] = new HashSet<IGameEvent> { new TestGameEventDTO() };

            _buffer.InvokePrivateMethod("Vacuum");

            Assert.IsTrue(_buffer.ContainsKey(5));
        }
    }

    public static class TestExtensions
    {
        public static void InvokePrivateMethod(this object obj, string methodName, params object[] parameters)
        {
            var methodInfo = obj.GetType().GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            methodInfo.Invoke(obj, parameters);
        }

        public static bool ContainsKey(this GameEventsBuffer buffer, int key)
        {
            var fieldInfo = typeof(GameEventsBuffer).GetField("_upcomingEvents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dictionary = (Dictionary<int, HashSet<IGameEvent>>)fieldInfo.GetValue(buffer);
            return dictionary.ContainsKey(key);
        }
    }
}
