using CakeOS.Cui;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace NineToOne.Web.Productivity.Boards;

// Observes the injected canonical Notes owner. A row is a current UI selection,
// never a read grant. Every history payload is read through that SAME owner.
public sealed partial class BoardsBrowserCore
{
    private bool _showBoardHistory;
    private long _historyEpoch, _historyRevision, _historyGeneration, _historyPreviewEpoch;
    private NotesDocument? _historyDocument, _historyPreview;
    private BoardsHistoryRow[] _historyRows = [];
    private BoardsHistoryPageRow[] _historyPages = [];
    private BoardsHistoryRow? _previewedHistoryRow;
    private WriteDocumentEditor? _historyEditor;
    private string _historyStatus = "Browse saved versions. Restoring creates an unsaved draft; Save creates a new version.";
    // Exact last source task/failure retained for diagnosis; all source tasks are awaited.
    // Original Home owns admission/cancellation and observes its underlying read task.
    internal Task? LastHistoryRead { get; private set; }
    internal Exception? LastHistoryReadFailure { get; private set; }
    internal bool IsHistoryPreview => _historyPreview is not null;
    internal WriteDocumentEditor? NativeHistoryEditor => _historyEditor;
    internal bool AllowHistorySelection => !IsBusy && !_disposed && !_presentationPending &&
        _pending is null && IsCurrentHistoryRow(_previewedHistoryRow) && _historyEditor is not null;
    private bool CanRestoreHistoryPreview => !IsBusy && CanRestoreHistory(_previewedHistoryRow);
    private bool CanRestoreHistory(BoardsHistoryRow? row) => IsCurrentHistoryRow(row) && !IsDirty && !IsReadOnly &&
        ActivePageId is { } page && VisiblePages.Any(value => value.Id == page) &&
        _boards.GetEditMode(_document!, page) == BoardsPageEditMode.Edit;

    private bool TryGetHistoryValue(string path, out object? value)
    {
        var visible = !_disposed && !_presentationPending && _pending is null && _document is not null;
        value = path switch
        {
            "ShowBoardHistory" => visible && _showBoardHistory,
            "IsHistoryPreview" => visible && IsHistoryPreview,
            "ShowCurrentBoard" => visible && !IsHistoryPreview,
            "BoardHistoryRows" => visible ? _historyRows : Array.Empty<BoardsHistoryRow>(),
            "BoardHistoryPages" => visible ? _historyPages : Array.Empty<BoardsHistoryPageRow>(),
            "BoardHistoryStatus" => visible ? ErrorCode is not null ? Status : _historyStatus : string.Empty,
            "HistoryPreviewTitle" => visible && _previewedHistoryRow is { } row ? "Saved version " + row.Info.Version : string.Empty,
            "HistoryPreviewPageTitle" => visible ? _historyPreview?.Sections.SelectMany(section => section.Pages)
                .SingleOrDefault(page => page.Id == _historyEditor?.PageScopeId)?.Title ?? string.Empty : string.Empty,
            "CanRestoreHistory" => visible && CanRestoreHistoryPreview,
            _ => null
        };
        return path is "ShowBoardHistory" or "IsHistoryPreview" or "ShowCurrentBoard" or "BoardHistoryRows" or
            "BoardHistoryPages" or "BoardHistoryStatus" or "HistoryPreviewTitle" or "HistoryPreviewPageTitle" or "CanRestoreHistory";
    }

    private void BeginHistoryRead(string status)
    { _historyStatus = status; Status = status; Notify(); }

    private async Task<T> ReadHistorySource<T>(Func<Task<T>> source)
    {
        LastHistoryRead = null; LastHistoryReadFailure = null;
        try { var actual = source() ?? throw new InvalidOperationException("The Notes owner returned no history task.");
            LastHistoryRead = actual; return await actual; }
        catch (Exception error) { LastHistoryReadFailure = error; throw; }
    }
    private Task<HomeCoreOperationResult<T>> HistoryOperation<T>(Func<CancellationToken, Task<HomeCoreOperationResult<T>>> action,
        CancellationToken token) => Operate(async ct =>
        {
            try { return await action(ct); }
            catch (NotSupportedException)
            { return Failure<T>("HistoryUnavailable", "This Notes owner does not support this history operation. No alternate store was opened."); }
            catch
            { _historyStatus = "History operation stopped. The current board and any previous preview are preserved."; Status = _historyStatus; throw; }
        }, token);

    public Task<HomeCoreOperationResult<int>> LoadBoardHistoryAsync(CancellationToken token = default) => HistoryOperation(async ct =>
    {
        if (_presentationPending || _document is null || _pending is not null)
            return Failure<int>("OperationDenied", "Open a board and finish save recovery before browsing versions.");
        var source = _document; var revision = DurableRevision; var generation = _generation;
        _showBoardHistory = true; BeginHistoryRead("Loading saved versions through Home; complete any permission review.");
        var versions = await ReadHistorySource(() => _repository.GetVersionsAsync(source.Id, ct));
        ct.ThrowIfCancellationRequested();
        if (!MatchesHistorySource(source, revision, generation))
            return Failure<int>("StaleHistory", "The current board changed. Open history again.");
        // Canonical repository retains a bounded history. Do not trust an unbounded UI list.
        var limited = versions.Take(201).ToArray();
        if (limited.Any(info => string.IsNullOrWhiteSpace(info.VersionId) || info.Version <= 0) ||
            limited.Select(info => info.VersionId).Distinct(StringComparer.Ordinal).Count() != limited.Length)
            return Failure<int>("InvalidHistory", "The Notes owner returned ambiguous saved-version metadata.");
        InvalidateBoardHistory(); _showBoardHistory = true;
        _historyDocument = source; _historyRevision = revision; _historyGeneration = generation;
        _historyRows = limited.Take(200).Select((info, index) => new BoardsHistoryRow(this, _historyEpoch, index, source.Id, info)).ToArray();
        _historyStatus = limited.Length > 200 ? "Showing the first 200 saved versions." : $"{_historyRows.Length} saved versions.";
        _historyStatus += IsDirty ? " Your current draft is preserved; save it before restoring a version." : " Preview is read-only. Restore creates a draft, not a saved replacement.";
        Status = _historyStatus; return Success(_historyRows.Length);
    }, token);

    public Task<HomeCoreOperationResult<bool>> PreviewBoardHistoryAsync(BoardsHistoryRow row, CancellationToken token = default) => HistoryOperation(async ct =>
    {
        if (!IsCurrentHistoryRow(row)) return Failure<bool>("StaleHistory", "Choose an original saved-version row from this board's current history.");
        BeginHistoryRead("Loading the selected saved copy through Home; the current board is preserved.");
        var snapshot = await ReadHistorySource(() => _repository.LoadVersionAsync(row.BoardId, row.Info.VersionId, ct));
        ct.ThrowIfCancellationRequested();
        if (!IsCurrentHistoryRow(row)) return Failure<bool>("StaleHistory", "The board changed while reading its saved version.");
        if (snapshot is null) return Failure<bool>("NotFound", "This saved version is no longer available.");
        if (snapshot.Id != row.BoardId || snapshot.Version != row.Info.Version)
            return Failure<bool>("VersionIdentityMismatch", "The saved version does not match the selected canonical identity.");
        var detached = Clone(snapshot);
        var pages = detached.Sections.SelectMany(section => section.Pages).ToArray();
        if (pages.Select(page => page.Id).Distinct().Count() != pages.Length)
            return Failure<bool>("InvalidHistory", "The saved version contains ambiguous page identities.");
        var pageId = pages.FirstOrDefault(page => page.Id == ActivePageId)?.Id ?? pages.FirstOrDefault()?.Id;
        var editor = pageId is { } id ? new WriteDocumentEditor(detached, id, initializeWithoutNormalization: true) : null;
        // Do not unbind/reinitialize the live editor: its exact draft and Undo survive preview.
        InvalidateBoardFind();
        _historyPreview = detached; _historyEditor = editor; _previewedHistoryRow = row;
        ++_historyPreviewEpoch;
        _historyPages = detached.Sections.SelectMany(section => section.Pages.OrderBy(page => page.Order)
            .Select((page, index) => new BoardsHistoryPageRow(this, _historyPreviewEpoch, page.Id,
                section.Title + " / " + page.Title + (BoardsWorkspaceService.IsPageDeleted(detached, page.Id) ? " · Trash" : string.Empty)))).ToArray();
        _showBoardHistory = true;
        _historyStatus = $"Previewing saved version {row.Info.Version}. Nothing in the current board has changed.";
        Status = _historyStatus; return Success(true);
    }, token);

    public Task<HomeCoreOperationResult<bool>> SelectBoardHistoryPageAsync(BoardsHistoryPageRow row, CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (!IsCurrentHistoryRow(_previewedHistoryRow) || _historyPreview is null || row is null ||
            !ReferenceEquals(row.Owner, this) || row.Epoch != _historyPreviewEpoch ||
            !_historyPages.Any(original => ReferenceEquals(original, row)))
            return Task.FromResult(Failure<bool>("StaleHistory", "Choose a page in the current saved-version preview."));
        _historyEditor = new WriteDocumentEditor(_historyPreview, row.PageId, initializeWithoutNormalization: true);
        return Task.FromResult(Success(true));
    }, token);

    public Task<HomeCoreOperationResult<bool>> RestoreBoardHistoryAsync(BoardsHistoryRow row, CancellationToken token = default) => HistoryOperation(async ct =>
    {
        if (!CanRestoreHistory(row)) return Failure<bool>("RestoreDenied", "Return to a clean, editable current board before restoring an original saved version.");
        var source = _document!; var revision = DurableRevision; var generation = _generation;
        // Both reads use the genuine current owner; cached metadata/preview is never admission.
        BeginHistoryRead("Checking the current saved board and original historical copy before restoring a draft.");
        var latest = await ReadHistorySource(() => _repository.LoadAsync(source.Id, ct));
        ct.ThrowIfCancellationRequested();
        if (!CanRestoreHistory(row) || !MatchesHistorySource(source, revision, generation))
            return Failure<bool>("StaleHistory", "The board changed while checking its current saved copy.");
        if (latest is null) return Failure<bool>("NotFound", "The current saved board is no longer available.");
        if (latest.Id != source.Id) return Failure<bool>("DocumentIdMismatch", "The saved board has a different canonical identity.");
        if (latest.Recovery.HasUnsavedRecovery || latest.Version != revision || !SameContent(latest, source))
            return Failure<bool>("RevisionConflict", "The current saved copy changed or needs recovery. The current board and preview are preserved.");
        var selected = await ReadHistorySource(() => _repository.LoadVersionAsync(source.Id, row.Info.VersionId, ct));
        ct.ThrowIfCancellationRequested();
        if (!CanRestoreHistory(row) || !MatchesHistorySource(source, revision, generation))
            return Failure<bool>("StaleHistory", "The board changed while reading the selected version.");
        if (selected is null) return Failure<bool>("NotFound", "This saved version is no longer available.");
        if (selected.Id != source.Id || selected.Version != row.Info.Version)
            return Failure<bool>("VersionIdentityMismatch", "The selected saved copy has a different canonical identity or revision.");
        if (!BoardsWorkspaceService.IsBoardsNotebook(selected) || BoardsWorkspaceService.IsDeletedNotebook(selected))
            return Failure<bool>("IncompatibleHistory", "This saved copy is not an active canonical Boards notebook. The current board is preserved.");
        var draft = Clone(selected); draft.Version = revision;
        // Keep the latest acknowledged CAS base; restoring never promotes an old durable version.
        // Pure shared-engine initialization preserves legacy runs and opaque/shared payloads.
        var restoredVersion = row.Info.Version;
        Attach(draft, false, initializeWithoutNormalization: true);
        ++_generation; IsDirty = true;
        _historyStatus = $"Restored version {restoredVersion} as an unsaved draft. Save creates a new version.";
        Status = _historyStatus; return Success(true);
    }, token);

    public Task<HomeCoreOperationResult<bool>> CloseBoardHistoryPreviewAsync(CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested(); ClearHistoryPreview();
        Status = IsDirty ? "Current unsaved draft preserved" : "Current saved board";
        _historyStatus = "Current board restored to view. Choose a saved version to preview again.";
        return Task.FromResult(Success(true));
    }, token);

    private bool MatchesHistorySource(NotesDocument source, long revision, long generation) =>
        !_disposed && !_presentationPending && _pending is null && ReferenceEquals(source, _document) &&
        DurableRevision == revision && _generation == generation;
    private bool IsCurrentHistoryRow(BoardsHistoryRow? row) => row is not null && _document is not null &&
        MatchesHistorySource(_historyDocument!, _historyRevision, _historyGeneration) &&
        ReferenceEquals(row.Owner, this) && row.BoardId == _document.Id && row.Epoch == _historyEpoch &&
        _historyRows.Any(original => ReferenceEquals(original, row));
    internal bool CanPreviewHistoryRow(BoardsHistoryRow row) => !IsBusy && IsCurrentHistoryRow(row);
    internal bool CanSelectHistoryPage(BoardsHistoryPageRow row) => AllowHistorySelection &&
        ReferenceEquals(row.Owner, this) && row.Epoch == _historyPreviewEpoch && _historyPages.Any(original => ReferenceEquals(original, row));
    private static bool IsHistoryPreviewAction(string command) => command is "ToggleHistory" or "LoadHistory" or "PreviewHistory" or
        "SelectHistoryPage" or "RestoreHistory" or "CloseHistoryPreview" or "ToggleSidebar" or "ToggleDetails";
    private void ClearHistoryPreview()
    { _historyPreview = null; _historyEditor = null; _previewedHistoryRow = null; _historyPages = []; ++_historyPreviewEpoch; }
    private void InvalidateBoardHistory()
    {
        ClearHistoryPreview(); _historyDocument = null; _historyRows = []; _showBoardHistory = false; ++_historyEpoch;
        _historyStatus = "Browse saved versions. Restoring creates an unsaved draft; Save creates a new version.";
    }
    private void InvalidateChangedBoardHistory()
    {
        if (_historyDocument is null || MatchesHistorySource(_historyDocument, _historyRevision, _historyGeneration)) return;
        var wasOpen = _showBoardHistory; InvalidateBoardHistory(); _showBoardHistory = wasOpen && _document is not null;
        _historyStatus = "The current board changed. Reload history before previewing or restoring.";
    }
    private async Task<bool> DispatchHistoryAsync(string command, object? parameter, CancellationToken token)
    {
        switch (command)
        {
            case "ToggleHistory":
                if (_showBoardHistory) { InvalidateBoardHistory(); Notify(); }
                else await LoadBoardHistoryAsync(token);
                return true;
            case "LoadHistory": await LoadBoardHistoryAsync(token); return true;
            case "PreviewHistory" when parameter is BoardsHistoryRow row: await PreviewBoardHistoryAsync(row, token); return true;
            case "SelectHistoryPage" when parameter is BoardsHistoryPageRow page: await SelectBoardHistoryPageAsync(page, token); return true;
            case "RestoreHistory" when _previewedHistoryRow is { } selected: await RestoreBoardHistoryAsync(selected, token); return true;
            case "CloseHistoryPreview": await CloseBoardHistoryPreviewAsync(token); return true;
            case "PreviewHistory": case "SelectHistoryPage": case "RestoreHistory":
                Failure<bool>("InvalidArgument", "Choose a current original history row or preview page."); Notify(); return true;
            default: return false;
        }
    }
}

public sealed class BoardsHistoryRow : ICuiBindingContext
{
    internal BoardsHistoryRow(BoardsBrowserCore owner, long epoch, int index, Guid boardId, NotesVersionInfo info)
    { Owner = owner; Epoch = epoch; BoardId = boardId; Info = info; Key = epoch + ":" + index;
        Label = $"Version {info.Version} · {info.CreatedAt.ToLocalTime():d MMM yyyy, HH:mm} · {info.Reason}"; }
    internal BoardsBrowserCore Owner { get; }
    internal long Epoch { get; }
    internal Guid BoardId { get; }
    internal NotesVersionInfo Info { get; }
    public string Key { get; }
    public string Label { get; }
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Key" => Key, "Label" or "AccessibleName" => Label, "CanPreview" => Owner.CanPreviewHistoryRow(this), _ => null };
        return path is "Key" or "Label" or "AccessibleName" or "CanPreview"; }
}
public sealed class BoardsHistoryPageRow : ICuiBindingContext
{
    internal BoardsHistoryPageRow(BoardsBrowserCore owner, long epoch, Guid pageId, string label)
    { Owner = owner; Epoch = epoch; PageId = pageId; Label = label; Key = epoch + ":" + pageId; }
    internal BoardsBrowserCore Owner { get; }
    internal long Epoch { get; }
    internal Guid PageId { get; }
    public string Key { get; }
    public string Label { get; }
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Key" => Key, "Label" => Label, "CanSelect" => Owner.CanSelectHistoryPage(this), _ => null };
        return path is "Key" or "Label" or "CanSelect"; }
}
