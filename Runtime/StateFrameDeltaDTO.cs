using InvertedTomato.Crc;
using System;
using System.IO;
using Unity.Netcode;
using UnityEngine;

namespace NSM
{
    public class StateFrameDeltaDTO : INetworkSerializable
    {
        private byte[] _stateDiffBytes;
        private byte stateCRC;

        public StateFrameDeltaDTO()
        {
            _stateDiffBytes = new byte[0];
        }

        public StateFrameDeltaDTO(StateFrameDTO baseState, StateFrameDTO targetState)
        {
            // Make a game state diff
            byte[] baseStateArray = baseState.GetBinaryRepresentation();
            byte[] targetStateArray = targetState.GetBinaryRepresentation();

            // CRC for the target
            CrcAlgorithm crc = CrcAlgorithm.CreateCrc8();
            crc.Append(targetStateArray);
            stateCRC = crc.ToByteArray()[0];

            // Create and store the patch
            MemoryStream patchMs = new();
            BsDiff.BinaryPatchUtility.Create(baseStateArray, targetStateArray, patchMs);
            _stateDiffBytes = (byte[])patchMs.ToArray().Clone();
        }

        public StateFrameDTO ApplyTo(StateFrameDTO baseState)
        {
            StateFrameDTO targetDTO = (StateFrameDTO)baseState.Clone();
            
            // Game state rehydration
            MemoryStream baseMs = new(baseState.GetBinaryRepresentation());
            MemoryStream patchedMs = new();
            BsDiff.BinaryPatchUtility.Apply(baseMs, () => new MemoryStream(_stateDiffBytes), patchedMs);
            byte[] targetBytes = patchedMs.ToArray();

            // CRC check
            var crc = CrcAlgorithm.CreateCrc8();
            crc.Append(targetBytes);
            if (stateCRC != crc.ToByteArray()[0])
            {
                Debug.LogError("Incoming game state delta failed CRC check when applied");
                throw new Exception("Incoming game state delta failed CRC check when applied");
            }

            targetDTO.RestoreFromBinaryRepresentation(targetBytes);

            return targetDTO;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            // TODO: some sort of metrics around how much data is sent, original data size, original size of target
            serializer.SerializeValue(ref stateCRC);

            byte[] compressionBuffer = new byte[0];
            if (serializer.IsWriter)
            {
                compressionBuffer = Compression.CompressBytes(_stateDiffBytes);
                serializer.SerializeValue(ref compressionBuffer);
            }

            if (serializer.IsReader)
            {
                serializer.SerializeValue(ref compressionBuffer);
                _stateDiffBytes = Compression.DecompressBytes(compressionBuffer);
            }
        }
    }
}
