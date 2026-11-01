// Regression coverage for the Boards topology the reference fixes and the carried-forward review items
// name. V3-BOARDS-LAY-01 and the boards-add rule state: "Sidebar is 256 logical units by default, with
// 32-unit rows and 16-unit nesting. Board icon/name/file menu and Undo/Redo stay above the tree";
// "This mock-up defines a 760-unit readable content column, typography and exact spacing"; and the
// corrected v3.2 atlas carries the same numbers in CSS (.board-sidebar{width:256px},
// .board-content{max-width:760px;margin:0 auto}). The chrome labels come from the atlas control row
// (button('Locked layout',...) paired with button('Snap on',...)), and the accent rule
// ("Professional uses per-app accent overrides", with the reference drawing the block "+" in the app
// accent) is what the green "+" against a purple accent violated.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using System.IO;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsTopologyTests
{
    [Theory]
    [InlineData(1200)]
    [InlineData(900)]
    public async Task TheSidebarIsTheReferenceWidthAndTheReadableColumnIsCappedAt760Units(int windowWidth) =>
        await OnMountedSurfaceAsync(windowWidth, (surface, window, _) =>
        {
            var nav = BoardsCuiControlFinder.FindByAutomationId<Control>(surface, "NavPane");
            Assert.NotNull(nav);
            Assert.Equal(256, nav!.Bounds.Width, 0.5);

            var column = BoardsCuiControlFinder.FindByAutomationId<Control>(surface, "DocumentContentLayer");
            var pageSurface = BoardsCuiControlFinder.FindByAutomationId<Control>(surface, "PageSurface");
            Assert.NotNull(column);
            Assert.NotNull(pageSurface);
            var available = pageSurface!.Bounds.Width - 152; // the layer keeps a 76-unit gutter on each side
            var actual = column!.Bounds.Width;
            if (available > 760)
            {
                // The cap must hold, and it must hold by *filling* the capped width: a column that
                // shrink-wraps to its content (147 units was the measured wrong answer) would move the
                // text away from the click target the rest of the editor assumes.
                Assert.True(actual <= 760.5, $"the readable column widened past the reference cap: {actual} of {available}");
                Assert.True(actual >= 759, $"the readable column shrink-wrapped instead of filling the cap: {actual} of {available}");
                Assert.True(column.Bounds.Left >= pageSurface.Bounds.Left - 0.5);
                Assert.True(column.Bounds.Right <= pageSurface.Bounds.Right + 0.5);
            }
            else
            {
                Assert.True(actual is > 0 && actual <= available + 0.5,
                    $"a narrow window must still fit the column inside the surface ({actual} of {available})");
            }
        });

    [Fact]
    public async Task TreeRowsShareOne32UnitPitchAndNestingIndentsByExactly16Units()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        var viewModel = new BoardsViewModel(session);
        var section = session.Document.Sections[0];
        var parent = section.Pages[0];
        var child = new RichBoardPage { Title = "Nested page", ParentPageId = parent.Id };
        section.Pages.Add(child);
        try
        {
            var observed = TestUiThread.Run(() =>
            {
                var nav = new StackPanel();
                NavBuilder.Rebuild(nav, viewModel);
                var window = new Window { Content = nav, Width = 380, Height = 400 };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                try
                {
                    var rows = nav.Children.OfType<Border>().ToArray();
                    var pageRows = rows.Select(row => (Row: row, Grid: row.Child as Grid))
                        .Where(item => item.Grid is not null &&
                            item.Grid.Children.OfType<Button>().Any(button => Grid.GetColumn(button) == 3))
                        .ToArray();
                    return (
                        Count: pageRows.Length,
                        Heights: pageRows.Select(item => item.Grid!.Height).ToArray(),
                        Indents: pageRows.Select(item => item.Row.Margin.Left).ToArray());
                }
                finally { window.Close(); }
            });
            Assert.True(observed.Count >= 2, $"expected a parent and a nested page row, saw {observed.Count}");
            Assert.All(observed.Heights, height => Assert.Equal(32, height, 0.5));
            var shallowest = observed.Indents.Min();
            var deepest = observed.Indents.Max();
            Assert.Equal(16, deepest - shallowest, 0.5);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public async Task TheLayoutControlNamesItsModeTheWayTheReferenceChromeDoes() =>
        await OnMountedSurfaceAsync(1200, (surface, _, viewModel) =>
        {
            var layout = BoardsCuiControlFinder.FindByAutomationId<Button>(surface, "LayoutModeButton");
            Assert.NotNull(layout);
            // A label that only describes the arrangement ("Vertical Stacking") is both longer than the
            // pill and confusable with View mode; the reference states the mode instead.
            Assert.Equal("Locked layout", layout!.Content);
            Assert.Contains("Switch to free placement",
                Avalonia.Automation.AutomationProperties.GetName(layout) ?? string.Empty);

            viewModel.DispatchAsync("ToggleLayoutMode", null).AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Free placement", layout.Content);
            Assert.Contains("Switch to locked layout",
                Avalonia.Automation.AutomationProperties.GetName(layout) ?? string.Empty);
            // The chrome keeps its own state truthful: the same control is what changes it, and nothing
            // else in the row moved.
            var snap = BoardsCuiControlFinder.FindByAutomationId<Button>(surface, "SnapButton");
            Assert.NotNull(snap);
            Assert.Equal("Snap off", snap!.Content);
        });

    [Fact]
    public void TheAddAffordanceFollowsTheAppAccentInsteadOfASuccessGreen()
    {
        // Avalonia objects are thread-affine, so the brushes are resolved on the UI thread and the colour
        // comparisons happen off it.
        var observed = TestUiThread.Run(() =>
        {
            var host = new Button();
            return (
                Plus: Colour(BoardsCuiIconResolver.SemanticBrush("plus", host)),
                Add: Colour(BoardsCuiIconResolver.SemanticBrush("add", host)),
                Insert: Colour(BoardsCuiIconResolver.SemanticBrush("insert", host)),
                Save: Colour(BoardsCuiIconResolver.SemanticBrush("save", host)),
                Accent: Colour(BoardsTheme.AccentBrush));
        });

        Assert.Equal(observed.Plus, observed.Add);
        Assert.Equal(observed.Plus, observed.Insert);
        // The rule is "creation follows the app accent", so the test is stated against whatever the app
        // accent actually is (another suite member switches the theme surface, and a theme whose accent
        // happens to be green must not be reported as a defect here). The carried-forward item - a green
        // block "+" inside a purple-accent app - is caught by the same reading: the plus may not go green
        // while the accent is not green, and the hard-coded success greens below must never stand in for
        // a creation action.
        var drift = Math.Abs(Hue(observed.Plus) - Hue(observed.Accent));
        Assert.True(drift <= 22,
            $"the + hue {Hue(observed.Plus):F0} drifted {drift:F0}° from the app accent {Hue(observed.Accent):F0}° ({observed.Plus} vs {observed.Accent})");
        if (!IsGreen(observed.Accent))
        {
            Assert.False(IsGreen(observed.Plus),
                $"the + glyph went green while the app accent is not green: {observed.Plus} against accent {observed.Accent}");
            foreach (var green in new[] { Color.Parse("#FF238A5A"), Color.Parse("#FF70D5A0") })
                Assert.True(Distance(observed.Plus, green) > 60,
                    $"the + glyph still resolves to the hard-coded success green {green} (got {observed.Plus})");
        }
    }

    private static bool IsGreen(Color colour) => colour.G > colour.R && colour.G > colour.B;

    private static double Hue(Color colour)
    {
        double max = Math.Max(colour.R, Math.Max(colour.G, colour.B));
        double min = Math.Min(colour.R, Math.Min(colour.G, colour.B));
        if (max == min) return 0;
        double delta = max - min;
        double segment = max == colour.R ? ((colour.G - colour.B) / delta) % 6
            : max == colour.G ? (colour.B - colour.R) / delta + 2
            : (colour.R - colour.G) / delta + 4;
        return (segment * 60 + 360) % 360;
    }

    private static Color Colour(IBrush brush) => brush switch
    {
        ISolidColorBrush solid => solid.Color,
        // Another suite member leaves the shared theme surface on a gradient accent (the Glow family),
        // and BoardsTheme.AccentBrush then hands back a LinearGradientBrush. The reference colour for a
        // hue-family comparison is the gradient's own lead stop; SemanticBrush itself always resolves to
        // a solid brush, so the value under test is unaffected by this branch.
        IGradientBrush gradient when gradient.GradientStops.Count > 0 => gradient.GradientStops[0].Color,
        _ => throw new InvalidOperationException($"expected a solid or gradient brush, got {brush.GetType().Name}"),
    };

    private static double Distance(Color left, Color right) => Math.Sqrt(
        (double)(left.R - right.R) * (left.R - right.R) +
        (double)(left.G - right.G) * (left.G - right.G) +
        (double)(left.B - right.B) * (left.B - right.B));

    private static async Task OnMountedSurfaceAsync(int windowWidth, Action<Control, Window, BoardsViewModel> inspect)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-topology-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        BoardsViewModel? viewModel = null;
        Window? window = null;
        CuiControlLoader? loader = null;
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "topology.9to1board"));
            var content = TestUiThread.Run(() =>
            {
                viewModel = new BoardsViewModel(adapter);
                loader = BoardsCuiDocuments.CreateLoader();
                loader.SetBindingContext(viewModel);
                loader.SetActionDispatcher(viewModel);
                var loaded = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
                Assert.DoesNotContain(loaded.Diagnostics, item =>
                    item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
                var rootControl = Assert.IsAssignableFrom<Control>(loaded.Root);
                loader.WireBindings(rootControl);
                viewModel.Attach(rootControl);
                window = new Window { Content = rootControl, Width = windowWidth, Height = 800 };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                return rootControl;
            });
            TestUiThread.Run(() => inspect(content, window!, viewModel!));
        }
        finally
        {
            TestUiThread.Run(() =>
            {
                window?.Close();
                viewModel?.Dispose();
                loader?.Dispose();
                Dispatcher.UIThread.RunJobs();
            });
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
