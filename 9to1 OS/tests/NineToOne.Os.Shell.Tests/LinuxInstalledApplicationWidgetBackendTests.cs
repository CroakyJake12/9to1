using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

// Actual FileHome/profile/kernel principal and canonical registry. Platform inventory and
// publisher tuple are controlled; this does not enroll or attest an installed native owner.
public sealed class LinuxInstalledApplicationWidgetBackendTests
{
    [Fact]
    public async Task OriginalAndLegacyMetadataReadDoNotResolveLaunchOrReconcileCanonicalInventory()
    {
        using var f = new Fixture(); var original = (await f.Actors.GetCurrentAsync(default))!;
        var app = Assert.Single(await f.Registry.RefreshForActorAsync(original, default)); f.Provider.Calls = 0;
        var before = await File.ReadAllBytesAsync(f.Path);
        var launcher = new LinuxApplicationLauncher(f.Registry, f.Resources, f.Actors);
        Assert.Equal(app, await launcher.ResolveForReadForActorAsync(app.ApplicationId, app.Revision, original, default));
        Assert.Equal(app, await launcher.ResolveForReadAsync(app.ApplicationId, app.Revision, default));
        Assert.Equal(0, f.Provider.Calls); Assert.Equal(before, await File.ReadAllBytesAsync(f.Path));
    }

    [Fact]
    public async Task OriginalMetadataReadRetirementAfterActualReadonlyOwnerReturnDeniesWithoutReconciliation()
    {
        using var f = new Fixture(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        var app = Assert.Single(await f.Registry.RefreshForActorAsync(actor, default)); f.Provider.Calls = 0;
        byte[]? retiredBytes = null;
        var observed = new ReadObserver(f.Registry, async () =>
        {
            var state = await f.Store.ReadAsync(); Assert.True(state.IsSuccess);
            var record = Assert.Single(state.State!.Records, r => r.RecordId == "home.local-profile");
            var profile = record.Payload.Deserialize<HomeLocalProfile>()!;
            var written = await f.Store.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(profile with { ProfileId = Guid.NewGuid() }) }, record.Revision);
            Assert.True(written.IsSuccess); retiredBytes = await File.ReadAllBytesAsync(f.Path);
        });
        var resources = new ResourceAuthorizationService(f.Actors, [new InstalledApplicationResourceResolver(observed)]);
        var launcher = new LinuxApplicationLauncher(observed, resources, f.Actors);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => launcher.ResolveForReadForActorAsync(app.ApplicationId, app.Revision, actor, default));
        Assert.Equal(2, observed.Reads); Assert.Equal(0, observed.LegacyCalls); Assert.Equal(0, f.Provider.Calls);
        Assert.NotNull(retiredBytes); Assert.Equal(retiredBytes, await File.ReadAllBytesAsync(f.Path));
    }

    [Fact]
    public async Task ExistingCanonicalCardReadsWithoutProviderObservationOrHomeWrite()
    {
        using var f = new Fixture();
        var actor = (await f.Actors.GetCurrentAsync(default))!;
        var app = Assert.Single(await f.Registry.RefreshForActorAsync(actor, default));
        f.Provider.Calls = 0; var before = await File.ReadAllBytesAsync(f.Path);
        var backend = f.Backend(app); var surface = await backend.CaptureForActorAsync(f.Request, actor, default);
        Assert.NotNull(surface); Assert.Equal(f.Owner, surface.Reference);
        Assert.Equal("Canonical <app>", surface.Data["label"].GetString());
        Assert.Equal("actual-observed-version", surface.Data["version"].GetString());
        Assert.DoesNotContain("Canonical <app>", surface.AuthoredCui);
        Assert.Empty(backend.Declaration().ActionIds);
        Assert.Equal(actor, await f.Resources.AuthorizeForActorAsync(actor, HomeNativeWidgetRegistry.RenderActionId,
            backend.Declaration().DataScopes));
        Assert.Equal(0, f.Provider.Calls); Assert.Equal(before, await File.ReadAllBytesAsync(f.Path));
        Assert.Null(await backend.CaptureAsync(f.Request, default));
    }

    [Fact]
    public async Task MissingExistingCanonicalRecordDoesNotInitializeOrDiscover()
    {
        using var f = new Fixture(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        var before = await File.ReadAllBytesAsync(f.Path);
        var backend = new LinuxInstalledApplicationWidgetBackend(f.Registry, f.Actors, f.Resources,
            f.Owner, Guid.NewGuid(), 1);
        Assert.Null(await backend.CaptureForActorAsync(f.Request, actor, default));
        Assert.Equal(0, f.Provider.Calls); Assert.Equal(before, await File.ReadAllBytesAsync(f.Path));
        Assert.DoesNotContain((await f.Store.ReadAsync()).State!.Records, r => r.RecordType == "home.installed-apps");
    }

    [Fact]
    public async Task MissingOriginalReadPortDeniesBeforeLegacyRegistryObservation()
    {
        using var f = new Fixture(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        var legacy = new LegacyRegistry(); var before = await File.ReadAllBytesAsync(f.Path);
        var resources = new ResourceAuthorizationService(f.Actors, [new InstalledApplicationResourceResolver(legacy)]);
        var backend = new LinuxInstalledApplicationWidgetBackend(legacy, f.Actors, resources, f.Owner, Guid.NewGuid(), 1);
        Assert.Null(await backend.CaptureForActorAsync(f.Request, actor, default));
        var scope = Assert.Single(backend.Declaration().DataScopes);
        Assert.Null(await resources.AuthorizeForActorAsync(actor, HomeNativeWidgetRegistry.RenderActionId, [scope]));
        Assert.Equal(0, legacy.Calls); Assert.Equal(before, await File.ReadAllBytesAsync(f.Path));
    }

    [Fact]
    public async Task ActualPersistedProfileReplacementCannotReadOriginalCard()
    {
        using var f = new Fixture(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        var app = Assert.Single(await f.Registry.RefreshForActorAsync(actor, default));
        var backend = f.Backend(app); f.Provider.Calls = 0;
        var state = await f.Store.ReadAsync(); Assert.True(state.IsSuccess);
        var record = Assert.Single(state.State!.Records, r => r.RecordId == "home.local-profile");
        var profile = record.Payload.Deserialize<HomeLocalProfile>()!;
        var write = await f.Store.WriteAsync(record with { Revision = record.Revision + 1,
            Payload = JsonSerializer.SerializeToElement(profile with { ProfileId = Guid.NewGuid() }) }, record.Revision);
        Assert.True(write.IsSuccess); var replacedBytes = await File.ReadAllBytesAsync(f.Path);
        Assert.Null(await backend.CaptureForActorAsync(f.Request, actor, default));
        Assert.Equal(0, f.Provider.Calls); Assert.Equal(replacedBytes, await File.ReadAllBytesAsync(f.Path));
    }

    [Fact]
    public async Task ProfileRetirementAfterActualCanonicalReadWithholdsCapturedData()
    {
        using var f = new Fixture(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        var app = Assert.Single(await f.Registry.RefreshForActorAsync(actor, default)); f.Provider.Calls = 0;
        byte[]? retiredBytes = null;
        var observed = new ReadObserver(f.Registry, async () =>
        {
            var state = await f.Store.ReadAsync(); Assert.True(state.IsSuccess);
            var record = Assert.Single(state.State!.Records, r => r.RecordId == "home.local-profile");
            var profile = record.Payload.Deserialize<HomeLocalProfile>()!;
            var written = await f.Store.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(profile with { ProfileId = Guid.NewGuid() }) }, record.Revision);
            Assert.True(written.IsSuccess); retiredBytes = await File.ReadAllBytesAsync(f.Path);
        });
        var resources = new ResourceAuthorizationService(f.Actors, [new InstalledApplicationResourceResolver(observed)]);
        var backend = new LinuxInstalledApplicationWidgetBackend(observed, f.Actors, resources, f.Owner, app.ApplicationId, app.Revision);
        Assert.Null(await backend.CaptureForActorAsync(f.Request, actor, default));
        Assert.Equal(2, observed.Reads); Assert.NotNull(retiredBytes);
        Assert.Equal(retiredBytes, await File.ReadAllBytesAsync(f.Path)); Assert.Equal(0, f.Provider.Calls);
        Assert.Equal(0, observed.LegacyCalls);
    }

    // Observes AFTER the actual canonical readonly owner returned; this is not a pause inside
    // FileHome I/O, a native transport or an atomic actor lease through surface publication.
    private sealed class ReadObserver(HomeInstalledApplicationRegistry actual, Func<Task> afterSecondRead)
        : IInstalledApplicationRegistry, IInstalledApplicationOriginalReadRegistry
    {
        public int Reads, LegacyCalls;
        public async ValueTask<InstalledApplicationReadSnapshot?> ReadExistingForActorAsync(AuthenticatedResourceActor actor, CancellationToken ct)
        {
            var result = await actual.ReadExistingForActorAsync(actor, ct);
            if (++Reads == 2) await afterSecondRead();
            return result;
        }
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct)
        { LegacyCalls++; throw new InvalidOperationException("Readonly capture must not discover."); }
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct)
        { LegacyCalls++; throw new InvalidOperationException("Readonly capture must not resolve launch."); }
    }

    private sealed class Observations : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "linux.xdg-desktop"; public int Calls;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        {
            Calls++;
            return ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([
                new("controlled-platform-profile", "Controlled platform", false, true,
                    [new("desktop:controlled.desktop", "desktop:controlled.desktop", "Canonical <app>",
                        "actual-observed-version", true)])]);
        }
    }
    private sealed class LegacyRegistry : IInstalledApplicationRegistry
    {
        public int Calls;
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct)
        { Calls++; throw new InvalidOperationException(); }
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct)
        { Calls++; throw new InvalidOperationException(); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "astra-card-" + Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public FileHomeCoreStateStore Store { get; }
        public HomeLocalProfileIdentity Actors { get; }
        public Observations Provider { get; } = new();
        public HomeInstalledApplicationRegistry Registry { get; }
        public ResourceAuthorizationService Resources { get; }
        public HomeNativeWidgetReference Owner { get; } = new("controlled-owner-not-installed", Guid.NewGuid(), "controlled-receipt",
            LinuxInstalledApplicationWidgetBackend.WidgetId, LinuxInstalledApplicationWidgetBackend.DefinitionRevision);
        public HomeNativeWidgetCaptureRequest Request => new(Owner, LinuxInstalledApplicationWidgetBackend.SurfaceReference, new(2, 1), 240, 120);
        public Fixture()
        {
            Directory.CreateDirectory(root); Path = System.IO.Path.Combine(root, "home.json"); Store = new(Path);
            Actors = new(Store, new OperatingSystemPrincipalSource()); Registry = new(Store, Actors, [Provider]);
            Resources = new(Actors, [new InstalledApplicationResourceResolver(Registry)]);
        }
        public LinuxInstalledApplicationWidgetBackend Backend(InstalledApplicationReference app)
            => new(Registry, Actors, Resources, Owner, app.ApplicationId, app.Revision);
        public void Dispose() => Directory.Delete(root, true);
    }
}
