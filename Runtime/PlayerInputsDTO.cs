using System.Collections.Generic;
using Unity.Netcode;

namespace NSM
{
    internal struct PlayerInputsDTO : INetworkSerializable
    {
        private Dictionary<byte, IPlayerInput> _playerInputs;

        public Dictionary<byte, IPlayerInput> PlayerInputs
        {
            get => _playerInputs ??= new();
            set => _playerInputs = value;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            _playerInputs ??= new();

            if (serializer.IsWriter)
            {
                byte count = (byte)_playerInputs.Count;
                serializer.SerializeValue(ref count);

                foreach (KeyValuePair<byte, IPlayerInput> keyValuePair in _playerInputs)
                {
                    byte playerId = keyValuePair.Key;
                    serializer.SerializeValue(ref playerId);

                    PlayerInputDTO playerInput = new()
                    {
                        input = keyValuePair.Value
                    };
                    serializer.SerializeValue(ref playerInput);
                }
            }

            if (serializer.IsReader)
            {
                byte count = 0;
                serializer.SerializeValue(ref count);

                for (int i = 0; i < count; i++)
                {
                    byte playerId = 0;
                    serializer.SerializeValue(ref playerId);

                    PlayerInputDTO playerInputDTO = new();
                    serializer.SerializeValue(ref playerInputDTO);

                    _playerInputs[playerId] = playerInputDTO.input;
                }
            }
        }
    }
}