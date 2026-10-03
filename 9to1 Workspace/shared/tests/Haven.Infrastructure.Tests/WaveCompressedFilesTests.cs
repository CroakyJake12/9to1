using System.Security.Cryptography;
using Haven.Core.Media;
using Haven.Infrastructure.Media;
using HavenOS.Apps.Wave;

namespace Haven.Infrastructure.Tests;

public sealed class WaveCompressedFilesTests
{
    [Fact]
    public void Schema_four_projects_migrate_without_inventing_derivations_and_future_layout_is_preserved_and_rejected()
    {
        var root = Directory.CreateTempSubdirectory("wave-schema-migration-").FullName;
        try
        {
            var path = Path.Combine(root, "project.9to1w");
            var project = WaveProjectStore.Create("Existing development project");
            WaveProjectStore.Save(path, project);
            var legacy = File.ReadAllText(path).Replace("\"SchemaVersion\": 5", "\"SchemaVersion\": 4", StringComparison.Ordinal);
            File.WriteAllText(path, legacy);
            var migrated = WaveProjectStore.Open(path);
            Assert.Equal(5, migrated.SchemaVersion);
            Assert.Equal(project.ProjectId, migrated.ProjectId);
            Assert.Equal(legacy, File.ReadAllText(path));
            var future = legacy.Replace("\"SchemaVersion\": 4", "\"SchemaVersion\": 6", StringComparison.Ordinal);
            File.WriteAllText(path, future);
            Assert.Throws<InvalidDataException>(() => WaveProjectStore.Open(path));
            Assert.Equal(future, File.ReadAllText(path));
        }
        finally { Directory.Delete(root, true); }
    }
    [AudioDecodeRuntimeFact]
    public async Task Compressed_Files_import_split_restart_rename_redecode_exports_exact_PCM_and_retains_original_identity()
    {
        var root = Directory.CreateTempSubdirectory("wave-compressed-files-").FullName;
        try
        {
            var original = Path.Combine(root, "source.flac");
            File.Copy(Environment.GetEnvironmentVariable("ASTRA_AUDIO_FLAC_FIXTURE")!, original);
            var originalHash = Hash(original);
            var files = new FilesResolver(original);
            var decoder = new TrackingDecoder(Decoder());
            var service = new WaveFilesProjectService(files, decoder);
            var project = WaveProjectStore.Create("Compressed Files", 44100, 1);
            var imported = await service.ImportAsync(project, 0, project.Tracks[0].TrackId, files.FileID.ToString("D"), "revision-1", 0);
            Assert.True(imported.IsSuccess, imported.Error?.Message);
            project = imported.Value!;
            var clip = Assert.Single(project.Tracks[0].Clips);
            Assert.Equal(originalHash, clip.SourceSha256);
            Assert.Equal(files.FileID.ToString("D"), clip.SourceFileID);
            Assert.Equal("revision-1", clip.SourceRevisionID);
            Assert.Empty(clip.SourcePath);
            Assert.NotNull(clip.AudioDerivation);
            Assert.Equal(10240, clip.FrameCount);
            project = WaveProjectEdits.Split(project, project.Revision, clip.ClipId, clip.FrameCount / 2);
            var projectPath = Path.Combine(root, "project.9to1w");
            WaveProjectStore.Save(projectPath, project);
            var renamed = Path.Combine(root, "renamed source.flac");
            File.Move(original, renamed);
            files.Path = renamed;
            project = WaveProjectStore.Open(projectPath);
            Assert.All(project.Tracks[0].Clips, item => Assert.Equal(clip.AudioDerivation, item.AudioDerivation));
            // New service and decoder represent process restart; no decoded file is retained in the project.
            var restartedDecoder = new TrackingDecoder(Decoder());
            service = new WaveFilesProjectService(files, restartedDecoder);
            var output = Path.Combine(root, "output.wav");
            var exported = await service.ExportPcm16Async(project, project.Revision, output);
            Assert.True(exported.IsSuccess, exported.Error?.Message);
            Assert.Equal(10240, exported.Value);
            Assert.Equal(Hash(Environment.GetEnvironmentVariable("ASTRA_PCM16_FIXTURE")!), Hash(output));
            Assert.Equal(originalHash, Hash(renamed));
            Assert.Equal(2, files.Resolutions);
            Assert.Equal(2, files.Releases);
            Assert.Equal(1, decoder.Decodes);
            Assert.Equal(1, decoder.Releases);
            Assert.Equal(1, restartedDecoder.Decodes); // Split clips share one canonical asset/one derivation lease.
            Assert.Equal(1, restartedDecoder.Releases);
            Assert.All(decoder.Paths.Concat(restartedDecoder.Paths), path => Assert.False(File.Exists(path)));
            Assert.DoesNotContain("decoded.wav", File.ReadAllText(projectPath), StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(root, ".wave-export-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    [AudioDecodeRuntimeFact]
    public async Task Changed_decode_evidence_or_canonical_source_rejects_export_without_publishing_output()
    {
        var root = Directory.CreateTempSubdirectory("wave-compressed-conflict-").FullName;
        try
        {
            var original = Path.Combine(root, "source.flac");
            File.Copy(Environment.GetEnvironmentVariable("ASTRA_AUDIO_FLAC_FIXTURE")!, original);
            var files = new FilesResolver(original);
            var service = new WaveFilesProjectService(files, Decoder());
            var project = WaveProjectStore.Create("Pinned", 44100, 1);
            var imported = await service.ImportAsync(project, 0, project.Tracks[0].TrackId, files.FileID.ToString("D"), "revision-1", 0);
            Assert.True(imported.IsSuccess, imported.Error?.Message);
            project = imported.Value!;
            var clip = project.Tracks[0].Clips[0];
            var changed = project with { Tracks = [project.Tracks[0] with { Clips = [clip with
                { AudioDerivation = clip.AudioDerivation! with { DecodeProfile = "different-profile" } }] }] };
            var output = Path.Combine(root, "must-not-publish.wav");
            Assert.Equal(MediaEngineErrorCode.RevisionConflict, (await service.ExportPcm16Async(changed, 1, output)).Error?.Code);
            Assert.False(File.Exists(output));
            var bytes = await File.ReadAllBytesAsync(original);
            // FLAC metadata byte changes while retaining a decodable stream; byte identity is still revision-pinned.
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(original, bytes);
            var rejected = await service.ExportPcm16Async(project, 1, output);
            Assert.Equal(MediaEngineErrorCode.RevisionConflict, rejected.Error?.Code);
            Assert.False(File.Exists(output));
            Assert.Equal(files.Resolutions, files.Releases);
            Assert.Empty(Directory.GetFiles(root, ".wave-export-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static GStreamerAudioDecoder Decoder() => new(Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_LAUNCH")!,
        discovererPath: Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_DISCOVERER")!);
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private sealed class TrackingDecoder(IMediaAudioDecoder inner) : IMediaAudioDecoder
    {
        public int Decodes { get; private set; }
        public int Releases { get; private set; }
        public List<string> Paths { get; } = [];
        public async Task<MediaEngineResult<MediaAudioDecodedLease>> DecodeAsync(MediaAssetReadLease source, CancellationToken cancellationToken = default)
        {
            Decodes++;
            var result = await inner.DecodeAsync(source, cancellationToken);
            if (!result.IsSuccess) return result;
            var original = result.Value!;
            Paths.Add(original.Source.TemporaryWavePath);
            return MediaEngineResult<MediaAudioDecodedLease>.Success(new(original.Source, async () =>
            {
                await original.DisposeAsync();
                Releases++;
            }));
        }
    }
    private sealed class FilesResolver(string path) : IMediaAssetSourceResolver
    {
        public Guid FileID { get; } = Guid.NewGuid();
        public string Path { get; set; } = path;
        public int Resolutions { get; private set; }
        public int Releases { get; private set; }
        public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string opaqueFileID, MediaAssetId assetID,
            string? expectedRevision, CancellationToken cancellationToken = default)
        {
            Assert.Equal(FileID.ToString("D"), opaqueFileID);
            Assert.Equal("revision-1", expectedRevision);
            Resolutions++;
            return Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(new(new(assetID, FileID, new Uri(Path), "revision-1"),
                () => { Releases++; return ValueTask.CompletedTask; })));
        }
    }
}
