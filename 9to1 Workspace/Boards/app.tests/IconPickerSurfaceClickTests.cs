using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// Drives the emoji grid through the rendered CUI surface rather than through the view model's command
/// dispatcher, which is what the earlier picker tests did. That difference is the point: in the running
/// application clicking an emoji cell changed nothing while Shuffle - the same popover, a parameterless
/// command - stored an emoji and recorded it. A command-level test could not see that, because it never
/// exercised the authored action wiring.
/// </summary>
public sealed class IconPickerSurfaceClickTests
{
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

    [Fact]
    public void Clicking_an_emoji_cell_in_the_rendered_picker_stores_it_on_the_page()
    {
        var priorMode = TestUiThread.Run(() => BoardsTheme.Mode);
        var priorCuiTheme = TestUiThread.Run(() => CuiSurfacePaletteCatalog.ActiveTheme);
        TestUiThread.Run(() =>
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
            BoardsTheme.SetMode(BoardsThemeMode.Dark);
            Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        });

        var bytes = SmallBoard();
        var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenWritableDocumentAsync(
            bytes, "inline:icon-surface", "icon-surface-click",
            (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(
                false, null, "no-external-save", "Test surface does not write to the owner's Files.")),
            standaloneChrome: true));
        var presentation = TestPump.Pump(opening, "open presentation");
        Window? window = null;
        try
        {
            window = TestUiThread.Run(() =>
            {
                var host = new Window { Width = 1440, Height = 900, Content = presentation.Content };
                host.Show();
                host.Measure(new Avalonia.Size(1440, 900));
                host.Arrange(new Avalonia.Rect(0, 0, 1440, 900));
                presentation.Content.Measure(new Avalonia.Size(1440, 900));
                presentation.Content.Arrange(new Avalonia.Rect(0, 0, 1440, 900));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return host;
            });
            TestPump.Pump(presentation.WaitUntilReadyAsync(), "ready");
            TestPump.Pump(presentation.DrainRenderingAsync(), "render");

            var viewModel = presentation.ViewModel;
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

            var emojiButtons = TestUiThread.Run(() => presentation.Content.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => !string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(button)) &&
                    Haven.UI.Emoji.EmojiCatalogue.Entries.Any(entry =>
                        entry.Name == Avalonia.Automation.AutomationProperties.GetName(button)))
                .ToList());
            Assert.NotEmpty(emojiButtons);

            TestUiThread.Run(() =>
            {
                emojiButtons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, emojiButtons[0]));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });

            var after = session.Document.Sections[0].Pages[0];
            Assert.Equal(HavenRichSectionIcons.Emoji, after.IconKey);
            Assert.False(string.IsNullOrEmpty(after.IconEmoji),
                "the clicked cell's emoji should be stored; an empty result means the authored action did not carry the item value");
        }
        finally
        {
            if (window is not null) TestUiThread.Run(window.Close);
            TestPump.Pump(presentation.DisposeAsync().AsTask(), "dispose");
            TestUiThread.Run(() =>
            {
                CuiSurfacePaletteCatalog.ActiveTheme = priorCuiTheme;
                BoardsTheme.SetMode(priorMode);
            });
        }
    }
}
