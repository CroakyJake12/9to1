using System.ComponentModel;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace NineToOne.Web.Productivity.Boards;

/// <summary>Host/storage capability metadata only. CUI chooses its own setup/workspace
/// composition; this does not assert artifact ownership, content consent or readiness.</summary>
public sealed record BoardsHostStorageCapabilities(bool RequiresOriginalStoreSetup)
{
    public bool UsesProvidedWorkspace => !RequiresOriginalStoreSetup;
}

/// <summary>
/// Browser lifecycle over the maintained BoardsWorkspaceService and injected real
/// canonical Notes repository. It owns no replacement document or persistence model.
/// The CUI workspace borrows this owner's page-scoped retained editor.
/// </summary>
public sealed partial class BoardsBrowserCore : ICuiWritableBindingContext, ICuiResultReportingDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly INotesRepository _repository;
    private readonly ObservedNotesRepository _writes;
    private readonly BoardsWorkspaceService _boards;
    private readonly Func<Exception, bool> _unknown;
    private readonly string? _storageDetail;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private NotesDocument? _document;
    private IReadOnlyList<NotesDocumentSummary> _notebooks = [];
    private PendingCommit? _pending;
    private bool _presentationPending, _disposed;
    private long _generation;
    public Guid? DocumentId => _document?.Id;
    public long DurableRevision { get; private set; }
    public bool IsDirty { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsReadOnly { get; private set; }
    public bool HasUnknownCommitOutcome => _pending is not null;
    public bool IsPresentationAdmitted => !_presentationPending;
    internal long EditGeneration => _generation;
    public string Status { get; private set; } = "Choose a board or create a new one";
    public string? ErrorCode { get; private set; }
    public NotesSaveResult? LastAcknowledgedSave { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public async ValueTask<CuiCommandOutcome> DispatchWithOutcomeAsync(string command, object? parameter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (command == "boards.host.observe")
            return new(true, false, "BoardsHostStorageCapabilities", null, new BoardsHostStorageCapabilities(false));
        await DispatchAsync(command, parameter, cancellationToken);
        return new(ErrorCode is null, false, ErrorCode ?? "Succeeded", Status, null);
    }

    public BoardsBrowserCore(INotesRepository authorisedRepository, INotesAttachmentStore? attachments = null,
        Func<Exception, bool>? unknownOutcome = null, IBoardsNotebookSummarySource? summarySource = null,
        string? storageDetail = null)
    {
        _repository = authorisedRepository ?? throw new ArgumentNullException(nameof(authorisedRepository));
        _writes = new(authorisedRepository); _boards = new(_writes, attachments, summarySource);
        _unknown = unknownOutcome ?? (_ => false);
        _storageDetail = storageDetail;
    }
    public NotesDocument? GetDocumentSnapshot() => _document is null ? null : Clone(_document);
    public async Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token = default)
    { _notebooks = await _boards.ListNotebooksAsync(token); return _notebooks; }
    internal void PrepareUnadmittedPresentation()
    {
        if (_document is not null || IsBusy || _disposed) throw new InvalidOperationException("Only a fresh core can prepare a view.");
        _presentationPending = true;
    }
    internal void AdmitPresentation()
    {
        if (!_presentationPending || IsBusy || _disposed) throw new InvalidOperationException("This view cannot be admitted.");
        _presentationPending = false;
        try { Notify(); } catch { _presentationPending = true; throw; }
    }
    internal void WithdrawUntransferredPresentation(Guid? id, long revision, long generation)
    {
        if (IsBusy || _disposed || DocumentId != id || DurableRevision != revision || _generation != generation)
            throw new InvalidOperationException("A changed core cannot be rolled back as an untransferred view.");
        _presentationPending = true;
    }
    /// <summary>Freeze the exact clean owner snapshot after actual close preparation. No
    /// save, ownership or discard is implied. The native preparation keeps this same core
    /// and restores it with AdmitPresentation if external retirement is aborted.</summary>
    internal bool TrySuspendPreparedPresentation(Guid? id, long revision, long generation)
    {
        if (_disposed || _presentationPending || IsBusy || IsDirty || _pending is not null ||
            _operations.CurrentCount != 1 || DocumentId != id || DurableRevision != revision || _generation != generation) return false;
        _presentationPending = true;
        try { Notify(); } catch { _presentationPending = false; throw; }
        return true;
    }
    internal void AbandonUnadmittedPresentation()
    {
        if (!_presentationPending || IsBusy || _operations.CurrentCount != 1) throw new InvalidOperationException("An active core cannot be abandoned.");
        InvalidateBoardFind();
        UnbindRichEditor(); _document = null; _disposed = true;
    }
    public Task<HomeCoreOperationResult<Guid>> OpenAsync(Guid id, bool readOnly = false, CancellationToken token = default) => Operate(async ct =>
    {
        if (_presentationPending && _document is not null) return Failure<Guid>("PresentationNotAdmitted", "This prepared view is not active.");
        if (id == Guid.Empty) return Failure<Guid>("InvalidArgument", "Choose a saved notebook.");
        if (!await SaveBeforeLeaving(ct)) return Failed<Guid>();
        var document = await _boards.OpenNotebookAsync(id, ct); ct.ThrowIfCancellationRequested();
        if (document is null) return Failure<Guid>("NotFound", "This Boards notebook could not be found.");
        if (document.Id != id) return Failure<Guid>("DocumentIdMismatch", "The stored identity differs from this notebook link.");
        Attach(document, readOnly);
        IsDirty = !readOnly && (IsDirty || document.Recovery.HasUnsavedRecovery);
        if (IsDirty) ++_generation;
        Status = readOnly ? "Read-only" : IsDirty ? "Recovered notebook; review and save" : "Saved";
        return Success(id);
    }, token);
    public Task<HomeCoreOperationResult<Guid>> CreateAsync(string title = "Untitled board", CancellationToken token = default) => Operate(async ct =>
    {
        if (_presentationPending) return Failure<Guid>("PresentationNotAdmitted", "This prepared view is not active.");
        if (!await SaveBeforeLeaving(ct)) return Failed<Guid>();
        _writes.Reset();
        try
        {
            var document = await _boards.CreateNotebookAsync(title, ct);
            var receipt = _writes.Receipt ?? throw new InvalidDataException("The owner returned without a canonical save receipt.");
            Attach(document, false); Acknowledge(receipt); return Success(document.Id);
        }
        catch (Exception error) when (_unknown(error) && _writes.Snapshot is { } snapshot)
        {
            _pending = new(snapshot, snapshot.Version, _generation);
            Attach(snapshot, false); IsDirty = true; throw;
        }
    }, token);
    public Task<HomeCoreOperationResult<Guid>> AddSectionAsync(string? title = null, CancellationToken token = default) =>
        Edit((owner, document) => owner.AddSection(document, title).Id, token);
    public Task<HomeCoreOperationResult<Guid>> AddPageAsync(Guid sectionId, string? title = null, CancellationToken token = default) =>
        Edit((owner, document) => owner.AddPage(document, sectionId, title).Id, token);
    public Task<HomeCoreOperationResult<Guid>> AddParagraphAsync(Guid pageId, string text, CancellationToken token = default) =>
        Edit((owner, document) => owner.AddBlock(document, pageId, NotesBlockKind.Paragraph, text).Id, token);
    public Task<HomeCoreOperationResult<bool>> SetEditModeAsync(Guid pageId, BoardsPageEditMode mode, CancellationToken token = default) =>
        Edit((owner, document) => { owner.SetEditMode(document, pageId, mode); return true; }, token);
    public Task<HomeCoreOperationResult<bool>> SetLayoutModeAsync(Guid pageId, BoardsPageLayoutMode mode, CancellationToken token = default) =>
        Edit((owner, document) => { owner.SetLayoutMode(document, pageId, mode); return true; }, token);
    public Task<HomeCoreOperationResult<Guid>> AddCanvasTextAsync(Guid pageId, string text, double x, double y, CancellationToken token = default) =>
        Edit((owner, document) => owner.AddCanvasObject(document, pageId, NotesCanvasObjectKind.Text, text, x, y).Id, token);
    public Task<HomeCoreOperationResult<bool>> MoveCanvasObjectAsync(Guid pageId, Guid objectId, double x, double y, CancellationToken token = default) =>
        Edit((owner, document) => owner.MoveCanvasObject(document, pageId, objectId, x, y), token);
    public Task<HomeCoreOperationResult<bool>> UpdateCanvasTextAsync(Guid pageId, Guid objectId, string text, CancellationToken token = default) =>
        Edit((owner, document) => owner.UpdateCanvasObjectText(document, pageId, objectId, text), token);
    private Task<HomeCoreOperationResult<T>> Edit<T>(Func<IBoardsWorkspaceService, NotesDocument, T> action, CancellationToken token) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (_presentationPending) return Task.FromResult(Failure<T>("PresentationNotAdmitted", "This prepared view is not active."));
        if (_document is null) return Task.FromResult(Failure<T>("NotFound", "Open a notebook first."));
        if (IsReadOnly) return Task.FromResult(Failure<T>("PermissionDenied", "This notebook is read-only."));
        if (_pending is not null) return Task.FromResult(Failure<T>("CommitOutcomeUnknown", "Inspect saved state before changing this preserved draft."));
        var candidate = Clone(_document);
        T result;
        try { result = action(_boards, candidate); } // The maintained owner enforces page/layout rules.
        catch (InvalidOperationException) { return Task.FromResult(Failure<T>("OperationDenied", "The owner denied this notebook operation. The draft is preserved.")); }
        if (JsonSerializer.Serialize(candidate, Json) != JsonSerializer.Serialize(_document, Json))
        { Attach(candidate, IsReadOnly); ++_generation; IsDirty = true; Status = "Unsaved changes"; }
        return Task.FromResult(Success(result));
    }, token);
    public Task<HomeCoreOperationResult<bool>> SaveAsync(CancellationToken token = default) => Operate(SaveCore, token);
    private async Task<HomeCoreOperationResult<bool>> SaveCore(CancellationToken ct)
    {
        if (_presentationPending) return Failure<bool>("PresentationNotAdmitted", "This prepared view is not active.");
        if (_pending is not null) return Failure<bool>("CommitOutcomeUnknown", "Save may already be committed; inspect saved state before retrying.");
        if (_document is null) return Failure<bool>("NotFound", "Open a notebook first.");
        if (IsReadOnly) return Failure<bool>("PermissionDenied", "This notebook is read-only.");
        if (!IsDirty) return Success(true);
        var candidate = Clone(_document); candidate.Version = DurableRevision;
        var intended = Clone(candidate); var generation = _generation;
        _writes.Reset();
        try { await _boards.SaveAsync(candidate, "Saved Boards notebook", ct); }
        catch (Exception error) when (_unknown(error)) { _pending = new(intended, DurableRevision, generation); throw; }
        var receipt = _writes.Receipt ?? throw new InvalidDataException("The owner returned without a canonical save receipt.");
        _document.Version = receipt.Version; _document.UpdatedAt = candidate.UpdatedAt; _document.Recovery = candidate.Recovery;
        IsDirty = generation != _generation; Acknowledge(receipt); return Success(true);
    }
    public Task<HomeCoreOperationResult<bool>> InspectDurableStateAsync(CancellationToken token = default) => Operate(async ct =>
    {
        if (_presentationPending) return Failure<bool>("PresentationNotAdmitted", "This prepared view is not active.");
        var pending = _pending; if (pending is null) return Success(true);
        var saved = await _repository.LoadAsync(pending.Snapshot.Id, ct);
        if (saved is not null && saved.Id != pending.Snapshot.Id) return Failure<bool>("DocumentIdMismatch", "The saved identity differs from this draft.");
        if (saved?.Recovery.HasUnsavedRecovery == true) return Failure<bool>("CommitOutcomeUnknown", "A recovery copy cannot confirm the uncertain current commit.");
        if (saved is not null && saved.Version == checked(pending.BaseRevision + 1) && SameContent(saved, pending.Snapshot))
        {
            _document!.Version = saved.Version; _document.UpdatedAt = saved.UpdatedAt; _document.Recovery = saved.Recovery;
            DurableRevision = saved.Version; IsDirty = pending.Generation != _generation; _pending = null;
            Status = IsDirty ? "Saved copy verified; newer edits remain unsaved" : "Saved copy verified"; return Success(true);
        }
        if (saved is null && pending.BaseRevision == 0 || saved?.Version == pending.BaseRevision)
        { _pending = null; Status = "Saved state inspected; draft remains unsaved. Save explicitly to retry."; return Success(true); }
        return Failure<bool>("RevisionConflict", "Stored content differs from the preserved draft; resolve the conflict before saving.");
    }, token);
    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken token = default) => Operate(async ct =>
        await SaveBeforeLeaving(ct) ? Success(true) : Failed<bool>(), token);
    public Task<HomeCoreOperationResult<bool>> CloseAsync(CancellationToken token = default) => Operate(async ct =>
    {
        if (_presentationPending) return Failure<bool>("PresentationNotAdmitted", "This prepared view is not active.");
        if (!await SaveBeforeLeaving(ct)) return Failed<bool>();
        InvalidateBoardFind();
        UnbindRichEditor(); _document = null; ActivePageId = null; DurableRevision = 0; IsDirty = false; IsReadOnly = false; Status = "Choose a board or create a new one"; return Success(true);
    }, token);
    private async Task<bool> SaveBeforeLeaving(CancellationToken ct)
    {
        if (_pending is not null) { Failure<bool>("CommitOutcomeUnknown", "Inspect saved state before leaving this preserved draft."); return false; }
        if (!IsDirty) return true;
        if (!(await SaveCore(ct)).Succeeded) return false;
        if (!IsDirty) return true;
        Failure<bool>("UnsavedChanges", "Newer changes remain unsaved; save before leaving."); return false;
    }
    private void Attach(NotesDocument document, bool readOnly)
    {
        InvalidateBoardFind();
        var sameBoard = _document?.Id == document.Id;
        UnbindRichEditor(); _document = document; DurableRevision = document.Version; IsReadOnly = readOnly; IsDirty = false;
        if (!sameBoard || !VisiblePages.Any(page => page.Id == ActivePageId)) ActivePageId = VisiblePages.FirstOrDefault()?.Id;
        BindRichEditor();
    }
    private void Acknowledge(NotesSaveResult receipt)
    {
        if (_document?.Id != receipt.DocumentId) throw new InvalidDataException("The canonical receipt identity differs from this notebook.");
        LastAcknowledgedSave = receipt; DurableRevision = receipt.Version;
        RememberBoard();
        Status = IsDirty ? "Saved copy acknowledged; newer changes remain unsaved" : "Saved";
        if (!receipt.VersionHistoryComplete) Status += "; version history needs attention";
        if (!string.IsNullOrWhiteSpace(receipt.PostCommitWarning)) Status += "; " + receipt.PostCommitWarning;
    }
    private async Task<HomeCoreOperationResult<T>> Operate<T>(Func<CancellationToken, Task<HomeCoreOperationResult<T>>> action, CancellationToken token)
    {
        await _operations.WaitAsync(token);
        try { if (_disposed) return Failure<T>("Disposed", "This notebook is closed."); IsBusy = true; ErrorCode = null; Notify(); return await action(token); }
        catch (NotesRevisionConflictException) { return Failure<T>("RevisionConflict", "A newer stored revision exists. The draft is preserved."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (_pending is not null && _unknown(error)) { return Failure<T>("CommitOutcomeUnknown", "Save may already be committed; inspect saved state before retrying."); }
        catch (UnauthorizedAccessException) { return Failure<T>("PermissionDenied", "The operation is not permitted. The draft is preserved."); }
        catch (ArgumentException) { return Failure<T>("InvalidArgument", "The notebook operation is invalid."); }
        catch (KeyNotFoundException) { return Failure<T>("NotFound", "The requested notebook object is unavailable."); }
        catch (Exception error) when (error is IOException or JsonException) { return Failure<T>("ProviderUnavailable", "The notebook operation could not complete. The draft is preserved."); }
        finally { IsBusy = false; try { Notify(); } finally { _operations.Release(); } }
    }
    public bool TryGetValue(string path, out object? value)
    {
        // Host wording describes the actual same repository location, never a storage guarantee.
        // Browser callers retain their existing browser-data notice; native composition supplies
        // its genuine device-store description without forking the shared CUI workspace.
        if (path == "StorageDetail" && _storageDetail is not null) { value = _storageDetail; return true; }
        if (TryGetFindValue(path, out value)) return true;
        if (TryGetWorkspaceValue(path, out value)) return true;
        value = path switch
        {
            "Title" => _document?.Title ?? "Boards", "Status" => Status,
            "Notebooks" => _notebooks.Where(summary => string.IsNullOrWhiteSpace(_boardSearch) || summary.Title.Contains(_boardSearch, StringComparison.OrdinalIgnoreCase)).Select(summary => new BoardsNotebookRow(summary)).ToArray(),
            "HasUnknownCommitOutcome" => HasUnknownCommitOutcome,
            "CanCreate" => IsActionAvailable("Create") == true, "CanOpen" => IsActionAvailable("Open") == true,
            "CanSave" => IsActionAvailable("Save") == true, "CanClose" => IsActionAvailable("Close") == true,
            "CanInspect" => IsActionAvailable("Inspect") == true,
            "CanBold" => IsActionAvailable("Bold") == true, "CanUndo" => IsActionAvailable("Undo") == true,
            "CanRedo" => IsActionAvailable("Redo") == true,
            _ => null
        };
        return path is "Title" or "Status" or "Notebooks" or "HasUnknownCommitOutcome" or "CanCreate" or "CanOpen" or "CanSave" or "CanClose" or "CanInspect" or "CanBold" or "CanUndo" or "CanRedo";
    }
    public bool? IsActionAvailable(string command) => !_disposed && !_presentationPending && !IsBusy &&
        (_pending is null || command is "Inspect" or "Refresh") && (command switch
        {
            "Create" or "Open" or "Refresh" => true, "Save" => _document is not null && !IsReadOnly,
            "Close" => _document is not null, "Inspect" => _pending is not null,
            "Bold" => RichEditingPermitted, "Undo" => RichEditingPermitted && _richEditor?.CanUndo == true,
            "Redo" => RichEditingPermitted && _richEditor?.CanRedo == true,
            "ToggleFind" or "FindBoard" or "OpenFindMatch" => _document is not null,
            _ => IsWorkspaceActionAvailable(command)
        });
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true) { Failure<bool>("OperationDenied", "This notebook action is unavailable."); Notify(); return; }
        if (await DispatchFindAsync(command, parameter, cancellationToken)) return;
        if (await DispatchWorkspaceAsync(command, parameter, cancellationToken)) return;
        switch (command)
        {
            case "Create": await CreateAsync(token: cancellationToken); break;
            case "Open" when parameter is BoardsNotebookRow row && _notebooks.Any(item => item.Id == row.Summary.Id):
                if ((await OpenAsync(row.Summary.Id, token: cancellationToken)).Succeeded) { _showPicker = false; Notify(); } break;
            case "Save": await SaveAsync(cancellationToken); break;
            case "Bold": await BoldRichSelectionAsync(cancellationToken); break;
            case "Undo": await UndoRichTextAsync(cancellationToken); break;
            case "Redo": await RedoRichTextAsync(cancellationToken); break;
            case "Close": await CloseAsync(cancellationToken); break;
            case "Inspect": await InspectDurableStateAsync(cancellationToken); break;
            case "Refresh": await ListAsync(cancellationToken); Notify(); break;
            default: Failure<bool>("InvalidArgument", "Choose a currently listed notebook."); Notify(); break;
        }
    }
    private void Notify()
    {
        InvalidateChangedBoardFind();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    private HomeCoreOperationResult<T> Success<T>(T value) => new(true, "Succeeded", Status, value, Revision: DurableRevision);
    private HomeCoreOperationResult<T> Failed<T>() => new(false, ErrorCode ?? "ProviderUnavailable", Status);
    private HomeCoreOperationResult<T> Failure<T>(string code, string message) { ErrorCode = code; Status = message; return new(false, code, message); }
    private static NotesDocument Clone(NotesDocument document) => JsonSerializer.Deserialize<NotesDocument>(JsonSerializer.Serialize(document, Json), Json)
        ?? throw new InvalidDataException("The canonical notebook snapshot could not be read.");
    private static bool SameContent(NotesDocument first, NotesDocument second)
    {
        static string Content(NotesDocument document) { var copy = Clone(document); copy.Version = 0; copy.UpdatedAt = default; copy.Recovery = new(); return JsonSerializer.Serialize(copy, Json); }
        return Content(first) == Content(second);
    }
    public async ValueTask DisposeAsync()
    {
        await _operations.WaitAsync();
        try { if (_disposed) return; if (IsDirty || _pending is not null) throw new InvalidOperationException("Save or resolve this notebook before disposal."); InvalidateBoardFind(); UnbindRichEditor(); _document = null; _disposed = true; }
        finally { _operations.Release(); }
    }
    private sealed record PendingCommit(NotesDocument Snapshot, long BaseRevision, long Generation);
    // Transparent observation of the maintained owner's canonical create/save
    // payload and receipt. All eight methods delegate to the injected real backend;
    // this decorator contains no storage, ID generation, schema or CAS substitute.
    private sealed class ObservedNotesRepository(INotesRepository owner) : INotesRepository
    {
        public NotesDocument? Snapshot { get; private set; }
        public NotesSaveResult? Receipt { get; private set; }
        public void Reset() { Snapshot = null; Receipt = null; }
        public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token)
        { Snapshot = Clone(document); Receipt = await owner.SaveAsync(document, reason, token); return Receipt; }
        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token) => owner.ListAsync(token);
        public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) => owner.LoadAsync(id, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => owner.DeleteAsync(id, token);
        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => owner.GetVersionsAsync(id, token);
        public Task<NotesDocument?> LoadVersionAsync(Guid id, string version, CancellationToken token) => owner.LoadVersionAsync(id, version, token);
        public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => owner.RecoverLatestAsync(id, token);
        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => owner.SearchAsync(query, token);
    }
}

public sealed record BoardsNotebookRow(NotesDocumentSummary Summary) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Id" => Summary.Id, "Title" => Summary.Title, "Version" => Summary.Version,
            "Detail" => $"{Summary.BlockCount} blocks · {Summary.UpdatedAt.ToLocalTime():d MMM, HH:mm}", _ => null };
        return path is "Id" or "Title" or "Version" or "Detail";
    }
}
