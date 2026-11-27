using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Prefabs;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;
using NativeButton = Avalonia.Controls.Button;

namespace Haven.Desktop.Tests;

/// <summary>Compiled product HUI, actual CUI composer and Avalonia popup component evidence.
/// Headless fallback captures do not establish installed native PopupRoot qualification.</summary>
public sealed class ChatAddMenuAtlasTests
{
    // Capture once before pumping callbacks from other retained UI owners.
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;

    [AvaloniaTheory]
    [InlineData("Glow", 400, 600)]
    [InlineData("Glow", 720, 600)]
    [InlineData("Glow", 1440, 900)]
    [InlineData("Professional", 400, 600)]
    [InlineData("Professional", 720, 600)]
    [InlineData("Professional", 1440, 900)]
    public Task Atlas_attach_categories_remain_bounded_searchable_and_scrollable(string theme, int width, int height)
        => AssertAtlasAttachCategoriesAsync(theme, width, height, CuiAppearance.Bright);

    [AvaloniaTheory]
    [InlineData("Glow", 400, 600)]
    [InlineData("Glow", 720, 600)]
    [InlineData("Glow", 1440, 900)]
    [InlineData("Professional", 400, 600)]
    [InlineData("Professional", 720, 600)]
    [InlineData("Professional", 1440, 900)]
    public Task Atlas_dark_attach_categories_render_and_retain_the_actual_selection_and_draft(string theme, int width, int height)
        => AssertAtlasAttachCategoriesAsync(theme, width, height, CuiAppearance.Dark);

    private async Task AssertAtlasAttachCategoriesAsync(string theme, int width, int height, CuiAppearance appearance)
    {
        await using var rig = await Rig.CreateAsync(width, height, theme, _token, appearance);
        var captureSuffix = appearance == CuiAppearance.Bright ? string.Empty : "-" + appearance;
        try
        {
            var scene = rig.Scene;
            var catalogue = Catalogue.Create();
            scene.SetCatalogue(catalogue.Agents, catalogue.Capabilities, catalogue.Skills, catalogue.Apps);
            var selected = new List<AddMenuSelection>();
            scene.CatalogItemSelected += (_, selection) => selected.Add(selection);
            var message = rig.Message;
            message.Text = "Keep my current draft.";
            message.CaretIndex = message.Text.Length;
            rig.Add.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            await rig.SettleAsync();

            var categories = scene.AddMenuPrefab.GetComponent<Container>("AttachCategories");
            var headers = CategoryHeaders(scene.AddMenuPrefab);
            Assert.Equal(["Files", "Skills", "Plugins & Apps", "Specialists", "Assistants"], headers.Select(item => item.Content));
            Assert.All(headers, item => Assert.False(item.State.HasFlag(HavenElementState.Expanded)));
            Assert.Equal(HavenVisibility.Visible, categories.GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Collapsed, scene.AddMenuPrefab.GetComponent<Container>("MainMenu").GetValue(HavenProperties.Visibility));
            Assert.Equal("Attach to Chat", scene.AddMenuPrefab.GetComponent<HavenText>("MenuTitle").Content);
            Assert.Same(rig.Add, scene.AddMenuPopup!.PlacementTarget);
            Assert.Same(scene.ComposerView, scene.AddMenuPopup.Parent);
            Assert.InRange(rig.PopupHost.Bounds.Width, 300, Math.Min(820, width - 24) + .5);
            Assert.InRange(rig.PopupHost.Bounds.Height, 1, Math.Min(470, height - (width <= 600 ? 190 : 200)) + .5);
            AssertInViewport(rig.PopupHost, scene.AddMenuPrefab.GetComponent<HavenButton>("Dismiss"));
            Assert.Empty(selected);
            await rig.CaptureAsync($"attach-collapsed-{theme}-{width}x{height}{captureSuffix}");

            rig.Click(scene.AddMenuPrefab.GetComponent<HavenButton>("Capabilities"));
            await rig.SettleAsync();
            Assert.Single(headers, item => item.State.HasFlag(HavenElementState.Expanded));
            Assert.Same(scene.AddMenuPrefab.GetComponent<Container>("AttachAppsSection"), scene.CatalogPanel.Parent);
            Assert.Equal(HavenVisibility.Visible, scene.CatalogSearch.GetValue(HavenProperties.Visibility));
            Assert.Equal("Search Plugins & Apps", scene.CatalogSearch.Placeholder);
            Assert.Contains(Results(scene), item => item.Content == "Document reader");
            Assert.Contains(Results(scene), item => item.Content == "Boards");
            Assert.Contains(scene.CatalogRows.DescendantsAndSelf().OfType<HavenText>(), item => item.Content == "Unsupported");
            Assert.DoesNotContain(Results(scene), item => item.Content == "Disabled capability");
            Assert.Empty(selected); // Opening/searching categories has no attachment or invocation side effect.
            Assert.Equal("Keep my current draft.", message.Text);
            var foreground = HavenAvaloniaThemeResolver.EffectColor(HavenAvaloniaThemeResolver.Resolve("TextPrimary", rig.PopupHost));
            var surface = HavenAvaloniaThemeResolver.EffectColor(HavenAvaloniaThemeResolver.Resolve("SurfaceRaised", rig.PopupHost));
            Assert.True(Math.Abs(Luma(foreground) - Luma(surface)) > .3, $"{theme} menu text {foreground} must remain distinct from its surface {surface}.");
            if (appearance == CuiAppearance.Dark)
            {
                Assert.Equal(CuiAppearance.Dark, CuiThemeScopeApplier.DetectAppearance());
                Assert.True(Luma(surface) < .3 && Luma(foreground) > .6,
                    $"The actual {theme} popup must use its dark surface and light foreground, observed {surface} and {foreground}.");
            }
            await rig.CaptureAsync($"attach-expanded-{theme}-{width}x{height}{captureSuffix}");

            rig.PopupHost.FocusElement(scene.CatalogSearch);
            rig.PopupHost.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "Boards" });
            await rig.SettleAsync();
            Assert.Equal("Boards", scene.CatalogSearch.Text);
            Assert.Equal("Boards", Assert.Single(Results(scene)).Content);
            Assert.Empty(selected);
            rig.Click(Assert.Single(Results(scene)));
            await rig.SettleAsync();
            var selection = Assert.Single(selected);
            Assert.Equal(AddMenu.AddMenuAction.App, selection.Kind);
            Assert.Same(catalogue.Apps[0], selection.Item);
            Assert.False(scene.AddMenuPopup.IsOpen);
            Assert.True(message.IsFocused);
            Assert.Equal("Keep my current draft.", message.Text);

            // A long REAL catalogue uses the one outer category scroller; the header/Close stay fixed.
            scene.SetCatalogue([], Enumerable.Range(0, 24).Select(index => catalogue.Capabilities[0] with
            { Id = Guid.NewGuid(), Name = $"Available plugin {index:00}" }).ToArray(), [], []);
            scene.ShowAddMenu();
            await rig.SettleAsync();
            rig.Click(scene.AddMenuPrefab.GetComponent<HavenButton>("Capabilities"));
            await rig.SettleAsync();
            Assert.True(categories.MaxScrollY > 0);
            Assert.Equal(HavenOverflow.Visible, scene.CatalogRows.GetValue(HavenProperties.Overflow));
            AssertInViewport(rig.PopupHost, scene.AddMenuPrefab.GetComponent<HavenButton>("Dismiss"));
            categories.ScrollY = categories.MaxScrollY;
            await rig.SettleAsync();
            AssertInViewport(rig.PopupHost, scene.AddMenuPrefab.GetComponent<HavenButton>("Assistants"));
            rig.Click(scene.AddMenuPrefab.GetComponent<HavenButton>("Dismiss"));
            await rig.SettleAsync();
            Assert.False(scene.AddMenuPopup.IsOpen);
            Assert.Equal("Keep my current draft.", message.Text);
        }
        catch (Exception error) { rig.PrimaryFailure = error; throw; }
    }

    [AvaloniaTheory]
    [InlineData("Agents", AddMenu.AddMenuAction.Agent)]
    [InlineData("Instructions", AddMenu.AddMenuAction.Instruction)]
    [InlineData("Capabilities", AddMenu.AddMenuAction.Capability)]
    public async Task Category_selection_preserves_canonical_identity_and_never_selects_on_browse(string header, AddMenu.AddMenuAction action)
    {
        await using var rig = await Rig.CreateAsync(720, 650, "Professional", _token);
        try
        {
            var catalogue = Catalogue.Create();
            rig.Scene.SetCatalogue(catalogue.Agents, catalogue.Capabilities, catalogue.Skills, []);
            var selected = new List<AddMenuSelection>();
            rig.Scene.CatalogItemSelected += (_, selection) => selected.Add(selection);
            rig.Scene.ShowAddMenu();
            await rig.SettleAsync();
            rig.Click(rig.Scene.AddMenuPrefab.GetComponent<HavenButton>(header));
            await rig.SettleAsync();
            Assert.Empty(selected);
            var expected = action switch
            {
                AddMenu.AddMenuAction.Agent => (object)catalogue.Agents[0],
                AddMenu.AddMenuAction.Instruction => catalogue.Skills[0],
                _ => catalogue.Capabilities[0]
            };
            var expectedName = expected switch
            {
                AgentDefinition agent => agent.Name,
                PromptDefinition skill => skill.Name,
                CapabilityDefinition capability => capability.Name,
                _ => throw new InvalidOperationException()
            };
            var row = Assert.Single(Results(rig.Scene), item => item.Content == expectedName);
            Assert.Contains("permissions are unchanged", row.Accessibility.Description);
            rig.Click(row);
            Assert.Same(expected, Assert.Single(selected).Item);
            Assert.Equal(action, selected[0].Kind);
            Assert.False(rig.Scene.AddMenuPopup!.IsOpen);
        }
        catch (Exception error) { rig.PrimaryFailure = error; throw; }
    }

    [AvaloniaFact]
    public async Task Files_expands_before_the_existing_chooser_action_and_category_keyboard_keeps_search_caret()
    {
        await using var rig = await Rig.CreateAsync(400, 600, "Glow", _token);
        try
        {
            var actions = new List<AddMenu.AddMenuAction>();
            rig.Scene.AddActionSelected += (_, action) => actions.Add(action);
            rig.Scene.ShowAddMenu();
            await rig.SettleAsync();
            var headers = CategoryHeaders(rig.Scene.AddMenuPrefab);
            Assert.True(headers[0].State.HasFlag(HavenElementState.Focused));
            rig.Key(Key.Down);
            Assert.True(headers[1].State.HasFlag(HavenElementState.Focused));
            rig.Key(Key.End);
            Assert.True(headers[^1].State.HasFlag(HavenElementState.Focused));
            rig.Key(Key.Home);
            Assert.True(headers[0].State.HasFlag(HavenElementState.Focused));
            rig.Key(Key.Enter, release: true);
            await rig.SettleAsync();
            Assert.True(headers[0].State.HasFlag(HavenElementState.Expanded));
            Assert.Empty(actions);
            Assert.Equal("Choose files…", Assert.Single(Results(rig.Scene)).Content);
            rig.PopupHost.FocusElement(rig.Scene.CatalogSearch);
            rig.PopupHost.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "device" });
            rig.Key(Key.Down);
            Assert.True(rig.Scene.CatalogSearch.State.HasFlag(HavenElementState.Focused));
            Assert.Equal("device", rig.Scene.CatalogSearch.Text);
            await rig.SettleAsync();
            rig.Click(Assert.Single(Results(rig.Scene)));
            Assert.Equal([AddMenu.AddMenuAction.File], actions);
            Assert.False(rig.Scene.AddMenuPopup!.IsOpen);
            Assert.Null(rig.Scene.AddMenuPopup.Parent);
        }
        catch (Exception error) { rig.PrimaryFailure = error; throw; }
    }

    [AvaloniaFact]
    public async Task Category_switch_and_live_catalogue_refresh_share_one_search_owner_and_keep_typed_duplicate_names()
    {
        await using var rig = await Rig.CreateAsync(720, 650, "Glow", _token);
        try
        {
            var catalogue = Catalogue.Create();
            var duplicate = catalogue.Capabilities[0] with { Id = Guid.NewGuid() };
            rig.Scene.SetCatalogue(catalogue.Agents, [catalogue.Capabilities[0], duplicate], catalogue.Skills, []);
            var selected = new List<AddMenuSelection>();
            rig.Scene.CatalogItemSelected += (_, selection) => selected.Add(selection);
            rig.Scene.ShowAddMenu();
            await rig.SettleAsync();
            var originalSearch = rig.Scene.CatalogSearch;
            rig.Click(rig.Scene.AddMenuPrefab.GetComponent<HavenButton>("Instructions"));
            await rig.SettleAsync();
            originalSearch.Text = "no such skill";
            await rig.SettleAsync();
            Assert.Empty(Results(rig.Scene));
            Assert.Contains(rig.Scene.CatalogRows.Children.OfType<HavenText>(), item => item.Content == "No matching items.");
            rig.Click(rig.Scene.AddMenuPrefab.GetComponent<HavenButton>("Capabilities"));
            await rig.SettleAsync();
            Assert.Same(originalSearch, rig.Scene.AddMenuPrefab.GetComponent<Input>("CatalogSearch"));
            Assert.Equal(string.Empty, originalSearch.Text);
            Assert.Equal(2, Results(rig.Scene).Count);
            originalSearch.Text = "Document";
            rig.Scene.SetCatalogue([], [duplicate], [], []);
            await rig.SettleAsync();
            Assert.Equal("Document", originalSearch.Text);
            var remaining = Assert.Single(Results(rig.Scene));
            rig.Click(remaining);
            Assert.Same(duplicate, Assert.Single(selected).Item);
            Assert.NotEqual(catalogue.Capabilities[0].Id, ((CapabilityDefinition)selected[0].Item).Id);
        }
        catch (Exception error) { rig.PrimaryFailure = error; throw; }
    }

    [AvaloniaFact]
    public void Go_retains_its_response_management_actions_and_drilldown_search()
    {
        var prefab = HavenPrefabCatalog.FromAssembly(typeof(ChatAddMenuPrefab).Assembly)
            .Create("ChatAddMenu", "attach-go-regression-" + Guid.NewGuid().ToString("N"));
        using var surface = new ChatAddMenuSurface(prefab);
        var host = new HavenSceneControl { Root = prefab };
        var window = new Window { Width = 500, Height = 650, Content = host };
        try
        {
            window.Show(); surface.Show(); window.UpdateLayout();
            Assert.Equal("Manage Responses", surface.MenuTitle.Content);
            Assert.Equal(HavenVisibility.Visible, surface.MainMenu.GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Visible, surface.OptionsPanel.GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Collapsed, prefab.GetComponent<Container>("AttachCategories").GetValue(HavenProperties.Visibility));
            var files = new List<AddMenu.AddMenuAction>();
            surface.AddActionSelected += (_, action) => files.Add(action);
            Click(host, surface.AttachFilesButton);
            Assert.Equal([AddMenu.AddMenuAction.File], files);
            surface.Show(); window.UpdateLayout();
            Click(host, surface.AgentsButton); window.UpdateLayout();
            Assert.Equal(HavenVisibility.Collapsed, surface.MainMenu.GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Visible, surface.CatalogSearch.GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Visible, surface.CatalogBackButton.GetValue(HavenProperties.Visibility));
        }
        finally { window.Content = null; window.Close(); host.Root = null; }
    }

    private static HavenButton[] CategoryHeaders(Prefab prefab) =>
        new[] { "AttachFiles", "Instructions", "Capabilities", "Agents", "Assistants" }
            .Select(prefab.GetComponent<HavenButton>).ToArray();
    private static List<HavenButton> Results(ChatHavenScene scene) => scene.CatalogRows.DescendantsAndSelf().OfType<HavenButton>().ToList();
    private static double Luma(Avalonia.Media.Color color) => (.2126 * color.R + .7152 * color.G + .0722 * color.B) / 255d;
    private static void AssertInViewport(Control host, HavenElement element)
    {
        Assert.True(element.Bounds.Width > 0 && element.Bounds.Height > 0);
        Assert.InRange(element.Bounds.X, -.5, host.Bounds.Width);
        Assert.InRange(element.Bounds.Y, -.5, host.Bounds.Height);
        Assert.True(element.Bounds.X + element.Bounds.Width <= host.Bounds.Width + .5);
        Assert.True(element.Bounds.Y + element.Bounds.Height <= host.Bounds.Height + .5);
    }

    private static void Click(HavenSceneControl host, HavenElement element)
    {
        AssertInViewport(host, element);
        var router = new HavenInputRouter(host.Root!);
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        Assert.Same(element, router.HitTest(point));
        var window = Assert.IsType<Window>(TopLevel.GetTopLevel(host));
        var nativePoint = host.TranslatePoint(new Point(point.X, point.Y), window);
        Assert.NotNull(nativePoint);
        window.MouseMove(nativePoint.Value);
        window.MouseDown(nativePoint.Value, MouseButton.Left);
        window.MouseUp(nativePoint.Value, MouseButton.Left);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly CuiTheme _previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        private readonly CuiAppearance _previousAppearance = CuiThemeScopeApplier.DetectAppearance();
        private readonly CancellationToken _token;
        private Task? _close;
        public Exception? PrimaryFailure { get; set; }
        public ChatHavenScene Scene { get; }
        public Window Window { get; }
        public TextBox Message => Window.GetVisualDescendants().OfType<TextBox>().Single(item => AutomationProperties.GetName(item) == "Message");
        public NativeButton Add => Scene.ComposerView.GetVisualDescendants().OfType<NativeButton>().Single(item => AutomationProperties.GetName(item) == "Add files or actions");
        public HavenSceneControl PopupHost => Assert.IsType<HavenSceneControl>(Scene.AddMenuPopupHost);

        private Rig(int width, int height, string theme, CancellationToken token, CuiAppearance appearance)
        {
            _token = token;
            CuiThemeScopeApplier.ApplyGlobalTheme(Enum.Parse<CuiTheme>(theme), appearanceOverride: appearance);
            var resolver = new ChatGenUiNativeControlResolver();
            Scene = new ChatHavenScene(resolver);
            var host = new HavenSceneControl(new HavenDesktopImageResolver(), resolver) { Root = Scene.Root };
            Window = new Window { Width = width, Height = height, Content = host };
            Window.Bind(Avalonia.Controls.Window.BackgroundProperty, Window.GetResourceObservable("CuiBackgroundBrush"));
        }

        public static async Task<Rig> CreateAsync(int width, int height, string theme, CancellationToken token,
            CuiAppearance appearance = CuiAppearance.Bright)
        {
            var rig = new Rig(width, height, theme, token, appearance);
            try
            {
                rig.Window.Show();
                await rig.Scene.ComposerView.InitializeAsync(token);
                await rig.SettleAsync();
                rig.Message.Focus();
                return rig;
            }
            catch (Exception error)
            {
                rig.PrimaryFailure = error;
                await rig.DisposeAsync();
                throw;
            }
        }

        public async Task SettleAsync()
        {
            for (var i = 0; i < 4; i++)
            {
                Window.UpdateLayout(); Scene.AddMenuPopupHost?.UpdateLayout();
                Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(1, _token);
            }
        }

        public void Click(HavenElement element) => ChatAddMenuAtlasTests.Click(PopupHost, element);
        public void Key(Key key, bool release = false)
        {
            PopupHost.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            if (release) PopupHost.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
        }

        public async Task CaptureAsync(string name)
        {
            var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) return;
            await SettleAsync();
            using var frame = Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, name + ".png"));
        }

        public ValueTask DisposeAsync() => new(_close ??= CloseAsync());

        private async Task CloseAsync()
        {
            var failures = new List<Exception>();
            try { Scene.Dispose(); }
            catch (Exception error) { failures.Add(error); }
            // Keep the mounted UI and resolver alive until all three real child close/release
            // owners settle. Synchronous Scene.Dispose only admits these originals.
            Task Admit(Func<Task> original)
            {
                try { return original(); }
                catch (Exception error) { return Task.FromException(error); }
            }
            var all = Task.WhenAll(Admit(Scene.JoinHeaderCloseAndReleaseAsync),
                Admit(Scene.JoinComposerCloseAndReleaseAsync), Admit(Scene.JoinRecoveryCloseAndReleaseAsync));
            try { await all; }
            catch (Exception error)
            {
                if (all.Exception is { } aggregate) failures.AddRange(aggregate.InnerExceptions);
                else failures.Add(error);
            }
            try { Window.Content = null; Window.Close(); }
            catch (Exception error) { failures.Add(error); }
            try { CuiThemeScopeApplier.ApplyGlobalTheme(_previousTheme, appearanceOverride: _previousAppearance); }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count == 0) return;
            if (PrimaryFailure is not null) failures.Insert(0, PrimaryFailure);
            throw new AggregateException("The Attach regression and/or its original child cleanup failed.", failures);
        }
    }

    private sealed record Catalogue(AgentDefinition[] Agents, CapabilityDefinition[] Capabilities, PromptDefinition[] Skills, ModeDefinition[] Apps)
    {
        public static Catalogue Create()
        {
            var now = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
            var capability = new CapabilityDefinition(Guid.NewGuid(), "documents.read", "Document reader", "Read supplied documents", "files", "file", "Use attachments", "fixture", "[]",
                CapabilityPlatform.All, CapabilityRiskClass.ReadOnly, CapabilityAvailability.Available, "[]", "fixture-provider", true, true, true, true, now);
            return new(
                [new(Guid.NewGuid(), "Research Specialist", "Research supplied context", "Use the provided sources", "agents", "local-model", null, "[]", "{}", true, true, now)],
                [capability, capability with { Id = Guid.NewGuid(), Key = "unsupported.fixture", Name = "Unavailable integration", Availability = CapabilityAvailability.Unsupported },
                    capability with { Id = Guid.NewGuid(), Key = "disabled.fixture", Name = "Disabled capability", IsEnabled = false }],
                [new(Guid.NewGuid(), "Study skill", "Explain provided material", "skills", "Use examples", false, true, true, now)],
                [new(Guid.NewGuid(), "boards", "Boards", "Organise supplied work", "apps", HavenMode.Chat, "[]", "[]", "[]", "[]", "", ModeSource.BuiltIn, ModeInstallState.BuiltIn, "9to1", "1", "[]", now, now)]);
        }
    }
}
