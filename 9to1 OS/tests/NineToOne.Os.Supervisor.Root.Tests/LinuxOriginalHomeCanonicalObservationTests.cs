using System.Runtime.Versioning;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Supervisor.Root.Tests;

// Real persisted Home profile and actual lease object; controlled absent registry port
// is a denial fixture, not an installed host or administrator issuance.
[SupportedOSPlatform("linux")]
public sealed class LinuxOriginalHomeCanonicalObservationTests
{
    [Fact]
    public async Task Missing_original_read_port_never_calls_ambient_registry_or_changes_actual_home()
    {
        using var f = new Fixture();
        var actor = Assert.IsType<AuthenticatedResourceActor>(await f.Actors.GetCurrentAsync(default));
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(f.Actors, f.Paths);
        Assert.NotNull(lease);
        var before = await File.ReadAllBytesAsync(f.StatePath);
        var registry = new AmbientRegistry();
        Assert.Null(await LinuxOriginalHomeCanonicalObserver.ReadAsync(lease, actor, f.Actors, registry,
            new ResourceAuthorizationService(f.Actors, []), "9to1-os-shell.desktop", default));
        Assert.Equal(0, registry.Calls);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath));
    }

    [Fact]
    public async Task Disposed_original_lease_cannot_observe_or_initialize_actual_canonical_registry()
    {
        using var f = new Fixture();
        var actor = Assert.IsType<AuthenticatedResourceActor>(await f.Actors.GetCurrentAsync(default));
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(f.Actors, f.Paths);
        Assert.NotNull(lease);
        var before = await File.ReadAllBytesAsync(f.StatePath);
        lease.Dispose();
        var registry = new HomeInstalledApplicationRegistry(f.Store, f.Actors, []);
        Assert.Null(await LinuxOriginalHomeCanonicalObserver.ReadAsync(lease, actor, f.Actors, registry,
            new ResourceAuthorizationService(f.Actors, []), "9to1-os-shell.desktop", default));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath));
        Assert.DoesNotContain((await f.Store.ReadAsync()).State!.Records, r => r.RecordType == "home.installed-apps");
    }

    private sealed class AmbientRegistry : IInstalledApplicationRegistry
    {
        public int Calls;
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct)
        { ++Calls; throw new InvalidOperationException("Ambient discovery must not be called."); }
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct)
        { ++Calls; throw new InvalidOperationException("Ambient launch must not be called."); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "astra-canonical-home-" + Guid.NewGuid().ToString("N"));
        public string StatePath { get; }
        public FileHomeCoreStateStore Store { get; }
        public HomeLocalProfileIdentity Actors { get; }
        public Paths Paths { get; }
        public Fixture()
        {
            Directory.CreateDirectory(root); StatePath = Path.Combine(root, "home.json");
            Store = new(StatePath); Actors = new(Store, new OperatingSystemPrincipalSource()); Paths = new(root);
        }
        public void Dispose() => Directory.Delete(root, true);
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy");
    }
}
