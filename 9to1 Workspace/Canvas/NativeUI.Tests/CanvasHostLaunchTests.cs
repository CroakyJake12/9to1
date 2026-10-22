using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HavenOS.Apps.Canvas.NativeUI.Tests;

/// <summary>Actual CUI host/catalogue controls. These checks intentionally do not
/// require or claim drawing-engine, installed-app or target-ARM acceptance.</summary>
[Collection("Canvas native UI")]
public sealed class CanvasHostLaunchTests
{
    [Fact]
    public async Task Empty_start_has_real_create_search_and_no_active_editor_or_sample_cards()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var window = await fixture.ShowAsync();
            Assert.True(Find(window, "canvas-launch").IsEffectivelyVisible);
            Assert.False(Find(window, "canvas-active-workspace").IsEffectivelyVisible);
            Assert.Empty(Cards(window));
            Assert.True(Find(window, "canvas-create").IsEffectivelyVisible);
            Assert.True(Find(window, "canvas-search-query").IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CanvasNativeViewport>(), control => control.IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), control =>
                control.IsEffectivelyVisible && control.Content is string text && text == "Finish approved request");
            Assert.Empty((await fixture.Workspace.Provider.ListAsync(fixture.Folder, new("", Limit: 100), null, default)).Items);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Search_uses_persisted_Files_items_and_failed_entry_preserves_the_start_and_revision()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            // Deterministic canonical catalogue fixtures deliberately lack a
            // donor engine payload: opening must fail honestly, never invent one.
            var maths = await fixture.SeedCatalogueArtifactAsync("Maths revision");
            await fixture.SeedCatalogueArtifactAsync("Physics diagrams");
            var before = await fixture.Bridge.OpenAsync(maths);
            var window = await fixture.ShowAsync();
            Assert.Equal(2, Cards(window).Length);
            var search = Assert.IsType<TextBox>(Find(window, "canvas-search-query"));
            search.Text = "Maths";
            Click(Assert.IsType<Button>(Find(window, "canvas-search")));
            await UntilAsync(() => Cards(window).Length == 1 &&
                AutomationProperties.GetName(Cards(window)[0]).Contains("Maths", StringComparison.Ordinal));
            Click(Assert.Single(Cards(window)));
            await UntilAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                text.Text?.Contains("no Rnote engine state", StringComparison.Ordinal) == true));
            Assert.True(Find(window, "canvas-launch").IsEffectivelyVisible);
            Assert.False(Find(window, "canvas-active-workspace").IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CanvasNativeViewport>(), control => control.IsEffectivelyVisible);
            var after = await fixture.Bridge.OpenAsync(maths);
            Assert.Equal(before.CasRevisionId, after.CasRevisionId);
            Assert.Equal(before.Artifact.ArtifactId, after.Artifact.ArtifactId);
            Assert.Equal(CanvasArtifactCodec.Serialize(before.Artifact), CanvasArtifactCodec.Serialize(after.Artifact));
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Invalid_search_keeps_the_previous_real_catalogue_without_mutating_artifacts()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var id = await fixture.SeedCatalogueArtifactAsync("Revision map");
            var before = await fixture.Bridge.OpenAsync(id);
            var window = await fixture.ShowAsync();
            Assert.Single(Cards(window));
            Assert.IsType<TextBox>(Find(window, "canvas-search-query")).Text = new string('x', 257);
            Click(Assert.IsType<Button>(Find(window, "canvas-search")));
            await UntilAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                text.Text?.Contains("at most 256 characters", StringComparison.Ordinal) == true));
            Assert.Single(Cards(window));
            Assert.True(Find(window, "canvas-launch").IsEffectivelyVisible);
            Assert.Equal(before.CasRevisionId, (await fixture.Bridge.OpenAsync(id)).CasRevisionId);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Actual_authored_scene_changes_view_only_on_reported_publication_and_keeps_pending_or_failed_entry_at_start()
    {
        // This exercises the real embedded scene and native controls through a scripted typed
        // result port. It verifies CUI transitions, not Rnote availability or app acceptance.
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            using var stream = typeof(CanvasHostWindow).Assembly.GetManifestResourceStream("HavenOS.Apps.Canvas.UI.CanvasHost.cui")!;
            using var reader = new StreamReader(stream);
            var model = new CuiViewModel();
            foreach (var key in new[] { "CanResume", "CanReturnStart", "CanOpen", "CanWrite", "CanSearch", "CanNavigate", "HasRetainedCanvas", "HasNoDocuments" })
                model.Set(key, true);
            foreach (var key in new[] { "HasNoRetainedCanvas", "HasPendingRequest", "HasMoreDocuments", "CanPage", "CanFinish", "CanSetup", "CanReviewWorkspace" })
                model.Set(key, false);
            model.Set("Canvases", Array.Empty<object>());
            model.Set("OpenedName", "Revision canvas");
            model.Set("NewName", "Untitled canvas");
            model.Set("SearchQuery", "");
            model.Set("Status", "");
            var outcomes = new SceneOutcomes();
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            loader.SetActionDispatcher(outcomes);
            loader.SetSurface("Canvas");
            var (root, diagnostics) = loader.LoadMarkup(await reader.ReadToEndAsync(), "CanvasHost.cui");
            Assert.Empty(diagnostics);
            Assert.NotNull(root);
            var window = new Window { Content = root, Width = 1200, Height = 800 };
            loader.WireBindings(root!);
            window.Show();
            try
            {
                foreach (var outcome in new[]
                {
                    new CuiCommandOutcome(false, false, "PendingHomeApproval", null, null),
                    new CuiCommandOutcome(false, false, "NoCanvasSessionPublished", null, null)
                })
                {
                    outcomes.Next = outcome;
                    Click(Assert.IsAssignableFrom<Button>(Find(root!, "canvas-resume")));
                    await loader.WhenActionsIdleAsync();
                    Assert.True(Find(root!, "canvas-launch").IsVisible);
                    Assert.False(Find(root!, "canvas-active-workspace").IsVisible);
                }
                outcomes.Next = new(true, false, "CanvasSessionPublished", null, null);
                Click(Assert.IsAssignableFrom<Button>(Find(root!, "canvas-resume")));
                await loader.WhenActionsIdleAsync();
                Assert.False(Find(root!, "canvas-launch").IsVisible);
                Assert.True(Find(root!, "canvas-active-workspace").IsVisible);
                outcomes.Next = new(true, false, "Completed", null, null);
                Click(Assert.IsAssignableFrom<Button>(Find(root!, "canvas-return-start")));
                await loader.WhenActionsIdleAsync();
                Assert.True(Find(root!, "canvas-launch").IsVisible);
                Assert.False(Find(root!, "canvas-active-workspace").IsVisible);
                Assert.Equal(new[] { "canvas.host.resume", "canvas.host.resume", "canvas.host.resume", "canvas.host.start" }, outcomes.Commands);
            }
            finally
            {
                window.Close();
                loader.Dispose();
                await loader.WhenActionsIdleAsync();
            }
            return true;
        }, CancellationToken.None);
    }

    private sealed class SceneOutcomes : ICuiResultReportingDispatcher
    {
        internal CuiCommandOutcome? Next { get; set; }
        internal List<string> Commands { get; } = [];
        public ValueTask<CuiCommandOutcome> DispatchWithOutcomeAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            var result = Next ?? throw new InvalidOperationException("No typed test outcome was supplied.");
            Next = null;
            return ValueTask.FromResult(result);
        }
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        { _ = await DispatchWithOutcomeAsync(command, parameter, cancellationToken); }
    }

    private static Control Find(Control root, string authoredId) => Assert.Single(AuthoredTree(root),
        control => CuiRuntimeIdentity.GetStableId(control) == "id:" + authoredId);
    private static IEnumerable<Control> AuthoredTree(Control root)
    {
        yield return root;
        var children = root is CuiContainerControl container ? container.AuthoredChildren : root.GetVisualChildren().OfType<Control>();
        foreach (var child in children)
            foreach (var descendant in AuthoredTree(child)) yield return descendant;
    }
    private static Button[] Cards(Control root) => Find(root, "canvas-launch-cards")
        .GetVisualDescendants().OfType<Button>().ToArray();
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task UntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (predicate()) return;
            await Task.Delay(10);
        }
        Assert.True(predicate());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-launch-" + Guid.NewGuid().ToString("N"));
        private ServiceProvider _services = null!;
        private CanvasHostWindow? _window;
        internal NativeFilesWorkspace Workspace = null!;
        internal CanvasFilesArtifactBridge Bridge = null!;
        internal HostedItemId Folder => Workspace.Configuration.AppFolders["canvas"];
        internal static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture._root);
            try
            {
                var state = new FileHomeCoreStateStore(Path.Combine(fixture._root, "home.json"));
                var profiles = new HomeLocalProfileIdentity(state, new OperatingSystemPrincipalSource());
                var files = new NativeFilesWorkspaceService(state, profiles);
                var permissions = new HomePermissionTrustService(state, new CanvasNativeActionPolicies().TryGet);
                var ownership = new HomeLocalStoreOwnership(state, profiles, new HomeLocalStoreEvidenceRegistry([files]), permissions);
                var authority = new NativeFilesWorkspaceAuthority(files, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
                var selected = Path.Combine(fixture._root, "selected-empty-workspace"); Directory.CreateDirectory(selected);
                await files.ConfigureNewAsync(selected, ownership, default);
                fixture.Workspace = (await authority.GetCurrentAsync(default))!;
                var resources = new ResourceAuthorizationService(profiles, [new FilesArtifactResourceResolver(async (actor, token) =>
                { var current = await authority.GetCurrentAsync(token); return current?.Actor == actor ? current.Provider : null; },
                async (actor, app, token) =>
                { var current = await authority.GetCurrentAsync(token); return current?.Actor == actor && current.Configuration.AppFolders.TryGetValue(app, out var folder) ? folder : null; })]);
                var broker = new HomeResourceOperationBroker(resources, permissions);
                var runtime = new HomeCoreRuntime([new HomeCoreStateService(state), new HomePermissionsCoreService(permissions, profiles)]);
                var registrations = new ServiceCollection();
                registrations.AddSingleton(runtime); registrations.AddSingleton(profiles);
                registrations.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
                registrations.AddSingleton(files); registrations.AddSingleton(authority); registrations.AddSingleton(resources);
                registrations.AddSingleton(broker); registrations.AddSingleton(permissions); registrations.AddSingleton(ownership);
                fixture._services = registrations.BuildServiceProvider();
                fixture.Bridge = new(profiles, actor => actor == fixture.Workspace.Actor ? fixture.Workspace.Provider : null,
                    fixture.Workspace.Directories, resources, () => true,
                    (actor, provider, token) => authority.CaptureCommitAuthorityAsync(actor, provider, () => true, token));
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        internal async Task<HostedItemId> SeedCatalogueArtifactAsync(string name)
            => (await Bridge.CreateAsync(CanvasArtifact.Create(name))).FileId;
        internal async Task<CanvasHostWindow> ShowAsync()
        {
            _window = new CanvasHostWindow(_services);
            _window.Show(); await _window.Initialization;
            _window.UpdateLayout();
            return _window;
        }
        public async ValueTask DisposeAsync()
        {
            if (_window is not null)
            {
                await _window.CloseAndDrainAsync(); _window.Close();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            if (_services is not null) await _services.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
