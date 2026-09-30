using System.Security.Cryptography;
using Haven.Core.Media;
using Haven.Infrastructure.Media;

namespace HavenOS.Apps.Motion;

internal static class MotionNativeRenderWorkflowTest
{
    public static async Task<int> RunAsync()
    {
        var fixture = Environment.GetEnvironmentVariable("ASTRA_MOTION_FIXTURE");
        var executable = Environment.GetEnvironmentVariable("ASTRA_GES_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(fixture) || string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("Native render validation requires actual configured GES and media fixture.");
        var directory = Directory.CreateTempSubdirectory("astra-motion-native-");
        try
        {
            var path = Path.Combine(directory.FullName, "edit.motion.json");
            var output = Path.Combine(directory.FullName, "export.webm");
            var store = new MotionProjectStore();
            var project = store.Create("d860a191-1e7d-425b-b30c-1d639851ac6b", 160, 120, 25, 1);
            var asset = project.AssetReferences[0] with { SourceRevisionID = "fixture-revision-1" };
            project = project with { AssetReferences = [asset] };
            var sequence = project.Sequences[0];
            var track = sequence.VideoTracks[0];
            store.Save(path, project, -1);
            project = store.Insert(project, 0, sequence.SequenceId, track.TrackId, asset.AssetId, 0, 12, 37);
            store.Save(path, project, 0);
            var elementID = project.Sequences[0].VideoTracks[0].Elements[0].ElementId;
            project = store.Split(project, 1, sequence.SequenceId, elementID, 12);
            store.Save(path, project, 1);
            var elementIDs = project.Sequences[0].VideoTracks[0].Elements.Select(element => element.ElementId).ToArray();
            project = new MotionProjectStore().Load(path);
            Check(project.ProjectId != Guid.Empty && project.Revision == 2, "Project revision lost.");
            Check(project.Sequences[0].VideoTracks[0].Elements.Select(element => element.ElementId).SequenceEqual(elementIDs), "Element identity lost.");
            var resolver = new FixtureResolver(fixture, asset);
            var renderer = new MotionRenderService(resolver, new GesTimelineRenderer(executable));
            var before = await HashAsync(fixture);
            var rendered = await renderer.RenderAsync(project, 2, sequence.SequenceId, Guid.NewGuid(), output);
            Check(rendered.IsSuccess, rendered.Error?.Message ?? "Native render failed.");
            Check(rendered.Value!.ProjectID == project.ProjectId && rendered.Value.ProjectRevision == 2 && rendered.Value.SequenceID == sequence.SequenceId, "Render switched canonical project identity/revision.");
            Check(rendered.Value.Sources.Single().AssetID.Value == asset.AssetId && File.Exists(output), "Canonical source identity/output lost.");
            Check(resolver.Resolved == 1 && resolver.Released == 1, "Source lease was duplicated or leaked.");
            Check(await HashAsync(fixture) == before, "Source media was modified.");
            Check(new MotionProjectStore().Load(path).Revision == 2, "Rendering mutated canonical editing state.");
            var stale = await renderer.RenderAsync(project, 1, sequence.SequenceId, Guid.NewGuid(), Path.Combine(directory.FullName, "stale.webm"));
            Check(stale.Error?.Code == MediaEngineErrorCode.RevisionConflict && resolver.Resolved == 1, "Stale revision was resolved/executed.");
            resolver.WrongHostedItem = true;
            var substitutedPath = Path.Combine(directory.FullName, "substituted.webm");
            var substituted = await renderer.RenderAsync(project, 2, sequence.SequenceId, Guid.NewGuid(), substitutedPath);
            Check(substituted.Error?.Code == MediaEngineErrorCode.RevisionConflict && resolver.Released == 2 && !File.Exists(substitutedPath),
                "Different canonical FileID was substituted or lease leaked.");
            Console.WriteLine("Motion actual GES canonical create/insert/split/save/reopen/render and Files-lease/revision checks passed.");
            return 0;
        }
        finally { directory.Delete(recursive: true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
    // Test-owned Files port. Production is the authenticated Files/Home adapter, never this fixture resolver.
    private sealed class FixtureResolver(string fixture, MotionAssetReference expected) : IMediaAssetSourceResolver
    {
        public bool WrongHostedItem { get; set; }
        public int Resolved { get; private set; }
        public int Released { get; private set; }
        public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID,
            string? expectedRevision, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Check(fileID == expected.FileId && assetID.Value == expected.AssetId && expectedRevision == expected.SourceRevisionID, "Resolver received a copied path or wrong identity.");
            Resolved++;
            var source = new MediaAssetSource(assetID, WrongHostedItem ? Guid.NewGuid() : Guid.Parse(expected.FileId), new Uri(Path.GetFullPath(fixture)), expectedRevision);
            return Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(new(source, () => { Released++; return ValueTask.CompletedTask; })));
        }
    }
}
