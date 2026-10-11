using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace NineToOne.Web.Write;

/// <summary>
/// Browser presentation/lifecycle adapter over the owning Write editor and repository.
/// The composition root supplies an authorised repository; this class is neither a
/// document model nor an authentication, permission, storage or collaboration service.
/// </summary>
public sealed class WriteBrowserSession : ICuiBindingContext, ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);
    private readonly INotesRepository _repository;
    private readonly IWriteNativeDocumentPackageStore _packages;
    private readonly Func<Exception, bool> _isUnknownCommitOutcome;
    private readonly IWriteBrowserPackageBroker? _packageBroker;
    private readonly IWriteBrowserImageSource? _imageSource;
    private readonly INotesAttachmentStore? _attachments;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private NotesDocument? _document;
    private WriteDocumentEditor? _editor;
    private long _durableVersion;
    private long _editGeneration;
    private bool _disposed;
    private int _tableRows = 3;
    private int _tableColumns = 3;
    private IReadOnlyList<NotesDocumentSummary> _documents = [];
    private PendingWriteCommit? _pendingCommit;
    private bool _presentationPending;
    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private string _replaceStatus = string.Empty;
    private int _findIndex = -1;
    private WriteStyleRow[]? _styleRows;
    private string? _styleSignature;

    public WriteBrowserSession(INotesRepository authorisedRepository, IWriteNativeDocumentPackageStore packages,
        Func<Exception, bool>? isUnknownCommitOutcome = null, IWriteBrowserPackageBroker? packageBroker = null,
        IWriteBrowserImageSource? imageSource = null, INotesAttachmentStore? attachments = null)
    {
        _repository = authorisedRepository ?? throw new ArgumentNullException(nameof(authorisedRepository));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _isUnknownCommitOutcome = isUnknownCommitOutcome ?? (_ => false);
        _packageBroker = packageBroker;
        _imageSource = imageSource;
        _attachments = attachments;
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

    public Task<HomeCoreOperationResult<bool>> SetAlignmentAsync(NotesTextAlignment alignment,
        CancellationToken cancellationToken = default) => EditAsync(editor =>
    {
        if (!Enum.IsDefined(alignment)) throw new ArgumentException("Unsupported paragraph alignment.", nameof(alignment));
        editor.SetAlignment(alignment);
        return true;
    }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> InsertBlockAsync(NotesBlockKind kind,
        NotesListKind listKind = NotesListKind.Bulleted, string? styleId = null,
        CancellationToken cancellationToken = default) => EditAsync(editor =>
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Unsupported block kind.", nameof(kind));
        var block = editor.InsertBlock(kind, listKind);
        if (styleId is null) return true;
        editor.SelectBlock(block.Id);
        editor.ApplyStyle(styleId);
        return true;
    }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> InsertPageBreakAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => { editor.InsertPageBreak(); return true; }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> InsertTableAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => { editor.InsertTable(_tableRows, _tableColumns); return true; }, cancellationToken);

    /// <summary>Inserts an equation block as an ordinary admitted edit.</summary>
    public Task<HomeCoreOperationResult<bool>> InsertEquationAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => { editor.InsertBlock(NotesBlockKind.Equation); return true; }, cancellationToken);

    /// <summary>
    /// Inserts an image the writer picked from the platform surface. The picker
    /// is the same admission path as every other edit: the image reaches the
    /// owning document through <see cref="WriteDocumentEditor.InsertMedia"/> so
    /// it takes part in undo, the dirty flag, save and the read-only gate. The
    /// digest, stored path and attachment identity come from the canonical
    /// attachment store, the same owner the Desktop host uses. The picker's
    /// declared media type is kept only when the staged file's extension
    /// carried none.
    /// </summary>
    public async Task<HomeCoreOperationResult<bool>> InsertImageAsync(CancellationToken cancellationToken = default)
    {
        if (_imageSource is null)
        {
            Status = "Image insertion is unavailable because no image source is registered.";
            ErrorCode = "ImageSourceMissing";
            Notify();
            return Failure<bool>("ImageSourceMissing", "Image insertion is unavailable on this surface.");
        }
        if (_attachments is null)
        {
            // Without the canonical attachment owner the browser could only keep
            // metadata for bytes it cannot store, so the action reports itself as
            // unavailable rather than accepting an image no save can carry.
            Status = "Image insertion is unavailable because no attachment store is registered.";
            ErrorCode = "AttachmentStoreMissing";
            Notify();
            return Failure<bool>("AttachmentStoreMissing", "Image insertion is unavailable on this surface.");
        }
        if (_editor is null || IsReadOnly || _presentationPending)
        {
            Status = _editor is null ? "Open a document first." : IsReadOnly ? "This document is read-only." : "This prepared view is not active.";
            Notify();
            return Failure<bool>(_editor is null ? "DocumentNotFound" : IsReadOnly ? "ReadOnlyDocument" : "PresentationNotAdmitted", Status);
        }
        WritePickedImage? picked;
        try { picked = await _imageSource.PickImageAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException or FormatException)
        {
            Status = error.Message;
            ErrorCode = "ImageRejected";
            Notify();
            return Failure<bool>("ImageRejected", error.Message);
        }
        if (picked is null)
        {
            Status = "Image insertion cancelled";
            Notify();
            return Success(false);
        }
        if (string.IsNullOrEmpty(picked.StagedPath))
        {
            // The canonical attachment owner imports from a staged file. Without
            // one the browser could only keep metadata for bytes it cannot store,
            // so the insertion is refused rather than accepted as a broken image.
            Status = "The picked image was not staged as a file the attachment store can import.";
            ErrorCode = "ImageNotStaged";
            Notify();
            return Failure<bool>("ImageNotStaged", "Image insertion is unavailable on this surface.");
        }
        NotesMediaData media;
        try { media = await _attachments.ImportAsync(picked.StagedPath, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            Status = error.Message;
            ErrorCode = "AttachmentImportFailed";
            Notify();
            return Failure<bool>("AttachmentImportFailed", error.Message);
        }
        // The attachment owner names the stored copy and computes the digest. The
        // picker's declared type is kept only where the extension carried none.
        if (media.MediaType == "application/octet-stream" && !string.IsNullOrWhiteSpace(picked.MediaType))
            media.MediaType = picked.MediaType;
        var result = await EditAsync(editor => { editor.InsertMedia(media); return true; }, cancellationToken);
        if (result.Succeeded)
        {
            Status = "Inserted " + picked.Name;
            ErrorCode = null;
        }
        Notify();
        return result;
    }

    /// <summary>Resizes, rotates or crops the selected image and reports the owning block again.</summary>
    public Task<HomeCoreOperationResult<bool>> UpdateSelectedMediaAsync(double width, double height,
        CancellationToken cancellationToken = default) =>
        EditAsync(editor => editor.ResizeSelectedMedia(width, height), cancellationToken);
    public Task<HomeCoreOperationResult<bool>> RotateSelectedMediaAsync(double degrees,
        CancellationToken cancellationToken = default) =>
        EditAsync(editor => editor.RotateSelectedMedia(degrees), cancellationToken);
    public Task<HomeCoreOperationResult<bool>> CropSelectedMediaAsync(double left, double top, double right, double bottom,
        CancellationToken cancellationToken = default) =>
        EditAsync(editor => editor.SetSelectedMediaCrop(left, top, right, bottom), cancellationToken);

    /// <summary>
    /// Reorders or removes the selected block as an ordinary admitted edit, so
    /// both participate in undo and in the same dirty, save and read-only rules
    /// as every other structural change. The editor itself refuses a move that
    /// would leave the block where it already is and a delete that would remove
    /// the document's last block, so those refusals reach the caller as a
    /// successful no-op instead of a missing control.
    /// </summary>
    public Task<HomeCoreOperationResult<bool>> MoveBlockAsync(int direction,
        CancellationToken cancellationToken = default) => EditAsync(editor => editor.MoveSelected(direction),
        cancellationToken);

    public Task<HomeCoreOperationResult<bool>> DeleteBlockAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => editor.DeleteSelected(), cancellationToken);

    /// <summary>
    /// Run and table cell structural edits. All four are ordinary admitted
    /// edits that the editor itself declines when they make no sense: a split
    /// refuses a caret that is not strictly inside a run, a merge refuses the
    /// first run, a cell merge refuses the last column of a row, and a cell
    /// split refuses a cell that is not spanning more than one column.
    /// </summary>
    public Task<HomeCoreOperationResult<bool>> SplitRunAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => editor.SplitRunAtCaret(), cancellationToken);

    public Task<HomeCoreOperationResult<bool>> MergeRunAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => editor.MergeRunWithPrevious(), cancellationToken);

    public Task<HomeCoreOperationResult<bool>> MergeCellRightAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => editor.MergeTableCellRight(), cancellationToken);

    public Task<HomeCoreOperationResult<bool>> SplitCellAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => editor.SplitTableCell(), cancellationToken);

    /// <summary>
    /// Applies one of the document's own named paragraph styles to the selected
    /// block. The editor resolves the style by id and declines an unknown one, so
    /// a stale catalogue row is a no-op instead of an error the writer cannot act
    /// on. The result reports whether the selected block's style actually changed.
    /// </summary>
    public Task<HomeCoreOperationResult<bool>> ApplyStyleAsync(string styleId,
        CancellationToken cancellationToken = default) => EditAsync(editor =>
    {
        var before = editor.SelectedBlock?.StyleId;
        editor.ApplyStyle(styleId ?? string.Empty);
        return !string.Equals(before, editor.SelectedBlock?.StyleId, StringComparison.Ordinal);
    }, cancellationToken);

    /// <summary>
    /// The style catalogue a paragraph style picker renders. With no document open
    /// it falls back to the built-in set so the surface keeps the same rows and
    /// only their enabled state changes when a document arrives, and the array
    /// identity is held while the document's style ids are unchanged so a refresh
    /// does not tear down and rebuild every style control.
    /// </summary>
    private WriteStyleRow[] StyleRows()
    {
        var styles = _editor?.Document.Styles ?? NotesNamedStyle.CreateDefaults();
        var signature = string.Join(' ', styles.Select(style => style.Id));
        if (_styleRows is { } cached && _styleSignature == signature) return cached;
        _styleSignature = signature;
        _styleRows = styles
            .Select(style => new WriteStyleRow(style.Id, style.Name, "Set " + style.Name.ToLowerInvariant()))
            .ToArray();
        return _styleRows;
    }

    public Task<HomeCoreOperationResult<bool>> AddListItemAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => { editor.AddListItem(); return true; }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> SetListKindAsync(NotesListKind kind,
        CancellationToken cancellationToken = default) => EditAsync(editor =>
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Unsupported list kind.", nameof(kind));
        editor.SetListKind(kind);
        return true;
    }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> AddTableRowAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => { editor.AddTableRow(); return true; }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> AddTableColumnAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => { editor.AddTableColumn(); return true; }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> RemoveTableRowAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => { editor.RemoveTableRow(); return true; }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> RemoveTableColumnAsync(CancellationToken cancellationToken = default) =>
        EditAsync(editor => { editor.RemoveTableColumn(); return true; }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> SetPagePresetAsync(string preset,
        CancellationToken cancellationToken = default) => EditAsync(editor =>
    {
        if (string.IsNullOrWhiteSpace(preset)) throw new ArgumentException("A page size is required.", nameof(preset));
        editor.SetPagePreset(preset);
        return true;
    }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> SetOrientationAsync(string orientation,
        CancellationToken cancellationToken = default) => EditAsync(editor =>
    {
        if (string.IsNullOrWhiteSpace(orientation)) throw new ArgumentException("An orientation is required.", nameof(orientation));
        editor.SetOrientation(orientation);
        return true;
    }, cancellationToken);

    public Task<HomeCoreOperationResult<bool>> SetLayoutModeAsync(NotesLayoutMode mode,
        CancellationToken cancellationToken = default) => EditAsync(editor =>
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Unsupported layout mode.", nameof(mode));
        editor.SetLayout(mode);
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
            if (_presentationPending) return Failure<bool>("PresentationNotAdmitted", "This prepared view is not active.");
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
            if (_presentationPending) return Failure<bool>("PresentationNotAdmitted", "This prepared view is not active.");
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
            "CanFormat" => IsActionAvailable("BoldSelection") == true,
            "FontFamily" => FormattingRun?.FontFamily ?? string.Empty,
            "FontSize" => FormattingRun?.FontSize ?? 14d,
            "TextColor" => FormattingRun?.Foreground ?? string.Empty,
            "TextHighlight" => FormattingRun?.Background ?? string.Empty,
            "Link" => FormattingRun?.Link ?? string.Empty,
            "CanEditPage" => IsActionAvailable("SetPageA4") == true,
            "PageMargins" => _editor?.Document.PageSetup.MarginTopPoints ?? 72d,
            "ShowPageNumbers" => _editor?.Document.PageSetup.ShowPageNumbers ?? false,
            "PageSizeLabel" => _editor is { } pageSize
                ? $"{pageSize.Document.PageSetup.WidthPoints:0} x {pageSize.Document.PageSetup.HeightPoints:0} pt"
                : string.Empty,
            "PageLayoutMode" => _editor?.Document.LayoutMode.ToString() ?? string.Empty,
            "CanAlign" => IsActionAvailable("AlignLeft") == true,
            "CanInsert" => IsActionAvailable("InsertParagraph") == true,
            "CanMoveBlocks" => IsActionAvailable("MoveBlockUp") == true,
            "CanDeleteBlock" => IsActionAvailable("DeleteBlock") == true,
            "CanSplitRun" => IsActionAvailable("SplitRun") == true,
            "CanMergeRun" => IsActionAvailable("MergeRun") == true,
            "CanMergeCell" => IsActionAvailable("MergeCellRight") == true,
            "CanSplitCell" => IsActionAvailable("SplitCell") == true,
            "CanApplyStyle" => IsActionAvailable("ApplyStyle") == true,
            "Styles" => StyleRows(),
            "SelectedStyle" => _editor?.SelectedBlock is { } styled
                ? _editor.Document.Styles.FirstOrDefault(value =>
                    value.Id.Equals(styled.StyleId, StringComparison.OrdinalIgnoreCase))?.Name ?? styled.StyleId
                : string.Empty,
            "ParagraphAlignment" => _editor?.SelectedBlock?.Paragraph.Alignment,
            "LineSpacing" => _editor?.SelectedBlock is { } spacing ? spacing.Paragraph.LineSpacing : null,
            "LeftIndent" => _editor?.SelectedBlock is { } lefted ? lefted.Paragraph.IndentLeft : null,
            "FirstLineIndent" => _editor?.SelectedBlock is { } first ? first.Paragraph.FirstLineIndent : null,
            "SpaceAfter" => _editor?.SelectedBlock is { } after ? after.Paragraph.SpaceAfter : null,
            "TableRows" => _tableRows,
            "TableColumns" => _tableColumns,
            "CanEditList" => IsActionAvailable("AddListItem") == true,
            "SelectedListItems" => _editor?.SelectedBlock is { List: { Items: { } items } } listBlock
                ? items.Select(item => new WriteListItemRow(this, listBlock.Id, item)).ToArray()
                : Array.Empty<WriteListItemRow>(),
            "CanEditTable" => IsActionAvailable("AddTableRow") == true,
            "SelectedTableRows" => _editor?.SelectedBlock is { Table: { Rows: { } tableRows } } tableBlock
                ? tableRows.Select(row => new WriteTableRowRow(this, tableBlock.Id, row)).ToArray()
                : Array.Empty<WriteTableRowRow>(),
            "CanUndo" => IsActionAvailable("Undo") == true,
            "CanRedo" => IsActionAvailable("Redo") == true,
            "CanClose" => IsActionAvailable("CloseDocument") == true,
            "CanInsertImage" => IsActionAvailable("InsertImage") == true,
            "CanEditMedia" => IsActionAvailable("SetMediaAlt") == true,
            "MediaAlt" => _editor?.SelectedBlock?.Media?.AltText ?? string.Empty,
            "MediaCaption" => _editor?.SelectedBlock?.Media?.Caption ?? string.Empty,
            "MediaWrapping" => _editor?.SelectedBlock?.Media?.Wrapping ?? "Inline",
            "MediaWidth" => _editor?.SelectedBlock?.Media?.Width,
            "MediaHeight" => _editor?.SelectedBlock?.Media?.Height,
            "MediaRotation" => _editor?.SelectedBlock?.Media?.Rotation,
            "MediaCropLeft" => _editor?.SelectedBlock?.Media?.CropLeft,
            "MediaCropTop" => _editor?.SelectedBlock?.Media?.CropTop,
            "MediaCropRight" => _editor?.SelectedBlock?.Media?.CropRight,
            "MediaCropBottom" => _editor?.SelectedBlock?.Media?.CropBottom,
            "CanInsertEquation" => IsActionAvailable("InsertEquation") == true,
            "CanEditEquation" => IsActionAvailable("SetEquationSource") == true,
            "EquationSource" => _editor?.SelectedBlock?.Equation?.Source ?? string.Empty,
            "EquationAlternative" => _editor?.SelectedBlock?.Equation?.AccessibleAlternative ?? string.Empty,
            "CanEditCellBackground" => IsActionAvailable("SetCellBackground") == true,
            "CellBackground" => _editor?.SelectedBlock?.Table?.Rows
                .SelectMany(row => row.Cells).FirstOrDefault(cell => cell.Id == _editor.SelectedTableCellId)?.Background
                ?? string.Empty,
            "Documents" => _documents.Select(document => new WriteSavedDocumentRow(document)).ToArray(),
            "CanBrowseDocuments" => IsActionAvailable("OpenDocument") == true,
            "CanRefreshDocuments" => IsActionAvailable("RefreshDocuments") == true,
            "CanInspectSavedState" => IsActionAvailable("InspectSavedState") == true,
            "HasUnknownCommitOutcome" => HasUnknownCommitOutcome,
            "CanImportPackage" => IsActionAvailable("ImportPackage") == true,
            "CanExportPackage" => IsActionAvailable("ExportPackage") == true,
            "PackageCleanupWarning" => PackageCleanupWarning ?? string.Empty,
            "FindText" => _findText,
            "ReplaceText" => _replaceText,
            "FindMatchCount" => FindMatches.Count,
            "FindStatus" => FindStatusValue,
            "ReplaceStatus" => _replaceStatus,
            "CanFind" => IsActionAvailable("FindNext") == true,
            "CanReplace" => IsActionAvailable("ReplaceAll") == true,
            _ => null
        };
        return path is "DocumentTitle" or "SaveStatus" or "DocumentId" or "DocumentRevision" or "WordCount"
            or "HasDocument" or "IsReadOnly" or "IsDirty" or "IsBusy"
            or "CanCreate" or "CanSave"             or "CanFormat" or "FontFamily" or "FontSize" or "TextColor" or "TextHighlight" or "Link"
            or "CanEditPage" or "PageMargins" or "ShowPageNumbers" or "PageSizeLabel" or "PageLayoutMode"
            or "CanAlign" or "CanInsert" or "CanMoveBlocks" or "CanDeleteBlock" or "ParagraphAlignment"             or "CanUndo" or "CanRedo" or "CanClose"
            or "CanInsertImage" or "CanEditMedia" or "MediaAlt" or "MediaCaption" or "MediaWrapping"
            or "MediaWidth" or "MediaHeight" or "MediaRotation"
            or "MediaCropLeft" or "MediaCropTop" or "MediaCropRight" or "MediaCropBottom"
            or "CanInsertEquation" or "CanEditEquation" or "EquationSource" or "EquationAlternative"
            or "CanEditCellBackground" or "CellBackground"
            or "CanSplitRun" or "CanMergeRun" or "CanMergeCell" or "CanSplitCell"
            or "CanApplyStyle" or "Styles" or "SelectedStyle"
            or "LineSpacing" or "LeftIndent" or "FirstLineIndent" or "SpaceAfter"
            or "TableRows" or "TableColumns" or "CanEditList" or "SelectedListItems"
            or "CanEditTable" or "SelectedTableRows"
            or "Documents" or "CanBrowseDocuments" or "CanRefreshDocuments" or "CanInspectSavedState" or "HasUnknownCommitOutcome"
            or "CanImportPackage" or "CanExportPackage" or "PackageCleanupWarning"
            or "FindText" or "ReplaceText" or "FindMatchCount" or "FindStatus" or "ReplaceStatus" or "CanFind" or "CanReplace";
    }

    /// <summary>
    /// The run the font controls read and the character format commands write. A
    /// selection is formatted by range, so the readout starts at the selection's
    /// first character rather than at the editor's internal active run, which may
    /// still sit on the run before the boundary. Without a selection the editor's
    /// own active run is exactly what the character commands target.
    /// </summary>
    private NotesTextRun? FormattingRun
    {
        get
        {
            if (_editor is not { } editor || editor.SelectedBlock is not { } block || block.Runs.Count == 0) return null;
            if (!editor.HasSelection) return editor.ActiveRun;
            var offset = 0;
            foreach (var run in block.Runs)
            {
                offset += run.Text.Length;
                if (editor.SelectionStart < offset) return run;
            }
            return block.Runs[^1];
        }
    }

    /// <summary>
    /// Writes the two-way paragraph fields the browser surface exposes as number
    /// inputs. The same admission, busy and read-only guards as the asynchronous
    /// editing commands apply; the editor's paragraph operations are synchronous,
    /// so an accepted value is applied immediately and the editor's own change
    /// notification marks the session dirty. Each field carries the same bounds the
    /// editor applies, so an out-of-range input is rejected for validation rather
    /// than silently clamped to a value the writer did not type.
    /// <para>
    /// FontFamily, FontSize, TextColor and TextHighlight write the same character
    /// format the font controls read back. FontSize carries the editor's own 4 to
    /// 300 point bounds. A colour must be a hex value the editor can normalise, so
    /// anything else is rejected for validation instead of quietly leaving the run
    /// with the colour the writer did not choose.
    /// </para>
    /// <para>
    /// PageMargins and ShowPageNumbers write the document's page setup. Margins
    /// carry the editor's own 0 to 1000 point bounds for the same reason: the
    /// editor clamps, so an out-of-range input has to be rejected here to reach
    /// the writer as an error instead of a number they did not type.
    /// </para>
    /// <para>
    /// TableRows and TableColumns are session-held insert dimensions rather than
    /// document state: they are what the next InsertTable command asks the editor
    /// for, they carry the editor's own 1-100 by 1-50 bounds, and they must be
    /// whole numbers because a table is never built from a fractional shape.
    /// </para>
    /// </summary>
    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || IsBusy || _presentationPending || HasUnknownCommitOutcome) return false;
        if (path is "FindText" or "ReplaceText")
        {
            // The find and replace strings are session state, not authored
            // content, so a read-only document still accepts them; only the
            // search itself must reach an open document.
            if (_editor is null || !TryText(value, out var query)) return false;
            if (path is "ReplaceText") { _replaceText = query; return true; }
            if (string.Equals(_findText, query, StringComparison.Ordinal)) return true;
            _findText = query;
            _findIndex = -1;
            _replaceStatus = string.Empty;
            return true;
        }
        if (IsReadOnly) return false;
        if (_editor is null || _editor.SelectedBlock is null) return false;
        var editor = _editor;
        // The media properties are the image the writer is pointing at, so they
        // read their untouched siblings from the block itself while a text or
        // colour property writes its own typed value only.
        if (path is "MediaAlt" or "MediaCaption" or "MediaWrapping")
        {
            if (!TryText(value, out var text) || _editor.SelectedBlock.Media is not { } textMedia) return false;
            editor.UpdateMedia(
                path is "MediaAlt" ? text : textMedia.AltText,
                path is "MediaCaption" ? text : textMedia.Caption,
                path is "MediaWrapping" ? text : textMedia.Wrapping);
            return true;
        }
        if (path is "EquationSource" or "EquationAlternative")
        {
            // The equation's own source and accessible alternative are written
            // together, so each input writes its typed value and reads its
            // untouched sibling from the block, exactly like the media fields.
            if (!TryText(value, out var text) || _editor.SelectedBlock.Equation is not { } equation) return false;
            editor.UpdateEquation(
                path is "EquationAlternative" ? equation.Source : text,
                path is "EquationSource" ? equation.AccessibleAlternative : text);
            return true;
        }
        if (path is "CellBackground")
        {
            if (_editor.SelectedTableCellId is not { } cellId) return false;
            if (!TryText(value, out var colour) || string.IsNullOrWhiteSpace(colour) || !IsHexColour(colour))
                return false;
            editor.SetTableCellBackground(cellId, colour);
            return true;
        }
        if (path is "MediaWidth" or "MediaHeight" or "MediaRotation" or "MediaCropLeft" or "MediaCropTop"
            or "MediaCropRight" or "MediaCropBottom")
        {
            if (_editor.SelectedBlock.Media is not { } media) return false;
            if (!TryAmount(value, out var mediaAmount)) return false;
            switch (path)
            {
                case "MediaWidth":
                    if (mediaAmount is < 24 or > 10000) return false;
                    return editor.ResizeSelectedMedia(mediaAmount, media.Height);
                case "MediaHeight":
                    if (mediaAmount is < 24 or > 10000) return false;
                    return editor.ResizeSelectedMedia(media.Width, mediaAmount);
                case "MediaRotation":
                    return editor.RotateSelectedMedia(mediaAmount - media.Rotation);
                default:
                    // The editor clamps a crop to 0-0.95 and refuses a pair that
                    // would leave nothing to crop. An input outside those bounds
                    // has to be rejected here so the writer sees an error rather
                    // than a fraction they did not type.
                    if (mediaAmount is < 0 or > 0.95) return false;
                    var left = path is "MediaCropLeft" ? mediaAmount : media.CropLeft;
                    var top = path is "MediaCropTop" ? mediaAmount : media.CropTop;
                    var right = path is "MediaCropRight" ? mediaAmount : media.CropRight;
                    var bottom = path is "MediaCropBottom" ? mediaAmount : media.CropBottom;
                    return editor.SetSelectedMediaCrop(left, top, right, bottom);
            }
        }
        if (path is "ShowPageNumbers")
        {
            if (!TryFlag(value, out var showPageNumbers)) return false;
            editor.SetPageNumbers(showPageNumbers);
            return true;
        }
        // An empty link is how a writer removes one, so it is accepted where the
        // font family and the hex colours, which need real values, are not.
        if (path is "Link")
        {
            if (!TryText(value, out var link)) return false;
            editor.SetLink(link);
            return true;
        }
        if (path is "FontFamily" or "TextColor" or "TextHighlight")
        {
            if (!TryText(value, out var text) || string.IsNullOrWhiteSpace(text)) return false;
            if (path is not "FontFamily" && !IsHexColour(text)) return false;
            switch (path)
            {
                case "FontFamily": editor.SetFontFamily(text); break;
                case "TextColor": editor.SetForeground(text); break;
                default: editor.SetBackground(text); break;
            }
            return true;
        }
        if (!TryAmount(value, out var amount)) return false;
        switch (path)
        {
            case "FontSize":
                if (amount is < 4 or > 300) return false;
                editor.SetFontSize(amount);
                return true;
            case "PageMargins":
                if (amount is < 0 or > 1000) return false;
                editor.SetMargins(amount);
                return true;
            case "LineSpacing":
                if (amount is < .5 or > 10) return false;
                editor.SetLineSpacing(amount);
                return true;
            case "LeftIndent":
                if (amount is < 0 or > 1000) return false;
                editor.SetLeftIndent(amount);
                return true;
            case "FirstLineIndent":
                if (amount is < -1000 or > 1000) return false;
                editor.SetFirstLineIndent(amount);
                return true;
            case "SpaceAfter":
                if (amount is < 0 or > 1000) return false;
                editor.SetSpaceAfter(amount);
                return true;
            case "TableRows":
                if (amount is < 1 or > 100 || amount != Math.Truncate(amount)) return false;
                _tableRows = (int)amount;
                return true;
            case "TableColumns":
                if (amount is < 1 or > 50 || amount != Math.Truncate(amount)) return false;
                _tableColumns = (int)amount;
                return true;
            default:
                return false;
        }
    }

    private static bool TryAmount(object? value, out double amount)
    {
        switch (value)
        {
            case double number: amount = number; return double.IsFinite(number);
            case float number: amount = number; return double.IsFinite(number);
            case decimal number: amount = (double)number; return true;
            case int number: amount = number; return true;
            case long number: amount = number; return true;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                amount = parsed; return double.IsFinite(parsed);
            default: amount = 0; return false;
        }
    }

    private static bool TryText(object? value, out string text)
    {
        switch (value)
        {
            case string raw: text = raw; return true;
            default: text = string.Empty; return false;
        }
    }

    private static bool IsHexColour(string value)
    {
        if (value.Length is not (7 or 9) || value[0] != '#') return false;
        for (var index = 1; index < value.Length; index++)
            if (!Uri.IsHexDigit(value[index])) return false;
        return true;
    }

    private static bool TryFlag(object? value, out bool flag)
    {
        switch (value)
        {
            case bool set: flag = set; return true;
            case string text when bool.TryParse(text, out var parsed): flag = parsed; return true;
            default: flag = false; return false;
        }
    }

    /// <summary>
    /// Applies a two-way list item write coming from inside the Write.cui repeat.
    /// The same admission, busy and read-only guards as every other write apply,
    /// the block and item must still be the ones the surface rendered, and each
    /// field carries the editor's own bounds so a level outside the editor's 0-8
    /// nesting range is rejected for validation instead of being clamped.
    /// </summary>
    internal bool TryEditListItem(Guid blockId, Guid itemId, string path, object? value)
    {
        if (_disposed || IsBusy || _presentationPending || HasUnknownCommitOutcome || IsReadOnly) return false;
        if (_editor?.SelectedBlock is not { List: { Items: { } items } } block || block.Id != blockId) return false;
        if (!items.Any(item => item.Id == itemId)) return false;
        switch (path)
        {
            case "Text":
                if (!TryText(value, out var text)) return false;
                _editor.UpdateListItem(itemId, text);
                return true;
            case "Level":
                if (!TryAmount(value, out var level) || level is < 0 or > 8 || level != Math.Truncate(level)) return false;
                _editor.SetListItemLevel(itemId, (int)level);
                return true;
            case "Checked":
                if (!TryFlag(value, out var flag)) return false;
                _editor.ToggleListItem(itemId, flag);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Applies a two-way table cell write coming from inside the Write.cui repeat.
    /// The same admission guards as every other write apply, and the block and
    /// cell must still be the ones the surface rendered so a stale control cannot
    /// edit a document that has moved on underneath it.
    /// </summary>
    internal bool TryEditTableCell(Guid blockId, Guid cellId, string path, object? value)
    {
        if (_disposed || IsBusy || _presentationPending || HasUnknownCommitOutcome || IsReadOnly) return false;
        if (_editor?.SelectedBlock is not { Table: { Rows: { } rows } } block || block.Id != blockId) return false;
        if (!rows.Any(row => row.Cells.Any(cell => cell.Id == cellId))) return false;
        if (path is not "Text" || !TryText(value, out var text)) return false;
        _editor.UpdateTableCell(cellId, text);
        return true;
    }

    /// <summary>
    /// Every textual match for the current find query in the document's own
    /// search order. Matches count across list items, table cells and equations
    /// as well as run text, so the readout is a whole-document figure.
    /// </summary>
    private IReadOnlyList<WriteFindResult> FindMatches =>
        _editor is not { } editor || string.IsNullOrWhiteSpace(_findText)
            ? Array.Empty<WriteFindResult>()
            : editor.Find(_findText);

    private string FindStatusValue
    {
        get
        {
            if (_editor is null) return "Open a document to search.";
            if (string.IsNullOrWhiteSpace(_findText)) return "Enter text to find.";
            var matches = FindMatches;
            if (matches.Count == 0) return "No matches.";
            var position = _findIndex >= 0 && _findIndex < matches.Count ? _findIndex + 1 : 1;
            return $"{matches.Count} match(es); showing {position}.";
        }
    }

    /// <summary>
    /// Moves the editor's selection to the next or previous match. Navigation
    /// never mutates authored content, so it runs outside the edit admission the
    /// same way every other selection movement in this session does. Only a match
    /// whose offset addresses run text is selected, because a list or table match
    /// is searched in its own joined text rather than in the run range the editor
    /// selects by; those matches still count and still advance the position.
    /// </summary>
    public Task<HomeCoreOperationResult<bool>> FindAsync(bool forward, CancellationToken cancellationToken = default) =>
        OperateAsync(token =>
        {
            token.ThrowIfCancellationRequested();
            if (_editor is null) return Task.FromResult(Failure<bool>("DocumentNotFound", "Open a document first."));
            if (_presentationPending)
                return Task.FromResult(Failure<bool>("PresentationNotAdmitted", "This prepared view is not active."));
            if (string.IsNullOrWhiteSpace(_findText))
                return Task.FromResult(Failure<bool>("EmptyQuery", "Enter text to find."));
            var matches = FindMatches;
            if (matches.Count == 0)
                return Task.FromResult(Failure<bool>("NoMatches", "No matches for that text."));
            var index = forward
                ? _findIndex + 1 >= matches.Count ? 0 : _findIndex + 1
                : _findIndex - 1 < 0 ? matches.Count - 1 : _findIndex - 1;
            _findIndex = index;
            SelectMatch(matches[index]);
            return Task.FromResult(Success(true));
        }, cancellationToken);

    private void SelectMatch(WriteFindResult match)
    {
        if (_editor is not { } editor) return;
        var block = editor.Blocks().FirstOrDefault(value => value.Id == match.BlockId);
        if (block is null) return;
        var text = block.Runs.Count > 0 ? string.Concat(block.Runs.Select(run => run.Text)) : block.PlainText;
        if (match.Offset < 0 || _findText.Length > text.Length - match.Offset) return;
        if (!text.AsSpan(match.Offset, _findText.Length).Equals(_findText, StringComparison.OrdinalIgnoreCase)) return;
        editor.SelectBlock(match.BlockId, match.Offset + _findText.Length, match.Offset, match.Offset + _findText.Length);
    }

    /// <summary>
    /// Replaces every match in the document as one admitted edit, so a single
    /// undo takes the whole replacement back. The query is rejected before it can
    /// reach the editor's replace loop, because an empty search would otherwise
    /// never advance and never terminate.
    /// </summary>
    public Task<HomeCoreOperationResult<bool>> ReplaceAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _presentationPending)
            return Task.FromResult(Failure<bool>("PresentationNotAdmitted", "This prepared view is not active."));
        if (_editor is null) return Task.FromResult(Failure<bool>("DocumentNotFound", "Open a document first."));
        if (string.IsNullOrWhiteSpace(_findText))
            return Task.FromResult(Failure<bool>("EmptyQuery", "Enter text to find."));
        if (IsReadOnly) return Task.FromResult(Failure<bool>("ReadOnly", "This document is read-only."));
        return EditAsync(editor =>
        {
            _replaceStatus = $"Replaced {editor.ReplaceAll(_findText, _replaceText)} match(es).";
            _findIndex = -1;
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Every local CUI presentation command this session dispatches. The owned
    /// Write.cui surface is validated against this catalogue, so renaming a markup
    /// action without renaming the dispatch case fails acceptance instead of
    /// rendering a control that silently reports UnsupportedFeature.
    /// </summary>
    public static readonly IReadOnlySet<string> DispatchableCommands =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "CreateDocument", "SaveDocument", "BoldSelection", "Undo", "Redo",
            "CloseDocument", "RefreshDocuments", "InspectSavedState",
            "ImportPackage", "ExportPackage", "OpenDocument",
            "ItalicSelection", "UnderlineSelection", "StrikeSelection",
            "AlignLeft", "AlignCenter", "AlignRight", "AlignJustify",
            "InsertParagraph", "InsertHeading", "InsertQuote", "InsertCode",
            "InsertBullets", "InsertNumbering", "InsertChecklist",
            "InsertPageBreak", "InsertDivider", "InsertTable", "AddListItem",
            "SetListBulleted", "SetListNumbered", "SetListChecklist",
            "AddTableRow", "AddTableColumn", "RemoveTableRow", "RemoveTableColumn",
            "SetPageA4", "SetPageLetter", "SetPagePortrait", "SetPageLandscape",
            "SetLayoutPaginated", "SetLayoutContinuous",
            "FindNext", "FindPrevious", "ReplaceAll",
            "MoveBlockUp", "MoveBlockDown", "DeleteBlock",
            "SplitRun", "MergeRun", "MergeCellRight", "SplitCell", "ApplyStyle",
            "InsertImage", "InsertEquation"
        };

    // These are local CUI presentation commands, not a new remote API catalogue.
    public bool? IsActionAvailable(string command) => !_disposed && !_presentationPending && !IsBusy &&
        (!HasUnknownCommitOutcome || command is "InspectSavedState" or "RefreshDocuments") && (command switch
    {
        "CreateDocument" => true,
        "SaveDocument" or "BoldSelection" or "ItalicSelection" or "UnderlineSelection" or "StrikeSelection"
            or "SetPageA4" or "SetPageLetter" or "SetPagePortrait" or "SetPageLandscape"
            or "SetLayoutPaginated" or "SetLayoutContinuous"
            or "AlignLeft" or "AlignCenter" or "AlignRight" or "AlignJustify"
            or "InsertParagraph" or "InsertHeading" or "InsertQuote" or "InsertCode"
            or "InsertBullets" or "InsertNumbering" or "InsertChecklist"
            or "InsertPageBreak" or "InsertDivider" or "InsertTable"
            or "InsertEquation"
            => _editor is not null && !IsReadOnly,
            "SetEquationSource" or "SetEquationAlternative" => _editor is not null && !IsReadOnly
            && _editor.SelectedBlock?.Equation is not null,
            "SetCellBackground" => _editor is not null && !IsReadOnly
            && _editor.SelectedBlock?.Table is not null && _editor.SelectedTableCellId is not null,
            "AddListItem" or "SetListBulleted" or "SetListNumbered" or "SetListChecklist"
            => _editor is not null && !IsReadOnly && _editor.SelectedBlock?.List is not null,
        "AddTableRow" or "AddTableColumn" or "RemoveTableRow" or "RemoveTableColumn"
            => _editor is not null && !IsReadOnly && _editor.SelectedBlock?.Table is not null,
        // The image is inserted from a real platform chooser, so the control
        // also reports whether this surface registered one.
        "InsertImage" => _imageSource is not null && _attachments is not null && _editor is not null && !IsReadOnly,
        // Every media property edit needs a selected image block, which the
        // editor itself refuses for any other kind of block.
        "SetMediaAlt" or "SetMediaCaption" or "SetMediaWrapping" or "SetMediaSize" or "SetMediaRotation"
            or "SetMediaCrop" => _editor is not null && !IsReadOnly
            && _editor.SelectedBlock?.Media is not null,
        "Undo" => _editor?.CanUndo == true && !IsReadOnly,
        "Redo" => _editor?.CanRedo == true && !IsReadOnly,
        // Searching and navigating matches never mutates, so a read-only document
        // still searches; only replacing needs an editable document.
        "FindNext" or "FindPrevious" => _editor is not null,
        "ReplaceAll" => _editor is not null && !IsReadOnly,
        // The editor refuses a move out of range and a delete of the document's
        // last block, so delete hides while there is nothing else left to select
        // rather than presenting a control the editor will only ever decline.
        "MoveBlockUp" or "MoveBlockDown" => _editor is not null && !IsReadOnly,
        "DeleteBlock" => _editor is not null && !IsReadOnly && _editor.Blocks().Take(2).Count() > 1,
        "SplitRun" or "MergeRun" => _editor is not null && !IsReadOnly,
        "MergeCellRight" or "SplitCell" => _editor is not null && !IsReadOnly &&
            _editor.SelectedBlock?.Table is not null,
        "ApplyStyle" => _editor is not null && !IsReadOnly,
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
            case "ItalicSelection": await ToggleCharacterAsync(WriteCharacterFormat.Italic, cancellationToken); break;
            case "UnderlineSelection": await ToggleCharacterAsync(WriteCharacterFormat.Underline, cancellationToken); break;
            case "StrikeSelection": await ToggleCharacterAsync(WriteCharacterFormat.StrikeThrough, cancellationToken); break;
            case "AlignLeft": await SetAlignmentAsync(NotesTextAlignment.Left, cancellationToken); break;
            case "AlignCenter": await SetAlignmentAsync(NotesTextAlignment.Center, cancellationToken); break;
            case "AlignRight": await SetAlignmentAsync(NotesTextAlignment.Right, cancellationToken); break;
            case "AlignJustify": await SetAlignmentAsync(NotesTextAlignment.Justify, cancellationToken); break;
            case "InsertParagraph": await InsertBlockAsync(NotesBlockKind.Paragraph, cancellationToken: cancellationToken); break;
            case "InsertHeading": await InsertBlockAsync(NotesBlockKind.Heading, styleId: "heading-1", cancellationToken: cancellationToken); break;
            case "InsertQuote": await InsertBlockAsync(NotesBlockKind.Quote, cancellationToken: cancellationToken); break;
            case "InsertCode": await InsertBlockAsync(NotesBlockKind.Code, cancellationToken: cancellationToken); break;
            case "InsertBullets": await InsertBlockAsync(NotesBlockKind.List, NotesListKind.Bulleted, cancellationToken: cancellationToken); break;
            case "InsertNumbering": await InsertBlockAsync(NotesBlockKind.List, NotesListKind.Numbered, cancellationToken: cancellationToken); break;
            case "InsertChecklist": await InsertBlockAsync(NotesBlockKind.List, NotesListKind.Checklist, cancellationToken: cancellationToken); break;
            case "InsertPageBreak": await InsertPageBreakAsync(cancellationToken); break;
            case "InsertDivider": await InsertBlockAsync(NotesBlockKind.Divider, cancellationToken: cancellationToken); break;
            case "InsertTable": await InsertTableAsync(cancellationToken); break;
            case "MoveBlockUp": await MoveBlockAsync(-1, cancellationToken); break;
            case "MoveBlockDown": await MoveBlockAsync(1, cancellationToken); break;
            case "DeleteBlock": await DeleteBlockAsync(cancellationToken); break;
            case "SplitRun": await SplitRunAsync(cancellationToken); break;
            case "MergeRun": await MergeRunAsync(cancellationToken); break;
            case "MergeCellRight": await MergeCellRightAsync(cancellationToken); break;
            case "SplitCell": await SplitCellAsync(cancellationToken); break;
            case "ApplyStyle":
                await ApplyStyleAsync(parameter switch
                {
                    WriteStyleRow selected => selected.Id,
                    string identifier => identifier,
                    _ => string.Empty
                }, cancellationToken);
                break;
            case "AddListItem": await AddListItemAsync(cancellationToken); break;
            case "SetListBulleted": await SetListKindAsync(NotesListKind.Bulleted, cancellationToken); break;
            case "SetListNumbered": await SetListKindAsync(NotesListKind.Numbered, cancellationToken); break;
            case "SetListChecklist": await SetListKindAsync(NotesListKind.Checklist, cancellationToken); break;
            case "AddTableRow": await AddTableRowAsync(cancellationToken); break;
            case "AddTableColumn": await AddTableColumnAsync(cancellationToken); break;
            case "RemoveTableRow": await RemoveTableRowAsync(cancellationToken); break;
            case "RemoveTableColumn": await RemoveTableColumnAsync(cancellationToken); break;
            case "InsertImage": await InsertImageAsync(cancellationToken); break;
            case "InsertEquation": await InsertEquationAsync(cancellationToken); break;
            case "SetPageA4": await SetPagePresetAsync("A4", cancellationToken); break;
            case "SetPageLetter": await SetPagePresetAsync("Letter", cancellationToken); break;
            case "SetPagePortrait": await SetOrientationAsync("Portrait", cancellationToken); break;
            case "SetPageLandscape": await SetOrientationAsync("Landscape", cancellationToken); break;
            case "SetLayoutPaginated": await SetLayoutModeAsync(NotesLayoutMode.Paginated, cancellationToken); break;
            case "SetLayoutContinuous": await SetLayoutModeAsync(NotesLayoutMode.Continuous, cancellationToken); break;
            case "FindNext": await FindAsync(forward: true, cancellationToken); break;
            case "FindPrevious": await FindAsync(forward: false, cancellationToken); break;
            case "ReplaceAll": await ReplaceAllAsync(cancellationToken); break;
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
            default:
                // A command that is in the owned catalogue but fell through to this
                // arm means the dispatch switch lost a case. That is an application
                // defect, not an unsupported user action, so it must not degrade to
                // a silent UnsupportedFeature failure.
                if (DispatchableCommands.Contains(command))
                    throw new InvalidOperationException(
                        "Dispatchable Write command '" + command + "' is missing a dispatch case.");
                Failure<bool>("UnsupportedFeature", "This document action is unavailable."); Notify(); break;
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

/// <summary>
/// Read-only CUI presentation over one of the document's named paragraph styles.
/// The label is derived here so the surface renders the same wording for every
/// style instead of hardcoding a button per built-in id.
/// </summary>
public sealed record WriteStyleRow(string Id, string Name, string Label) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Id" => Id, "Name" => Name, "Label" => Label, _ => null };
        return path is "Id" or "Name" or "Label";
    }
}

/// <summary>
/// CUI presentation over one live list item of the selected list block. Reads come
/// straight from the editor's model, and two-way writes are handed back to the
/// owning session so admission, selection and bound checks run on every edit
/// instead of mutating a detached copy the writer never sees.
/// </summary>
public sealed class WriteListItemRow : ICuiBindingContext, ICuiWritableBindingContext
{
    private readonly WriteBrowserSession _session;
    private readonly Guid _blockId;
    private readonly NotesListItem _item;

    public WriteListItemRow(WriteBrowserSession session, Guid blockId, NotesListItem item)
    {
        _session = session;
        _blockId = blockId;
        _item = item;
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Id" => _item.Id,
            "Text" => _item.Text,
            "Level" => _item.Level,
            "Checked" => _item.Checked,
            _ => null
        };
        return path is "Id" or "Text" or "Level" or "Checked";
    }

    public bool TrySetValue(string path, object? value) =>
        path is "Text" or "Level" or "Checked" && _session.TryEditListItem(_blockId, _item.Id, path, value);
}

/// <summary>
/// CUI presentation over one row of the selected table block, exposing its cells
/// for a nested repeat so the surface keeps the table's row structure instead of
/// flattening every cell into an anonymous list.
/// </summary>
public sealed class WriteTableRowRow : ICuiBindingContext
{
    private readonly WriteBrowserSession _session;
    private readonly Guid _blockId;
    private readonly NotesTableRow _row;

    public WriteTableRowRow(WriteBrowserSession session, Guid blockId, NotesTableRow row)
    {
        _session = session;
        _blockId = blockId;
        _row = row;
    }

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case "Id":
                value = _row.Id;
                return true;
            case "Cells":
                value = _row.Cells.Select(cell => new WriteTableCellRow(_session, _blockId, cell)).ToArray();
                return true;
            default:
                value = null;
                return false;
        }
    }
}

/// <summary>
/// CUI presentation over one live table cell. Reads come from the editor's model
/// and two-way text writes are handed back to the owning session so admission and
/// selection checks run on every edit.
/// </summary>
public sealed class WriteTableCellRow : ICuiBindingContext, ICuiWritableBindingContext
{
    private readonly WriteBrowserSession _session;
    private readonly Guid _blockId;
    private readonly NotesTableCell _cell;

    public WriteTableCellRow(WriteBrowserSession session, Guid blockId, NotesTableCell cell)
    {
        _session = session;
        _blockId = blockId;
        _cell = cell;
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Id" => _cell.Id, "Text" => _cell.Text, _ => null };
        return path is "Id" or "Text";
    }

    public bool TrySetValue(string path, object? value) =>
        path is "Text" && _session.TryEditTableCell(_blockId, _cell.Id, path, value);
}
