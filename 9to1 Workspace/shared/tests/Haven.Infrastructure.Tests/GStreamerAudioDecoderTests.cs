using System.Security.Cryptography;
using Haven.Core.Media;
using Haven.Infrastructure.Media;

namespace Haven.Infrastructure.Tests;

public sealed class GStreamerAudioDecoderTests
{
    [AudioDecodeRuntimeFact]
    public async Task Actual_float_source_decodes_with_stable_pins_precision_and_private_lease_cleanup()
    {
        var directory = Directory.CreateTempSubdirectory("audio-decode-source-").FullName;
        try
        {
            var path = Path.Combine(directory, "audio ! quoted \" source.wav");
            File.Copy(Fixture(), path);
            var original = Hash(path);
            var releases = 0;
            await using var source = new MediaAssetReadLease(new(MediaAssetId.New(), Guid.NewGuid(), new Uri(path), "source-revision"),
                () => { releases++; return ValueTask.CompletedTask; });
            var decoder = new GStreamerAudioDecoder(Runtime(), discovererPath: Discoverer());
            var result = await decoder.DecodeAsync(source, CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var lease = result.Value!;
            Assert.Equal(source.Source.AssetId, lease.Source.AssetID);
            Assert.Equal(source.Source.HostedItemId, lease.Source.FileID);
            Assert.Equal("source-revision", lease.Source.SourceRevision);
            Assert.Equal(original, lease.Source.SourceSha256);
            Assert.Equal(original, Hash(path));
            Assert.Equal("GStreamer", lease.Source.Runtime.Engine);
            Assert.Equal(Hash(Runtime()), lease.Source.Runtime.ExecutableSha256);
            Assert.NotEmpty(lease.Source.Runtime.ObservedVersion);
            VerifyFloatWave(lease.Source.TemporaryWavePath);
            Assert.Equal(0, releases);
            var repeated = await decoder.DecodeAsync(source, CancellationToken.None);
            Assert.True(repeated.IsSuccess, repeated.Error?.Message);
            await using (var repeatedLease = repeated.Value!)
                Assert.Equal(lease.Source.DecodedSha256, repeatedLease.Source.DecodedSha256);
            var temporary = lease.Source.TemporaryWavePath;
            await lease.DisposeAsync();
            await lease.DisposeAsync();
            Assert.False(File.Exists(temporary));
            Assert.True(File.Exists(path));
            Assert.Equal(0, releases);
        }
        finally { Directory.Delete(directory, true); }
    }

    [AudioDecodeRuntimeFact]
    public async Task Actual_compressed_FLAC_decodes_to_float64_without_changing_canonical_source()
    {
        var path = Environment.GetEnvironmentVariable("ASTRA_AUDIO_FLAC_FIXTURE")!;
        var original = Hash(path);
        await using var source = Source(path);
        var result = await new GStreamerAudioDecoder(Runtime(), discovererPath: Discoverer()).DecodeAsync(source, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        await using var lease = result.Value!;
        VerifyFloatWave(lease.Source.TemporaryWavePath);
        Assert.Equal(original, lease.Source.SourceSha256);
        Assert.Equal(original, Hash(path));
        Assert.Equal(source.Source.HostedItemId, lease.Source.FileID);
    }

    [AudioDecodeRuntimeFact]
    public async Task Actual_native_byte_limit_and_execution_deadline_fail_without_modifying_source()
    {
        var path = Fixture();
        var original = Hash(path);
        await using var source = Source(path);
        var bounded = await new GStreamerAudioDecoder(Runtime(), new(MaximumDecodedBytes: 1024), Discoverer())
            .DecodeAsync(source, CancellationToken.None);
        Assert.Equal(MediaEngineErrorCode.ExportFailed, bounded.Error?.Code);
        var cancelled = await new GStreamerAudioDecoder(Runtime(), new(MaximumExecutionTime: TimeSpan.FromMilliseconds(1)), Discoverer())
            .DecodeAsync(source, CancellationToken.None);
        Assert.Equal(MediaEngineErrorCode.OperationCancelled, cancelled.Error?.Code);
        Assert.Equal(original, Hash(path));
    }

    [Fact]
    public async Task Missing_backend_and_nonhydrated_remote_source_fail_closed()
    {
        await using var remote = new MediaAssetReadLease(new(MediaAssetId.New(), Guid.NewGuid(), new Uri("https://example.invalid/audio.flac"), "revision"),
            () => ValueTask.CompletedTask);
        var decoder = new GStreamerAudioDecoder(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Equal(MediaEngineErrorCode.UnsupportedSource, (await decoder.DecodeAsync(remote, CancellationToken.None)).Error?.Code);
        await using var local = Source(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Equal(MediaEngineErrorCode.BackendUnavailable, (await decoder.DecodeAsync(local, CancellationToken.None)).Error?.Code);
    }

    private static MediaAssetReadLease Source(string path) => new(new(MediaAssetId.New(), Guid.NewGuid(), new Uri(Path.GetFullPath(path)), "revision"), () => ValueTask.CompletedTask);
    [AudioDecodeRuntimeFact]
    public async Task Multiple_audio_streams_are_rejected_before_decoding()
    {
        await using var source = Source(Environment.GetEnvironmentVariable("ASTRA_AUDIO_MULTI_FIXTURE")!);
        var result = await new GStreamerAudioDecoder(Runtime(), discovererPath: Discoverer()).DecodeAsync(source);
        Assert.Equal(MediaEngineErrorCode.UnsupportedSource, result.Error?.Code);
    }

    private static string Discoverer() => Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_DISCOVERER")!;
    private static string Runtime() => Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_LAUNCH")!;
    private static string Fixture() => Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_FIXTURE")!;
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void VerifyFloatWave(string path)
    {
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        file.Position = 12;
        long frames = -1;
        ushort align = 0;
        while (file.Position + 8 <= file.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadUInt32();
            var end = file.Position + size;
            if (id == "fmt ")
            {
                var tag = reader.ReadUInt16();
                Assert.True(tag is 3 or 0xfffe);
                Assert.Equal(1, reader.ReadUInt16());
                Assert.Equal(44100u, reader.ReadUInt32());
                _ = reader.ReadUInt32();
                align = reader.ReadUInt16();
                Assert.Equal(8, align);
                Assert.Equal(64, reader.ReadUInt16());
            }
            if (id == "data") frames = size / align;
            file.Position = end + (size & 1);
        }
        Assert.Equal(10240, frames);
    }
}

public sealed class AudioDecodeRuntimeFactAttribute : FactAttribute
{
    public AudioDecodeRuntimeFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_LAUNCH"))
            || !File.Exists(Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_FIXTURE"))
            || !File.Exists(Environment.GetEnvironmentVariable("ASTRA_AUDIO_FLAC_FIXTURE"))
            || !File.Exists(Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_DISCOVERER"))
            || !File.Exists(Environment.GetEnvironmentVariable("ASTRA_AUDIO_MULTI_FIXTURE")))
            Skip = "Configure actual GStreamer launch and WAV/FLAC fixtures for native decoding acceptance.";
    }
}
