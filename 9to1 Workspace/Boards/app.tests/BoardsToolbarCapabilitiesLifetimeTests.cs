using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsToolbarCapabilitiesLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Toolbar_font_draft_survives_render_with_backward_range_and_respects_new_search_focus(bool searchTakesFocus) =>
        WithToolbar((presentation, window, parentId, _, _) =>
        {
            var vm = presentation.ViewModel;
            TextBox Editor() => window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "t_" + parentId);
            Editor().Focus();
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            var editor = Editor(); editor.SelectionStart = 12; editor.SelectionEnd = 7;
            var font = Find<TextBox>(presentation, "AdaptiveFont");
            Assert.True(font.Focus()); font.Text = "Georgia";
            var snapshot = vm.Session.Snapshot;
            vm.RequestRebuild();
            var search = Find<TextBox>(presentation, "NavSearchBox");
            if (searchTakesFocus) Assert.True(search.Focus());
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            if (searchTakesFocus)
            {
                Assert.True(search.IsFocused);
                Assert.Equal(snapshot, vm.Session.Snapshot);
                return;
            }
            Assert.True(font.IsFocused);
            Assert.Equal(12, Editor().SelectionStart); Assert.Equal(7, Editor().SelectionEnd);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Dispatcher.UIThread.RunJobs();
            var runs = vm.FindBlock(parentId)!.Runs;
            Assert.Equal(new[] { "Parent ", "parag", "raph" }, runs.Select(run => run.Text));
            Assert.Equal(new[] { "", "Georgia", "" }, runs.Select(run => run.FontFamily));
        });

    [Fact]
    public Task Font_drafts_follow_block_and_range_selection_but_survive_ordinary_render() =>
        WithToolbar((presentation, window, parentId, _, _) =>
        {
            var vm = presentation.ViewModel;
            var parent = vm.FindBlock(parentId)!;
            parent.Runs = [new HavenRichTextRun { Text = "Parent ", FontFamily = "Inter" },
                new HavenRichTextRun { Text = "paragraph", FontFamily = "Georgia" }];
            var second = new RichBoardBlock { Text = "Another paragraph", FontFamily = "Times New Roman" };
            vm.Session.Document.Sections.SelectMany(section => section.Pages).Single(page => page.Id == vm.SelectedPageId).Blocks.Add(second);
            vm.Session.MarkDirty(); vm.RequestRebuild();
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            TextBox Editor(string id) => window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "t_" + id);
            var input = Editor(parentId); input.Focus(); input.CaretIndex = 1;
            Dispatcher.UIThread.RunJobs();
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            var root = Find<Control>(presentation, "AdaptiveToolbar");
            var font = Find<TextBox>(presentation, "AdaptiveFont");
            font.Text = "Uncommitted family";
            vm.RequestRebuild(); BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Uncommitted family", font.Text);
            input = Editor(parentId); input.Focus(); input.SelectionStart = 7; input.SelectionEnd = 16;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Georgia", font.Text);
            using (var context = new AdaptiveToolbar.Context(vm))
            {
                input.SelectionStart = 0; input.SelectionEnd = 7;
                Assert.True(context.TrySetValue("FontDraft", "Cascadia Mono"));
                input.SelectionStart = 7; input.SelectionEnd = 16;
                context.DispatchAsync("ApplyFont", null).GetAwaiter().GetResult();
                Assert.Equal(new[] { "Inter", "Georgia" }, vm.FindBlock(parentId)!.Runs.Select(run => run.FontFamily));
                input.SelectionStart = 0; input.SelectionEnd = 7;
                Assert.True(context.TrySetValue("FontDraft", "Cascadia Mono"));
                Dispatcher.UIThread.RunJobs();
                Assert.True(context.TryGetValue("FontDraft", out var current)); Assert.Equal("Cascadia Mono", current);
                context.DispatchAsync("ApplyFont", null).GetAwaiter().GetResult();
                Assert.Equal(new[] { "Cascadia Mono", "Georgia" }, vm.FindBlock(parentId)!.Runs.Select(run => run.FontFamily));
            }
            font.Text = "Another uncommitted family";
            Editor(second.Id).Focus();
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(root, Find<Control>(presentation, "AdaptiveToolbar"));
            Assert.Equal("Times New Roman", font.Text);
        });

    [Fact]
    public Task Populated_graph_and_table_renders_preserve_selection_revision_and_numeric_precision() =>
        WithToolbar((presentation, window, parentId, _, _) =>
        {
            var vm = presentation.ViewModel;
            var adapter = Assert.IsType<ContractSessionAdapter>(vm.Session);
            var graph = vm.PageBlocks.Single(block => block.Kind == "graph");
            Assert.Equal(-10.1234567890123, graph.Graph!.XMin);
            var snapshot = adapter.Snapshot;
            var revision = adapter.DocumentVersion;
            window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "t_" + parentId).Focus();
            for (var index = 0; index < 3; index++)
            {
                vm.RequestRebuild();
                BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Equal(parentId, vm.SelectedBlockId);
                Assert.Equal(snapshot, adapter.Snapshot);
                Assert.Equal(revision, adapter.DocumentVersion);
            }
            var settings = window.GetVisualDescendants().OfType<Expander>().Single(control => Equals(control.Header, "Graph settings"));
            settings.IsExpanded = true; window.UpdateLayout();
            var expression = window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name?.StartsWith("gx_", StringComparison.Ordinal) == true);
            var accepted = expression.Focus();
            var beforeTyping = $"accepted={accepted}, focused={expression.IsFocused}, attached={TopLevel.GetTopLevel(expression) is not null}, visible={expression.IsEffectivelyVisible}, expanded={settings.IsExpanded}, selection={vm.SelectedBlockId}";
            expression.SelectAll(); window.KeyTextInput("y = x^2");
            var immediateText = expression.Text;
            Dispatcher.UIThread.RunJobs();
            var actual = vm.FindBlock(graph.Id)!.Graph!.Expressions.Single().Text;
            var currentInput = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(input => input.Name?.StartsWith("gx_", StringComparison.Ordinal) == true);
            var currentFocus = window.FocusManager?.GetFocusedElement() as Control;
            var currentSettings = window.GetVisualDescendants().OfType<Expander>().FirstOrDefault(control => Equals(control.Header, "Graph settings"));
            Assert.True(actual == "y = x^2", $"Graph typing should commit y = x^2, actual={actual}; before=[{beforeTyping}]; immediateText={immediateText}; " +
                $"after oldText={expression.Text}, oldAttached={TopLevel.GetTopLevel(expression) is not null}, currentText={currentInput?.Text}, currentVisible={currentInput?.IsEffectivelyVisible}, currentExpanded={currentSettings?.IsExpanded}, focus={currentFocus?.GetType().Name}/{currentFocus?.Name}, selection={vm.SelectedBlockId}");
            Assert.Equal(graph.Id, vm.SelectedBlockId);
            Assert.NotNull(currentSettings); Assert.True(currentSettings.IsExpanded, "Rendering must preserve the open graph settings.");
            Assert.NotNull(currentInput); Assert.True(currentInput.IsFocused, "The replacement graph editor must keep keyboard focus.");
            window.KeyTextInput("+1");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("y = x^2+1", vm.FindBlock(graph.Id)!.Graph!.Expressions.Single().Text);
            expression = window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name?.StartsWith("gx_", StringComparison.Ordinal) == true);
            Assert.True(expression.IsFocused);
            expression.SelectAll(); window.KeyTextInput("y = x");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("y = x", vm.FindBlock(graph.Id)!.Graph!.Expressions.Single().Text);
        });

    [Fact]
    public Task Queued_block_render_keeps_open_style_menu_and_uncommitted_font_draft() =>
        WithToolbar((presentation, window, parentId, _, _) =>
        {
            var vm = presentation.ViewModel;
            var editor = window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "t_" + parentId);
            editor.Focus();
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            var toolbar = Find<Control>(presentation, "AdaptiveToolbar");
            var font = Find<TextBox>(presentation, "AdaptiveFont");
            var style = Find<ComboBox>(presentation, "AdaptiveStyle");
            font.Text = "Uncommitted family";
            style.Focus(); style.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(style.IsDropDownOpen);
            vm.RequestRebuild();
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Same(toolbar, Find<Control>(presentation, "AdaptiveToolbar"));
            Assert.Same(font, Find<TextBox>(presentation, "AdaptiveFont"));
            Assert.Equal("Uncommitted family", font.Text);
            Assert.True(style.IsDropDownOpen);
            Assert.NotNull(TopLevel.GetTopLevel(style));
            Assert.True(style.IsEffectivelyEnabled);
            style.IsDropDownOpen = false;
            var georgia = Find<MenuItem>(presentation, "AdaptiveFontSection_Menu_Choose_font_family_FontGeorgia");
            georgia.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Georgia", font.Text);
            var size = Find<TextBox>(presentation, "AdaptiveSize");
            size.Text = "51";
            Find<Button>(presentation, "AdaptiveFontSection_FontBigger").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("16", size.Text);
        });

    [Fact]
    public Task Switching_to_child_replaces_insert_target_and_adds_to_the_selected_pane() =>
        WithToolbar((presentation, window, parentId, childId, childPageId) =>
        {
            var vm = presentation.ViewModel;
            window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "t_" + parentId).Focus();
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            var previous = Find<Control>(presentation, "AdaptiveToolbar");
            var previousInsert = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(Find<Button>(presentation, "AdaptiveAdd")));
            var parentCount = vm.PageBlocks.Count;
            window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "t_" + childId).Focus();
            BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(childPageId, vm.ToolbarTarget.SelectedPageId);
            Assert.NotSame(previous, Find<Control>(presentation, "AdaptiveToolbar"));
            Assert.False(previousInsert.Open());
            var child = vm.ToolbarTarget;
            var before = child.PageBlocks.Count;
            var insert = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(Find<Button>(presentation, "AdaptiveAdd")));
            Assert.True(insert.Open());
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var paragraph = insert.Popup.Child!.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Insert Paragraph");
            paragraph.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            BoardsTabberInsertionTests.Await(insert.WhenActionsIdleAsync());
            Assert.Equal(before + 1, child.PageBlocks.Count);
            Assert.Equal(parentCount, vm.PageBlocks.Count);
        });

    private static T Find<T>(BoardsWritablePresentation presentation, string id) where T : Control =>
        Assert.IsAssignableFrom<T>(BoardsCuiControlFinder.FindByAutomationId<T>(presentation.Content, id));

    private static async Task WithToolbar(Action<BoardsWritablePresentation, Window, string, string, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "boards-toolbar-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var documents = new BoardsLocalDocuments("toolbar-lifetime", directory);
            var id = await documents.CreateAsync();
            string parentId, childId, childPageId;
            await using (var seed = await documents.OpenAsync(id))
            {
                var page = seed.Document.Sections[0].Pages[0];
                parentId = page.Blocks[0].Id; page.Blocks[0].Text = "Parent paragraph"; seed.MarkDirty();
                var owner = await seed.AddTabberAsync(page.Id, parentId);
                var tabs = seed.Document.Sections[0].Pages[0].Blocks.Single(block => block.Id == owner).Tabber!;
                childPageId = tabs.Panes.Single(pane => pane.Id == tabs.SelectedPaneId).PageId;
                childId = seed.Document.Sections.SelectMany(section => section.Pages).Single(child => child.Id == childPageId).Blocks[0].Id;
                var current = seed.Document.Sections[0].Pages[0];
                current.Blocks.Add(new RichBoardBlock { Kind = "table", TableRows = 2, TableCols = 2,
                    TableCells = new() { ["0,0"] = "Heading", ["0,1"] = "Value", ["1,0"] = "Stable", ["1,1"] = "42" } });
                current.Blocks.Add(new RichBoardBlock { Kind = "graph", Graph = new RichGraphView {
                    XMin = -10.1234567890123, Expressions = [new RichGraphExpressionView { Text = "y = x" }] } });
                seed.MarkDirty();
                await seed.SaveAsync();
            }
            var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(documents, id, (_, _) => Task.CompletedTask));
            BoardsTabberInsertionTests.OnUi(() => opening);
            var presentation = await opening;
            Window? window = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Content = presentation.Content, Width = 1800, Height = 900 };
                    window.Show();
                    BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<TextBox>().Any(input => input.Name == "t_" + childId));
                    BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
                    window.UpdateLayout();
                    check(presentation, window, parentId, childId, childPageId);
                });
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => presentation.DisposeAsync().AsTask());
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
