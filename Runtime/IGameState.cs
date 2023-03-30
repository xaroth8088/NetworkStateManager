using System;

namespace NSM
{
    public interface IGameState
    {
        // TODO: see if we can get ISerializable to work for this instead
        public byte[] GetBinaryRepresentation();

        public void RestoreFromBinaryRepresentation(byte[] bytes);
    }
}