using System.ComponentModel;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace NineToOne.Web.Productivity.Present;

/// <summary>Presentation/lifecycle only; canonical editing and durable authority belong to injected owners.</summary>
public sealed class PresentBrowserSession : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IPresentRepository _repository;
    private readonly Func<Exception, bool> _unknown;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private IReadOnlyList<PresentDocumentSummary> _documents = [];
    private bool _disposed, _dirty, _presentationPending;
    private long _generation;
    private PendingCommit? _pending;
    public PresentEditor? Editor { get; private set; }
    public Guid? DocumentId => Editor?.Document.Id;
    public int DurableRevision { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsReadOnly { get; private set; }
    public bool IsDirty => _dirty || Editor?.IsLiveTextEditActive == true;
    public bool HasUnknownCommitOutcome => _pending is not null;
    public bool IsPresentationAdmitted => !_presentationPending;
    internal long EditGeneration => _generation;
    internal void PrepareUnadmittedPresentation()
    {
        if (Editor is not null || IsBusy || _disposed) throw new InvalidOperationException("Only a fresh session can prepare an unadmitted view.");
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
        if (IsBusy || _disposed || DocumentId != id || DurableRevision != revision || _generation != generation)
            throw new InvalidOperationException("An authored presentation cannot be withdrawn as an untransferred snapshot.");
        _presentationPending = true;
    }
    internal void AbandonUnadmittedPresentation()
    {
        if (!_presentationPending || IsBusy || _operations.CurrentCount != 1)
            throw new InvalidOperationException("An active or busy presentation cannot be abandoned.");
        Detach(); _disposed = true;
    }
    public string Status { get; private set; } = "No presentation open";
    public string? ErrorCode { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public PresentBrowserSession(IPresentRepository authorisedRepository, Func<Exception, bool>? unknownOutcome = null)
    { _repository = authorisedRepository ?? throw new ArgumentNullException(nameof(authorisedRepository)); _unknown = unknownOutcome ?? (_ => false); }

    public PresentDocument? GetDocumentSnapshot() => Editor is null ? null : Clone(Editor.Document);
    public Task<HomeCoreOperationResult<bool>> RefreshAsync(CancellationToken token = default) => Operate(async ct =>
    { _documents = await _repository.ListAsync(ct); return Success(true); }, token);
    public Task<HomeCoreOperationResult<Guid>> CreateAsync(string title = "Untitled presentation", CancellationToken token = default) => Operate(async ct =>
    {
        if (_presentationPending) return Failure<Guid>("PresentationNotAdmitted", "This prepared view is not active.");
        if (!await SaveBeforeLeaving(ct)) return Failed<Guid>();
        var candidate = PresentDocument.Create(title);
        _ = new PresentEditor(candidate); // The owner initializes/normalizes canonical content before the initial commit.
        var receipt = await SaveOwned(candidate, "Created browser presentation", true, ct);
        Attach(candidate, false);
        DurableRevision = receipt.Version;
        Remember(candidate);
        Status = "Saved";
        return Success(candidate.Id);
    }, token);
    public Task<HomeCoreOperationResult<Guid>> OpenAsync(Guid id, bool readOnly = false, CancellationToken token = default) => Operate(async ct =>
    {
        if (_presentationPending && Editor is not null) return Failure<Guid>("PresentationNotAdmitted", "This prepared view is not active.");
        if (id == Guid.Empty) return Failure<Guid>("InvalidArgument", "Choose a saved presentation.");
        if (!await SaveBeforeLeaving(ct)) return Failed<Guid>();
        var candidate = await _repository.LoadAsync(id, ct);
        ct.ThrowIfCancellationRequested();
        if (candidate is null) return Failure<Guid>("NotFound", "This presentation could not be found.");
        if (candidate.Id != id) return Failure<Guid>("DocumentIdMismatch", "The stored presentation identity does not match this link.");
        var loaded = Clone(candidate);
        Attach(candidate, readOnly);
        _dirty = !readOnly && (candidate.Recovery.RecoveredFromBackup || !SameContent(loaded, candidate));
        if (_dirty) ++_generation;
        Status = readOnly ? "Read-only" : _dirty ? "Recovered or initialized content; review and save" : "Saved";
        return Success(id);
    }, token);
    public Task<HomeCoreOperationResult<bool>> SaveAsync(CancellationToken token = default) => Operate(SaveCore, token);
    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken token = default) => Operate(async ct =>
        await SaveBeforeLeaving(ct) ? Success(true) : Failed<bool>(), token);
    public Task<HomeCoreOperationResult<bool>> CloseAsync(CancellationToken token = default) => Operate(async ct =>
    {
        if (_presentationPending) return Failure<bool>("PresentationNotAdmitted", "This prepared view is not active.");
        if (!await SaveBeforeLeaving(ct)) return Failed<bool>();
        Detach(); Status = "No presentation open"; return Success(true);
    }, token);
    public Task<HomeCoreOperationResult<bool>> EditAsync(Func<PresentEditor, bool> edit, CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (_presentationPending) return Task.FromResult(Failure<bool>("PresentationNotAdmitted", "This prepared view is not active."));
        if (Editor is null) return Task.FromResult(Failure<bool>("NotFound", "Open a presentation first."));
        if (IsReadOnly) return Task.FromResult(Failure<bool>("PermissionDenied", "This presentation is read-only."));
        if (_pending is not null) return Task.FromResult(Failure<bool>("CommitOutcomeUnknown", "Inspect saved state before editing this preserved draft."));
        Editor.CommitLiveTextEdit();
        var changed = edit(Editor);
        // Owner undo/redo restores content; the actual acknowledged CAS base stays fixed.
        Editor.Document.Version = DurableRevision;
        Notify();
        return Task.FromResult(Success(changed));
    }, token);

    /// <summary>Called only after the actual retained canvas invokes an owning live-text preview.</summary>
    public void RecordRetainedPreview()
    {
        if (_disposed || _presentationPending || IsBusy || IsReadOnly || _pending is not null) return;
        ++_generation; _dirty = true; Status = "Editing text; Ctrl+Enter commits, Esc cancels"; Notify();
    }

    public Task<HomeCoreOperationResult<bool>> InspectSavedStateAsync(CancellationToken token = default) => Operate(async ct =>
    {
        if (_presentationPending) return Failure<bool>("PresentationNotAdmitted", "This prepared view is not active.");
        var pending = _pending;
        if (pending is null) return Success(true);
        var saved = await _repository.LoadAsync(pending.Snapshot.Id, ct);
        if (saved is not null && saved.Id != pending.Snapshot.Id)
            return Failure<bool>("DocumentIdMismatch", "The saved identity differs from this draft.");
        if (saved?.Recovery.RecoveredFromBackup == true)
            return Failure<bool>("CommitOutcomeUnknown", "A recovery copy does not confirm the uncertain current commit.");
        if (saved is not null && saved.Version == checked(pending.BaseRevision + 1) && SameContent(saved, pending.Snapshot))
        {
            DurableRevision = saved.Version;
            Editor!.Document.Version = saved.Version;
            Editor.Document.UpdatedAt = saved.UpdatedAt;
            Editor.Document.Recovery = saved.Recovery;
            if (saved.Metadata.TryGetValue("lastSaveReason", out var reason)) Editor.Document.Metadata["lastSaveReason"] = reason;
            _dirty = pending.Generation != _generation;
            _pending = null; Remember(saved); Status = _dirty ? "Saved copy verified; newer edits remain unsaved" : "Saved copy verified";
            return Success(true);
        }
        if (saved is null && pending.BaseRevision == 0 || saved?.Version == pending.BaseRevision)
        { _pending = null; Status = "Saved state inspected; draft remains unsaved. Save explicitly to retry."; return Success(true); }
        return Failure<bool>("RevisionConflict", "Saved state differs from the preserved draft. Resolve this conflict before saving.");
    }, token);

    private async Task<HomeCoreOperationResult<bool>> SaveCore(CancellationToken ct)
    {
        if (_presentationPending) return Failure<bool>("PresentationNotAdmitted", "This prepared view is not active.");
        if (_pending is not null) return Failure<bool>("CommitOutcomeUnknown", "Save may already be committed; inspect saved state before retrying.");
        if (Editor is null) return Failure<bool>("NotFound", "Open a presentation first.");
        if (IsReadOnly) return Failure<bool>("PermissionDenied", "This presentation is read-only.");
        Editor.CommitLiveTextEdit();
        if (!IsDirty) return Success(true);
        var candidate = Clone(Editor.Document); candidate.Version = DurableRevision;
        var generation = _generation;
        var receipt = await SaveOwned(candidate, "Saved browser presentation", false, ct);
        DurableRevision = receipt.Version;
        Editor.Document.Version = receipt.Version; Editor.Document.UpdatedAt = candidate.UpdatedAt; Editor.Document.Recovery = candidate.Recovery;
        if (candidate.Metadata.TryGetValue("lastSaveReason", out var reason)) Editor.Document.Metadata["lastSaveReason"] = reason;
        _dirty = generation != _generation; Remember(candidate); Status = _dirty ? "Unsaved changes" : "Saved";
        return Success(true);
    }
    private async Task<PresentSaveResult> SaveOwned(PresentDocument candidate, string reason, bool initial, CancellationToken ct)
    {
        var snapshot = Clone(candidate); var generation = _generation;
        try { return await _repository.SaveAsync(candidate, reason, ct); }
        catch (Exception error) when (_unknown(error))
        {
            _pending = new(snapshot, snapshot.Version, generation);
            if (initial) Attach(snapshot, false);
            _dirty = true;
            throw;
        }
    }
    private async Task<bool> SaveBeforeLeaving(CancellationToken ct)
    {
        if (_pending is not null) { Failure<bool>("CommitOutcomeUnknown", "Inspect saved state before leaving this preserved draft."); return false; }
        if (!IsDirty) return true;
        if (!(await SaveCore(ct)).Succeeded) return false;
        if (!IsDirty) return true;
        Failure<bool>("UnsavedChanges", "Newer changes remain unsaved; save them before leaving."); return false;
    }
    private async Task<HomeCoreOperationResult<T>> Operate<T>(Func<CancellationToken, Task<HomeCoreOperationResult<T>>> action, CancellationToken token)
    {
        await _operations.WaitAsync(token);
        try
        {
            if (_disposed) return Failure<T>("Disposed", "This presentation session is closed.");
            IsBusy = true; ErrorCode = null; Notify();
            return await action(token);
        }
        catch (PresentRevisionConflictException) { return Failure<T>("RevisionConflict", "A newer stored revision exists. The draft is preserved."); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return Failure<T>("PermissionDenied", "The operation is not permitted. The draft is preserved."); }
        catch (ArgumentException) { return Failure<T>("InvalidArgument", "The presentation operation is invalid."); }
        catch (Exception error) when (_pending is not null && _unknown(error))
        { return Failure<T>("CommitOutcomeUnknown", "Save may already be committed; inspect saved state before retrying."); }
        catch (Exception error) when (error is IOException or JsonException)
        { return Failure<T>("ProviderUnavailable", "The presentation could not be saved or opened. The draft is preserved."); }
        finally { IsBusy = false; try { Notify(); } finally { _operations.Release(); } }
    }
    private void Attach(PresentDocument document, bool readOnly)
    { Detach(); Editor = new(document); Editor.Changed += Changed; DurableRevision = document.Version; IsReadOnly = readOnly; }
    private void Detach()
    { if (Editor is not null) Editor.Changed -= Changed; Editor = null; DurableRevision = 0; _dirty = false; IsReadOnly = false; }
    private void Changed(object? sender, EventArgs args)
    { ++_generation; _dirty = true; Status = "Unsaved changes"; Notify(); }
    private void Remember(PresentDocument document)
    {
        var summary = new PresentDocumentSummary(document.Id, document.Title, document.UpdatedAt, document.Version,
            document.Slides.Count, document.Recovery.RecoveredFromBackup);
        _documents = _documents.Where(item => item.Id != document.Id).Prepend(summary).ToArray();
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Title" => Editor?.Document.Title ?? "Present", "Status" => Status,
            "SpeakerNotes" => Editor?.SelectedSlide.SpeakerNotes ?? string.Empty,
            "SlideStatus" => Editor is null ? "No slide" : $"Slide {Editor.SelectedSlide.Order + 1} of {Editor.Document.Slides.Count}",
            "Documents" => _documents.Select(summary => new PresentSavedRow(summary)).ToArray(),
            "Slides" => Editor?.Document.Slides.Select(slide => new PresentSlideRow(slide)).ToArray() ?? [],
            "CanCreate" => IsActionAvailable("Create") == true, "CanBrowse" => IsActionAvailable("Open") == true,
            "CanEdit" => IsActionAvailable("AddSlide") == true, "CanSave" => IsActionAvailable("Save") == true,
            "CanClose" => IsActionAvailable("Close") == true, "CanUndo" => IsActionAvailable("Undo") == true,
            "CanRedo" => IsActionAvailable("Redo") == true, "CanInspect" => IsActionAvailable("Inspect") == true,
            "HasUnknownCommitOutcome" => _pending is not null,
            _ => null
        };
        return path is "Title" or "Status" or "SpeakerNotes" or "SlideStatus" or "Documents" or "Slides" or
            "CanCreate" or "CanBrowse" or "CanEdit" or "CanSave" or "CanClose" or "CanUndo" or "CanRedo" or "CanInspect" or "HasUnknownCommitOutcome";
    }
    public bool TrySetValue(string path, object? value)
    {
        if (path != "SpeakerNotes" || value is not string text || IsActionAvailable("AddSlide") != true || Editor is null) return false;
        Editor.SetSpeakerNotes(Editor.Selection.SlideId, text); Editor.Document.Version = DurableRevision; return true;
    }
    public bool? IsActionAvailable(string command) => !_disposed && !_presentationPending && !IsBusy &&
        (_pending is null || command is "Inspect" or "Refresh") && (command switch
        {
            "Create" or "Open" or "Refresh" => true,
            "SelectSlide" => Editor is not null,
            "Save" or "AddSlide" or "DuplicateSlide" or "AddText" or "AddShape" => Editor is not null && !IsReadOnly,
            "Undo" => Editor?.CanUndo == true && !IsReadOnly, "Redo" => Editor?.CanRedo == true && !IsReadOnly,
            "Close" => Editor is not null, "Inspect" => _pending is not null, _ => false
        });
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true) { Failure<bool>("UnsupportedFeature", "This presentation action is unavailable."); Notify(); return; }
        switch (command)
        {
            case "Create": await CreateAsync(token: cancellationToken); break;
            case "Refresh": await RefreshAsync(cancellationToken); break;
            case "Save": await SaveAsync(cancellationToken); break;
            case "Close": await CloseAsync(cancellationToken); break;
            case "Inspect": await InspectSavedStateAsync(cancellationToken); break;
            case "Open" when parameter is PresentSavedRow row && _documents.Any(item => item.Id == row.Summary.Id):
                await OpenAsync(row.Summary.Id, token: cancellationToken); break;
            case "SelectSlide" when parameter is PresentSlideRow row && Editor!.Document.Slides.Any(slide => slide.Id == row.Slide.Id):
                Editor.CommitLiveTextEdit(); Editor.SelectSlide(row.Slide.Id); Notify(); break;
            case "AddSlide": await EditAsync(editor => { editor.AddSlide(editor.Selection.SlideId); return true; }, cancellationToken); break;
            case "DuplicateSlide": await EditAsync(editor => { editor.DuplicateSlide(editor.Selection.SlideId); return true; }, cancellationToken); break;
            case "AddText": await EditAsync(editor => { editor.AddText(editor.Selection.SlideId, "Text"); return true; }, cancellationToken); break;
            case "AddShape": await EditAsync(editor => { editor.AddShape(editor.Selection.SlideId); return true; }, cancellationToken); break;
            case "Undo": await EditAsync(editor => editor.Undo(), cancellationToken); break;
            case "Redo": await EditAsync(editor => editor.Redo(), cancellationToken); break;
            default: Failure<bool>("InvalidArgument", "Choose a current presentation or slide."); Notify(); break;
        }
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private HomeCoreOperationResult<T> Success<T>(T value) => new(true, "Succeeded", Status, value, Revision: DurableRevision);
    private HomeCoreOperationResult<T> Failed<T>() => new(false, ErrorCode ?? "ProviderUnavailable", Status);
    private HomeCoreOperationResult<T> Failure<T>(string code, string message)
    { ErrorCode = code; Status = message; return new(false, code, message); }
    private static PresentDocument Clone(PresentDocument document) => JsonSerializer.Deserialize<PresentDocument>(JsonSerializer.Serialize(document, Json), Json)
        ?? throw new InvalidDataException("The owning presentation snapshot could not be read.");
    private static bool SameContent(PresentDocument first, PresentDocument second)
    {
        static string Content(PresentDocument value)
        { var copy = Clone(value); copy.Version = 0; copy.UpdatedAt = default; copy.Recovery = new(); copy.Metadata.Remove("lastSaveReason"); return JsonSerializer.Serialize(copy, Json); }
        return Content(first) == Content(second);
    }
    public async ValueTask DisposeAsync()
    {
        await _operations.WaitAsync();
        try
        { if (_disposed) return; if (IsDirty || _pending is not null) throw new InvalidOperationException("Save or resolve this presentation before disposal."); Detach(); _disposed = true; }
        finally { _operations.Release(); }
    }
    private sealed record PendingCommit(PresentDocument Snapshot, int BaseRevision, long Generation);
}

public sealed record PresentSavedRow(PresentDocumentSummary Summary) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Id" => Summary.Id, "Title" => Summary.Title, _ => null }; return path is "Id" or "Title"; }
}
public sealed record PresentSlideRow(PresentSlide Slide) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Id" => Slide.Id, "Title" => Slide.Title, _ => null }; return path is "Id" or "Title"; }
}
