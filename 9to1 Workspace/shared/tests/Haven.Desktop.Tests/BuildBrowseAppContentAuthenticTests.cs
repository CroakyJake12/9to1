using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>
/// Authentic Build (Dev) native session controls over the SAME genuine Linux Home/Files/SQLite rig.
/// They drive the REAL <see cref="BuildBrowseAppContentSession"/> with real Dev services and a real
/// saved-project catalogue; only the borrowed host Home readiness boundary (a host seam) is supplied
/// by a deterministic test double. These do not claim a real Home readiness producer.
/// </summary>
public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    private sealed record BuildTestBorrowedResources(ICuiSceneReadiness? Readiness = null) : IBuildBorrowedResources
    {
        public ICuiSceneReadiness BorrowOriginalHomeReadiness() =>
            Readiness ?? ReadyReadiness.Instance;
        public void DemandOriginalHomeJoin() { }
        public Task CloseAndDrainAsync() => Task.CompletedTask;
    }

    private sealed class ReadyReadiness : ICuiSceneReadiness
    {
        public static readonly ReadyReadiness Instance = new();
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            new(new(CuiSceneAvailabilityState.Ready, "FixtureHomeReady", "The authentic fixture Home boundary is ready."));
    }

    /// <summary>The normal Build landing presents the real saved-project picker (not a placeholder or
    /// NotSupported), publishes, reports a clean close, and retires cleanly.</summary>
    [AvaloniaFact]
    public async Task Authentic_Build_landing_presents_the_real_saved_project_picker_and_closes_cleanly()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var spaces = rig.Provider.GetRequiredService<SpaceRegistry>();
            var conversations = rig.Provider.GetRequiredService<IConversationRepository>();
            var canonical = rig.Provider.GetRequiredService<TaskExecutionCoordinator>();
            var development = rig.Provider.GetRequiredService<DeveloperTaskWorkspaceService>();
            var actors = rig.Provider.GetRequiredService<IAuthenticatedResourceActorSource>();
            var taskViews = new SpaceTaskWorkspaceService(spaces, conversations, canonical);
            var workspace = new SpaceDevelopmentWorkspace(spaces, taskViews, development);
            var catalog = new SpaceDevelopmentReferenceCatalog(spaces, taskViews, workspace, actors);
            var composition = new BuildLandingComposition(spaces, catalog, new BuildTestBorrowedResources());
            var workspaces = rig.Provider.GetRequiredService<IDeveloperWorkspaceStore>();
            var factory = rig.Provider.GetRequiredService<DeveloperProjectWorkbenchPageFactory>();
            using var lifetime = new CancellationTokenSource();
            var address = AppContentAddress.Parse("9to1://dev");
            var request = new BrowseAppContentRequest(Guid.NewGuid(), address, rig.Actor.ProfileId, BrowserTabPrivacy.Standard);
            var session = new BuildBrowseAppContentSession(request, BuildBrowseAppContentAddress.Parse(address),
                workspaces, development, canonical, factory, composition, lifetime);

            await session.InitializeAsync(token);
            Assert.True(session.IsPublicationCurrent);
            var view = Assert.IsType<Border>(session.OriginalNativeView);
            Assert.IsType<DeveloperSavedProjectsPage>(view.Content);

            Assert.True(await session.TryPrepareToCloseAsync(token));
            await session.CloseAndDrainAsync();
            Assert.True(session.CanRetireOriginalView);
        });
    }
}
