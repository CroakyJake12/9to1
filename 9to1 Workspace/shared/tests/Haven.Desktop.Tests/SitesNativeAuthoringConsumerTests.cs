using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Sites;
using Haven.Desktop.Views.Shell;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Runtime;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Real CUI consumer, canonical Files persistence, renderer and Home admission.
/// The recording platform adapter observes exact issued HTML; these facts do not prove native browser rendering.</summary>
public sealed class SitesNativeAuthoringConsumerTests
{
    [AvaloniaFact]
    public async Task Registered_Sites_route_mounts_the_actual_factory_with_existing_owner_services()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var fixture = await Fixture.Create(deadline.Token);
        using var services = new ServiceCollection()
            .AddSingleton<ISiteNativeWorkspaceAuthority>(fixture)
            .AddSingleton(fixture.Resources).AddSingleton(fixture.Home).AddSingleton(fixture.Writes)
            .BuildServiceProvider();
        var mode = Assert.Single(BuiltInModeSeed.Modes, row => row.Key == "sites");
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000028"), mode.Id);
        Assert.Equal(new HavenAppRoute(HavenAppRouteKind.ModeWorkspace, HavenSurface.Sites), HavenAppRoutePolicy.Resolve(mode));
        var surface = new RecordingSurface();
        using var page = MainView.CreateSitesDocumentWorkspace(services, fixture.Review, surface);
        var window = new Window { Width = 1100, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(deadline.Token);
            Assert.Single(page.Scene.Root!.DescendantsAndSelf().OfType<NativeHost>());
            Assert.Contains(page.Scene.Root.DescendantsAndSelf().OfType<Haven.UI.Components.Button>(), row => row.Name == "Sites.Create");
            Assert.Empty(page.CurrentView.Projects); Assert.Null(surface.Document);
            Input(page, "Sites.Project.Name").Text = "Mounted website";
            Press(page, "Sites.Create"); await page.PendingOperation.WaitAsync(deadline.Token);
            var created = Assert.Single(page.CurrentView.Projects);
            Assert.Equal("Mounted website", created.Name);
            Assert.Equal(fixture.Binding!.FilesFolderId, created.Source.FilesDirectoryId);
            Assert.Equal(created.SiteId, (await fixture.Projects.GetProjectAsync(created.SiteId, deadline.Token)).Value!.SiteId);
            Assert.Equal(1, fixture.Reviews);
            Assert.Equal(HomePermissionRequestState.Succeeded, (await fixture.Permissions.GetAuthorizationAsync(fixture.Requests.Single(), deadline.Token)).State);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Actual_component_save_keeps_last_valid_preview_after_Home_navigation_and_recovers()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        await using var fixture = await Fixture.Create(token);
        var session = fixture.Session(); var project = await fixture.Seed(session, token);
        var surface = new RecordingSurface(); using var page = new NativeSitesPage(session, surface);
        var window = new Window { Width = 1100, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token);
            var valid = Assert.IsType<string>(page.DisplayedDocument);
            Assert.Equal(new SiteDocumentRenderer().Render(project, project.Pages.Single().PageId, SiteRenderContext.PagePreview).Document, valid);
            var component = project.Components.Single();
            Press(page, "Sites.Component." + component.ComponentId.ToString("N")); await page.PendingOperation.WaitAsync(token);
            Input(page, "Sites.Component.Properties").Text = "{\"href\":\"javascript:alert(1)\",\"text\":\"Invalid new edit\"}";
            fixture.BeforeReview = (_, _) => { page.Deactivate(); return Task.CompletedTask; };
            Press(page, "Sites.Component.Save"); await page.PendingOperation.WaitAsync(token);
            Assert.Null(page.DisplayedDocument); Assert.Null(surface.Document);
            var saved = (await fixture.Projects.GetProjectAsync(project.SiteId, token)).Value!;
            Assert.Equal(project.Revision + 1, saved.Revision);
            Assert.Equal("javascript:alert(1)", saved.Components.Single().Properties["href"].GetString());
            fixture.BeforeReview = null;
            await page.ActivateAsync(token);
            Assert.Equal(SitePreviewState.BuildFailed, page.CurrentView.Preview!.State);
            Assert.True(page.CurrentView.Preview.IsRetainedLastValid);
            Assert.Equal(valid, surface.Document); Assert.Equal(project.Revision, page.CurrentView.Preview.RenderedProjectRevision);
            Assert.Equal(saved.Revision, page.CurrentView.Project!.Revision);
            Assert.Contains(page.CurrentView.Preview.Diagnostics, row => row.Code == "UnsafeURL");
            Assert.Contains("Invalid new edit", Input(page, "Sites.Component.Properties").Text); // same authorised context retains its editable draft.
            Press(page, "Sites.Component." + component.ComponentId.ToString("N")); await page.PendingOperation.WaitAsync(token);
            Input(page, "Sites.Component.Properties").Text = "{\"href\":\"/recovered\",\"text\":\"Recovered edit\"}";
            Press(page, "Sites.Component.Save"); await page.PendingOperation.WaitAsync(token);
            var recovered = (await fixture.Projects.GetProjectAsync(project.SiteId, token)).Value!;
            Assert.Equal(saved.Revision + 1, recovered.Revision);
            Assert.Equal(SitePreviewState.Live, page.CurrentView.Preview!.State);
            Assert.False(page.CurrentView.Preview.IsRetainedLastValid);
            Assert.Equal(new SiteDocumentRenderer().Render(recovered, recovered.Pages.Single().PageId, SiteRenderContext.PagePreview).Document, surface.Document);
            Assert.DoesNotContain("Invalid new edit", surface.Document!);
            Assert.Equal(5, fixture.Reviews); // create, page, component, invalid save, recovery save; every edit has an actual request.
        }
        finally { fixture.BeforeReview = null; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("revoke")]
    [InlineData("source")]
    [InlineData("actor")]
    public async Task Mounted_cached_preview_and_project_names_are_cleared_when_current_authority_changes(string change)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        await using var fixture = await Fixture.Create(token);
        var session = fixture.Session(); var project = await fixture.Seed(session, token);
        var original = fixture.Binding!; var surface = new RecordingSurface(); using var page = new NativeSitesPage(session, surface);
        var window = new Window { Width = 1100, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token); Assert.NotNull(surface.Document);
            fixture.Binding = change switch
            {
                "revoke" => null,
                "source" => original with { FilesFolderId = Guid.NewGuid(), FolderRevision = "new-folder-revision", RootDirectory = fixture.ForeignRoot },
                _ => original with { ActorId = "foreign-actor", AuthenticationRevision = "foreign-revision" }
            };
            await page.RefreshAndDisplayAsync(token);
            Assert.Null(surface.Document); Assert.Null(page.DisplayedDocument); Assert.Empty(page.CurrentView.Projects);
            Assert.Empty(page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Where(row => row.Name.StartsWith("Sites.Project.", StringComparison.Ordinal)));
            fixture.Binding = original;
            Assert.Null((await session.ReadCurrentAsync(token)).Preview); // restoring access cannot resurrect a cleared cached document.
            Assert.Null(surface.Document);
            await page.RefreshAndDisplayAsync(token);
            Assert.Equal(SitePreviewState.Live, page.CurrentView.Preview!.State);
            Assert.Equal(project.SiteId, page.CurrentView.Project!.SiteId);
        }
        finally { fixture.Binding = original; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Held_actual_workspace_read_cannot_publish_after_source_switch_or_disposal(bool dispose)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        await using var fixture = await Fixture.Create(token);
        var session = fixture.Session(); await fixture.Seed(session, token);
        var surface = new RecordingSurface(); using var page = new NativeSitesPage(session, surface);
        var window = new Window { Width = 1100, Height = 800, Content = page }; Task? original = null;
        try
        {
            window.Show(); await page.ActivateAsync(token);
            fixture.HoldNext = true; original = page.RefreshAndDisplayAsync(token);
            await fixture.Entered.Task.WaitAsync(token);
            if (dispose) page.Dispose();
            else
            {
                fixture.Binding = fixture.Binding! with { FilesFolderId = Guid.NewGuid(), FolderRevision = "replacement", RootDirectory = fixture.ForeignRoot };
                await page.RefreshAndDisplayAsync(token);
            }
            fixture.Release.TrySetResult();
            await original.WaitAsync(token);
            Assert.Null(page.DisplayedDocument); Assert.Null(surface.Document); Assert.Empty(page.CurrentView.Projects);
            if (dispose) { Assert.True(surface.Disposed); Assert.Null(page.Content); Assert.Null(page.Scene.Root); }
        }
        finally
        {
            fixture.Release.TrySetResult();
            try { if (original is not null) await original; }
            finally { window.Close(); }
        }
    }

    [AvaloniaTheory]
    [InlineData("refresh")]
    [InlineData("cached-read")]
    public async Task Actual_owner_decision_held_after_its_final_source_read_cannot_publish_when_folder_changes(string phase)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        await using var fixture = await Fixture.Create(token);
        var session = fixture.Session(); await fixture.Seed(session, token);
        var surface = new RecordingSurface(); using var page = new NativeSitesPage(session, surface);
        var window = new Window { Width = 1100, Height = 800, Content = page }; Task<SiteAuthoringView>? pending = null;
        try
        {
            window.Show(); await page.ActivateAsync(token); page.Deactivate();
            await session.RefreshAsync(cancellationToken: token);
            // Refresh has candidate/read-render/read-publication/final-list owning decisions;
            // cached retrieval has current-list and exact-preview decisions. The wrapper holds
            // the final actual decision only after the maintained owner checked its real store.
            fixture.ProjectReadsUntilHold = phase == "refresh" ? 4 : 2;
            pending = phase == "refresh" ? session.RefreshAsync(cancellationToken: token) : session.ReadCurrentAsync(token);
            await fixture.AuthorizationEntered.Task.WaitAsync(token);
            fixture.Binding = fixture.Binding! with { FilesFolderId = Guid.NewGuid(), FolderRevision = "retired-during-authority-await", RootDirectory = fixture.ForeignRoot };
            fixture.AuthorizationRelease.TrySetResult();
            var observed = await pending.WaitAsync(token);
            Assert.Empty(observed.Projects); Assert.Null(observed.Project); Assert.Null(observed.Preview);
            await page.ActivateAsync(token);
            Assert.Empty(page.CurrentView.Projects); Assert.Null(page.DisplayedDocument); Assert.Null(surface.Document);
        }
        finally
        {
            fixture.AuthorizationRelease.TrySetResult();
            try { if (pending is not null) await pending; }
            finally { window.Close(); }
        }
    }

    [AvaloniaTheory]
    [InlineData("revoke")]
    [InlineData("source")]
    public async Task Component_fields_are_published_only_after_the_final_current_read_and_purged_on_retirement(string change)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        await using var fixture = await Fixture.Create(token);
        var session = fixture.Session(); var project = await fixture.Seed(session, token);
        var original = fixture.Binding!; var surface = new RecordingSurface(); using var page = new NativeSitesPage(session, surface);
        var window = new Window { Width = 1100, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token);
            var component = project.Components.Single(); var buttonName = "Sites.Component." + component.ComponentId.ToString("N");
            Press(page, buttonName); await page.PendingOperation.WaitAsync(token);
            Assert.Equal("link", Input(page, "Sites.Component.Type").Text);
            Assert.Contains("Original valid link", Input(page, "Sites.Component.Properties").Text);
            // Each actual cached read checks the project and exact cached preview. Hold the
            // second read's final owning decision after its maintained canonical store check.
            fixture.ProjectReadsUntilHold = 4;
            Press(page, buttonName);
            await fixture.AuthorizationEntered.Task.WaitAsync(token);
            fixture.Binding = change == "revoke" ? null : original with
            { FilesFolderId = Guid.NewGuid(), FolderRevision = "retired-component-context", RootDirectory = fixture.ForeignRoot };
            fixture.AuthorizationRelease.TrySetResult();
            await page.PendingOperation.WaitAsync(token);
            Assert.Empty(Input(page, "Sites.Component.Properties").Text);
            Assert.Equal("heading", Input(page, "Sites.Component.Type").Text);
            Assert.Null(surface.Document); Assert.Null(page.DisplayedDocument); Assert.Empty(page.CurrentView.Projects);
            fixture.Binding = original;
            await page.RefreshAndDisplayAsync(token);
            Assert.Equal(project.SiteId, page.CurrentView.Project!.SiteId); Assert.NotNull(surface.Document);
            Assert.Empty(Input(page, "Sites.Component.Properties").Text); // reopen requires a fresh explicit component selection.
        }
        finally { fixture.AuthorizationRelease.TrySetResult(); fixture.Binding = original; window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_real_failed_create_retains_the_authorized_preview_and_its_failure_message_after_settlement()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        await using var fixture = await Fixture.Create(token);
        var session = fixture.Session(); var project = await fixture.Seed(session, token);
        var surface = new RecordingSurface(); using var page = new NativeSitesPage(session, surface);
        var window = new Window { Width = 1100, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token); var valid = Assert.IsType<string>(surface.Document);
            Input(page, "Sites.Project.Name").Text = " "; Input(page, "Sites.Project.Path").Text = "invalid-create";
            Press(page, "Sites.Create"); await page.PendingOperation.WaitAsync(token);
            Assert.Equal(4, fixture.Reviews); // real Home authorization occurs before the owning input refusal.
            var unchanged = (await fixture.Projects.GetProjectAsync(project.SiteId, token)).Value!;
            Assert.Equal(project.Revision, unchanged.Revision); Assert.Equal(project.SiteId, Assert.Single(page.CurrentView.Projects).SiteId);
            Assert.Equal(valid, surface.Document); Assert.Equal(SitePreviewState.Live, page.CurrentView.Preview!.State);
            var status = page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Text>().Single(row => row.Name == "Sites.Status");
            Assert.Contains("The edit could not complete", status.Content);
            await page.RefreshAndDisplayAsync(token); // subsequent current-read publication must preserve the same-context operation error.
            Assert.Contains("The edit could not complete", status.Content); Assert.Equal(valid, surface.Document);
            Press(page, "Sites.Refresh"); await page.PendingOperation.WaitAsync(token);
            Assert.Equal("Preview is current.", status.Content); // the next explicit action replaces the retained operation message.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task External_canonical_move_off_the_current_page_retires_the_selected_component_draft()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        await using var fixture = await Fixture.Create(token);
        var session = fixture.Session(); var project = await fixture.Seed(session, token);
        var surface = new RecordingSurface(); using var page = new NativeSitesPage(session, surface);
        var window = new Window { Width = 1100, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token);
            var component = project.Components.Single(); var originalPage = project.Pages.Single().PageId;
            Press(page, "Sites.Component." + component.ComponentId.ToString("N")); await page.PendingOperation.WaitAsync(token);
            Assert.Contains("Original valid link", Input(page, "Sites.Component.Properties").Text);
            // A separate canonical author edits the same project through its real service;
            // the mounted consumer only refreshes and must retire its old-page selection.
            var author = new SiteAuthoringService(fixture.Projects);
            var added = await author.CreatePageAsync(project.SiteId, project.Revision, "Moved page", "/moved", token);
            Assert.True(added.IsSuccess); var next = added.Value!;
            var destination = next.Pages.Single(row => row.PageId != originalPage).PageId;
            var moved = await author.MoveComponentAsync(project.SiteId, next.Revision, component.ComponentId, destination, null, 0, token);
            Assert.True(moved.IsSuccess); Assert.Contains(moved.Value!.Components, row => row.ComponentId == component.ComponentId);
            await page.RefreshAndDisplayAsync(token, project.SiteId, originalPage);
            Assert.Equal(originalPage, page.CurrentView.PageID);
            Assert.Empty(Input(page, "Sites.Component.Properties").Text); Assert.Equal("heading", Input(page, "Sites.Component.Type").Text);
            var save = page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(row => row.Name == "Sites.Component.Save");
            Assert.False(save.GetValue(HavenProperties.Enabled));
            Assert.DoesNotContain(page.Scene.Root.DescendantsAndSelf().OfType<Haven.UI.Components.Button>(), row => row.Name == "Sites.Component." + component.ComponentId.ToString("N"));
            Assert.Equal(3, fixture.Reviews); // this read-only refresh creates no new Home write request.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_retained_original_component_gesture_cannot_keep_fields_when_its_target_is_no_longer_on_the_current_page()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        await using var fixture = await Fixture.Create(token);
        var session = fixture.Session(); var project = await fixture.Seed(session, token);
        var surface = new RecordingSurface(); using var page = new NativeSitesPage(session, surface);
        var window = new Window { Width = 1100, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token);
            var component = project.Components.Single(); var originalPage = project.Pages.Single().PageId;
            var name = "Sites.Component." + component.ComponentId.ToString("N");
            Press(page, name); await page.PendingOperation.WaitAsync(token);
            Assert.Contains("Original valid link", Input(page, "Sites.Component.Properties").Text);
            var originalButton = page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(row => row.Name == name);
            Assert.True(originalButton.KeyDown(new(HavenKey.Enter, HavenKeyModifiers.None)));
            var author = new SiteAuthoringService(fixture.Projects);
            var added = await author.CreatePageAsync(project.SiteId, project.Revision, "Destination", "/destination", token);
            Assert.True(added.IsSuccess); var next = added.Value!;
            var destination = next.Pages.Single(row => row.PageId != originalPage).PageId;
            Assert.True((await author.MoveComponentAsync(project.SiteId, next.Revision, component.ComponentId, destination, null, 0, token)).IsSuccess);
            await session.RefreshAsync(project.SiteId, originalPage, token); // owning state updates before the pending original UI gesture finishes.
            Assert.True(originalButton.KeyUp(new(HavenKey.Enter, HavenKeyModifiers.None)));
            await page.PendingOperation.WaitAsync(token);
            Assert.Empty(Input(page, "Sites.Component.Properties").Text); Assert.Equal("heading", Input(page, "Sites.Component.Type").Text);
            var save = page.Scene.Root.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(row => row.Name == "Sites.Component.Save");
            Assert.False(save.GetValue(HavenProperties.Enabled)); Assert.Equal(3, fixture.Reviews);
        }
        finally { window.Close(); }
    }

    private static Input Input(NativeSitesPage page, string name) => page.Scene.Root!.DescendantsAndSelf().OfType<Input>().Single(row => row.Name == name);
    private static void Press(NativeSitesPage page, string name)
    {
        var button = page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(row => row.Name == name);
        Assert.True(button.KeyDown(new(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.True(button.KeyUp(new(HavenKey.Enter, HavenKeyModifiers.None)));
    }
    private sealed class RecordingSurface : ISitePreviewSurface
    {
        public Control Control { get; } = new Border();
        public string? Document { get; private set; }
        public bool Disposed { get; private set; }
        public void Display(string? document) { Assert.False(Disposed); Document = document; }
        public void Dispose() { Document = null; Disposed = true; }
    }
    private sealed class Fixture : ISiteNativeWorkspaceAuthority, ICanonicalResourceAccessResolver, IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "astra-sites-mounted-" + Guid.NewGuid().ToString("N"));
        public string ForeignRoot => Path.Combine(root, "foreign");
        public SiteNativeWorkspaceBinding? Binding { get; set; }
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        public ResourceAuthorizationService Resources { get; private set; } = null!;
        public HomePermissionTrustService Permissions { get; private set; } = null!;
        public HomeResourceOperationBroker Home { get; private set; } = null!;
        public SiteNativeWriteCoordinator Writes { get; private set; } = null!;
        public SiteProjectService Projects { get; private set; } = null!;
        public int Reviews { get; private set; }
        public List<string> Requests { get; } = [];
        public Func<string, CancellationToken, Task>? BeforeReview { get; set; }
        public bool HoldNext { get; set; }
        public int ProjectReadsUntilHold { get; set; } = int.MaxValue;
        public TaskCompletionSource AuthorizationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AuthorizationRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ResourceKind => "files.item";
        public static async Task<Fixture> Create(CancellationToken token)
        {
            var fixture = new Fixture(); Directory.CreateDirectory(fixture.root); Directory.CreateDirectory(fixture.ForeignRoot);
            var store = new FileHomeCoreStateStore(Path.Combine(fixture.root, "home.json"));
            var actors = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            fixture.Actor = (await actors.GetCurrentAsync(token))!;
            fixture.Binding = new(fixture.Actor.ProfileId, fixture.Actor.ActorId, fixture.Actor.AuthenticationRevision, Guid.NewGuid(), "original-folder", fixture.root);
            fixture.Resources = new(actors, [fixture, new HeldProjectOwner(fixture)]);
            fixture.Permissions = new(store, new SiteNativeActionPolicies().TryGet);
            fixture.Home = new(fixture.Resources, fixture.Permissions); fixture.Writes = new(fixture, fixture.Home);
            fixture.Projects = new(new FileSiteWorkspaceStore(fixture.root)); return fixture;
        }
        public SiteNativeAuthoringSession Session() => new(this, Resources, Home, Writes, Review);
        public async Task Review(string requestID, AuthenticatedResourceActor originalActor, CancellationToken token)
        {
            Assert.Equal(Actor, originalActor); Reviews++; Requests.Add(requestID);
            Assert.Equal(HomePermissionRequestState.PendingApproval, (await Permissions.GetAuthorizationAsync(requestID, token)).State);
            if (BeforeReview is { } before) await before(requestID, token);
            Assert.True((await Permissions.DecideAsync(requestID, HomeApprovalChoice.Accept, token)).Succeeded);
        }
        public async Task<SiteProject> Seed(SiteNativeAuthoringSession session, CancellationToken token)
        {
            await session.CreateProjectAsync("Canonical website", "canonical", token);
            await session.CreatePageAsync("Home", "/", token);
            return (await session.AddComponentAsync("link", new Dictionary<string, JsonElement>
            { ["href"] = JsonSerializer.SerializeToElement("/valid"), ["text"] = JsonSerializer.SerializeToElement("Original valid link") }, token)).Project!;
        }
        public async Task<SiteNativeWorkspaceBinding?> GetCurrentAsync(CancellationToken token = default)
        {
            var actual = Binding;
            if (HoldNext) { HoldNext = false; Entered.TrySetResult(); await Release.Task; }
            // A retained original read intentionally returns its original result after retirement;
            // the owning canonical store/session must reject it using their subsequent actual checks.
            return actual;
        }
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionID, ResourceScope scope, CancellationToken token)
            => ValueTask.FromResult(new ResourceAccessDecision(Binding is { } binding && actor == Actor && binding.ActorId == actor.ActorId &&
                binding.AuthenticationRevision == actor.AuthenticationRevision && scope.Id == binding.FilesFolderId.ToString() && scope.Revision == binding.FolderRevision &&
                scope.Access == ResourceAccess.Write && actionID is "sites.project.create" or "sites.project.save", "explicit-fixture-files-owner", actor.ActorId, scope.Revision, null));
        private sealed class HeldProjectOwner(Fixture fixture) : ICanonicalResourceAccessResolver
        {
            private readonly SiteNativeProjectAccessResolver actual = new(fixture);
            public string ResourceKind => actual.ResourceKind;
            public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionID, ResourceScope scope, CancellationToken token)
            {
                var observed = await actual.EvaluateAsync(actor, actionID, scope, token);
                if (actionID == "sites.project.read" && --fixture.ProjectReadsUntilHold == 0)
                {
                    fixture.AuthorizationEntered.TrySetResult();
                    await fixture.AuthorizationRelease.Task;
                }
                return observed;
            }
        }
        public ValueTask DisposeAsync() { Directory.Delete(root, true); return ValueTask.CompletedTask; }
    }
}
