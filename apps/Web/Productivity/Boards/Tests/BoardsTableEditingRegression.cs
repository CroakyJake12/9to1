using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Productivity.Boards;

// Focused implementation smoke over the actual core and caller-supplied isolated
// owner repository. This is not native, donor-parity or release acceptance.
internal static class BoardsTableEditingRegression
{
    public static async Task Run(INotesRepository repository)
    {
        var assertions = 0;
        void Check(bool passed, string message)
        { if (!passed) throw new InvalidOperationException(message); assertions++; Console.WriteLine("ASSERT " + message); }
        var core = new BoardsBrowserCore(repository);
        Check((await core.CreateAsync("Boards table workflow smoke")).Succeeded, "Create a real owner-backed board");
        var document = core.GetDocumentSnapshot()!;
        var boardId = document.Id;
        var pageId = document.Sections[0].Pages[0].Id;
        var otherPageId = (await core.AddPageAsync(document.Sections[0].Id, "Other revision notes")).Value;
        await core.AddParagraphAsync(otherPageId, "Independent revision material must survive table actions.");
        await core.SelectPageAsync(pageId);
        var untouchedPage = Page(core, otherPageId);
        await core.DispatchAsync("InsertTable", null);
        var tableBlock = core.NativeRichEditor!.SelectedBlock!;
        var blockId = tableBlock.Id;
        var initialRows = tableBlock.Table!.Rows.Count;
        var initialColumns = tableBlock.Table.Rows[0].Cells.Count;
        var firstCellId = tableBlock.Table.Rows[0].Cells[0].Id;
        var secondCellId = tableBlock.Table.Rows[0].Cells[1].Id;
        Check(initialRows > 1 && initialColumns > 1 && core.TryGetValue("IsTableSelected", out var selected) && selected is true,
            "The inserted canonical table exposes contextual controls");
        Check((await core.SelectTableCellAsync(pageId, blockId, firstCellId)).Succeeded,
            "Select an original cell by stable page, block and cell IDs");
        Check(core.TryGetValue("SelectedTableCellEditors", out var raw) && raw is BoardsTableCellBinding[] { Length: 1 },
            "Context binds exactly one selected original cell rather than duplicating the grid");
        var firstField = ((BoardsTableCellBinding[])raw!)[0];
        Check(firstField.TrySetValue("Text", "Photosynthesis") && Table(core, blockId).Rows[0].Cells[0].Text == "Photosynthesis",
            "CUI field edits the real shared cell and marks the canonical draft dirty");
        await core.SelectTableCellAsync(pageId, blockId, secondCellId);
        var beforeStale = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(!firstField.TrySetValue("Text", "Stale callback") && JsonSerializer.Serialize(core.GetDocumentSnapshot()) == beforeStale,
            "An old cell field cannot redirect input after selection changes");
        Check((await core.EditTableAsync(pageId, blockId, secondCellId, BoardsTableEdit.Text, "Chlorophyll")).Value,
            "The typed action edits the second original cell");
        var beforeForeign = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(!(await core.EditTableAsync(otherPageId, blockId, secondCellId, BoardsTableEdit.Text, "Wrong page")).Succeeded &&
            JsonSerializer.Serialize(core.GetDocumentSnapshot()) == beforeForeign,
            "A table action cannot mutate a foreign page through a stale target");
        await core.SelectTableCellAsync(pageId, blockId, firstCellId);
        Check((await core.EditTableAsync(pageId, blockId, firstCellId, BoardsTableEdit.Fill, "#335C93")).Value,
            "Cell fill is authored through the original shared editor");
        var beforeInvalidFill = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(!(await core.EditTableAsync(pageId, blockId, firstCellId, BoardsTableEdit.Fill, "#GGGGGG")).Succeeded &&
            JsonSerializer.Serialize(core.GetDocumentSnapshot()) == beforeInvalidFill,
            "Invalid fill is rejected without changing table data");
        Check((await core.EditTableAsync(pageId, blockId, firstCellId, BoardsTableEdit.MergeRight)).Value,
            "Merge-right invokes the existing shared table operation");
        var merged = Table(core, blockId).Rows[0].Cells[0];
        Check(merged.Id == firstCellId && merged.ColumnSpan == 2 && merged.Text.Contains("Photosynthesis", StringComparison.Ordinal) &&
            merged.Text.Contains("Chlorophyll", StringComparison.Ordinal) && merged.Background == "#335C93",
            "Merge retains the primary cell identity, both values, span and fill");
        var beforeBlocked = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(core.IsActionAvailable("TableAddColumn") == false &&
            !(await core.EditTableAsync(pageId, blockId, firstCellId, BoardsTableEdit.AddColumnAfter)).Succeeded &&
            JsonSerializer.Serialize(core.GetDocumentSnapshot()) == beforeBlocked,
            "Merged geometry blocks unsafe physical-column changes without dropping cells");
        core.TryGetValue("SelectedTableCellEditors", out raw);
        var mergedField = ((BoardsTableCellBinding[])raw!)[0];
        Check((await core.UndoRichTextAsync()).Value && Table(core, blockId).Rows[0].Cells[1].Id == secondCellId,
            "Shared undo restores the original second cell identity and value");
        var beforeRetiredField = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(!mergedField.TrySetValue("Text", "Retired after undo") &&
            JsonSerializer.Serialize(core.GetDocumentSnapshot()) == beforeRetiredField,
            "Undo retires old cell-field references instead of mutating detached content");
        Check((await core.RedoRichTextAsync()).Value && Table(core, blockId).Rows[0].Cells[0].ColumnSpan == 2,
            "Shared redo restores the same canonical merged table");
        Check((await core.EditTableAsync(pageId, blockId, firstCellId, BoardsTableEdit.Split)).Value &&
            Table(core, blockId).Rows[0].Cells.Count == initialColumns && core.IsActionAvailable("TableAddColumn") == true,
            "Explicit split restores valid structural editing");
        Check((await core.EditTableAsync(pageId, blockId, firstCellId, BoardsTableEdit.AddRowAfter)).Value &&
            Table(core, blockId).Rows.Count == initialRows + 1,
            "Add row inserts beside the selected shared cell");
        var addedRowCell = core.NativeRichEditor!.SelectedTableCellId!.Value;
        Check((await core.EditTableAsync(pageId, blockId, addedRowCell, BoardsTableEdit.AddColumnAfter)).Value &&
            Table(core, blockId).Rows.All(row => row.Cells.Count == initialColumns + 1),
            "Add column updates every canonical row consistently");
        var addedColumnCell = core.NativeRichEditor!.SelectedTableCellId!.Value;
        Check((await core.EditTableAsync(pageId, blockId, addedColumnCell, BoardsTableEdit.RemoveColumn)).Value &&
            Table(core, blockId).Rows.All(row => row.Cells.Count == initialColumns),
            "Remove column preserves a regular shared table");
        Check(Page(core, otherPageId) == untouchedPage, "All table edits preserve the unrelated page exactly");
        Check(core.TrySetValue("IsEditMode", false), "Select the real persistent page View mode");
        var viewBefore = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        var viewCell = core.NativeRichEditor!.SelectedBlock!.Table!.Rows[0].Cells[0].Id;
        Check(!(await core.EditTableAsync(pageId, blockId, viewCell, BoardsTableEdit.Text, "Denied View edit")).Succeeded &&
            core.IsActionAvailable("TableAddRow") == false && JsonSerializer.Serialize(core.GetDocumentSnapshot()) == viewBefore,
            "View denies typed table mutation and authoring commands with unchanged content");
        Check((await core.SelectTableCellAsync(pageId, blockId, firstCellId)).Succeeded &&
            JsonSerializer.Serialize(core.GetDocumentSnapshot()) == viewBefore,
            "View-safe cell selection remains non-authoring");
        Check(core.TrySetValue("IsEditMode", true), "Return explicitly to Edit mode");
        await core.SelectTableCellAsync(pageId, blockId, firstCellId);
        Check((await core.EditTableAsync(pageId, blockId, firstCellId, BoardsTableEdit.Fill, "#315F4C")).Value,
            "Table controls work after the original editor rebinds");
        Check((await core.SaveAsync()).Succeeded && !core.IsDirty, "Actual owner save acknowledges the canonical table revision");
        var expected = JsonSerializer.Serialize(Table(core, blockId));
        await core.CloseAsync(); await core.DisposeAsync();
        var reopened = new BoardsBrowserCore(repository);
        Check((await reopened.OpenAsync(boardId)).Succeeded && (await reopened.SelectPageAsync(pageId)).Succeeded &&
            JsonSerializer.Serialize(Table(reopened, blockId)) == expected && Page(reopened, otherPageId) == untouchedPage,
            "A new core reopens exact table values, IDs, spans, shape and fill from the original repository");
        await reopened.SelectTableCellAsync(pageId, blockId, firstCellId);
        await reopened.DispatchAsync("TableNextCell", null);
        Check(reopened.NativeRichEditor!.SelectedTableCellId != firstCellId && !reopened.IsDirty,
            "Contextual next-cell navigation uses the shared selection without creating an edit");
        var readOnly = new BoardsBrowserCore(repository);
        await readOnly.OpenAsync(boardId, readOnly: true);
        await readOnly.SelectPageAsync(pageId);
        await readOnly.SelectTableCellAsync(pageId, blockId, firstCellId);
        Check(!(await readOnly.EditTableAsync(pageId, blockId, firstCellId, BoardsTableEdit.Fill, "#00000000")).Succeeded && !readOnly.IsDirty,
            "Read-only admission still rejects typed table edits independently of page mode");
        await readOnly.CloseAsync(); await readOnly.DisposeAsync();
        await reopened.CloseAsync(); await reopened.DisposeAsync();
        // Structural boundary controls use the same public shared model/editor;
        // no pretend repository acknowledgement is involved in this local check.
        await VerifyLastRowAndColumn(repository, Check);
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "tables", assertions,
            scope = "Focused genuine shared Boards table implementation smoke; native and acceptance NOT RUN" }));
    }

    private static async Task VerifyLastRowAndColumn(INotesRepository repository, Action<bool, string> check)
    {
        var core = new BoardsBrowserCore(repository);
        await core.CreateAsync("Table structural boundary smoke");
        var pageId = core.ActivePageId!.Value;
        await core.DispatchAsync("InsertTable", null);
        var block = core.NativeRichEditor!.SelectedBlock!;
        await core.SelectTableCellAsync(pageId, block.Id, block.Table!.Rows[0].Cells[0].Id);
        while (core.NativeRichEditor!.SelectedBlock!.Table!.Rows.Count > 1)
            if (!(await core.EditTableAsync(pageId, block.Id, core.NativeRichEditor!.SelectedTableCellId!.Value, BoardsTableEdit.RemoveRow)).Value)
                throw new InvalidOperationException("Expected genuine row removal.");
        while (core.NativeRichEditor!.SelectedBlock!.Table!.Rows[0].Cells.Count > 1)
            if (!(await core.EditTableAsync(pageId, block.Id, core.NativeRichEditor!.SelectedTableCellId!.Value, BoardsTableEdit.RemoveColumn)).Value)
                throw new InvalidOperationException("Expected genuine column removal.");
        var before = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        var cellId = core.NativeRichEditor!.SelectedTableCellId!.Value;
        check(core.IsActionAvailable("TableRemoveRow") == false && core.IsActionAvailable("TableRemoveColumn") == false &&
            !(await core.EditTableAsync(pageId, block.Id, cellId, BoardsTableEdit.RemoveRow)).Succeeded &&
            !(await core.EditTableAsync(pageId, block.Id, cellId, BoardsTableEdit.RemoveColumn)).Succeeded &&
            JsonSerializer.Serialize(core.GetDocumentSnapshot()) == before,
            "Removing the final row or column is disabled and denied without corrupting the table");
        await core.CloseAsync(); await core.DisposeAsync();
    }

    private static NotesTableData Table(BoardsBrowserCore core, Guid blockId) => core.GetDocumentSnapshot()!
        .Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Single(block => block.Id == blockId).Table!;
    private static string Page(BoardsBrowserCore core, Guid pageId) => JsonSerializer.Serialize(core.GetDocumentSnapshot()!
        .Sections.SelectMany(section => section.Pages).Single(page => page.Id == pageId));
}
