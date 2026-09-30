using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core.Media;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureFilesSourceRendererTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_pinned_source_lease_releases_before_actual_raster_replay_and_preserves_green_crop(bool useControlledGlycin)
    {
        var directory = Path.Combine(Path.GetTempPath(), "picture-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "source.bmp");
            var bytes = TwoPixelBmp();
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            var source = new PictureSourceAssetReference(Guid.NewGuid(), Guid.NewGuid(), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, Guid.NewGuid());
            var document = PictureDocument.Create(2, 1, source.FileId.ToString(), source.RevisionId.ToString()).Crop(1, 0, 1, 1).Resize(3, 4);
            var artifact = new PictureArtifactEnvelope { BackingFileId = Guid.NewGuid(), Document = document, SourceAsset = source };
            var authority = new Authority(artifact.BackingFileId);
            var released = false;
            var resolver = new PictureFilesSourceRenderer((requested, _) =>
            {
                Assert.Equal(source, requested);
                return Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(new(new(new(source.AssetId), source.FileId, new Uri(path), source.RevisionId.ToString()), () =>
                {
                    released = true;
                    File.Delete(path);
                    return ValueTask.CompletedTask;
                })));
            }, authority.Service);
            using var pinned = useControlledGlycin
                ? await resolver.LoadWithGlycinAsync(artifact, authority.Revision, new PictureGlycinDecoder(), TestContext.Current.CancellationToken)
                : await resolver.LoadAsync(artifact, authority.Revision, TestContext.Current.CancellationToken);
            Assert.True(released);
            Assert.False(File.Exists(path));
            using var frame = pinned.Render();
            Assert.Equal(new PixelSize(3, 4), frame.PixelSize);
            var pixels = new byte[3 * 4 * 4];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { frame.CopyPixels(new(0, 0, 3, 4), handle.AddrOfPinnedObject(), pixels.Length, 12); }
            finally { handle.Free(); }
            Assert.Equal((byte)0, pixels[0]);
            Assert.Equal((byte)255, pixels[1]);
            Assert.Equal((byte)0, pixels[2]);
            var sharedFrame = pinned.RenderSharedFrame();
            Assert.Equal(3, sharedFrame.Width);
            Assert.Equal(4, sharedFrame.Height);
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, sharedFrame.CopyPixels()[..4]);
            Assert.Equal(document.DocumentId, pinned.DocumentId);
            Assert.Equal(document.Revision, pinned.Revision);
            pinned.Dispose();
            Assert.Throws<ObjectDisposedException>(() => pinned.Render());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Source_revision_or_byte_integrity_mismatch_rejects_and_disposes_lease()
    {
        var directory = Path.Combine(Path.GetTempPath(), "picture-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "source.bmp");
            var bytes = TwoPixelBmp();
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            var source = new PictureSourceAssetReference(Guid.NewGuid(), Guid.NewGuid(), new string('0', 64), bytes.Length, Guid.NewGuid());
            var artifact = new PictureArtifactEnvelope
            {
                BackingFileId = Guid.NewGuid(), SourceAsset = source,
                Document = PictureDocument.Create(2, 1, source.FileId.ToString(), source.RevisionId.ToString())
            };
            var authority = new Authority(artifact.BackingFileId);
            var releases = 0;
            var wrongRevision = true;
            var renderer = new PictureFilesSourceRenderer((_, _) => Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(
                new(new(new(source.AssetId), source.FileId, new Uri(path), (wrongRevision ? Guid.NewGuid() : source.RevisionId).ToString()), () =>
                {
                    releases++;
                    return ValueTask.CompletedTask;
                }))), authority.Service);
            await Assert.ThrowsAsync<InvalidDataException>(() => renderer.LoadAsync(artifact, authority.Revision, TestContext.Current.CancellationToken));
            wrongRevision = false;
            await Assert.ThrowsAsync<InvalidDataException>(() => renderer.LoadAsync(artifact, authority.Revision, TestContext.Current.CancellationToken));
            Assert.Equal(2, releases);
            var valid = artifact with { SourceAsset = source with { ContentHash = Convert.ToHexString(SHA256.HashData(bytes)) } };
            renderer = new PictureFilesSourceRenderer((_, _) =>
            {
                authority.Deny = true;
                return Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(new(new(new(source.AssetId), source.FileId, new Uri(path), source.RevisionId.ToString()), () =>
                {
                    releases++;
                    return ValueTask.CompletedTask;
                })));
            }, authority.Service);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => renderer.LoadAsync(valid, authority.Revision, TestContext.Current.CancellationToken));
            Assert.Equal(3, releases);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Native_source_pipeline_rejects_corrupt_identified_format_and_authority_revocation_after_decoding()
    {
        var directory = Path.Combine(Path.GetTempPath(), "picture-native-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "source.bmp");
            var bytes = TwoPixelBmp();
            var backing = Guid.NewGuid();
            var authority = new Authority(backing) { DenyAtAuthorization = 3 };
            var released = 0;
            var source = new PictureSourceAssetReference(Guid.NewGuid(), Guid.NewGuid(), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, Guid.NewGuid());
            var artifact = new PictureArtifactEnvelope { BackingFileId = backing, SourceAsset = source,
                Document = PictureDocument.Create(2, 1, source.FileId.ToString(), source.RevisionId.ToString()) };
            var renderer = new PictureFilesSourceRenderer((_, _) => Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(
                new(new(new(source.AssetId), source.FileId, new Uri(path), source.RevisionId.ToString()), () =>
                { released++; return ValueTask.CompletedTask; }))), authority.Service);
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => renderer.LoadWithGlycinAsync(artifact, authority.Revision,
                new PictureGlycinDecoder(), TestContext.Current.CancellationToken));
            Assert.Equal(3, authority.Authorizations);
            Assert.Equal(1, released);
            authority.DenyAtAuthorization = int.MaxValue;
            var corrupt = bytes.Take(20).ToArray();
            await File.WriteAllBytesAsync(path, corrupt, TestContext.Current.CancellationToken);
            var corruptArtifact = artifact with { SourceAsset = source with { SizeBytes = corrupt.Length, ContentHash = Convert.ToHexString(SHA256.HashData(corrupt)) } };
            await Assert.ThrowsAsync<IOException>(() => renderer.LoadWithGlycinAsync(corruptArtifact, authority.Revision,
                new PictureGlycinDecoder(), TestContext.Current.CancellationToken));
            Assert.Equal(2, released);
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task Canonical_animation_step_replays_edit_graph_and_rechecks_backing_and_raw_source_authority()
    {
        var root = Path.Combine(Path.GetTempPath(), "picture-animation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "source.gif");
            var bytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            var source = new PictureSourceAssetReference(Guid.NewGuid(), Guid.NewGuid(), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, Guid.NewGuid());
            var document = PictureDocument.Create(2, 1, source.FileId.ToString(), source.RevisionId.ToString()).Crop(0, 0, 1, 1).Resize(3, 4);
            var artifact = new PictureArtifactEnvelope { BackingFileId = Guid.NewGuid(), Document = document, SourceAsset = source };
            var authority = new Authority(artifact.BackingFileId);
            var requests = 0;
            var releases = 0;
            var wrongSource = false;
            var renderer = new PictureFilesSourceRenderer((requested, _) =>
            {
                Assert.Equal(source, requested);
                requests++;
                return Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(new(new(new(source.AssetId), source.FileId,
                    new Uri(path), (wrongSource ? Guid.NewGuid() : source.RevisionId).ToString()), () => { releases++; return ValueTask.CompletedTask; })));
            }, authority.Service);
            using var pinned = await renderer.LoadAnimationWithGlycinAsync(artifact, authority.Revision, new PictureGlycinDecoder(), TestContext.Current.CancellationToken);
            Assert.True(pinned.CanAdvanceFrames);
            Assert.Equal(80_000, pinned.FrameDelayMicroseconds);
            Assert.Equal(2, releases);
            using var red = pinned.Render();
            Assert.Equal(new PixelSize(3, 4), red.PixelSize);
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, FirstPixel(red));
            await pinned.AdvanceFrameAsync(TestContext.Current.CancellationToken);
            Assert.Equal(120_000, pinned.FrameDelayMicroseconds);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, pinned.RenderSharedFrame().CopyPixels()[..4]);
            Assert.Equal(4, requests);
            Assert.Equal(requests, releases);
            using var blue = pinned.Render();
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, FirstPixel(blue));
            Assert.Equal(document.Revision, pinned.Revision);
            Assert.Equal(document.DocumentId, pinned.DocumentId);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            wrongSource = true;
            await Assert.ThrowsAsync<InvalidDataException>(() => pinned.AdvanceFrameAsync(TestContext.Current.CancellationToken));
            Assert.Equal(120_000, pinned.FrameDelayMicroseconds);
            wrongSource = false;
            authority.Deny = true;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pinned.AdvanceFrameAsync(TestContext.Current.CancellationToken));
            Assert.False(pinned.CanAdvanceFrames);
            Assert.Throws<ObjectDisposedException>(() => pinned.Render());
            Assert.Equal(requests, releases);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Initial_materialization_rechecks_revoked_raw_source_before_returning_any_pinned_frame(int mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "picture-initial-authority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var path = Path.Combine(root, "source.gif");
            var bytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
            await File.WriteAllBytesAsync(path, bytes, ct);
            var source = new PictureSourceAssetReference(Guid.NewGuid(), Guid.NewGuid(), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, Guid.NewGuid());
            var artifact = new PictureArtifactEnvelope { BackingFileId = Guid.NewGuid(), SourceAsset = source,
                Document = PictureDocument.Create(2, 1, source.FileId.ToString(), source.RevisionId.ToString()) };
            var authority = new Authority(artifact.BackingFileId);
            var requests = 0; var releases = 0;
            var renderer = new PictureFilesSourceRenderer((_, _) => {
                if (++requests > 1) throw new UnauthorizedAccessException("Raw Files source revoked after initial lease materialization.");
                return Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(new(new(new(source.AssetId), source.FileId,
                    new Uri(path), source.RevisionId.ToString()), () => { releases++; return ValueTask.CompletedTask; })));
            }, authority.Service);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => mode switch {
                0 => renderer.LoadAsync(artifact, authority.Revision, ct),
                1 => renderer.LoadWithGlycinAsync(artifact, authority.Revision, new PictureGlycinDecoder(), ct),
                _ => renderer.LoadAnimationWithGlycinAsync(artifact, authority.Revision, new PictureGlycinDecoder(), ct)
            });
            Assert.Equal(2, requests); Assert.Equal(1, releases);
        }
        finally { Directory.Delete(root, true); }
    }

    private static byte[] FirstPixel(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var bytes = new byte[4];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new(0, 0, 1, 1), pinned.AddrOfPinnedObject(), bytes.Length, 4); }
        finally { pinned.Free(); }
        return bytes;
    }

    private static byte[] TwoPixelBmp()
    {
        var bytes = new byte[62];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2); BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14); BitConverter.GetBytes(2).CopyTo(bytes, 18); BitConverter.GetBytes(1).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26); BitConverter.GetBytes((short)24).CopyTo(bytes, 28); BitConverter.GetBytes(8).CopyTo(bytes, 34);
        bytes[56] = 255; bytes[58] = 255;
        return bytes;
    }

    // Injected trusted authority/lease fixture, not production OS/profile binding proof.
    private sealed class Authority : IAuthenticatedResourceActorSource, ICanonicalResourceAccessResolver
    {
        private readonly Guid _fileId;
        private readonly AuthenticatedResourceActor _actor = new("fixture-actor", Guid.NewGuid().ToString(), null, null, "fixture-auth");
        public Authority(Guid fileId) { _fileId = fileId; Service = new(this, [this]); }
        public Guid Revision { get; } = Guid.NewGuid();
        public bool Deny { get; set; }
        public int Authorizations { get; private set; }
        public int DenyAtAuthorization { get; set; } = int.MaxValue;
        public ResourceAuthorizationService Service { get; }
        public string ResourceKind => "files.item";
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(_actor);
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken)
        {
            Authorizations++;
            return ValueTask.FromResult(new ResourceAccessDecision(!Deny && Authorizations < DenyAtAuthorization && actionId == "picture.file.open" && scope.Access == ResourceAccess.Read
                && scope.Id == _fileId.ToString() && scope.Revision == Revision.ToString(), "fixture", actor.ActorId, scope.Revision, null));
        }
    }
}
