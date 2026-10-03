using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Android;

// A platform boundary recorder, not an Android activation implementation or device proof.
internal sealed class AndroidLauncherPlatformCatalog
{
    public const string ProviderId = "android.launcher-apps";
    public List<(string Profile, string Entrypoint)> Calls { get; } = [];
    public void Launch(string profile, string entrypoint) => Calls.Add((profile, entrypoint));
    public List<(string Profile, string Entrypoint)> ShortcutQueries { get; } = [];
    public List<(string Profile, string Entrypoint, string Shortcut)> ShortcutCalls { get; } = [];
    public bool ShortcutAvailable { get; set; } = true;
    public Action? AfterShortcutQuery { get; set; }
    public IReadOnlyList<AndroidPlatformShortcut> ListShortcuts(string profile, string entrypoint)
    { ShortcutQueries.Add((profile, entrypoint)); AfterShortcutQuery?.Invoke(); return ShortcutAvailable ? [new("compose", "Compose")] : []; }
    public void LaunchShortcut(string profile, string entrypoint, string id)
    {
        if (!ShortcutAvailable || id != "compose") throw new InvalidOperationException("Shortcut unavailable.");
        ShortcutCalls.Add((profile, entrypoint, id));
    }
}

public sealed class AndroidGoProviderTests
{
    [Fact]
    public async Task StableOwnerResolutionSurvivesRenameAndReturnsOnlyCurrentAuthorizedRevision()
    {
        using var f = new Fixture(true); var original = Assert.Single(await Results(f.Go));
        var engine = new GoService([f.Go]); var locator = new GoCanonicalLocator("Home", "os.installed-application", original.Reference.Id);
        f.Observations.Label = "Renamed without matching stored label"; f.Observations.Version = "2";
        var resolved = await engine.ResolveAsync(AndroidInstalledApplicationsGoProvider.Id, locator);
        Assert.NotNull(resolved); Assert.Equal(original.Reference.Id, resolved.Reference.Id); Assert.Equal(f.Observations.Label, resolved.Label);
        Assert.NotEqual(original.Reference.Revision, resolved.Reference.Revision);
        await engine.InvokeAsync(resolved, "Open"); Assert.Single(f.Platform.Calls);
        f.Observations.Accessible = false;
        Assert.Null(await engine.ResolveAsync(AndroidInstalledApplicationsGoProvider.Id, locator));
        Assert.Single(f.Platform.Calls);
    }
    [Fact]
    public async Task StableOwnerResolutionWithoutReadAuthorityExposesNoResult()
    {
        using var f = new Fixture(false); var app = Assert.Single(await f.Registry.RefreshAsync(default));
        Assert.Null(await new GoService([f.Go]).ResolveAsync(AndroidInstalledApplicationsGoProvider.Id,
            new("Home", "os.installed-application", app.ApplicationId.ToString("D"))));
        Assert.Empty(f.Platform.Calls);
    }
    [Fact]
    public async Task DiscoveryRequiresCurrentOwnerReadAuthorization()
    {
        using var f = new Fixture(false);
        Assert.Empty(await Results(f.Go));
        Assert.Empty(f.Platform.Calls);
    }

    [Fact]
    public async Task CurrentCanonicalReferenceDelegatesToOwningPlatformAndStaleVersionIsDenied()
    {
        using var f = new Fixture(true);
        var result = Assert.Single(await Results(f.Go));
        await f.Go.InvokeAsync(result.Reference, "Open", default);
        Assert.Equal(("personal", "app/main"), Assert.Single(f.Platform.Calls));
        f.Observations.Version = "2"; await f.Registry.RefreshAsync(default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Go.InvokeAsync(result.Reference, "Open", default));
        Assert.Single(f.Platform.Calls);
    }

    [Fact]
    public async Task DisabledProfileAndExcludedCategoryExposeNoApplication()
    {
        using var f = new Fixture(true);
        f.Observations.Accessible = false;
        Assert.Empty(await Results(f.Go));
        Assert.Empty(await Results(f.Go, "Files"));
        Assert.Empty(f.Platform.Calls);
    }

    [Fact]
    public async Task TypedShortcutChecksOwnerAndCurrentRegistryBeforePlatformInvocation()
    {
        using var f = new Fixture(true); var app = Assert.Single(await f.Registry.RefreshAsync(default));
        var shortcut = Assert.Single(await f.Shortcuts.QueryAsync(app.ApplicationId, app.Revision, default));
        Assert.Equal(("personal", "app/main"), Assert.Single(f.Platform.ShortcutQueries));
        await f.Shortcuts.InvokeAsync(shortcut, default);
        Assert.Equal(("personal", "app/main", "compose"), Assert.Single(f.Platform.ShortcutCalls));
        f.Observations.Version = "2"; await f.Registry.RefreshAsync(default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Shortcuts.InvokeAsync(shortcut, default));
        Assert.Single(f.Platform.ShortcutCalls);
    }

    [Fact]
    public async Task ShortcutReadDenialDoesNotObservePlatformAndChangedSessionDoesNotExposeResults()
    {
        using var denied = new Fixture(false); var deniedApp = Assert.Single(await denied.Registry.RefreshAsync(default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.Shortcuts.QueryAsync(deniedApp.ApplicationId, deniedApp.Revision, default));
        Assert.Empty(denied.Platform.ShortcutQueries);
        using var f = new Fixture(true); var app = Assert.Single(await f.Registry.RefreshAsync(default));
        f.Platform.AfterShortcutQuery = () => f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "changed" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Shortcuts.QueryAsync(app.ApplicationId, app.Revision, default));
        Assert.Empty(f.Platform.ShortcutCalls);
    }

    [Fact]
    public async Task RemovedShortcutAndForeignProfileCannotInvokeOldCommand()
    {
        using var f = new Fixture(true); var app = Assert.Single(await f.Registry.RefreshAsync(default));
        var shortcut = Assert.Single(await f.Shortcuts.QueryAsync(app.ApplicationId, app.Revision, default));
        f.Platform.ShortcutAvailable = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Shortcuts.InvokeAsync(shortcut, default));
        f.Actors.Current = f.Actors.Current with { ProfileId = "other-profile" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Shortcuts.InvokeAsync(shortcut, default));
        Assert.Empty(f.Platform.ShortcutCalls);
    }

    [Fact]
    public async Task SessionChangeDuringCanonicalResolutionPreventsNativeShortcutObservation()
    {
        using var f = new Fixture(true); var app = Assert.Single(await f.Registry.RefreshAsync(default));
        var changing = new ChangeAfterResolve(f.Registry, () => f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "changed-before-platform" });
        var owner = new AndroidInstalledApplicationShortcuts(changing, f.Resources, f.Platform);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.QueryAsync(app.ApplicationId, app.Revision, default));
        Assert.Empty(f.Platform.ShortcutQueries); Assert.Empty(f.Platform.ShortcutCalls);
    }
    [Fact]
    public async Task OriginalQueryAndResolveUseActualCanonicalRegistryAndOwningPlatformInvocation()
    {
        using var f = new Fixture(true); var original = f.Actors.Current;
        var result = Assert.Single(await OriginalResults(f.Go, original));
        var resolved = await f.Go.ResolveForActorAsync(new("Home", "os.installed-application", result.Reference.Id), original, default);
        Assert.NotNull(resolved); Assert.Equal(result.Reference, resolved.Reference);
        await f.Go.InvokeForActorAsync(result.Reference, "Open", original, default);
        Assert.Equal(("personal", "app/main"), Assert.Single(f.Platform.Calls));
    }
    [Fact]
    public async Task ReplacedOriginalActorCannotDiscoverResolveOrInvokeAndPreservesActualStoredRegistry()
    {
        using var f = new Fixture(true); var original = f.Actors.Current;
        var result = Assert.Single(await OriginalResults(f.Go, original)); var bytes = await File.ReadAllBytesAsync(f.StatePath);
        var calls = f.Observations.Calls;
        f.Actors.Current = original with { AuthenticationRevision = "replacement" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => OriginalResults(f.Go, original));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Go.ResolveForActorAsync(new("Home", "os.installed-application", result.Reference.Id), original, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Go.InvokeForActorAsync(result.Reference, "Open", original, default));
        Assert.Equal(calls, f.Observations.Calls); Assert.Empty(f.Platform.Calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task OriginalProviderObservationSessionChangeCannotPublishResultsOrCreateReplacementRegistry()
    {
        using var f = new Fixture(true); var original = f.Actors.Current;
        f.Observations.DuringObservation = () => f.Actors.Current = original with { AuthenticationRevision = "replacement" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => OriginalResults(f.Go, original));
        Assert.Equal(1, f.Observations.Calls); Assert.Empty(f.Platform.Calls);
        Assert.DoesNotContain((await f.Home.ReadAsync()).State!.Records, r => r.RecordType == "home.installed-apps");
    }
    [Fact]
    public async Task MissingOriginalRegistryPortDeniesQueryAndResolverWithoutAmbientFallback()
    {
        using var f = new Fixture(true); var original = f.Actors.Current; var ambient = new AmbientOnly(f.Registry);
        var owner = new AndroidInstalledApplicationsGoProvider(ambient, f.Actors, f.Resources, f.Platform);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => OriginalResults(owner, original));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ResolveForActorAsync(new("Home", "os.installed-application", Guid.NewGuid().ToString("D")), original, default));
        var resolver = new AndroidInstalledApplicationResourceResolver(ambient);
        var decision = await resolver.EvaluateAsync(original, "os.application.read", new("os.installed-application", Guid.NewGuid().ToString("D"), "1", ResourceAccess.Read), default);
        Assert.False(decision.Allowed); Assert.Equal(0, ambient.Calls); Assert.Empty(f.Platform.Calls);
    }
    [Fact]
    public async Task OriginalShortcutQueryIssuesExactPrivateSelectionAndInvokesActualOriginalPackageProfile()
    {
        using var f = new Fixture(true); var actor = f.Actors.Current; var app = Assert.Single(await f.Registry.RefreshForActorAsync(actor, default));
        var selection = Assert.Single(await f.Shortcuts.QueryForActorAsync(app.ApplicationId, app.Revision, actor, default));
        Assert.Equal("Compose", selection.Label); Assert.Equal(("personal", "app/main"), Assert.Single(f.Platform.ShortcutQueries));
        await f.Shortcuts.InvokeOriginalAsync(selection, () => true, default);
        Assert.Equal(("personal", "app/main", "compose"), Assert.Single(f.Platform.ShortcutCalls));
    }
    [Fact]
    public async Task CopiedAndForeignShortcutSelectionsCannotInvokeOrObserveAnotherCanonicalOwner()
    {
        using var f = new Fixture(true); var actor = f.Actors.Current; var app = Assert.Single(await f.Registry.RefreshForActorAsync(actor, default));
        var original = Assert.Single(await f.Shortcuts.QueryForActorAsync(app.ApplicationId, app.Revision, actor, default));
        var copy = new AndroidOriginalShortcutSelection(f.Shortcuts, actor, original.Command);
        var foreign = new AndroidInstalledApplicationShortcuts(f.Registry, f.Resources, f.Platform);
        var calls = f.Observations.Calls; var bytes = await File.ReadAllBytesAsync(f.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Shortcuts.InvokeOriginalAsync(copy, () => true, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.InvokeOriginalAsync(original, () => true, default));
        Assert.Equal(calls, f.Observations.Calls); Assert.Empty(f.Platform.ShortcutCalls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task ActualNativeShortcutQuerySessionRetirementCannotIssueSelectionsOrRebindOldActor()
    {
        using var f = new Fixture(true); var actor = f.Actors.Current; var app = Assert.Single(await f.Registry.RefreshForActorAsync(actor, default));
        f.Platform.AfterShortcutQuery = () => f.Actors.Current = actor with { AuthenticationRevision = "replacement" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Shortcuts.QueryForActorAsync(app.ApplicationId, app.Revision, actor, default));
        Assert.Single(f.Platform.ShortcutQueries); Assert.Empty(f.Platform.ShortcutCalls);
    }
    [Fact]
    public async Task ValidReplacementActorCannotAdoptOldShortcutSelectionAndClosedHostCannotLaunchAfterOwnerRead()
    {
        using var f = new Fixture(true); var actor = f.Actors.Current; var app = Assert.Single(await f.Registry.RefreshForActorAsync(actor, default));
        var original = Assert.Single(await f.Shortcuts.QueryForActorAsync(app.ApplicationId, app.Revision, actor, default));
        f.Actors.Current = actor with { AuthenticationRevision = "replacement" };
        var replacement = Assert.Single(await f.Shortcuts.QueryForActorAsync(app.ApplicationId, app.Revision, f.Actors.Current, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Shortcuts.InvokeOriginalAsync(original, () => true, default));
        Assert.Empty(f.Platform.ShortcutCalls);
        var live = true; f.Observations.DuringObservation = () => live = false;
        var bytes = await File.ReadAllBytesAsync(f.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Shortcuts.InvokeOriginalAsync(replacement, () => live, default));
        Assert.Empty(f.Platform.ShortcutCalls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task MissingOriginalShortcutRegistryPortCannotFallBackToAmbientCanonicalAdmission()
    {
        using var f = new Fixture(true); var actor = f.Actors.Current; var app = Assert.Single(await f.Registry.RefreshForActorAsync(actor, default));
        var ambient = new AmbientOnly(f.Registry); var owner = new AndroidInstalledApplicationShortcuts(ambient, f.Resources, f.Platform);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.QueryForActorAsync(app.ApplicationId, app.Revision, actor, default));
        Assert.Equal(0, ambient.Calls); Assert.Empty(f.Platform.ShortcutQueries); Assert.Empty(f.Platform.ShortcutCalls);
    }
    private static async Task<List<GoResult>> OriginalResults(AndroidInstalledApplicationsGoProvider provider, AuthenticatedResourceActor original)
    { var results = new List<GoResult>(); await foreach (var result in provider.QueryForActorAsync(new("", "Apps"), original, default)) results.Add(result); return results; }
    private sealed class AmbientOnly(IInstalledApplicationRegistry inner) : IInstalledApplicationRegistry
    {
        public int Calls;
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct) { Calls++; return inner.RefreshAsync(ct); }
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct) { Calls++; return inner.ResolveLaunchAsync(id, revision, ct); }
    }
    private sealed class ChangeAfterResolve(IInstalledApplicationRegistry inner, Action change) : IInstalledApplicationRegistry
    {
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct) => inner.RefreshAsync(ct);
        public async ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct)
        { var app = await inner.ResolveLaunchAsync(id, revision, ct); change(); return app; }
    }

    private static async Task<List<GoResult>> Results(AndroidInstalledApplicationsGoProvider provider, string? category = "Apps")
    { var list = new List<GoResult>(); await foreach (var item in provider.QueryAsync(new("", category), default)) list.Add(item); return list; }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "home-profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expectedActor);
    }
    private sealed class Observations : IInstalledApplicationObservationProvider
    {
        public string ProviderId => AndroidLauncherPlatformCatalog.ProviderId;
        public string Version = "1"; public string Label = "App"; public bool Accessible = true;
        public int Calls; public Action? DuringObservation;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls++; DuringObservation?.Invoke(); return ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([new("personal", "Personal", false, Accessible, [new("app", "app/main", Label, Version, true)])]); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-android-go-" + Guid.NewGuid().ToString("N"));
        public string StatePath => Path.Combine(_root, "home.json");
        public FileHomeCoreStateStore Home { get; }
        public Actors Actors { get; } = new();
        public Observations Observations { get; } = new();
        public HomeInstalledApplicationRegistry Registry { get; }
        public AndroidLauncherPlatformCatalog Platform { get; } = new();
        public AndroidInstalledApplicationsGoProvider Go { get; }
        public AndroidInstalledApplicationShortcuts Shortcuts { get; }
        public ResourceAuthorizationService Resources { get; }
        public Fixture(bool authorize)
        {
            var actors = Actors; var home = Home = new FileHomeCoreStateStore(StatePath);
            Registry = new(home, actors, [Observations]);
            var resources = Resources = new ResourceAuthorizationService(actors, authorize ? [new AndroidInstalledApplicationResourceResolver(Registry)] : []);
            Go = new(Registry, actors, resources, Platform);
            Shortcuts = new(Registry, resources, Platform);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
