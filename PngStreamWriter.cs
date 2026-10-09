using System;
using System.IO;
using System.IO.Compression;

namespace Overcooked2RecipeViewer
{
    // Writes PNG scanlines without allocating a full-height Texture2D. This
    // allows a recipe board taller than the GPU's maximum texture dimension.
    internal static class PngStreamWriter
    {
        private static readonly byte[] Signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly uint[] CrcTable = BuildCrcTable();

        internal static void Save(string path, int width, int height, Action<int, byte[]> fillScanline)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            bool created = false;
            try
            {
                using (FileStream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    created = true;
                    using (Encoder encoder = new Encoder(file, width, height))
                    {
                        byte[] row = new byte[checked(width * 4 + 1)];
                        for (int y = 0; y < height; y++) { fillScanline(y, row); encoder.WriteRow(row); }
                        encoder.Finish();
                    }
                }
            }
            catch { if (created && File.Exists(path)) File.Delete(path); throw; }
        }

        internal sealed class Encoder : IDisposable
        {
            private readonly BinaryWriter _writer;
            private readonly IdatStream _chunks;
            private readonly DeflateStream _deflater;
            private readonly int _rowLength, _height;
            private int _rows;
            private uint _adlerA = 1, _adlerB;
            private bool _finished, _disposed;
            internal Encoder(Stream output, int width, int height)
            {
                if (width < 1 || height < 1) throw new ArgumentOutOfRangeException("width");
                _rowLength = checked(width * 4 + 1); _height = height;
                _writer = new BinaryWriter(output);
                _writer.Write(Signature);
                byte[] header = new byte[13];
                WriteBigEndian(header, 0, (uint)width); WriteBigEndian(header, 4, (uint)height);
                header[8] = 8; header[9] = 6; // RGBA, straight alpha.
                WriteChunk(_writer, "IHDR", header);
                WriteChunk(_writer, "sRGB", new byte[] { 0 });
                _chunks = new IdatStream(_writer);
                _chunks.Write(new byte[] { 0x78, 0x9C }, 0, 2);
                _deflater = new DeflateStream(_chunks, CompressionMode.Compress, true);
            }
            internal void WriteRow(byte[] row)
            {
                if (_disposed || _finished || _rows >= _height || row.Length != _rowLength)
                    throw new InvalidOperationException("Invalid PNG scanline.");
                row[0] = 0;
                _deflater.Write(row, 0, row.Length);
                for (int i = 0; i < row.Length; i++)
                {
                    _adlerA = (_adlerA + row[i]) % 65521;
                    _adlerB = (_adlerB + _adlerA) % 65521;
                }
                _rows++;
            }
            internal void Finish()
            {
                if (_disposed || _finished || _rows != _height) throw new InvalidOperationException("Incomplete PNG.");
                _deflater.Dispose();
                byte[] checksum = new byte[4];
                WriteBigEndian(checksum, 0, (_adlerB << 16) | _adlerA);
                _chunks.Write(checksum, 0, 4); _chunks.Flush();
                WriteChunk(_writer, "IEND", new byte[0]); _writer.Flush();
                _finished = true;
            }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (!_finished) _deflater.Dispose();
                // The caller owns the output stream.
            }
        }

        // Bounded IDAT chunks; no full-image or compressed-image MemoryStream.
        private sealed class IdatStream : Stream
        {
            private readonly BinaryWriter _writer;
            private readonly byte[] _buffer = new byte[65536];
            private int _used;
            internal IdatStream(BinaryWriter writer) { _writer = writer; }
            public override void Write(byte[] buffer, int offset, int count)
            {
                while (count > 0)
                {
                    int n = Math.Min(count, _buffer.Length - _used);
                    Buffer.BlockCopy(buffer, offset, _buffer, _used, n);
                    _used += n; offset += n; count -= n;
                    if (_used == _buffer.Length) Flush();
                }
            }
            public override void Flush()
            {
                if (_used == 0) return;
                byte[] data = new byte[_used];
                Buffer.BlockCopy(_buffer, 0, data, 0, _used);
                WriteChunk(_writer, "IDAT", data); _used = 0;
            }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override int Read(byte[] b, int o, int c) { throw new NotSupportedException(); }
            public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); }
            public override void SetLength(long v) { throw new NotSupportedException(); }
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
