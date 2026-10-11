using System.Text;
using Haven.Infrastructure.Media;

namespace Haven.Infrastructure.Tests;

public sealed class PcmWaveformBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Truncated_chunk_or_odd_padding_is_rejected(bool oddChunk)
    {
        var bytes = CreateWave(oddChunk, includePadding: false);
        var path = Path.Combine(Path.GetTempPath(), "wave-boundary-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.Throws<InvalidDataException>(() => PcmWaveformReader.Decode(path));
            Assert.Throws<InvalidDataException>(() => PcmWaveformReader.AnalyzeRange(path, 0, 1));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Properly_padded_unknown_chunk_preserves_pcm()
    {
        var path = Path.Combine(Path.GetTempPath(), "wave-boundary-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(path, CreateWave(oddChunk: true, includePadding: true));
            var result = PcmWaveformReader.Decode(path);
            Assert.Equal(8000, result.SampleRate);
            Assert.Equal(2, result.DataSize);
            Assert.Equal(1d / 8000, result.DurationSeconds);
        }
        finally { File.Delete(path); }
    }

    private static byte[] CreateWave(bool oddChunk, bool includePadding)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(0u);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16u);
        writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(8000u); writer.Write(16000u);
        writer.Write((ushort)2); writer.Write((ushort)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(2u); writer.Write((short)8192);
        if (oddChunk)
        {
            writer.Write(Encoding.ASCII.GetBytes("JUNK")); writer.Write(1u); writer.Write((byte)1);
            if (includePadding) writer.Write((byte)0);
        }
        else writer.Write(new byte[] { 1, 2, 3 });
        writer.Flush(); stream.Position = 4; writer.Write(checked((uint)stream.Length - 8));
        return stream.ToArray();
    }
}
