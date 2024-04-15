using MemoryPack;
using System;
using Unity.Netcode;
using UnityEngine;

namespace NSM
{
    [MemoryPackable]
    public partial struct StateFrameDTO : ICloneable, INetworkSerializable
    {
        [MemoryPackIgnore]
        public bool authoritative;

        // TODO: While gameTick is nice as a safety measure of sorts, we probably don't need it, so look into removing it
        public int gameTick;
        public PhysicsStateDTO PhysicsState;
        private byte[] _gameStateBytes;

        [MemoryPackIgnore]
        public IGameState GameState
        {
            get
            {
                if (_gameStateBytes == null)
                {
                    return null;
                }

                IGameState gameState = TypeStore.Instance.CreateBlankGameState();
                gameState.RestoreFromBinaryRepresentation(_gameStateBytes);
                return gameState;
            }
            set
            {
                if (authoritative)
                {
                    Debug.LogError("Tried to write game state to an authoritative frame");
                }
                _gameStateBytes = (byte[])value.GetBinaryRepresentation().Clone();

                // Did something fail during serialization?
                if (_gameStateBytes == null)
                {
                    throw new Exception("Gamestate serialization to bytes failed");
                }
            }
        }

        #region Serialization

        public object Clone()
        {
            StateFrameDTO newFrame = new();
            newFrame.RestoreFromBinaryRepresentation(GetBinaryRepresentation());

            return newFrame;
        }

        public byte[] GetBinaryRepresentation()
        {
            return MemoryPackSerializer.Serialize(this);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            // TODO: some sort of metrics around how much data is sent, original data size, original size of target
            if (serializer.IsWriter)
            {
                byte[] compressionBuffer = Compression.CompressBytes(GetBinaryRepresentation());
                serializer.SerializeValue(ref compressionBuffer);
            }

            if (serializer.IsReader)
            {
                byte[] compressionBuffer = new byte[0];
                if (serializer.IsReader)
                {
                    serializer.SerializeValue(ref compressionBuffer);
                    RestoreFromBinaryRepresentation(Compression.DecompressBytes(compressionBuffer));
                }
            }
        }

        public void RestoreFromBinaryRepresentation(byte[] bytes)
        {
            MemoryPackSerializer.Deserialize(bytes, ref this);
        }

        #endregion Serialization
    }
}