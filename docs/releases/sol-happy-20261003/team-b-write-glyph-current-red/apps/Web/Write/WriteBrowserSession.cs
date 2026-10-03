using System.ComponentModel;
using System.Text.Json;
using CakeOS.Cui;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace NineToOne.Web.Write;

/// <summary>
/// Browser presentation/lifecycle adapter over the owning Write editor and repository.
/// The composition root supplies an authorised repository; this class is neither a
/// document model nor an authentication, permission, storage or collaboration service.
/// </summary>
public sealed class WriteBrowserSession : ICuiBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);
    private readonly INotesRepository _repository;
    private readonly IWriteNativeDocumentPackageStore _packages;
    private readonly Func<Exception, bool> _isUnknownCommitOutcome;
    private readonly IWriteBrowserPackageBroker? _packageBroker;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private NotesDocument? _document;
    private WriteDocumentEditor? _editor;
    private long _durableVersion;
    private long _editGeneration;
    private bool _disposed;
    private IReadOnlyList<NotesDocumentSummary> _documents = [];
    private PendingWriteCommit? _pendingCommit;
    private bool _presentationPending;

    public WriteBrowserSession(INotesRepository authorisedRepository, IWriteNativeDocumentPackageStore packages,
        Func<Exception, bool>? isUnknownCommitOutcome = null, IWriteBrowserPackageBroker? packageBroker = null)
    {
        _repository = authorisedRepository ?? throw new ArgumentNullException(nameof(authorisedRepository));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _isUnknownCommitOutcome = isUnknownCommitOutcome ?? (_ => false);
        _packageBroker = packageBroker;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DocumentOpened;
    internal WriteDocumentEditor? Editor => _editor;
    public Guid? DocumentId => _document?.Id;
    public bool IsDirty { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsReadOnly { get; private set; }
    public string Status { get; private set; } = "No document open";
    public string? ErrorCode { get; private set; }
    public long DurableRevision => _durableVersion;
    public bool HasUnknownCommitOutcome => _pendingCommit is not null;
    public Guid? PendingCommitDocumentId => _pendingCommit?.Snapshot.Id;
    public string? PackageCleanupWarning { get; private set; }
    public Exception? LastPackageCleanupError { get; private set; }
    public bool IsPresentationAdmitted => !_presentationPending;
    internal long EditGeneration => _editGeneration;

    internal void PrepareUnadmittedPresentation()
    {
        if (_document is not null || IsBusy || _disposed) throw new InvalidOperationException("Only a fresh session can prepare an unadmitted view.");
        _presentationPending = true;
    }
    internal void AdmitPresentation()
    {
        if (!_presentationPending || IsBusy || _disposed) throw new InvalidOperationException("This prepared view cannot be admitted.");
        _presentationPending = false;
        try { Notify(); } catch { _presentationPending = true; throw; }
    }
    internal void WithdrawUntransferredPresentation(Guid? id, long revision, long generation)
    {
        if (IsBusy || _disposed || DocumentId != id || DurableRevision != revision || _editGeneration != generation)
            throw new InvalidOperationException("An authored or changed presentation cannot be withdrawn as an untransferred snapshot.");
        _presentationPending = true;
    }
    internal void AbandonUnadmittedPresentation()
    {
        if (!_presentationPending || IsBusy || _operations.CurrentCount != 1)
            throw new InvalidOperationException("An active or busy editor cannot be abandoned as an unadmitted snapshot.");
        // These owner-initialized/recovered bytes were loaded for an unaccepted
        // presentation. Input and typed mutations have never been admitted.
        Detach(); _disposed = true;
    }

    public NotesDocument? GetDocumentSnapshot() => _document is null ? null : Clone(_document);

    public Task<HomeCoreOperationResult<bool>> RefreshDocumentsAsync(CancellationToken cancellationToken = default) =>
        OperateAsync(async token =>
        {
            _documents = await _repository.ListAsync(token);
            return Success(true);
        }, cancellationToken);

    /// <summary>Reads the same canonical identity before allowing any explicit retry.</summary>
    public Task<HomeCoreOperationResult<bool>> InspectDurableStateAsync(CancellationToken cancellationToken = default) =>
        OperateAsync(async token =>
        {
            var pending = _pendingCommit;
            if (pending is null) return Success(true);
            var stored = await _repository.LoadAsync(pending.Snapshot.Id, token);
            if (stored is not null && stored.Id != pending.Snapshot.Id)
                return Failure<bool>("DocumentIdMismatch", "The saved document identity does not match this draft.");
            if (stored?.Recovery.HasUnsavedRecovery == true)
                return Failure<bool>("CommitOutcomeUnknown", "Storage returned a recovery copy. The draft is preserved; inspect history before replacing it.");
            if (stored is not null && stored.Version == checked(pending.BaseRevision + 1) && SameAuthoredContent(stored, pending.Snapshot))
            {
                // Only the actual loaded owner's revision resolves the lost acknowledgment.
                _durableVersion = stored.Version;
                _document!.Version = stored.Version;
                _document.UpdatedAt = stored.UpdatedAt;
                _document.Recovery = stored.Recovery;
                IsDirty = pending.EditGeneration != _editGeneration;
                _pendingCommit = null;
                RememberSavedDocument(stored);
                Status = IsDirty ? "Saved copy verified; newer draft changes remain unsaved" : "Saved copy verified";
                return Success(true);
            }
            if (stored is null && pending.BaseRevision == 0 || stored?.Version == pending.BaseRevision)
            {
                _pendingCommit = null;
                Status = "Inspected saved state; draft remains unsaved. Save explicitly to retry.";
                return Success(true);
            }
            return Failure<bool>("RevisionConflict", "Saved state differs from this draft. The draft is preserved; resolve the conflict before saving.");
        }, cancellationToken);

    public Task<HomeCoreOperationResult<Guid>> CreateAsync(string title = "Untitled document",
        CancellationToken cancellationToken = default) => OperateAsync(async token =>
    {
        if (_presentationPending) return Failure<Guid>("PresentationNotAdmitted", "This prepared view is not active.");
        if (!await SaveBeforeLeavingAsync(token)) return Failed<Guid>();
        var candidate = NotesDocument.Create(title);
        // The owning editor initializes an empty paragraph's first run when it
        // selects that paragraph. Publish those owner-created IDs in the first
        // durable revision, rather than introducing them only after Save.
        _ = new WriteDocumentEditor(candidate);
        var saved = await SaveOwnedDocumentAsync(candidate, "Created Write document", newDocument: true, token);
        Attach(candidate, readOnly: false);
        Status = saved.VersionHistoryComplete ? "Saved" : "Saved; version history needs attention";
        RememberSavedDocument(candidate);
        DocumentOpened?.Invoke(this, EventArgs.Empty);
        return Success(candidate.Id);
    }, cancellationToken);

    public Task<HomeCoreOperationResult<Guid>> OpenAsync(Guid documentId, bool readOnly = false,
        CancellationToken cancellationToken = default) => OperateAsync(async token =>
    {
        if (documentId == Guid.Empty) return Failure<Guid>("InvalidArgument", "Choose a document to open.");
        if (_presentationPending && _document is not null) return Failure<Guid>("PresentationNotAdmitted", "This prepared view is not active.");
        if (!await SaveBeforeLeavingAsync(token)) return Failed<Guid>();
        var candidate = await _repository.LoadAsync(documentId, token);
        token.ThrowIfCancellationRequested();
        if (candidate is null) return Failure<Guid>("DocumentNotFound", "This document could not be found.");
        if (candidate.Id != documentId) return Failure<Guid>("DocumentIdMismatch", "The document identity does not match this link.");
        var loadedContent = Clone(candidate);
        Attach(candidate, readOnly);
        var editorInitializedContent = !SameAuthoredContent(loadedContent, candidate);
        IsDirty = !readOnly && (candidate.Recovery.HasUnsavedRecovery || editorInitializedContent);
        if (IsDirty) ++_editGeneration;
        Status = readOnly ? "Read-only" : candidate.Recovery.HasUnsavedRecovery
            ? "Recovered document; review and save to confirm recovery" : editorInitializedContent
                ? "Editor initialized document content; save to retain these changes" : "Saved";
        DocumentOpened?.Invoke(this, EventArgs.Empty);
        return Success(candidate.Id);
    }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> SelectAsync(Guid objectId, int start, int end,
        CancellationToken cancellationToken = default) => OperateAsync(token =>
    {
        token.ThrowIfCancellationRequested();
        if (_editor is null) return Task.FromResult(Failure<bool>("DocumentNotFound", "Open a document first."));
        if (_presentationPending) return Task.FromResult(Failure<bool>("PresentationNotAdmitted", "This prepared view is not active."));
        var block = _editor.Blocks().FirstOrDefault(value => value.Id == objectId);
        if (block is null) return Task.FromResult(Failure<bool>("ObjectNotFound", "This document object could not be found."));
        var text = string.Concat(block.Runs.Select(run => run.Text));
        if (start < 0 || end < start || end > text.Length)
            return Task.FromResult(Failure<bool>("InvalidRange", "Choose a valid text range."));
        _editor.SelectBlock(objectId, end, start, end);
        return Task.FromResult(Success(true));
    }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> InsertTextAsync(string text,
        CancellationToken cancellationToken = default) => EditAsync(editor => editor.InsertDocumentText(text), cancellationToken);

    public Task<HomeCoreOperationResult<bool>> ToggleCharacterAsync(WriteCharacterFormat format,
        CancellationToken cancellationToken = default) => EditAsync(editor =>
    {
        if (!Enum.IsDefined(format)) throw new ArgumentException("Unsupported character format.", nameof(format));
        editor.ToggleSelectionCharacter(format);
        return true;
    }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> UndoAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => editor.Undo(), cancellationToken);
    public Task<HomeCoreOperationResult<bool>> RedoAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => editor.Redo(), cancellationToken);

    public Task<HomeCoreOperationResult<bool>> SaveAsync(CancellationToken cancellationToken = default) =>
        OperateAsync(SaveCoreAsync, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default) =>
        OperateAsync(async token => await SaveBeforeLeavingAsync(token) ? Success(true) : Failed<bool>(), cancellationToken);

    public Task<HomeCoreOperationResult<bool>> CloseAsync(CancellationToken cancellationToken = default) =>
        OperateAsync(async token =>
        {
            if (_presentationPending) return Failure<bool>("PresentationNotAdmitted", "This prepared view is not active.");
            if (!await SaveBeforeLeavingAsync(token)) return Failed<bool>();
            Detach();
            Status = "No document open";
            return Success(true);
        }, cancellationToken);

    /// <summary>Uses the existing native package codec. File brokerage remains the host's responsibility.</summary>
    public Task<HomeCoreOperationResult<string>> ExportNativeAsync(string brokeredDestination,
        CancellationToken cancellationToken = default) => OperateAsync(async token =>
    {
        if (_presentationPending) return Failure<string>("PresentationNotAdmitted", "This prepared view is not active.");
        if (_document is null) return Failure<string>("DocumentNotFound", "Open a document first.");
        if (!await SaveBeforeLeavingAsync(token)) return Failed<string>();
        var result = await _packages.SaveAsync(Clone(_document), brokeredDestination, token);
        if (!result.IsSuccess) return Failure<string>(result.Error!.Code.ToString(), result.Error.Message);
        Status = "Exported";
        return Success(result.Value!);
    }, cancellationToken);

    /// <summary>
    /// Explicit independent-copy import, matching the existing WritePage native
    /// import contract. A package cannot replace an independently edited artifact.
    /// </summary>
    public Task<HomeCoreOperationResult<Guid>> ImportNativeCopyAsync(string brokeredSource,
        CancellationToken cancellationToken = default) => OperateAsync(token => ImportNativeCopyCoreAsync(brokeredSource, token), cancellationToken);

    private async Task<HomeCoreOperationResult<Guid>> ImportNativeCopyCoreAsync(string brokeredSource, CancellationToken token)
    {
        if (_presentationPending) return Failure<Guid>("PresentationNotAdmitted", "This prepared view is not active.");
        if (!await SaveBeforeLeavingAsync(token)) return Failed<Guid>();
        var result = await _packages.OpenAsync(brokeredSource, token);
        if (!result.IsSuccess) return Failure<Guid>(result.Error!.Code.ToString(), result.Error.Message);
        var candidate = result.Value!;
        candidate.Id = Guid.NewGuid();
        candidate.Version = 0;
        candidate.CreatedAt = DateTimeOffset.UtcNow;
        candidate.UpdatedAt = candidate.CreatedAt;
        _ = new WriteDocumentEditor(candidate);
        var saved = await SaveOwnedDocumentAsync(candidate, "Imported independent Write document", newDocument: true, token);
        Attach(candidate, readOnly: false);
        Status = saved.VersionHistoryComplete ? "Imported and saved" : "Imported; version history needs attention";
        RememberSavedDocument(candidate);
        DocumentOpened?.Invoke(this, EventArgs.Empty);
        return Success(candidate.Id);
    }

    public Task<HomeCoreOperationResult<bool>> ImportFromBrowserAsync(CancellationToken cancellationToken = default) =>
        OperateAsync(async token =>
        {
            if (_packageBroker is null) return Failure<bool>("BrowserCapabilityUnavailable", "Native package import is unavailable.");
            if (HasUnknownCommitOutcome) return Failure<bool>("CommitOutcomeUnknown", "Inspect saved state before importing another document.");
            var staged = await _packageBroker.PickImportAsync(token);
            if (staged is null) { Status = "Import cancelled"; return Success(false); }
            HomeCoreOperationResult<Guid> imported;
            string? cleanupWarning;
            try { imported = await ImportNativeCopyCoreAsync(staged.Path, token); }
            finally { cleanupWarning = await CleanupPackageStageAsync(staged); }
            if (imported.Succeeded && cleanupWarning is not null) Status += "; temporary package copy could not be removed";
            return imported.Succeeded ? Success(true) : Failed<bool>();
        }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> ExportToBrowserAsync(CancellationToken cancellationToken = default) =>
        OperateAsync(async token =>
        {
            if (_packageBroker is null) return Failure<bool>("BrowserCapabilityUnavailable", "Native package download is unavailable.");
            if (_document is null) return Failure<bool>("DocumentNotFound", "Open a document first.");
            if (!await SaveBeforeLeavingAsync(token)) return Failed<bool>();
            var staged = await _packageBroker.StageExportAsync(_document.Title + ".9to1w", token);
            HomeCoreOperationResult<bool>? failed = null;
            var published = false;
            string? cleanupWarning;
            try
            {
                var exported = await _packages.SaveAsync(Clone(_document), staged.Path, token);
                if (!exported.IsSuccess) failed = Failure<bool>(exported.Error!.Code.ToString(), exported.Error.Message);
                else
                {
                    await _packageBroker.PublishAsync(staged, token);
                    published = true;
                    Status = "Native package download started";
                }
            }
            finally { cleanupWarning = await CleanupPackageStageAsync(staged); }
            if (published && cleanupWarning is not null) Status += "; temporary package copy could not be removed";
            return failed ?? Success(true);
        }, cancellationToken);

    private async Task<string?> CleanupPackageStageAsync(WriteStagedPackage staged)
    {
        try { await staged.DisposeAsync(); return null; }
        catch (Exception error)
        {
            // Ancillary cleanup cannot turn an acknowledged import/download into
            // a false failed effect. Retain the real error for the host diagnostics.
            LastPackageCleanupError = error;
            PackageCleanupWarning = "A temporary native package copy could not be removed.";
            return PackageCleanupWarning;
        }
    }

    private Task<HomeCoreOperationResult<bool>> EditAsync(Func<WriteDocumentEditor, bool> edit,
        CancellationToken cancellationToken) => OperateAsync(token =>
    {
        token.ThrowIfCancellationRequested();
        if (_editor is null) return Task.FromResult(Failure<bool>("DocumentNotFound", "Open a document first."));
        if (_presentationPending) return Task.FromResult(Failure<bool>("PresentationNotAdmitted", "This prepared view is not active."));
        if (HasUnknownCommitOutcome) return Task.FromResult(Failure<bool>("CommitOutcomeUnknown", "Inspect the saved state before editing this preserved draft."));
        if (IsReadOnly) return Task.FromResult(Failure<bool>("ReadOnlyDocument", "This document is read-only."));
        var changed = edit(_editor);
        // Owner undo snapshots may contain an earlier stored version. Undo changes content,
        // never the revision against which the next repository compare-and-save runs.
        _document!.Version = _durableVersion;
        return Task.FromResult(Success(changed));
    }, cancellationToken);

    private async Task<HomeCoreOperationResult<bool>> SaveCoreAsync(CancellationToken token)
    {
        if (_presentationPending) return Failure<bool>("PresentationNotAdmitted", "This prepared view is not active.");
        if (HasUnknownCommitOutcome) return Failure<bool>("CommitOutcomeUnknown", "Save may already be committed. Inspect saved state before retrying; the draft is preserved.");
        if (_document is null) return Failure<bool>("DocumentNotFound", "Open a document first.");
        if (IsReadOnly) return Failure<bool>("ReadOnlyDocument", "This document is read-only.");
        if (!IsDirty) return Success(true);
        var snapshot = Clone(_document);
        snapshot.Version = _durableVersion;
        var generation = _editGeneration;
        var saved = await SaveOwnedDocumentAsync(snapshot, "Saved browser Write edit", newDocument: false, token);
        _durableVersion = saved.Version;
        _document.Version = saved.Version;
        _document.UpdatedAt = snapshot.UpdatedAt;
        _document.Recovery = snapshot.Recovery;
        RememberSavedDocument(snapshot);
        IsDirty = generation != _editGeneration;
        Status = IsDirty ? "Unsaved changes" : saved.VersionHistoryComplete ? "Saved" : "Saved; version history needs attention";
        return Success(true);
    }

    private async Task<NotesSaveResult> SaveOwnedDocumentAsync(NotesDocument candidate, string reason,
        bool newDocument, CancellationToken token)
    {
        var beforeAwait = Clone(candidate);
        var generation = _editGeneration;
        try { return await _repository.SaveAsync(candidate, reason, token); }
        catch (Exception error) when (_isUnknownCommitOutcome(error))
        {
            // Establish uncertain authority before public presentation observers can run.
            _pendingCommit = new(beforeAwait, beforeAwait.Version, generation);
            ErrorCode = "CommitOutcomeUnknown";
            Status = "Save may already be committed. Inspect saved state before retrying; the draft is preserved.";
            try { if (newDocument) Attach(beforeAwait, readOnly: false); }
            finally { IsDirty = true; }
            if (newDocument) DocumentOpened?.Invoke(this, EventArgs.Empty);
            throw;
        }
    }

    private async Task<bool> SaveBeforeLeavingAsync(CancellationToken token)
    {
        if (HasUnknownCommitOutcome)
        {
            Failure<bool>("CommitOutcomeUnknown", "Inspect saved state before leaving this preserved draft.");
            return false;
        }
        if (!IsDirty) return true;
        if (!(await SaveCoreAsync(token)).Succeeded) return false;
        if (!IsDirty) return true;
        Failure<bool>("UnsavedChanges", "The document changed while saving. Save the remaining changes before leaving.");
        return false;
    }

    private async Task<HomeCoreOperationResult<T>> OperateAsync<T>(
        Func<CancellationToken, Task<HomeCoreOperationResult<T>>> operation, CancellationToken cancellationToken)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            if (_disposed) return Failure<T>("ProviderUnavailable", "This document session has closed.");
            IsBusy = true;
            ErrorCode = null;
            Notify();
            return await operation(cancellationToken);
        }
        catch (NotesRevisionConflictException)
        { return Failure<T>("RevisionConflict", "This document changed elsewhere. Your unsaved work has been preserved."); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException)
        { return Failure<T>("PermissionDenied", "This operation is not permitted. Your work has been preserved."); }
        catch (ArgumentException)
        { return Failure<T>("InvalidArgument", "The requested document operation is invalid."); }
        catch (Exception error) when (_pendingCommit is not null && _isUnknownCommitOutcome(error))
        { return Failure<T>("CommitOutcomeUnknown", "Save may already be committed. Inspect saved state before retrying; the draft is preserved."); }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException)
        { return Failure<T>("ProviderUnavailable", "The document could not be saved or opened. Your work has been preserved."); }
        finally
        {
            IsBusy = false;
            try { Notify(); }
            finally { _operations.Release(); }
        }
    }

    private void Attach(NotesDocument document, bool readOnly)
    {
        Detach();
        _document = document;
        _editor = new WriteDocumentEditor(document);
        _editor.Changed += OnChanged;
        _durableVersion = document.Version;
        IsReadOnly = readOnly;
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        ++_editGeneration;
        IsDirty = true;
        Status = "Unsaved changes";
        Notify();
    }

    private void Detach()
    {
        if (_editor is not null) _editor.Changed -= OnChanged;
        _editor = null;
        _document = null;
        IsDirty = false;
        IsReadOnly = false;
        _durableVersion = 0;
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "DocumentTitle" => _document?.Title ?? "Write",
            "SaveStatus" => Status,
            "DocumentId" => DocumentId,
            "DocumentRevision" => _durableVersion,
            "WordCount" => _editor?.Statistics.Words ?? 0,
            "HasDocument" => _document is not null,
            "IsReadOnly" => IsReadOnly,
            "IsDirty" => IsDirty,
            "IsBusy" => IsBusy,
            "CanCreate" => IsActionAvailable("CreateDocument") == true,
            "CanSave" => IsActionAvailable("SaveDocument") == true,
            "CanBold" => IsActionAvailable("BoldSelection") == true,
            "CanUndo" => IsActionAvailable("Undo") == true,
            "CanRedo" => IsActionAvailable("Redo") == true,
            "CanClose" => IsActionAvailable("CloseDocument") == true,
            "Documents" => _documents.Select(document => new WriteSavedDocumentRow(document)).ToArray(),
            "CanBrowseDocuments" => IsActionAvailable("OpenDocument") == true,
            "CanRefreshDocuments" => IsActionAvailable("RefreshDocuments") == true,
            "CanInspectSavedState" => IsActionAvailable("InspectSavedState") == true,
            "HasUnknownCommitOutcome" => HasUnknownCommitOutcome,
            "CanImportPackage" => IsActionAvailable("ImportPackage") == true,
            "CanExportPackage" => IsActionAvailable("ExportPackage") == true,
            "PackageCleanupWarning" => PackageCleanupWarning ?? string.Empty,
            _ => null
        };
        return path is "DocumentTitle" or "SaveStatus" or "DocumentId" or "DocumentRevision" or "WordCount"
            or "HasDocument" or "IsReadOnly" or "IsDirty" or "IsBusy"
            or "CanCreate" or "CanSave" or "CanBold" or "CanUndo" or "CanRedo" or "CanClose"
            or "Documents" or "CanBrowseDocuments" or "CanRefreshDocuments" or "CanInspectSavedState" or "HasUnknownCommitOutcome"
            or "CanImportPackage" or "CanExportPackage" or "PackageCleanupWarning";
    }

    // These are local CUI presentation commands, not a new remote API catalogue.
    public bool? IsActionAvailable(string command) => !_disposed && !_presentationPending && !IsBusy &&
        (!HasUnknownCommitOutcome || command is "InspectSavedState" or "RefreshDocuments") && (command switch
    {
        "CreateDocument" => true,
        "SaveDocument" or "BoldSelection" => _editor is not null && !IsReadOnly,
        "Undo" => _editor?.CanUndo == true && !IsReadOnly,
        "Redo" => _editor?.CanRedo == true && !IsReadOnly,
        "CloseDocument" => _editor is not null,
        "OpenDocument" or "RefreshDocuments" => true,
        "InspectSavedState" => HasUnknownCommitOutcome,
        "ImportPackage" => _packageBroker is not null,
        "ExportPackage" => _packageBroker is not null && _document is not null,
        _ => false
    });

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case "CreateDocument": await CreateAsync(cancellationToken: cancellationToken); break;
            case "SaveDocument": await SaveAsync(cancellationToken); break;
            case "BoldSelection": await ToggleCharacterAsync(WriteCharacterFormat.Bold, cancellationToken); break;
            case "Undo": await UndoAsync(cancellationToken); break;
            case "Redo": await RedoAsync(cancellationToken); break;
            case "CloseDocument": await CloseAsync(cancellationToken); break;
            case "RefreshDocuments": await RefreshDocumentsAsync(cancellationToken); break;
            case "InspectSavedState": await InspectDurableStateAsync(cancellationToken); break;
            case "ImportPackage": await ImportFromBrowserAsync(cancellationToken); break;
            case "ExportPackage": await ExportToBrowserAsync(cancellationToken); break;
            case "OpenDocument" when parameter is WriteSavedDocumentRow savedDocument &&
                _documents.Any(current => current.Id == savedDocument.Summary.Id):
                await OpenAsync(savedDocument.Summary.Id, cancellationToken: cancellationToken); break;
            default: Failure<bool>("UnsupportedFeature", "This document action is unavailable."); Notify(); break;
        }
    }

    private HomeCoreOperationResult<T> Success<T>(T value) => new(true, "Succeeded", Status, value, Revision: _durableVersion);
    private HomeCoreOperationResult<T> Failed<T>() => new(false, ErrorCode ?? "ProviderUnavailable", Status);
    private HomeCoreOperationResult<T> Failure<T>(string code, string message)
    { ErrorCode = code; Status = message; return new(false, code, message); }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private static NotesDocument Clone(NotesDocument document) =>
        JsonSerializer.Deserialize<NotesDocument>(JsonSerializer.Serialize(document, SnapshotOptions), SnapshotOptions)
        ?? throw new InvalidDataException("The document snapshot could not be read.");

    private static bool SameAuthoredContent(NotesDocument first, NotesDocument second)
    {
        static string Content(NotesDocument document)
        {
            var copy = Clone(document);
            // These fields belong to durable publication, not authored content.
            copy.Version = 0;
            copy.UpdatedAt = default;
            copy.Recovery = new();
            return JsonSerializer.Serialize(copy, SnapshotOptions);
        }
        return Content(first) == Content(second);
    }

    private sealed record PendingWriteCommit(NotesDocument Snapshot, long BaseRevision, long EditGeneration);

    private void RememberSavedDocument(NotesDocument document)
    {
        var summary = new NotesDocumentSummary(document.Id, document.Title, document.UpdatedAt, document.Version,
            document.Sections.Count, document.Sections.Sum(section => section.Pages.Sum(page => page.Blocks.Count)),
            NotesTextStatistics.Calculate(document).Words, document.Recovery.HasUnsavedRecovery);
        _documents = _documents.Where(item => item.Id != document.Id).Append(summary)
            .OrderByDescending(item => item.UpdatedAt).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await _operations.WaitAsync();
        try
        {
            if (_disposed) return;
            if (IsDirty) throw new InvalidOperationException("Save or recover the document before disposing this session.");
            _disposed = true;
            Detach();
        }
        finally { _operations.Release(); }
    }
}

/// <summary>Read-only CUI presentation over the owner's existing canonical summary.</summary>
public sealed record WriteSavedDocumentRow(NotesDocumentSummary Summary) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Id" => Summary.Id, "Title" => Summary.Title, "Version" => Summary.Version, _ => null };
        return path is "Id" or "Title" or "Version";
    }
}
