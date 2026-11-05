using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// A mounted, measured and drained Boards surface that tests can click on, plus the helpers that read what the
/// owner would actually see on it. Shared by the icon and Trash flow tests: both need real authored elements
/// rather than a view model built in isolation, because the thing under test is often the authored action
/// wiring, which a command-level test cannot reach.
///
/// The CUI runtime only realises authored elements while dispatcher jobs are pumped, so a surface that has not
/// been measured, arranged and drained is not a surface at all - its elements simply are not there.
/// </summary>
internal sealed class BoardsRenderedSurface
{
    private readonly BoardsThemeMode _priorMode;
    private readonly CuiTheme _priorCuiTheme;
    private Window? _host;

    private BoardsRenderedSurface(BoardsWritablePresentation presentation, BoardsThemeMode priorMode,
        CuiTheme priorCuiTheme)
    {
        Presentation = presentation;
        _priorMode = priorMode;
        _priorCuiTheme = priorCuiTheme;
    }

    public BoardsWritablePresentation Presentation { get; }

    public Control Content => Presentation.Content;

    public BoardsViewModel ViewModel => Presentation.ViewModel;

    public static BoardsRenderedSurface Open(string tag)
    {
        var priorMode = TestUiThread.Run(() => BoardsTheme.Mode);
        var priorCuiTheme = TestUiThread.Run(() => CuiSurfacePaletteCatalog.ActiveTheme);
        TestUiThread.Run(() =>
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
            BoardsTheme.SetMode(BoardsThemeMode.Dark);
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        });

        var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenWritableDocumentAsync(
            SmallBoard(), "inline:rendered-" + tag, "rendered-" + tag,
            (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(
                false, null, "no-external-save", "Test surface does not write to the owner's Files.")),
            standaloneChrome: true));
        var presentation = TestPump.Pump(opening, "open presentation");
        var surface = new BoardsRenderedSurface(presentation, priorMode, priorCuiTheme);
        surface._host = TestUiThread.Run(() =>
        {
            var window = new Window { Width = 1440, Height = 900, Content = presentation.Content };
            window.Show();
            window.Measure(new Size(1440, 900));
            window.Arrange(new Rect(0, 0, 1440, 900));
            presentation.Content.Measure(new Size(1440, 900));
            presentation.Content.Arrange(new Rect(0, 0, 1440, 900));
            Dispatcher.UIThread.RunJobs();
            return window;
        });
        TestPump.Pump(presentation.WaitUntilReadyAsync(), "ready");
        TestPump.Pump(presentation.DrainRenderingAsync(), "render");
        return surface;
    }

    public void Close()
    {
        if (_host is not null)
            TestUiThread.Run(_host.Close);
        TestPump.Pump(Presentation.DisposeAsync().AsTask(), "dispose");
        TestUiThread.Run(() =>
        {
            CuiSurfacePaletteCatalog.ActiveTheme = _priorCuiTheme;
            BoardsTheme.SetMode(_priorMode);
        });
    }

    /// <summary>
    /// A board built through the real session, so the surface starts with genuine document state. It carries a
    /// second page and a subpage: a section's final page cannot be trashed by design, and a one-page fixture
    /// would make every destructive-flow test exercise a refusal rather than the flow.
    /// </summary>
    private static byte[] SmallBoard()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-rendered-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new JsonFileHavenBoardStore(root);
        var session = RichBoardSession.CreateNewAsync(store, "Rendered surface").GetAwaiter().GetResult();
        var rich = session.Rich;
        var sectionId = rich.Sections[0].Id;
        var firstPageId = rich.Sections[0].Pages[0].Id;
        HavenRichNotesOps.AddSubpage(rich, sectionId, firstPageId, "Actus reus");
        HavenRichNotesOps.AddPage(rich, sectionId, "Homicide");
        var path = Path.Combine(root, "Rendered surface.9to1board");
        session.SaveAsAsync(path).GetAwaiter().GetResult();
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return File.ReadAllBytes(path);
    }

    /// <summary>An authored element the running surface actually realised, found by its authored ID.</summary>
    public T? Find<T>(string automationId) where T : Control =>
        TestUiThread.Run(() => BoardsCuiControlFinder.FindByAutomationId<T>(Content, automationId));

    public string AccessibleName(Control control) =>
        TestUiThread.Run(() => Avalonia.Automation.AutomationProperties.GetName(control)) ?? string.Empty;

    // Avalonia objects belong to the UI thread, so a test thread may hold a reference to a control found on the
    // surface but may not read its properties. These helpers are how the assertions look at what the owner would
    // actually see: is it shown, is it enabled, what text is in it, and what did the authored reference resolve to.
    public bool Shown(Control control) => TestUiThread.Run(() => control.IsVisible);

    public bool Enabled(Control control) => TestUiThread.Run(() => control.IsEnabled);

    public string TextOf(TextBlock block) => TestUiThread.Run(() => block.Text) ?? string.Empty;

    public object? SourceOf(Image image) => TestUiThread.Run(() => image.Source);

    public Rect BoundsOf(Control control) => TestUiThread.Run(() => control.Bounds);

    /// <summary>
    /// The context menus the navigation tree built, as the header lists each one carries. The row menus are
    /// Avalonia ContextMenus attached to the built rows, so this is what the owner would see on a right click.
    /// </summary>
    public List<IReadOnlyList<string>> NavContextMenus() => TestUiThread.Run(() => Content
        .GetVisualDescendants()
        .OfType<Avalonia.Controls.Primitives.TemplatedControl>()
        .Select(control => control.ContextMenu)
        .Where(menu => menu is not null)
        .Select(menu => (IReadOnlyList<string>)menu!.Items
            .OfType<Avalonia.Controls.MenuItem>()
            .Select(item => item.Header as string ?? string.Empty)
            .ToList())
        .Where(headers => headers.Count > 0)
        .ToList());

    /// <summary>A button in the rendered surface whose accessible name matches, for clicking authored rows.</summary>
    public Button? ButtonWithAccessibleName(string accessibleName) => TestUiThread.Run(() => Content
        .GetVisualDescendants().OfType<Button>()
        .FirstOrDefault(button => string.Equals(Avalonia.Automation.AutomationProperties.GetName(button),
            accessibleName, StringComparison.Ordinal)));

    public bool HasGlyph(CakeOS.Cui.Runtime.CuiMenuItem item) => TestUiThread.Run(() => item.HasIconGlyph);

    public string? GlyphOf(CakeOS.Cui.Runtime.CuiMenuItem item) => TestUiThread.Run(() => item.IconGlyph);

    public bool DrawsText(Control host, string text) => TestUiThread.Run(() => host.GetVisualDescendants()
        .OfType<TextBlock>().Any(block => block.Text == text && block.IsVisible));

    public object? ResolveVector(string reference, Image target) =>
        TestUiThread.Run(() => CakeOS.Apps.Boards.App.BoardsCuiIconResolver.Resolve(reference, target));

    public Point PositionIn(Control control, Control relativeTo) => TestUiThread.Run(() =>
        control.TranslatePoint(new Point(0, 0), relativeTo) ?? new Point(double.NaN, double.NaN));

    /// <summary>The emoji cells the picker currently draws, identified by their CLDR accessible names.</summary>
    public List<Button> EmojiGridButtons() =>
        TestUiThread.Run(() => Content.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => !string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(button)) &&
                Haven.UI.Emoji.EmojiCatalogue.Entries.Any(entry =>
                    entry.Name == Avalonia.Automation.AutomationProperties.GetName(button)))
            .ToList());

    public void Click(Control control)
    {
        TestUiThread.Run(() =>
        {
            // An authored menu row is a MenuItem, which raises its own Click event rather than Button.ClickEvent.
            var routed = control is MenuItem
                ? new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent, control)
                : new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, control);
            control.RaiseEvent(routed);
            Dispatcher.UIThread.RunJobs();
        });
        // The authored header shows its emoji inside an <If> branch, which the runtime rebuilds in a later
        // pass; without draining, the element the owner would see has not been mounted when the assertions run.
        TestPump.Pump(Presentation.DrainRenderingAsync(), "after-click-render");
    }

    public void Pump() => TestPump.Pump(Presentation.DrainRenderingAsync(), "pump");

    public void EnableEdit()
    {
        TestUiThread.Run(() =>
        {
            if (!ViewModel.IsEditMode) ViewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
        });
    }

    public void DisableEdit()
    {
        TestUiThread.Run(() =>
        {
            if (ViewModel.IsEditMode) ViewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
        });
    }
}
