using System;
using Unity.Netcode;

namespace NSM.Tests
{
    public struct TestGameEventDTO : IGameEvent, IEquatable<TestGameEventDTO>
    {
        public int EventValue { get; set; }

        public bool Equals(TestGameEventDTO other) => EventValue == other.EventValue; // Basic equality
        public override bool Equals(object obj) => obj is TestGameEventDTO other && Equals(other);
        public override int GetHashCode() => EventValue.GetHashCode();

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            int value = EventValue;
            serializer.SerializeValue(ref value);
            EventValue = value;
        }
    }

    [Serializable]
    public partial struct TestGameStateDTO : IGameState
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

    public struct TestPlayerInputDTO : IPlayerInput
    {
        public bool buttonWasPressed;

        public bool Equals(IPlayerInput other)
        {
            TestPlayerInputDTO otherInput = (TestPlayerInputDTO)other;
            return buttonWasPressed == otherInput.buttonWasPressed;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref buttonWasPressed);
        }
    }
}
