using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Productivity.Boards;

internal static class BoardsWorkspaceRegression
{
    public static async Task Run(INotesRepository repository)
    {
        var count = 0;
        void Check(bool passed, string name)
        { if (!passed) throw new InvalidOperationException(name); count++; Console.WriteLine("ASSERT " + name); }
        var core = new BoardsBrowserCore(repository);
        var created = await core.CreateAsync("Workspace regression");
        Check(created.Succeeded, "Create a real canonical board");
        var document = core.GetDocumentSnapshot()!;
        var boardId = document.Id;
        var sectionId = document.Sections[0].Id;
        var firstId = document.Sections[0].Pages[0].Id;
        Check(core.ActivePageId == firstId && core.NativeRichEditor?.PageScopeId == firstId, "First page is selected and editor is scoped by stable page ID");
        var secondId = (await core.AddPageAsync(sectionId, "Second page")).Value;
        await core.AddParagraphAsync(secondId, "Must remain intact");
        var secondBefore = Page(core, secondId);
        Check((await core.SelectPageAsync(firstId)).Succeeded, "Select an existing page through the real core");
        var editor = core.NativeRichEditor!;
        editor.SelectAllDocumentText();
        Check(!editor.SelectedDocumentText.Contains("Must remain intact", StringComparison.Ordinal), "Select all excludes content on another page");
        Check((await core.InsertRichTextAsync("First page replacement")).Succeeded, "Replace selected page text");
        Check(Page(core, secondId) == secondBefore, "Text replacement cannot mutate the unseen page");
        Check((await core.UndoRichTextAsync()).Succeeded && Page(core, secondId) == secondBefore, "Scoped undo preserves unrelated page data");
        await core.RedoRichTextAsync();
        await core.SaveAsync();
        var acknowledgedRevision = core.DurableRevision;
        await core.UndoRichTextAsync();
        Check(core.GetDocumentSnapshot()!.Version == acknowledgedRevision && core.DurableRevision == acknowledgedRevision,
            "Undo after save cannot rewind the acknowledged document revision");
        await core.RedoRichTextAsync();
        await core.SelectPageAsync(secondId);
        var firstBefore = Page(core, firstId);
        editor = core.NativeRichEditor!;
        editor.SelectBlock(editor.Blocks()[0].Id, 0);
        editor.BackspaceDocument();
        Check(Page(core, firstId) == firstBefore, "Backspace at page start cannot merge with another page");
        Check(!editor.CanUndo, "Page switch does not reuse notebook-wide undo snapshots from another scope");
        var foreignBlock = core.GetDocumentSnapshot()!.Sections.SelectMany(section => section.Pages).Single(page => page.Id == firstId).Blocks[0].Id;
        Check(!(await core.SelectRichTextAsync(foreignBlock, 0, 0)).Succeeded, "Text selection refuses an off-page block ID");
        Check(core.TrySetValue("IsEditMode", false), "Persistent View mode can be selected");
        var viewBefore = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        Check(!(await core.InsertRichTextAsync("Denied")).Succeeded && !core.TrySetValue("PageTitle", "Denied"), "View mode rejects both text and title mutation");
        Check(!core.TrySetValue("LayoutUnlocked", true), "View mode cannot unlock authoring layout");
        Check(viewBefore == JsonSerializer.Serialize(core.GetDocumentSnapshot()), "View denials preserve the full canonical notebook");
        await core.SelectPageAsync(firstId);
        Check(core.AllowRetainedRichInput, "A different View page does not disable editing this Edit page");
        await core.SelectPageAsync(secondId);
        Check(core.TrySetValue("IsEditMode", true), "Return explicitly to Edit mode");
        await core.DispatchAsync("InsertChecklist", null);
        var checklist = core.NativeRichEditor!.SelectedBlock!;
        var taskId = checklist.List!.Items[0].Id;
        Check(core.UpdateChecklist(secondId, checklist.Id, taskId, text: "A real task"), "Contextual checklist text updates the original shared editor");
        Check(core.UpdateChecklist(secondId, checklist.Id, taskId, check: true), "Contextual checklist completion updates the canonical item");
        await core.UndoRichTextAsync();
        Check(core.NativeRichEditor.SelectedBlock!.List!.Items[0] is { Checked: false, Text: "A real task" }, "Checklist undo preserves item text and reverses only completion");
        Check(core.TrySetValue("LayoutUnlocked", true), "Layout mode is independent and persists through the owner");
        Check(core.TrySetValue("PageTitle", "Renamed page"), "Rename active page without changing identity");
        Check(core.ActivePageId == secondId, "Page identity survives title changes");
        Check((await core.MovePageAsync(secondId, sectionId, 0)).Succeeded, "Reorder through the canonical owner");
        Check(core.GetDocumentSnapshot()!.Sections[0].Pages[0].Id == secondId, "Reorder keeps stable ID at new position");
        var expectedFirst = JsonSerializer.Deserialize<NotesPage>(firstBefore)!;
        expectedFirst.Order = 1; // Moving second to index zero deliberately changes its neighbour's order.
        var firstAfterMove = JsonSerializer.Serialize(expectedFirst);
        Check(Page(core, firstId) == firstAfterMove, "Reorder changes only the neighbour's order metadata");
        Check((await core.DeletePageAsync(secondId)).Value, "Recoverable delete uses the owner trash mechanism");
        Check(core.ActivePageId == firstId && !(await core.SelectPageAsync(secondId)).Succeeded, "Deleted page is removed from selection and editing scope");
        Check((await core.RestorePageAsync(secondId)).Value, "Restore preserves the original page");
        await core.SelectPageAsync(secondId);
        Check(core.GetDocumentSnapshot()!.Sections[0].Pages.Single(page => page.Id == secondId).Title == "Renamed page", "Recovered title and ID remain intact");
        Check(core.TrySetValue("IsEditMode", false), "Persist View mode for reopen");
        Check((await core.SaveAsync()).Succeeded && !core.IsDirty, "Save acknowledges canonical durable revision");
        await core.CloseAsync(); await core.DisposeAsync();
        var reopened = new BoardsBrowserCore(repository);
        Check((await reopened.OpenAsync(boardId)).Succeeded, "New session reopens real repository data");
        await reopened.SelectPageAsync(secondId);
        Check(!reopened.AllowRetainedRichInput, "View mode survives a new session");
        reopened.TryGetValue("LayoutUnlocked", out var unlocked);
        Check(unlocked is true, "Independent layout mode survives a new session");
        Check(Page(reopened, firstId) == firstAfterMove, "Unrelated first-page content and intended order survive save and reopen");
        await reopened.CloseAsync(); await reopened.DisposeAsync();
        var emptyScope = NotesDocument.Create();
        var other = NotesPage.CreateDefault(); other.Blocks.Clear(); emptyScope.Sections[0].Pages.Add(other);
        var scoped = new WriteDocumentEditor(emptyScope, other.Id);
        scoped.InsertBlock(NotesBlockKind.Paragraph);
        Check(emptyScope.Sections[0].Pages[0].Blocks.Count == 1 && other.Blocks.Count == 1, "Empty-page insertion fallback targets the requested scope");
        var legacy = new WriteDocumentEditor(emptyScope);
        Check(legacy.Blocks().Count == 2, "Default Write editor still sees its whole document");
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "workspace", assertions = count,
            scope = "Actual canonical Boards owner and repository; browser/native/donor acceptance separate" }));
    }

    private static string Page(BoardsBrowserCore core, Guid id) => JsonSerializer.Serialize(core.GetDocumentSnapshot()!
        .Sections.SelectMany(section => section.Pages).Single(page => page.Id == id));
}
