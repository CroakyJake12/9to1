using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Owning Windows controls, separate from the preserved Linux Console cases.
/// Run this class on actual Windows; a Linux host is an explicit failing prerequisite,
/// not a successful early-return substitute for Windows principal or native bootstrap.</summary>
public sealed class WindowsHomeDomainCompositionTests
{
    [Fact]
    public async Task Genuine_Windows_principal_registers_same_Home_tuple_without_CAKE_identity_or_grants()
    {
        Assert.True(OperatingSystem.IsWindows(), "This owning test requires actual Windows.");
        var root = NewRoot(); var paths = new Paths(root); var principal = new OperatingSystemPrincipalSource();
        var home = Create(paths, principal);
        try
        {
            var services = Graph(paths); services.AddHavenOwnedWindowsHomeDomain(home, paths, principal);
            await using var provider = services.BuildServiceProvider();
            var components = provider.RequireOriginalWindowsHomeComponents(home);
            Assert.Same(home.StateStore, components.StateStore); Assert.Same(home.Profiles, components.Profiles);
            Assert.Same(home.Permissions, components.Permissions); Assert.Same(home.Broker, components.Broker);
            Assert.Same(principal, provider.GetRequiredService<ITrustedHostPrincipalSource>());
            Assert.False(home.InstalledPeerAdmissionConfigured); Assert.Null(home.OriginalStartTask);
            var actualPrincipal = await principal.GetPrincipalAsync(CancellationToken.None);
            Assert.NotNull(actualPrincipal); Assert.StartsWith("windows-sid:", actualPrincipal);
            var actor = await home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor); Assert.StartsWith("local-profile:", actor.ActorId);
            Assert.Null(actor.AccountId); Assert.Null(actor.OrganisationId);
            var permissions = await home.Permissions.GetSnapshotAsync(cancellationToken: CancellationToken.None);
            Assert.Empty(permissions.Grants); Assert.Empty(permissions.PendingRequests);
            Assert.Null(home.OriginalStartTask); // Registration/profile capture does not certify bootstrap.
        }
        finally { await home.CloseAndDrainAsync(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Existing_Home_or_foreign_principal_is_refused_before_descriptor_mutation()
    {
        Assert.True(OperatingSystem.IsWindows(), "This owning test requires actual Windows.");
        var root = NewRoot(); var paths = new Paths(root); var principal = new OperatingSystemPrincipalSource();
        var home = Create(paths, principal);
        try
        {
            var existing = Graph(paths); existing.AddSingleton<IHomeCoreStateStore>(home.StateStore);
            var originalRows = existing.ToArray();
            Assert.Throws<InvalidOperationException>(() => existing.AddHavenOwnedWindowsHomeDomain(home, paths, principal));
            Assert.Equal(originalRows, existing.ToArray());
            var foreign = Graph(paths); originalRows = foreign.ToArray();
            Assert.Throws<UnauthorizedAccessException>(() => foreign.AddHavenOwnedWindowsHomeDomain(home, paths, new OperatingSystemPrincipalSource()));
            Assert.Equal(originalRows, foreign.ToArray());
            Assert.Null(home.OriginalStartTask); Assert.False(home.InstalledPeerAdmissionConfigured);
        }
        finally { await home.CloseAndDrainAsync(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Real_Windows_Home_bootstrap_coalesces_original_start_and_drain_without_installed_peer_admission()
    {
        Assert.True(OperatingSystem.IsWindows(), "This owning test requires actual Windows.");
        var root = NewRoot(); var paths = new Paths(root); var principal = new OperatingSystemPrincipalSource();
        var home = Create(paths, principal);
        try
        {
            var start = home.StartOriginalAsync(); Assert.Same(start, home.StartOriginalAsync());
            Assert.Same(start, home.OriginalStartTask); await start;
            var actor = await home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor); Assert.Null(actor.AccountId);
            Assert.False(home.InstalledPeerAdmissionConfigured);
            var close = home.CloseAndDrainAsync(); Assert.Same(close, home.CloseAndDrainAsync()); await close;
            Assert.Same(close, home.OriginalCloseTask);
            Assert.Throws<ObjectDisposedException>(() => { _ = home.StartOriginalAsync(); });
        }
        finally { await home.CloseAndDrainAsync(); Directory.Delete(root, true); }
    }

    private static string NewRoot()
    { var root = Path.Combine(Path.GetTempPath(), "astra-windows-home48-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    private static ServiceCollection Graph(IAppPaths paths)
    { var services = new ServiceCollection(); services.AddSingleton(paths); return services; }
    private static HomeNativeWindowsComposition Create(IAppPaths paths, ITrustedHostPrincipalSource principal) =>
        new(new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json")), principal, paths,
            new("9to1.home.windows-control48." + Guid.NewGuid().ToString("N")));
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
