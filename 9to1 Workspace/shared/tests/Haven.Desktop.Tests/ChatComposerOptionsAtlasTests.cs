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
using Haven.Desktop.Views.Pages.Chat;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual compiled shared composer and native popup, with controlled source operations.
/// These frames qualify this component at the stated owner viewport, not installed app activation.</summary>
public sealed class ChatComposerOptionsAtlasTests
{
    // Capture once before pumping callbacks from other retained UI owners.
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;

    [AvaloniaTheory]
    [InlineData("Glow", 400, 650)]
    [InlineData("Glow", 720, 650)]
    [InlineData("Glow", 1440, 900)]
    [InlineData("Professional", 400, 650)]
    [InlineData("Professional", 720, 650)]
    [InlineData("Professional", 1440, 900)]
    public async Task Atlas_Options_keep_fixed_header_footer_and_independent_permissions_at_each_viewport(string theme, int width, int height)
    {
        await using var rig = await OptionsRig.CreateAsync(theme, width, height, _token);
        try
        {
            var source = rig.Source;
            var draft = source.Draft;
            var chip = Assert.Single(source.Chips);
            var input = Find<TextBox>(rig.View, "composer-input");
            rig.View.Input.SetSelection(2, 7);
            var settings = Find<Button>(rig.View, "settings-button");
            Assert.DoesNotContain(rig.View.GetVisualDescendants().OfType<Button>(),
                button => Id(button) == "permissions-button");
            Assert.True(rig.View.Bindings.IsActionAvailable("composer.permissions"));
            await rig.ClickAsync(settings);
            var panel = rig.Panel;
            Assert.True(Find<Button>(panel, "options-close").TryFindResource("CuiEffectiveThemeName", out var actualTheme));
            Assert.Equal(theme, actualTheme);
            Assert.Same(settings, rig.View.OptionsPopup!.PlacementTarget);
            Assert.InRange(panel.Bounds.Width, 1, Math.Min(400, width - 24) + 1);
            Assert.InRange(panel.Bounds.Height, 1, height - 31);
            Assert.False(Find<Control>(panel, "options-permission-fields").IsEffectivelyVisible);
            Assert.True(Find<Button>(panel, "options-temporary").IsEffectivelyEnabled);
            Assert.True(Find<Button>(panel, "options-compact").IsEffectivelyEnabled);
            AssertReachable(panel, Find<Button>(panel, "options-close"));
            AssertReachable(panel, Find<Button>(panel, "options-cancel"));
            AssertReachable(panel, Find<Button>(panel, "options-save"));
            var body = Find<Control>(panel, "options-scroll-body");
            var scroll = Assert.Single(body.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(scroll.Viewport.Height >= 60, $"The real Options body needs a usable viewport: {scroll.Viewport}.");
            Assert.Equal(0, source.Owner!.TemporaryCalls);
            Assert.Equal(0, source.Owner!.CompactionCalls);
            await rig.CaptureAsync($"options-collapsed-{theme}-{width}x{height}");

            var presentation = Find<MenuItem>(panel, "options-presentation");
            AssertReachable(panel, presentation);
            Assert.Equal("Response presentation: Automatic", AutomationProperties.GetName(presentation));
            Assert.Equal(0, source.ResponseModeWrites);
            await rig.PressPresentationAsync("options-response-prefer-visual", $"options-presentation-{theme}-{width}x{height}");
            await rig.View.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10), _token);
            await rig.SettleAsync();
            Assert.Equal(GenerativeUiResponseMode.PreferVisual, source.ResponseMode);
            Assert.Equal(1, source.ResponseModeWrites);
            Assert.False(presentation.IsSubMenuOpen);
            Assert.True(rig.View.OptionsPopup!.IsOpen);
            Assert.Same(panel, rig.Panel);
            Assert.Equal("Response presentation: Prefer visual", AutomationProperties.GetName(presentation));
            Assert.Equal(0, source.ActionModeWrites);
            Assert.Equal(draft, source.Draft);

            await rig.ClickAsync(Find<Button>(panel, "options-permissions-toggle"));
            Assert.True(Find<Control>(panel, "options-permission-fields").IsEffectivelyVisible);
            await rig.ClickAsync(Choice(panel, "All actions"));
            Assert.Equal(ChatActionMode.AllowBasicActions, source.ActionMode);
            Assert.Equal(0, source.ActionModeWrites);
            AssertReachable(panel, Find<Button>(panel, "options-close"));
            AssertReachable(panel, Find<Button>(panel, "options-save"));
            await rig.CaptureAsync($"options-permissions-{theme}-{width}x{height}");
            panel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            await rig.SettleAsync();
            Assert.Null(rig.View.OptionsPopup);
            Assert.Same(settings, rig.Window.FocusManager?.GetFocusedElement());
            Assert.Equal(0, source.ActionModeWrites);
            Assert.Equal(GenerativeUiResponseMode.PreferVisual, source.ResponseMode);
            Assert.Equal(1, source.ResponseModeWrites);

            // The existing direct permissions command remains available, opening the same
            // transactional surface through the single visible Options trigger.
            await rig.View.Bindings.DispatchAsync("composer.permissions", null, _token);
            await rig.SettleAsync();
            panel = rig.Panel;
            Assert.Same(settings, rig.View.OptionsPopup!.PlacementTarget);
            Assert.True(Find<Control>(panel, "options-permission-fields").IsEffectivelyVisible);
            await rig.ClickAsync(Choice(panel, "All actions"));
            await rig.DismissOptionsAsync();
            Assert.Null(rig.View.OptionsPopup);
            Assert.Same(settings, rig.Window.FocusManager?.GetFocusedElement());
            Assert.Equal(0, source.ActionModeWrites);
            Assert.Same(input, Find<TextBox>(rig.View, "composer-input"));
            Assert.Equal(draft, rig.View.Input.Text);
            Assert.Equal(2, rig.View.Input.SelectionStart);
            Assert.Equal(5, rig.View.Input.SelectionLength);
            Assert.Same(chip, Assert.Single(source.Chips));
            Assert.Equal(0, source.Owner!.TemporaryCalls);
            Assert.Equal(0, source.Owner!.CompactionCalls);
        }
        catch (Exception error) { rig.TestFailure = error; throw; }
    }

    [AvaloniaFact]
    public async Task Options_without_a_conversation_owner_do_not_advertise_Temporary_or_compaction()
    {
        await using var rig = await OptionsRig.CreateAsync("Professional", 720, 650, _token, new OptionsSource { Owner = null });
        try
        {
            await rig.ClickAsync(Find<Button>(rig.View, "settings-button"));
            Assert.DoesNotContain(Controls(rig.Panel), control => Id(control) is "options-temporary" or "options-compact");
            Assert.NotNull(Find<Button>(rig.Panel, "options-permissions-toggle"));
            Assert.False(rig.View.Bindings.IsActionAvailable("composer.temporary"));
            Assert.False(rig.View.Bindings.IsActionAvailable("composer.compact-context"));
        }
        catch (Exception error) { rig.TestFailure = error; throw; }
    }

    [AvaloniaTheory]
    [InlineData("identity")]
    [InlineData("owner")]
    [InlineData("safety")]
    [InlineData("availability")]
    public async Task Options_revalidate_the_exact_admitted_conversation_before_invoking_an_action(string change)
    {
        var source = new OptionsSource();
        var original = source.Owner!;
        var bindings = new ChatComposerBindings(source);
        await bindings.DispatchAsync("composer.settings", null, _token);
        switch (change)
        {
            case "identity": source.ActionIdentity = Guid.NewGuid(); break;
            case "owner": source.Owner = new OptionsActions(); break;
            case "safety": source.IsSafetyLocked = true; break;
            case "availability": original.State = original.State with { CanChangeTemporary = false, CanCompactContext = false }; break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.temporary", null, _token).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.compact-context", null, _token).AsTask());
        Assert.Equal(0, original.TemporaryCalls); Assert.Equal(0, original.CompactionCalls);
        Assert.Equal(0, source.Owner!.TemporaryCalls); Assert.Equal(0, source.Owner!.CompactionCalls);
        Assert.Equal("Keep the exact pending draft.", source.Draft);
        Assert.Equal(0, source.ActionModeWrites);
    }

    [AvaloniaFact]
    public async Task Options_dismissal_failure_keeps_the_original_operation_pending_and_preserves_both_causes()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new OptionsSource();
        source.Owner!.Temporary = _ => new ValueTask(held.Task);
        var bindings = new ChatComposerBindings(source);
        await bindings.DispatchAsync("composer.settings", null, _token);
        var dismissal = new InvalidOperationException("controlled Options dismissal failure");
        var late = new IOException("controlled late original action failure");
        bindings.OptionsVisibilityChanged += show => { if (!show) throw dismissal; };
        var actual = bindings.DispatchAsync("composer.temporary", null, _token).AsTask();
        try
        {
            Assert.Equal(1, source.Owner!.TemporaryCalls);
            Assert.False(actual.IsCompleted);
            source.ActionIdentity = Guid.NewGuid(); // Cannot revoke custody of the already-admitted operation.
            held.SetException(late);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual.WaitAsync(TimeSpan.FromSeconds(10), _token));
            Assert.Contains(Leaves(error), cause => ReferenceEquals(cause, dismissal));
            Assert.Contains(Leaves(error), cause => ReferenceEquals(cause, late));
            Assert.Equal(1, source.Owner!.TemporaryCalls);
        }
        finally
        {
            held.TrySetException(late);
            Assert.Same(late, await Record.ExceptionAsync(() => held.Task.WaitAsync(TimeSpan.FromSeconds(10), _token)));
            // Always join the same original, including if an assertion above fails.
            var settlement = await Record.ExceptionAsync(() => actual.WaitAsync(TimeSpan.FromSeconds(10), _token));
            Assert.NotNull(settlement);
            Assert.All(Leaves(settlement), cause => Assert.True(ReferenceEquals(cause, dismissal) || ReferenceEquals(cause, late)));
        }
    }

    [AvaloniaFact]
    public async Task Options_completing_action_cannot_clear_a_newer_admitted_action_during_refresh()
    {
        var firstSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new OptionsSource();
        source.Owner!.Temporary = _ => source.Owner!.TemporaryCalls switch
        {
            1 => new ValueTask(firstSource.Task),
            2 => new ValueTask(secondSource.Task),
            _ => ValueTask.FromException(new InvalidOperationException("A third source operation must not be admitted."))
        };
        var bindings = new ChatComposerBindings(source);
        await bindings.DispatchAsync("composer.settings", null, _token);
        var firstAction = bindings.DispatchAsync("composer.temporary", null, _token).AsTask();
        Task? openSecond = null, secondAction = null, secondChain = null;
        var admittedSecond = false;
        Exception? testFailure = null;
        async Task AdmitSecondAsync()
        {
            openSecond = bindings.DispatchAsync("composer.settings", null, _token).AsTask();
            await openSecond;
            secondAction = bindings.DispatchAsync("composer.temporary", null, _token).AsTask();
            await secondAction;
        }
        void OnRefresh(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
        {
            if (admittedSecond || !firstSource.Task.IsCompleted) return;
            admittedSecond = true;
            secondChain = AdmitSecondAsync();
        }
        bindings.PropertyChanged += OnRefresh;
        try
        {
            Assert.Equal(1, source.Owner!.TemporaryCalls);
            Assert.False(firstAction.IsCompleted);
            firstSource.SetResult();
            await firstAction.WaitAsync(TimeSpan.FromSeconds(10), _token);
            Assert.Equal(2, source.Owner!.TemporaryCalls);
            Assert.NotNull(secondAction); Assert.NotNull(secondChain);
            Assert.False(secondAction.IsCompleted);

            // Reopening the menu is allowed, but the actual pending B operation still
            // prevents another invocation after A's completion and final cleanup.
            await bindings.DispatchAsync("composer.settings", null, _token);
            Assert.False(bindings.IsActionAvailable("composer.temporary"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.temporary", null, _token).AsTask());
            Assert.Equal(2, source.Owner!.TemporaryCalls);
            Assert.False(secondAction.IsCompleted);
            secondSource.SetResult();
            await secondChain.WaitAsync(TimeSpan.FromSeconds(10), _token);
            Assert.True(bindings.IsActionAvailable("composer.temporary"));
            Assert.Equal(2, source.Owner!.TemporaryCalls);
            Assert.Equal("Keep the exact pending draft.", source.Draft);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            bindings.PropertyChanged -= OnRefresh;
            firstSource.TrySetResult(); secondSource.TrySetResult();
            var failures = new List<Exception>();
            var originals = new Task?[] { firstSource.Task, secondSource.Task, firstAction, openSecond, secondAction, secondChain };
            foreach (var original in originals.OfType<Task>().Distinct())
            {
                try { await original.WaitAsync(TimeSpan.FromSeconds(10), _token); }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count > 0)
            {
                if (testFailure is not null) failures.Insert(0, testFailure);
                throw new AggregateException("The Options completion regression and its actual source operations did not settle cleanly.", failures);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Options_native_action_and_composer_close_wait_for_the_same_held_source_and_keep_its_late_failure(bool responsePresentation)
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new OptionsSource();
        if (responsePresentation) source.ResponseWrite = _ => new ValueTask(held.Task);
        else source.Owner!.Temporary = _ => new ValueTask(held.Task);
        var late = new IOException("controlled late original Options owner failure");
        await using var rig = await OptionsRig.CreateAsync("Glow", 720, 650, _token, source);
        rig.Release = () => held.TrySetException(late);
        rig.ExpectedCloseCauses.Add(late);
        rig.AdditionalOriginals.Add(held.Task);
        try
        {
            await rig.ClickAsync(Find<Button>(rig.View, "settings-button"));
            if (responsePresentation) await rig.PressPresentationAsync("options-response-prefer-visual");
            else Find<Button>(rig.Panel, "options-temporary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var admitted = rig.View.CaptureOriginalActionSettlement();
            await rig.SettleAsync();
            Assert.Equal(responsePresentation ? 0 : 1, source.Owner!.TemporaryCalls);
            Assert.Equal(responsePresentation ? 1 : 0, source.ResponseModeWrites);
            if (responsePresentation) Assert.True(rig.View.OptionsPopup?.IsOpen);
            else Assert.Null(rig.View.OptionsPopup);
            Assert.False(admitted.IsCompleted);
            var close = rig.View.CloseAsync();
            Assert.False(close.IsCompleted);
            held.SetException(late);
            var actionError = await Record.ExceptionAsync(() => admitted.WaitAsync(TimeSpan.FromSeconds(10), _token));
            var closeError = await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(10), _token));
            Assert.NotNull(actionError); Assert.NotNull(closeError);
            Assert.Contains(Leaves(actionError), cause => ReferenceEquals(cause, late));
            Assert.Contains(Leaves(closeError), cause => ReferenceEquals(cause, late));
            // This case deliberately retires the native CUI publication before its held action
            // fails. Its exact refusal to publish that late error is retained alongside the
            // original IOException. Qualify every extra cause, then retain those same instances
            // for cleanup; unrelated cancellation and independent failures remain failures.
            foreach (var cause in Leaves(actionError).Concat(Leaves(closeError)).Distinct())
            {
                if (ReferenceEquals(cause, late)) continue;
                Assert.True(responsePresentation);
                var retirement = Assert.IsType<OperationCanceledException>(cause);
                Assert.Equal("The original CUI scene publication retired.", retirement.Message);
                Assert.Equal(CancellationToken.None, retirement.CancellationToken);
                Assert.Contains("CuiSceneHost.DemandPublicationCurrent", retirement.StackTrace);
                rig.ExpectedCloseCauses.Add(retirement);
            }
            Assert.Equal("Keep the exact pending draft.", source.Draft);
            Assert.Equal(0, source.ActionModeWrites);
            Assert.Equal(GenerativeUiResponseMode.Auto, source.ResponseMode);
        }
        catch (Exception error) { rig.TestFailure = error; throw; }
    }

    private static IEnumerable<Control> Controls(Control root) => root.GetVisualDescendants().OfType<Control>().Prepend(root);
    private static string? Id(Control control) => AutomationProperties.GetAutomationId(control) is { Length: > 0 } id ? id : control.Name;
    private static T Find<T>(Control root, string id) where T : Control => Assert.Single(AuthoredCuiControls.Enumerate(root).OfType<T>(), control => Id(control) == id);
    private static Button Choice(Control panel, string content) => Assert.Single(Controls(panel).OfType<Button>(), button => Equals(button.Content, content));
    private static void AssertReachable(Control panel, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var point = control.TranslatePoint(default, panel); Assert.NotNull(point);
        Assert.True(point.Value.X >= -1 && point.Value.Y >= -1 &&
            point.Value.X + control.Bounds.Width <= panel.Bounds.Width + 1 &&
            point.Value.Y + control.Bounds.Height <= panel.Bounds.Height + 1,
            $"{Id(control)} {control.Bounds} at {point} must fit inside the actual Options panel {panel.Bounds}.");
    }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(Leaves) : [error];

    private sealed class OptionsActions : IChatComposerConversationActions
    {
        internal ChatComposerConversationOptions State = new(false, true, "This unsent chat keeps its draft.", true,
            "Older turns can be summarised while retaining full history.", "Messages stay local. Saving does not grant tools.");
        public ChatComposerConversationOptions Current => State;
        internal int TemporaryCalls, CompactionCalls;
        internal Func<bool, ValueTask>? Temporary;
        public ValueTask SetTemporary(bool temporary)
        {
            TemporaryCalls++;
            if (Temporary is not null) return Temporary(temporary);
            State = State with { IsTemporary = temporary }; return ValueTask.CompletedTask;
        }
        public ValueTask CompactContext() { CompactionCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class OptionsSource : IChatComposerSource
    {
        internal OptionsActions? Owner = new();
        public IChatComposerConversationActions? ConversationActions => Owner;
        public object? ActionIdentity { get; set; } = Guid.NewGuid();
        public string Draft { get; set; } = "Keep the exact pending draft.";
        public string Placeholder => "Message";
        public bool IsSending => false;
        public bool IsSafetyLocked { get; set; }
        public bool HasAdd => true;
        public bool CanSend => true;
        public bool IsEditing => false;
        public IReadOnlyList<ChatComposerChip> Chips { get; } = [new("retained-attachment", "Existing attachment", false)];
        public ChatActionMode ActionMode { get; private set; } = ChatActionMode.AllowBasicActions;
        public GenerativeUiResponseMode ResponseMode { get; private set; } = GenerativeUiResponseMode.Auto;
        internal int ActionModeWrites, ResponseModeWrites;
        internal Func<string, ValueTask>? ResponseWrite;
        public ValueTask SetActionMode(string mode) { ActionModeWrites++; ActionMode = Enum.Parse<ChatActionMode>(mode); return ValueTask.CompletedTask; }
        public async ValueTask SetResponseMode(string mode)
        {
            ResponseModeWrites++;
            if (ResponseWrite is not null) await ResponseWrite(mode);
            ResponseMode = Enum.Parse<GenerativeUiResponseMode>(mode);
        }
        public ValueTask Send() => ValueTask.CompletedTask;
        public ValueTask Stop() => ValueTask.CompletedTask;
        public ValueTask Add() => ValueTask.CompletedTask;
        public ValueTask OpenSettings() => ValueTask.CompletedTask;
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask InvokeChip(string id) => ValueTask.CompletedTask;
        public ValueTask RemoveChip(string id) => ValueTask.CompletedTask;
        public ValueTask FormatSelection(string command) => ValueTask.CompletedTask;
        public ValueTask InsertEmoji(string emoji) => ValueTask.CompletedTask;
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }

    private sealed class OptionsTheme(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "options-component", true,
            CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class OptionsRig : IAsyncDisposable
    {
        internal readonly OptionsSource Source;
        internal readonly ChatComposerCuiView View;
        internal readonly Window Window;
        private readonly CuiThemePreferenceContext _theme;
        private readonly CancellationToken _token;
        private Task? _close;
        internal Exception? TestFailure;
        internal readonly List<Exception> ExpectedCloseCauses = [];
        internal Action? Release;
        internal readonly List<Task> AdditionalOriginals = [];
        internal Control Panel => Assert.IsAssignableFrom<Control>(View.OptionsPopup?.Child);
        private OptionsRig(string theme, int width, int height, OptionsSource source, CancellationToken token)
        {
            _token = token;
            Source = source; _theme = new(new OptionsTheme(theme));
            View = new(source, owningAppId: "spaces", themePreference: _theme);
            var content = new Grid { RowDefinitions = new("*,Auto") };
            content.Children.Add(new ScrollViewer { Content = new Border { Height = 1800 } });
            content.Children.Add(View); Grid.SetRow(View, 1);
            Window = new() { Width = width, Height = height, Content = content };
        }
        internal static async Task<OptionsRig> CreateAsync(string theme, int width, int height, CancellationToken token, OptionsSource? source = null)
        {
            var rig = new OptionsRig(theme, width, height, source ?? new(), token);
            try
            {
                rig.Window.Show(); rig.Window.Activate();
                await rig.View.InitializeAsync(token).WaitAsync(TimeSpan.FromSeconds(15), token);
                await rig.SettleAsync(); return rig;
            }
            catch (Exception error) { rig.TestFailure = error; await rig.DisposeAsync(); throw; }
        }
        internal async Task ClickAsync(Button button)
        {
            button.BringIntoView(); await SettleAsync();
            Assert.True(button.IsEffectivelyEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await View.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10), _token);
            await SettleAsync();
        }
        internal async Task DismissOptionsAsync()
        {
            var panel = Panel;
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR")))
            {
                using var frame = Window.CaptureRenderedFrame(); Assert.NotNull(frame);
                var outside = new Point(4, 4);
                // The panel may belong to a separate native PopupRoot. Screen conversion
                // preserves the actual roots and their scaling without assuming a common ancestor.
                var topLeft = Window.PointToClient(panel.PointToScreen(default));
                var bottomRight = Window.PointToClient(panel.PointToScreen(new Point(panel.Bounds.Width, panel.Bounds.Height)));
                Assert.False(new Rect(topLeft, bottomRight).Contains(outside));
                Window.MouseMove(outside);
                Window.MouseDown(outside, MouseButton.Left); Window.MouseUp(outside, MouseButton.Left);
            }
            else
            {
                // Simplified drawing has no material hit geometry. Exercise the real keyboard
                // dismissal here; the Skia run above separately requires actual outside-click dismissal.
                Assert.True(Find<Button>(panel, "options-close").Focus());
                var top = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(panel));
                top.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                top.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            }
            await View.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10), _token);
            await SettleAsync();
        }
        internal async Task PressPresentationAsync(string choiceId, string? captureName = null)
        {
            var menu = Find<MenuItem>(Panel, "options-presentation");
            menu.BringIntoView(); await SettleAsync();
            Assert.False(menu.IsSubMenuOpen);
            var writes = Source.ResponseModeWrites;
            Assert.True(menu.Focus());
            var opener = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(menu));
            opener.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            opener.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            await SettleAsync();
            Assert.True(menu.IsSubMenuOpen);
            Assert.Equal(writes, Source.ResponseModeWrites); // Opening never changes the preference.
            var choices = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(5, choices.Length);
            Assert.All(choices, item => Assert.True(item.IsEffectivelyVisible && item.Bounds.Width > 0 && item.Bounds.Height >= 40));
            var choice = Assert.Single(choices, item => Id(item) == choiceId);
            var top = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(choice));
            top.UpdateLayout();
            // TestAppBuilder selects Skia for capture runs and simplified drawing otherwise.
            // Qualify real pointer targets with the actual renderer; the ordinary behavioural
            // run exercises the same native menu through keyboard input instead.
            var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
            using var frame = top.CaptureRenderedFrame();
            if (!string.IsNullOrWhiteSpace(directory)) Assert.NotNull(frame);
            if (frame is not null && captureName is not null && !string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory); frame.Save(Path.Combine(directory, captureName + ".png"));
            }
            Assert.True(choice.IsEffectivelyEnabled);
            var clicks = 0;
            EventHandler<RoutedEventArgs> clicked = (_, args) => { if (ReferenceEquals(args.Source, choice)) clicks++; };
            choice.Click += clicked;
            try
            {
                if (frame is null)
                {
                    Assert.True(choice.Focus());
                    top.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    top.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                }
                else
                {
                    var origin = choice.TranslatePoint(default, top); Assert.NotNull(origin);
                    var point = origin.Value + new Vector(choice.Bounds.Width / 2, choice.Bounds.Height / 2);
                    var hit = top.InputHitTest(point) as Visual;
                    Assert.True(ReferenceEquals(hit, choice) || hit is not null && choice.IsVisualAncestorOf(hit),
                        $"The actual menu choice must own its pointer target: point={point}, choice={choice.Bounds}, top={top.Bounds}, hit={hit?.GetType().Name}.");
                    top.MouseMove(point);
                    top.MouseDown(point, MouseButton.Left); top.MouseUp(point, MouseButton.Left);
                }
                await SettleAsync(); // The caller retains and joins the original loader action.
                Assert.True(clicks == 1, $"The actual native input journey must invoke the current choice once: clicks={clicks}, submenuOpen={menu.IsSubMenuOpen}, enabled={choice.IsEffectivelyEnabled}.");
            }
            finally { choice.Click -= clicked; }
        }
        internal async Task SettleAsync()
        {
            for (var pass = 0; pass < 3; pass++)
            {
                Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
                if (View.OptionsPopup?.Child is Control panel) TopLevel.GetTopLevel(panel)?.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); await Task.Delay(1, _token);
            }
        }
        internal async Task CaptureAsync(string name)
        {
            var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) return;
            await SettleAsync();
            var top = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(Panel));
            using var frame = top.CaptureRenderedFrame(); Assert.NotNull(frame);
            Directory.CreateDirectory(directory); frame.Save(Path.Combine(directory, name + ".png"));
        }
        public ValueTask DisposeAsync() => new(_close ??= CloseAsync());
        private async Task CloseAsync()
        {
            var failures = new List<Exception>();
            try { Release?.Invoke(); } catch (Exception error) { failures.Add(error); }
            Task? close = null;
            try { close = View.CloseAsync(); } catch (Exception error) { failures.Add(error); }
            var originals = new List<Task>(AdditionalOriginals);
            if (close is not null) originals.Add(close);
            foreach (var original in originals.Distinct())
            {
                try { await original.WaitAsync(TimeSpan.FromSeconds(15), _token); }
                catch (Exception error)
                {
                    var causes = Leaves(error).ToArray();
                    if (causes.Length == 0 || causes.Any(cause => !ExpectedCloseCauses.Any(expected => ReferenceEquals(cause, expected))))
                        failures.Add(error);
                }
            }
            // Do not detach a still-running original just because a bounded waiter timed out.
            if (close?.IsCompleted == true && originals.All(original => original.IsCompleted))
            {
                try { Window.Content = null; Window.Close(); _theme.Dispose(); }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count == 0) return;
            if (TestFailure is not null) failures.Insert(0, TestFailure);
            throw new AggregateException("Options test and original composer cleanup did not settle cleanly.", failures);
        }
    }
}
