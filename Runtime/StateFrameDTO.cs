using InvertedTomato.Crc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace NSM
{
    public struct StateFrameDTO : INetworkSerializable
    {
        public bool authoritative;
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
            set {
                if( authoritative )
                {
                    Debug.LogError("Tried to write game state to an authoritative frame");
                }
                _gameStateBytes = (byte[])value.GetBinaryRepresentation().Clone();
            }
        }

        public PhysicsStateDTO PhysicsState
        {
            get => _physicsState;
            set
            {
                if (authoritative)
                {
                    Debug.LogError("Tried to write physics state to an authoritative frame");
                }
                _physicsState = value;
            }
        }

        public Dictionary<byte, IPlayerInput> PlayerInputs
        {
            get => _playerInputs ??= new Dictionary<byte, IPlayerInput>();
            set
            {
                if (authoritative)
                {
                    Debug.LogError("Tried to write inputs to an authoritative frame");
                }
                _playerInputs = value;
            }
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

            // Temporarily set authoritative to false, so that the initialization doesn't trigger
            // the "you're writing to an authoritative frame" warnings
            StateFrameDTO newFrame = new()
            {
                authoritative = false,
                gameTick = gameTick,
                _gameStateBytes = (byte[])_gameStateBytes.Clone(),
                PhysicsState = PhysicsState,
                PlayerInputs = new Dictionary<byte, IPlayerInput>(PlayerInputs)
            };

            newFrame.authoritative = authoritative;

            return newFrame;
        }

        #region Deltas
        public StateFrameDeltaDTO GenerateDelta(StateFrameDTO targetState)
        {
            // TODO: this whole thing feels a lot like we should rethink how we're syncing frames, with more happening over in NSM and less here

            StateFrameDeltaDTO deltaState = new()
            {
                gameTick = targetState.gameTick,
                PlayerInputs = new()
            };

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

            // Make a game state diff
            byte[] baseStateArray = _gameStateBytes;
            byte[] targetStateArray = targetState._gameStateBytes;

            // CRC for the target
            CrcAlgorithm crc = CrcAlgorithm.CreateCrc8();
            crc.Append(targetStateArray);
            deltaState.gameStateCRC = crc.ToByteArray()[0];

            MemoryStream patchMs = new();
            BsDiff.BinaryPatchUtility.Create(baseStateArray, targetStateArray, patchMs);
            deltaState._gameStateDiffBytes = (byte[])patchMs.ToArray().Clone();

            // Make a physics state diff
            baseStateArray = _physicsState.GetBinaryRepresentation();
            targetStateArray = targetState._physicsState.GetBinaryRepresentation();

            // CRC for the target
            crc.Clear();
            crc.Append(targetStateArray);
            deltaState.physicsStateCRC = crc.ToByteArray()[0];

            patchMs = new();
            BsDiff.BinaryPatchUtility.Create(baseStateArray, targetStateArray, patchMs);
            deltaState._physicsStateDiffBytes = (byte[])patchMs.ToArray().Clone();

            return deltaState;
        }

        public void ApplyDelta(StateFrameDeltaDTO deltaState)
        {
            if( !authoritative)
            {
                throw new Exception("Attempted to apply a delta to a non-authoritative state frame");
            }

            gameTick = deltaState.gameTick;

            foreach (KeyValuePair<byte, IPlayerInput> entry in deltaState.PlayerInputs)
            {
                _playerInputs[entry.Key] = entry.Value;
            }

            // Game state rehydration
            MemoryStream baseMs = new(_gameStateBytes);
            MemoryStream patchedMs = new();
            BsDiff.BinaryPatchUtility.Apply(baseMs, () => new MemoryStream(deltaState._gameStateDiffBytes), patchedMs);
            _gameStateBytes = (byte[])patchedMs.ToArray().Clone();

            // CRC check
            var crc = CrcAlgorithm.CreateCrc8();
            crc.Append(_gameStateBytes);
            if( deltaState.gameStateCRC != crc.ToByteArray()[0] )
            {
                throw new Exception("Incoming game state delta failed CRC check when applied");
            }

            // Game state rehydration
            baseMs = new(_physicsState.GetBinaryRepresentation());
            patchedMs = new();
            BsDiff.BinaryPatchUtility.Apply(baseMs, () => new MemoryStream(deltaState._physicsStateDiffBytes), patchedMs);
            _physicsState.RestoreFromBinaryRepresentation((byte[])patchedMs.ToArray().Clone());

            // CRC check
            crc.Clear();
            crc.Append(_physicsState.GetBinaryRepresentation());
            if (deltaState.physicsStateCRC != crc.ToByteArray()[0])
            {
                throw new Exception("Incoming physics state delta failed CRC check when applied");
            }
        }

        #endregion Deltas

        #region Serialization
        void INetworkSerializable.NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            _playerInputs ??= new Dictionary<byte, IPlayerInput>();
            _gameStateBytes ??= new byte[0];

            serializer.SerializeValue(ref gameTick);

            byte[] compressionBuffer = new byte[0];
            if( serializer.IsReader )
            {
                serializer.SerializeValue(ref compressionBuffer);
                _gameStateBytes = Compression.DecompressBytes(compressionBuffer);
                serializer.SerializeValue(ref compressionBuffer);
                _physicsState = new PhysicsStateDTO();
                _physicsState.RestoreFromBinaryRepresentation(Compression.DecompressBytes(compressionBuffer));
            }

            if (serializer.IsWriter)
            {
                compressionBuffer = Compression.CompressBytes(_gameStateBytes);
                serializer.SerializeValue(ref compressionBuffer);
                compressionBuffer = Compression.CompressBytes(_physicsState.GetBinaryRepresentation());
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