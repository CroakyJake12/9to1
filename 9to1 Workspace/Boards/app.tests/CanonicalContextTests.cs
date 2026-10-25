using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class CanonicalContextTests
{
    [Fact]
    public async Task Actual_authored_inspector_preserves_the_session_and_rejects_invalid_selection_values()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/canonical-context-" + Guid.NewGuid().ToString("N"));
        var vm = new BoardsViewModel(session);
        var document = session.Document;
        var table = document.Sections[0].Pages[0].Blocks.First(block => block.Kind == "table");
        vm.FocusBlock(table.Id);
        TestUiThread.Run(() =>
        {
            using var loader = new CuiControlLoader(BoardsCuiDocuments.CreateControlRegistry());
            loader.SetBindingContext(vm); loader.SetActionDispatcher(vm);
            var (root, diagnostics) = loader.LoadFile(FindSource());
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            Assert.NotNull(root);
            loader.WireBindings(root);
            var controls = root.GetLogicalDescendants().OfType<Control>().ToArray();
            var alternating = Assert.Single(controls.OfType<CheckBox>(), control => control.Content?.ToString() == "Alternating rows");
            alternating.IsChecked = true;
            Assert.True(table.TableStyle.AlternatingRows);
            var surface = Assert.Single(controls.OfType<ComboBox>(), control => AutomationProperties.GetName(control) == "Table background");
            surface.SelectedIndex = 1;
            Assert.Equal("#FFFFFFFF", table.TableStyle.Background);
            Assert.False(vm.TrySetValue("ContextTableSurfaceIndex", -1));
            Assert.False(vm.TrySetValue("ContextTableBorderIndex", 100));
            Assert.Equal("#FFFFFFFF", table.TableStyle.Background);
            var addRow = Assert.Single(controls.OfType<Button>(), control => AutomationProperties.GetName(control) == "Add table row");
            // This real legacy fixture stores a 2x2 table as cells and leaves
            // TableRows unset. The owner materializes its dimensions on edit.
            Assert.Equal(new[] { "0,0", "0,1", "1,0", "1,1" }, table.TableCells.Keys.Order().ToArray());
            addRow.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, table.TableRows);
            Assert.Equal(2, table.TableCols);
            Assert.Same(document, session.Document);
            Assert.Equal(table.Id, vm.SelectedBlockId);
        });
        await session.SaveAsync();
        Assert.Contains("#FFFFFFFF", session.Snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Context_commands_require_the_current_kind_and_keep_menu_commands_nonmutating()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/canonical-context-safety-" + Guid.NewGuid().ToString("N"));
        var vm = new BoardsViewModel(session);
        var before = session.Snapshot;
        Assert.False(vm.TrySetValue("ContextTableSurfaceIndex", 1));
        await vm.DispatchAsync("ContextDividerThicker", null);
        var properties = 0; var close = 0;
        vm.BoardPropertiesRequested += () => properties++;
        vm.CloseWindowRequested += () => close++;
        await vm.DispatchAsync("BoardProperties", null);
        await vm.DispatchAsync("CloseWindow", null);
        Assert.Equal(1, properties); Assert.Equal(1, close);
        Assert.Equal(before, session.Snapshot);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.DispatchAsync("ContextAddRow", null, cancellation.Token).AsTask());
        Assert.Equal(before, session.Snapshot);
    }

    private static string FindSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "9to1 Workspace", "Boards", "app", "Boards.cui");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("The actual native Boards.cui source was not found.");
    }
}
