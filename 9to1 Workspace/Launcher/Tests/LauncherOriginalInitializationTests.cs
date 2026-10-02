using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Launcher;
namespace NineToOne.Launcher.Tests;

public sealed class LauncherOriginalInitializationTests
{
    [Fact]
    public async Task GenuineMissingLayoutInitializesThroughOriginalRegistryAndReopensWithoutObservation()
    {
        using var f = new Fixture(); var original = f.Actors.Current;
        var initial = await f.Store.GetForActorAsync(original);
        Assert.Equal(1, initial.Revision); Assert.Equal(1, f.Provider.Calls);
        var app = Assert.Single(await f.Registry.RefreshForActorAsync(original, default));
        Assert.Equal(app.ApplicationId, Assert.Single(initial.Current.ActivePage.Items).ApplicationId);
        var bytes = await File.ReadAllBytesAsync(f.StatePath); var calls = f.Provider.Calls;
        var reopened = await f.Store.GetForActorAsync(original);
        Assert.Equal(initial.Revision, reopened.Revision); Assert.Equal(initial.Current.ActivePageId, reopened.Current.ActivePageId);
        Assert.Equal(calls, f.Provider.Calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task ReplacedOriginalActorCannotReadHomeOrInitializeAReplacementLayout()
    {
        using var f = new Fixture(); var original = f.Actors.Current;
        f.Actors.Current = original with { AuthenticationRevision = "replacement" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Store.GetForActorAsync(original));
        Assert.Equal(0, f.Provider.Calls); Assert.False(File.Exists(f.StatePath));
    }
    [Fact]
    public async Task MissingOriginalRegistryPortDeniesInitializationButAllowsExistingOriginalOwnedRead()
    {
        using var f = new Fixture(); var original = f.Actors.Current;
        var ambient = new AmbientRegistry(); var store = f.Owner(ambient);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.GetForActorAsync(original));
        Assert.Equal(0, ambient.Calls);
        Assert.DoesNotContain((await f.Home.ReadAsync()).State!.Records, r => r.RecordType == HomeLauncherLayoutStore.RecordType);
        var saved = await f.Store.GetForActorAsync(original); var bytes = await File.ReadAllBytesAsync(f.StatePath);
        var shown = await store.GetForActorAsync(original);
        Assert.Equal(saved.Revision, shown.Revision); Assert.Equal(0, ambient.Calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task SessionChangeInsideActualProviderObservationPersistsNoRegistryOrLayout()
    {
        using var f = new Fixture(); var original = f.Actors.Current;
        f.Provider.DuringObservation = () => f.Actors.Current = original with { AuthenticationRevision = "replacement" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Store.GetForActorAsync(original));
        Assert.Equal(1, f.Provider.Calls);
        Assert.DoesNotContain((await f.Home.ReadAsync()).State!.Records, r => r.RecordType == HomeLauncherLayoutStore.RecordType || r.RecordType == "home.installed-apps");
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class Provider : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "android-test"; public int Calls; public Action? DuringObservation;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls++; DuringObservation?.Invoke(); return ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([new("personal", "Personal", false, true, [new("first", "first/main", "First app", "1", true)])]); }
    }
    private sealed class AmbientRegistry : IInstalledApplicationRegistry
    {
        public int Calls;
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Ambient registry must not be used."); }
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct) => throw new InvalidOperationException("No launch fixture.");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "astra-original-launcher-init-" + Guid.NewGuid());
        public string StatePath => Path.Combine(directory, "home.json");
        public FileHomeCoreStateStore Home { get; } public Actors Actors { get; } = new(); public Provider Provider { get; } = new();
        public HomeInstalledApplicationRegistry Registry { get; } public HomeLauncherLayoutStore Store { get; }
        public Fixture() { Home = new(StatePath); Registry = new(Home, Actors, [Provider]); Store = Owner(Registry); }
        public HomeLauncherLayoutStore Owner(IInstalledApplicationRegistry registry) => new(Home, Actors, new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(Home)]), registry);
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
