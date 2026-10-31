using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsLosslessConversionMenuTests
{
    [Theory]
    [InlineData("Checklist", "checklist", HavenRichBlockKind.Checklist)]
    [InlineData("Bulleted", "bulleted", HavenRichBlockKind.BulletList)]
    [InlineData("Numbered", "numbered", HavenRichBlockKind.NumberedList)]
    [InlineData("Table", "table", HavenRichBlockKind.Table)]
    [InlineData("Divider", "divider", HavenRichBlockKind.Divider)]
    public async Task Mounted_blank_block_menu_creates_the_real_kind_and_preserves_one_edit_history(
        string menuName, string viewKind, HavenRichBlockKind contractKind)
    {
        await WithConversionAsync(menuName, viewKind, async fixture =>
        {
            var adapter = Assert.IsType<ContractSessionAdapter>(fixture.Presentation.ViewModel.Session);
            var after = adapter.Snapshot;
            await adapter.SaveAsync();
            var saved = (await fixture.Documents.Store.LoadDocumentAtPathAsync(fixture.Path))!
                .RichNotes!.Sections.Single().Pages.Single().Blocks.Single(block => block.Id == "blank-target");
            Assert.Equal(contractKind, saved.Kind);
            Assert.Equal(27, saved.X); Assert.Equal(52, saved.Y);
            Assert.Equal(411, saved.Width); Assert.Equal(124, saved.Height); Assert.Equal(3, saved.ZOrder);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(fixture.Before, adapter.Snapshot);
            Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(after, adapter.Snapshot);
            await adapter.SaveAsync();
            await using var reopened = await fixture.Documents.OpenAsync(fixture.DocumentId);
            Assert.Equal(after, reopened.Snapshot);
        });
    }

    [Theory]
    [InlineData("Checklist", "checklist")]
    [InlineData("Bulleted", "bulleted")]
    [InlineData("Numbered", "numbered")]
    public async Task Mounted_blank_list_conversion_accepts_typing_without_a_second_click(string menuName, string viewKind)
    {
        await WithConversionAsync(menuName, viewKind, async fixture =>
        {
            var vm = fixture.Presentation.ViewModel;
            TestUiThread.Run(() =>
            {
                var input = Assert.IsType<TextBox>(fixture.Window.FocusManager!.GetFocusedElement());
                Assert.StartsWith("chk_blank-target_", input.Name);
                fixture.Window.KeyTextInput("Continued typing");
                Assert.Equal("Continued typing", input.Text);
                Assert.Equal("Continued typing", vm.PageBlocks.Single(block => block.Id == vm.SelectedBlockId).Text);
            });
            await vm.Session.SaveAsync();
            await using var reopened = await fixture.Documents.OpenAsync(fixture.DocumentId);
            var rows = reopened.Document.Sections.Single().Pages.Single().Blocks;
            Assert.Equal("First stays", rows[0].Text); Assert.Equal("Last stays", rows[2].Text);
            Assert.Equal("Continued typing", rows[1].Text); Assert.Equal(viewKind, rows[1].Kind);
        });
    }

    private sealed record Fixture(BoardsLocalDocuments Documents, Guid DocumentId, string Path,
        BoardsWritablePresentation Presentation, Window Window, string Before);

    private static async Task WithConversionAsync(string menuName, string viewKind, Func<Fixture, Task> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-lossless-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var documents = new BoardsLocalDocuments("lossless-menu", root);
            var id = await documents.CreateAsync();
            await using (var seed = await documents.OpenAsync(id))
            {
                var page = seed.Document.Sections.Single().Pages.Single();
                page.Blocks = [new RichBoardBlock { Id = "first", Text = "First stays" },
                    new RichBoardBlock { Id = "blank-target", Text = "", X = 27, Y = 52, Width = 411, Height = 124, ZOrder = 3 },
                    new RichBoardBlock { Id = "last", Text = "Last stays" }];
                seed.MarkDirty(); await seed.SaveAsync();
            }
            var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(documents, id,
                (_, _) => Task.CompletedTask));
            BoardsTabberInsertionTests.OnUi(() => opening);
            var presentation = await opening;
            Window? window = null;
            try
            {
                var adapter = Assert.IsType<ContractSessionAdapter>(presentation.ViewModel.Session);
                var before = adapter.Snapshot;
                var host = TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1180, Height = 900, Content = presentation.Content };
                    window.Show();
                    BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<TextBox>()
                        .Any(input => input.Name == "t_blank-target"));
                    BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
                    var row = window.GetVisualDescendants().OfType<Border>().Single(border =>
                        border.Tag is BlockReorderRow target && target.BlockId == "blank-target");
                    var trigger = row.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "BlockGripToggle");
                    var grip = Assert.IsType<BlockGripCui.BlockActionsHost>(BlockGripCui.HostFor(trigger));
                    trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return grip;
                });
                await DrainAsync(host);
                TestUiThread.Run(() =>
                {
                    Assert.True(host.Popup.IsOpen);
                    var toggle = MenuRow(host, "BlockDesignMenuToggle");
                    Assert.True(toggle.IsEffectivelyVisible); Assert.True(toggle.Focus());
                    Enter(window!);
                });
                await DrainAsync(host);
                TestUiThread.Run(() =>
                {
                    var conversion = MenuRow(host, "BlockTurnInto" + menuName);
                    Assert.True(conversion.IsEffectivelyVisible); Assert.True(conversion.IsEnabled);
                    Assert.True(conversion.Focus()); Enter(window!);
                });
                await DrainAsync(host);
                BoardsTabberInsertionTests.OnUi(presentation.DrainRenderingAsync);
                TestUiThread.Run(() =>
                {
                    window!.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    Assert.False(host.Popup.IsOpen);
                    var vm = presentation.ViewModel;
                    var converted = vm.PageBlocks.Single(block => block.Kind == viewKind);
                    Assert.Equal(converted.Id, vm.SelectedBlockId);
                    Assert.Equal("First stays", vm.PageBlocks[0].Text); Assert.Equal("Last stays", vm.PageBlocks[2].Text);
                    if (viewKind is "checklist" or "bulleted" or "numbered")
                    {
                        Assert.StartsWith("blank-target:", converted.Id);
                        var input = Assert.IsType<TextBox>(window.FocusManager!.GetFocusedElement());
                        Assert.Equal("chk_" + converted.Id.Replace(':', '_'), input.Name);
                    }
                    else
                    {
                        Assert.Equal("blank-target", converted.Id);
                        if (viewKind == "table") Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(),
                            input => input.Name == "cell_blank-target_0_0");
                    }
                });
                await check(new Fixture(documents, id, documents.PathFor(id), presentation, window!, before));
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => presentation.DisposeAsync().AsTask());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Enter(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
    }

    private static MenuItem MenuRow(BlockGripCui.BlockActionsHost host, string name) =>
        AuthoredControls(host.Menu).OfType<MenuItem>().Single(control => control.Name == name);

    private static IEnumerable<Control> AuthoredControls(Control root)
    {
        yield return root;
        var children = root is CuiContainerControl container ? container.AuthoredChildren
            : root.GetVisualChildren().OfType<Control>();
        foreach (var child in children)
            foreach (var descendant in AuthoredControls(child)) yield return descendant;
    }

    private static async Task DrainAsync(BlockGripCui.BlockActionsHost host)
    {
        var actions = TestUiThread.Run(host.WhenActionsIdleAsync);
        for (var attempt = 0; attempt < 200 && !actions.IsCompleted; attempt++)
        {
            TestUiThread.Run(() => Dispatcher.UIThread.RunJobs()); await Task.Delay(10);
        }
        await actions.WaitAsync(TimeSpan.FromSeconds(5));
        TestUiThread.Run(() => Dispatcher.UIThread.RunJobs());
    }
}
