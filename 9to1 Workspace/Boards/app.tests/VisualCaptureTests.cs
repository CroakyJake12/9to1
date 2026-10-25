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

    private static string Capture(string boardPath, int width, int height, string shotName, bool dark = false,
        bool openStyleMenu = false, bool openSectionIconPicker = false)
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
            return TestUiThread.Run(() => CaptureReadyOnUiThread(presentation, mountedWindow, width, height,
                shotName, openStyleMenu, openSectionIconPicker));
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
        int width, int height, string shotName, bool openStyleMenu, bool openSectionIconPicker)
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

        if (openSectionIconPicker)
        {
            // Keep this capture tied to the real picker state used by the
            // navigation context menu. Reflection lets this focused visual
            // fixture remain source-compatible with older presentation APIs;
            // the method must exist in the integrated picker owner.
            var openPicker = viewModel.GetType().GetMethod("OpenSectionIconPicker");
            Assert.NotNull(openPicker);
            openPicker.Invoke(viewModel, [viewModel.SelectedSectionId]);
            Dispatcher.UIThread.RunJobs();

            var picker = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(root, "SectionIconPickerPanel")
                ?? throw new InvalidOperationException("Missing authored section icon picker panel. Runtime tree:\n" +
                    BoardsCuiControlFinder.DescribeRuntimeTree(root));
            Assert.True(picker.IsVisible, "Opening the selected section's appearance action should show the real picker.");
            var search = BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "SectionIconPickerSearch");
            Assert.NotNull(search);
            var iconChoices = root.GetVisualDescendants().OfType<Button>()
                .Select(button => Avalonia.Automation.AutomationProperties.GetName(button))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
            Assert.Contains("Use Boards section icon", iconChoices);
            Assert.Contains("Use Folder section icon", iconChoices);
            Assert.Contains("Use Violet for this section icon", iconChoices);
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
        Assert.Equal(new Thickness(16, 6, 14, 12), pageCard.Margin);
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
        Assert.Equal(new Thickness(20, 12), pageCard.Padding);
        Assert.Equal(new Thickness(0), pageCard.BorderThickness);
        Assert.True(pageCard.CornerRadius.TopLeft > 0,
            $"The authored PageCard should retain its rounded panel treatment; radius={pageCard.CornerRadius}.");
        var contentLayer = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(root, "DocumentContentLayer")
            ?? throw new InvalidOperationException("Missing authored DocumentContentLayer. Runtime tree:\n" + tree);
        Assert.Equal(new Thickness(76, 10), contentLayer.Margin);
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

        var pageTitle = BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "PageTitleBox")
            ?? throw new InvalidOperationException("Missing authored PageTitleBox. Runtime tree:\n" + tree);
        var firstBlockText = host.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(input => input.Name?.StartsWith("t_", StringComparison.Ordinal) == true)
            ?? throw new InvalidOperationException("The rendered page has no block text editor. Runtime tree:\n" + tree);
        var firstBlockRow = host.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(border => border.Tag is BlockReorderRow)
            ?? throw new InvalidOperationException("The rendered page has no block gutter row. Runtime tree:\n" + tree);
        var gutterGrid = firstBlockRow.Child as Grid
            ?? throw new InvalidOperationException("The block gutter row has no authored grid.");
        var grip = gutterGrid.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "BlockGripToggle");
        var insert = gutterGrid.GetVisualDescendants().OfType<Button>().Single(button =>
            Avalonia.Automation.AutomationProperties.GetName(button) == "Insert");
        var titlePoint = pageTitle.TranslatePoint(new Point(0, 0), pageCard)
            ?? throw new InvalidOperationException("The page title is not in the page card visual tree.");
        var blockTextPoint = firstBlockText.TranslatePoint(new Point(0, 0), pageCard)
            ?? throw new InvalidOperationException("The block text is not in the page card visual tree.");
        var titlePresenter = pageTitle.GetVisualDescendants()
            .OfType<Avalonia.Controls.Presenters.TextPresenter>().FirstOrDefault()
            ?? throw new InvalidOperationException("The page title has no rendered text presenter.");
        var blockTextPresenter = firstBlockText.GetVisualDescendants()
            .OfType<Avalonia.Controls.Presenters.TextPresenter>().FirstOrDefault()
            ?? throw new InvalidOperationException("The first document block has no rendered text presenter.");
        var titleGlyphPoint = titlePresenter.TranslatePoint(new Point(0, 0), pageCard)
            ?? throw new InvalidOperationException("The page title glyphs are not in the page card visual tree.");
        var blockGlyphPoint = blockTextPresenter.TranslatePoint(new Point(0, 0), pageCard)
            ?? throw new InvalidOperationException("The block glyphs are not in the page card visual tree.");
        var gripPoint = grip.TranslatePoint(new Point(0, 0), pageCard)
            ?? throw new InvalidOperationException("The block grip is not in the page card visual tree.");
        var insertPoint = insert.TranslatePoint(new Point(0, 0), pageCard)
            ?? throw new InvalidOperationException("The block insert action is not in the page card visual tree.");
        Assert.True(Math.Abs(blockTextPoint.X - titlePoint.X) <= 1,
            $"Block text should align with the page title; block={blockTextPoint}, title={titlePoint}.");
        Assert.True(Math.Abs(blockGlyphPoint.X - titleGlyphPoint.X) <= 1,
            $"Rendered block glyphs should align with rendered page-title glyphs; block={blockGlyphPoint}, title={titleGlyphPoint}.");
        Assert.Equal(32, grip.Bounds.Width);
        Assert.Equal(32, grip.Bounds.Height);
        Assert.Equal(32, insert.Bounds.Width);
        Assert.Equal(32, insert.Bounds.Height);
        Assert.True(gripPoint.X + grip.Bounds.Width <= insertPoint.X + 0.5,
            $"The 32px grip and insert targets must remain adjacent without overlap; grip={gripPoint}, insert={insertPoint}.");
        Assert.True(gripPoint.X >= 0 && gripPoint.X + grip.Bounds.Width <= titlePoint.X + 1,
            $"The 32px grip target must fit in the margin left of the title; grip={gripPoint}, title={titlePoint}, card={pageCard.Bounds}.");
        Assert.True(insertPoint.X >= 0 && insertPoint.X + insert.Bounds.Width <= titlePoint.X + 1,
            $"The 32px insert target must fit in the margin left of the title; insert={insertPoint}, title={titlePoint}, card={pageCard.Bounds}.");

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

    [Fact]
    public async Task Capture_builtin_section_icon_picker_1440x900()
    {
        var dir = await FixtureDirAsync();
        var shot = Capture(Path.Combine(dir, "A-Level Maths.9to1board"), 1440, 900,
            "section-icon-picker-1440x900.png", dark: true, openSectionIconPicker: true);
        Assert.True(File.Exists(shot));
        Assert.True(new FileInfo(shot).Length > 4096);
    }
}
