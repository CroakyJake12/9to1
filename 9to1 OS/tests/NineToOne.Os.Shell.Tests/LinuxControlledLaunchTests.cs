using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Shell.Tests;

public sealed class LinuxControlledLaunchTests
{
    [Fact]
    public async Task ActualHeldLeaseAndObservedExecutableCannotReplaceUnavailableLaunchAuthority()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new Fixture();
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(fixture.Actors, fixture.Paths);
        Assert.NotNull(lease);
        var gate = new LinuxControlledLaunchGate(fixture.Actors, new UnavailableHomeNativeControlledLaunchAuthority());
        gate.BindHeldLease(lease);
        Assert.False(await gate.VerifyAsync(Peer(), "os.shell", "", default));
        Assert.False(await gate.VerifyAsync(Peer(), "os.shell", HomeNativeSessionHostRequirement.RequiredRole, default));
        Assert.True(lease.IsHeld);
    }

    [Fact]
    public async Task PortReceivesFreshKernelAndActualLeaseContextAndDeniesChangedProfile()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new Fixture();
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(fixture.Actors, fixture.Paths);
        Assert.NotNull(lease);
        var authority = new RecordingAuthority();
        var gate = new LinuxControlledLaunchGate(fixture.Actors, authority);
        Assert.False(await gate.VerifyAsync(Peer(), "os.shell", "home.session-host", default));
        Assert.Null(authority.Observed);
        gate.BindHeldLease(lease);
        authority.BeforeReturn = () => fixture.Actors.Current = fixture.Actors.Current with { ProfileId = "different-profile" };
        Assert.False(await gate.VerifyAsync(Peer(), "os.shell", "home.session-host", default));
        var actual = Assert.IsType<HomeNativeControlledLaunchObservation>(authority.Observed);
        Assert.Equal(Environment.ProcessId, actual.ProcessId);
        Assert.Equal(Peer().OperatingSystemPrincipalId, actual.OperatingSystemPrincipalId);
        Assert.Equal("profile", actual.ProfileId);
        Assert.Equal(lease.LeaseIdentity.ToString("N"), actual.SessionLeaseIdentity);
        Assert.NotEmpty(actual.ProcessStartIdentity);
        Assert.StartsWith(new FileInfo("/proc/self/exe").LinkTarget + ";sha256:", actual.ExecutableIdentity, StringComparison.Ordinal);
        Assert.Equal("os.shell", actual.AppId); Assert.Equal("home.session-host", actual.RequiredRole);
        // This fixture tests port wiring only. A recording test double does not authenticate an installed process.
    }

    [Fact]
    public async Task ReleasedLeaseAndCancelledVerificationDoNotIssueAuthority()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new Fixture();
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(fixture.Actors, fixture.Paths);
        Assert.NotNull(lease);
        var authority = new RecordingAuthority(); var gate = new LinuxControlledLaunchGate(fixture.Actors, authority);
        gate.BindHeldLease(lease); authority.BeforeReturn = lease.Dispose;
        Assert.False(await gate.VerifyAsync(Peer(), "os.shell", "home.session-host", default));
        var unavailable = new UnavailableHomeNativeControlledLaunchAuthority();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await unavailable.IsCurrentAsync(authority.Observed!, cancelled.Token));
    }

    private static HomeNativeObservedPeer Peer() => new(Environment.ProcessId, "unix-euid:" +
        File.ReadAllLines("/proc/self/status").Single(l => l.StartsWith("Uid:", StringComparison.Ordinal))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[2]);
    private sealed class RecordingAuthority : IHomeNativeControlledLaunchAuthority
    {
        public HomeNativeControlledLaunchObservation? Observed;
        public Action? BeforeReturn;
        public ValueTask<bool> IsCurrentAsync(HomeNativeControlledLaunchObservation observation, CancellationToken ct)
        { Observed = observation; BeforeReturn?.Invoke(); return ValueTask.FromResult(true); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "1");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-launch-" + Guid.NewGuid().ToString("N"));
        public Actors Actors { get; } = new();
        public Paths Paths { get; }
        public Fixture() => Paths = new(_root);
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy");
    }
}
