using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

namespace NSM
{
    public class StateFrameDeltaDTO : INetworkSerializable
    {
        public int gameTick;
        public byte[] _gameStateDiffBytes;
        private PhysicsStateDTO _physicsState;
        private Dictionary<byte, IPlayerInput> _playerInputs;

        public PhysicsStateDTO PhysicsState
        {
            get => _physicsState ??= new PhysicsStateDTO();
            set => _physicsState = value;
        }
        public Dictionary<byte, IPlayerInput> PlayerInputs
        {
            get => _playerInputs ??= new Dictionary<byte, IPlayerInput>();
            set => _playerInputs = value;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            _playerInputs ??= new Dictionary<byte, IPlayerInput>();
            _physicsState ??= new PhysicsStateDTO();
            _gameStateDiffBytes ??= new byte[0];

            serializer.SerializeValue(ref gameTick);
            serializer.SerializeValue(ref _physicsState);

            byte[] compressionBuffer = new byte[0];
            if (serializer.IsReader)
            {
                serializer.SerializeValue(ref compressionBuffer);
                _gameStateDiffBytes = Compression.DecompressBytes(compressionBuffer);
            }

            if (serializer.IsWriter)
            {
                compressionBuffer = Compression.CompressBytes(_gameStateDiffBytes);
                serializer.SerializeValue(ref compressionBuffer);
            }

            SerializePlayerInputs(ref _playerInputs, serializer);
        }

        private void SerializePlayerInputs<T>(ref Dictionary<byte, IPlayerInput> playerInputs, BufferSerializer<T> serializer)
    where T : IReaderWriter
        {
            // This is a copy of StateFrameDTO::SerializePlayerInputs
            if (serializer.IsReader)
            {
                // Read the length of the dictionary
                byte length = 0;
                serializer.SerializeValue(ref length);

                // Set up our interim storage for the data
                byte[] keys = new byte[length];
                IPlayerInput[] values = new IPlayerInput[length];

                // Fill the values array with concrete instances, so we can tell them to serialize later
                Type playerInputType = TypeStore.Instance.PlayerInputType;
                IPlayerInput defaultPlayerInput = (IPlayerInput)Activator.CreateInstance(playerInputType);
                Array.Fill(values, defaultPlayerInput);

                // Read the data
                for (byte i = 0; i < length; i++)
                {
                    serializer.SerializeValue(ref keys[i]);
                    values[i].NetworkSerialize(serializer);
                }

                // Construct the output dictionary and set it
                playerInputs = Enumerable.Range(0, keys.Length).ToDictionary(i => keys[i], i => values[i]);
            }
            else if (serializer.IsWriter)
            {
                // Write the length of the dictionary
                byte length = (byte)playerInputs.Count;
                serializer.SerializeValue(ref length);

                // Write the data
                foreach (KeyValuePair<byte, IPlayerInput> item in playerInputs)
                {
                    byte key = item.Key;
                    serializer.SerializeValue(ref key);
                    item.Value.NetworkSerialize(serializer);
                }
            }
        }

    }
}
