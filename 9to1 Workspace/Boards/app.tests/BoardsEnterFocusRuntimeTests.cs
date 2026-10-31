using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsEnterFocusRuntimeTests
{
    [Fact]
    public Task Enter_keeps_typing_in_the_new_block_after_all_presentation_renders() => Run((presentation, window) =>
    {
        var vm = presentation.ViewModel;
        var input = Editor(window, "second paragraph");
        input.Focus();
        input.CaretIndex = 6;
        // Selecting a different block has already queued a real presentation render.
        // Enter and another invalidation must not consume focus on only the first pass.
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        vm.RequestRebuild();
        vm.RequestRebuild();
        BoardsTabberLifetimeTests.Settle(window, () => vm.PageBlocks.Count == 3);
        BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var next = Editor(window, " paragraph");
        Assert.True(next.IsFocused, "The new editor must retain focus after every queued presentation render.");
        Assert.Equal(0, next.CaretIndex);
        window.KeyTextInput("continued");
        Assert.Equal("continued paragraph", next.Text);
        Assert.Equal("continued paragraph", vm.PageBlocks[2].Text);
        Assert.Equal("second", vm.PageBlocks[1].Text);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Selecting_another_editor_retains_its_range_after_presentation_renders(bool backwards) => Run((presentation, window) =>
    {
        var input = Editor(window, "second paragraph");
        input.Focus();
        input.SelectionStart = backwards ? 8 : 2;
        input.SelectionEnd = backwards ? 2 : 8;
        presentation.ViewModel.RequestRebuild();
        presentation.ViewModel.RequestRebuild();
        BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var current = Editor(window, "second paragraph");
        Assert.True(current.IsFocused);
        Assert.Equal(backwards ? 8 : 2, current.SelectionStart);
        Assert.Equal(backwards ? 2 : 8, current.SelectionEnd);
        window.KeyTextInput("!");
        Assert.Equal("se!aragraph", current.Text);
    });

    private static TextBox Editor(Window window, string text) =>
        window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Text == text);

    private static async Task Run(Action<BoardsWritablePresentation, Window> action)
    {
        TestUiThread.Run(() => { });
        var root = Path.Combine(Path.GetTempPath(), "boards-enter-focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var documents = new BoardsLocalDocuments("enter-focus", root);
            var id = await documents.CreateAsync();
            await using (var seed = await documents.OpenAsync(id))
            {
                var page = seed.Document.Sections[0].Pages[0];
                page.Blocks[0].Text = "first paragraph";
                page.Blocks.Add(new RichBoardBlock { Kind = "paragraph", Text = "second paragraph" });
                seed.MarkDirty(); await seed.SaveAsync();
            }
            var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(documents, id, (_, _) => Task.CompletedTask));
            BoardsTabberInsertionTests.OnUi(() => opening);
            var presentation = await opening;
            Window? window = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1180, Height = 900, Content = presentation.Content };
                    window.Show();
                    BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<TextBox>()
                        .Any(input => input.Text == "second paragraph"));
                    BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
                    action(presentation, window);
                });
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => presentation.DisposeAsync().AsTask());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
