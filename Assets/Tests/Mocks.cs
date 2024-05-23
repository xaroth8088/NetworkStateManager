using System;
using Unity.Netcode;

namespace NSM.Tests
{
    public struct TestGameEventDTO : IGameEvent
    {
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            throw new NotImplementedException();
        }
    }

    public struct TestGameStateDTO : IGameState
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
        public bool Equals(IPlayerInput other)
        {
            throw new NotImplementedException();
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            throw new NotImplementedException();
        }
    }
}
