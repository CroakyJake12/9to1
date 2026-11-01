// Regression coverage for the 1 November 2026 native review of the running standalone Boards app
// (BF-12 popover/focus lifecycle, and the v3.1 rule "Preserve commands in responsive overflow and
// keyboard access" together with the shared CUI requirement that focus and selection be visible).
//
// What was observed on the real X11 build, frame by frame:
//   * opening the toolbar Add menu and pressing Down five times *did* move keyboard focus - the search
//     field lost its focus outline, and Return then inserted a paragraph - yet no row ever showed a
//     state, so the row about to be applied could not be seen (frames
//     /tmp/opencode/qwen-native-20261101/70-crop-insert-popover.png and 76-after-kb-enter.png);
//   * every row sat centred under its left-aligned group heading, while the app's own footer Add menu
//     (a real CUI menu) left-aligns its rows: the same kind of list, two alignments.
// The cases below pin the repaired behaviour: the row that is hovered or keyboard-focused shows the
// same selection treatment the toolbar already uses for a chosen control, rows are left-aligned
// through CUI's own property precedence, the keyboard path lands on a visible row, Home/End stay with
// the search field while the caret is in it, and Escape returns focus to the control that opened it.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Threading;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsInsertMenuRowStateTests
{
    [Fact]
    public Task InsertRowsAreLeftAlignedByTheSameCuiPrecedenceThatCentresOrdinaryButtons() => RunAsync(fixture =>
    {
        fixture.OpenMenu();
        var row = fixture.Row("Insert Paragraph");
        var content = Assert.Single(row.GetVisualDescendants().OfType<ContentPresenter>());
        // The shared button resource centres content at Theme level, so the row's own assignment only
        // counts if the effective, template-bound value really is left.
        Assert.Equal(HorizontalAlignment.Left, content.HorizontalContentAlignment);
        Assert.Equal(HorizontalAlignment.Stretch, row.HorizontalAlignment);
        Assert.True(row.Bounds.Width > 60, "the row must fill the menu column so alignment is meaningful");
        // The measured defect was the visible one: the label sat centred in the row while its group
        // heading sat at the left edge, so assert on where the text actually is.
        var text = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(),
            candidate => candidate.Text == "Paragraph");
        Assert.True(text.Bounds.Left - row.Bounds.Left < 24,
            $"a left-aligned row must sit near the column edge, not centred ({text.Bounds.Left - row.Bounds.Left:F1}px in)");
        Assert.True(text.Bounds.Right <= row.Bounds.Right + 1);
    });

    [Fact]
    public Task TheKeyboardFocusedInsertRowShowsTheSelectionTreatmentAndThePreviousRowClearsIt() => RunAsync(fixture =>
    {
        fixture.OpenMenu();
        var first = fixture.Row("Insert Paragraph");
        var second = fixture.Row("Insert Title");
        Assert.Equal(Brushes.Transparent, first.Background);

        Assert.True(first.Focus());
        fixture.Settle();
        Assert.NotSame(Brushes.Transparent, first.Background);
        Assert.Equal(Brushes.Transparent, second.Background);

        Assert.True(second.Focus());
        fixture.Settle();
        Assert.Equal(Brushes.Transparent, first.Background);
        Assert.NotSame(Brushes.Transparent, second.Background);
    });

    [Fact]
    public Task ThePointerOverInsertRowShowsTheSameTreatmentAndClearsWhenThePointerLeaves() => RunAsync(fixture =>
    {
        fixture.OpenMenu();
        var row = fixture.Row("Insert Quote");
        Assert.Equal(Brushes.Transparent, row.Background);

        fixture.Hover(row);
        Assert.NotSame(Brushes.Transparent, row.Background);

        fixture.HoverAway();
        Assert.Equal(Brushes.Transparent, row.Background);
    });

    [Fact]
    public Task DownFromTheSearchFieldLandsOnAVisibleRowAndEnterAppliesThatSameRow() => RunAsync(fixture =>
    {
        fixture.OpenMenu();
        var before = fixture.Session.Document.Sections[0].Pages[0].Blocks.Count;
        var rows = fixture.Rows();
        Assert.True(fixture.Search.IsKeyboardFocusWithin, "opening the menu puts the caret in the search field");

        fixture.Key(Key.Down);
        var landed = Assert.Single(rows, row => row.IsKeyboardFocusWithin);
        Assert.NotSame(Brushes.Transparent, landed.Background);

        fixture.Key(Key.Down);
        var moved = Assert.Single(rows, row => row.IsKeyboardFocusWithin);
        Assert.NotSame(landed, moved);
        Assert.Equal(Brushes.Transparent, landed.Background);
        Assert.NotSame(Brushes.Transparent, moved.Background);

        // Home and End are caret movement while the caret is in the search field, not menu navigation.
        Assert.True(fixture.Search.Focus());
        fixture.Settle();
        Assert.DoesNotContain(rows, row => row.IsKeyboardFocusWithin);
        fixture.Key(Key.Home);
        Assert.True(fixture.Search.IsKeyboardFocusWithin);
        Assert.DoesNotContain(rows, row => row.IsKeyboardFocusWithin);
        fixture.Key(Key.End);
        Assert.True(fixture.Search.IsKeyboardFocusWithin);

        // Enter applies the row the user can see, so the applied type is the chosen type.
        fixture.Key(Key.Down);
        Assert.Single(rows, row => row.IsKeyboardFocusWithin);
        fixture.Key(Key.Enter);
        fixture.Settle();
        Assert.Equal(before + 1, fixture.Session.Document.Sections[0].Pages[0].Blocks.Count);
        Assert.False(fixture.Context.Popup.IsOpen);
    });

    [Fact]
    public Task EscapeClosesTheMenuAndReturnsFocusToTheControlThatOpenedIt() => RunAsync(fixture =>
    {
        fixture.OpenMenu();
        var rows = fixture.Rows();
        Assert.NotEmpty(rows);
        fixture.Key(Key.Down);
        Assert.NotSame(Brushes.Transparent, Assert.Single(rows, row => row.IsKeyboardFocusWithin).Background);
        fixture.Key(Key.Escape);
        fixture.Settle();
        Assert.False(fixture.Context.Popup.IsOpen);
        Assert.True(fixture.Insert.IsFocused);
        Assert.All(rows, row => Assert.Equal(Brushes.Transparent, row.Background));
    });

    private static async Task RunAsync(Action<Fixture> work)
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/insert-row-state-" + Guid.NewGuid().ToString("N"));
        TestUiThread.Run(() =>
        {
            using var fixture = new Fixture(session);
            work(fixture);
        });
    }

    private sealed class Fixture : IDisposable
    {
        internal IRichBoardSession Session { get; }
        internal BoardsViewModel Vm { get; }
        internal Window Window { get; }
        internal Control Insert { get; }
        internal InsertMenuContext Context { get; private set; } = null!;

        internal Fixture(IRichBoardSession session)
        {
            Session = session;
            Vm = new BoardsViewModel(session);
            var host = new StackPanel { Width = 420, Margin = new Thickness(40, 20, 0, 0) };
            Insert = ToolbarBuilder.CreateInsertButton(Vm);
            host.Children.Add(Insert);
            Window = new Window { Width = 800, Height = 600, Content = host };
            Window.Show();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        internal void OpenMenu()
        {
            Context = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(Insert));
            var point = Insert.TranslatePoint(new Point(16, 16), Window)!.Value;
            Window.MouseMove(point);
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            Settle();
            Assert.True(Context.Popup.IsOpen);
        }

        internal Button[] Rows() => Context.Popup.Child!.GetVisualDescendants().OfType<Button>()
            .Where(button => (AutomationProperties.GetName(button) ?? string.Empty)
                .StartsWith("Insert ", StringComparison.Ordinal) &&
                button.IsEffectivelyVisible && button.IsEffectivelyEnabled)
            .ToArray();

        internal Button Row(string automationName) =>
            Assert.Single(Rows(), button => AutomationProperties.GetName(button) == automationName);

        internal TextBox Search => Context.Search;

        internal void Key(Key key)
        {
            Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            Settle();
        }

        internal void Hover(Control control)
        {
            var point = control.TranslatePoint(
                new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;
            Window.MouseMove(point);
            Settle();
        }

        internal void HoverAway()
        {
            Window.MouseMove(new Point(760, 560));
            Settle();
        }

        internal void Settle()
        {
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!Context.WhenActionsIdleAsync().IsCompleted && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Context?.Close();
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Vm.Dispose();
        }
    }
}
