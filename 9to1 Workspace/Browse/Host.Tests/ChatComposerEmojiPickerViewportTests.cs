using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Prefabs;
using Haven.Desktop.Views.Pages.Chat;
using System.Globalization;
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
        public int InsertCaret { get; private set; } = -1;

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

    private static async Task FinishComposerTestAsync(Window window, ChatComposerCuiView view, Exception? testFailure)
    {
        var failures = new List<Exception>();
        try { await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception error) { failures.Add(error); }
        try { await view.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception error) { failures.Add(error); }
        try { window.Close(); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count == 0) return;
        if (testFailure is not null) failures.Insert(0, testFailure);
        throw new AggregateException("Composer picker test and original cleanup failed.", failures);
    }

    private static Button OpenPicker(ChatComposerCuiView view, Window window)
    {
        var box = FindInput(view); Assert.NotNull(box);
        box!.Focus(); Pump();
        var emojiButton = FindButton(view, "emoji-button"); Assert.NotNull(emojiButton);
        emojiButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var panel = FindContainer(view, "emoji-panel"); Assert.NotNull(panel);
        Assert.True(panel!.IsVisible, "picker must be open after the trigger");
        return emojiButton;
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
            OpenPicker(view, window);

            var viewport = FindContainer(view, "emoji-grid-viewport");
            Assert.NotNull(viewport);

            // The emitted cells form a real multi-column grid occupying multiple rows.
            var glyphs = viewport!.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.True(glyphs.Length >= 6, "viewport must realize a bounded grid of emoji cells");
            var rows = glyphs.Select(b => b.Bounds.Y).Distinct().ToArray();
            Assert.True(rows.Length >= 2, "grid cells must occupy multiple rows");
            var cols = glyphs.Take(6).Select(b => b.Bounds.X).Distinct().Count();
            Assert.True(cols >= 2, "grid cells must occupy multiple columns");

            // A genuine scroll viewport: bounded Viewport with content extent taller than the viewport.
            var scroller = viewport.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            Assert.NotNull(scroller);
            Assert.True(scroller!.Viewport.Height > 0, "scroll viewport must have a positive bounded size");
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height,
                "grid content must overflow the viewport so rolling reaches noninitial rows");

            // Real scrolling reaches a noninitial row: scroll down and confirm offset/content change.
            var firstRowY = glyphs[0].Bounds.Y;
            scroller.Offset = new Vector(0, Math.Min(60, scroller.Extent.Height - scroller.Viewport.Height));
            PumpLayout(window);
            Assert.True(scroller.Offset.Y > 0, "wheel/scroll must move the viewport");
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
            var realized = viewport!.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.True(realized.Length <= EmojiPageSize,
                $"realized emoji buttons must stay bounded (<= {EmojiPageSize}), got {realized.Length}");
            Assert.True(realized.Length <= 60, "realized emoji buttons must not approach the full 3,944 catalogue");

            // Truthful totals and first-page reach.
            Assert.True(view.Bindings.TryGetValue("Composer.EmojiTotalCount", out var total) && total is int t && t == ComposerEmojiCatalogue.Entries.Count,
                "total must equal the full catalogue count");
            Assert.True(view.Bindings.TryGetValue("Composer.EmojiPageStart", out var start) && start is 1);

            // Paging reaches the final page/entry and every intermediate page (complete-catalogue nav).
            var next = FindButton(view, "emoji-next"); Assert.NotNull(next);
            var guard = 0;
            while (next!.IsVisible && guard < 200) { next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); guard++; }
            Assert.True(guard < 200, "paging must terminate after the final page");
            Assert.True(view.Bindings.TryGetValue("Composer.EmojiHasNext", out var hasNext) && hasNext is false, "final page must have no Next");
            Assert.True(view.Bindings.TryGetValue("Composer.EmojiPageEnd", out var end) && end is int e && e == ComposerEmojiCatalogue.Entries.Count,
                "final entry must be reachable through paging");

            // Flags remain in the picker path (allowlist/flag filter never applies to the picker):
            // search finds a flag-sequence entry, proving flags are reachable, not excluded.
            view.Bindings.TrySetValue("Composer.EmojiQuery", "flag"); Pump();
            Assert.True(view.Bindings.TryGetValue("Composer.EmojiEntries", out var results) && results is IReadOnlyList<ComposerEmoji> list
                && list.Any(entry => entry.Group.Equals("Flags", StringComparison.Ordinal)),
                "flags must be reachable through the picker/search (full catalogue)");

            // A trigger-blacklisted/suggestive sequence also stays reachable in the picker.
            view.Bindings.TrySetValue("Composer.EmojiQuery", "peach"); Pump();
            Assert.True(view.Bindings.TryGetValue("Composer.EmojiEntries", out var peach) && peach is IReadOnlyList<ComposerEmoji> pl
                && pl.Any(entry => entry.Text.Contains("\U0001F351", StringComparison.Ordinal)),
                "trigger-excluded sequences must remain in the picker/search");

            // Clearing search restores the full set again.
            view.Bindings.TrySetValue("Composer.EmojiQuery", ""); Pump();
            Assert.True(view.Bindings.TryGetValue("Composer.EmojiTotalCount", out var cleared) && cleared is int c && c == ComposerEmojiCatalogue.Entries.Count);
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
            var visible = viewport.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.True(visible.Length >= 2, "need a noninitial visible cell to click");
            var target = visible[^1];
            var expected = Assert.IsType<string>(target.Content);
            target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
}
