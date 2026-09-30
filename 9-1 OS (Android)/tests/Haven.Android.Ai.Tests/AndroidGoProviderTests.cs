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
}

public sealed class AndroidGoProviderTests
{
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

    private static async Task<List<GoResult>> Results(AndroidInstalledApplicationsGoProvider provider, string? category = "Apps")
    { var list = new List<GoResult>(); await foreach (var item in provider.QueryAsync(new("", category), default)) list.Add(item); return list; }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        private static readonly AuthenticatedResourceActor Current = new("actor", "home-profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expectedActor);
    }
    private sealed class Observations : IInstalledApplicationObservationProvider
    {
        public string ProviderId => AndroidLauncherPlatformCatalog.ProviderId;
        public string Version = "1"; public bool Accessible = true;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>(
            [new("personal", "Personal", false, Accessible, [new("app", "app/main", "App", Version, true)])]);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-android-go-" + Guid.NewGuid().ToString("N"));
        public Observations Observations { get; } = new();
        public HomeInstalledApplicationRegistry Registry { get; }
        public AndroidLauncherPlatformCatalog Platform { get; } = new();
        public AndroidInstalledApplicationsGoProvider Go { get; }
        public Fixture(bool authorize)
        {
            var actors = new Actors(); var home = new FileHomeCoreStateStore(Path.Combine(_root, "home.json"));
            Registry = new(home, actors, [Observations]);
            Go = new(Registry, actors, new ResourceAuthorizationService(actors, authorize ? [new AndroidInstalledApplicationResourceResolver(Registry)] : []), Platform);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
