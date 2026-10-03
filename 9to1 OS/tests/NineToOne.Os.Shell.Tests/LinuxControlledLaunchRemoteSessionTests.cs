using Microsoft.Extensions.DependencyInjection;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Shell.Tests;

// Actual Home FileStream lease and held managed owner read. Actor/context source/authority are
// controlled protocol fixtures; they never establish a root supervisor or installed launch.
public sealed class LinuxControlledLaunchRemoteSessionTests
{
    [Fact]
    public async Task ExplicitRemoteModeCannotAdoptRealLocalLeaseDuringPendingOriginalContextRead()
    {
        Assert.True(OperatingSystem.IsLinux(), "Actual Linux lease/kernel fixture requires Linux; no prerequisite skip.");
        using var f = new Fixture(); var source = new HeldSource(); var authority = new Authority();
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(f.Actors, f.Paths, default);
        Assert.NotNull(lease);
        var gate = new LinuxControlledLaunchGate(f.Actors, authority, source);
        var peer = new HomeNativeObservedPeer(Environment.ProcessId,
            (await new OperatingSystemPrincipalSource().GetPrincipalAsync(default))!);
        var pending = gate.VerifyForActorAsync(peer, f.Actors.Current, "fixture", "fixture-role", default).AsTask();
        ExceptionDispatchInfo? failure = null;
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Throws<InvalidOperationException>(() => gate.BindHeldLease(lease));
            Assert.True(lease.IsHeld);
        }
        catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            source.Release.TrySetResult();
            try { await pending; } catch when (failure is not null) { }
        }
        failure?.Throw();
        Assert.False(await pending); Assert.Equal(0, authority.Calls); Assert.Equal(1, source.Reads);
        Assert.True(lease.IsHeld); Assert.False(File.Exists(Path.Combine(f.Paths.DataDirectory, "home.json")));
    }
    [Fact]
    public async Task NullRemoteContextDuringActorReplacementCannotReachAuthorityOrAdoptLocalLease()
    {
        Assert.True(OperatingSystem.IsLinux(), "Actual Linux lease/kernel fixture requires Linux; no prerequisite skip.");
        using var f = new Fixture(); var source = new HeldSource(); var authority = new Authority();
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(f.Actors, f.Paths, default);
        Assert.NotNull(lease); var original = f.Actors.Current;
        var gate = new LinuxControlledLaunchGate(f.Actors, authority, source);
        var peer = new HomeNativeObservedPeer(Environment.ProcessId,
            (await new OperatingSystemPrincipalSource().GetPrincipalAsync(default))!);
        var pending = gate.VerifyForActorAsync(peer, original, "fixture", "fixture-role", default).AsTask();
        ExceptionDispatchInfo? failure = null;
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            f.Actors.Current = original with { AuthenticationRevision = "replacement" };
            Assert.Throws<InvalidOperationException>(() => gate.BindHeldLease(lease));
        }
        catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            source.Release.TrySetResult();
            try { await pending; } catch when (failure is not null) { }
        }
        failure?.Throw();
        Assert.False(await pending); Assert.Equal(0, authority.Calls); Assert.Equal(0, source.CurrentChecks);
        Assert.True(lease.IsHeld);
    }
    [Fact]
    public void ExactOriginalTwoArgumentClrConstructorRemainsAvailable()
    {
        Assert.NotNull(typeof(LinuxControlledLaunchGate).GetConstructor([
            typeof(IAuthenticatedResourceActorSource), typeof(IHomeNativeControlledLaunchAuthority)]));
    }
    [Fact]
    public async Task ExplicitHomeFactoryKeepsActualLocalLeaseWhenRemotePortIsAlsoRegistered()
    {
        Assert.True(OperatingSystem.IsLinux(), "Actual Linux lease/kernel fixture requires Linux; no prerequisite skip.");
        using var f = new Fixture(); var source = new HeldSource(); var authority = new Authority();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticatedResourceActorSource>(f.Actors);
        services.AddSingleton<IHomeNativeControlledLaunchAuthority>(authority);
        services.AddSingleton<IHomeNativeControlledLaunchOriginalSessionContextSource>(source);
        services.AddSingleton<LinuxControlledLaunchGate>(sp => new(
            sp.GetRequiredService<IAuthenticatedResourceActorSource>(),
            sp.GetRequiredService<IHomeNativeControlledLaunchAuthority>()));
        using var provider = services.BuildServiceProvider();
        using var lease = await HomeNativeSessionLease.TryAcquireAsync(f.Actors, f.Paths, default);
        Assert.NotNull(lease); var gate = provider.GetRequiredService<LinuxControlledLaunchGate>();
        gate.BindHeldLease(lease);
        var peer = new HomeNativeObservedPeer(Environment.ProcessId,
            (await new OperatingSystemPrincipalSource().GetPrincipalAsync(default))!);
        Assert.False(await gate.VerifyForActorAsync(peer, f.Actors.Current, "fixture", "fixture-role", default));
        Assert.Equal(1, authority.Calls); Assert.Equal(0, source.Reads); Assert.True(lease.IsHeld);
    }
    private sealed class HeldSource : IHomeNativeControlledLaunchOriginalSessionContextSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads, CurrentChecks;
        public async ValueTask<HomeNativeControlledLaunchSessionContext?> GetForActorAsync(AuthenticatedResourceActor actor, CancellationToken ct)
        { Reads++; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return null; }
        public ValueTask<bool> IsCurrentForActorAsync(HomeNativeControlledLaunchSessionContext context, AuthenticatedResourceActor actor, CancellationToken ct)
        { CurrentChecks++; return ValueTask.FromResult(false); }
    }
    private sealed class Authority : IHomeNativeControlledLaunchAuthority
    {
        public int Calls;
        public ValueTask<bool> IsCurrentAsync(HomeNativeControlledLaunchObservation observation, CancellationToken ct)
        { Calls++; return ValueTask.FromResult(false); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "original");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult<AuthenticatedResourceActor?>(Current); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "remote-launch-mode-" + Guid.NewGuid().ToString("N"));
        public Actors Actors { get; } = new(); public Paths Paths { get; }
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
