using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

public sealed class ShellNavigationGoTests
{
    [Fact]
    public async Task CanonicalPageNavigationPreviewsThenPersistsSamePageAndSpaceAfterReopen()
    {
        using var fixture = new Fixture(); var initial = await fixture.Configuration.GetAsync();
        var candidate = DesktopPageEdits.AddPage(initial.Effective, "Planning");
        var expected = DesktopPageEdits.Effective(candidate).ActivePageId;
        candidate = ShellEdits.DuplicateSpace(candidate, "Work");
        candidate = DesktopPageEdits.SetSpaceSpecific(candidate, true);
        candidate = DesktopPageEdits.RenamePage(candidate, "Private work page");
        await fixture.Save(candidate);
        var results = await fixture.Query(new("Planning", "Desktop Pages"));
        var target = Assert.Single(results);
        Assert.Equal(expected.ToString("D"), target.Reference.Id);
        Assert.Equal("OS", target.Reference.Owner);
        var before = await fixture.Configuration.GetAsync();
        await fixture.Provider.InvokeAsync(target.Reference, "Navigate", default);
        var preview = await fixture.Configuration.GetAsync();
        Assert.Equal(before.Stored.Revision, (await fixture.Store.ReadAsync(default)).Revision);
        Assert.Equal(expected, DesktopPageEdits.Effective(preview.Effective).ActivePageId);
        Assert.Equal(initial.Effective.ActiveSpaceId, preview.Effective.ActiveSpaceId);
        await fixture.Configuration.KeepAsync(preview.Preview!.Id);
        var reopened = await new ShellConfigurationService(fixture.Store).GetAsync();
        Assert.Equal(expected, DesktopPageEdits.Effective(reopened.Effective).ActivePageId);
        Assert.Equal(initial.Effective.ActiveSpaceId, reopened.Effective.ActiveSpaceId);
    }
    [Fact]
    public async Task StaleRevisionAndForeignProfileCannotNavigateExistingResults()
    {
        using var fixture = new Fixture(); var initial = await fixture.Configuration.GetAsync();
        var target = Assert.Single(await fixture.Query(new("", "Desktop Spaces")));
        await fixture.Save(ShellEdits.RenameSpace(initial.Effective, "Renamed"));
        await Assert.ThrowsAsync<ShellConfigurationConflictException>(() => fixture.Provider.InvokeAsync(target.Reference, "Navigate", default));
        target = Assert.Single(await fixture.Query(new("", "Desktop Spaces")));
        fixture.Actors.Current = fixture.Actors.Current with { ProfileId = "other-profile", AuthenticationRevision = "other-session" };
        await Assert.ThrowsAsync<ShellConfigurationConflictException>(() => fixture.Provider.InvokeAsync(target.Reference, "Navigate", default));
        Assert.Null((await fixture.Configuration.GetAsync()).Preview);
    }
    [Fact]
    public async Task NavigationCannotDiscardUnsavedPreviewOrInventAnEntity()
    {
        using var fixture = new Fixture(); var initial = await fixture.Configuration.GetAsync();
        var target = Assert.Single(await fixture.Query(new("", "Desktop Spaces")));
        var preview = await fixture.Configuration.PreviewAsync(initial.Stored.Revision, ShellEdits.RenameSpace(initial.Effective, "Unsaved"), TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Provider.InvokeAsync(target.Reference, "Navigate", default));
        Assert.Equal(preview.Preview!.Id, (await fixture.Configuration.GetAsync()).Preview!.Id);
        await fixture.Configuration.RevertAsync(preview.Preview.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Provider.InvokeAsync(target.Reference with { Id = Guid.NewGuid().ToString() }, "Navigate", default));
        Assert.Null((await fixture.Configuration.GetAsync()).Preview);
    }
    [Fact]
    public async Task ProviderDoesNotReadShellForExcludedCategoryAndReadResolverIsRequired()
    {
        var provider = new ShellNavigationGoProvider(new ShellConfigurationService(new ForbiddenStore()));
        await foreach (var result in provider.QueryAsync(new("", "Apps"), default)) Assert.Fail("An excluded category returned a shell result.");
        using var fixture = new Fixture(); await fixture.Configuration.GetAsync();
        var deniedStore = new HomeShellConfigurationStore(fixture.Home, fixture.Actors, new ResourceAuthorizationService(fixture.Actors, []));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => deniedStore.ReadAsync(default));
    }
    [Fact]
    public async Task LayerNavigationUsesExistingLayerIdentityWithoutCloningLayout()
    {
        using var fixture = new Fixture(); var initial = await fixture.Configuration.GetAsync();
        var candidate = ShellEdits.AddLayer(initial.Effective, "Developer tools"); var expected = candidate.ActiveSpace.Taskbar.ActiveLayerId;
        candidate = ShellEdits.StepLayer(candidate, -1); await fixture.Save(candidate);
        var target = Assert.Single(await fixture.Query(new("Developer", "Taskbar Layers")));
        await fixture.Provider.InvokeAsync(target.Reference, "Navigate", default);
        var preview = await fixture.Configuration.GetAsync();
        Assert.Equal(expected, preview.Effective.ActiveSpace.Taskbar.ActiveLayerId);
        Assert.Equal(2, preview.Effective.ActiveSpace.Taskbar.Layers.Count);
    }
    private sealed class ForbiddenStore : IShellConfigurationStore
    {
        public Task<ShellStoredConfiguration> ReadAsync(CancellationToken ct) => throw new InvalidOperationException("Excluded provider read metadata.");
        public Task<bool> TryWriteAsync(long revision, ShellStoredConfiguration next, CancellationToken ct) => throw new InvalidOperationException();
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expectedActor);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-go-navigation-" + Guid.NewGuid().ToString("N"));
        public FileHomeCoreStateStore Home { get; }
        public Actors Actors { get; } = new();
        public HomeShellConfigurationStore Store { get; }
        public ShellConfigurationService Configuration { get; }
        public ShellNavigationGoProvider Provider { get; }
        public Fixture()
        {
            Home = new(Path.Combine(_root, "home.json"));
            Store = new(Home, Actors, new ResourceAuthorizationService(Actors, [new ShellConfigurationResourceResolver(Home)]));
            Configuration = new(Store); Provider = new(Configuration);
        }
        public async Task Save(ShellConfiguration candidate)
        {
            var current = await Configuration.GetAsync();
            var preview = await Configuration.PreviewAsync(current.Stored.Revision, candidate, TimeSpan.FromMinutes(1));
            await Configuration.KeepAsync(preview.Preview!.Id);
        }
        public async Task<List<GoResult>> Query(GoQuery query)
        { var list = new List<GoResult>(); await foreach (var result in Provider.QueryAsync(query, default)) list.Add(result); return list; }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
