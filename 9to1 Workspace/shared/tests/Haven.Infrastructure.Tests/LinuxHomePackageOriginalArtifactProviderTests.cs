using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure.Native.Linux;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Real canonical FileHomeCoreStateStore/database and OS profile. Catalogue
/// shape fixtures deliberately contain zero signature bytes and no certificate/key.
/// They test detached observations/refusals, never successful publisher enrollment,
/// installed peer authentication, privileged package activation or release acceptance.</summary>
public sealed class LinuxHomePackageOriginalArtifactProviderTests
{
    [Fact]
    public async Task Real_file_catalogue_returns_exact_detached_bytes_and_never_turns_them_into_a_native_session()
    {
        await using var rig = await Rig.CreateAsync();
        var registry = await rig.RetainCatalogueAsync();
        var before = File.ReadAllBytes(rig.FilePath);
        var artifact = await rig.Provider.ResolveAsync(rig.Request(), registry, rig.Token);
        Assert.NotNull(artifact);
        Assert.Equal(rig.HomeSigned, artifact.SignedDescriptorBytes.ToArray());
        Assert.Equal(rig.HomePayload, artifact.DescriptorPayloadBytes.ToArray());
        Assert.Equal("fixture.catalogue", artifact.CatalogueRevision);
        var parsed = HomePackageOriginalArtifactDescriptorParser.Parse(artifact.SignedDescriptorBytes,
            artifact.DescriptorPayloadBytes, artifact.CatalogueRevision, rig.Request());
        Assert.Equal("linux", parsed.Descriptor.Platform);
        Assert.Equal("arm64", parsed.Descriptor.Abi);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Provider.DemandOriginalCurrentAsync(artifact,
            rig.Actor, DetachedClaim(), "copied-session-is-not-authority", rig.Token));
        Assert.Equal(before, File.ReadAllBytes(rig.FilePath));
        Assert.False(Directory.Exists(Path.Combine(rig.Root, "active")));
    }

    [Fact]
    public async Task Stale_or_substituted_registry_is_refused_against_the_current_actual_file_database()
    {
        await using var rig = await Rig.CreateAsync();
        var previous = await rig.RetainCatalogueAsync();
        var write = await rig.Database.SaveAsync(previous, previous.Revision, rig.Token);
        Assert.True(write.Succeeded);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Provider.ResolveAsync(rig.Request(), previous, rig.Token).AsTask());
        var fabricated = write.Snapshot! with { Revision = 999 };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Provider.ResolveAsync(rig.Request(), fabricated, rig.Token).AsTask());
        Assert.Equal(write.Snapshot!.Revision, (await rig.Database.ReadAsync(rig.Token)).Snapshot!.Revision);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("duplicate")]
    [InlineData("issuer")]
    [InlineData("abi")]
    [InlineData("missing-root")]
    [InlineData("schema")]
    public async Task Malformed_or_inconsistent_retained_catalogue_is_refused_without_rewriting_its_original_file(string damage)
    {
        await using var rig = await Rig.CreateAsync();
        var registry = await rig.RetainCatalogueAsync(damage);
        var before = File.ReadAllBytes(rig.FilePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Provider.ResolveAsync(rig.Request(), registry, rig.Token).AsTask());
        Assert.Equal(before, File.ReadAllBytes(rig.FilePath));
    }

    [Fact]
    public async Task Missing_enrollment_or_unavailable_exact_selection_returns_no_artifact()
    {
        await using var rig = await Rig.CreateAsync();
        var empty = (await rig.Database.ReadAsync(rig.Token)).Snapshot!;
        Assert.Null(await rig.Provider.ResolveAsync(rig.Request(), empty, rig.Token));
        var registry = await rig.RetainCatalogueAsync();
        Assert.Null(await rig.Provider.ResolveAsync(rig.Request() with { PackageId = "absent.pkg" }, registry, rig.Token));
        Assert.Null(await rig.Provider.ResolveAsync(rig.Request() with { RequestedVersion = "2" }, registry, rig.Token));
        Assert.Null(await rig.Provider.ResolveAsync(rig.Request() with { RequestedChannel = "other" }, registry, rig.Token));
        Assert.Null(await rig.Provider.ResolveAsync(rig.Request() with { ExpectedRevision = "older.catalogue" }, registry, rig.Token));
    }

    [Fact]
    public async Task Copied_evidence_and_another_provider_cannot_recreate_the_original_artifact_observation()
    {
        await using var rig = await Rig.CreateAsync();
        var registry = await rig.RetainCatalogueAsync();
        var artifact = (await rig.Provider.ResolveAsync(rig.Request(), registry, rig.Token))!;
        var copy = artifact with { };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Provider.DemandOriginalCurrentAsync(copy,
            rig.Actor, DetachedClaim(), "copied-session", rig.Token));
        var other = rig.CreateProvider();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.DemandOriginalCurrentAsync(artifact,
            rig.Actor, DetachedClaim(), "copied-session", rig.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Sessions.DemandOriginalPackageArtifactSessionCurrentAsync(
            rig.Actor, DetachedClaim(), "copied-session", rig.Token));
    }

    [Fact]
    public async Task Action_maps_are_explicit_detached_and_do_not_adopt_other_ABIs_or_unregistered_actions()
    {
        await using var rig = await Rig.CreateAsync();
        var scopes = new[] { new ResourceScope("fixture.package", "home.pkg", "fixture.review", ResourceAccess.Write) };
        var source = new HomePackageOriginalActionMap("fixture.owner", "fixture.install", "home.core", scopes);
        var provider = rig.CreateProvider([new("home.pkg", HomePackageAction.Install, source)]);
        scopes[0] = new("foreign", "foreign", "foreign", ResourceAccess.Execute);
        var map = provider.ResolveAction(HomePackageAction.Install, rig.HomeDescriptor);
        Assert.NotNull(map);
        Assert.Equal("fixture.package", Assert.Single(map.Scopes).Kind);
        Assert.Null(provider.ResolveAction(HomePackageAction.Repair, rig.HomeDescriptor));
        Assert.Null(provider.ResolveAction(HomePackageAction.Install, rig.HomeDescriptor with { Abi = "x64" }));
        Assert.Null(provider.ResolveAction(HomePackageAction.Install, rig.HomeDescriptor with { Platform = "windows" }));
        Assert.Throws<ArgumentException>(() => rig.CreateProvider([
            new("home.pkg", HomePackageAction.Install, source), new("home.pkg", HomePackageAction.Install, source)]));
    }

    [Fact]
    public async Task Foreign_store_binding_and_cancelled_resolution_do_not_open_or_modify_package_state()
    {
        await using var rig = await Rig.CreateAsync();
        var foreignFile = Path.Combine(rig.Root, "foreign.json");
        var foreign = new FileHomeCoreStateStore(foreignFile);
        Assert.Throws<UnauthorizedAccessException>(() => new LinuxHomePackageOriginalArtifactProvider(rig.Database,
            foreign, rig.Profiles, rig.Sessions, "arm64", []));
        Assert.False(File.Exists(foreignFile));
        var registry = await rig.RetainCatalogueAsync();
        var before = File.ReadAllBytes(rig.FilePath);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => rig.Provider.ResolveAsync(rig.Request(), registry, cancelled.Token).AsTask());
        Assert.Equal(cancelled.Token, error.CancellationToken);
        Assert.Equal(before, File.ReadAllBytes(rig.FilePath));
    }

    // Deliberately detached/untrusted claim: no verifier accepts or installs it.
    private static HomeNativeInstalledPeer DetachedClaim() => new("home", Guid.NewGuid(), "copied-revision",
        "copied-executable", new HashSet<string>(StringComparer.Ordinal) { "home.core" });
    private static string Digest(byte[] value) => Convert.ToHexString(SHA256.HashData(value));

    private sealed class Rig : IAsyncDisposable
    {
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(20));
        internal CancellationToken Token => _deadline.Token;
        internal string Root { get; } = Directory.CreateTempSubdirectory("linux-package-artifact-").FullName;
        internal string FilePath => Path.Combine(Root, "canonical-device.json");
        internal FileHomeCoreStateStore Store = null!;
        internal HomePackageDatabase Database = null!;
        internal HomeLocalProfileIdentity Profiles = null!;
        internal HomeNativeCoreApiSessions Sessions = null!;
        internal AuthenticatedResourceActor Actor = null!;
        internal LinuxHomePackageOriginalArtifactProvider Provider = null!;
        internal HomePackageArtifactDescriptor HomeDescriptor = null!;
        internal byte[] HomeSigned = [], HomePayload = [];
        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                rig.Store = new(rig.FilePath); rig.Database = new(rig.Store);
                rig.Profiles = new(rig.Store, new OperatingSystemPrincipalSource());
                rig.Actor = await rig.Profiles.GetCurrentAsync(rig.Token) ?? throw new InvalidOperationException("The actual OS profile is unavailable.");
                var permissions = new HomePermissionTrustService(rig.Store, (_, _) => null);
                rig.Sessions = new(permissions, rig.Profiles, new UnavailableHomeNativeInstalledPeerVerifier(),
                    () => throw new InvalidOperationException("No detached artifact observation may dispatch the Home API."));
                rig.HomeDescriptor = Descriptor("home.pkg", "home");
                (rig.HomeSigned, rig.HomePayload) = Shape(rig.HomeDescriptor);
                rig.Provider = rig.CreateProvider();
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal HomePackageActionRequest Request() => new("home.pkg", HomePackageAction.Install,
            "fixture.observation", "1", "stable", "fixture.catalogue");
        internal LinuxHomePackageOriginalArtifactProvider CreateProvider(
            IEnumerable<LinuxHomePackageOriginalArtifactProvider.Registration>? maps = null) =>
            new(Database, Store, Profiles, Sessions, "arm64", maps ?? []);
        internal async Task<HomePackageDatabaseSnapshot> RetainCatalogueAsync(string? damage = null)
        {
            var root = Descriptor("root.pkg", "root");
            if (damage == "abi") root = root with { Abi = "x64" };
            var rootShape = Shape(root);
            var packages = new List<NativeWindowsHomeInstallerBootstrapAdmission.CataloguePackage>
            {
                new(Convert.ToBase64String(HomeSigned), Convert.ToBase64String(HomePayload)),
                new(Convert.ToBase64String(rootShape.Signed), Convert.ToBase64String(rootShape.Payload))
            };
            if (damage == "duplicate") packages.Add(packages[0]);
            if (damage == "missing-root") packages.RemoveAt(1);
            var catalogue = new NativeWindowsHomeInstallerBootstrapAdmission.Catalogue(1, "fixture.catalogue",
                "fixture.untrusted", "home.pkg", "root.pkg", ["home.pkg", "root.pkg"], packages);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(catalogue, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var principal = await new OperatingSystemPrincipalSource().GetPrincipalAsync(Token)
                ?? throw new InvalidOperationException("The actual OS principal is unavailable.");
            var enrollment = new HomePackageOriginalPublisherEnrollmentRecord(damage == "schema" ? 1 : 2,
                new string('1', 64), damage == "issuer" ? "different.untrusted" : "fixture.untrusted", "fixture.catalogue",
                damage == "digest" ? new string('0', 64) : Digest(bytes), "fixture.untrusted-not-installed",
                Guid.NewGuid(), Actor.ActorId, Actor.ProfileId, principal, "home.pkg", "root.pkg", ["home.pkg", "root.pkg"],
                DateTimeOffset.UnixEpoch, Convert.ToBase64String(bytes));
            var current = (await Database.ReadAsync(Token)).Snapshot!;
            var write = await Database.SaveAsync(current with { UnknownFields = new(StringComparer.Ordinal) {
                [HomePackageOriginalPublisherEnrollmentRecord.Field] = JsonSerializer.SerializeToElement(enrollment) } }, current.Revision, Token);
            Assert.True(write.Succeeded);
            return write.Snapshot!;
        }
        private static HomePackageArtifactDescriptor Descriptor(string package, string app) => new(1, package, app,
            "1", "stable", "native-app", "linux", "arm64", [], ["home.core"], new string('a', 64), new string('b', 64), 1);
        private static (byte[] Signed, byte[] Payload) Shape(HomePackageArtifactDescriptor descriptor)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return (JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, issuerKeyId = "fixture.untrusted",
                payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(new byte[384]) }), payload);
        }
        public async ValueTask DisposeAsync()
        {
            try { if (Sessions is not null) await Sessions.DisposeAsync(); }
            finally { _deadline.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        }
    }
}
