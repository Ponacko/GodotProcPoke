using System.IO.Compression;

namespace ProcPoke.MapGen;

/// <summary>
/// A minimal, dependency-free PNG encoder (24-bit RGB, single IDAT). Enough to write debug map images
/// without System.Drawing or any NuGet package. Uses <see cref="DeflateStream"/> for the pixel data,
/// wrapped in a zlib envelope with an Adler-32 checksum, and CRC-32 per chunk.
/// </summary>
internal static class Png
{
    public static void WriteRgb(string path, int width, int height, byte[] rgb)
    {
        using var fs = File.Create(path);
        fs.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, (uint)width);
        WriteBe(ihdr, 4, (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type: truecolour RGB
        WriteChunk(fs, "IHDR", ihdr);

        // IDAT: filtered scanlines (filter 0), zlib-wrapped deflate.
        var raw = new byte[height * (1 + width * 3)];
        var p = 0;
        for (var y = 0; y < height; y++)
        {
            raw[p++] = 0; // filter type: none
            Array.Copy(rgb, y * width * 3, raw, p, width * 3);
            p += width * 3;
        }
        WriteChunk(fs, "IDAT", ZlibCompress(raw));

        WriteChunk(fs, "IEND", []);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); // zlib header
        ms.WriteByte(0x01);
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        WriteBe(ms, Adler32(data));
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        WriteBe(len, 0, (uint)data.Length);
        s.Write(len);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBe(crcBytes, 0, crc);
        s.Write(crcBytes);
    }

    private static void WriteBe(Span<byte> buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteBe(Stream s, uint value)
        => s.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private static uint Adler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        foreach (var d in data)
        {
            a = (a + d) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
