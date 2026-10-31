using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsConversionFocusTests
{
    [Theory]
    [InlineData("bulleted")]
    [InlineData("numbered")]
    [InlineData("checklist")]
    public Task Converting_third_paragraph_selects_and_focuses_its_new_projected_row(string kind) =>
        Run(async (presentation, window, thirdId) =>
        {
            var vm = presentation.ViewModel;
            TestUiThread.Run(() =>
            {
                var original = Editor(window, "third paragraph");
                original.Focus(); original.CaretIndex = 5;
                BoardsTabberInsertionTests.Await(vm.ConvertSelectedKindAsync(kind));
                BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var selected = vm.PageBlocks.Single(block => block.Id == vm.SelectedBlockId);
                Assert.StartsWith(thirdId + ":", selected.Id);
                Assert.Equal(kind, selected.Kind);
                var converted = Editor(window, "third paragraph");
                Assert.StartsWith("chk_", converted.Name ?? string.Empty);
                Assert.True(converted.IsFocused);
                Assert.Equal(5, converted.CaretIndex);
                window.KeyTextInput("!");
                Assert.Equal("third! paragraph", converted.Text);
                Assert.Equal("third! paragraph", vm.PageBlocks.Single(block => block.Id == vm.SelectedBlockId).Text);
                Assert.Equal("first paragraph", vm.PageBlocks[0].Text);
                Assert.Equal("second paragraph", vm.PageBlocks[1].Text);
            });
            await vm.Session.SaveAsync();
        });

    [Fact]
    public Task Delayed_conversion_preserves_an_explicit_new_search_focus() => Run(async (presentation, window, thirdId) =>
    {
        var vm = presentation.ViewModel;
        var adapter = Assert.IsType<ContractSessionAdapter>(vm.Session);
        var gate = (SemaphoreSlim)typeof(ContractSessionAdapter)
            .GetField("_mergeGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(adapter)!;
        await gate.WaitAsync();
        var held = true;
        Task? conversion = null;
        TextBox? search = null;
        try
        {
            TestUiThread.Run(() =>
            {
                var original = Editor(window, "third paragraph");
                original.Focus(); original.CaretIndex = 5;
                conversion = vm.ConvertSelectedKindAsync("bulleted");
                Assert.False(conversion.IsCompleted);
                search = BoardsCuiControlFinder.FindByAutomationId<TextBox>(presentation.Content, "NavSearchBox")
                    ?? throw new InvalidOperationException("The mounted Boards search input is missing.");
                search.Focus();
            });
            gate.Release(); held = false;
            BoardsTabberInsertionTests.OnUi(() => conversion!);
            BoardsTabberInsertionTests.OnUi(presentation.DrainRenderingAsync);
            TestUiThread.Run(() =>
            {
                Assert.True(search!.IsFocused, "The completed conversion must not steal explicit newer focus.");
                Assert.StartsWith(thirdId + ":", vm.SelectedBlockId);
                Assert.Equal("first paragraph", vm.PageBlocks[0].Text);
            });
        }
        finally { if (held) gate.Release(); }
    });

    [Fact]
    public async Task In_memory_conversion_keeps_third_block_focus_and_caret()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/conversion-focus");
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            session.Document.Sections[0].Pages[0].Blocks =
            [
                new RichBoardBlock { Id = "first", Text = "first paragraph" },
                new RichBoardBlock { Id = "second", Text = "second paragraph" },
                new RichBoardBlock { Id = "third", Text = "third paragraph" }
            ];
            var host = new StackPanel(); vm.Attach(host);
            vm.RebuildRequested += () => Dispatcher.UIThread.Post(() => BlockRenderer.Rebuild(host, vm, []));
            BlockRenderer.Rebuild(host, vm, []);
            var window = new Window { Content = host, Width = 800, Height = 600 };
            try
            {
                window.Show();
                var input = Editor(window, "third paragraph"); input.Focus(); input.CaretIndex = 5;
                BoardsTabberInsertionTests.Await(vm.ConvertSelectedKindAsync("bulleted"));
                Dispatcher.UIThread.RunJobs();
                var converted = Editor(window, "third paragraph");
                Assert.Equal("third", vm.SelectedBlockId);
                Assert.True(converted.IsFocused); Assert.Equal(5, converted.CaretIndex);
                window.KeyTextInput("!");
                Assert.Equal("third! paragraph", vm.PageBlocks[2].Text);
            }
            finally { window.Close(); }
        });
    }

    private static TextBox Editor(Window window, string text) =>
        window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Text == text);

    private static async Task Run(Func<BoardsWritablePresentation, Window, string, Task> check)
    {
        TestUiThread.Run(() => { });
        var root = Path.Combine(Path.GetTempPath(), "boards-conversion-focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var documents = new BoardsLocalDocuments("conversion-focus", root);
            var id = await documents.CreateAsync();
            string thirdId;
            await using (var seed = await documents.OpenAsync(id))
            {
                var page = seed.Document.Sections[0].Pages[0];
                page.Blocks[0].Text = "first paragraph";
                page.Blocks.Add(new RichBoardBlock { Text = "second paragraph" });
                var third = new RichBoardBlock { Text = "third paragraph" }; thirdId = third.Id;
                page.Blocks.Add(third); seed.MarkDirty(); await seed.SaveAsync();
            }
            var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(documents, id, (_, _) => Task.CompletedTask));
            BoardsTabberInsertionTests.OnUi(() => opening);
            var presentation = await opening;
            Window? window = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Content = presentation.Content, Width = 1180, Height = 900 }; window.Show();
                    BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<TextBox>()
                        .Any(input => input.Text == "third paragraph"));
                    BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
                });
                await check(presentation, window!, thirdId);
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
