using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using NineToOne.Web.Productivity.Boards;

// Authored, NOT_RUN. This is the real local owner domain/repository gate, not a
// raw-key/browser acceptance shortcut. Browser Space remains an expected blocker.
if (args.Length != 1 || args[0] is not ("seed" or "verify") ||
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")))
    throw new ArgumentException("Choose seed/verify and a fresh isolated HAVEN_DATA_DIR.");
var paths = new AppPaths();
var repository = new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
var statePath = Path.Combine(paths.DataDirectory, "boards-rich-text-state.json");
var checks = 0;
void Check(bool condition, string name)
{ if (!condition) throw new InvalidOperationException(name); ++checks; Console.WriteLine("ASSERT " + name); }
NotesBlock Block(NotesDocument doc, Guid id) => doc.Sections.SelectMany(s => s.Pages).SelectMany(p => p.Blocks).Single(b => b.Id == id);
void Format(NotesDocument doc, Guid id)
{
    var runs = Block(doc, id).Runs;
    Check(string.Concat(runs.Select(r => r.Text)) == "Alpha beta gamma", "Exact three-word content, including both spaces");
    Check(runs.Count == 3 && runs[0].Text == "Alpha " && runs[1].Text == "beta" && runs[2].Text == " gamma", "Only selected beta forms the middle owner run");
    Check(!runs[0].Bold && runs[1].Bold && !runs[2].Bold, "Exact owner rich formatting on beta and unaffected neighbours");
}
if (args[0] == "seed")
{
    Check(!File.Exists(statePath), "Fresh seed never overwrites another fixture");
    var core = new BoardsBrowserCore(repository);
    Check((await core.CreateAsync("Durable notebook rich-text journey")).Succeeded, "Actual Boards owner creates a durable notebook");
    var doc = core.GetDocumentSnapshot()!;
    var section = doc.Sections[0]; var page = section.Pages[0];
    var created = await core.AddParagraphAsync(page.Id, string.Empty);
    Check(created.Succeeded && created.Value != Guid.Empty, "Actual Boards service creates a canonical paragraph on the owner page");
    var blockId = created.Value;
    Check((await core.SelectRichTextAsync(blockId, 0, 0)).Succeeded, "Editor selects the exact owner block ID");
    Check((await core.InsertRichTextAsync("Alpha beta gamma")).Succeeded && core.IsDirty, "Actual public owner editor mutation marks the same notebook dirty");
    var editor = core.NativeRichEditor;
    Check(editor is not null && editor.Document.Id == doc.Id && Block(core.GetDocumentSnapshot()!, blockId).PlainText == Block(editor.Document, blockId).PlainText,
        "Retained editor and core expose the same owning document identity/content");
    Check((await core.SelectRichTextAsync(blockId, 6, 10)).Succeeded, "Owner beta range6..10 selected");
    await core.DispatchAsync("Bold", null);
    Format(core.GetDocumentSnapshot()!, blockId);
    var baseRevision = core.DurableRevision;
    Check((await core.UndoRichTextAsync()).Succeeded && Block(core.GetDocumentSnapshot()!, blockId).Runs.All(r => !r.Bold), "Owner undo removes only selected bold format");
    Check(core.DurableRevision == baseRevision, "Undo does not invent durable revision");
    Check((await core.RedoRichTextAsync()).Succeeded, "Owner redo restores selected rich format");
    Format(core.GetDocumentSnapshot()!, blockId);
    Check((await core.SaveAsync()).Succeeded && !core.IsDirty, "Actual durable owner receipt acknowledges save");
    Check(ReferenceEquals(editor, core.NativeRichEditor), "Save preserves the actual editor and its undo history");
    var saved = core.GetDocumentSnapshot()!;
    var versions = await repository.GetVersionsAsync(saved.Id, default);
    Check(versions.Count >= 2, "Actual owner persists create and rich edit revision history");
    var history = JsonSerializer.Serialize(versions.OrderBy(version => version.Version).ToArray());
    var latest = versions.OrderByDescending(version => version.Version).First();
    var historical = await repository.LoadVersionAsync(saved.Id, latest.VersionId, default);
    Check(historical is not null && historical.Id == saved.Id && historical.Version == core.DurableRevision,
        "Latest real historical snapshot preserves canonical identity/revision");
    Format(historical!, blockId);
    await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new State(saved.Id, section.Id, page.Id, blockId, core.DurableRevision, JsonSerializer.Serialize(saved.Sections), history)));
    Check((await core.CloseAsync()).Succeeded && core.DocumentId is null && core.NativeRichEditor is null, "Saved close releases editor and notebook");
    await core.DisposeAsync();
}
else
{
    var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(statePath))!;
    var winner = new BoardsBrowserCore(repository);
    Check((await winner.OpenAsync(state.Id)).Succeeded, "Fresh process opens the same actual notebook ID");
    var opened = winner.GetDocumentSnapshot()!;
    Check(opened.Version == state.Revision && opened.Sections[0].Id == state.SectionId && opened.Sections[0].Pages[0].Id == state.PageId, "Canonical revision/section/page identities survive fresh process");
    Check(JsonSerializer.Serialize(opened.Sections) == state.Sections, "Whole sections/pages/blocks/runs/attributes survive restart exactly");
    Format(opened, state.BlockId);
    var versions = await repository.GetVersionsAsync(state.Id, default);
    Check(JsonSerializer.Serialize(versions.OrderBy(version => version.Version).ToArray()) == state.History,
        "Fresh process preserves exact owner history revision IDs/digests/metadata");
    var latest = versions.OrderByDescending(version => version.Version).First();
    var historical = await repository.LoadVersionAsync(state.Id, latest.VersionId, default);
    Check(historical is not null && historical.Id == state.Id && historical.Version == state.Revision,
        "Fresh process loads canonical rich history snapshot");
    Format(historical!, state.BlockId);
    var stale = new BoardsBrowserCore(new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths)));
    Check((await stale.OpenAsync(state.Id)).Succeeded, "Independent real repository object opens the same base");
    await winner.SelectRichTextAsync(state.BlockId, 16, 16); await stale.SelectRichTextAsync(state.BlockId, 16, 16);
    await winner.InsertRichTextAsync(" winner"); await stale.InsertRichTextAsync(" stale");
    Check((await winner.SaveAsync()).Succeeded, "Winning owner save publishes next revision");
    var current = Path.Combine(paths.DataDirectory, "Notes", "Documents", state.Id.ToString("D"), "current.haven-notes.json");
    var before = SHA256.HashData(await File.ReadAllBytesAsync(current));
    var conflict = await stale.SaveAsync();
    Check(!conflict.Succeeded && conflict.Code == "RevisionConflict" && stale.IsDirty, "Stale owner CAS failure preserves the rich draft");
    var after = SHA256.HashData(await File.ReadAllBytesAsync(current));
    Check(before.SequenceEqual(after), "Conflict cannot change winner durable bytes");
    Check(string.Concat(Block(stale.GetDocumentSnapshot()!, state.BlockId).Runs.Select(r => r.Text)) == "Alpha beta gamma stale", "Conflict retains exact unsaved text and original IDs");
    Check(!(await stale.PrepareToCloseAsync()).Succeeded && stale.DocumentId == state.Id, "Dirty conflict denies close and retains the active notebook");
    var readonlyCore = new BoardsBrowserCore(repository);
    Check((await readonlyCore.OpenAsync(state.Id, readOnly: true)).Succeeded, "Real saved notebook opens in read-only presentation");
    Check(!(await readonlyCore.InsertRichTextAsync("denied")).Succeeded && !readonlyCore.IsDirty && !readonlyCore.AllowRetainedRichInput,
        "Typed read-only authoring and retained input admission denied");
    await readonlyCore.CloseAsync(); await readonlyCore.DisposeAsync();
    Check((await winner.SetEditModeAsync(state.PageId, BoardsPageEditMode.View)).Succeeded && !winner.AllowRetainedRichInput,
        "Owning View mode freezes rich authoring");
    Check(!(await winner.BoldRichSelectionAsync()).Succeeded, "View mode denies typed rich formatting");
    Check((await winner.SetEditModeAsync(state.PageId, BoardsPageEditMode.Edit)).Succeeded, "Owning Edit mode restores authoring permission");
    Check((await winner.SaveAsync()).Succeeded && (await winner.CloseAsync()).Succeeded, "Known clean owner document saves and closes");
    await winner.DisposeAsync();
    // Conflict is intentionally retained, not silently discarded by implicit Dispose.
}
Console.WriteLine(JsonSerializer.Serialize(new { mode = args[0], assertions = checks, scope = "real owner notebook editor/local durable fixture; native/browser NOT_RUN" }));
internal sealed record State(Guid Id, Guid SectionId, Guid PageId, Guid BlockId, long Revision, string Sections, string History);
