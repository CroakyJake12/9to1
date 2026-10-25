// Headless visual capture: renders real fixture boards through the production
// Boards.cui + BlockRenderer stack and saves PNG screenshots for inspection.
// Also asserts the render pipeline completes without exceptions.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    private static string Capture(string boardPath, int width, int height, string shotName, bool dark = false, bool openStyleMenu = false) =>
        // Funnelled through the shared test UI thread like every other suite.
        TestUiThread.Run(() => CaptureOnUiThread(boardPath, width, height, shotName, dark, openStyleMenu));

    private static string CaptureOnUiThread(string boardPath, int width, int height, string shotName, bool dark, bool openStyleMenu)
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
        BoardsViewModel? viewModel = null;
        CuiControlLoader? loader = null;
        BoardInkInteractionController? inkInteraction = null;
        BoardsTextAndKeyboardOwner? inputOwner = null;
        Window? window = null;
        try
        {
            viewModel = new BoardsViewModel(adapter);

            loader = new CuiControlLoader(BoardsCuiDocuments.CreateControlRegistry());
            loader.SetBindingContext(viewModel);
            loader.SetActionDispatcher(viewModel);
            var (root, diagnostics) = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
            Assert.True(diagnostics.Count == 0, "CUI diagnostics: " + string.Join("; ", diagnostics));
            Assert.NotNull(root);
            loader.WireBindings(root);
            viewModel.Attach(root);
            ThemeApplier.ApplyChrome(root);

            // Compiled CUI realizes named template hosts only after the root is attached
            // to a top-level and receives layout. Match the production mount lifecycle.
            window = new Window
            {
                Width = width,
                Height = height,
                Content = root,
                Background = BoardsTheme.AppBackgroundBrush,
            };
            window.Show();
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();

            var host = BoardsCuiControlFinder.FindByAutomationId<StackPanel>(root, "BlocksHost");
            Assert.True(host is not null,
                "Compiled CUI did not expose its BlocksHost StackPanel after mount. Runtime tree:\n" +
                BoardsCuiControlFinder.DescribeRuntimeTree(root));
            BlockRenderer.RebuildAsync(host!, viewModel).GetAwaiter().GetResult();
            inputOwner = new BoardsTextAndKeyboardOwner(root, viewModel);
            inputOwner.AttachWindow(window);
            var inkOverlay = BoardsCuiControlFinder.FindByAutomationId<Canvas>(root, "InkOverlayLayer");
            Assert.NotNull(inkOverlay);
            inkInteraction = new BoardInkInteractionController(inkOverlay!, viewModel);
            if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(root, "NavHost") is { } nav)
                NavBuilder.Rebuild(nav, viewModel);
            if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(root, "ToolbarHost") is { } toolbar)
                ToolbarBuilder.Rebuild(toolbar, viewModel);
            inkInteraction.RenderAsync().GetAwaiter().GetResult();
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
        if (openStyleMenu)
        {
            var styleMenu = root.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(combo =>
                Avalonia.Automation.AutomationProperties.GetName(combo) == "Paragraph style");
            Assert.NotNull(styleMenu);
            Assert.Equal(viewModel.StyleList.Count, styleMenu.Items.Count);
            foreach (var name in new[] { "Header 4", "Header 5", "Header 6" })
                Assert.Contains(styleMenu.Items.Cast<ComboBoxItem>(), item =>
                    item.Content is TextBlock preview && preview.Text == name);
            styleMenu.Focus();
            Assert.True(styleMenu.IsFocused, "The style menu must be keyboard-focusable.");
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.True(styleMenu.IsDropDownOpen, "Space should open the focused style menu.");
        }
        var pageCard = BoardsCuiControlFinder.FindByAutomationId<Border>(root, "PageCard");
        Assert.NotNull(pageCard);
        Assert.True(root.Bounds.Width >= width - 1,
            $"The CUI root only measured {root.Bounds.Width}px of the {width}px capture.");
        var navPane = BoardsCuiControlFinder.FindByAutomationId<Border>(root, "NavPane");
        Assert.NotNull(navPane);
        var editorScroll = BoardsCuiControlFinder.FindByAutomationId<ScrollViewer>(root, "EditorScroll");
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
        var contentLayer = BoardsCuiControlFinder.FindByAutomationId<StackPanel>(root, "DocumentContentLayer");
        Assert.NotNull(contentLayer);
        Assert.Equal(new Thickness(40, 32), contentLayer.Margin);
        Assert.Equal(CuiTheme.Glow, BoardsTheme.SharedPalette.Theme);
        object? accent = null;
        Assert.True(Avalonia.Application.Current?.TryGetResource("CuiAccentBrush", null, out accent) == true);
        Assert.IsType<Avalonia.Media.LinearGradientBrush>(accent);
        var topBar = BoardsCuiControlFinder.FindByAutomationId<Border>(root, "TopBar");
        Assert.NotNull(topBar);
        Assert.Same(accent, topBar.BorderBrush);
        Assert.NotEmpty(host.Children);

        var pixelSize = new PixelSize(width, height);
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(window);
        var shotPath = Path.Combine(ShotsDirectory(), shotName);
        bitmap.Save(shotPath);
        if (openStyleMenu)
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape");
            Dispatcher.UIThread.RunJobs();
            var styleMenu = root.GetVisualDescendants().OfType<ComboBox>().Single(combo =>
                Avalonia.Automation.AutomationProperties.GetName(combo) == "Paragraph style");
            Assert.False(styleMenu.IsDropDownOpen, "Escape should close the style menu.");
            Assert.True(styleMenu.IsFocused, "Closing the menu should restore focus to the style control.");
        }
        return shotPath;
        }
        finally
        {
            try
            {
                window?.Close();
                inputOwner?.Dispose();
                if (inputOwner is not null) inputOwner.DrainAsync().GetAwaiter().GetResult();
                inkInteraction?.Dispose();
                if (inkInteraction is not null) inkInteraction.DrainAsync().GetAwaiter().GetResult();

                // TextBox raises TextChanged through Dispatcher jobs. Drain those jobs while
                // the view model's session is still alive, then detach static CUI handlers and
                // durably merge/save any legitimate final edit before disposing the adapter.
                Dispatcher.UIThread.RunJobs();
                if (loader is not null) loader.WhenActionsIdleAsync().GetAwaiter().GetResult();
                viewModel?.Detach();
                loader?.Dispose();
                adapter.SaveAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                adapter.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
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

    [Theory]
    [InlineData(1440, 900, "style-menu-1440x900.png")]
    [InlineData(1280, 720, "style-menu-1280x720.png")]
    public async Task Capture_open_style_menu_at_requested_sizes(int width, int height, string shotName)
    {
        var dir = await FixtureDirAsync();
        var shot = Capture(Path.Combine(dir, "A-Level Maths.9to1board"), width, height, shotName,
            openStyleMenu: true);
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
