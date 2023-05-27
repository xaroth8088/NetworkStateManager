using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
            get => _physicsState ??= new PhysicsStateDTO();
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
                PlayerInputs = new(),
            };

            deltaState.PhysicsState = PhysicsState.GenerateDelta(targetState.PhysicsState);

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
            byte[] baseStateArray = _gameStateBytes;
            byte[] targetStateArray = targetState._gameStateBytes;

            using MemoryStream patchMs = new();
            BsDiff.BinaryPatchUtility.Create(baseStateArray, targetStateArray, patchMs);

            deltaState._gameStateDiffBytes = (byte[])patchMs.ToArray().Clone();

            using (SHA256 mySHA = SHA256.Create())
            {
                PrintByteArray(mySHA.ComputeHash(_gameStateBytes));
            }
            using (SHA256 mySHA = SHA256.Create())
            {
                PrintByteArray(mySHA.ComputeHash(targetState._gameStateBytes));
            }
            using (SHA256 mySHA = SHA256.Create())
            {
                PrintByteArray(mySHA.ComputeHash(deltaState._gameStateDiffBytes));
            }

            return deltaState;
        }

        public static void PrintByteArray(byte[] array)
        {
            string output = "";
            for (int i = 0; i < array.Length; i++)
            {
                output += $"{array[i]:X2}";
                if ((i % 4) == 3) output += " ";
            }
            Debug.Log(output);
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
            using (SHA256 mySHA = SHA256.Create())
            {
                PrintByteArray(mySHA.ComputeHash(_gameStateBytes));
            }

            using MemoryStream baseMs = new(_gameStateBytes);
            using MemoryStream patchedMs = new();
            BsDiff.BinaryPatchUtility.Apply(baseMs, () => new MemoryStream(deltaState._gameStateDiffBytes), patchedMs);
            _gameStateBytes = (byte[])patchedMs.ToArray().Clone();

            using (SHA256 mySHA = SHA256.Create())
            {
                PrintByteArray(mySHA.ComputeHash(_gameStateBytes));
            }
            using (SHA256 mySHA = SHA256.Create())
            {
                PrintByteArray(mySHA.ComputeHash(deltaState._gameStateDiffBytes));
            }
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