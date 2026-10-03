using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;
namespace NineToOne.Os.Shell.Tests;
public sealed class GoHomeCustomizationTests
{
    [Fact]
    public async Task OriginalHomePreviewKeepReopenPreservesOrderVisibilitySizeGroupAndLayout()
    {
        using var f = new Fixture(); var original = await f.Configuration.GetAsync();
        var candidate = GoHomeEdits.Move(original.Effective, GoHomeSectionKind.AllApps, 0);
        candidate = GoHomeEdits.Present(candidate, GoHomeSectionKind.Suggested, false, GoHomeSectionSize.Large, "Work");
        candidate = GoHomeEdits.Configure(candidate, candidate.EffectiveGoHome with { Layout = GoHomeLayout.Dashboard });
        var preview = await f.Configuration.PreviewAsync(original.Stored, candidate, TimeSpan.FromSeconds(30));
        Assert.Equal(GoHomeSectionKind.Pinned, (await f.Configuration.GetAsync()).Stored.Current.EffectiveGoHome.Sections[0].Kind);
        await f.Configuration.KeepAsync(preview.Preview!.Id);
        var saved = (await f.Reopen().GetAsync()).Stored;
        Assert.Equal(GoHomeSectionKind.AllApps, saved.Current.EffectiveGoHome.Sections[0].Kind);
        var suggested = saved.Current.EffectiveGoHome.Sections.Single(s => s.Kind == GoHomeSectionKind.Suggested);
        Assert.False(suggested.Visible); Assert.Equal(GoHomeSectionSize.Large, suggested.Size); Assert.Equal("Work", suggested.Group);
        Assert.Equal(GoHomeLayout.Dashboard, saved.Current.EffectiveGoHome.Layout);
        Assert.Equal(GoHomeLayout.StartMenu, saved.Previous!.EffectiveGoHome.Layout);
    }
    [Fact]
    public async Task ReplacedSessionCannotKeepOldPresentationOrAdoptItIntoCurrentHome()
    {
        using var f = new Fixture(); var original = await f.Configuration.GetAsync();
        var preview = await f.Configuration.PreviewAsync(original.Stored, GoHomeEdits.Move(original.Effective, GoHomeSectionKind.AllApps, 0), TimeSpan.FromSeconds(30));
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "replacement" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Configuration.KeepAsync(preview.Preview!.Id));
        Assert.Equal(original.Stored.Revision, (await f.Configuration.GetAsync()).Stored.Revision);
    }
    [Fact]
    public async Task MutableCallerListCannotChangePreviewAndInvalidSectionsCannotBeSaved()
    {
        using var f = new Fixture(); var original = await f.Configuration.GetAsync();
        var sections = GoHomeConfiguration.Default().Sections.ToArray();
        var candidate = GoHomeEdits.Configure(original.Effective, new(GoHomeLayout.CompactSearch, sections));
        var preview = await f.Configuration.PreviewAsync(original.Stored, candidate, TimeSpan.FromSeconds(30));
        sections[0] = sections[0] with { Visible = false };
        Assert.True(preview.Effective.EffectiveGoHome.Sections[0].Visible);
        await f.Configuration.RevertAsync(preview.Preview!.Id);
        Assert.Throws<InvalidDataException>(() => GoHomeEdits.Configure(original.Effective, new(GoHomeLayout.StartMenu, [sections[0], sections[0], sections[2], sections[3]])));
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "astra-go-home-" + Guid.NewGuid());
        public Guid ApplicationId { get; } = Guid.NewGuid(); public Actors Actors { get; } = new();
        public ShellConfigurationService Configuration { get; }
        public Fixture()
        {
            var home = new FileHomeCoreStateStore(Path.Combine(directory, "home.json"));
            Configuration = new(new HomeShellConfigurationStore(home, Actors, new ResourceAuthorizationService(Actors, [new ShellConfigurationResourceResolver(home)])));
        }
        public ShellConfigurationService Reopen()
        {
            var home = new FileHomeCoreStateStore(Path.Combine(directory, "home.json"));
            return new(new HomeShellConfigurationStore(home, Actors, new ResourceAuthorizationService(Actors, [new ShellConfigurationResourceResolver(home)])));
        }
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
