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
        public PhysicsStateDTO PhysicsState;

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

        public StateFrameDTO Duplicate()
        {
            // TODO: change all mutable collections inside this struct (and its children) to instead use immutable versions,
            //       so that we don't need to do this (and can avoid other sneaky bugs down the line)
            // TODO: ICloneable for this object?

            if(_gameStateBytes == null)
            {
                throw new Exception("_gameStateBytes was null, so can't duplicate state");
            }

            PhysicsStateDTO physicsStateDTO = new();
            physicsStateDTO.RestoreFromBinaryRepresentation(PhysicsState.GetBinaryRepresentation());
            StateFrameDTO newFrame = new()
            {
                authoritative = false,
                gameTick = gameTick,
                _gameStateBytes = (byte[])_gameStateBytes.Clone(),
                PhysicsState = physicsStateDTO
            };

            return newFrame;
        }

        #region Deltas
        public StateFrameDeltaDTO GenerateDelta(StateFrameDTO targetState)
        {
            // TODO: this whole thing feels a lot like we should rethink how we're syncing frames, with more happening over in NSM and less here

            StateFrameDeltaDTO deltaState = new()
            {
                gameTick = targetState.gameTick
            };

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
            baseStateArray = PhysicsState.GetBinaryRepresentation();
            targetStateArray = targetState.PhysicsState.GetBinaryRepresentation();

            // CRC for the target
            crc.Clear();
            crc.Append(targetStateArray);
            deltaState.physicsStateCRC = crc.ToByteArray()[0];

            patchMs = new();
            BsDiff.BinaryPatchUtility.Create(baseStateArray, targetStateArray, patchMs);
            deltaState._physicsStateDiffBytes = (byte[])patchMs.ToArray().Clone();

            Debug.LogWarning("Created delta, target count: " + targetState.PhysicsState.RigidBodyStates.Count);

            return deltaState;
        }

        public void ApplyDelta(StateFrameDeltaDTO deltaState)
        {
            Debug.LogWarning("HERE 3");
            if( !authoritative)
            {
                Debug.LogError("Attempted to apply a delta to a non-authoritative state frame.  We are tick " + gameTick + " and delta is for tick " + deltaState.gameTick);

                /*** 
                 * For some reason, this Exception doesn't appear to be actually thrown?!
                 */

                throw new Exception("Attempted to apply a delta to a non-authoritative state frame");
            }

            Debug.LogWarning("HERE 4");
            gameTick = deltaState.gameTick;

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
            Debug.LogWarning("HERE 5");

            // Game state rehydration
            baseMs = new(PhysicsState.GetBinaryRepresentation());
            patchedMs = new();
            BsDiff.BinaryPatchUtility.Apply(baseMs, () => new MemoryStream(deltaState._physicsStateDiffBytes), patchedMs);
            PhysicsState.RestoreFromBinaryRepresentation((byte[])patchedMs.ToArray().Clone());

            Debug.LogWarning("HERE 6");
            // CRC check
            crc.Clear();
            crc.Append(PhysicsState.GetBinaryRepresentation());
            if (deltaState.physicsStateCRC != crc.ToByteArray()[0])
            {
                throw new Exception("Incoming physics state delta failed CRC check when applied");
            }

            Debug.LogWarning("Applied delta, new count: " + PhysicsState.RigidBodyStates.Count);
        }

        #endregion Deltas

        #region Serialization
        void INetworkSerializable.NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            _gameStateBytes ??= new byte[0];

            serializer.SerializeValue(ref gameTick);

            byte[] compressionBuffer = new byte[0];
            if( serializer.IsReader )
            {
                serializer.SerializeValue(ref compressionBuffer);
                _gameStateBytes = Compression.DecompressBytes(compressionBuffer);
                serializer.SerializeValue(ref compressionBuffer);
                PhysicsState = new PhysicsStateDTO();
                PhysicsState.RestoreFromBinaryRepresentation(Compression.DecompressBytes(compressionBuffer));
            }

            if (serializer.IsWriter)
            {
                compressionBuffer = Compression.CompressBytes(_gameStateBytes);
                serializer.SerializeValue(ref compressionBuffer);
                compressionBuffer = Compression.CompressBytes(PhysicsState.GetBinaryRepresentation());
                serializer.SerializeValue(ref compressionBuffer);
            }
        }

        #endregion Serialization
    }
}