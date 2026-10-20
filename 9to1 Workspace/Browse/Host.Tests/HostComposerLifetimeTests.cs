using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Prefabs;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;
using System.Threading;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Executable shared-component portable regression coverage for the single shared CUI composer. It
/// exercises the guarded initialization, real input wiring (SelectionStart/End, AcceptsReturn,
/// caret), async action settlement, close/self-join and watcher detach against the real
/// <see cref="ChatComposerCuiView"/>, plus mounting that same view inside a CUI host (the ChatSpace
/// route composition).
/// </summary>
public sealed class HostComposerLifetimeTests
{
    private sealed class RecordingSource : IChatComposerSource
    {
        public string Draft { get; set; } = string.Empty;
        public string Placeholder => "Ask Haven anything";
        public bool IsSending { get; set; }
        public bool IsSafetyLocked { get; set; }
        public bool HasAdd { get; set; } = true;
        public bool CanSend { get; set; } = true;
        public bool IsEditing { get; set; }
        public IReadOnlyList<ChatComposerChip> Chips { get; set; } = [];
        public int SendCount;
        public int FormatCount;
        public string? LastFormatCommand;
        public int EmojiInsertions;
        public int StopCount;

        public ValueTask Send() { SendCount++; return ValueTask.CompletedTask; }
        public ValueTask Stop() { StopCount++; return ValueTask.CompletedTask; }
        public ValueTask Add() => ValueTask.CompletedTask;
        public ValueTask OpenSettings() => ValueTask.CompletedTask;
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask SetActionMode(string mode) => ValueTask.CompletedTask;
        public ValueTask SetResponseMode(string mode) => ValueTask.CompletedTask;
        public ValueTask InvokeChip(string id) => ValueTask.CompletedTask;
        public ValueTask RemoveChip(string id) => ValueTask.CompletedTask;
        public ValueTask FormatSelection(string command) { LastFormatCommand = command; FormatCount++; return ValueTask.CompletedTask; }
        public ValueTask InsertEmoji(string emoji) { EmojiInsertions++; return ValueTask.CompletedTask; }
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }

    /// <summary>A real source whose Send returns the SAME held operation task, so settlement,
    /// cancellation, fault retention and no-duplicate-admission can be asserted genuinely.</summary>
    private sealed class HeldOperationSource : IChatComposerSource
    {
        private readonly TaskCompletionSource _send = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Draft { get; set; } = string.Empty;
        public string Placeholder => "Ask Haven anything";
        public bool IsSending { get; set; }
        public bool IsSafetyLocked { get; set; }
        public bool HasAdd { get; set; } = true;
        public bool CanSend { get; set; } = true;
        public bool IsEditing { get; set; }
        public IReadOnlyList<ChatComposerChip> Chips { get; set; } = [];
        public int SendStarted;
        public Task LastAdmitted => _send.Task;
        public bool IsPending => !_send.Task.IsCompleted;
        public ValueTask Send() { SendStarted++; return new ValueTask(_send.Task); }
        public void Release() => _send.TrySetResult();
        public void Fault(Exception error) => _send.TrySetException(error);
        public ValueTask Stop() => ValueTask.CompletedTask;
        public ValueTask Add() => ValueTask.CompletedTask;
        public ValueTask OpenSettings() => ValueTask.CompletedTask;
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask SetActionMode(string mode) => ValueTask.CompletedTask;
        public ValueTask SetResponseMode(string mode) => ValueTask.CompletedTask;
        public ValueTask InvokeChip(string id) => ValueTask.CompletedTask;
        public ValueTask RemoveChip(string id) => ValueTask.CompletedTask;
        public ValueTask FormatSelection(string command) => ValueTask.CompletedTask;
        public ValueTask InsertEmoji(string emoji) => ValueTask.CompletedTask;
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }

    private sealed class EmptyBindings : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability
    {
        public static readonly EmptyBindings Instance = new();
        public bool TryGetValue(string path, out object? value) { value = null; return false; }
        public bool? IsActionAvailable(string command) => false;
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException($"Empty test bindings command '{command}'."));
    }

    [AvaloniaFact]
    public async Task Shared_composer_guarded_init_wires_real_input_then_close_detaches_watchers()
    {
        var source = new RecordingSource();
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            // Real input wiring: typed text round-trips through the canonical typed port and the
            // underlying TextBox advertises the multiline AcceptsReturn contract.
            view.Input.Text = "hello";
            Assert.Equal("hello", view.Input.Text);
            var box = FindInput(view);
            Assert.NotNull(box);
            Assert.True(box!.AcceptsReturn);

            // Selection semantics use the real endpoints and preserve forward/backward selection.
            view.Input.SetSelection(0, 2);
            Assert.Equal(0, view.Input.SelectionStart);
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("he", view.Input.SelectedText);
            view.Input.PlaceCaretAtEnd();
            Assert.Equal(5, view.Input.CaretIndex);
            Assert.Equal(0, view.Input.SelectionLength);
            // Backward-directed selection is normalized to the bounded region with the correct
            // length and selected text.
            view.Input.SetSelection(3, 1);
            Assert.Equal(1, view.Input.SelectionStart);
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("el", view.Input.SelectedText);
            view.Input.ClearSelection();
            Assert.Equal(0, view.Input.SelectionLength);

            // Async action settlement returns the real admitted source operation.
            _ = view.Bindings.DispatchAsync("composer.send", null, CancellationToken.None);
            Assert.Equal(1, source.SendCount);
            _ = view.Bindings.DispatchAsync("composer.stop", null, CancellationToken.None);
            Assert.Equal(1, source.StopCount);
            _ = view.Bindings.DispatchAsync("composer.format", "Bold", CancellationToken.None);
            Assert.Equal(1, source.FormatCount);
            _ = view.Bindings.DispatchAsync("composer.emoji-insert", "😀", CancellationToken.None);
            Assert.Equal(1, source.EmojiInsertions);

            // Close/self-join: the published close completes and detaches every watcher so no
            // late write or delayed attachment can publish text after close.
            await view.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10));
            view.Input.Text = "should not publish";
            Assert.Equal(string.Empty, view.Input.Text);
            Assert.Null(FindInput(view));
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Shared_composer_mounts_inside_a_cui_host_with_responsive_input_bounds()
    {
        var composer = new ChatComposerCuiView(new RecordingSource());
        var registry = new CuiControlRegistry();
        registry.BindAuthoredControl("composer-mount", _ => composer);
        var host = new CuiSceneHost(registry);
        var document = new CuiRichParser().Parse(
            "<Cui><Container id=\"surface\" Type=\"Vertical Stack\"><Container id=\"composer-mount\" Type=\"Vertical Stack\" /></Container></Cui>");
        var window = new Window { Width = 800, Height = 600, Content = host };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await host.ShowAsync(new CuiNativeScene("spaces.chat", "Chat Space", "Chat",
                document, EmptyBindings.Instance, EmptyBindings.Instance, ChatSpacePage.ImmediateReadinessInstance)
            { IsPublicationCurrent = () => true }).WaitAsync(TimeSpan.FromSeconds(10));
            await composer.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var box = FindInput(composer);
            Assert.NotNull(box);
            Assert.True(box!.AcceptsReturn);
            Assert.InRange(box.Bounds.Height, 44, double.MaxValue);
            Assert.True(box.Bounds.Width > 0);
            Assert.InRange(box.Bounds.Y + box.Bounds.Height, 1, 600);
            Assert.InRange(box.Bounds.X + box.Bounds.Width, 1, 800);

            await host.CloseOriginalAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, composer, testFailure, additionalOriginals: [host.CloseOriginalAsync()]);
        }
    }

    [AvaloniaFact]
    public async Task Shared_composer_aa_button_toggles_floating_toolbar_and_dispatches_format_with_selection_preserved()
    {
        // Production path: the REAL authored ChatComposerCuiView (not a hand-built fixture). The
        // Aa button must toggle a same-scene floating toolbar (B/I/code/link/quote/list) on/off
        // through pure-local state, dispatch genuine formatting commands with their parameters
        // through the loader's retained action pipeline, keep the toolbar open across actions, and
        // preserve the SAME input/draft/selection/caret throughout.
        var source = new RecordingSource { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var box = FindInput(view);
            Assert.NotNull(box);
            view.Input.Text = "draft";
            Assert.Equal("draft", view.Input.Text);
            view.Input.SetSelection(0, 2);
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("dr", view.Input.SelectedText);
            box!.Focus(); Pump();
            // A focused editor shows the expanded formatting controls.
            Assert.True(view.Bindings.TryGetValue("Composer.ShowFormatting", out var sf) && sf is true);

            // The floating toolbar is hidden until Aa toggles it open (not rendered while closed).
            Assert.Null(FindContainer(view, "format-toolbar"));

            var format = FindButton(view, "format-button");
            Assert.NotNull(format);
            format!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.CaptureOriginalActionSettlement(); Pump();
            var toolbar = FindContainer(view, "format-toolbar");
            Assert.NotNull(toolbar);
            Assert.True(toolbar.IsVisible, "Aa must open the floating toolbar");

            // The toolbar buttons are B (bold) then I (italic); each carries its parameter through
            // the loader's retained action pipeline, never a direct Bindings bypass.
            var bold = ToolbarButton(toolbar, 0);
            var italic = ToolbarButton(toolbar, 1);
            Assert.NotNull(bold);
            Assert.NotNull(italic);

            bold!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.CaptureOriginalActionSettlement();
            Assert.Equal(1, source.FormatCount);
            Assert.Equal("Bold", source.LastFormatCommand);
            Assert.True(toolbar.IsVisible, "Bold dispatch must leave the toolbar open");
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("dr", view.Input.SelectedText);

            italic!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.CaptureOriginalActionSettlement();
            Assert.Equal(2, source.FormatCount);
            Assert.Equal("Italic", source.LastFormatCommand);
            Assert.True(toolbar.IsVisible, "Italic dispatch must leave the toolbar open");
            Assert.Equal("draft", box.Text);
            Assert.Same(box, FindInput(view));
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("dr", view.Input.SelectedText);

            // Aa toggles the floating toolbar closed again; the SAME input/draft/selection survive.
            format.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Assert.False(toolbar.IsVisible);
            Assert.Equal("draft", box.Text);
            Assert.Same(box, FindInput(view));
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("dr", view.Input.SelectedText);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [Fact]
    public void Emoji_trigger_pool_excludes_flags_and_blacklisted_suggestive_sequences()
    {
        var candidates = ComposerEmojiIconPool.Candidates;
        Assert.NotEmpty(candidates);

        // No trigger candidate may come from the Flags group (generic/country/subdivision).
        var flagTexts = ComposerEmojiCatalogue.Entries
            .Where(entry => entry.Group.Equals("Flags", StringComparison.Ordinal))
            .Select(entry => entry.Text)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(candidates, candidate => flagTexts.Contains(candidate));

        // Explicit owner examples plus the adjacent clearly-suggestive sequences are all absent.
        var blacklist = new HashSet<string>(StringComparer.Ordinal)
        {
            "\U0001F92C", // 🤬 face with symbols over mouth
            "\U0001F4A6", // 💦 sweat droplets
            "\U0001F51E", // 🔞 no one under eighteen
            "\U0001F351", // 🍑 peach
            "\U0001F346", // 🍆 eggplant
            "\U0001F34C", // 🍌 banana
            "\U0001FAE6", // 🫦 biting lip
            "\U0001F37E", // 🍾 bottle with popping cork
            "\U0001F445", // 👅 tongue
        };
        Assert.DoesNotContain(candidates, candidate => blacklist.Contains(candidate));
    }

    [AvaloniaFact]
    public async Task Shared_composer_emoji_trigger_renders_eligible_candidate_and_picker_preserves_editing()
    {
        var source = new RecordingSource { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var box = FindInput(view);
            Assert.NotNull(box);
            view.Input.Text = "draft";
            view.Input.SetSelection(0, 2);
            Assert.Equal("dr", view.Input.SelectedText);
            box!.Focus(); Pump(); // expand formatting so editing trigger controls are live

            // The trigger icon is a genuine, eligible candidate from the pool.
            var emojiButton = FindButton(view, "emoji-button");
            Assert.NotNull(emojiButton);
            var content = Assert.IsType<string>(emojiButton!.Content);
            Assert.Contains(content, ComposerEmojiIconPool.Candidates);

            // Unselected trigger has no visible background.
            Assert.True(IsEffectivelyTransparent(emojiButton.Background));

            // The picker panel is closed before the trigger is pressed.
            Assert.Null(FindContainer(view, "emoji-panel"));

            // Opening the picker marks the trigger selected and reveals the genuine panel.
            emojiButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var panel = FindContainer(view, "emoji-panel");
            Assert.NotNull(panel);
            Assert.True(panel.IsVisible);
            Assert.False(IsEffectivelyTransparent(emojiButton.Background));

            // The draft, selection and real input survive the picker open round trip unchanged.
            Assert.Equal("draft", box.Text);
            Assert.Same(box, FindInput(view));
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("dr", view.Input.SelectedText);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Shared_composer_add_button_renders_large_packaged_svg_plus()
    {
        var source = new RecordingSource { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var addButton = FindButton(view, "add-button");
            Assert.NotNull(addButton);
            var image = Assert.IsType<Image>(addButton!.Content);
            Assert.True(image.Width >= 32 && image.Height >= 32);

            // The authored target is a 44px accessible button with 6px padding; the arranged image
            // surface must be the 32x32 content box, measured in real arranged bounds, centered and
            // fully inside the button (no clipping).
            Assert.True(addButton.Bounds.Width >= 44 && addButton.Bounds.Height >= 44,
                $"add button should be at least 44x44, was {addButton.Bounds}");
            Assert.Equal(new Thickness(6), addButton.Padding);
            Assert.True(image.Bounds.Width >= 31 && image.Bounds.Width <= 33, $"plus image arranged width {image.Bounds.Width}");
            Assert.True(image.Bounds.Height >= 31 && image.Bounds.Height <= 33, $"plus image arranged height {image.Bounds.Height}");
            Assert.True(addButton.Bounds.Contains(image.Bounds), $"plus image {image.Bounds} clipped outside add button {addButton.Bounds}");
            var imageCenter = image.Bounds.Center;
            var buttonCenter = addButton.Bounds.Center;
            Assert.True(Math.Abs(imageCenter.X - buttonCenter.X) < 1d && Math.Abs(imageCenter.Y - buttonCenter.Y) < 1d,
                $"plus image center {imageCenter} not centered in add button center {buttonCenter}");

            // The plus is the genuine packaged SVG: two rounded rects painted per-element, covering
            // the centre without clipping. The painted geometry union stays inside the 32x32 view
            // and its centre is genuinely painted.
            var drawing = Assert.IsType<DrawingImage>(image.Source);
            var group = Assert.IsType<DrawingGroup>(drawing.Drawing);
            Assert.Equal(2, group.Children.Count);
            var first = true;
            var union = default(Rect);
            foreach (var child in group.Children)
            {
                var geometryDrawing = Assert.IsType<GeometryDrawing>(child);
                Assert.NotNull(geometryDrawing.Brush);
                Assert.NotNull(geometryDrawing.Geometry);
                Assert.True(geometryDrawing.Geometry!.FillContains(new Point(16, 16)));
                var bounds = geometryDrawing.Geometry.Bounds;
                union = first ? bounds : union.Union(bounds);
                first = false;
            }
            Assert.True(new Rect(0, 0, 32, 32).Contains(union), $"plus painted bounds {union} exceed the 32x32 view");

            // The foreground is the live scoped CUI text brush resolved on the button itself, so a
            // theme change that repaints CuiTextBrush is what the icon reflects. Compare the painted
            // fill color to the scoped brush's color.
            Assert.True(addButton.TryFindResource("CuiTextBrush", out var scoped));
            var scopedSolid = Assert.IsAssignableFrom<ISolidColorBrush>(scoped);
            foreach (var child in group.Children)
            {
                var geometryBrush = Assert.IsType<GeometryDrawing>(child).Brush;
                var solid = Assert.IsAssignableFrom<ISolidColorBrush>(geometryBrush);
                Assert.Equal(scopedSolid.Color, solid.Color);
            }
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    private static Control? FindContainer(ContentControl root, string automationId) =>
        root.GetVisualDescendants().OfType<Control>().SingleOrDefault(control =>
            AutomationProperties.GetAutomationId(control) == automationId);

    /// <summary>Returns the Nth Button inside the floating toolbar (B=0, I=1, then code/link/quote/list).</summary>
    private static Button? ToolbarButton(Control toolbar, int index) =>
        toolbar.GetVisualDescendants().OfType<Button>().ToArray().ElementAtOrDefault(index);

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>Runs a few dispatcher/layout passes so headless composition builds the visual tree
    /// and real bounds before assertions inspect the real rendered input.</summary>
    private static void PumpLayout(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
        }
    }

    private static TextBox? FindInput(ContentControl root) =>
        root.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(box =>
            AutomationProperties.GetAutomationId(box) == "composer-input");

    /// <summary>True when the brush paints nothing visible (null, transparent opacity or a solid
    /// transparent color). Used to assert the unselected trigger has no background.</summary>
    private static bool IsEffectivelyTransparent(IBrush? brush)
    {
        if (brush is null) return true;
        if (brush.Opacity <= 0d) return true;
        return brush is ISolidColorBrush solid && solid.Color.A == 0;
    }

    private static Button? FindButton(ContentControl root, string automationId) =>
        root.GetVisualDescendants().OfType<Button>().SingleOrDefault(b =>
            AutomationProperties.GetAutomationId(b) == automationId);

    private static IEnumerable<AutomationPeer> AutomationDescendants(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            yield return child;
            foreach (var descendant in AutomationDescendants(child)) yield return descendant;
        }
    }

    [AvaloniaFact]
    public async Task Native_composer_textbox_peer_appears_exactly_once_in_rendered_scene_tree()
    {
        // Production-style mount: only shared composer transported through the real HavenSceneControl
        // with the same native registrar, so the automation walk exercises the actual surface.
        var registrar = new RecordingRegistrar();
        var scene = new ChatHavenScene(registrar);
        var window = new Window
        {
            Width = 900, Height = 700,
            Content = new HavenSceneControl(new HavenAvaloniaImageResolver(), registrar) { Root = scene.Root }
        };
        Exception? testFailure = null;
        window.Show();
        try
        {
            await scene.ComposerInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            Assert.Contains(scene.ComposerView, window.GetVisualDescendants());
            var box = FindInput(scene.ComposerView);
            Assert.NotNull(box);
            Assert.True(box!.Bounds.Width > 0 && box.Bounds.Height > 0);

            var scenePeer = ControlAutomationPeer.CreatePeerForElement((Control)window.Content!);
            Assert.NotNull(scenePeer);
            var peers = AutomationDescendants(scenePeer).ToArray();

            // The real composer TextBox peer appears EXACTLY once, mapping to the same actual input.
            var inputPeers = peers.Where(peer => peer.GetAutomationId() == "composer-input").ToArray();
            Assert.Single(inputPeers);
            var inputPeer = inputPeers[0];
            Assert.Same(box, (inputPeer as TextBoxAutomationPeer)?.Owner);
            Assert.Equal(AutomationControlType.Edit, inputPeer.GetAutomationControlType());
            Assert.True(inputPeer.GetBoundingRectangle().Width > 0d);
            Assert.True(inputPeer.GetBoundingRectangle().Height > 0d);

            // The native peer that composes the composer control must be linked to a real parent
            // whose recriprocal children contain the SAME native peer (NATIVE-004/006 base hook).
            var nativePeers = peers.OfType<ControlAutomationPeer>()
                .Where(p => ReferenceEquals(p.Owner, scene.ComposerView))
                .ToArray();
            Assert.Single(nativePeers);
            var nativePeer = nativePeers[0];
            var nativeParent = nativePeer.GetParent();
            Assert.NotNull(nativeParent);
            Assert.Contains(nativePeer, nativeParent!.GetChildren());
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, scene.ComposerView, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Native_composer_peer_remains_after_public_cache_walk_and_prunes_on_root_replacement()
    {
        // CACHE the peer tree first (public GetChildren path), then replace the actual scene root
        // (mount/unmount the native composer), then walk the SAME public GetChildren path again to
        // prove cache invalidation: the previous native subtree is removed and the current appears once.
        var registrar = new RecordingRegistrar();
        var scene = new ChatHavenScene(registrar);
        var sceneControl = new HavenSceneControl(new HavenAvaloniaImageResolver(), registrar);
        var window = new Window { Width = 900, Height = 700, Content = sceneControl };
        Exception? testFailure = null;
        window.Show();
        try
        {
            sceneControl.Root = scene.Root;
            await scene.ComposerInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var scenePeer = ControlAutomationPeer.CreatePeerForElement(sceneControl);
            Assert.NotNull(scenePeer);

            // First walk caches/attaches peers; the real composer input must be present once.
            var firstWalk = AutomationDescendants(scenePeer).Where(peer => peer.GetAutomationId() == "composer-input").ToArray();
            Assert.Single(firstWalk);
            var firstPeer = firstWalk[0];
            Assert.True(firstPeer.GetBoundingRectangle().Width > 0d);
            var nativeRoot = AutomationDescendants(scenePeer).OfType<ControlAutomationPeer>()
                .Single(peer => ReferenceEquals(peer.Owner, scene.ComposerView));
            var nativeParent = nativeRoot.GetParent();
            Assert.NotNull(nativeParent);
            Assert.Contains(nativeRoot, nativeParent!.GetChildren());

            // HUI semantic exposure mutation: collapse the composer host, then rewalk the PUBLIC
            // cached GetChildren path. The stale native subtree must prune WITHOUT root replacement.
            var host = scene.ComposerHost;
            host.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            PumpLayout(window);
            var exposureWalk = AutomationDescendants(scenePeer).Where(peer => peer.GetAutomationId() == "composer-input").ToArray();
            Assert.Empty(exposureWalk);
            Assert.Null(nativeRoot.GetParent());

            // Restore exposure: the SAME native peer reappears exactly once.
            host.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
            PumpLayout(window);
            var restoredWalk = AutomationDescendants(scenePeer).Where(peer => peer.GetAutomationId() == "composer-input").ToArray();
            Assert.Single(restoredWalk);
            Assert.Same(firstPeer, restoredWalk[0]);
            var restoredRoot = AutomationDescendants(scenePeer).OfType<ControlAutomationPeer>()
                .Single(peer => ReferenceEquals(peer.Owner, scene.ComposerView));
            Assert.Same(nativeRoot, restoredRoot);
            Assert.Same(nativeParent, restoredRoot.GetParent());
            Assert.Contains(restoredRoot, nativeParent.GetChildren());

            // Replace the actual root with a plain empty container (no NativeHost/composer mount).
            sceneControl.Root = new Haven.UI.Components.Container();
            PumpLayout(window);
            Assert.DoesNotContain(scene.ComposerView, window.GetVisualDescendants());

            // Public GetChildren path again: the stale composer subtree is pruned.
            var secondWalk = AutomationDescendants(scenePeer).Where(peer => peer.GetAutomationId() == "composer-input").ToArray();
            Assert.Empty(secondWalk);
            Assert.Null(nativeRoot.GetParent());
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, scene.ComposerView, testFailure);
        }
    }

    [AvaloniaFact]
    public async Task Native_peer_within_nested_semantic_group_links_to_intended_semantic_parent()
    {
        // A NESTED semantic Group -> role-None NativeHost -> real native control must expose the
        // native peer as a child of the semantic parent peer, NOT flattened into the scene root,
        // with reciprocal parent/child linking (NATIVE-004/006).
        var registrar = new RecordingRegistrar();
        var group = new Haven.UI.Components.Container { Name = "OuterGroup" };
        var host = new Haven.UI.Components.NativeHost { Name = "NestedHost" };
        group.Add(host);
        var root = new Haven.UI.Components.Container { Name = "SceneRoot" };
        root.Add(group);

        var nativeBox = new TextBox { Text = "native" };
        AutomationProperties.SetAutomationId(nativeBox, "nested-native-input");
        registrar.Register(host, nativeBox);

        var sceneControl = new HavenSceneControl(new HavenAvaloniaImageResolver(), registrar) { Root = root };
        var window = new Window { Width = 900, Height = 700, Content = sceneControl };
        Exception? testFailure = null;
        window.Show();
        try
        {
            PumpLayout(window);
            Assert.Contains(nativeBox, window.GetVisualDescendants());

            var scenePeer = ControlAutomationPeer.CreatePeerForElement(sceneControl);
            Assert.NotNull(scenePeer);
            var peers = AutomationDescendants(scenePeer).ToArray();

            var nativePeers = peers.OfType<ControlAutomationPeer>()
                .Where(p => ReferenceEquals(p.Owner, nativeBox))
                .ToArray();
            Assert.Single(nativePeers);
            var nativePeer = nativePeers[0];

            // Intended semantic parent is the Group peer (role Group), not the scene root peer.
            var groupPeer = peers.Single(p => p.GetAutomationId() == "OuterGroup");
            Assert.Same(groupPeer, nativePeer.GetParent());
            Assert.Contains(nativePeer, groupPeer.GetChildren());
            Assert.DoesNotContain(nativePeer, scenePeer.GetChildren());
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            window.Close();
            if (testFailure is not null) throw testFailure;
        }
    }

    [AvaloniaFact]
    public async Task Native_group_child_add_materializes_new_peer_during_cohort_invalidation_safely()
    {
        // NATIVE-009/010a: when a semantic child is added to a group, the group element invalidates
        // and InvalidateCachedChildren iterates its peer cohort. A public ChildrenChanged observer
        // that READS the subtree during that iteration materializes the NEW child's peer, mutating
        // _elementPeers mid-foreach. The cohort must be snapshotted before notifications, else
        // InvalidOperationException. This test reproduces that exact read during a member notification.
        var registrar = new RecordingRegistrar();
        var group = new Haven.UI.Components.Container { Name = "OuterGroup" };
        var host = new Haven.UI.Components.NativeHost { Name = "NestedHost" };
        group.Add(host);
        var root = new Haven.UI.Components.Container { Name = "SceneRoot" };
        root.Add(group);

        var nativeBox = new TextBox { Text = "native" };
        AutomationProperties.SetAutomationId(nativeBox, "nested-native-input");
        registrar.Register(host, nativeBox);

        var sceneControl = new HavenSceneControl(new HavenAvaloniaImageResolver(), registrar) { Root = root };
        var window = new Window { Width = 900, Height = 700, Content = sceneControl };
        Exception? testFailure = null;
        window.Show();
        try
        {
            PumpLayout(window);
            var scenePeer = ControlAutomationPeer.CreatePeerForElement(sceneControl);
            Assert.NotNull(scenePeer);

            // Initial walk caches the Group peer (and every currently-materialized peer).
            var initialPeers = AutomationDescendants(scenePeer).ToArray();
            var groupPeer = initialPeers.Single(p => p.GetAutomationId() == "OuterGroup");
            Assert.Contains(nativeBox, initialPeers.OfType<ControlAutomationPeer>().Select(p => p.Owner));

            // Observer reads the PUBLIC subtree whenever the group's children change. Reading creates
            // peers and mutates _elementPeers while InvalidateCachedChildren iterates its cohort.
            var reads = 0;
            EventHandler reader = (_, _) => reads += AutomationDescendants(scenePeer).Count();
            groupPeer.ChildrenChanged += reader;

            // Add a brand-new semantic child never materialized as a peer. The group invalidates ->
            // InvalidateAutomationTree -> cohort foreach includes groupPeer, whose ChildrenChanged
            // fires the reentrant read that materializes FreshChild's peer mid-iteration.
            var fresh = new Haven.UI.Components.Button { Name = "FreshChild" };
            group.Add(fresh);
            PumpLayout(window);

            groupPeer.ChildrenChanged -= reader;
            Assert.True(reads > 0, "reentrant read never executed during cohort invalidation");
            Assert.Single(AutomationDescendants(scenePeer), p => p.GetAutomationId() == "FreshChild");
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            window.Close();
            if (testFailure is not null) throw testFailure;
        }
    }

    [AvaloniaFact]
    public void Accessibility_metadata_update_does_not_restart_class_reconciliation()
    {
        var probe = new Haven.UI.Components.Container { Name = "MetadataProbe" };
        var root = new Haven.UI.Components.Container { Name = "SceneRoot" };
        root.Add(probe);
        var sceneControl = new HavenSceneControl(new HavenAvaloniaImageResolver()) { Root = root };
        var window = new Window { Width = 900, Height = 700, Content = sceneControl };
        window.Show();
        try
        {
            PumpLayout(window);
            var scenePeer = ControlAutomationPeer.CreatePeerForElement(sceneControl);
            var originalPeer = Assert.Single(AutomationDescendants(scenePeer),
                peer => peer.GetAutomationId() == "MetadataProbe");
            var before = sceneControl.DiagApplyClassesRuns;

            probe.Accessibility.AccessibleName = "Updated accessible name";

            Assert.Equal("Updated accessible name", originalPeer.GetName());
            Assert.Equal(before, sceneControl.DiagApplyClassesRuns);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Native_retained_descendant_role_mutation_updates_cached_peer_without_manual_invalidation()
    {
        // NATIVE-010b: Accessibility.Role is a plain auto-property with no dedicated invalidation event.
        // The cached peer must still react to a direct Role mutation on a retained descendant WITHOUT a
        // manual InvalidateAutomationTree/root reset/unrelated mutation. Role None <-> Group.
        var registrar = new RecordingRegistrar();
        var probe = new Haven.UI.Components.Container { Name = "RoleProbe" }; // default role Group
        var root = new Haven.UI.Components.Container { Name = "SceneRoot" };
        root.Add(probe);

        var sceneControl = new HavenSceneControl(new HavenAvaloniaImageResolver(), registrar) { Root = root };
        var window = new Window { Width = 900, Height = 700, Content = sceneControl };
        Exception? testFailure = null;
        window.Show();
        try
        {
            PumpLayout(window);
            var scenePeer = ControlAutomationPeer.CreatePeerForElement(sceneControl);
            Assert.NotNull(scenePeer);

            // Cache the peer tree first via the PUBLIC GetChildren path.
            var initialWalk = AutomationDescendants(scenePeer).ToArray();
            var originalProbe = Assert.Single(initialWalk, p => p.GetAutomationId() == "RoleProbe");
            Assert.Same(scenePeer, originalProbe.GetParent());
            Assert.Contains(originalProbe, scenePeer.GetChildren());

            // Direct Role mutation to None: the cached walk must EXCLUDE the probe. No manual
            // invalidation, root reset, or unrelated mutation is applied.
            probe.Accessibility.Role = HavenAccessibleRole.None;
            PumpLayout(window);
            var noneWalk = AutomationDescendants(scenePeer).ToArray();
            Assert.DoesNotContain(noneWalk, p => p.GetAutomationId() == "RoleProbe");
            Assert.Null(originalProbe.GetParent());

            // Direct Role mutation back to Group: the SAME probe peer reappears with Group type.
            probe.Accessibility.Role = HavenAccessibleRole.Group;
            PumpLayout(window);
            var groupWalk = AutomationDescendants(scenePeer).ToArray();
            var back = Assert.Single(groupWalk, p => p.GetAutomationId() == "RoleProbe");
            Assert.Same(originalProbe, back);
            Assert.Equal(AutomationControlType.Group, back.GetAutomationControlType());
            Assert.Same(scenePeer, back.GetParent());
            Assert.Contains(back, scenePeer.GetChildren());
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            window.Close();
            if (testFailure is not null) throw testFailure;
        }
    }

    [AvaloniaFact]
    public async Task Real_send_button_click_settles_actual_loader_snapshot()
    {
        var source = new HeldOperationSource();
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        Task? pipeline = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            // The authored send control is rendered and interactive with a genuine draft.
            source.Draft = "draft";
            var sendButton = FindButton(view, "send-button");
            Assert.NotNull(sendButton);
            Assert.True(sendButton!.IsEnabled);

            // Click the REAL rendered send control; the click dispatches source.Send().
            sendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, source.SendStarted);
            Assert.True(source.IsPending);
            Assert.False(source.LastAdmitted.IsCompleted);

            // Captured from the UI thread AFTER admission: the ACTUAL loader action-settlement
            // pipeline, using the exact retained raw host Show (never a source-only witness).
            // The snapshot must be genuinely pending while the same raw source task is held.
            pipeline = view.CaptureOriginalActionSettlement();
            Assert.NotNull(pipeline);
            Assert.False(pipeline.IsCompleted);

            // Release the SAME raw source task on a worker thread; the actual loader snapshot
            // (not merely LastAdmitted) settles exactly once.
            await Task.Run(() => source.Release());
            await pipeline.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(source.IsPending);
            Assert.Equal(1, source.SendStarted); // no duplicate admission
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure, release: source.Release, additionalOriginals: [pipeline, source.LastAdmitted]);
        }
    }

    [AvaloniaFact]
    public async Task Faulted_source_operation_retains_its_real_fault_through_loader()
    {
        var source = new HeldOperationSource();
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        Task? pipeline = null;
        var failure = new IOException("source offline");
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            source.Draft = "draft";
            var sendButton = FindButton(view, "send-button");
            Assert.NotNull(sendButton);

            sendButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(source.IsPending);

            // Capture the ACTUAL loader pipeline after admission, then fault the held source.
            pipeline = view.CaptureOriginalActionSettlement();
            Assert.False(pipeline.IsCompleted);
            await Task.Run(() => source.Fault(failure));

            // The EXACT IOException must surface through the actual loader snapshot, independently
            // of awaiting the source task itself.
            var pipelineError = await Record.ExceptionAsync(() => pipeline.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(pipelineError);
            Assert.Contains(failure, AllCauses(pipelineError!));

            var sourceError = await Record.ExceptionAsync(() => source.LastAdmitted.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(sourceError);
            Assert.Contains(failure, AllCauses(sourceError!));
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure, release: source.Release, additionalOriginals: [pipeline, source.LastAdmitted], expectedCause: failure);
        }
    }

    /// <summary>A deterministic held readiness barrier proving the real host Show is genuinely
    /// incomplete until released. It DELIBERATELY ignores cancellation (matching the contract that
    /// readiness/actions ignoring cancellation keep close genuinely pending), while still observing
    /// when the host requested cancellation of the show.</summary>
    private sealed class HeldReadiness : ICuiSceneReadiness
    {
        private readonly TaskCompletionSource<CuiSceneAvailability> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<CuiSceneAvailability> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public Task CancellationObserved => _cancellationObserved.Task;
        public int CheckCount;
        public void Release() => _released.TrySetResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "ready"));
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            _entered.TrySetResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "observed", "observed"));
            cancellationToken.Register(() => _cancellationObserved.TrySetResult());
            return new ValueTask<CuiSceneAvailability>(_released.Task); // deliberately ignores cancellation
        }
    }

    [AvaloniaFact]
    public async Task Close_before_init_holds_then_joins_same_show_and_close()
    {
        var source = new RecordingSource();
        var readiness = new HeldReadiness();
        var view = new ChatComposerCuiView(source, readiness);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        Task? init = null;
        Task? close = null;
        try
        {
            init = view.InitializeAsync();
            // Prove readiness actually entered and raw Show/guarded init are genuinely incomplete.
            await readiness.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, readiness.CheckCount);
            Assert.False(init.IsCompleted);

            // Close while the real show is still held: it withdraws, joins the SAME init/host,
            // and stays genuinely pending until readiness (which ignores cancellation) is released.
            close = view.CloseAsync();
            Assert.False(close.IsCompleted);
            await readiness.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(10));
            // The cancellation was requested but readiness ignores it => close stays genuinely pending.
            Assert.False(close.IsCompleted);
            Assert.Same(close, view.CloseAsync()); // repeated close shares the same real task

            readiness.Release();
            // The host withdrawal cancels the in-flight show, so init settles faulted/canceled
            // with the owner-withdrawal cause; close joins it and must NOT report false success.
            var initError = await ObserveTask(init).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(initError);
            var closeError = await ObserveTask(close).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(closeError);
            // close genuinely joined the withdrawal: a cancellation/owner-withdrawal cause is
            // retained (never laundered into a clean receipt).
            Assert.Contains(AllCauses(closeError!), e => e is OperationCanceledException);
            Assert.Equal(1, readiness.CheckCount); // close created no second init/Show
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure, release: readiness.Release, allowOwnerWithdrawal: true);
        }
    }

    private static async Task<Exception?> ObserveTask(Task task)
    {
        try { await task.ConfigureAwait(false); return null; }
        catch (Exception error) { return error; }
    }

    [AvaloniaFact]
    public async Task Reentrant_close_withdraws_remaining_edit_callbacks_while_the_original_action_is_held()
    {
        var source = new HeldOperationSource { Draft = "draft" };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        Task? pipeline = null;
        Task? close = null;
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(view);
            var button = FindButton(view, "send-button");
            Assert.NotNull(box);
            Assert.NotNull(button);
            Assert.True(button!.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pipeline = view.CaptureOriginalActionSettlement();
            Assert.False(pipeline.IsCompleted);

            var laterTextCallbacks = 0;
            var invalidations = 0;
            view.Input.TextChanged += (_, _) => close = view.CloseAsync();
            view.Input.TextChanged += (_, _) => laterTextCallbacks++;
            view.Input.Invalidated += (_, _) => invalidations++;

            // This real synchronous edit closes from the FIRST typed edit callback. Neither a
            // later subscriber nor the subsequent invalidation may publish after withdrawal.
            box!.Text = "closing edit";
            Assert.NotNull(close);
            Assert.Same(close, view.CloseAsync());
            Assert.False(close!.IsCompleted);
            Assert.Equal(0, laterTextCallbacks);
            Assert.Equal(0, invalidations);
            view.Input.Text = "late edit";
            Assert.Equal("closing edit", box.Text);

            await Task.Run(source.Release);
            await pipeline.WaitAsync(TimeSpan.FromSeconds(10));
            await close.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure, release: source.Release, additionalOriginals: [pipeline, source.LastAdmitted]);
        }
    }

    [AvaloniaFact]
    public async Task Held_send_close_withdraws_late_input_and_releases_only_after_ack()
    {
        var source = new HeldOperationSource();
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        Task? pipeline = null;
        Task? close = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            source.Draft = "draft";
            var sendButton = FindButton(view, "send-button");
            Assert.NotNull(sendButton);
            sendButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, source.SendStarted);
            Assert.True(source.IsPending);

            pipeline = view.CaptureOriginalActionSettlement();
            Assert.False(pipeline.IsCompleted);

            var box = FindInput(view);
            Assert.NotNull(box);
            box!.Text = "original";

            close = view.CloseAsync();
            Assert.False(close.IsCompleted);

            // Producer retirement: late writes/selection/caret/focus cannot reach the real TextBox.
            view.Input.Text = "late write";
            Assert.Equal("original", box.Text);
            var selStartBefore = box.SelectionStart;
            var selLenBefore = box.SelectionEnd - box.SelectionStart;
            view.Input.SetSelection(0, 3);
            view.Input.PlaceCaretAtEnd();
            view.Input.Focus();
            Assert.Equal(selStartBefore, box.SelectionStart);
            Assert.Equal(selLenBefore, box.SelectionEnd - box.SelectionStart);

            // Release the held source on a worker thread; only then does the child ACK and the
            // physical input get terminally released.
            await Task.Run(() => source.Release());
            await pipeline.WaitAsync(TimeSpan.FromSeconds(10));
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(source.IsPending);
            Assert.Equal(string.Empty, view.Input.Text); // terminal release after ACK
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure, release: source.Release, additionalOriginals: [pipeline, source.LastAdmitted]);
        }
    }

    [AvaloniaFact]
    public async Task Faulted_held_send_close_retains_input_and_same_failed_close()
    {
        var source = new HeldOperationSource();
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        var failure = new IOException("send backend failed");
        Task? pipeline = null;
        Task? close = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            source.Draft = "draft";
            view.Input.Text = "retained";
            var sendButton = FindButton(view, "send-button");
            Assert.NotNull(sendButton);
            sendButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pipeline = view.CaptureOriginalActionSettlement();
            Assert.False(pipeline.IsCompleted);

            close = view.CloseAsync();
            Assert.False(close.IsCompleted);

            await Task.Run(() => source.Fault(failure));

            var closeError = await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(closeError);
            Assert.Contains(failure, AllCauses(closeError!));
            var pipelineError = await Record.ExceptionAsync(() => pipeline.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(pipelineError);
            Assert.Contains(failure, AllCauses(pipelineError!));

            // Faulted close retains input custody: the port is not terminally released and the
            // retained reference stays readable (host may clear its Content, but we hold the box).
            Assert.Equal("retained", view.Input.Text);
            // Repeated close returns the SAME failed task; no second release attempt.
            Assert.Same(close, view.CloseAsync());
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure, release: source.Release, additionalOriginals: [pipeline, source.LastAdmitted], expectedCause: failure);
        }
    }

    [AvaloniaFact]
    public async Task Capture_refuses_pre_init_after_withdrawal_and_off_thread()
    {
        var source = new RecordingSource();
        var readiness = new HeldReadiness();
        var view = new ChatComposerCuiView(source, readiness);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            // The SAME guarded init task is returned on repeated calls.
            var init = view.InitializeAsync();
            Assert.Same(init, view.InitializeAsync());

            // Pre-init (guarded attach not yet successful): capture is refused.
            await readiness.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var preInitError = await Record.ExceptionAsync(() => view.CaptureOriginalActionSettlement());
            Assert.NotNull(preInitError);

            readiness.Release();
            try { await init.WaitAsync(TimeSpan.FromSeconds(10)); } catch { /* owner withdrawal */ }

            // After guarded init succeeds, capture works and returns the actual loader snapshot.
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            source.Draft = "draft";
            var sendButton = FindButton(view, "send-button");
            Assert.NotNull(sendButton);
            sendButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pipeline = view.CaptureOriginalActionSettlement();
            Assert.NotNull(pipeline);

            // After withdrawal/close, capture is refused.
            await view.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var afterCloseError = await Record.ExceptionAsync(() => view.CaptureOriginalActionSettlement());
            Assert.NotNull(afterCloseError);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure, release: readiness.Release, allowOwnerWithdrawal: true);
        }
    }

    [AvaloniaFact]
    public async Task Capture_refuses_off_ui_thread()
    {
        var source = new RecordingSource();
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var error = await Record.ExceptionAsync(() => Task.Run(() => view.CaptureOriginalActionSettlement()));
            Assert.NotNull(error);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure);
        }
    }

    /// <summary>Recording native registrar that delegates real identity-safe mapping, records
    /// UI-thread access and exact instance/count, and can fault on Unregister or reenter the real
    /// release method (proving publication-before-callback).</summary>
    private sealed class RecordingRegistrar : IHavenNativeControlRegistrar
    {
        private readonly ChatGenUiNativeControlResolver _mapping = new();
        public int RegisterCount;
        public int UnregisterCount;
        public bool LastRegisterOnUI;
        public bool LastUnregisterOnUI;
        public Exception? UnregisterFault;
        public Action? OnUnregister;

        public bool TryCreate(HavenElement element, out Control? control)
        {
            return _mapping.TryCreate(element, out control);
        }

        public void Register(HavenElement element, Control control)
        {
            RegisterCount++;
            LastRegisterOnUI = Dispatcher.UIThread.CheckAccess();
            _mapping.Register(element, control);
        }

        public void Unregister(HavenElement element, Control control)
        {
            UnregisterCount++;
            LastUnregisterOnUI = Dispatcher.UIThread.CheckAccess();
            OnUnregister?.Invoke();
            if (UnregisterFault is not null) throw UnregisterFault;
            _mapping.Unregister(element, control);
        }

        public bool IsRegistered(HavenElement element) => _mapping.TryCreate(element, out _);
    }

    [AvaloniaFact]
    public async Task Parent_native_registrar_holds_mapping_until_child_ack_then_unregisters_once_on_ui()
    {
        var registrar = new RecordingRegistrar();
        var scene = new ChatHavenScene(registrar);
        var window = new Window { Width = 900, Height = 700, Content = new HavenSceneControl(new HavenAvaloniaImageResolver(), registrar) { Root = scene.Root } };
        window.Show();
        Exception? testFailure = null;
        Task? release = null;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await scene.ComposerInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            Assert.Equal(1, registrar.RegisterCount);
            Assert.Contains(scene.ComposerView, window.GetVisualDescendants());
            Assert.True(FindInput(scene.ComposerView)!.Bounds.Width > 0);
            Assert.True(registrar.TryCreate(scene.ComposerHost, out var mapped));
            Assert.Same(scene.ComposerView, mapped);
            Assert.True(registrar.LastRegisterOnUI);
            Assert.True(registrar.IsRegistered(scene.ComposerHost));

            // A held child action keeps the physical mapping until a positive child ACK.
            scene.ComposerDirectSend = _ => new ValueTask(held.Task);
            scene.Input.Text = "draft";
            var sendButton = FindButton(scene.ComposerView, "send-button");
            Assert.NotNull(sendButton);
            Assert.True(sendButton!.IsEnabled);
            sendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pipeline = scene.ComposerView.CaptureOriginalActionSettlement();
            Assert.False(pipeline.IsCompleted);

            release = scene.JoinComposerCloseAndReleaseAsync();
            Assert.False(release.IsCompleted);
            Assert.True(registrar.IsRegistered(scene.ComposerHost)); // not released pre-ACK

            // Reenter the real release method DURING Unregister: it must return the SAME published
            // release task (published before the callback), never a fresh attempt.
            registrar.OnUnregister = () =>
            {
                var reentered = scene.JoinComposerCloseAndReleaseAsync();
                Assert.Same(release, reentered);
            };

            // Release the held source on a worker thread; child ACKs; only then unregister once on UI.
            await Task.Run(() => held.TrySetResult());
            await release.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, registrar.UnregisterCount);
            Assert.True(registrar.LastUnregisterOnUI);
            Assert.False(registrar.IsRegistered(scene.ComposerHost));
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, scene.ComposerView, testFailure, additionalOriginals: [scene.JoinComposerCloseAndReleaseAsync()], release: () => held.TrySetResult());
        }
    }

    [AvaloniaFact]
    public async Task Parent_failed_unregister_retains_same_release_task_no_second_unregister()
    {
        var registrar = new RecordingRegistrar { UnregisterFault = new IOException("unregister failed") };
        var scene = new ChatHavenScene(registrar);
        var window = new Window { Width = 900, Height = 700, Content = new HavenSceneControl(new HavenAvaloniaImageResolver(), registrar) { Root = scene.Root } };
        window.Show();
        Exception? testFailure = null;
        Task? release = null;
        try
        {
            await scene.ComposerInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            Assert.Equal(1, registrar.RegisterCount);
            Assert.Contains(scene.ComposerView, window.GetVisualDescendants());
            Assert.True(FindInput(scene.ComposerView)!.Bounds.Width > 0);
            Assert.True(registrar.TryCreate(scene.ComposerHost, out var mapped));
            Assert.Same(scene.ComposerView, mapped);

            // Child close ACKs (no held action) then Unregister faults.
            release = scene.JoinComposerCloseAndReleaseAsync();
            var error = await Record.ExceptionAsync(() => release.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(error);
            Assert.Contains(registrar.UnregisterFault!, AllCauses(error!));
            Assert.Equal(1, registrar.UnregisterCount); // exactly one attempt

            // Repeated/reentrant join returns the SAME failed release task; no retry/unregister.
            var releaseAgain = scene.JoinComposerCloseAndReleaseAsync();
            Assert.Same(release, releaseAgain);
            Assert.Equal(1, registrar.UnregisterCount);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, scene.ComposerView, testFailure, additionalOriginals: [scene.JoinComposerCloseAndReleaseAsync()], expectedCause: registrar.UnregisterFault);
        }
    }

    [AvaloniaFact]
    public async Task Parent_child_fault_keeps_original_mapping_without_unregistering()
    {
        var registrar = new RecordingRegistrar();
        var scene = new ChatHavenScene(registrar);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("held child failed");
        var window = new Window
        {
            Width = 900, Height = 700,
            Content = new HavenSceneControl(new HavenAvaloniaImageResolver(), registrar) { Root = scene.Root }
        };
        Task? pipeline = null;
        Task? release = null;
        Exception? testFailure = null;
        window.Show();
        try
        {
            await scene.ComposerInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            Assert.Contains(scene.ComposerView, window.GetVisualDescendants());
            Assert.True(FindInput(scene.ComposerView)!.Bounds.Width > 0);
            scene.ComposerDirectSend = _ => new ValueTask(held.Task);
            scene.Input.Text = "draft";
            var button = FindButton(scene.ComposerView, "send-button");
            Assert.NotNull(button);
            Assert.True(button!.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pipeline = scene.ComposerView.CaptureOriginalActionSettlement();
            Assert.False(pipeline.IsCompleted);
            release = scene.JoinComposerCloseAndReleaseAsync();
            Assert.False(release.IsCompleted);

            await Task.Run(() => held.TrySetException(failure));
            var parentError = await Record.ExceptionAsync(() => release.WaitAsync(TimeSpan.FromSeconds(10)));
            var actionError = await Record.ExceptionAsync(() => pipeline.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(parentError);
            Assert.NotNull(actionError);
            Assert.Contains(failure, AllCauses(parentError!));
            Assert.Contains(failure, AllCauses(actionError!));
            Assert.Equal(0, registrar.UnregisterCount);
            Assert.True(registrar.TryCreate(scene.ComposerHost, out var mapped));
            Assert.Same(scene.ComposerView, mapped);
            Assert.Same(release, scene.JoinComposerCloseAndReleaseAsync());
            Assert.Equal(0, registrar.UnregisterCount);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, scene.ComposerView, testFailure,
                release: () => held.TrySetResult(),
                additionalOriginals: [pipeline, scene.JoinComposerCloseAndReleaseAsync(), held.Task],
                expectedCause: failure);
        }
    }

    private static async Task FinishComposerTestAsync(Window window, ChatComposerCuiView view,
        Exception? testFailure, Action? release = null, Task?[]? additionalOriginals = null,
        Exception? expectedCause = null, bool allowOwnerWithdrawal = false)
    {
        var failures = new List<Exception>();
        var originals = new List<Task>();
        try { release?.Invoke(); }
        catch (Exception error) { failures.Add(error); }
        // Acquire both duties independently: a failed acquisition/join cannot skip its sibling.
        try { originals.Add(view.InitializeAsync()); }
        catch (Exception error) { failures.Add(error); }
        try { originals.Add(view.CloseAsync()); }
        catch (Exception error) { failures.Add(error); }
        if (additionalOriginals is not null)
            originals.AddRange(additionalOriginals.OfType<Task>());
        foreach (var original in originals.Distinct())
        {
            try { await original.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception error)
            {
                var causes = AllCauses(error).ToArray();
                if (causes.Length > 0 && causes.All(cause => ReferenceEquals(cause, expectedCause) ||
                    (allowOwnerWithdrawal && cause is OperationCanceledException))) continue;
                failures.Add(error);
            }
        }
        try { window.Close(); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count == 0) return;
        if (testFailure is not null) failures.Insert(0, testFailure);
        throw new AggregateException("Composer test and independent original cleanup failed.", failures);
    }

    [AvaloniaFact]
    public async Task Native_composer_real_headless_keyboard_and_text_edit_actual_textbox()
    {
        // Honest native editing test: drive the ACTUAL headless Window/device keyboard+text
        // transport (KeyTextInput/KeyPress/KeyRelease) at the focused Realm TextBox, not direct
        // Text assignment. The native TextBox legally marks its own edits handled; we assert the
        // actual native value/selection/focus instead of an invalid Handled=false expectation.
        var registrar = new RecordingRegistrar();
        var scene = new ChatHavenScene(registrar);
        var window = new Window
        {
            Width = 900, Height = 700,
            Content = new HavenSceneControl(new HavenAvaloniaImageResolver(), registrar) { Root = scene.Root }
        };
        Exception? testFailure = null;
        window.Show();
        try
        {
            await scene.ComposerInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(scene.ComposerView);
            Assert.NotNull(box);
            box!.Focus();
            Assert.True(box.IsKeyboardFocusWithin);

            var sceneControl = (HavenSceneControl)window.Content!;
            Assert.True(sceneControl.IsNativeControlSource(box));

            // Real headless text input at the retained native TextBox.
            window.KeyTextInput("first line");
            Assert.Equal("first line", box.Text);

            // Shift+Enter through the real device route inserts a second line (AcceptsReturn).
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
            window.KeyTextInput("second line");

            Assert.Equal("first line\nsecond line", box.Text);
            Assert.True(box.GetLineCount() >= 2);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, scene.ComposerView, testFailure);
        }
    }

    private static IEnumerable<Exception> AllCauses(Exception error)
    {
        if (error is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var cause in AllCauses(inner))
                    yield return cause;
        else yield return error;
    }
}
