using System.Buffers.Binary;
using System.IO.Compression;

namespace XylemUI.Text.Tests;

// Writes small PNG files for the tests of the PNG decoder: chunks with their check values, and image data compressed with zlib.
// Each test builds the one malformed piece it needs, so a file is a few hundred bytes at most.
internal static class TestPng
{
    public static byte[] Signature { get; } = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // IHDR: the size, the bit depth, the color type, and the interlace method (compression and filter method 0).
    public static byte[] Header(uint width, uint height, byte depth, byte colorType, byte interlace = 0)
    {
        byte[] body = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(body, width);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(4), height);
        body[8] = depth;
        body[9] = colorType;
        body[12] = interlace;
        return Chunk("IHDR", body);
    }

    public static byte[] Chunk(string type, ReadOnlySpan<byte> body, bool wrongCheck = false)
    {
        byte[] chunk = new byte[12 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)body.Length);
        for (int i = 0; i < 4; i++)
        {
            chunk[4 + i] = (byte)type[i];
        }

        body.CopyTo(chunk.AsSpan(8));
        uint check = Crc32(chunk.AsSpan(4, 4 + body.Length)) ^ (wrongCheck ? 1u : 0u);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + body.Length), check);
        return chunk;
    }

    // The rows as they are stored: each starts with its filter type.
    public static byte[] Compressed(ReadOnlySpan<byte> rows)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(rows);
        }

        return output.ToArray();
    }

    public static byte[] Data(ReadOnlySpan<byte> rows) => Chunk("IDAT", Compressed(rows));

    // The rows followed, in the same zlib stream, by that many zero bytes, written 64 KB at a time: the zeros are never in one array.
    // A 64 MB array, made on one thread while the steady-state tests measured on others, made TextSteadyStateTests count 1.5 to
    // 6 KB in TextShaper.Shape on its own thread, with no collection (the cause is not known; Issue #92).
    public static byte[] CompressedWithZeros(ReadOnlySpan<byte> rows, int zeros)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(rows);
            byte[] block = new byte[64 * 1024];
            for (int written = 0; written < zeros; written += block.Length)
            {
                zlib.Write(block, 0, Math.Min(block.Length, zeros - written));
            }
        }

        return output.ToArray();
    }

    public static byte[] End() => Chunk("IEND", []);

    public static byte[] File(params byte[][] chunks)
    {
        using var output = new MemoryStream();
        output.Write(Signature);
        foreach (byte[] chunk in chunks)
        {
            output.Write(chunk);
        }

        return output.ToArray();
    }

    // A 2 x 1 RGBA image, 8 bits: opaque red, then blue at alpha 128.
    public static byte[] Rows() => [0, 255, 0, 0, 255, 0, 0, 255, 128];

    public static byte[] Valid() => File(Header(2, 1, 8, 6), Data(Rows()), End());

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFF;
    }
}
