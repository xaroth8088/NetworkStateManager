using System;

namespace NSM
{
    public interface IGameState : ICloneable
    {
        // TODO: research if there's a more "normal" interface for doing this
        public byte[] GetBinaryRepresentation();

        public void RestoreFromBinaryRepresentation(byte[] bytes);
    }
}