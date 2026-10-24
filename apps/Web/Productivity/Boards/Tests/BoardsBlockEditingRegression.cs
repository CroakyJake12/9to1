using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Productivity.Boards;

// Small real-owner implementation smoke. Native/visual/donor acceptance is separate.
internal static class BoardsBlockEditingRegression
{
    public static async Task Run(INotesRepository repository)
    {
        var assertions = 0;
        void Check(bool passed, string name)
        { if (!passed) throw new InvalidOperationException(name); assertions++; Console.WriteLine("ASSERT " + name); }
        var core = new BoardsBrowserCore(repository);
        Check((await core.CreateAsync("Revision block workflow smoke")).Succeeded, "Create the real canonical board");
        var doc = core.GetDocumentSnapshot()!;
        var boardId = doc.Id;
        var pageId = core.ActivePageId!.Value;
        var first = doc.Sections[0].Pages[0].Blocks[0];
        var otherPage = (await core.AddPageAsync(doc.Sections[0].Id, "Other subject")).Value;
        await core.AddParagraphAsync(otherPage, "Other subject must remain unchanged.");
        await core.SelectPageAsync(pageId);
        var otherBefore = Page(core, otherPage);
        var lastId = (await core.AddParagraphAsync(pageId, "Keep this final paragraph")).Value;
        var originalOrder = Blocks(core, pageId).Select(block => block.Id).ToArray();
        await core.SelectRichTextAsync(first.Id, 0, 0);
        var codeResult = await core.InsertNoteBlockAsync(pageId, first.Id, NotesBlockKind.Code);
        var codeId = codeResult.Value;
        var blocks = Blocks(core, pageId);
        Check(codeResult.Succeeded && blocks.Select(block => block.Id).SequenceEqual(originalOrder.Take(1).Append(codeId).Concat(originalOrder.Skip(1))) && blocks[^1].Id == lastId,
            "Typed insertion uses the selected block position instead of appending to the page");
        await core.SelectRichTextAsync(codeId, 0, "Code".Length);
        await core.InsertRichTextAsync("print('revision')");
        Check(string.Concat(Block(core, codeId).Runs.Select(run => run.Text)) == "print('revision')",
            "Inserted code uses the existing shared text editor");
        await core.SelectRichTextAsync(codeId, 0, 5);
        await core.DispatchAsync("Strikethrough", null);
        Check(Block(core, codeId).Runs[0] is { Text: "print", StrikeThrough: true } &&
            Block(core, codeId).Runs.Skip(1).All(run => !run.StrikeThrough),
            "Strikethrough changes only the intended shared text range");
        var equationId = (await core.InsertNoteBlockAsync(pageId, codeId, NotesBlockKind.Equation)).Value;
        blocks = Blocks(core, pageId);
        Check(blocks.Select(block => block.Id).SequenceEqual(originalOrder.Take(1).Append(codeId).Append(equationId).Concat(originalOrder.Skip(1))) && Block(core, equationId).Equation is not null,
            "An equation is a real shared object inserted between the existing blocks");
        core.TryGetValue("SelectedEquationEditors", out var raw);
        var equationField = ((BoardsEquationBinding[])raw!)[0];
        Check(equationField.TrySetValue("Source", "y = 2x + 3") &&
            equationField.TrySetValue("Alternative", "y equals two x plus three") &&
            Block(core, equationId).Equation is { Source: "y = 2x + 3", AccessibleAlternative: "y equals two x plus three" },
            "Both live equation fields update the original payload without overwriting one another");
        Check((await core.UndoRichTextAsync()).Value && Block(core, equationId).Equation!.Source == "y = 2x + 3",
            "Shared undo restores only the latest equation-field edit");
        var beforeStale = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(!equationField.TrySetValue("Source", "Stale after undo") && JsonSerializer.Serialize(core.GetDocumentSnapshot()) == beforeStale,
            "Undo retires the old equation field rather than changing detached payloads");
        await core.RedoRichTextAsync();
        Check((await core.ChangeNoteBlockAsync(pageId, equationId, BoardsBlockEdit.MoveUp)).Value &&
            Blocks(core, pageId)[1].Id == equationId && Blocks(core, pageId)[2].Id == codeId,
            "Move-up reorders original block identities through shared history");
        Check((await core.UndoRichTextAsync()).Value && Blocks(core, pageId)[2].Id == equationId,
            "Undo restores deterministic original order");
        Check((await core.ChangeNoteBlockAsync(pageId, equationId, BoardsBlockEdit.Delete)).Value &&
            Blocks(core, pageId).All(block => block.Id != equationId),
            "Delete removes the selected block from the actual page without purging other artifacts");
        Check((await core.UndoRichTextAsync()).Value && Block(core, equationId).Equation!.Source == "y = 2x + 3",
            "Undo recovers the same equation ID and authored source");
        await core.SelectRichTextAsync(first.Id, 0, 0);
        var listId = (await core.InsertNoteBlockAsync(pageId, first.Id, NotesBlockKind.List, NotesListKind.Checklist)).Value;
        var itemId = Block(core, listId).List!.Items[0].Id;
        Check(core.UpdateChecklist(pageId, listId, itemId, "Revise algorithms", true),
            "The real shared checklist item accepts text and checked state");
        Check((await core.ChangeNoteBlockAsync(pageId, listId, BoardsBlockEdit.NumberedList)).Value &&
            Block(core, listId).List is { Kind: NotesListKind.Numbered } numbered &&
            numbered.Items[0].Id == itemId && numbered.Items[0].Checked,
            "List-kind conversion preserves original item identities, values and checked metadata");
        core.TryGetValue("ChecklistItems", out raw);
        var listField = ((BoardsChecklistRow[])raw!)[0];
        Check(listField.TryGetValue("Marker", out var marker) && Equals(marker, "1.") &&
            listField.TrySetValue("Text", "Revise search algorithms") && Block(core, listId).List!.Items[0].Text == "Revise search algorithms",
            "Numbered-list CUI fields use the same original list items");
        await core.DispatchAsync("AddChecklistItem", null);
        Check(Block(core, listId).List!.Items.Count == 2, "Existing shared list insertion also works for numbered lists");
        Check((await core.ChangeNoteBlockAsync(pageId, listId, BoardsBlockEdit.BulletedList)).Value &&
            Block(core, listId).List!.Kind == NotesListKind.Bulleted,
            "Bulleted lists use the same payload and history");
        await core.UndoRichTextAsync();
        var beforeUndoneListField = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(!listField.TrySetValue("Text", "Retired list after undo") && JsonSerializer.Serialize(core.GetDocumentSnapshot()) == beforeUndoneListField,
            "Undo retires prior list-field object references as well as equation fields");
        await core.RedoRichTextAsync();
        await core.SelectRichTextAsync(codeId, 0, 0);
        var beforeOldList = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(!listField.TrySetValue("Text", "Wrong selected block") && JsonSerializer.Serialize(core.GetDocumentSnapshot()) == beforeOldList,
            "A stale list field cannot mutate a different selected block");
        var beforeWrongPage = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(!(await core.InsertNoteBlockAsync(otherPage, codeId, NotesBlockKind.Quote)).Succeeded &&
            !(await core.ChangeNoteBlockAsync(otherPage, codeId, BoardsBlockEdit.Delete)).Succeeded &&
            !(await core.InsertNoteBlockAsync(pageId, codeId, NotesBlockKind.HtmlWidget)).Succeeded &&
            JsonSerializer.Serialize(core.GetDocumentSnapshot()) == beforeWrongPage,
            "Foreign-page targets and unsupported insert types preserve the full draft");
        await core.SelectRichTextAsync(equationId, 0, 0);
        Check(core.TrySetValue("IsEditMode", false), "Set the real persistent View mode");
        var viewBefore = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(!(await core.InsertNoteBlockAsync(pageId, equationId, NotesBlockKind.Divider)).Succeeded &&
            !(await core.ChangeNoteBlockAsync(pageId, equationId, BoardsBlockEdit.Delete)).Succeeded &&
            !(await core.UpdateNoteEquationAsync(pageId, equationId, "Denied", "Denied")).Succeeded &&
            core.IsActionAvailable("InsertEquation") == false && JsonSerializer.Serialize(core.GetDocumentSnapshot()) == viewBefore,
            "View denies insertion, reordering/deletion and equation authoring without mutation");
        Check(core.TrySetValue("IsEditMode", true), "Return explicitly to Edit mode");
        Check(Page(core, otherPage) == otherBefore, "Every block operation preserves the unrelated page exactly");
        Check((await core.SaveAsync()).Succeeded && !core.IsDirty, "Save through the original Notes revision owner");
        var expected = Page(core, pageId);
        await core.CloseAsync(); await core.DisposeAsync();
        var reopened = new BoardsBrowserCore(repository);
        Check((await reopened.OpenAsync(boardId)).Succeeded && Page(reopened, pageId) == expected && Page(reopened, otherPage) == otherBefore,
            "A new session reopens exact block order, code marks, list IDs/data and equation source");
        await reopened.CloseAsync(); await reopened.DisposeAsync();
        var single = new BoardsBrowserCore(repository); await single.CreateAsync("Last-block boundary");
        while (Blocks(single, single.ActivePageId!.Value).Length > 1)
            if (!(await single.ChangeNoteBlockAsync(single.ActivePageId!.Value, single.NativeRichEditor!.SelectedBlockId!.Value,
                BoardsBlockEdit.Delete)).Value) throw new InvalidOperationException("Expected shared block removal.");
        var singleBefore = JsonSerializer.Serialize(single.GetDocumentSnapshot());
        Check(!(await single.ChangeNoteBlockAsync(single.ActivePageId!.Value, single.NativeRichEditor!.SelectedBlockId!.Value,
            BoardsBlockEdit.Delete)).Succeeded && JsonSerializer.Serialize(single.GetDocumentSnapshot()) == singleBefore,
            "The last block cannot be deleted into an invalid empty page");
        await single.CloseAsync(); await single.DisposeAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "blocks", assertions,
            scope = "Real owner/shared editor basic implementation smoke; native, visual and donor acceptance NOT RUN" }));
    }

    private static NotesBlock[] Blocks(BoardsBrowserCore core, Guid pageId) => core.GetDocumentSnapshot()!
        .Sections.SelectMany(section => section.Pages).Single(page => page.Id == pageId).Blocks.ToArray();
    private static NotesBlock Block(BoardsBrowserCore core, Guid id) => core.GetDocumentSnapshot()!
        .Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Single(block => block.Id == id);
    private static string Page(BoardsBrowserCore core, Guid id) => JsonSerializer.Serialize(core.GetDocumentSnapshot()!
        .Sections.SelectMany(section => section.Pages).Single(page => page.Id == id));
}
