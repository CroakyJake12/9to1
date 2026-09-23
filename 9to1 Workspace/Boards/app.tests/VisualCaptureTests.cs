// Headless visual capture: renders real fixture boards through the production
// Boards.cui + BlockRenderer stack and saves PNG screenshots for inspection.
// Also asserts the render pipeline completes without exceptions.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

public sealed class VisualCaptureTests
{
    internal static string ShotsDirectory()
    {
        var dir = Environment.GetEnvironmentVariable("BOARDS_SHOTS");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(Path.GetTempPath(), "opencode", "boards-shots");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindBoardsCui()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (var i = 0; i < 12 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "9to1 Workspace", "Boards", "app", "Boards.cui");
            if (File.Exists(candidate))
                return candidate;
            var direct = Path.Combine(dir.FullName, "Boards.cui");
            if (File.Exists(direct))
                return direct;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Boards.cui was not found from the test output directory.");
    }

    private static string Capture(string boardPath, int width, int height, string shotName, bool dark = false) =>
        // Funnelled through the shared test UI thread like every other suite.
        TestUiThread.Run(() => CaptureOnUiThread(boardPath, width, height, shotName, dark));

    private static string CaptureOnUiThread(string boardPath, int width, int height, string shotName, bool dark)
    {
        var priorMode = BoardsTheme.Mode;
        BoardsTheme.SetMode(dark ? BoardsThemeMode.Dark : BoardsThemeMode.Light);
        if (Avalonia.Application.Current is not null)
            Avalonia.Application.Current.RequestedThemeVariant = dark
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;
        try
        {
        var storeRoot = Path.Combine(Path.GetTempPath(), "boards-shots-store", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storeRoot);
        var store = new JsonFileHavenBoardStore(storeRoot);
        var adapter = ContractSessionAdapter.OpenAsync(store, boardPath).GetAwaiter().GetResult();
        try
        {
            var viewModel = new BoardsViewModel(adapter);

            var loader = new CuiControlLoader();
            loader.SetBindingContext(viewModel);
            loader.SetActionDispatcher(viewModel);
            var (root, diagnostics) = loader.LoadFile(FindBoardsCui());
            Assert.True(diagnostics.Count == 0, "CUI diagnostics: " + string.Join("; ", diagnostics));
            Assert.NotNull(root);
            loader.WireBindings(root);
            viewModel.Attach(root);

            var host = FindByAutomationId<StackPanel>(root, "BlocksHost");
            Assert.NotNull(host);
            BlockRenderer.RebuildAsync(host, viewModel).GetAwaiter().GetResult();
            FillCombo(root, "StyleBox", viewModel.StyleList.Select(s => s.Name).ToList());
            FillCombo(root, "AddKindBox", ["Paragraph", "Checklist", "Table", "Graph", "Image"]);
            // Mirror production shell wiring so shots show the real chrome.
            ThemeApplier.ApplyChrome(root);
            var left = FindByAutomationId<StackPanel>(root, "TopBarLeft");
            var right = FindByAutomationId<StackPanel>(root, "TopBarRight");
            if (left is not null && right is not null)
                ShellChrome.BuildTopBar(left, right, viewModel, onFileMenu: () => { }, onOverflow: () => { }, onTheme: () => { },
                    onSaveState: state => state.Text = viewModel.Get("SaveStateText")?.ToString() ?? "Saved");
            var nav = FindByAutomationId<StackPanel>(root, "NavHost");
            if (nav is not null)
                NavBuilder.Rebuild(nav, viewModel);
            var toolbar = FindByAutomationId<StackPanel>(root, "ToolbarHost");
            if (toolbar is not null)
                ToolbarBuilder.Rebuild(toolbar, viewModel);
            var context = FindByAutomationId<StackPanel>(root, "ContextHost");
            if (context is not null)
                ContextPanels.Rebuild(context, viewModel);

        var window = new Window
        {
            Width = width,
            Height = height,
            Content = root,
            Background = Avalonia.Media.Brushes.White,
        };
        window.Show();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        var pageCard = FindByAutomationId<Border>(root, "PageCard");
        Assert.NotNull(pageCard);
        Assert.True(root.Bounds.Width >= width - 1,
            $"The CUI root only measured {root.Bounds.Width}px of the {width}px capture.");
        var navPane = FindByAutomationId<Border>(root, "NavPane");
        Assert.NotNull(navPane);
        var editorScroll = FindByAutomationId<ScrollViewer>(root, "EditorScroll");
        Assert.NotNull(editorScroll);
        Assert.True(editorScroll.Bounds.Width > width * 0.70,
            $"Editor viewport was {editorScroll.Bounds.Width}px wide (root={root.Bounds}, nav={navPane.Bounds}).");
        var widthDifference = Math.Abs(pageCard.Bounds.Width - editorScroll.Viewport.Width);
        Assert.True(widthDifference <= 1,
            $"The seamless page width {pageCard.Bounds.Width} did not fill its editor viewport {editorScroll.Viewport.Width}.");
        Assert.True(pageCard.Bounds.Height >= 400, $"PageCard layout height was only {pageCard.Bounds.Height}.");
        Assert.Equal(new Thickness(0), pageCard.Margin);
        Assert.Equal(new Thickness(0), pageCard.Padding);
        Assert.Equal(new Thickness(0), pageCard.BorderThickness);
        Assert.Equal(new CornerRadius(0), pageCard.CornerRadius);
        var contentLayer = FindByAutomationId<StackPanel>(root, "DocumentContentLayer");
        Assert.NotNull(contentLayer);
        Assert.Equal(new Thickness(40, 32), contentLayer.Margin);
        Assert.Equal(CuiTheme.Glow, BoardsTheme.SharedPalette.Theme);
        object? accent = null;
        Assert.True(Avalonia.Application.Current?.TryGetResource("CuiAccentBrush", null, out accent) == true);
        Assert.IsType<Avalonia.Media.LinearGradientBrush>(accent);
        var topBar = FindByAutomationId<Border>(root, "TopBar");
        Assert.NotNull(topBar);
        Assert.Same(accent, topBar.BorderBrush);
        Assert.NotEmpty(host.Children);

        var pixelSize = new PixelSize(width, height);
        var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(root);
        var shotPath = Path.Combine(ShotsDirectory(), shotName);
        bitmap.Save(shotPath);
        window.Close();
        return shotPath;
        }
        finally
        {
            adapter.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        }
        finally
        {
            BoardsTheme.SetMode(priorMode);
            if (Avalonia.Application.Current is not null)
                Avalonia.Application.Current.RequestedThemeVariant =
                    Avalonia.Styling.ThemeVariant.Light;
        }
    }

    private static void FillCombo(Control root, string automationId, IReadOnlyList<string> items)
    {
        var combo = FindByAutomationId<ComboBox>(root, automationId);
        if (combo is null)
            return;
        combo.ItemsSource = items.Select(text => new ComboBoxItem { Content = text }).ToList();
        if (items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private static T? FindByAutomationId<T>(Control root, string automationId) where T : Control
    {
        if (root is T match && string.Equals(
                Avalonia.Automation.AutomationProperties.GetAutomationId(root), automationId, StringComparison.Ordinal))
            return match;
        foreach (var child in LogicalChildren(root))
        {
            var found = FindByAutomationId<T>(child, automationId);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static IEnumerable<Control> LogicalChildren(Control control)
    {
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control c)
                    yield return c;
        }
        else if (control is Decorator decorator && decorator.Child is Control decChild)
        {
            yield return decChild;
        }
        else if (control is ContentControl cc && cc.Content is Control ccChild)
        {
            yield return ccChild;
        }
        else if (control is ItemsControl ic)
        {
            foreach (var item in ic.Items)
                if (item is Control icChild)
                    yield return icChild;
        }
        else if (control is ScrollViewer scroller && scroller.Content is Control scChild)
        {
            yield return scChild;
        }
    }

    private static async Task<string> FixtureDirAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "boards-visual-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await VisualFixtures.BuildMathsAsync(dir);
        await VisualFixtures.BuildLawAsync(dir);
        await VisualFixtures.BuildCsAsync(dir);
        return dir;
    }

    [Fact]
    public async Task Capture_maths_1440x900()
    {
        var dir = await FixtureDirAsync();
        var shot = Capture(Path.Combine(dir, "A-Level Maths.9to1board"), 1440, 900, "maths-1440x900.png");
        Assert.True(File.Exists(shot));
        Assert.True(new FileInfo(shot).Length > 4096);
    }

    [Fact]
    public async Task Capture_law_1440x900()
    {
        var dir = await FixtureDirAsync();
        var shot = Capture(Path.Combine(dir, "A-Level Law.9to1board"), 1440, 900, "law-1440x900.png");
        Assert.True(File.Exists(shot));
        Assert.True(new FileInfo(shot).Length > 4096);
    }

    [Fact]
    public async Task Capture_cs_1440x900()
    {
        var dir = await FixtureDirAsync();
        var shot = Capture(Path.Combine(dir, "A-Level Computer Science.9to1board"), 1440, 900, "cs-1440x900.png");
        Assert.True(File.Exists(shot));
        Assert.True(new FileInfo(shot).Length > 4096);
    }

    [Fact]
    public async Task Capture_maths_1280x720()
    {
        var dir = await FixtureDirAsync();
        var shot = Capture(Path.Combine(dir, "A-Level Maths.9to1board"), 1280, 720, "maths-1280x720.png");
        Assert.True(File.Exists(shot));
        Assert.True(new FileInfo(shot).Length > 4096);
    }

    [Fact]
    public async Task Capture_maths_1920x1080()
    {
        var dir = await FixtureDirAsync();
        var shot = Capture(Path.Combine(dir, "A-Level Maths.9to1board"), 1920, 1080, "maths-1920x1080.png");
        Assert.True(File.Exists(shot));
        Assert.True(new FileInfo(shot).Length > 4096);
    }

    [Fact]
    public async Task Capture_maths_dark_1440x900()
    {
        var dir = await FixtureDirAsync();
        var shot = Capture(Path.Combine(dir, "A-Level Maths.9to1board"), 1440, 900, "maths-dark-1440x900.png", dark: true);
        Assert.True(File.Exists(shot));
        Assert.True(new FileInfo(shot).Length > 4096);
    }
}
