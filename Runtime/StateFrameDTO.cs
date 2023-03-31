using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Unity.Netcode;
using Random = UnityEngine.Random;
using System.Diagnostics;

namespace NSM
{
    public struct SerializableRandomState : INetworkSerializeByMemcpy
    {
        public Random.State State;
    }

    public struct StateFrameDTO : INetworkSerializable
    {
        public IGameState gameState;
        public int gameTick;
        public SerializableRandomState randomState;
        private PhysicsStateDTO _physicsState;
        private Dictionary<byte, IPlayerInput> _playerInputs;
        public byte[] _gameStateDiffBytes;

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

        public void ApplyDelta(StateFrameDTO deltaState)
        {
            gameTick = deltaState.gameTick;
            randomState = deltaState.randomState;

            PhysicsState.ApplyDelta(deltaState.PhysicsState);

            foreach (KeyValuePair<byte, IPlayerInput> entry in deltaState.PlayerInputs)
            {
                _playerInputs[entry.Key] = entry.Value;
            }

            // Game state rehydration
            if (deltaState._gameStateDiffBytes.Length == 0)
            {
                UnityEngine.Debug.Log("No diff bytes, so skipping.");
                return;
            }

            gameState ??= CreateBlankGameState();

            byte[] baseStateArray = gameState.GetBinaryRepresentation();
            byte[] compressedDiffArray = deltaState._gameStateDiffBytes;
            byte[] decompressedDiffArray = DecompressBytes(compressedDiffArray);

            using MemoryStream baseMs = new(baseStateArray);
            using MemoryStream patchedMs = new();
            BsDiff.BinaryPatchUtility.Apply(baseMs, () => new MemoryStream(decompressedDiffArray), patchedMs);
            byte[] targetStateArray = patchedMs.ToArray();

            gameState.RestoreFromBinaryRepresentation(targetStateArray);

            // We don't need the diff bytes anymore, so ditch 'em
            _gameStateDiffBytes = new byte[0];
        }

        public StateFrameDTO Duplicate()
        {
            // TODO: change all mutable collections inside this struct (and its children) to instead use immutable versions,
            //       so that we don't need to do this (and can avoid other sneaky bugs down the line)
            StateFrameDTO newFrame = new()
            {
                gameTick = gameTick,
                gameState = CreateBlankGameState(),
                PhysicsState = PhysicsState,
                PlayerInputs = new Dictionary<byte, IPlayerInput>(PlayerInputs)
            };
            newFrame.gameState.RestoreFromBinaryRepresentation(gameState.GetBinaryRepresentation());    // "Clone" gameState, without ICloneable

            return newFrame;
        }

        public StateFrameDTO GenerateDelta(StateFrameDTO targetState)
        {

            // TODO: this whole thing feels a lot like we should rethink how we're syncing frames, with more happening over in NSM and less here

            StateFrameDTO deltaState = new()
            {
                gameTick = targetState.gameTick,
                _playerInputs = new(),
                randomState = targetState.randomState
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

                deltaState._playerInputs[entry.Key] = entry.Value;
            }

            // Make a gamestate diff
            gameState ??= CreateBlankGameState();

            byte[] baseStateArray = gameState.GetBinaryRepresentation();
            byte[] targetStateArray = targetState.gameState.GetBinaryRepresentation();
            byte[] diffArray;

            using MemoryStream patchMs = new();
            BsDiff.BinaryPatchUtility.Create(baseStateArray, targetStateArray, patchMs);
            diffArray = patchMs.ToArray();

            // Send compressed bytes instead of uncompressed (and decompress when received)
            byte[] compressedBytes = CompressBytes(diffArray);
            deltaState._gameStateDiffBytes = compressedBytes;

            return deltaState;
        }

        private static byte[] CompressBytes(byte[] input)
        {
            MemoryStream outputStream = new();
            using (BrotliStream compressionStream = new(outputStream, CompressionLevel.Optimal))
            {
                compressionStream.Write(input, 0, input.Length);
            }
            return outputStream.ToArray();
        }

        public static byte[] DecompressBytes(byte[] compressedInput)
        {
            MemoryStream inputStream = new(compressedInput);
            MemoryStream outputStream = new();
            using (BrotliStream compressionStream = new(inputStream, CompressionMode.Decompress))
            {
                compressionStream.CopyTo(outputStream);
            }
            return outputStream.ToArray();
        }

        private IGameState CreateBlankGameState()
        {
            Type gameStateType = TypeStore.Instance.GameStateType;
            return (IGameState)Activator.CreateInstance(gameStateType);
        }

        void INetworkSerializable.NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            // We might not have some of these fields either because this frame simply doesn't have a part of it,
            // or because the serializer is reading in data and there's no default created yet.
            // Either way, create default versions of the objects for use in the serialization process.
            _playerInputs ??= new Dictionary<byte, IPlayerInput>();

            _physicsState ??= new PhysicsStateDTO();

            gameState ??= CreateBlankGameState();

            _gameStateDiffBytes ??= new byte[0];

            serializer.SerializeValue(ref randomState);
            serializer.SerializeValue(ref gameTick);
            serializer.SerializeValue(ref _physicsState);
            serializer.SerializeValue(ref _gameStateDiffBytes);
            SerializePlayerInputs<T>(ref _playerInputs, serializer);
        }

        private void SerializePlayerInputs<T>(ref Dictionary<byte, IPlayerInput> playerInputs, BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
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