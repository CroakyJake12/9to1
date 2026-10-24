using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Productivity.Boards;

internal static class BoardsHistoryRegression
{
    public static async Task Run(INotesRepository actualRepository)
    {
        var assertions = 0;
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException(name); assertions++; Console.WriteLine("ASSERT " + name); }
        var source = new ObservedHistory(actualRepository);
        var owner = new BoardsWorkspaceService(source);
        var board = await owner.CreateNotebookAsync("Revision history");
        var page = board.Sections[0].Pages[0];
        var paragraph = owner.AddBlock(board, page.Id, NotesBlockKind.Paragraph, "Historical mechanics notes");
        paragraph.Runs = [new NotesTextRun { Text = "Historical ", Bold = true }, new NotesTextRun { Text = "mechanics notes", Italic = true }];
        paragraph.Metadata["foreign-extension"] = "{\"future-format\":[1,2,3]}";
        var drawing = owner.AddBlock(board, page.Id, NotesBlockKind.Canvas);
        using (var payload = JsonDocument.Parse("{\"schemaVersion\":77,\"unrecognizedExtension\":{\"points\":[1,2,3]}}"))
            drawing.SharedProductivityBundles = [payload.RootElement.Clone()];
        var legacy = owner.AddPage(board, board.Sections[0].Id, "Historical legacy page");
        legacy.Blocks[0].PlainText = "Original empty-run text"; legacy.Blocks[0].Runs.Clear();
        owner.SetEditMode(board, legacy.Id, BoardsPageEditMode.View);
        await owner.SaveAsync(board, "Rich and opaque shared payload fixture", default);
        var historicalVersion = board.Version;
        var historicalInfo = (await actualRepository.GetVersionsAsync(board.Id, default)).Single(info => info.Version == historicalVersion);
        var historical = (await actualRepository.LoadVersionAsync(board.Id, historicalInfo.VersionId, default))!;
        board.Title = "Current revision title";
        paragraph.Runs[0].Text = "Current "; paragraph.PlainText = "Current mechanics notes";
        await owner.SaveAsync(board, "Actual newer current board", default);
        var core = new BoardsBrowserCore(source); await core.OpenAsync(board.Id);
        var current = Canonical(core.GetDocumentSnapshot()!); var revision = core.DurableRevision; var writes = source.Writes;
        var liveEditor = core.NativeRichEditor;
        Check((await core.LoadBoardHistoryAsync()).Succeeded && Rows(core).Length >= 3 && source.HistoryLists == 1,
            "History metadata comes from the same actual canonical repository with real saved versions");
        var row = Rows(core).Single(value => value.Info.Version == historicalVersion);
        Check((await core.PreviewBoardHistoryAsync(row)).Succeeded && source.VersionReads == 1,
            "Original current row performs a fresh actual saved-version read");
        Check(ReferenceEquals(core.NativeRichEditor, liveEditor) && Canonical(core.GetDocumentSnapshot()!) == current &&
            !core.IsDirty && core.DurableRevision == revision && source.Writes == writes,
            "Preview preserves exact current document, live editor and acknowledged durable revision without writes");
        Check(Canonical(core.NativeHistoryEditor!.Document) == Canonical(historical) &&
            !ReferenceEquals(core.NativeHistoryEditor.Document, core.NativeRichEditor!.Document),
            "Preview is a detached full canonical version including rich runs and opaque shared payloads");
        Check(!core.AllowRetainedSelection && !core.RichEditingPermitted && core.AllowHistorySelection &&
            core.IsActionAvailable("Undo") == false && core.IsActionAvailable("Save") == false,
            "Actual retained live input and authoring commands are gated behind read-only preview");
        Check(!core.TrySetValue("IsEditMode", false) && !core.TrySetValue("PageTitle", "Hidden mutation") &&
            !(await core.InsertRichTextAsync("Hidden input")).Succeeded &&
            !(await core.AddSectionAsync("Hidden section")).Succeeded && !(await core.SaveAsync()).Succeeded &&
            !(await core.SelectPageAsync(legacy.Id)).Succeeded && Canonical(core.GetDocumentSnapshot()!) == current && source.Writes == writes,
            "Direct mutation, stale two-way mode/title bindings, page rebind and save refuse without hidden authoring");
        var legacyRow = Pages(core).Single(value => value.PageId == legacy.Id);
        Check((await core.SelectBoardHistoryPageAsync(legacyRow)).Succeeded && core.NativeHistoryEditor!.PageScopeId == legacy.Id &&
            Canonical(core.NativeHistoryEditor.Document) == Canonical(historical) && core.NativeRichEditor == liveEditor,
            "History page navigation uses stable IDs and pure shared-engine initialization without changing legacy runs or live Undo owner");
        var oldPreviewPage = legacyRow;
        await core.CloseBoardHistoryPreviewAsync();
        Check(!core.IsHistoryPreview && core.NativeRichEditor == liveEditor && core.AllowRetainedSelection &&
            Canonical(core.GetDocumentSnapshot()!) == current && !(await core.SelectBoardHistoryPageAsync(oldPreviewPage)).Succeeded,
            "Return to current restores the same live editor and rejects retired preview-page rows");
        var copied = new BoardsHistoryRow(core, row.Epoch, 0, row.BoardId, row.Info);
        var reads = source.VersionReads;
        Check(!(await core.PreviewBoardHistoryAsync(copied)).Succeeded && source.VersionReads == reads,
            "Copied public version coordinates cannot replace original issued row membership");
        var foreign = new BoardsBrowserCore(source); await foreign.OpenAsync(board.Id); await foreign.LoadBoardHistoryAsync();
        Check(!(await core.PreviewBoardHistoryAsync(Rows(foreign)[0])).Succeeded && source.VersionReads == reads,
            "Another editor owner over the same board cannot issue this owner's preview row");
        await foreign.CloseAsync(); await foreign.DisposeAsync();
        await core.InsertRichTextAsync("Current unsaved draft ");
        Check(core.IsDirty && Rows(core).Length == 0 && !(await core.PreviewBoardHistoryAsync(row)).Succeeded,
            "A genuine edited generation invalidates old rows before any version read");
        await core.LoadBoardHistoryAsync(); row = Rows(core).Single(value => value.Info.Version == historicalVersion);
        liveEditor = core.NativeRichEditor; var dirty = Canonical(core.GetDocumentSnapshot()!); var canUndo = liveEditor!.CanUndo;
        Check((await core.PreviewBoardHistoryAsync(row)).Succeeded && !(await core.RestoreBoardHistoryAsync(row)).Succeeded &&
            core.IsDirty && Canonical(core.GetDocumentSnapshot()!) == dirty && core.NativeRichEditor == liveEditor && liveEditor!.CanUndo == canUndo,
            "Browsing while dirty preserves the complete draft and Undo; restore refuses to overwrite it");
        await core.CloseBoardHistoryPreviewAsync();
        Check(core.NativeRichEditor == liveEditor && core.NativeRichEditor!.CanUndo == canUndo && (await core.UndoRichTextAsync()).Succeeded,
            "Leaving preview retains actual live Undo history rather than replacing its editor");
        await core.SaveAsync(); revision = core.DurableRevision;
        await core.LoadBoardHistoryAsync(); row = Rows(core).Single(value => value.Info.Version == historicalVersion);
        await core.PreviewBoardHistoryAsync(row);
        current = Canonical(core.GetDocumentSnapshot()!); var previewEditor = core.NativeHistoryEditor;
        source.NextListFailure = new UnauthorizedAccessException("Controlled genuine-owner denial");
        Check(!(await core.LoadBoardHistoryAsync()).Succeeded && core.ErrorCode == "PermissionDenied" &&
            core.NativeHistoryEditor == previewEditor && Canonical(core.GetDocumentSnapshot()!) == current &&
            ReferenceEquals(core.LastHistoryRead, source.LastTask) && core.LastHistoryRead!.IsFaulted &&
            ReferenceEquals(core.LastHistoryReadFailure, source.LastFailure),
            "Denied actual history read preserves preview/current state and retains original source fault");
        source.NextVersionFailure = new IOException("Controlled actual-owner read failure");
        Check(!(await core.PreviewBoardHistoryAsync(row)).Succeeded && core.ErrorCode == "ProviderUnavailable" &&
            core.NativeHistoryEditor == previewEditor && Canonical(core.GetDocumentSnapshot()!) == current,
            "Provider read failure preserves original preview and current board rather than reporting empty success");
        source.NextListFailure = new NotSupportedException("Controlled original route unavailable");
        Check(!(await core.LoadBoardHistoryAsync()).Succeeded && core.ErrorCode == "HistoryUnavailable" &&
            core.NativeHistoryEditor == previewEditor && source.Writes == writes + 1,
            "Unsupported original history refuses without raw-store fallback or substitute success");
        source.CancelNextList = true; var canceled = false;
        try { await core.LoadBoardHistoryAsync(); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled && core.LastHistoryRead!.IsCanceled && core.NativeHistoryEditor == previewEditor && !core.IsBusy,
            "Actual canceled history task drains and leaves the previous preview/current board intact");
        source.NextListFailure = new OperationCanceledException("Controlled faulted original task, not task cancellation");
        canceled = false; try { await core.LoadBoardHistoryAsync(); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled && core.LastHistoryRead!.IsFaulted && !core.LastHistoryRead!.IsCanceled && core.NativeHistoryEditor == previewEditor,
            "A faulted original task containing OCE is retained as faulted, not fabricated task cancellation");
        source.RewriteNextVersion = value => { value!.Id = Guid.NewGuid(); return value; };
        Check(!(await core.PreviewBoardHistoryAsync(row)).Succeeded && core.ErrorCode == "VersionIdentityMismatch" &&
            core.NativeHistoryEditor == previewEditor && Canonical(core.GetDocumentSnapshot()!) == current,
            "Unexpected version identity is rejected before replacing either editor");
        var existingReads = source.CurrentReads; var existingVersionReads = source.VersionReads; writes = source.Writes;
        Check((await core.RestoreBoardHistoryAsync(row)).Succeeded && source.CurrentReads == existingReads + 1 &&
            source.VersionReads == existingVersionReads + 1 && source.Writes == writes,
            "Explicit restore reauthorizes current and selected saved copy through the same original owner without a write");
        var restored = core.GetDocumentSnapshot()!; var expected = Clone(historical); expected.Version = revision;
        Check(core.IsDirty && !core.IsHistoryPreview && core.DurableRevision == revision && restored.Version == revision &&
            Canonical(restored) == Canonical(expected),
            "Restore stages the entire historical payload with latest acknowledged CAS base, never old revision promotion or normalization");
        Check((await core.SaveAsync()).Succeeded && !core.IsDirty && core.DurableRevision == revision + 1 && source.Writes == writes + 1,
            "Normal canonical Save creates exactly one new version from the restored unsaved draft");
        var actualSaved = (await actualRepository.LoadAsync(board.Id, default))!;
        Check(actualSaved.Version == revision + 1 && SameAuthored(actualSaved, historical) &&
            (await actualRepository.GetVersionsAsync(board.Id, default)).Any(info => info.Version == historicalVersion),
            "Independent actual repository read confirms preserved payload/IDs and retained prior history after restore-save");
        await core.LoadBoardHistoryAsync(); row = Rows(core).Single(value => value.Info.Version == historicalVersion);
        await core.PreviewBoardHistoryAsync(row); current = Canonical(core.GetDocumentSnapshot()!); previewEditor = core.NativeHistoryEditor;
        var competing = (await actualRepository.LoadAsync(board.Id, default))!; competing.Title = "Genuine independent winner";
        await actualRepository.SaveAsync(competing, "Actual independent history-restore race", default);
        writes = source.Writes;
        Check(!(await core.RestoreBoardHistoryAsync(row)).Succeeded && core.ErrorCode == "RevisionConflict" && !core.IsDirty &&
            Canonical(core.GetDocumentSnapshot()!) == current && core.NativeHistoryEditor == previewEditor && source.Writes == writes,
            "Newer actual durable writer rejects restore and preserves both current board and preview");
        await core.CloseBoardHistoryPreviewAsync(); await core.OpenAsync(board.Id);
        await core.SetEditModeAsync(page.Id, BoardsPageEditMode.View); await core.SaveAsync();
        await core.LoadBoardHistoryAsync(); row = Rows(core).Single(value => value.Info.Version == historicalVersion);
        Check(!(await core.RestoreBoardHistoryAsync(row)).Succeeded && core.ErrorCode == "RestoreDenied" && !core.IsDirty,
            "View mode cannot restore a saved version into authoring state");
        await core.SetEditModeAsync(page.Id, BoardsPageEditMode.Edit); await core.SaveAsync();
        await core.LoadBoardHistoryAsync(); row = Rows(core)[0];
        await core.CreateAsync("Different actual history board");
        Check(Rows(core).Length == 0 && !(await core.PreviewBoardHistoryAsync(row)).Succeeded,
            "Board switching retires old owner/revision history selections");
        var releaseRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.HoldNextList = releaseRead;
        var pendingRead = core.LoadBoardHistoryAsync();
        Check(core.IsBusy && !pendingRead.IsCompleted && ReferenceEquals(core.LastHistoryRead, source.LastTask) &&
            !core.TrySetValue("IsEditMode", false) && core.IsActionAvailable("Save") == false,
            "A genuinely pending source read retains its task and gates live editing until settlement");
        var closeAfterRead = core.CloseAsync();
        Check(!closeAfterRead.IsCompleted && core.DocumentId is not null,
            "Close joins the exact pending history operation rather than retiring its owner early");
        releaseRead.SetResult(true); await pendingRead; await closeAfterRead;
        Check(!core.IsBusy && Rows(core).Length == 0 && !(await core.LoadBoardHistoryAsync()).Succeeded,
            "Closed owner exposes no prior board history or preview");
        await core.DisposeAsync();
        var readOnly = new BoardsBrowserCore(source); await readOnly.OpenAsync(board.Id, readOnly: true); await readOnly.LoadBoardHistoryAsync();
        row = Rows(readOnly).Single(value => value.Info.Version == historicalVersion);
        Check((await readOnly.PreviewBoardHistoryAsync(row)).Succeeded && !(await readOnly.RestoreBoardHistoryAsync(row)).Succeeded && !readOnly.IsDirty,
            "Read-only owner may browse authorized saved copies but never restore a writable draft");
        await readOnly.CloseBoardHistoryPreviewAsync(); await readOnly.CloseAsync(); await readOnly.DisposeAsync();
        var prepared = new BoardsBrowserCore(source); prepared.PrepareUnadmittedPresentation(); await prepared.OpenAsync(board.Id);
        var lists = source.HistoryLists;
        Check(!(await prepared.LoadBoardHistoryAsync()).Succeeded && source.HistoryLists == lists && Rows(prepared).Length == 0,
            "Unadmitted presentation refuses history source calls and withholds metadata");
        prepared.AbandonUnadmittedPresentation();
        var loss = new LostReceipt(actualRepository); var uncertain = new BoardsBrowserCore(loss, unknownOutcome: error => ReferenceEquals(error, loss.Error));
        await uncertain.OpenAsync(board.Id); var p = uncertain.GetDocumentSnapshot()!.Sections[0].Pages[0];
        await uncertain.AddParagraphAsync(p.Id, "Actual uncertain next save"); loss.Lose = true; await uncertain.SaveAsync();
        Check(uncertain.HasUnknownCommitOutcome && !(await uncertain.LoadBoardHistoryAsync()).Succeeded && Rows(uncertain).Length == 0,
            "Unknown actual save outcome blocks version browsing/restore until original commit recovery");
        await uncertain.InspectDurableStateAsync(); await uncertain.CloseAsync(); await uncertain.DisposeAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "history", assertions,
            scope = "Actual canonical NotesRepository and shared Boards/Write owner; original native Home prompts, browser rendering and native acceptance NOT RUN by this fixture" }));
    }
    private static BoardsHistoryRow[] Rows(BoardsBrowserCore core)
    { if (!core.TryGetValue("BoardHistoryRows", out var value) || value is not BoardsHistoryRow[] rows) throw new InvalidOperationException("Missing original history rows."); return rows; }
    private static BoardsHistoryPageRow[] Pages(BoardsBrowserCore core)
    { if (!core.TryGetValue("BoardHistoryPages", out var value) || value is not BoardsHistoryPageRow[] rows) throw new InvalidOperationException("Missing original preview pages."); return rows; }
    private static NotesDocument Clone(NotesDocument value) => JsonSerializer.Deserialize<NotesDocument>(JsonSerializer.Serialize(value))!;
    private static string Canonical(NotesDocument value) => JsonSerializer.Serialize(value);
    private static bool SameAuthored(NotesDocument first, NotesDocument second)
    { var a = Clone(first); var b = Clone(second); a.Version = b.Version = 0; a.UpdatedAt = b.UpdatedAt = default; a.Recovery = new(); b.Recovery = new(); return Canonical(a) == Canonical(b); }

    // Transparent controlled failures around real repository operations; no storage/grant substitute.
    private sealed class ObservedHistory(INotesRepository actual) : INotesRepository
    {
        public int Writes, HistoryLists, VersionReads, CurrentReads;
        public Exception? NextListFailure, NextVersionFailure, LastFailure;
        public bool CancelNextList;
        public TaskCompletionSource<bool>? HoldNextList;
        public Task? LastTask;
        public Func<NotesDocument?, NotesDocument?>? RewriteNextVersion;
        public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token)
        { Writes++; return await actual.SaveAsync(document, reason, token); }
        public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token)
        { CurrentReads++; return actual.LoadAsync(id, token); }
        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token) => actual.ListAsync(token);
        public Task DeleteAsync(Guid id, CancellationToken token) => actual.DeleteAsync(id, token);
        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token)
        {
            HistoryLists++;
            if (NextListFailure is { } error) { NextListFailure = null; LastFailure = error;
                var fault = Task.FromException<IReadOnlyList<NotesVersionInfo>>(error); LastTask = fault; return fault; }
            if (CancelNextList) { CancelNextList = false;
                var canceled = Task.FromCanceled<IReadOnlyList<NotesVersionInfo>>(new CancellationToken(true)); LastTask = canceled; return canceled; }
            var hold = HoldNextList; HoldNextList = null;
            var source = hold is null ? actual.GetVersionsAsync(id, token) : HeldList(id, hold.Task, token);
            LastTask = source; return source;
        }
        private async Task<IReadOnlyList<NotesVersionInfo>> HeldList(Guid id, Task release, CancellationToken token)
        { var versions = await actual.GetVersionsAsync(id, token); await release.WaitAsync(token); return versions; }
        public Task<NotesDocument?> LoadVersionAsync(Guid id, string version, CancellationToken token)
        {
            VersionReads++;
            if (NextVersionFailure is { } error) { NextVersionFailure = null; LastFailure = error;
                var fault = Task.FromException<NotesDocument?>(error); LastTask = fault; return fault; }
            var rewrite = RewriteNextVersion; RewriteNextVersion = null;
            var source = rewrite is null ? actual.LoadVersionAsync(id, version, token) : RewriteVersion(id, version, rewrite, token);
            LastTask = source; return source;
        }
        private async Task<NotesDocument?> RewriteVersion(Guid id, string version, Func<NotesDocument?, NotesDocument?> rewrite, CancellationToken token) =>
            rewrite(await actual.LoadVersionAsync(id, version, token));
        public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => actual.RecoverLatestAsync(id, token);
        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => actual.SearchAsync(query, token);
    }
}
