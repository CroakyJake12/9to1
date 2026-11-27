using Avalonia;
using Haven.UI.Emoji;
using Haven.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Desktop.Prefabs;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Tests;
using System.Globalization;
using System.IO;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Bounded emoji picker viewport evidence for the shared composer. These mount the REAL authored
/// ChatComposerCuiView (compiled artifact) in a real Window and assert:
///   - the generic Repeat emits cells into the owning Wrap so a genuine multi-column/row grid spans
///     a positive bounded scroll viewport (not one hidden vertical column);
///   - realized control count is bounded while the full 3,944-entry catalogue (including flags and
///     trigger-excluded sequences) stays reachable through paging/search;
///   - pointer click on a noninitial visible emoji dispatches its exact Unicode once and preserves
///     the surrounding draft, input identity, focus and settlement.
/// NOTE: these tests are authored for execution after root rebuilds the compiled composer artifact
/// from the updated ChatComposer.cui (catalog/lane-2 allocation). They are NOT RUN until then.
/// </summary>
public sealed class ChatComposerEmojiPickerViewportTests
{
    private const int EmojiPageSize = 48;

    /// <summary>A source that performs a GENUINE insertion into its Draft (not a counter): it reads
    /// the composer input port's real caret and inserts the emoji there, preserving the rest of the
    /// draft. Undo/identity are tracked so the preservation assertions are meaningful.</summary>
    private sealed class DraftInsertingSource : IChatComposerSource
    {
        private readonly List<string> _undo = [];
        public string Draft { get; set; } = string.Empty;
        public string Placeholder => "Ask Haven anything";
        public bool IsSending { get; set; }
        public bool IsSafetyLocked { get; set; }
        public bool HasAdd { get; set; } = true;
        public bool CanSend { get; set; } = true;
        public bool IsEditing { get; set; }
        public IReadOnlyList<ChatComposerChip> Chips { get; set; } = [];
        public string? LastInserted { get; private set; }
        public int InsertCount { get; private set; }
        public int InsertCaret { get; set; } = -1;
        public int SendCount { get; private set; }
        public int StopCount { get; private set; }
        public bool CanEnterVoice { get; set; }
        public ValueTask EnterVoice() => ValueTask.CompletedTask;

        public ValueTask InsertEmoji(string emoji)
        {
            InsertCount++;
            LastInserted = emoji;
            var caret = Math.Clamp(InsertCaret, 0, Draft.Length);
            _undo.Add(Draft);
            Draft = Draft[..caret] + emoji + Draft[caret..];
            return ValueTask.CompletedTask;
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            Draft = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            return true;
        }

        public ValueTask Send() { SendCount++; return ValueTask.CompletedTask; }
        public ValueTask Stop() { StopCount++; return ValueTask.CompletedTask; }
        public ValueTask Add() => ValueTask.CompletedTask;
        public ValueTask OpenSettings() => ValueTask.CompletedTask;
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ChatActionMode ActionMode { get; private set; } = ChatActionMode.AllowBasicActions;
        public GenerativeUiResponseMode ResponseMode { get; private set; } = GenerativeUiResponseMode.Auto;
        public int ActionModeWrites { get; private set; }
        public object? ActionIdentity { get; set; }
        public Task? ActionWriteReceipt { get; set; }

        public async ValueTask SetActionMode(string mode)
        {
            var conversation = ActionIdentity;
            ActionModeWrites++;
            if (ActionWriteReceipt is { } receipt) await receipt;
            if (Equals(conversation, ActionIdentity))
                ActionMode = Enum.Parse<ChatActionMode>(mode);
        }
        public ValueTask SetResponseMode(string mode)
        {
            ResponseMode = Enum.Parse<GenerativeUiResponseMode>(mode);
            return ValueTask.CompletedTask;
        }
        public ValueTask InvokeChip(string id) => ValueTask.CompletedTask;
        public ValueTask RemoveChip(string id) => ValueTask.CompletedTask;
        public ValueTask FormatSelection(string command) => ValueTask.CompletedTask;
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }

    /// <summary>A real source whose InsertEmoji returns the SAME held operation task; the edit is
    /// applied only when that operation is released/faulted, so pending editor state, exact-once
    /// settlement, fault retention and close-join can be asserted genuinely.</summary>
    private sealed class HeldEmojiSource : IChatComposerSource
    {
        private readonly TaskCompletionSource _insert = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Draft { get; set; } = string.Empty;
        public string Placeholder => "Ask Haven anything";
        public bool IsSending { get; set; }
        public bool IsSafetyLocked { get; set; }
        public bool HasAdd { get; set; } = true;
        public bool CanSend { get; set; } = true;
        public bool IsEditing { get; set; }
        public IReadOnlyList<ChatComposerChip> Chips { get; set; } = [];
        public int InsertCount { get; private set; }
        public string? LastInserted { get; private set; }
        public int InsertCaret { get; set; } = -1;
        public bool InsertPending => !_insert.Task.IsCompleted;

        public ValueTask InsertEmoji(string emoji)
        {
            InsertCount++;
            LastInserted = emoji;
            return new ValueTask(_insert.Task);
        }

        public void Release()
        {
            if (_insert.Task.IsCompleted) return;
            var caret = Math.Clamp(InsertCaret, 0, Draft.Length);
            Draft = Draft[..caret] + LastInserted + Draft[caret..];
            _insert.TrySetResult();
        }

        public void Fault(Exception error) => _insert.TrySetException(error);
        public ValueTask Send() => ValueTask.CompletedTask;
        public ValueTask Stop() => ValueTask.CompletedTask;
        public ValueTask Add() => ValueTask.CompletedTask;
        public ValueTask OpenSettings() => ValueTask.CompletedTask;
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask SetActionMode(string mode) => ValueTask.CompletedTask;
        public ValueTask SetResponseMode(string mode) => ValueTask.CompletedTask;
        public ValueTask InvokeChip(string id) => ValueTask.CompletedTask;
        public ValueTask RemoveChip(string id) => ValueTask.CompletedTask;
        public ValueTask FormatSelection(string command) => ValueTask.CompletedTask;
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }

    private static Control? FindContainer(ContentControl root, string automationId) =>
        root.GetVisualDescendants().OfType<Control>().SingleOrDefault(control =>
            AutomationProperties.GetAutomationId(control) == automationId);

    private static Button? FindButton(ContentControl root, string automationId) =>
        root.GetVisualDescendants().OfType<Button>().SingleOrDefault(b =>
            AutomationProperties.GetAutomationId(b) == automationId);

    private static TextBox? FindInput(ContentControl root) =>
        root.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(box =>
            AutomationProperties.GetAutomationId(box) == "composer-input");

    private static TextBox? FindInput(ContentControl root, string automationId) =>
        root.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(box =>
            AutomationProperties.GetAutomationId(box) == automationId);

    private static TextBlock? FindLabel(ContentControl root, string automationId) =>
        root.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(label =>
            AutomationProperties.GetAutomationId(label) == automationId);

    // Include every authored insertion cell, even one with malformed content; exclude scrollbar controls.
    private static Button[] EmojiCells(Control viewport) =>
        viewport.GetVisualDescendants().OfType<Button>()
            .Where(button => string.Equals(button.Tag as string, "composer.emoji-insert.Text", StringComparison.Ordinal))
            .ToArray();

    private static Point OriginIn(Control control, Control owner)
    {
        var origin = control.TranslatePoint(default, owner);
        Assert.NotNull(origin);
        return origin.Value;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

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
        throw new AggregateException("Composer picker test and original cleanup failed.", failures);
    }

    private static IEnumerable<Exception> AllCauses(Exception error)
    {
        if (error is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var cause in AllCauses(inner))
                    yield return cause;
        else yield return error;
    }

    private static Button OpenPicker(ChatComposerCuiView view, Window window)
    {
        var box = FindInput(view); Assert.NotNull(box);
        box!.Focus(); Pump();
        var emojiButton = FindButton(view, "emoji-button"); Assert.NotNull(emojiButton);
        emojiButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var panel = FindContainer(view, "emoji-panel"); Assert.NotNull(panel);
        Assert.True(panel!.IsVisible, "picker must be open after the trigger");
        PumpLayout(window);
        return emojiButton;
    }

    /// <summary>Scrolls to a noninitial row and returns a real visible emoji cell on it plus its
    /// exact authored Unicode sequence, ready for a pointer click.</summary>
    private static (Button Target, string Expected) PickNonInitialTarget(
        ChatComposerCuiView view, Window window)
    {
        var viewport = FindContainer(view, "emoji-grid-viewport"); Assert.NotNull(viewport);
        var scroller = viewport!.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        Assert.NotNull(scroller);
        if (scroller!.Extent.Height > scroller.Viewport.Height)
        {
            scroller.Offset = new Vector(0, Math.Min(60, scroller.Extent.Height - scroller.Viewport.Height));
            PumpLayout(window);
        }
        var cells = EmojiCells(viewport);
        Assert.True(cells.Length >= 2, "need a noninitial cell to click");
        var clip = Assert.Single(scroller.GetVisualDescendants().OfType<ScrollContentPresenter>());
        var clipBounds = new Rect(clip.Bounds.Size);
        var target = cells.Skip(1).LastOrDefault(button => button.IsEffectivelyVisible
            && clipBounds.Contains(new Rect(OriginIn(button, clip), button.Bounds.Size)));
        Assert.NotNull(target);
        var expected = Assert.IsType<string>(target!.Content);
        return (target!, expected);
    }


    [AvaloniaFact]
    public async Task Picker_opens_a_real_multi_column_wrap_grid_inside_a_bounded_scroll_viewport()
    {
        var source = new DraftInsertingSource { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var trigger = OpenPicker(view, window);

            Assert.True(view.Bindings.TryGetValue("Composer.EmojiFontFamily", out var emojiFontValue));
            var emojiFontFamily = Assert.IsType<string>(emojiFontValue);
            Assert.Equal(emojiFontFamily, trigger.FontFamily?.Name);

            var viewport = FindContainer(view, "emoji-grid-viewport");
            Assert.NotNull(viewport);

            // The emitted cells form a real multi-column grid occupying multiple rows.
            var glyphs = EmojiCells(viewport!);
            Assert.True(glyphs.Length >= 6, "viewport must realize a bounded grid of emoji cells");
            var unicodeGlyph = glyphs[0];
            var glyphText = Assert.IsType<string>(unicodeGlyph.Content);
            Assert.Contains(EmojiCatalogue.Entries, entry => entry.Text == glyphText);
            Assert.Equal(emojiFontFamily, unicodeGlyph.FontFamily?.Name);
            var origins = glyphs.Select(button => OriginIn(button, viewport!)).ToArray();
            var rows = origins.Select(origin => origin.Y).Distinct().ToArray();
            Assert.True(rows.Length >= 2, "grid cells must occupy multiple rows");
            var cols = origins.Take(6).Select(origin => origin.X).Distinct().Count();
            Assert.True(cols >= 2, "grid cells must occupy multiple columns");

            // A genuine scroll viewport: bounded Viewport with content extent taller than the viewport.
            var scroller = viewport.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            Assert.NotNull(scroller);
            Assert.True(scroller!.Viewport.Height > 0, "scroll viewport must have a positive bounded size");
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height,
                "grid content must overflow the viewport so rolling reaches noninitial rows");

            // Real scrolling reaches a noninitial row: scroll down and confirm offset/content change.
            var firstRowY = OriginIn(glyphs[0], viewport).Y;
            scroller.Offset = new Vector(0, Math.Min(60, scroller.Extent.Height - scroller.Viewport.Height));
            PumpLayout(window);
            Assert.True(scroller.Offset.Y > 0, "wheel/scroll must move the viewport");
            Assert.True(OriginIn(glyphs[0], viewport).Y < firstRowY,
                "scrolling must move the authored cells in viewport coordinates");
            Assert.True(scroller.Viewport.Height > 0);

            // The composer input identity, focus and draft survive picker open/scroll.
            var box = FindInput(view); Assert.NotNull(box);
            Assert.Equal("", box!.Text);
            Assert.True(box.IsKeyboardFocusWithin || view.Input.IsFocused);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { await FinishComposerTestAsync(window, view, testFailure); }
    }

    [AvaloniaFact]
    public async Task Realized_count_stays_bounded_while_full_catalogue_paging_reaches_first_last_and_flags()
    {
        var source = new DraftInsertingSource { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            OpenPicker(view, window);

            var viewport = FindContainer(view, "emoji-grid-viewport"); Assert.NotNull(viewport);
            var realized = EmojiCells(viewport!);
            Assert.True(realized.Length <= EmojiPageSize,
                $"realized emoji buttons must stay bounded (<= {EmojiPageSize}), got {realized.Length}");
            Assert.True(realized.Length >= 1, "first window must realize emoji cells");

            // Paging through the REAL Next button actions (routed through the runtime QueryViewport)
            // reaches the final page while keeping realized cells bounded on every page.
            var next = FindButton(view, "emoji-next"); Assert.NotNull(next);
            var guard = 0;
            while (next!.IsVisible && guard < 200) { next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); guard++; }
            Assert.True(guard < 200, "paging must terminate after the final page");
            Assert.False(next.IsVisible, "final page must hide Next");
            PumpLayout(window);

            realized = EmojiCells(viewport);
            Assert.True(realized.Length <= EmojiPageSize,
                "final page must still realize a bounded window");
            Assert.True(realized.Length >= 1, "final page must realize cells");

            // The numeric total label reports the truthful full-catalogue total on the final page.
            var totalLabel = FindLabel(view, "emoji-total");
            Assert.NotNull(totalLabel);
            Assert.EndsWith(EmojiCatalogue.Entries.Count.ToString(CultureInfo.InvariantCulture),
                totalLabel!.Text, StringComparison.Ordinal);

            // The final page's realized glyphs are genuine catalogue entries (complete reach).
            var catalogueGlyphs = EmojiCatalogue.Entries.Select(e => e.Text).ToHashSet(StringComparer.Ordinal);
            Assert.True(realized.All(b => b.Content is string g && catalogueGlyphs.Contains(g)),
                "every realized cell must be a real catalogue sequence");

            // Flags remain reachable through the REAL search input (typed two-way into the runtime
            // QueryViewport): full-catalogue filtering, never the trigger-only icon-pool allowlist.
            var search = FindInput(view, "emoji-search"); Assert.NotNull(search);
            search!.Text = "flag"; PumpLayout(window);
            var flagButtons = EmojiCells(viewport);
            var flagSet = ComposerEmojiCatalogueQuery.Evaluate("flag", null).Items
                .Select(e => ((EmojiEntry)e).Text).ToHashSet(StringComparer.Ordinal);
            Assert.True(flagButtons.Any(b => b.Content is string s && flagSet.Contains(s)),
                "flags must be reachable through real picker search");

            // A trigger-blacklisted/suggestive sequence also stays reachable in the picker.
            search.Text = "peach"; PumpLayout(window);
            var peachButtons = EmojiCells(viewport);
            Assert.True(peachButtons.Any(b => b.Content is string p && p.Contains("\U0001F351", StringComparison.Ordinal)),
                "trigger-excluded sequences must remain in the picker/search");

            // Clearing search restores the full catalogue again (first window realizes again).
            search.Text = ""; PumpLayout(window);
            Assert.True(EmojiCells(viewport).Any(),
                "clearing search must restore catalogue cells");
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { await FinishComposerTestAsync(window, view, testFailure); }
    }

    [AvaloniaFact]
    public async Task Pointer_click_on_noninitial_emoji_inserts_exact_unicode_once_and_preserves_draft_identity_focus()
    {
        var source = new DraftInsertingSource { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var box = FindInput(view); Assert.NotNull(box);
            view.Input.Text = "alpha\nbeta";
            // Place the caret inside the first line (before the LF) so insertion must preserve both
            // the leading alpha and the trailing LF + beta content.
            view.Input.SetSelection(5, 5);
            source.InsertCaret = 5;
            box!.Focus(); Pump();

            OpenPicker(view, window);
            var viewport = FindContainer(view, "emoji-grid-viewport"); Assert.NotNull(viewport);

            // Scroll to a noninitial row, then choose a visible cell on that row.
            var scroller = viewport!.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            Assert.NotNull(scroller);
            if (scroller!.Extent.Height > scroller.Viewport.Height)
            {
                scroller.Offset = new Vector(0, Math.Min(60, scroller.Extent.Height - scroller.Viewport.Height));
                PumpLayout(window);
            }
            var cells = EmojiCells(viewport);
            Assert.True(cells.Length >= 2, "need a noninitial cell to click");
            var clip = Assert.Single(scroller.GetVisualDescendants().OfType<ScrollContentPresenter>());
            var clipBounds = new Rect(clip.Bounds.Size);
            var target = cells.Skip(1).LastOrDefault(button => button.IsEffectivelyVisible
                && clipBounds.Contains(new Rect(OriginIn(button, clip), button.Bounds.Size)));
            Assert.NotNull(target);
            var expected = Assert.IsType<string>(target!.Content);
            var origin = OriginIn(target, window);
            var center = origin + new Vector(target.Bounds.Width / 2, target.Bounds.Height / 2);
            window.MouseMove(center);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            PumpLayout(window);
            await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(1, source.InsertCount);
            Assert.Equal(expected, source.LastInserted);
            Assert.Equal($"alpha{expected}\nbeta", source.Draft);
            Assert.Equal(5, source.InsertCaret);

            // The same real input survives; insertion dispatched through the real input port/editor.
            var boxAfter = FindInput(view); Assert.NotNull(boxAfter);
            Assert.Same(box, boxAfter);
            Assert.Equal($"alpha{expected}\nbeta", boxAfter!.Text);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { await FinishComposerTestAsync(window, view, testFailure); }
    }

    [AvaloniaFact]
    public async Task Held_emoji_insertion_stays_pending_until_editor_reflects_exact_unicode()
    {
        // The original action must remain genuinely pending through BOTH the real source insertion
        // and the required UI projection: the retained editor reflects the exact Unicode only when
        // the held insertion is released, and settlement completes only after that projection.
        var source = new HeldEmojiSource { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        Task? pipeline = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var box = FindInput(view); Assert.NotNull(box);
            view.Input.Text = "alpha\nbeta";
            view.Input.SetSelection(5, 5);
            source.InsertCaret = 5;
            box!.Focus(); Pump();

            OpenPicker(view, window);
            var (target, expected) = PickNonInitialTarget(view, window);

            var origin = OriginIn(target, window);
            var center = origin + new Vector(target.Bounds.Width / 2, target.Bounds.Height / 2);
            window.MouseMove(center);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            PumpLayout(window);

            // The genuine source operation is admitted once and held.
            Assert.Equal(1, source.InsertCount);
            Assert.Equal(expected, source.LastInserted);
            Assert.True(source.InsertPending);

            // The actual loader action snapshot is genuinely pending while the insertion AND the
            // required editor projection are held.
            pipeline = view.CaptureOriginalActionSettlement();
            Assert.False(pipeline.IsCompleted);

            // The retained editor has NOT yet reflected the inserted sequence.
            Assert.Equal("alpha\nbeta", box.Text);
            Assert.Equal("alpha\nbeta", source.Draft);

            // Release the genuine insertion; only then must the editor reflect the exact Unicode and
            // the original action settle.
            await Task.Run(() => source.Release());
            await pipeline.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(source.InsertPending);
            Assert.Equal(1, source.InsertCount); // exactly once
            Assert.Equal($"alpha{expected}\nbeta", source.Draft);
            var boxAfter = FindInput(view); Assert.NotNull(boxAfter);
            Assert.Same(box, boxAfter);
            Assert.Equal($"alpha{expected}\nbeta", boxAfter!.Text);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure, release: source.Release,
                additionalOriginals: pipeline is null ? null : [pipeline]);
        }
    }

    [AvaloniaFact]
    public async Task Faulted_emoji_insertion_settles_real_fault_without_projecting_stale_draft()
    {
        // A real insertion failure must remain observable through the original action settlement, and
        // the picker-closing projection must NOT publish an uncommitted/stale draft into the editor.
        var source = new HeldEmojiSource { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        Task? pipeline = null;
        var failure = new IOException("emoji source offline");
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var box = FindInput(view); Assert.NotNull(box);
            view.Input.Text = "alpha\nbeta";
            view.Input.SetSelection(5, 5);
            source.InsertCaret = 5;
            box!.Focus(); Pump();

            OpenPicker(view, window);
            var (target, _) = PickNonInitialTarget(view, window);

            var origin = OriginIn(target, window);
            var center = origin + new Vector(target.Bounds.Width / 2, target.Bounds.Height / 2);
            window.MouseMove(center);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            PumpLayout(window);

            Assert.True(source.InsertPending);
            pipeline = view.CaptureOriginalActionSettlement();
            Assert.False(pipeline.IsCompleted);

            await Task.Run(() => source.Fault(failure));
            var pipelineError = await Record.ExceptionAsync(() => pipeline.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(pipelineError);
            Assert.Contains(failure, AllCauses(pipelineError!));

            // The failed insertion must NOT project any uncommitted edit into the retained editor.
            Assert.Equal("alpha\nbeta", box.Text);
            var boxAfter = FindInput(view); Assert.NotNull(boxAfter);
            Assert.Same(box, boxAfter);
            Assert.Equal("alpha\nbeta", boxAfter!.Text);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure, release: source.Release,
                additionalOriginals: pipeline is null ? null : [pipeline], expectedCause: failure);
        }
    }

    [AvaloniaFact]
    public async Task Held_emoji_insertion_joins_close_and_is_refused_after_sealing()
    {
        // An already-admitted held insertion must remain genuinely joined through close/retirement:
        // close stays pending until the real source releases, and after sealing no late edit reaches
        // the retained editor. The original source/host lifetime owns the whole flow.
        var source = new HeldEmojiSource { CanSend = true };
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

            var box = FindInput(view); Assert.NotNull(box);
            view.Input.Text = "alpha\nbeta";
            view.Input.SetSelection(5, 5);
            source.InsertCaret = 5;
            box!.Focus(); Pump();

            OpenPicker(view, window);
            var (target, expected) = PickNonInitialTarget(view, window);

            var origin = OriginIn(target, window);
            var center = origin + new Vector(target.Bounds.Width / 2, target.Bounds.Height / 2);
            window.MouseMove(center);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            PumpLayout(window);

            Assert.True(source.InsertPending);
            pipeline = view.CaptureOriginalActionSettlement();
            Assert.False(pipeline.IsCompleted);

            // Close while the genuine insertion is held: the already-admitted action stays joined
            // through retirement, and close remains genuinely pending until the source releases.
            close = view.CloseAsync();
            Assert.False(close.IsCompleted);

            // After sealing, no late write reaches the retained editor.
            view.Input.Text = "late write";
            Assert.Equal("alpha\nbeta", box.Text);

            await Task.Run(() => source.Release());
            await pipeline.WaitAsync(TimeSpan.FromSeconds(10));
            await close.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(source.InsertPending);
            Assert.Equal(1, source.InsertCount);
            Assert.Equal($"alpha{expected}\nbeta", source.Draft);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally
        {
            await FinishComposerTestAsync(window, view, testFailure, release: source.Release,
                additionalOriginals: pipeline is null || close is null ? null : [pipeline, close]);
        }
    }

    [AvaloniaFact]
    public async Task Icon_tools_and_primary_action_render_44px_hit_targets_with_centered_svg_glyphs()
    {
        // Every icon tool and the primary action share one consistent 44x44 hit target, while the
        // authored glyph extent is smaller and centered (icon extent and hit target are distinct).
        var source = new DraftInsertingSource { Draft = "Send this message", CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            foreach (var id in new[] { "add-button", "settings-button", "permissions-button", "send-button" })
            {
                var button = FindButton(view, id);
                Assert.NotNull(button);
                Assert.True(button!.Bounds.Width >= 44 && button.Bounds.Height >= 44,
                    $"{id} must expose a 44px hit target, got {button.Bounds}");

                var image = Assert.IsType<Image>(button.Content);
                Assert.IsType<DrawingImage>(image.Source); // genuine packaged-SVG decode
                var toButton = image.TransformToVisual(button);
                Assert.NotNull(toButton);
                var imageInButton = new Rect(image.Bounds.Size).TransformToAABB(toButton!.Value);
                var buttonLocal = new Rect(button.Bounds.Size);
                Assert.True(buttonLocal.Contains(imageInButton), $"{id} glyph {imageInButton} clipped outside hit target {buttonLocal}");
                Assert.True(Math.Abs(imageInButton.Center.X - buttonLocal.Center.X) < 1d
                    && Math.Abs(imageInButton.Center.Y - buttonLocal.Center.Y) < 1d,
                    $"{id} glyph must be centered in its hit target");
            }
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { await FinishComposerTestAsync(window, view, testFailure); }
    }

    private sealed class CompactThemeSource(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "composer-compact-component",
            false, CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }

    [AvaloniaTheory]
    [InlineData("Glow")]
    [InlineData("Professional")]
    public async Task Compact_composer_keeps_input_and_real_action_shelf_reachable_at_400_pixels(string theme)
    {
        var token = TestContext.Current.CancellationToken;
        using var context = new CuiThemePreferenceContext(new CompactThemeSource(theme));
        var source = new DraftInsertingSource();
        var view = new ChatComposerCuiView(source, "app.spaces", context)
        {
            // The retained 400px Spaces footer has 16px workspace +20px transcript inset per side.
            Margin = new Thickness(36, 0, 36, 16),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom
        };
        var outside = new Border { Child = view, Focusable = true };
        var window = new Window { Width = 400, Height = 650, Content = outside };
        Exception? testFailure = null;
        try
        {
            window.Show();
            await view.InitializeAsync(token).WaitAsync(TimeSpan.FromSeconds(10), token);
            outside.Focus(); PumpLayout(window);
            var input = FindInput(view); Assert.NotNull(input);
            var originalInput = input!;
            var surface = Authored<Control>("composer-surface");
            var shelf = Authored<CuiActionShelf>("conversation-actions");
            Assert.True(input.TryFindResource("CuiEffectiveThemeName", out var actualTheme));
            Assert.Equal(theme, actualTheme);
            Assert.True(input.TryFindResource("CuiBackgroundBrush", out var backdrop));
            window.Background = Assert.IsAssignableFrom<IBrush>(backdrop);

            AssertCompactEmpty();
            Capture("empty");

            // Keyboard submission still addresses the SAME editable input and actual source.
            view.Input.Text = "Keep this original draft";
            originalInput.Focus(); view.Input.SetSelection(5, 9); PumpLayout(window);
            Assert.Same(originalInput, FindInput(view));
            Assert.True(originalInput.IsFocused);
            Assert.Equal(5, originalInput.SelectionStart); Assert.Equal(9, originalInput.SelectionEnd);
            foreach (var id in new[] { "add-button", "format-button", "emoji-button", "settings-button", "permissions-button", "send-button" })
                AssertFullyWithin(Authored<Button>(id), surface);
            AssertFullyWithin(originalInput, surface);
            Assert.True(originalInput.Bounds.Width >= 280, "An expanded narrow composer must give the retained input its full row.");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(1, source.SendCount);
            Assert.Equal("Keep this original draft", source.Draft);

            // Changing geometry must preserve the shelf's chosen real command, draft and caret.
            source.CanEnterVoice = true; view.RefreshSurface(); PumpLayout(window);
            Authored<Button>("action-shelf-next").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10), token);
            view.Input.SetSelection(5, 9); originalInput.Focus();
            window.Width = 720; PumpLayout(window);
            window.Width = 400; PumpLayout(window);
            Assert.Same(shelf, Authored<CuiActionShelf>("conversation-actions"));
            Assert.Same(originalInput, FindInput(view));
            Assert.Equal("Voice, 2 of 2", AutomationProperties.GetName(Authored<Button>("send-button")));
            Assert.Equal("Keep this original draft", source.Draft);
            Assert.True(originalInput.IsFocused);
            Assert.Equal(5, originalInput.SelectionStart); Assert.Equal(9, originalInput.SelectionEnd);
            AssertFullyWithin(Authored<Button>("send-button"), surface);
            Capture("expanded");

            view.Input.Text = string.Empty; outside.Focus(); view.RefreshSurface(); PumpLayout(window);
            Assert.False(originalInput.IsFocused);
            Assert.False(Authored<Button>("format-button").IsEffectivelyVisible);
            Assert.True(shelf.IsEffectivelyVisible);
            Assert.Equal("Voice, 1 of 1", AutomationProperties.GetName(Authored<Button>("send-button")));
            AssertFullyWithin(originalInput, surface);
            AssertFullyWithin(Authored<Button>("send-button"), surface);
            Assert.True(originalInput.Bounds.Width >= 280, "An idle Voice action must not squeeze the narrow placeholder.");

            // Options freezes focus expansion, not viewport geometry. A wide, unfocused Voice
            // session must still gain a full input row when its SAME popup owner is resized narrow.
            window.Width = 720; PumpLayout(window);
            Assert.False(originalInput.IsFocused);
            Assert.True(originalInput.Bounds.Width < surface.Bounds.Width - 100);
            Authored<Button>("settings-button").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10), token);
            PumpLayout(window);
            var options = view.OptionsPopup; Assert.NotNull(options);
            Assert.True(options!.IsOpen);
            var optionsBody = Assert.IsAssignableFrom<Control>(options.Child);
            window.Width = 400; PumpLayout(window);
            Assert.Same(options, view.OptionsPopup); Assert.Same(optionsBody, options.Child);
            Assert.True(options.IsOpen);
            Assert.Same(originalInput, FindInput(view));
            Assert.Equal(string.Empty, source.Draft);
            Assert.False(originalInput.IsFocused);
            Assert.True(originalInput.Bounds.Width >= 280, "Resizing with Options open must retain the narrow full input row.");
            AssertFullyWithin(originalInput, surface);
            AssertFullyWithin(Authored<Button>("send-button"), surface);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); PumpLayout(window);
            await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(options.IsOpen);
            outside.Focus(); PumpLayout(window);
            Capture("voice");

            source.CanEnterVoice = false; source.IsSending = true;
            view.Input.Text = "Keep this while stopping"; view.RefreshSurface(); PumpLayout(window);
            var stop = Authored<Button>("send-button");
            Assert.Equal("Cancel response, 1 of 2", AutomationProperties.GetName(stop));
            AssertFullyWithin(stop, surface); AssertFullyWithin(originalInput, surface);
            Capture("stop");
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(1, source.StopCount);
            Assert.Equal("Keep this while stopping", source.Draft);
            source.IsSending = false; view.Input.Text = string.Empty;
            outside.Focus(); view.RefreshSurface(); PumpLayout(window);
            AssertCompactEmpty();
            Assert.Same(shelf, Authored<CuiActionShelf>("conversation-actions"));
            Assert.Same(originalInput, FindInput(view));

            void AssertCompactEmpty()
            {
                Assert.False(originalInput.IsFocused);
                Assert.False(shelf.IsEffectivelyVisible);
                Assert.InRange(surface.Bounds.Height, 44, 72);
                Assert.True(originalInput.Bounds.Width >= 140);
                var placeholder = Assert.Single(originalInput.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == source.Placeholder);
                Assert.True(placeholder.IsEffectivelyVisible);
                Assert.Single(placeholder.TextLayout.TextLines);
                AssertFullyWithin(originalInput, surface);
                foreach (var id in new[] { "add-button", "settings-button", "permissions-button" })
                    AssertFullyWithin(Authored<Button>(id), surface);
            }

            T Authored<T>(string id) where T : Control => Assert.IsAssignableFrom<T>(
                Assert.Single(AuthoredCuiControls.Enumerate(view), control => AutomationProperties.GetAutomationId(control) == id));

            void AssertFullyWithin(Control control, Control owner)
            {
                var origin = control.TranslatePoint(default, owner); Assert.NotNull(origin);
                Assert.True(control.IsEffectivelyVisible);
                Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
                var bounds = new Rect(origin!.Value, control.Bounds.Size);
                Assert.True(bounds.Left >= -.5 && bounds.Top >= -.5 &&
                    bounds.Right <= owner.Bounds.Width + .5 && bounds.Bottom <= owner.Bounds.Height + .5,
                    $"{AutomationProperties.GetAutomationId(control)} {bounds} must fit the actual composer {owner.Bounds.Size}.");
                var inWindow = control.TranslatePoint(default, window); Assert.NotNull(inWindow);
                Assert.True(inWindow!.Value.X >= 0 && inWindow.Value.X + control.Bounds.Width <= window.Width + .5);
                Assert.True(inWindow.Value.Y >= 0 && inWindow.Value.Y + control.Bounds.Height <= window.Height + .5);
            }

            void Capture(string state)
            {
                if (Environment.GetEnvironmentVariable("CUI_EVIDENCE_DIRECTORY") is not { Length: > 0 } directory) return;
                Directory.CreateDirectory(directory);
                using var capture = window.CaptureRenderedFrame(); Assert.NotNull(capture);
                capture.Save(Path.Combine(directory, $"composer-compact-{state}-{theme}-400x650.png"));
            }
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { await FinishComposerTestAsync(window, view, testFailure); }
    }

    [AvaloniaFact]
    public async Task Completed_options_write_never_reenables_another_conversations_pending_save()
    {
        var source = new DraftInsertingSource { ActionIdentity = "first" };
        var bindings = new ChatComposerBindings(source);
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        await bindings.DispatchAsync("composer.settings", null);
        await bindings.DispatchAsync("composer.set-action-mode", "AllowAllActions");
        source.ActionWriteReceipt = first.Task;
        var firstSave = bindings.DispatchAsync("composer.save-options", null).AsTask();
        source.ActionIdentity = "second";
        bindings.RefreshSource();
        await bindings.DispatchAsync("composer.settings", null);
        await bindings.DispatchAsync("composer.set-action-mode", "JustChat");
        source.ActionWriteReceipt = second.Task;
        var secondSave = bindings.DispatchAsync("composer.save-options", null).AsTask();

        first.SetResult();
        await firstSave;
        Assert.False(bindings.IsActionAvailable("composer.save-options"));
        Assert.True(bindings.TryGetValue("Composer.ShowSettings", out var open));
        Assert.Equal(true, open);
        Assert.Equal(2, source.ActionModeWrites);
        second.SetResult();
        await secondSave;
        Assert.Equal(ChatActionMode.JustChat, source.ActionMode);
        Assert.True(bindings.TryGetValue("Composer.ShowSettings", out open));
        Assert.Equal(false, open);
    }

    [AvaloniaFact]
    public async Task Options_floats_over_the_retained_conversation_and_only_Save_applies_allowed_actions()
    {
        var chip = new ChatComposerChip("existing-file", "Revision notes", false);
        var source = new DraftInsertingSource { Draft = "Keep this draft", Chips = [chip] };
        var view = new ChatComposerCuiView(source);
        var transcript = new ScrollViewer { Content = new Border { Height = 2000 } };
        var grid = new Grid { RowDefinitions = new("*,Auto") };
        grid.Children.Add(transcript);
        grid.Children.Add(view);
        Grid.SetRow(view, 1);
        var window = new Window { Width = 900, Height = 700, Content = grid };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            window.Activate();
            var input = Assert.IsType<TextBox>(FindInput(view));
            view.Input.SetSelection(2, 7);
            transcript.Offset = new Vector(0, 165);
            var offset = transcript.Offset;
            var settings = Assert.IsType<Button>(FindButton(view, "settings-button"));
            Assert.Equal("Options", AutomationProperties.GetName(settings));
            Assert.IsType<DrawingImage>(Assert.IsType<Image>(settings.Content).Source);
            var composerBounds = view.Bounds;
            void Click(Button button)
            {
                Assert.True(button.IsEffectivelyEnabled);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpLayout(window);
            }
            Control Open()
            {
                Click(settings);
                Assert.True(view.OptionsPopup?.IsOpen);
                var opened = Assert.IsAssignableFrom<Control>(view.OptionsPopup!.Child);
                Click(Assert.Single(opened.GetVisualDescendants().OfType<Button>(), control =>
                    AutomationProperties.GetAutomationId(control) == "options-permissions-toggle"));
                return opened;
            }
            static Button Choice(Control panel, string label) =>
                panel.GetVisualDescendants().OfType<Button>().Single(control => Equals(control.Content, label));
            var panel = Open();
            var typography = new (string Label, FontWeight Weight)[]
            {
                ("Options", (FontWeight)750),
                ("Response", (FontWeight)750),
                ("Presentation", (FontWeight)600),
                ("Allowed actions", (FontWeight)750),
                ("This conversation", (FontWeight)600),
                ("Applies to future responses in this chat.", (FontWeight)600),
                ("Save to change which actions this conversation can request.", (FontWeight)600)
            };
            foreach (var (label, weight) in typography)
            {
                var text = panel.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == label);
                Assert.Equal(weight, text.FontWeight);
            }
            var panelTop = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(panel));
            var close = panel.GetVisualDescendants().OfType<Button>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "options-close");
            Assert.True(close.Focus());
            var visited = new HashSet<Control>();
            for (var index = 0; index < 24; index++)
            {
                var focused = Assert.IsAssignableFrom<Control>(panelTop.FocusManager?.GetFocusedElement());
                Assert.True(ReferenceEquals(focused, panel) || panel.IsVisualAncestorOf(focused));
                if (!visited.Add(focused)) break;
                panelTop.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                panelTop.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                PumpLayout(window);
            }
            Assert.True(visited.Count >= 4);
            Assert.Same(close, panelTop.FocusManager?.GetFocusedElement());
            Assert.Equal(composerBounds, view.Bounds);
            var presentation = Assert.Single(AuthoredCuiControls.Enumerate(panel).OfType<MenuItem>(), control =>
                AutomationProperties.GetAutomationId(control) == "options-presentation");
            presentation.IsSubMenuOpen = true; PumpLayout(window);
            var preferVisual = Assert.Single(presentation.Items.OfType<MenuItem>(), control => Equals(control.Header, "Prefer visual"));
            Assert.True(preferVisual.IsEffectivelyEnabled);
            preferVisual.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10));
            presentation.IsSubMenuOpen = false; PumpLayout(window);
            Assert.Equal(GenerativeUiResponseMode.PreferVisual, source.ResponseMode);
            Assert.True(view.OptionsPopup?.IsOpen);
            Click(Choice(panel, "All actions"));
            Assert.Equal(0, source.ActionModeWrites);
            Click(Choice(panel, "Cancel"));
            Assert.Null(view.OptionsPopup);
            Assert.Equal(0, source.ActionModeWrites);
            Assert.Same(settings, window.FocusManager?.GetFocusedElement());

            panel = Open();
            Click(Choice(panel, "All actions"));
            Choice(panel, "All actions").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            PumpLayout(window);
            Assert.Null(view.OptionsPopup);
            Assert.Equal(0, source.ActionModeWrites);

            panel = Open();
            Click(Choice(panel, "All actions"));
            window.MouseDown(new Point(4, 4), MouseButton.Left);
            window.MouseUp(new Point(4, 4), MouseButton.Left);
            PumpLayout(window);
            Assert.Null(view.OptionsPopup);
            Assert.Equal(0, source.ActionModeWrites);
            panel = Open();
            Click(Choice(panel, "All actions"));
            source.ActionIdentity = Guid.NewGuid();
            view.RefreshSurface();
            PumpLayout(window);
            Assert.Null(view.OptionsPopup);
            Assert.Equal(0, source.ActionModeWrites);

            panel = Open();
            Click(Choice(panel, "Just chat"));
            Click(Choice(panel, "Save permissions"));
            Assert.Equal(ChatActionMode.JustChat, source.ActionMode);
            Assert.Equal(1, source.ActionModeWrites);
            Assert.Null(view.OptionsPopup);
            Assert.Same(settings, window.FocusManager?.GetFocusedElement());
            Assert.Same(input, FindInput(view));
            Assert.Equal("Keep this draft", view.Input.Text);
            Assert.Equal(2, view.Input.SelectionStart);
            Assert.Equal(5, view.Input.SelectionLength);
            Assert.Same(chip, Assert.Single(source.Chips));
            Assert.Equal(offset, transcript.Offset);
            Assert.Equal(0, source.SendCount);

            window.Width = 365;
            PumpLayout(window);
            panel = Open();
            var activePopup = view.OptionsPopup!;
            Assert.InRange(panel.Bounds.Width, 1, window.ClientSize.Width - 23);
            Click(Choice(panel, "All actions"));
            await view.CloseAsync();
            Assert.False(activePopup.IsOpen);
            Assert.Null(view.OptionsPopup);
            Assert.Equal(1, source.ActionModeWrites);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { await FinishComposerTestAsync(window, view, testFailure); }
    }

    [AvaloniaFact]
    public async Task Primary_send_action_accessibility_reflects_real_operation_and_availability()
    {
        var source = new DraftInsertingSource { Draft = "Send this message", CanSend = true, IsEditing = false };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var send = FindButton(view, "send-button");
            Assert.NotNull(send);
            Assert.True(send!.IsEnabled, "send must be enabled while CanSend");
            Assert.Equal("Send, 1 of 1", send.GetValue(AutomationProperties.NameProperty)?.ToString());

            // Edit state is the REAL current operation: the shelf announcement names the same
            // contextual primary action and its current position.
            source.IsEditing = true;
            view.RefreshSurface(); Pump();
            Assert.Equal("Update message, 1 of 1", send.GetValue(AutomationProperties.NameProperty)?.ToString());

            // Ineligible actions leave the shelf. The send slot does not remain as an inert button.
            source.CanSend = false;
            view.RefreshSurface(); Pump();
            Assert.False(send.IsVisible, "send must leave the shelf while drafting is not sendable");

            source.CanSend = true;
            view.RefreshSurface(); Pump();
            Assert.True(send.IsVisible);
            Assert.True(send.IsEnabled);
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, source.SendCount);
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { await FinishComposerTestAsync(window, view, testFailure); }
    }

    [AvaloniaFact]
    public async Task Aa_and_emoji_triggers_are_chromeless_when_unselected_and_selected_when_open()
    {
        var source = new DraftInsertingSource { CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var box = FindInput(view); Assert.NotNull(box);
            box!.Focus(); Pump(); // expanded formatting shows Aa (emoji trigger

            var format = FindButton(view, "format-button"); Assert.NotNull(format);
            var emoji = FindButton(view, "emoji-button"); Assert.NotNull(emoji);

            // Unselected: both triggers are plain/chromeless (no always-filled disk).
            Assert.True(IsEffectivelyTransparent(format!.Background), "Aa must be chromeless when unselected");
            Assert.True(IsEffectivelyTransparent(emoji!.Background), "emoji must be chromeless when unselected");

            // Opening Aa selects it (visible selected affordance) and reveals the floating toolbar.
            format.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Assert.False(IsEffectivelyTransparent(format.Background), "Aa must show a selected affordance while open");
            Assert.NotNull(FindContainer(view, "format-toolbar"));
            format.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Assert.True(IsEffectivelyTransparent(format.Background), "Aa returns to chromeless when closed");

            // Opening the emoji picker selects the trigger; closing returns it to chromeless.
            emoji.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Assert.False(IsEffectivelyTransparent(emoji.Background), "emoji must show a selected affordance while open");
            emoji.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Assert.True(IsEffectivelyTransparent(emoji.Background), "emoji returns to chromeless when closed");
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { await FinishComposerTestAsync(window, view, testFailure); }
    }

    [AvaloniaFact]
    public async Task Icon_tools_and_primary_action_are_keyboard_focus_reachable()
    {
        // The authored chrome must not suppress keyboard focus: each tool participates in focus.
        var source = new DraftInsertingSource { Draft = "Send this message", CanSend = true };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Exception? testFailure = null;
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);
            var box = FindInput(view); Assert.NotNull(box);
            box!.Focus(); Pump(); // expand so Aa/emoji tools are live
            foreach (var id in new[] { "add-button", "format-button", "settings-button", "send-button" })
            {
                var button = FindButton(view, id);
                Assert.NotNull(button);
                button!.Focus(); Pump();
                Assert.True(button.IsKeyboardFocusWithin, $"{id} must be keyboard-focus reachable");
            }
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { await FinishComposerTestAsync(window, view, testFailure); }
    }

    private static Rect WindowBounds(Control control, TopLevel top)
    {
        var origin = control.TranslatePoint(default, top) ?? default;
        return new Rect(origin.X, origin.Y, control.Bounds.Width, control.Bounds.Height);
    }

    private static bool IsEffectivelyTransparent(IBrush? brush)
    {
        if (brush is null) return true;
        if (brush.Opacity <= 0d) return true;
        return brush is ISolidColorBrush solid && solid.Color.A == 0;
    }
}
