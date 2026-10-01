using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core.Media;
using Haven.Infrastructure.Media;
using HavenOS.Apps.Motion;
using HavenOS.Apps.Wave;

namespace Haven.Infrastructure.Tests;

public sealed class MediaAssetPreparationTests
{
    [Fact]
    public void Shared_reference_round_trip_and_app_adapters_preserve_original_identity_without_a_path()
    {
        var assetID = Guid.NewGuid(); var fileID = Guid.NewGuid(); var revision = "retained/version:opaque-7";
        var sha = new string('a', 64);
        var wave = new WaveClip(Guid.NewGuid(), assetID, "", sha, 0, 10, 0,
            SourceFileID: fileID.ToString("N"), SourceRevisionID: revision,
            AudioDerivation: new(new string('b', 64), "secondary", new("decoder", "1", new string('c', 64))));
        var fromWave = WaveMediaAssetReferences.RetainedSource(wave);
        var fromMotion = MotionMediaAssetReferences.RetainedSource(new(assetID, fileID.ToString("D"), revision));
        Assert.Equal(assetID, fromWave.AssetID.Value); Assert.Equal(fileID, fromWave.FileID);
        Assert.Equal(revision, fromWave.RevisionID); Assert.Equal(sha, fromWave.ExpectedSHA256);
        Assert.Equal(fromMotion, fromWave with { ExpectedSHA256 = null });
        var json = JsonSerializer.Serialize(fromWave);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(assetID, document.RootElement.GetProperty("AssetID").GetGuid());
        Assert.False(document.RootElement.TryGetProperty("SourceUri", out _));
        Assert.Equal(fromWave, JsonSerializer.Deserialize<MediaAssetReference>(json));
        Assert.Throws<InvalidOperationException>(() => WaveMediaAssetReferences.RetainedSource(wave with { SourceRevisionID = null }));
        Assert.Throws<InvalidOperationException>(() => MotionMediaAssetReferences.RetainedSource(new(assetID, "legacy-not-files", revision)));
    }

    [Fact]
    public async Task Retained_owner_is_used_and_revocation_releases_original_lease()
    {
        using var fixture = new Fixture();
        var prepared = await fixture.Service.PrepareAsync(fixture.Reference);
        Assert.True(prepared.IsSuccess);
        var lease = prepared.Value!;
        Assert.Equal(fixture.Reference.AssetID, lease.Source.AssetId);
        Assert.Equal(fixture.Reference.ExpectedSHA256, lease.ObservedSHA256);
        Assert.Equal(1, fixture.Resolver.Calls); Assert.Equal(0, fixture.Resolver.Released);
        Assert.True(await lease.RevalidateAsync());
        Assert.Equal(2, fixture.Resolver.Calls); Assert.Equal(1, fixture.Resolver.Released);
        fixture.Resolver.Allowed = false;
        Assert.False(await lease.RevalidateAsync());
        Assert.Equal(2, fixture.Resolver.Released);
        Assert.Throws<ObjectDisposedException>(() => lease.Source);
        await lease.DisposeAsync(); Assert.Equal(2, fixture.Resolver.Released);
    }

    [Theory]
    [InlineData("identity", MediaEngineErrorCode.RevisionConflict)]
    [InlineData("revision", MediaEngineErrorCode.RevisionConflict)]
    [InlineData("hash", MediaEngineErrorCode.RevisionConflict)]
    [InlineData("size", MediaEngineErrorCode.RevisionConflict)]
    [InlineData("actor", MediaEngineErrorCode.PermissionDenied)]
    [InlineData("budget", MediaEngineErrorCode.UnsupportedSource)]
    public async Task Preparation_rejects_changed_identity_integrity_actor_and_resource_budget(string change, MediaEngineErrorCode expected)
    {
        using var fixture = new Fixture();
        var reference = fixture.Reference;
        if (change == "identity") fixture.Resolver.WrongIdentity = true;
        if (change == "revision") fixture.Resolver.WrongRevision = true;
        if (change == "hash") reference = reference with { ExpectedSHA256 = new string('0', 64) };
        if (change == "size") reference = reference with { ExpectedSizeBytes = 0 };
        if (change == "actor") fixture.Resolver.AfterResolve = () => fixture.Actors.Current = fixture.Actors.Current with { AuthenticationRevision = "changed" };
        var service = change == "budget" ? new MediaAssetPreparationService(fixture.Resolver, fixture.Actors, 1) : fixture.Service;
        var result = await service.PrepareAsync(reference);
        Assert.False(result.IsSuccess); Assert.Equal(expected, result.Error!.Code);
        Assert.Equal(1, fixture.Resolver.Released);
    }

    [Fact]
    public async Task Revalidation_detects_changed_bytes_and_actor_without_retaining_a_grant()
    {
        using var fixture = new Fixture();
        var prepared = (await fixture.Service.PrepareAsync(fixture.Reference with { ExpectedSHA256 = null, ExpectedSizeBytes = null })).Value!;
        File.WriteAllText(fixture.Path, "changed");
        Assert.False(await prepared.RevalidateAsync());
        Assert.Equal(2, fixture.Resolver.Released);
        Assert.Throws<ObjectDisposedException>(() => prepared.Source);
        var again = (await fixture.Service.PrepareAsync(fixture.Reference with { ExpectedSHA256 = null, ExpectedSizeBytes = null })).Value!;
        fixture.Actors.Current = fixture.Actors.Current with { ActorId = "other" };
        var calls = fixture.Resolver.Calls;
        Assert.False(await again.RevalidateAsync()); Assert.Equal(calls, fixture.Resolver.Calls);
        Assert.Throws<ObjectDisposedException>(() => again.Source);
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetTempFileName();
        public Actor Actors { get; } = new();
        public Resolver Resolver { get; }
        public MediaAssetPreparationService Service { get; }
        public MediaAssetReference Reference { get; }
        public Fixture()
        {
            var bytes = Encoding.UTF8.GetBytes("retained media fixture"); File.WriteAllBytes(Path, bytes);
            Reference = new(MediaAssetId.New(), Guid.NewGuid(), "revision/opaque:1",
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
            Resolver = new(Path, Reference); Service = new(Resolver, Actors, 1024);
        }
        public void Dispose() => File.Delete(Path);
    }
    private sealed class Actor : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = new("actor", "profile", null, null, "1");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Resolver(string path, MediaAssetReference reference) : IMediaRetainedAssetSourceResolver
    {
        public bool Allowed { get; set; } = true;
        public bool WrongIdentity { get; set; }
        public bool WrongRevision { get; set; }
        public Action? AfterResolve { get; set; }
        public int Calls { get; private set; }
        public int Released { get; private set; }
        public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID, string? expectedRevision,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Current-only resolution must never be used.");
        public Task<MediaEngineResult<MediaAssetReadLease>> ResolveRetainedAsync(string fileID, MediaAssetId assetID, string expectedRevision,
            CancellationToken cancellationToken = default)
        {
            Calls++; cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(reference.FileID, Guid.Parse(fileID)); Assert.Equal(reference.AssetID, assetID); Assert.Equal(reference.RevisionID, expectedRevision);
            if (!Allowed) return Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Failure(
                new(MediaEngineErrorCode.PermissionDenied, "Revoked", "read", fileID, true, false)));
            var source = new MediaAssetSource(assetID, WrongIdentity ? Guid.NewGuid() : reference.FileID, new Uri(path),
                WrongRevision ? "other-revision" : expectedRevision);
            AfterResolve?.Invoke();
            return Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(new(source, () => { Released++; return ValueTask.CompletedTask; })));
        }
    }
}
