using Avalonia.Controls;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// Drives the icon surface through the rendered CUI controls rather than through the view model's command
/// dispatcher, which is what the earlier picker tests did. That difference is the point: in the running
/// application clicking an emoji cell changed nothing while Shuffle - the same popover, a parameterless
/// command - stored an emoji and recorded it, and a command-level test could not see either the failure or
/// the fix, because it never exercised the authored action wiring.
/// </summary>
[Collection(BoardSessionTestCollection.Name)]
public sealed class IconPickerSurfaceClickTests
{
    /// <summary>
    /// A rendered Boards surface, plus the theme settings it was opened over so that closing the surface
    /// restores them. These tests change process-wide CUI state, so the pair has to travel with the surface
    /// rather than sit in a shared field another test could overwrite.
    /// </summary>
    private sealed record Surface(
        BoardsWritablePresentation Presentation, Window Host, BoardsThemeMode PriorMode, CuiTheme PriorCuiTheme);

    private static byte[] SmallBoard()
    {
        var root = Path.Combine(Path.GetTempPath(), "icon-surface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new JsonFileHavenBoardStore(root);
        var session = RichBoardSession.CreateNewAsync(store, "Icon surface").GetAwaiter().GetResult();
        var path = Path.Combine(root, "Icon surface.9to1board");
        session.SaveAsAsync(path).GetAwaiter().GetResult();
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// A mounted Boards surface at a fixed size. The CUI runtime only realises authored elements while
    /// dispatcher jobs are pumped, so everything these tests click has to come from a surface that has been
    /// measured, arranged and drained, not from a view model built in isolation.
    /// </summary>
    private static Surface OpenRenderedSurface(string tag)
    {
        var priorMode = TestUiThread.Run(() => BoardsTheme.Mode);
        var priorCuiTheme = TestUiThread.Run(() => CuiSurfacePaletteCatalog.ActiveTheme);
        TestUiThread.Run(() =>
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
            BoardsTheme.SetMode(BoardsThemeMode.Dark);
            Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        });

        var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenWritableDocumentAsync(
            SmallBoard(), "inline:icon-surface-" + tag, "icon-surface-" + tag,
            (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(
                false, null, "no-external-save", "Test surface does not write to the owner's Files.")),
            standaloneChrome: true));
        var presentation = TestPump.Pump(opening, "open presentation");
        var host = TestUiThread.Run(() =>
        {
            var window = new Window { Width = 1440, Height = 900, Content = presentation.Content };
            window.Show();
            window.Measure(new Avalonia.Size(1440, 900));
            window.Arrange(new Avalonia.Rect(0, 0, 1440, 900));
            presentation.Content.Measure(new Avalonia.Size(1440, 900));
            presentation.Content.Arrange(new Avalonia.Rect(0, 0, 1440, 900));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return window;
        });
        TestPump.Pump(presentation.WaitUntilReadyAsync(), "ready");
        TestPump.Pump(presentation.DrainRenderingAsync(), "render");
        return new Surface(presentation, host, priorMode, priorCuiTheme);
    }

    private static void CloseRenderedSurface(Surface surface)
    {
        TestUiThread.Run(surface.Host.Close);
        TestPump.Pump(surface.Presentation.DisposeAsync().AsTask(), "dispose");
        TestUiThread.Run(() =>
        {
            CuiSurfacePaletteCatalog.ActiveTheme = surface.PriorCuiTheme;
            BoardsTheme.SetMode(surface.PriorMode);
        });
    }

    /// <summary>An authored element the running surface actually realised, found by its authored ID.</summary>
    private static T? Find<T>(Surface surface, string automationId) where T : Control =>
        TestUiThread.Run(() => BoardsCuiControlFinder.FindByAutomationId<T>(surface.Presentation.Content, automationId));

    private static string AccessibleName(Control control) =>
        TestUiThread.Run(() => Avalonia.Automation.AutomationProperties.GetName(control)) ?? string.Empty;

    // Avalonia objects belong to the UI thread, so a test thread may hold a reference to a control found on
    // the surface but may not read its properties. These helpers are how the assertions look at what the
    // owner would actually see: is it shown, is it enabled, and what text is in it.
    private static bool Shown(Control control) => TestUiThread.Run(() => control.IsVisible);

    private static bool Enabled(Control control) => TestUiThread.Run(() => control.IsEnabled);

    private static string TextOf(TextBlock block) => TestUiThread.Run(() => block.Text) ?? string.Empty;

    private static object? SourceOf(Image image) => TestUiThread.Run(() => image.Source);

    /// <summary>The emoji cells the picker currently draws, identified by their CLDR accessible names.</summary>
    private static List<Button> EmojiGridButtons(Surface surface) =>
        TestUiThread.Run(() => surface.Presentation.Content.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => !string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(button)) &&
                Haven.UI.Emoji.EmojiCatalogue.Entries.Any(entry =>
                    entry.Name == Avalonia.Automation.AutomationProperties.GetName(button)))
            .ToList());

    private static void Click(Surface surface, Control control)
    {
        TestUiThread.Run(() =>
        {
            // A authored menu row is a MenuItem, which raises its own Click event rather than Button.ClickEvent.
            var routed = control is Avalonia.Controls.MenuItem
                ? new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent, control)
                : new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, control);
            control.RaiseEvent(routed);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        });
        // The authored header shows its emoji inside an <If> branch, which the runtime rebuilds in a later
        // pass; without draining, the element the owner would see has not been mounted when the assertions run.
        TestPump.Pump(surface.Presentation.DrainRenderingAsync(), "after-click-render");
    }

    [Fact]
    public void Clicking_an_emoji_cell_in_the_rendered_picker_stores_it_on_the_page()
    {
        var surface = OpenRenderedSurface("cell");
        try
        {
            var viewModel = surface.Presentation.ViewModel;
            var session = viewModel.Session;
            var section = session.Document.Sections[0];
            var page = section.Pages[0];

            TestUiThread.Run(() =>
            {
                if (!viewModel.IsEditMode) viewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
                viewModel.OpenPageIconPicker(section.Id, page.Id);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            Assert.True(viewModel.Get("IsSectionIconPickerOpen") as bool?, "the authored picker should be open");

            var emojiButtons = EmojiGridButtons(surface);
            Assert.NotEmpty(emojiButtons);

            Click(surface, emojiButtons[0]);

            var after = session.Document.Sections[0].Pages[0];
            Assert.Equal(HavenRichSectionIcons.Emoji, after.IconKey);
            Assert.False(string.IsNullOrEmpty(after.IconEmoji),
                "the clicked cell's emoji should be stored; an empty result means the authored action did not carry the item value");
        }
        finally
        {
            CloseRenderedSurface(surface);
        }
    }

    /// <summary>
    /// The reference design puts the page's icon to the left of its title, and that slot is where the owner
    /// opens the picker while reading the page they are on. Both halves matter: the slot must exist in the
    /// rendered header (an authored element the runtime never realises is invisible to the owner), and its
    /// activation must target the displayed page, not whichever one the tree happens to highlight.
    /// </summary>
    [Fact]
    public void The_page_header_icon_slot_opens_the_shared_picker_for_the_displayed_page()
    {
        var surface = OpenRenderedSurface("header");
        try
        {
            var viewModel = surface.Presentation.ViewModel;
            var session = viewModel.Session;
            var page = session.Document.Sections[0].Pages[0];

            TestUiThread.Run(() =>
            {
                if (!viewModel.IsEditMode) viewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });

            var slot = Find<Button>(surface, "PageIconButton");
            Assert.NotNull(slot);
            Assert.False(string.IsNullOrWhiteSpace(AccessibleName(slot!)),
                "the header icon slot needs a name that says it is this page's icon");
            Assert.Contains("icon", AccessibleName(slot!), StringComparison.OrdinalIgnoreCase);

            Click(surface, slot!);

            Assert.True(viewModel.Get("IsSectionIconPickerOpen") as bool?,
                "the header slot should open the same shared picker the tree rows use");
            Assert.Equal("page", viewModel.Get("IconPickerTargetKind") as string);

            var emojiButtons = EmojiGridButtons(surface);
            Assert.NotEmpty(emojiButtons);
            Click(surface, emojiButtons[0]);

            var after = session.Document.Sections[0].Pages[0];
            Assert.Equal(HavenRichSectionIcons.Emoji, after.IconKey);
            Assert.False(string.IsNullOrEmpty(after.IconEmoji));

            // Applying an icon through the picker has to update the header it was opened from, otherwise the
            // owner sees no confirmation that the page they are standing on changed.
            Assert.True(viewModel.Get("PageIconIsEmoji") as bool?,
                "the header should switch to the emoji form once an emoji is chosen");
            Assert.Equal(after.IconEmoji, viewModel.Get("PageIconGlyph") as string);
            var glyph = Find<TextBlock>(surface, "PageIconGlyph");
            Assert.NotNull(glyph);
            Assert.True(Shown(glyph!), "the chosen emoji should be drawn in the header");
            Assert.Equal(after.IconEmoji, TextOf(glyph!));
        }
        finally
        {
            CloseRenderedSurface(surface);
        }
    }

    /// <summary>
    /// The icon belongs to the page, not to the editing state, so it stays drawn in View mode; only its
    /// activation is withheld there. This is the same rule the tree rows follow, asserted on the rendered
    /// header rather than in the projection, because the rule is enforced by authored bindings.
    /// </summary>
    [Fact]
    public void The_page_header_icon_stays_displayed_but_inert_outside_edit_mode()
    {
        var surface = OpenRenderedSurface("viewmode");
        try
        {
            var viewModel = surface.Presentation.ViewModel;
            var session = viewModel.Session;
            var section = session.Document.Sections[0];
            var page = section.Pages[0];

            TestUiThread.Run(() =>
            {
                if (!viewModel.IsEditMode) viewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
                viewModel.OpenPageIconPicker(section.Id, page.Id);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            var emojiButtons = EmojiGridButtons(surface);
            Assert.NotEmpty(emojiButtons);
            Click(surface, emojiButtons[0]);

            TestUiThread.Run(() =>
            {
                if (viewModel.IsEditMode) viewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            Assert.False(viewModel.IsEditMode, "the surface should be back in View mode");

            var slot = Find<Button>(surface, "PageIconButton");
            Assert.NotNull(slot);
            Assert.False(Enabled(slot!),
                "View mode must not offer to change the page's icon, but the icon itself stays visible");
            var glyph = Find<TextBlock>(surface, "PageIconGlyph");
            Assert.NotNull(glyph);
            Assert.True(Shown(glyph!), "a chosen icon belongs to the page and is still shown in View mode");
        }
        finally
        {
            CloseRenderedSurface(surface);
        }
    }

    /// <summary>
    /// The Board menu is the entry point for the Board's icon, so it has to exist in the rendered surface and
    /// its row has to reach the shared popover with the Board as target. A stored field with a menu row that
    /// does nothing is exactly what canonical v2 line 9397 forbids.
    /// </summary>
    [Fact]
    public void The_Board_menu_offers_the_icon_action_and_it_targets_the_Board()
    {
        var surface = OpenRenderedSurface("boardmenu");
        try
        {
            var viewModel = surface.Presentation.ViewModel;
            TestUiThread.Run(() =>
            {
                if (!viewModel.IsEditMode) viewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });

            var row = Find<Control>(surface, "BoardIconMenuItem");
            Assert.NotNull(row);
            Assert.True(Enabled(row!), "the Board's icon can be set while a Board is open in Edit mode");
            Assert.Contains("icon", AccessibleName(row!), StringComparison.OrdinalIgnoreCase);

            Click(surface, row!);

            Assert.True(viewModel.Get("IsSectionIconPickerOpen") as bool?,
                "the Board menu row must open the same popover the tree rows and the page header use");
            Assert.Equal("board", viewModel.Get("IconPickerTargetKind") as string);
            Assert.Contains("Board appearance", viewModel.Get("SectionIconPickerTitle") as string);
        }
        finally
        {
            CloseRenderedSurface(surface);
        }
    }

    /// <summary>
    /// The board switcher is where the owner sees which Board they are in, so the Board's own mark belongs
    /// there. An owner-chosen emoji is carried as a text mark - an image cannot hold a colour emoji - and the
    /// mark stays visible in View mode, because it identifies the Board rather than an editing state.
    /// </summary>
    [Fact]
    public void The_board_switcher_shows_the_Board_chosen_mark()
    {
        var surface = OpenRenderedSurface("switcher");
        try
        {
            var viewModel = surface.Presentation.ViewModel;
            TestUiThread.Run(() =>
            {
                if (!viewModel.IsEditMode) viewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
                viewModel.DispatchAsync("OpenBoardIconPicker", null).GetAwaiter().GetResult();
                viewModel.DispatchAsync("ApplyEmojiIconChoice", "📚").GetAwaiter().GetResult();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            Assert.Equal(HavenRichSectionIcons.Emoji, viewModel.Session.Document.IconKey);

            var item = Find<CakeOS.Cui.Runtime.CuiMenuItem>(surface, "BoardSwitcher");
            Assert.NotNull(item);
            Assert.True(TestUiThread.Run(() => item!.HasIconGlyph),
                "the switcher has to carry the mark the Board was given");
            Assert.Equal("📚", TestUiThread.Run(() => item!.IconGlyph));
            Assert.True(TestUiThread.Run(() => item.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text == "📚" && text.IsVisible)), "the mark is drawn, not merely stored");

            // The native pass caught this the way a headless test would not: the first version of the template
            // hid the label whenever a mark existed, so the switcher showed the emoji and lost the Board's
            // name - the one thing that control exists to tell the owner.
            var title = TestUiThread.Run(() => viewModel.Get("BoardMenuTitle") as string);
            Assert.False(string.IsNullOrWhiteSpace(title), "the switcher keeps naming the Board");
            Assert.True(TestUiThread.Run(() => item.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.IsVisible && text.Text == title)),
                "a mark sits beside the Board's name, it does not replace it");

            // View mode keeps the identity mark and only withdraws the editing routes.
            TestUiThread.Run(() =>
            {
                viewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            Assert.False(viewModel.IsEditMode);
            Assert.True(TestUiThread.Run(() => item!.HasIconGlyph),
                "a Board mark belongs to the Board, not to the editing state");
        }
        finally
        {
            CloseRenderedSurface(surface);
        }
    }

    /// <summary>
    /// A page with a vector identity icon publishes a catalog reference (icon:file, or icon:file|#RRGGBBAA
    /// when the owner has tinted it) rather than a media object, because the view model that publishes it can
    /// be built on a background thread while a save or reload is in flight. The reference is only useful if
    /// the surface really turns it into a drawing, so this asserts both halves: what is published, and what
    /// the rendered header holds after resolution.
    /// </summary>
    [Fact]
    public void The_header_vector_icon_publishes_a_catalog_reference_and_resolves_it_on_the_surface()
    {
        var surface = OpenRenderedSurface("vector");
        try
        {
            var viewModel = surface.Presentation.ViewModel;
            var page = viewModel.Session.Document.Sections[0].Pages[0];
            Assert.Equal($"icon:{page.IconKey}", viewModel.Get("PageIconSource") as string);

            var image = Find<Image>(surface, "PageIconImage");
            Assert.NotNull(image);
            Assert.True(Shown(image!), "a page's vector identity icon is shown in the header in both modes");
            Assert.NotNull(SourceOf(image!));

            var tinted = TestUiThread.Run(() => BoardsCuiIconResolver.Resolve("icon:file|#FFFF918A", image!));
            Assert.NotNull(tinted);
            Assert.NotSame(SourceOf(image!), tinted);

            var toolbar = TestUiThread.Run(() => BoardsCuiIconResolver.Resolve("icon:undo", image!));
            Assert.NotNull(toolbar);
        }
        finally
        {
            CloseRenderedSurface(surface);
        }
    }
}
