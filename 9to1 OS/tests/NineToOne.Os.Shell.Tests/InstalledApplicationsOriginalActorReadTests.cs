using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

// Actual FileHome/local identity/registry; controlled platform observation. No native launch.
public sealed class InstalledApplicationsOriginalActorReadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacedPrincipalBeforeOriginalDiscoveryNeverObservesOrPublishes(bool resolve)
    {
        using var f = new Fixture(); var original = (await f.Actors.GetCurrentAsync(default))!;
        f.Principal.Value = "replacement-principal";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.ReadAsync(original, resolve));
        Assert.Equal(0, f.Observations.Calls); await f.AssertNoInstalledRecordsAsync();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuspendedOwningDiscoveryCannotAdoptReplacementPrincipal(bool resolve)
    {
        using var f = new Fixture(); var original = (await f.Actors.GetCurrentAsync(default))!;
        f.Observations.Suspend = true;
        var pending = f.ReadAsync(original, resolve);
        await f.Observations.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Principal.Value = "replacement-principal"; f.Observations.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(1, f.Observations.Calls); await f.AssertNoInstalledRecordsAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => { await f.Actors.GetCurrentAsync(default); });
    }
    [Fact]
    public async Task SuspendedDiscoveryRejectsActualNewIdentitySessionRevisionForSameProfile()
    {
        using var f = new Fixture(); var original = (await f.Actors.GetCurrentAsync(default))!;
        var actors = new SwitchingActors(f.Actors);
        var registry = new HomeInstalledApplicationRegistry(f.Store, actors, [f.Observations]);
        var resources = new ResourceAuthorizationService(actors, [new InstalledApplicationResourceResolver(registry)]);
        var provider = new InstalledApplicationsGoProvider(registry, new LinuxApplicationLauncher(registry, resources, actors));
        f.Observations.Suspend = true;
        async Task ReadAsync()
        { await foreach (var row in provider.QueryForActorAsync(new(""), original, default)) throw new InvalidOperationException("Unexpected row"); }
        var pending = ReadAsync();
        await f.Observations.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        actors.Current = new HomeLocalProfileIdentity(f.Store, f.Principal);
        var replacement = (await actors.GetCurrentAsync(default))!;
        Assert.Equal(original.ProfileId, replacement.ProfileId);
        Assert.NotEqual(original.AuthenticationRevision, replacement.AuthenticationRevision);
        f.Observations.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(1, f.Observations.Calls); await f.AssertNoInstalledRecordsAsync();
    }
    [Fact]
    public async Task ResourceActorReadSuspendedThenReplacedNeverEntersForeignResolver()
    {
        using var f = new Fixture(); var original = (await f.Actors.GetCurrentAsync(default))!;
        var held = new HeldActors(f.Actors); var counted = new CountedResolver(new InstalledApplicationResourceResolver(f.Registry));
        var launcher = new LinuxApplicationLauncher(f.Registry, new ResourceAuthorizationService(held, [counted]), f.Actors);
        var pending = launcher.ResolveForReadForActorAsync(Guid.NewGuid(), 1, original, default);
        await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Principal.Value = "replacement-principal"; held.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(0, counted.Calls); Assert.Equal(0, f.Observations.Calls);
        await f.AssertNoInstalledRecordsAsync();
    }
    [Fact]
    public async Task OriginalLaunchResourceActorReadSuspendedThenReplacedNeverReachesResolverOrNativeStart()
    {
        using var f = new Fixture(); var original = (await f.Actors.GetCurrentAsync(default))!;
        var held = new HeldActors(f.Actors); var counted = new CountedResolver(new InstalledApplicationResourceResolver(f.Registry));
        var launcher = new LinuxApplicationLauncher(f.Registry, new ResourceAuthorizationService(held, [counted]), f.Actors);
        var pending = launcher.LaunchForActorAsync(Guid.NewGuid(), 1, original, default);
        await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Principal.Value = "replacement-principal"; held.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(0, counted.Calls); Assert.Equal(0, f.Observations.Calls);
        await f.AssertNoInstalledRecordsAsync();
    }
    [Fact]
    public async Task MissingOriginalRegistryDeniesQueryAndResolveWithoutLegacyCalls()
    {
        var legacy = new Legacy(); var actor = new AuthenticatedResourceActor("a", "p", null, null, "r");
        var actors = new FixedActors(actor); var launcher = new LinuxApplicationLauncher(legacy, new ResourceAuthorizationService(actors, []), actors);
        var provider = new InstalledApplicationsGoProvider(legacy, launcher);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        { await foreach (var row in provider.QueryForActorAsync(new(""), actor, default)) throw new InvalidOperationException("Unexpected row"); });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ResolveForActorAsync(Locator(), actor, default));
        Assert.Equal(0, legacy.Calls);
    }
    private static GoCanonicalLocator Locator() => new("Home", "os.installed-application", Guid.NewGuid().ToString("D"));
    private sealed class Principal : ITrustedHostPrincipalSource
    { public string Value = "original-principal"; public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>(Value); }
    private sealed class Observations : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "linux.xdg-desktop"; public bool Suspend; public int Calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        { Calls++; Entered.TrySetResult(); if (Suspend) await Release.Task.WaitAsync(ct);
          return [new("original-principal", "Original", false, true, [new("controlled-app", "controlled-entry", "Controlled", null, true)])]; }
    }
    private sealed class SwitchingActors(HomeLocalProfileIdentity initial) : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public HomeLocalProfileIdentity Current = initial;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => Current.GetCurrentAsync(ct);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct)
            => Current.CheckAsync(state, expected, phase, ct);
    }
    private sealed class HeldActors(IAuthenticatedResourceActorSource actual) : IAuthenticatedResourceActorSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return await actual.GetCurrentAsync(ct); }
    }
    private sealed class CountedResolver(ICanonicalResourceAccessResolver actual) : ICanonicalResourceAccessResolver
    { public string ResourceKind => actual.ResourceKind; public int Calls;
      public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken ct)
      { Calls++; return actual.EvaluateAsync(actor, action, scope, ct); } }
    private sealed class FixedActors(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    { public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(actor); }
    private sealed class Legacy : IInstalledApplicationRegistry
    { public int Calls;
      public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct) { Calls++; throw new InvalidOperationException(); }
      public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct) { Calls++; throw new InvalidOperationException(); } }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "astra-original-app-read-" + Guid.NewGuid().ToString("N"));
        public Principal Principal { get; } = new(); public Observations Observations { get; } = new();
        public FileHomeCoreStateStore Store { get; } public HomeLocalProfileIdentity Actors { get; }
        public HomeInstalledApplicationRegistry Registry { get; } public InstalledApplicationsGoProvider Provider { get; }
        public Fixture()
        { Directory.CreateDirectory(root); Store = new(Path.Combine(root, "home.json")); Actors = new(Store, Principal);
          Registry = new(Store, Actors, [Observations]); var resources = new ResourceAuthorizationService(Actors, [new InstalledApplicationResourceResolver(Registry)]);
          Provider = new(Registry, new LinuxApplicationLauncher(Registry, resources, Actors)); }
        public async Task ReadAsync(AuthenticatedResourceActor actor, bool resolve)
        { if (resolve) Assert.Null(await Provider.ResolveForActorAsync(Locator(), actor, default));
          else { await foreach (var row in Provider.QueryForActorAsync(new(""), actor, default)) throw new InvalidOperationException("Unexpected row"); } }
        public async Task AssertNoInstalledRecordsAsync() => Assert.DoesNotContain((await Store.ReadAsync()).State!.Records, r => r.RecordType == "home.installed-apps");
        public void Dispose() => Directory.Delete(root, true);
    }
}
