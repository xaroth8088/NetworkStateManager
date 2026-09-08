using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using System;

namespace NSM
{
    internal struct PlayerInputsDTO : INetworkSerializable
    {
        internal const int MaxInputBytes = 1024;
        internal const int MaxPacketBytes = 65536;
        private Dictionary<byte, IPlayerInput> _playerInputs;

        public Dictionary<byte, IPlayerInput> PlayerInputs
        {
            get => _playerInputs ??= new();
            set => _playerInputs = value;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            _playerInputs ??= new();

            int totalBytes = 2;
            if (serializer.IsWriter)
            {
                if (_playerInputs.Count > 256) throw new System.InvalidOperationException("Too many player inputs.");
                ushort count = (ushort)_playerInputs.Count;
                serializer.SerializeValue(ref count);

                foreach (KeyValuePair<byte, IPlayerInput> keyValuePair in _playerInputs)
                {
                    byte playerId = keyValuePair.Key;
                    serializer.SerializeValue(ref playerId);

                    PlayerInputDTO playerInput = new()
                    {
                        input = keyValuePair.Value
                    };
                    using var writer = new FastBufferWriter(MaxInputBytes, Allocator.Temp);
                    writer.WriteNetworkSerializable(playerInput);
                    ushort size = checked((ushort)writer.Length);
                    totalBytes += 3 + size;
                    if (totalBytes > MaxPacketBytes) throw new InvalidOperationException("Input packet exceeds byte budget.");
                    serializer.SerializeValue(ref size);
                    serializer.GetFastBufferWriter().WriteBytesSafe(writer.ToArray());
                }
            }

            if (serializer.IsReader)
            {
                _playerInputs.Clear();
                ushort count = 0;
                serializer.SerializeValue(ref count);
                if (count > 256) throw new System.InvalidOperationException("Too many player inputs.");

                for (int i = 0; i < count; i++)
                {
                    byte playerId = 0;
                    serializer.SerializeValue(ref playerId);

                    if (_playerInputs.ContainsKey(playerId)) throw new InvalidOperationException("Duplicate player ID in input packet.");
                    ushort size = 0;
                    serializer.SerializeValue(ref size);
                    totalBytes += 3 + size;
                    if (size > MaxInputBytes || totalBytes > MaxPacketBytes)
                        throw new InvalidOperationException("Input packet exceeds byte budget.");
                    byte[] bytes = new byte[size];
                    serializer.GetFastBufferReader().ReadBytesSafe(ref bytes, size);
                    using var reader = new FastBufferReader(bytes, Allocator.Temp);
                    reader.ReadNetworkSerializable(out PlayerInputDTO playerInputDTO);
                    if (reader.Position != reader.Length) throw new InvalidOperationException("Input payload contains trailing bytes.");

                    if (!_playerInputs.TryAdd(playerId, playerInputDTO.input))
                        throw new System.InvalidOperationException("Duplicate player ID in input packet.");
                }
            }
        }
    }
}
