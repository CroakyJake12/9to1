using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Productivity.Boards;

internal static class BoardsFindRegression
{
    public static async Task Run(INotesRepository actualRepository)
    {
        var assertions = 0;
        void Check(bool ok, string name)
        { if (!ok) throw new InvalidOperationException(name); assertions++; Console.WriteLine("ASSERT " + name); }
        var repository = new CountedNotes(actualRepository);
        var owner = new BoardsWorkspaceService(repository);
        var document = await owner.CreateNotebookAsync("Revision search", CancellationToken.None);
        var first = document.Sections[0].Pages[0];
        var paragraph = owner.AddBlock(document, first.Id, NotesBlockKind.Paragraph, "Alpha mechanics ALPHA revision");
        paragraph.Runs = [new NotesTextRun { Text = "Alpha ", Bold = true }, new NotesTextRun { Text = "mechanics ALPHA revision", Italic = true }];
        paragraph.Metadata["unknown-extension-payload"] = "{\"revision\":7,\"foreign\":[1,2,3]}";
        var second = owner.AddPage(document, document.Sections[0].Id, "Second revision page");
        second.Blocks[0].PlainText = "Legacy alpha source"; second.Blocks[0].Runs.Clear();
        owner.SetEditMode(document, second.Id, BoardsPageEditMode.View);
        var table = owner.AddBlock(document, first.Id, NotesBlockKind.Table);
        table.Table!.Rows[0].Cells[0].Text = "alpha table value";
        var removed = owner.AddPage(document, document.Sections[0].Id, "Deleted page");
        removed.Blocks[0].PlainText = "alpha hidden by Trash";
        Check(owner.DeletePage(document, removed.Id), "Actual owner moves a page into recoverable Trash");
        await owner.SaveAsync(document, "Actual search fixture rich/legacy/view/table content", default);
        var core = new BoardsBrowserCore(repository);
        Check((await core.OpenAsync(document.Id)).Succeeded, "Actual canonical repository opens the board");
        var before = Canonical(core); var revision = core.DurableRevision; var reads = repository.Reads; var writes = repository.Writes;
        var found = await core.FindBoardAsync("alpha"); var rows = Rows(core);
        Check(found.Succeeded && rows.Length >= 4, "Existing shared search finds real case-insensitive text/table matches across current pages");
        Check(repository.Reads == reads && repository.Writes == writes, "Find uses already-authorised canonical memory without new repository reads/writes");
        Check(Canonical(core) == before && !core.IsDirty && core.DurableRevision == revision, "Detached Find snapshot preserves rich runs/foreign metadata/full live payload and durable receipt");
        Check(rows.All(row => row.PageId != removed.Id), "Recoverable Trash page contents do not leak into current-board matches");
        var legacy = rows.First(row => row.PageId == second.Id && row.Match.BlockId == second.Blocks[0].Id);
        Check((await core.OpenBoardFindMatchAsync(legacy)).Succeeded && core.ActivePageId == second.Id,
            "Original match activates the correct stable page and retained shared editor");
        Check(core.NativeRichEditor!.SelectedBlockId == legacy.Match.BlockId && core.NativeRichEditor.SelectedText.Equals("alpha", StringComparison.OrdinalIgnoreCase),
            "Actual live editor selects the exact text match");
        Check(!core.AllowRetainedRichInput && Canonical(core) == before && core.DurableRevision == revision && !core.IsDirty,
            "View-page navigation is selection-only and does not normalize empty legacy runs or mutate content");
        var tableRow = rows.First(row => row.Match.BlockId == table.Id);
        Check((await core.OpenBoardFindMatchAsync(tableRow)).Succeeded && core.NativeRichEditor!.SelectedBlockId == table.Id && core.ActivePageId == first.Id,
            "Table match selects its actual original block rather than inventing an incompatible flattened text range");
        Check(Canonical(core) == before && repository.Writes == writes, "Find navigation performs no document or storage mutation");
        var copied = new BoardsFindRow(core, legacy.Epoch, 0, legacy.BoardId, legacy.PageId, "Copied", legacy.PageTitle, legacy.Query, legacy.Match);
        var selected = core.ActivePageId;
        Check(!(await core.OpenBoardFindMatchAsync(copied)).Succeeded && core.ActivePageId == selected,
            "Copied public coordinates cannot replace the source-issued original row");
        var foreign = new BoardsBrowserCore(repository); await foreign.OpenAsync(document.Id, readOnly: true); await foreign.FindBoardAsync("alpha");
        Check(!(await core.OpenBoardFindMatchAsync(Rows(foreign)[0])).Succeeded && Canonical(core) == before,
            "Another owner over the same board cannot supply a selection row");
        await foreign.CloseAsync(); await foreign.DisposeAsync();
        await core.SelectPageAsync(first.Id);
        Check((await core.InsertRichTextAsync("New unsaved content ")).Succeeded && core.IsDirty, "Actual shared editor creates a real dirty generation");
        var dirty = Canonical(core);
        Check(Rows(core).Length == 0 && !(await core.OpenBoardFindMatchAsync(legacy)).Succeeded && Canonical(core) == dirty,
            "A changed generation invalidates old matches before navigation and preserves the newer draft");
        await core.FindBoardAsync("alpha"); var beforeSaveRow = Rows(core)[0];
        Check((await core.SaveAsync()).Succeeded && core.DurableRevision == revision + 1, "Normal owner Save still controls the actual next durable revision");
        Check(Rows(core).Length == 0 && !(await core.OpenBoardFindMatchAsync(beforeSaveRow)).Succeeded,
            "Acknowledged revision change invalidates earlier selection rows");
        await core.FindBoardAsync("alpha"); var beforeSwitch = Rows(core)[0];
        Check((await core.CreateAsync("Another actual board")).Succeeded && core.DocumentId != document.Id,
            "Ordinary board switching keeps genuine canonical identity");
        Check(!(await core.OpenBoardFindMatchAsync(beforeSwitch)).Succeeded && Rows(core).Length == 0,
            "A switched board cannot adopt the old board's original match");
        await core.CloseAsync();
        Check(!(await core.OpenBoardFindMatchAsync(beforeSwitch)).Succeeded && Rows(core).Length == 0,
            "Closed board withholds old matches and refuses navigation");
        await core.DisposeAsync();
        var prepared = new BoardsBrowserCore(repository); prepared.PrepareUnadmittedPresentation(); await prepared.OpenAsync(document.Id);
        Check(!(await prepared.FindBoardAsync("alpha")).Succeeded && Rows(prepared).Length == 0,
            "Unadmitted presentation cannot expose search results");
        prepared.AbandonUnadmittedPresentation();

        // Real canonical model/shared-engine checks; no new renderer or backend.
        var plain = NotesDocument.Create("Bounded selection"); var block = plain.Sections[0].Pages[0].Blocks[0];
        block.PlainText = string.Join(' ', Enumerable.Repeat("alpha", 250)); block.Runs.Clear();
        var original = JsonSerializer.Serialize(plain);
        var engine = new WriteDocumentEditor(plain, null, initializeWithoutNormalization: true);
        Check(JsonSerializer.Serialize(plain) == original, "Explicit pure initialization leaves legacy source untouched");
        var all = engine.Find("alpha"); var bounded = engine.Find("alpha", 201);
        Check(all.Count == 250 && bounded.Count == 201 && bounded.SequenceEqual(all.Take(201)),
            "Bounded Find preserves original offsets/ordering while returning at most201 actual matches");
        Check(engine.SelectFindResult(bounded[0], "alpha") && engine.SelectedText == "alpha" && JsonSerializer.Serialize(plain) == original,
            "Pure current-result selection preserves legacy empty runs and exact full source");
        var offset = engine.SelectionStart;
        Check(!engine.SelectFindResult(bounded[0] with { Offset = block.PlainText.Length + 1 }, "alpha") && engine.SelectionStart == offset,
            "Invalid original match coordinates fail before selection change");
        var threw = false; try { _ = engine.Find("alpha", 0); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw && engine.Find(" ", 201).Count == 0, "Invalid result bounds and empty queries retain explicit semantics");
        var compatibility = JsonSerializer.Deserialize<NotesDocument>(original)!;
        _ = new WriteDocumentEditor(compatibility);
        Check(compatibility.Sections[0].Pages[0].Blocks[0].Runs.Count > 0, "Existing default constructor selection/normalization behavior is preserved");
        var crowded = await owner.CreateNotebookAsync("Many actual search matches", CancellationToken.None);
        owner.AddBlock(crowded, crowded.Sections[0].Pages[0].Id, NotesBlockKind.Paragraph, string.Join(' ', Enumerable.Repeat("needle", 250)));
        await owner.SaveAsync(crowded, "Actual bounded search source", default);
        var limited = new BoardsBrowserCore(repository); await limited.OpenAsync(crowded.Id);
        Check((await limited.FindBoardAsync("needle")).Value == 200 && Rows(limited).Length == 200,
            "Core publishes only200 real rows rather than materializing an unbounded UI list");
        limited.TryGetValue("BoardFindStatus", out var status);
        Check(status is string message && message.Contains("first 200", StringComparison.Ordinal), "Truncation is explicitly reported instead of inventing a total");
        await limited.CloseAsync(); await limited.DisposeAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "find", assertions,
            scope = "Actual canonical Notes repository/shared Boards core/Write engine; native/Home grants/focus/browser/standalone acceptance NOT RUN by this fixture" }));
    }

    private static BoardsFindRow[] Rows(BoardsBrowserCore core)
    { if (!core.TryGetValue("BoardFindMatches", out var value) || value is not BoardsFindRow[] rows) throw new InvalidOperationException("Missing real Find row binding."); return rows; }
    private static string Canonical(BoardsBrowserCore core) => JsonSerializer.Serialize(core.GetDocumentSnapshot());
    private sealed class CountedNotes(INotesRepository owner) : INotesRepository
    {
        public int Reads, Writes;
        public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token)
        { Writes++; return await owner.SaveAsync(document, reason, token); }
        public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) { Reads++; return owner.LoadAsync(id, token); }
        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token) { Reads++; return owner.ListAsync(token); }
        public Task DeleteAsync(Guid id, CancellationToken token) => owner.DeleteAsync(id, token);
        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => owner.GetVersionsAsync(id, token);
        public Task<NotesDocument?> LoadVersionAsync(Guid id, string version, CancellationToken token) => owner.LoadVersionAsync(id, version, token);
        public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => owner.RecoverLatestAsync(id, token);
        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => owner.SearchAsync(query, token);
    }
}
