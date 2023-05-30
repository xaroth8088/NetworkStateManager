using Unity.Netcode;

namespace NSM
{
    public class StateFrameDeltaDTO : INetworkSerializable
    {
        public int gameTick;
        public byte[] _gameStateDiffBytes;
        public byte[] _physicsStateDiffBytes;
        public byte gameStateCRC;
        public byte physicsStateCRC;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            _gameStateDiffBytes ??= new byte[0];
            _physicsStateDiffBytes ??= new byte[0];

            serializer.SerializeValue(ref gameTick);
            serializer.SerializeValue(ref gameStateCRC);
            serializer.SerializeValue(ref physicsStateCRC);

            byte[] compressionBuffer = new byte[0];
            if (serializer.IsReader)
            {
                serializer.SerializeValue(ref compressionBuffer);
                _gameStateDiffBytes = Compression.DecompressBytes(compressionBuffer);
                serializer.SerializeValue(ref compressionBuffer);
                _physicsStateDiffBytes = Compression.DecompressBytes(compressionBuffer);
            }

            if (serializer.IsWriter)
            {
                compressionBuffer = Compression.CompressBytes(_gameStateDiffBytes);
                serializer.SerializeValue(ref compressionBuffer);
                compressionBuffer = Compression.CompressBytes(_physicsStateDiffBytes);
                serializer.SerializeValue(ref compressionBuffer);
            }
        }
    }
}
