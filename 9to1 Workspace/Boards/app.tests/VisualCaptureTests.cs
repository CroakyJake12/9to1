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

    private static string Capture(string boardPath, int width, int height, string shotName, bool dark = false, bool openStyleMenu = false)
    {
        var priorMode = TestUiThread.Run(() => BoardsTheme.Mode);
        var priorCuiTheme = TestUiThread.Run(() => CuiSurfacePaletteCatalog.ActiveTheme);
        TestUiThread.Run(() =>
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
            SetTheme(dark);
        });
        var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenWritableDocumentAsync(
            File.ReadAllBytes(boardPath), boardPath, "visual-capture-r1",
            (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(
                false, null, "capture-save-disabled", "Visual capture does not save fixture edits.")),
            standaloneChrome: true));
        var presentation = PumpUi(opening, "open presentation");
        Window? window = null;
        try
        {
            var mountedWindow = TestUiThread.Run(() => Mount(presentation.Content, width, height));
            window = mountedWindow;
            PumpUi(TestUiThread.Run(() => presentation.WaitUntilReadyAsync()), "surface readiness");
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()), "render drain");
            return TestUiThread.Run(() => CaptureReadyOnUiThread(presentation, mountedWindow, width, height, shotName, openStyleMenu));
        }
        finally
        {
            if (window is not null) TestUiThread.Run(window.Close);
            var disposal = TestUiThread.Run(() => presentation.DisposeAsync().AsTask());
            PumpUi(disposal, "presentation disposal");
            TestUiThread.Run(() =>
            {
                CuiSurfacePaletteCatalog.ActiveTheme = priorCuiTheme;
                SetTheme(priorMode == BoardsThemeMode.Dark);
            });
        }
    }

    private static void SetTheme(bool dark)
    {
        BoardsTheme.SetMode(dark ? BoardsThemeMode.Dark : BoardsThemeMode.Light);
        if (Avalonia.Application.Current is not null)
            Avalonia.Application.Current.RequestedThemeVariant = dark
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;
    }

    private static Window Mount(Control root, int width, int height)
    {
        var window = new Window
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
        return window;
    }

    private static string CaptureReadyOnUiThread(BoardsWritablePresentation presentation, Window window,
        int width, int height, string shotName, bool openStyleMenu)
    {
        var viewModel = presentation.ViewModel;
        var root = presentation.Content;
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        var shotPath = Path.Combine(ShotsDirectory(), shotName);
        void SaveCapture(string path)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            bitmap.Render(window);
            bitmap.Save(path);
        }
        SaveCapture(shotPath);

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
            SaveCapture(Path.Combine(ShotsDirectory(), Path.GetFileNameWithoutExtension(shotName) + "-rendered.png"));
            SaveCapture(shotPath);
        }

        var tree = BoardsCuiControlFinder.DescribeRuntimeTree(root);
        Assert.False(viewModel.IsLanding, "Opening a saved Board must activate its page surface.");
        var pageCard = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(root, "PageCard")
            ?? throw new InvalidOperationException("Missing authored PageCard. Runtime tree:\n" + tree);
        Assert.True(root.Bounds.Width >= width - 1,
            $"The CUI root only measured {root.Bounds.Width}px of the {width}px capture.");
        var navPane = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(root, "NavPane")
            ?? throw new InvalidOperationException("Missing authored NavPane. Runtime tree:\n" + tree);
        var editorScrollHost = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(root, "EditorScroll")
            ?? throw new InvalidOperationException("Missing authored EditorScroll host. Runtime tree:\n" + tree);
        var editorScroll = editorScrollHost.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()
            ?? throw new InvalidOperationException("The authored EditorScroll host did not create its CUI-owned ScrollViewer. Runtime tree:\n" + tree);
        Assert.True(editorScroll.Bounds.Width > width * 0.70,
            $"Editor viewport was {editorScroll.Bounds.Width}px wide (root={root.Bounds}, nav={navPane.Bounds}).");
        var horizontalInset = pageCard.Margin.Left + pageCard.Margin.Right;
        var widthDifference = editorScroll.Viewport.Width - pageCard.Bounds.Width;
        Assert.True(horizontalInset > 0,
            $"The authored PageCard should retain its intentional inset margin; margin={pageCard.Margin}.");
        Assert.InRange(widthDifference, horizontalInset - 1, horizontalInset + 1);
        var pageCardOrigin = pageCard.TranslatePoint(new Point(0, 0), editorScroll);
        Assert.NotNull(pageCardOrigin);
        Assert.True(pageCardOrigin.Value.X >= -1,
            $"The inset page card begins outside the editor viewport; origin={pageCardOrigin.Value}, viewport={editorScroll.Viewport}.");
        Assert.True(pageCardOrigin.Value.X + pageCard.Bounds.Width <= editorScroll.Viewport.Width + 1,
            $"The page card is clipped by the editor viewport; origin={pageCardOrigin.Value}, card={pageCard.Bounds}, viewport={editorScroll.Viewport}.");
        Assert.True(pageCard.Bounds.Height >= 400, $"PageCard layout height was only {pageCard.Bounds.Height}.");
        Assert.True(pageCard.Padding.Left + pageCard.Padding.Right > 0 &&
            pageCard.Padding.Top + pageCard.Padding.Bottom > 0,
            $"The authored PageCard should preserve its inner content inset; padding={pageCard.Padding}.");
        Assert.Equal(new Thickness(0), pageCard.BorderThickness);
        Assert.True(pageCard.CornerRadius.TopLeft > 0,
            $"The authored PageCard should retain its rounded panel treatment; radius={pageCard.CornerRadius}.");
        var contentLayer = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(root, "DocumentContentLayer")
            ?? throw new InvalidOperationException("Missing authored DocumentContentLayer. Runtime tree:\n" + tree);
        Assert.Equal(new Thickness(40, 36), contentLayer.Margin);
        Assert.Equal(CuiTheme.Glow, BoardsTheme.SharedPalette.Theme);
        var topBar = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(root, "TopBar")
            ?? throw new InvalidOperationException("Missing authored standalone TopBar. Runtime tree:\n" + tree);
        object? accent = null;
        Assert.True(Avalonia.Application.Current?.TryGetResource("CuiAccentBrush", null, out accent) == true);
        Assert.IsType<Avalonia.Media.LinearGradientBrush>(accent);
        Assert.Equal(new Thickness(0), topBar.BorderThickness);
        Assert.Null(topBar.BorderBrush);
        var host = BoardsCuiControlFinder.FindByAutomationId<StackPanel>(root, "BlocksHost")
            ?? throw new InvalidOperationException("Missing BlocksHost after the real presentation reached ready state. Runtime tree:\n" + tree);
        Assert.NotEmpty(host.Children);

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

    private static T PumpUi<T>(Task<T> task, string operation) => TestUiThread.Run(() =>
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        if (!task.IsCompleted) throw new TimeoutException($"Boards capture timed out during {operation}.");
        return task.GetAwaiter().GetResult();
    });

    private static void PumpUi(Task task, string operation) => TestUiThread.Run(() =>
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        if (!task.IsCompleted) throw new TimeoutException($"Boards capture timed out during {operation}.");
        task.GetAwaiter().GetResult();
    });

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
