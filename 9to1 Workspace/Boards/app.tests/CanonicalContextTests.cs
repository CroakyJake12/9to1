using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
            var (root, diagnostics) = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            var loadedRoot = Assert.IsAssignableFrom<Control>(root);
            loader.WireBindings(loadedRoot);
            vm.Attach(loadedRoot);
            using var textOwner = new BoardsTextAndKeyboardOwner(loadedRoot, vm);
            var controls = loadedRoot.GetLogicalDescendants().OfType<Control>().ToArray();
            var pageTitle = Assert.Single(controls.OfType<TextBox>(), control => AutomationProperties.GetName(control) == "Page title");
            pageTitle.Text = "Editable page heading";
            Assert.Equal("Editable page heading", document.Sections[0].Pages[0].Title);
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
            vm.FocusCell(table.Id, 1, 1);
            Assert.True(vm.TryGetValue("ContextHasCell", out var hasCell) && hasCell is true);
            var widen = Assert.Single(controls.OfType<Button>(), control => AutomationProperties.GetName(control) == "Widen selected table column");
            widen.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(184, table.ColumnWidths[1]);
            var taller = Assert.Single(controls.OfType<Button>(), control => AutomationProperties.GetName(control) == "Heighten selected table row");
            taller.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(80, table.RowHeights[1]);
            var widthDraft = Assert.Single(controls.OfType<NumericUpDown>(), control =>
                AutomationProperties.GetName(control)?.StartsWith("Exact selected table column width", StringComparison.Ordinal) == true);
            var heightDraft = Assert.Single(controls.OfType<NumericUpDown>(), control =>
                AutomationProperties.GetName(control)?.StartsWith("Exact selected table row height", StringComparison.Ordinal) == true);
            widthDraft.Value = 312;
            heightDraft.Value = 128;
            Assert.Equal(184, table.ColumnWidths[1]);
            Assert.Equal(80, table.RowHeights[1]);
            var applyDimensions = Assert.Single(controls.OfType<Button>(), control =>
                AutomationProperties.GetName(control) == "Apply exact selected row height and column width");
            applyDimensions.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(312, table.ColumnWidths[1]);
            Assert.Equal(128, table.RowHeights[1]);
            var italic = Assert.Single(controls.OfType<Button>(), control => AutomationProperties.GetName(control) == "Toggle selected cell italic");
            italic.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(table.CellStyles["1,1"].Italic);
            var cellFill = Assert.Single(controls.OfType<ComboBox>(), control => AutomationProperties.GetName(control) == "Selected cell fill");
            cellFill.SelectedIndex = 3;
            Assert.Equal("#FFFFF3C4", table.CellStyles["1,1"].Background);
            Assert.Same(document, session.Document);
            Assert.Equal(table.Id, vm.SelectedBlockId);
            vm.Detach();
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
}
