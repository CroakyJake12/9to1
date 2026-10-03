using System.Runtime.Versioning;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;
namespace NineToOne.Os.Supervisor.Root.Tests;

// Actual FileHome/kernel personal profile and genuine local Home lease. These
// negative cases never impersonate root or claim an installed launch positive.
[SupportedOSPlatform("linux")]
public sealed class LinuxAdministratorOriginalHomeLaunchClientTests
{
    [Fact]
    public async Task Retired_actual_original_lease_refuses_before_connect_without_home_write()
    {
        using var f = new Fixture();
        var actor = Assert.IsType<AuthenticatedResourceActor>(await f.Actors.GetCurrentAsync(default));
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(f.Actors, f.Paths);
        Assert.NotNull(lease);
        var endpoint = "/etc/astra-unconnected-authority-" + Guid.NewGuid().ToString("N") + ".sock";
        Assert.True(LinuxRootOwnedFiles.DirectoryImmutable("/etc"));
        await using var client = await LinuxAdministratorOriginalHomeLaunchClient.CaptureAsync(endpoint, lease, f.Actors, default);
        Assert.NotNull(client);
        var before = await File.ReadAllBytesAsync(f.StatePath);
        lease.Dispose();
        Assert.False(await client.IsCurrentAsync(new(Environment.ProcessId,
            "unix-euid:unissued", "unissued-start", "unissued-executable", actor.ProfileId,
            lease.LeaseIdentity.ToString("N"), "unissued.owner", ""), default));
        Assert.Null(typeof(LinuxAdministratorOriginalHomeLaunchClient).GetField("_socket",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(client));
        Assert.False(File.Exists(endpoint));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task Disposed_client_cannot_adopt_still_held_local_lease_or_open_administrator_socket()
    {
        using var f = new Fixture();
        var actor = Assert.IsType<AuthenticatedResourceActor>(await f.Actors.GetCurrentAsync(default));
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(f.Actors, f.Paths);
        Assert.NotNull(lease);
        var endpoint = "/etc/astra-unconnected-authority-" + Guid.NewGuid().ToString("N") + ".sock";
        Assert.True(LinuxRootOwnedFiles.DirectoryImmutable("/etc"));
        var client = await LinuxAdministratorOriginalHomeLaunchClient.CaptureAsync(endpoint, lease, f.Actors, default);
        Assert.NotNull(client);
        var before = await File.ReadAllBytesAsync(f.StatePath);
        await client.DisposeAsync(); await client.DisposeAsync();
        Assert.True(lease.IsHeld);
        Assert.False(await client.IsCurrentAsync(new(Environment.ProcessId,
            "unix-euid:unissued", "unissued-start", "unissued-executable", actor.ProfileId,
            lease.LeaseIdentity.ToString("N"), "unissued.owner", ""), default));
        Assert.Null(typeof(LinuxAdministratorOriginalHomeLaunchClient).GetField("_socket",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(client));
        Assert.False(File.Exists(endpoint));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task Slot_binds_only_once_and_drains_without_releasing_the_actual_home_lease()
    {
        using var f = new Fixture();
        var actor = Assert.IsType<AuthenticatedResourceActor>(await f.Actors.GetCurrentAsync(default));
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(f.Actors, f.Paths);
        Assert.NotNull(lease);
        Assert.True(LinuxRootOwnedFiles.DirectoryImmutable("/etc"));
        var endpoint = "/etc/astra-unconnected-authority-" + Guid.NewGuid().ToString("N") + ".sock";
        await using var client = await LinuxAdministratorOriginalHomeLaunchClient.CaptureAsync(endpoint, lease, f.Actors, default);
        Assert.NotNull(client);
        await using var slot = new LinuxOriginalHomeLaunchAuthoritySlot();
        var before = await File.ReadAllBytesAsync(f.StatePath);
        Assert.True(slot.BindOriginal(client));
        Assert.False(slot.BindOriginal(client));
        await slot.DisposeAsync(); await slot.DisposeAsync();
        Assert.False(slot.BindOriginal(client));
        Assert.True(lease.IsHeld);
        Assert.False(await slot.IsCurrentAsync(new(Environment.ProcessId,
            "unix-euid:unissued", "unissued-start", "unissued-executable", actor.ProfileId,
            lease.LeaseIdentity.ToString("N"), "unissued.owner", ""), default));
        Assert.Null(typeof(LinuxAdministratorOriginalHomeLaunchClient).GetField("_socket",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(client));
        Assert.False(File.Exists(endpoint));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath));
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
