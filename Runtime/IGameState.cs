namespace NSM
{
    public interface IGameState
    {
        // TODO: see if we can get ISerializable to work for this instead
        //       This may be tricky to do in a safe, efficient way since we shouldn't be using BinaryFormatter (the obvious choice for this purpose).
        public byte[] GetBinaryRepresentation();

        public void RestoreFromBinaryRepresentation(byte[] bytes);
    }
}