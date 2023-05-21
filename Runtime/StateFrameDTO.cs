using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;

namespace NSM
{
    public struct StateFrameDTO : INetworkSerializable
    {
        public int gameTick;
        private byte[] _gameStateBytes;
        private PhysicsStateDTO _physicsState;
        private Dictionary<byte, IPlayerInput> _playerInputs;

        public IGameState GameState
        {
            get
            {
                if( _gameStateBytes == null)
                {
                    return null;
                }

                IGameState gameState = TypeStore.Instance.CreateBlankGameState();
                gameState.RestoreFromBinaryRepresentation(_gameStateBytes);
                return gameState;
            }
            set => _gameStateBytes = (byte[])value.GetBinaryRepresentation().Clone();
        }

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

        public StateFrameDTO Duplicate()
        {
            // TODO: change all mutable collections inside this struct (and its children) to instead use immutable versions,
            //       so that we don't need to do this (and can avoid other sneaky bugs down the line)
            // TODO: ICloneable for this object?

            if(_gameStateBytes == null)
            {
                throw new Exception("_gameStateBytes was null, so can't duplicate state");
            }

            StateFrameDTO newFrame = new()
            {
                gameTick = gameTick,
                _gameStateBytes = (byte[])_gameStateBytes.Clone(),
                PhysicsState = PhysicsState,
                PlayerInputs = new Dictionary<byte, IPlayerInput>(PlayerInputs)
            };

            return newFrame;
        }

        #region Deltas
        public StateFrameDeltaDTO GenerateDelta(StateFrameDTO targetState)
        {
            // TODO: this whole thing feels a lot like we should rethink how we're syncing frames, with more happening over in NSM and less here

            StateFrameDeltaDTO deltaState = new()
            {
                gameTick = targetState.gameTick,
                PlayerInputs = new(),
            };

            deltaState.PhysicsState = deltaState.PhysicsState.GenerateDelta(targetState.PhysicsState);

            foreach (KeyValuePair<byte, IPlayerInput> entry in targetState.PlayerInputs)
            {
                Type playerInputType = TypeStore.Instance.PlayerInputType;
                IPlayerInput defaultInput = (IPlayerInput)Activator.CreateInstance(playerInputType);

                if (_playerInputs != null && _playerInputs.GetValueOrDefault(entry.Key, defaultInput).Equals(entry.Value))
                {
                    continue;
                }

                deltaState.PlayerInputs[entry.Key] = entry.Value;
            }

            // Make a gamestate diff
            byte[] baseStateArray = GameState.GetBinaryRepresentation();
            byte[] targetStateArray = targetState.GameState.GetBinaryRepresentation();
            byte[] diffArray;

            using MemoryStream patchMs = new();
            BsDiff.BinaryPatchUtility.Create(baseStateArray, targetStateArray, patchMs);
            diffArray = patchMs.ToArray();

            deltaState._gameStateDiffBytes = diffArray;

            return deltaState;
        }

        public void ApplyDelta(StateFrameDeltaDTO deltaState)
        {
            gameTick = deltaState.gameTick;

            PhysicsState.ApplyDelta(deltaState.PhysicsState);

            foreach (KeyValuePair<byte, IPlayerInput> entry in deltaState.PlayerInputs)
            {
                _playerInputs[entry.Key] = entry.Value;
            }

            // Game state rehydration
            byte[] baseStateArray = GameState.GetBinaryRepresentation();

            using MemoryStream baseMs = new(baseStateArray);
            using MemoryStream patchedMs = new();
            BsDiff.BinaryPatchUtility.Apply(baseMs, () => new MemoryStream(deltaState._gameStateDiffBytes), patchedMs);
            _gameStateBytes = patchedMs.ToArray();
        }

        #endregion Deltas

        #region Serialization
        void INetworkSerializable.NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            _playerInputs ??= new Dictionary<byte, IPlayerInput>();
            _physicsState ??= new PhysicsStateDTO();
            _gameStateBytes ??= new byte[0];

            serializer.SerializeValue(ref gameTick);
            serializer.SerializeValue(ref _physicsState);

            byte[] compressionBuffer = new byte[0];
            if( serializer.IsReader )
            {
                serializer.SerializeValue(ref compressionBuffer);
                _gameStateBytes = Compression.DecompressBytes(compressionBuffer);
            }

            if (serializer.IsWriter)
            {
                compressionBuffer = Compression.CompressBytes(_gameStateBytes);
                serializer.SerializeValue(ref compressionBuffer);
            }

            SerializePlayerInputs(ref _playerInputs, serializer);
        }

        private void SerializePlayerInputs<T>(ref Dictionary<byte, IPlayerInput> playerInputs, BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            // This is copied in StateFrameDeltaDTO::SerializePlayerInputs
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
        #endregion Serialization
    }
}