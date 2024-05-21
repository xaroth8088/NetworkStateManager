using NUnit.Framework;
using System.Text;
using System;

namespace NSM.Tests
{
    [TestFixture]
    public class CompressionTests
    {
        [Test]
        public void CompressBytes_ValidInput_ReturnsCompressedBytes()
        {
            // Arrange
            string originalString = "This is a test string to compress";
            byte[] originalBytes = Encoding.UTF8.GetBytes(originalString);

            // Act
            byte[] compressedBytes = Compression.CompressBytes(originalBytes);

            // Assert
            Assert.IsNotNull(compressedBytes);
            Assert.IsNotEmpty(compressedBytes);
            Assert.Less(compressedBytes.Length, originalBytes.Length);
        }

        [Test]
        public void DecompressBytes_ValidCompressedInput_ReturnsOriginalBytes()
        {
            // Arrange
            string originalString = "This is a test string to compress";
            byte[] originalBytes = Encoding.UTF8.GetBytes(originalString);
            byte[] compressedBytes = Compression.CompressBytes(originalBytes);

            // Act
            byte[] decompressedBytes = Compression.DecompressBytes(compressedBytes);

            // Assert
            Assert.IsNotNull(decompressedBytes);
            Assert.IsNotEmpty(decompressedBytes);
            Assert.AreEqual(originalBytes.Length, decompressedBytes.Length);
            Assert.AreEqual(originalBytes, decompressedBytes);
        }

        [Test]
        public void CompressAndDecompress_ValidInput_ReturnsOriginalInput()
        {
            // Arrange
            string originalString = "This is a test string to compress and decompress";
            byte[] originalBytes = Encoding.UTF8.GetBytes(originalString);

            // Act
            byte[] compressedBytes = Compression.CompressBytes(originalBytes);
            byte[] decompressedBytes = Compression.DecompressBytes(compressedBytes);

            // Assert
            Assert.AreEqual(originalBytes, decompressedBytes);
        }

        [Test]
        public void DecompressBytes_InvalidCompressedInput_ThrowsInvalidDataException()
        {
            // Arrange
            // Amusingly, { 0x00, 0x01, 0x02, 0x03 } turns out to be a valid byte sequence
            byte[] invalidCompressedBytes = { 0xff, 0x01, 0x02, 0x03 };

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => Compression.DecompressBytes(invalidCompressedBytes));
        }
    }
}
