using System.Collections;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using UnityEngine;

namespace NSM
{
    public class Compression
    {
        public static byte[] CompressBytes(byte[] input)
        {
            MemoryStream outputStream = new();
            using (BrotliStream compressionStream = new(outputStream, System.IO.Compression.CompressionLevel.Optimal))
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

    }
}
