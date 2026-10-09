using System.Security.Cryptography;
using System.Text;
using Haven.Infrastructure.Media;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class PcmWaveformReaderTests
{
    [Fact]
    public void Selected_source_range_preserves_independent_channel_envelopes_and_original_bytes()
    {
        var path = Write(2, [32767, 0, 0, -32768, 16384, 8192, -32768, 32767]);
        var original = SHA256.HashData(File.ReadAllBytes(path));
        var range = PcmWaveformReader.AnalyzeRange(path, 1, 2, 2);
        Assert.Equal(1, range.SourceStartFrame); Assert.Equal(2, range.FrameCount);
        Assert.Equal(48000, range.SampleRate); Assert.Equal(2, range.Channels.Count);
        Assert.Equal(new float[] { 0, .5f }, range.Channels[0].Maximum);
        Assert.Equal(new float[] { -1, .25f }, range.Channels[1].Minimum);
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(path)));
    }
    [Fact]
    public void Wide_bucket_examines_short_transient_at_edge_and_keeps_sign()
    {
        var samples = new short[65536]; samples[3] = 32767; samples[^4] = -32768;
        var range = PcmWaveformReader.AnalyzeRange(Write(1, samples), 0, samples.Length, 1);
        Assert.Equal(32767 / 32768f, Assert.Single(range.Channels).Maximum[0]);
        Assert.Equal(-1f, range.Channels[0].Minimum[0]); Assert.Equal(1, range.BucketCount);
    }
    [Fact]
    public void Empty_buckets_are_not_invented_when_range_is_shorter_than_display()
    {
        var range = PcmWaveformReader.AnalyzeRange(Write(1, [8192, -8192]), 0, 2, 512);
        Assert.Equal(2, range.BucketCount); Assert.Equal(new[] { .25f, -.25f }, range.Channels[0].Minimum);
    }
    [Fact]
    public void Bounds_cancellation_and_excess_channels_refuse_before_reading_unrelated_ranges()
    {
        var path = Write(1, [0, 1, 2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => PcmWaveformReader.AnalyzeRange(path, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcmWaveformReader.AnalyzeRange(path, 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcmWaveformReader.AnalyzeRange(path, 0, 3, 4097));
        Assert.Throws<NotSupportedException>(() => PcmWaveformReader.AnalyzeRange(Write(33, new short[33]), 0, 1));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PcmWaveformReader.AnalyzeRange(path, 0, 3, 1, cancellation.Token));
    }
    private static string Write(ushort channels, short[] samples)
    {
        var directory = Directory.CreateTempSubdirectory("shared-waveform-fixture-");
        var path = Path.Combine(directory.FullName, "original.wav");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)1); writer.Write(channels);
        writer.Write(48000); writer.Write(48000 * channels * 2); writer.Write((ushort)(channels * 2)); writer.Write((ushort)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
        foreach (var sample in samples) writer.Write(sample); return path;
    }
}
