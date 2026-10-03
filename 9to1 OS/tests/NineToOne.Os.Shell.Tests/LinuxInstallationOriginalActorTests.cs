using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;
namespace NineToOne.Os.Shell.Tests;

public sealed class LinuxInstallationOriginalActorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOriginalRegistryPortDeclinesActualPeerBeforePrincipalOrAmbientRegistryObservation(bool host)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture(); var ambient = new AmbientOnly(f.Registry); var principals = new Principals();
        var verifier = f.Verifier(ambient, principals); var peer = await Peer();
        var identity = host ? await verifier.VerifyHostAsync(peer, new("os.shell", "desktop:9to1-os-shell.desktop"), default)
            : await verifier.VerifyAsync(peer, default);
        Assert.Null(identity); Assert.Equal(0, principals.Calls); Assert.Equal(0, ambient.Calls); Assert.False(File.Exists(f.StatePath));
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OriginalActorRetirementAtCaptureOrActualKernelPrincipalReadDeclinesWithoutStoredRegistryChange(bool host, bool duringPrincipal)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture(); var original = f.Actors.Current;
        await f.Registry.RefreshForActorAsync(original, default); var bytes = await File.ReadAllBytesAsync(f.StatePath);
        var principals = new Principals(); Action revoke = () => f.Actors.Current = original with { AuthenticationRevision = "revoked" };
        if (duringPrincipal) principals.AfterRead = revoke;
        else { f.Actors.AfterRead = revoke; f.Actors.RevokeAtRead = f.Actors.Reads + 1; }
        var verifier = f.Verifier(f.Registry, principals); var peer = await Peer();
        var identity = host ? await verifier.VerifyHostAsync(peer, new("os.shell", "desktop:9to1-os-shell.desktop"), default)
            : await verifier.VerifyAsync(peer, default);
        Assert.Null(identity); Assert.Equal(duringPrincipal ? 1 : 0, principals.Calls);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
        Assert.Equal("revoked", f.Actors.Current.AuthenticationRevision);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitOriginalPeerVerifierDeniesReplacementBeforeActualPrincipalAndPreservesStoredRegistry(bool host)
    {
        Assert.True(OperatingSystem.IsLinux(), "Actual Linux peer fixture requires Linux; no prerequisite skip.");
        using var f = new Fixture(); var original = f.Actors.Current;
        await f.Registry.RefreshForActorAsync(original, default); var before = await File.ReadAllBytesAsync(f.StatePath);
        f.Actors.Current = original with { AuthenticationRevision = "explicit-replacement" };
        var principals = new Principals(); var verifier = f.Verifier(f.Registry, principals);
        var peer = await Peer();
        Assert.Null(host ? await verifier.VerifyHostForActorAsync(peer, new("os.shell", "desktop:9to1-os-shell.desktop"), original, default)
            : await verifier.VerifyForActorAsync(peer, original, default));
        Assert.Equal(0, principals.Calls); Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task ExplicitOriginalControlledLaunchDeniesReplacementBeforeIndependentAuthority()
    {
        Assert.True(OperatingSystem.IsLinux(), "Actual Linux peer fixture requires Linux; no prerequisite skip.");
        using var f = new Fixture(); var original = f.Actors.Current; var authority = new CountingAuthority();
        var gate = new LinuxControlledLaunchGate(f.Actors, authority);
        f.Actors.Current = original with { AuthenticationRevision = "controlled-launch-replacement" };
        Assert.False(await gate.VerifyForActorAsync(await Peer(), original, "fixture", "fixture-role", default));
        Assert.Equal(0, authority.Calls); Assert.False(File.Exists(f.StatePath));
    }
    private sealed class CountingAuthority : IHomeNativeControlledLaunchAuthority
    {
        public int Calls;
        public ValueTask<bool> IsCurrentAsync(HomeNativeControlledLaunchObservation observation, CancellationToken ct)
        { Calls++; return ValueTask.FromResult(false); }
    }
    // Actual /proc-backed principal identity only; these tests do not fabricate receipts, package identity or launch authority.
    private static async Task<HomeNativeObservedPeer> Peer() => new(Environment.ProcessId,
        (await new OperatingSystemPrincipalSource().GetPrincipalAsync(default))!);
    private sealed class Principals : ITrustedHostPrincipalSource
    {
        public int Calls; public Action? AfterRead;
        public async ValueTask<string?> GetPrincipalAsync(CancellationToken ct)
        { Calls++; var principal = await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct); AfterRead?.Invoke(); return principal; }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public int Reads, RevokeAtRead = -1; public Action? AfterRead;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); var actor = Current; if (++Reads == RevokeAtRead) AfterRead?.Invoke(); return ValueTask.FromResult<AuthenticatedResourceActor?>(actor); }
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class AmbientOnly(IInstalledApplicationRegistry inner) : IInstalledApplicationRegistry
    {
        public int Calls;
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct) { Calls++; return inner.RefreshAsync(ct); }
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct) { Calls++; return inner.ResolveLaunchAsync(id, revision, ct); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "astra-linux-original-install-" + Guid.NewGuid());
        public string StatePath => Path.Combine(directory, "home.json"); public Actors Actors { get; } = new();
        public HomeInstalledApplicationRegistry Registry { get; }
        public Fixture() => Registry = new(new FileHomeCoreStateStore(StatePath), Actors, []);
        public LinuxInstallationPeerVerifier Verifier(IInstalledApplicationRegistry registry, ITrustedHostPrincipalSource principals)
            => new(Actors, principals, registry, new LinuxControlledLaunchGate(Actors, new UnavailableHomeNativeControlledLaunchAuthority()));
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
