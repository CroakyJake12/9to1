using Xunit;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace HavenOS.Images.Tests;

/// <summary>Requires the genuine external loader and enabled BWRAP sandbox.</summary>
public sealed class NativeGlycinDecodeTests
{
    [Fact]
    public void Actual_sandboxed_png_loader_preserves_transparency_and_premultiplies_pixels()
    {
        var decoded = new PictureGlycinDecoder().DecodeFirstFrame(TransparentTwoPixelPng());
        Assert.Equal(2u, decoded.Width);
        Assert.Equal(1u, decoded.Height);
        Assert.Equal(new byte[] { 0, 0, 128, 128, 255, 0, 0, 255 }, decoded.BgraPremultipliedPixels.Take(8).ToArray());
        Assert.Equal(0, decoded.DelayMicroseconds);
        Assert.Equal(1, decoded.ColorMode);
    }

    [Fact]
    public void Actual_sandboxed_image_loader_decodes_bmp_pixels_and_reports_corrupt_payload()
    {
        var decoder = new PictureGlycinDecoder();
        var bytes = new byte[62];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2); BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14); BitConverter.GetBytes(2).CopyTo(bytes, 18); BitConverter.GetBytes(1).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26); BitConverter.GetBytes((short)24).CopyTo(bytes, 28); BitConverter.GetBytes(8).CopyTo(bytes, 34);
        bytes[56] = 255; bytes[57] = 255;
        var frame = decoder.DecodeFirstFrame(bytes);
        Assert.Equal(2u, frame.Width);
        Assert.Equal(1u, frame.Height);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, frame.BgraPremultipliedPixels.Take(8).ToArray());
        Assert.Equal(0, frame.DelayMicroseconds);
        Assert.InRange(frame.ColorMode, 1, 3);
        // Retaining a valid format signature reaches the real loader's error
        // path rather than only failing client format identification.
        Assert.Throws<IOException>(() => decoder.DecodeFirstFrame(bytes.AsSpan(0, 20)));
        var repeated = decoder.DecodeFirstFrame(bytes);
        Assert.Equal(frame.BgraPremultipliedPixels, repeated.BgraPremultipliedPixels);
    }

    private static byte[] TransparentTwoPixelPng()
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 2);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8; header[9] = 6; // RGBA, 8 bits per channel
        WriteChunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write([0, 255, 0, 0, 128, 0, 0, 255, 255]);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] content)
    {
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(integer, checked((uint)content.Length));
        output.Write(integer);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(content);
        uint crc = uint.MaxValue;
        foreach (var value in typeBytes.Concat(content))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(integer, ~crc);
        output.Write(integer);
    }
}
