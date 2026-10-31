using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsLineSpacingTests
{
    [Fact]
    public async Task Style_catalogue_projects_the_persisted_canonical_line_spacing()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-style-spacing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "spacing.9to1board");
            await using (var owner = await RichBoardSession.CreateNewAsync(store, "Style spacing"))
            {
                await owner.MutateAsync(rich => rich.Styles.Single(style => style.Id == "normal").LineSpacing = 1.75);
                await owner.SaveAsAsync(path);
            }
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Equal(1.75, adapter.Styles.Single(style => style.Id == "normal").LineSpacing);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public Task Spacing_changes_the_actual_layout_and_caret_without_replacing_the_editor() => Run((window, input, block, style) =>
    {
        var presenter = Presenter(input);
        input.Focus(); input.CaretIndex = 3;
        var normal = presenter.TextLayout.TextLines[0].Height;
        input.LineHeight = double.NaN; Pump(window);
        var natural = presenter.TextLayout.TextLines[0].Height;
        Assert.True(normal >= natural - .001, "1x spacing must not clip the font's natural line box.");

        block.LineSpacing = 2;
        BlockRenderer.UpdateTextPresentation(input, block, style); Pump(window);
        Assert.Same(presenter, Presenter(input));
        Assert.True(input.IsFocused); Assert.Equal(3, input.CaretIndex);
        Assert.InRange(presenter.TextLayout.TextLines[0].Height, natural * 2 - .001, natural * 2 + .001);
        var first = presenter.TextLayout.HitTestTextPosition(0);
        var second = presenter.TextLayout.HitTestTextPosition(3);
        Assert.InRange(second.Y - first.Y, natural * 2 - .001, natural * 2 + .001);
        Assert.Equal(input.LineHeight, presenter.LineHeight);
    });

    [Fact]
    public Task Enlarging_a_formatted_run_recomputes_line_and_caret_spacing() => Run((window, input, block, style) =>
    {
        block.LineSpacing = 1.5;
        block.Runs = [new HavenRichTextRun { Text = block.Text, FontSize = 14 }];
        BlockRenderer.UpdateTextPresentation(input, block, style); Pump(window);
        var presenter = Presenter(input);
        var before = presenter.TextLayout.TextLines[0].Height;
        block.Runs[0].FontSize = 28;
        BlockRenderer.UpdateTextPresentation(input, block, style); Pump(window);
        var after = presenter.TextLayout.TextLines[0].Height;
        Assert.InRange(after, before * 2 - .001, before * 2 + .001);
        var first = presenter.TextLayout.HitTestTextPosition(0);
        var second = presenter.TextLayout.HitTestTextPosition(3);
        Assert.InRange(second.Y - first.Y, after - .001, after + .001);
        Assert.Equal(input.LineHeight, presenter.LineHeight);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public Task Unset_or_invalid_block_spacing_uses_the_named_style(double spacing) => Run((window, input, block, style) =>
    {
        input.LineHeight = double.NaN; Pump(window);
        var natural = Presenter(input).TextLayout.TextLines[0].Height;
        block.LineSpacing = spacing; style.LineSpacing = 1.5;
        BlockRenderer.UpdateTextPresentation(input, block, style); Pump(window);
        Assert.InRange(Presenter(input).TextLayout.TextLines[0].Height, natural * 1.5 - .001, natural * 1.5 + .001);
        style.LineSpacing = 0;
        BlockRenderer.UpdateTextPresentation(input, block, style); Pump(window);
        Assert.InRange(Presenter(input).TextLayout.TextLines[0].Height, natural - .001, natural + .001);
    });

    private static CuiFormattedTextPresenter Presenter(TextBox input) =>
        Assert.Single(input.GetVisualDescendants().OfType<CuiFormattedTextPresenter>());

    private static void Pump(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static async Task Run(Action<Window, TextBox, RichBoardBlock, RichStyleView> check)
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/line-spacing");
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            var page = session.Document.Sections[0].Pages[0];
            var block = new RichBoardBlock { Id = "spacing", Text = "Ag\nAg", FontSize = 14, LineSpacing = 1 };
            page.Blocks = [block];
            var style = vm.StyleList.Single(item => item.Id == "normal");
            var host = new StackPanel(); BlockRenderer.Rebuild(host, vm, []);
            var window = new Window { Content = host, Width = 700, Height = 500 };
            try
            {
                window.Show(); Pump(window);
                check(window, Assert.Single(host.GetVisualDescendants().OfType<TextBox>()), block, style);
            }
            finally { window.Close(); }
        });
    }
}
