using System;
using System.IO;
using System.IO.Compression;

namespace Overcooked2RecipePreview
{
    // Writes PNG scanlines without allocating a full-height Texture2D. This
    // allows a recipe board taller than the GPU's maximum texture dimension.
    internal static class PngStreamWriter
    {
        private static readonly byte[] Signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly uint[] CrcTable = BuildCrcTable();

        internal static void Save(string path, int width, int height, Action<int, byte[]> fillScanline)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException("width");
            byte[] row = new byte[checked(width * 3 + 1)];
            uint adlerA = 1, adlerB = 0;
            MemoryStream compressed = new MemoryStream();
            using (DeflateStream deflater = new DeflateStream(compressed, CompressionMode.Compress, true))
            {
                for (int y = 0; y < height; y++)
                {
                    row[0] = 0; // PNG filter type: None.
                    fillScanline(y, row);
                    deflater.Write(row, 0, row.Length);
                    for (int i = 0; i < row.Length; i++)
                    {
                        adlerA = (adlerA + row[i]) % 65521;
                        adlerB = (adlerB + adlerA) % 65521;
                    }
                }
            }

            byte[] rawDeflate = compressed.ToArray();
            byte[] zlib = new byte[checked(rawDeflate.Length + 6)];
            zlib[0] = 0x78;
            zlib[1] = 0x9C;
            Buffer.BlockCopy(rawDeflate, 0, zlib, 2, rawDeflate.Length);
            WriteBigEndian(zlib, zlib.Length - 4, (adlerB << 16) | adlerA);

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (FileStream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (BinaryWriter writer = new BinaryWriter(file))
            {
                writer.Write(Signature);
                byte[] header = new byte[13];
                WriteBigEndian(header, 0, (uint)width);
                WriteBigEndian(header, 4, (uint)height);
                header[8] = 8; // 8-bit RGB.
                header[9] = 2;
                WriteChunk(writer, "IHDR", header);
                WriteChunk(writer, "IDAT", zlib);
                WriteChunk(writer, "IEND", new byte[0]);
            }
        }

        private static void WriteChunk(BinaryWriter writer, string name, byte[] data)
        {
            byte[] type = System.Text.Encoding.ASCII.GetBytes(name);
            WriteBigEndian(writer, (uint)data.Length);
            writer.Write(type);
            writer.Write(data);
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < type.Length; i++) crc = UpdateCrc(crc, type[i]);
            for (int i = 0; i < data.Length; i++) crc = UpdateCrc(crc, data[i]);
            WriteBigEndian(writer, crc ^ 0xFFFFFFFF);
        }

        private static uint UpdateCrc(uint crc, byte value)
        {
            return CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        private static uint[] BuildCrcTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) == 0 ? c >> 1 : 0xEDB88320 ^ (c >> 1);
                table[n] = c;
            }
            return table;
        }

        private static void WriteBigEndian(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }

        private static void WriteBigEndian(BinaryWriter writer, uint value)
        {
            writer.Write((byte)(value >> 24));
            writer.Write((byte)(value >> 16));
            writer.Write((byte)(value >> 8));
            writer.Write((byte)value);
        }
    }
}
