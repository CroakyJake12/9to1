using System.Security.Cryptography;
using Haven.Core.Media;

namespace HavenOS.Apps.Wave;

internal static class WaveFilesProjectWorkflowTest
{
    private const string FileID = "994da36c-0969-44de-9f2d-a2c48252cc1f";
    public static async Task<int> RunAsync()
    {
        var fixture = Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_FIXTURE")
            ?? throw new InvalidOperationException("Configure the real WAV fixture before running this explicit validation.");
        var root = Directory.CreateTempSubdirectory("wave-files-workflow-").FullName;
        try
        {
            var original = Path.Combine(root, "original.wav");
            File.Copy(fixture, original);
            var preview = PcmWaveformReader.Decode(original);
            var sourceHash = Hash(original);
            var reference = WaveProjectStore.Create("Declared PCM16 reference", preview.SampleRate, preview.Channels);
            reference = WaveProjectStore.AddWavClip(reference, reference.Tracks[0].TrackId, original, 0);
            var referencePath = Path.Combine(root, "reference-pcm16.wav");
            WaveProjectExporter.ExportPcm16(reference, referencePath);
            var referenceHash = Hash(referencePath);
            var resolver = new FixtureFilesLeaseResolver(original);
            var service = new WaveFilesProjectService(resolver);
            var project = WaveProjectStore.Create("Files audio", preview.SampleRate, preview.Channels);
            var imported = await service.ImportAsync(project, 0, project.Tracks[0].TrackId, FileID, "revision-1", 0);
            Require(imported.IsSuccess, "Files import failed.");
            project = imported.Value!;
            var clip = project.Tracks[0].Clips.Single();
            Require(clip.SourcePath.Length == 0 && clip.SourceFileID == FileID && clip.SourceRevisionID == "revision-1",
                "Canonical project persisted an ephemeral source path instead of Files identity.");
            project = WaveProjectEdits.Split(project, 1, clip.ClipId, clip.FrameCount / 2);
            var path = Path.Combine(root, "project.9to1w");
            WaveProjectStore.Save(path, project);
            var renamed = Path.Combine(root, "renamed.wav");
            File.Move(original, renamed);
            resolver.Path = renamed;
            project = WaveProjectStore.Open(path);
            var output = Path.Combine(root, "render.wav");
            var exported = await service.ExportPcm16Async(project, 2, output);
            Require(exported.IsSuccess && exported.Value == clip.FrameCount, "Exact export did not retain the timeline sample count.");
            Require(Hash(output) == referenceHash && Hash(renamed) == sourceHash, "Split/reopened Files source changed or declared PCM16 output differed from the same canonical source.");
            if (preview.FormatTag == 1 && preview.BitsPerSample == 16 && preview.DataOffset == 44)
                Require(Hash(output) == sourceHash, "Canonical PCM16 source did not export bit-identically.");
            Require(resolver.Resolutions == 2 && resolver.Releases == 2, "One Files asset must be leased once per operation and released.");
            var stale = await service.ExportPcm16Async(project, 1, Path.Combine(root, "stale.wav"));
            Require(stale.Error?.Code == MediaEngineErrorCode.RevisionConflict && resolver.Resolutions == 2,
                "Stale export acquired source authority.");
            resolver.Revision = "revision-2";
            var changed = await service.ExportPcm16Async(project, 2, Path.Combine(root, "changed.wav"));
            Require(changed.Error?.Code == MediaEngineErrorCode.RevisionConflict && resolver.Releases == 3,
                "Changed source revision was exported or a lease leaked.");
            resolver.Revision = "revision-1";
            resolver.WrongHostedItem = true;
            var substituted = await service.ExportPcm16Async(project, 2, Path.Combine(root, "substituted.wav"));
            Require(substituted.Error?.Code == MediaEngineErrorCode.RevisionConflict && resolver.Releases == 4,
                "A different canonical Files object was substituted or its lease leaked.");
            Console.WriteLine("Wave Files create/import/split/save/reopen/rename/exact export and revision/lease checks passed.");
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }
    private static string Hash(string path) { using var source = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(source)); }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    // Explicit validation fixture only. Production uses the canonical authorised Files host adapter.
    private sealed class FixtureFilesLeaseResolver(string path) : IMediaAssetSourceResolver
    {
        public string Path { get; set; } = path;
        public string Revision { get; set; } = "revision-1";
        public bool WrongHostedItem { get; set; }
        public int Resolutions { get; private set; }
        public int Releases { get; private set; }
        public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID,
            string? expectedRevision, CancellationToken cancellationToken = default)
        {
            Require(fileID == FileID, "Wrong canonical FileID.");
            Resolutions++;
            return Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(new(new(assetID, WrongHostedItem ? Guid.NewGuid() : Guid.Parse(FileID), new Uri(Path), Revision),
                () => { Releases++; return ValueTask.CompletedTask; })));
        }
    }
}
