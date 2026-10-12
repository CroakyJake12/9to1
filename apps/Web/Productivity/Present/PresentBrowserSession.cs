using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace NineToOne.Web.Productivity.Present;

/// <summary>Presentation/lifecycle only; canonical editing and durable authority belong to injected owners.</summary>
public sealed partial class PresentBrowserSession : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IPresentRepository _repository;
    private readonly Func<Exception, bool> _unknown;
    private readonly IPresentExportService? _exporter;
    private readonly IPresentImportService? _importer;
    private readonly IPresentBrowserMedia? _media;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private IReadOnlyList<PresentDocumentSummary> _documents = [];
    private bool _disposed, _dirty, _presentationPending;
    private PresentPlaybackSession? _playback;
    private string _fillColor = "#4472c4";
    private string _strokeColor = "#202020";
    private double _strokeWidth = 2;
    private string _clipboard = string.Empty;
    private string _transitionKind = "None";
    private double _transitionDuration;
    private (Guid SlideId, PresentTransitionKind Kind, double Duration)? _observedTransition;
    private string _animationEffect = "Fade";
    private string _animationTrigger = "OnClick";
    private bool _showFiles;
    private bool _showInspector;
    private bool _showStart = true;
    private long _generation;
    private PendingCommit? _pending;
    public PresentEditor? Editor { get; private set; }
    public Guid? DocumentId => Editor?.Document.Id;
    public bool IsWorkspaceActive => Editor is not null && !_showStart;
    public int DurableRevision { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsReadOnly { get; private set; }
    /// <summary>True while a presentation is running; authoring and lifecycle actions are frozen for its duration.</summary>
    public bool IsPlaying => _playback is not null;
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

    public PresentBrowserSession(IPresentRepository authorisedRepository, Func<Exception, bool>? unknownOutcome = null,
        IPresentExportService? exporter = null, IPresentImportService? importer = null, IPresentBrowserMedia? media = null)
    {
        _repository = authorisedRepository ?? throw new ArgumentNullException(nameof(authorisedRepository));
        _unknown = unknownOutcome ?? (_ => false);
        _exporter = exporter; _importer = importer; _media = media;
    }

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
        if (_presentationPending || !IsWorkspaceActive) return Task.FromResult(Failure<bool>("PresentationNotAdmitted", "This presentation editor is not active."));
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

    /// <summary>
    /// Copies the current object selection into this session's clipboard
    /// payload. Copying reads the selection without mutating the document, so
    /// it also works on a read-only deck and never disturbs the acknowledged
    /// revision or the live selection.
    /// </summary>
    private Task<HomeCoreOperationResult<bool>> CopySelectionAsync(CancellationToken token) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (Editor is null) return Task.FromResult(Failure<bool>("NotFound", "Open a presentation first."));
        var payload = Editor.CopySelection();
        if (payload.Length == 0)
        { Status = "Select an object to copy."; Notify(); return Task.FromResult(Success(false)); }
        _clipboard = payload;
        Status = "Copied selected presentation object(s).";
        Notify();
        return Task.FromResult(Success(true));
    }, token);

    /// <summary>Moves the selected objects one donor nudge step of 0.01 along an axis, snapping like the Desktop chrome.</summary>
    private Task<HomeCoreOperationResult<bool>> MoveObjectAsync(double deltaX, double deltaY, CancellationToken token) =>
        EditAsync(editor => editor.MoveSelection(deltaX, deltaY, snap: true), token);

    /// <summary>
    /// Applies the donor's shape outline colour. A vector shape takes the owner's
    /// vector stroke path, where a blank colour clears the outline. A plain shape
    /// keeps its fill and corner radius and writes a transparent stroke with a
    /// zero width for a blank colour, exactly like the Desktop chrome.
    /// </summary>
    private static bool ApplyShapeStroke(PresentEditor editor, string stroke) => editor.SetSelectedShapeAppearance(stroke: stroke);
    private static bool ApplyShapeStrokeWidth(PresentEditor editor, double width) => editor.SetSelectedShapeAppearance(strokeWidth: width);

    /// <summary>
    /// Hides or shows the current slide for the running presentation. Hiding a
    /// slide removes it from `PresentPlaybackSession`, which filters hidden
    /// slides when it builds its frame list, so this is an authoring edit over
    /// the same editor mutation the Desktop toggle drives.
    /// </summary>
    private Task<HomeCoreOperationResult<bool>> SetSlideVisibilityAsync(bool hidden, CancellationToken token) =>
        EditAsync(editor => editor.SetSlideHidden(editor.Selection.SlideId, hidden), token);

    /// <summary>
    /// Applies the transition the writer chose for the current slide. The owner
    /// declines a transition that would leave the slide unchanged, so the
    /// control reports the real result instead of pretending the deck changed.
    /// </summary>
    private Task<HomeCoreOperationResult<bool>> ApplyTransitionAsync(CancellationToken token) =>
        EditAsync(editor => Enum.TryParse<PresentTransitionKind>(_transitionKind, ignoreCase: true, out var kind)
            ? editor.SetSlideTransition(editor.Selection.SlideId, kind, _transitionDuration)
            : false, token);

    private Task<HomeCoreOperationResult<bool>> AddAnimationAsync(CancellationToken token) =>
        EditAsync(editor => Enum.TryParse<PresentAnimationEffect>(_animationEffect, ignoreCase: true, out var effect)
            && Enum.TryParse<PresentAnimationTrigger>(_animationTrigger, ignoreCase: true, out var trigger)
            && editor.AddAnimationToSelection(effect, trigger).Count > 0, token);

    private Task<HomeCoreOperationResult<bool>> RemoveSelectedAnimationsAsync(CancellationToken token) =>
        EditAsync(editor => editor.RemoveAnimationsFromSelection(), token);

    /// <summary>
    /// Moves the selected slide one position earlier or later in the deck. The
    /// editor clamps the target and declines a move that would leave the slide
    /// where it already is, so the first and last slides simply report that
    /// nothing changed instead of reshuffling the deck.
    /// </summary>
    private Task<HomeCoreOperationResult<bool>> MoveSlideAsync(int offset, CancellationToken token) =>
        EditAsync(editor =>
        {
            var index = editor.Document.Slides.FindIndex(slide => slide.Id == editor.Selection.SlideId);
            if (index < 0) return false;
            return editor.MoveSlide(editor.Selection.SlideId, index + offset);
        }, token);

    /// <summary>
    /// Writes the open deck through the owner PowerPoint exporter and hands the
    /// bytes to the browser as a download. The export never mutates the open
    /// document, so the draft, its revision and its selection all survive it.
    /// </summary>
    private Task<HomeCoreOperationResult<bool>> ExportPowerPointAsync(CancellationToken token) => Operate(async ct =>
    {
        if (Editor is null || _exporter is null || _media is null)
            return Failure<bool>("NotFound", "Open a presentation first.");
        var path = TransferPath(Editor.Document.Title, ".pptx");
        var delivered = await _exporter.ExportAsync(Editor.Document, path, ct);
        await _media.DownloadAsync(Path.GetFileName(delivered), Convert.ToBase64String(await File.ReadAllBytesAsync(delivered, ct)), ct);
        Status = $"PowerPoint download started · {Path.GetFileName(delivered)}";
        return Success(true);
    }, token);

    /// <summary>
    /// Asks the browser for a PowerPoint file, rebuilds it through the owner
    /// importer and commits the result as a new stored presentation. The
    /// current deck is saved first, so nothing in the open draft is dropped.
    /// </summary>
    private Task<HomeCoreOperationResult<Guid>> ImportPowerPointAsync(CancellationToken token) => Operate(async ct =>
    {
        if (_presentationPending) return Failure<Guid>("PresentationNotAdmitted", "This prepared view is not active.");
        if (_importer is null || _media is null)
            return Failure<Guid>("UnsupportedFeature", "PowerPoint import is unavailable in this browser.");
        if (!await SaveBeforeLeaving(ct)) return Failed<Guid>();
        var picked = await _media.PickAsync(".pptx", ct);
        if (picked is null) { Status = "Import cancelled"; return Failed<Guid>(); }
        var path = TransferPath(picked.Name, ".pptx");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(picked.Base64), ct);
        PresentDocument imported;
        try { imported = await _importer.ImportAsync(path, ct); }
        finally { try { File.Delete(path); } catch (IOException) { } }
        ct.ThrowIfCancellationRequested();
        var receipt = await SaveOwned(imported, "Imported PowerPoint presentation", true, ct);
        Attach(imported, false);
        DurableRevision = receipt.Version;
        Remember(imported);
        Status = "Imported";
        return Success(imported.Id);
    }, token);

    /// <summary>A writable staging path for one transfer, named from untrusted user text.</summary>
    private static string TransferPath(string name, string extension)
    {
        var directory = Path.Combine(Path.GetTempPath(), "9to1-present-transfer");
        Directory.CreateDirectory(directory);
        var safe = new string((Path.GetFileNameWithoutExtension(name) ?? string.Empty)
            .Select(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' ? character : '-')
            .ToArray()).Trim('-', ' ');
        if (safe.Length == 0) safe = "presentation";
        return Path.Combine(directory, safe + extension);
    }

    /// <summary>
    /// Starts playback from the selected slide. The session runs over a private
    /// snapshot so presenting never renormalizes the owner's live document; the
    /// retained canvas follows the frame by selecting the slide it points at.
    /// </summary>
    private void StartPresentation()
    {
        if (Editor is null) { Failure<bool>("NotFound", "Open a presentation first."); Notify(); return; }
        try { _playback = new PresentPlaybackSession(Clone(Editor.Document)); }
        catch (InvalidOperationException)
        { Failure<bool>("InvalidArgument", "This presentation has no visible slide to show."); Notify(); return; }
        SyncPlaybackSlide();
        Status = "Presenting";
        Notify();
    }

    /// <summary>Advances a running presentation by one animation step or slide, or steps it back.</summary>
    private void StepPresentation(int direction)
    {
        if (_playback is null || Editor is null) { Notify(); return; }
        if (direction > 0 ? _playback.Advance() : _playback.Previous()) SyncPlaybackSlide();
        Notify();
    }

    private void EndPresentation()
    {
        _playback = null;
        Status = "Presentation ended.";
        Notify();
    }

    private void SyncPlaybackSlide()
    {
        if (_playback is null || Editor is null) return;
        Editor.SelectSlide(_playback.CurrentSlide.Id);
    }

    /// <summary>Called only after the actual retained canvas invokes an owning live-text preview.</summary>
    public void RecordRetainedPreview()
    {
        if (_disposed || _presentationPending || !IsWorkspaceActive || IsBusy || IsReadOnly || _pending is not null) return;
        ++_generation; _dirty = true; Status = "Editing text; Esc returns to object selection"; Notify();
    }

    /// <summary>
    /// Called only after the actual retained canvas changes its own selection.
    /// A pointer selection is not an owning edit, so it raises no document
    /// change; without this the controls that read the selection would keep the
    /// state they held the last time an edit notified.
    /// </summary>
    public void RecordRetainedSelection()
    {
        if (_disposed || _presentationPending) return;
        var shape = Editor?.SelectedElements.FirstOrDefault(element => element.Kind == PresentElementKind.Shape);
        if (shape is not null && !string.IsNullOrWhiteSpace(shape.Style.FillColor)) _fillColor = shape.Style.FillColor;
        if (shape is not null && !string.IsNullOrWhiteSpace(shape.Style.StrokeColor) && shape.Style.StrokeColor != "#00000000")
            _strokeColor = shape.Style.StrokeColor;
        if (shape is not null && shape.Style.StrokeWidth > 0) _strokeWidth = shape.Style.StrokeWidth;
        Notify();
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
            IsBusy = true; ErrorCode = null; LastOperationError = null; Notify();
            return await action(token);
        }
        catch (PresentRevisionConflictException) { return Failure<T>("RevisionConflict", "A newer stored revision exists. The draft is preserved."); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return Failure<T>("PermissionDenied", "The operation is not permitted. The draft is preserved."); }
        catch (ArgumentException error) { LastOperationError = error; return Failure<T>("InvalidArgument", "The presentation operation is invalid."); }
        catch (Exception error) when (_pending is not null && _unknown(error))
        { return Failure<T>("CommitOutcomeUnknown", "Save may already be committed; inspect saved state before retrying."); }
        catch (Exception error) when (error is IOException or JsonException)
        { return Failure<T>("ProviderUnavailable", "The presentation could not be saved or opened. The draft is preserved."); }
        finally { IsBusy = false; try { Notify(); } finally { _operations.Release(); } }
    }
    private void Attach(PresentDocument document, bool readOnly)
    { Detach(); _clipboard = string.Empty; _showFiles = false; _showStart = false; Editor = new(document); Editor.Changed += Changed; DurableRevision = document.Version; IsReadOnly = readOnly; }
    private void Detach()
    { if (Editor is not null) Editor.Changed -= Changed; Editor = null; DurableRevision = 0; _dirty = false; IsReadOnly = false; _playback = null; _observedTransition = null; _showStart = true; }
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
        if (TryGetEditorUiValue(path, out value) || TryGetContentUiValue(path, out value)) return true;
        value = path switch
        {
            "Title" => Editor?.Document.Title ?? "Present", "Status" => Status,
            "ShowStart" => !IsWorkspaceActive, "ShowWorkspace" => IsWorkspaceActive,
            "HasDocument" => Editor is not null, "NoSavedDocuments" => _documents.Count == 0,
            "CanResume" => IsActionAvailable("ResumePresentation") == true,
            "StartStatus" => IsDirty ? "Your open presentation has unsaved changes. Resume to continue." : "Your open presentation is saved in this browser.",
            "SlideTitle" => Editor?.SelectedSlide.Title ?? "No slide selected",
            "EmptyDocument" => Editor is null,
            "SelectedText" => SelectedTextElement?.Text ?? string.Empty,
            "HasTextSelection" => SelectedTextElement is not null,
            "HasSelection" => Editor?.SelectedElements.Count > 0,
            "CanEditSelection" => IsActionAvailable("DeleteObject") == true,
            "CanDeleteSlide" => IsActionAvailable("DeleteSlide") == true,
            "CanSlideUp" => IsActionAvailable("SlideUp") == true,
            "CanSlideDown" => IsActionAvailable("SlideDown") == true,
            "CanPreviousSlide" => IsActionAvailable("PreviousSlide") == true,
            "CanNextSlide" => IsActionAvailable("NextSlide") == true,
            "HideSlideLabel" => Editor?.SelectedSlide.Hidden == true ? "Show slide" : "Hide slide",
            "ShowSidebar" => _showSidebar,
            "WorkspaceColumns" => _compact || !_showSidebar ? "0 1fr" : "224 1fr",
            "SidebarColumnSpan" => _compact ? 2 : 1,
            "SelectionStatus" => Editor is null ? string.Empty : Editor.SelectedElements.Count == 0 ? "Select an object to format or arrange" : $"{Editor.SelectedElements.Count} object(s) selected",
            "ShowInspector" => _showInspector,
            "ShowEditingArea" => !_compact || !_showInspector,
            "InspectorColumns" => _showInspector ? (_compact ? "0 1fr" : "1fr 320") : "1fr 0",
            "ShowFiles" => _showFiles, "ShowSlides" => !_showFiles,
            "SpeakerNotes" => Editor?.SelectedSlide.SpeakerNotes ?? string.Empty,
            "SlideStatus" => Editor is null ? "No slide" : $"Slide {Editor.SelectedSlide.Order + 1} of {Editor.Document.Slides.Count}"
                + (Editor is null || !Editor.SelectedSlide.Hidden ? string.Empty : " · hidden"),
            "SlideHidden" => Editor?.SelectedSlide.Hidden == true,
            "CanHideSlide" => IsActionAvailable("HideSlide") == true,
            "CanShowSlide" => IsActionAvailable("ShowSlide") == true,
            "TransitionKind" => _transitionKind,
            "TransitionDuration" => _transitionDuration,
            "AnimationEffect" => _animationEffect,
            "AnimationTrigger" => _animationTrigger,
            "CanApplyTransition" => IsActionAvailable("ApplyTransition") == true,
            "CanAddAnimation" => IsActionAvailable("AddAnimation") == true,
            "CanRemoveAnimation" => IsActionAvailable("RemoveAnimation") == true,
            "AnimationSteps" => Editor?.SelectedSlide.Animations.Select(cue => new PresentAnimationRow(cue)).ToArray() ?? [],
            "Documents" => _documents.Select(summary => new PresentSavedRow(summary)).ToArray(),
            "Slides" => Editor?.Document.Slides.Select(slide => new PresentSlideRow(slide)).ToArray() ?? [],
            "CanCreate" => IsActionAvailable("Create") == true, "CanBrowse" => IsActionAvailable("Open") == true,
            "CanEdit" => IsActionAvailable("AddSlide") == true, "CanSave" => IsActionAvailable("Save") == true,
            "CanClose" => IsActionAvailable("Close") == true, "CanUndo" => IsActionAvailable("Undo") == true,
            "CanRedo" => IsActionAvailable("Redo") == true, "CanInspect" => IsActionAvailable("Inspect") == true,
            "CanMoveSlideEarlier" => IsActionAvailable("MoveSlideEarlier") == true,
            "CanMoveSlideLater" => IsActionAvailable("MoveSlideLater") == true,
            "CanDeleteElement" => IsActionAvailable("DeleteElement") == true,
            "CanBringToFront" => IsActionAvailable("BringToFront") == true,
            "CanSendToBack" => IsActionAvailable("SendToBack") == true,
            "CanBold" => IsActionAvailable("Bold") == true,
            "CanItalic" => IsActionAvailable("Italic") == true,
            "CanTransformObject" => IsActionAvailable("Grow") == true,
            "CanAlign" => IsActionAvailable("AlignLeft") == true,
            "CanDistribute" => IsActionAvailable("DistributeHorizontal") == true,
            "CanGroup" => IsActionAvailable("Group") == true,
            "CanUngroup" => IsActionAvailable("Ungroup") == true,
            "CanBringForward" => IsActionAvailable("BringForward") == true,
            "CanSendBackward" => IsActionAvailable("SendBackward") == true,
            "FillColor" => _fillColor,
            "CanApplyFill" => IsActionAvailable("ApplyFill") == true,
            "StrokeColor" => _strokeColor,
            "StrokeWidth" => _strokeWidth,
            "CanApplyStroke" => IsActionAvailable("ApplyStroke") == true,
            "CanApplyStrokeWidth" => IsActionAvailable("ApplyStrokeWidth") == true,
            "CanFontSize" => IsActionAvailable("FontLarger") == true,
            "IsPlaying" => _playback is not null,
            "PlayStatus" => _playback is null
                ? string.Empty
                : $"Slide {_playback.SlideIndex + 1} of {_playback.SlideCount}; animation step {_playback.AnimationStep}",
            "CanPresent" => IsActionAvailable("Present") == true,
            "CanAdvance" => IsActionAvailable("Advance") == true,
            "CanPlayPrevious" => IsActionAvailable("PlayPrevious") == true,
            "CanExitPresentation" => IsActionAvailable("ExitPresentation") == true,
            "CanExportPowerPoint" => IsActionAvailable("ExportPowerPoint") == true,
            "CanImportPowerPoint" => IsActionAvailable("ImportPowerPoint") == true,
            "CanCopy" => IsActionAvailable("Copy") == true,
            "CanPaste" => IsActionAvailable("Paste") == true,
            "CanMoveObject" => IsActionAvailable("MoveObjectLeft") == true,
            "HasUnknownCommitOutcome" => _pending is not null,
            _ => null
        };
        return path is "ShowStart" or "ShowWorkspace" or "HasDocument" or "NoSavedDocuments" or "CanResume" or "StartStatus" or "ShowInspector" or "ShowEditingArea" or "InspectorColumns" or "Title" or "Status" or "SpeakerNotes" or "SlideStatus" or "Documents" or "Slides" or
            "CanCreate" or "CanBrowse" or "CanEdit" or "CanSave" or "CanClose" or "CanUndo" or "CanRedo" or "CanInspect" or "CanDeleteSlide" or "CanMoveSlideEarlier" or "CanMoveSlideLater" or "CanDeleteElement" or "CanBringToFront" or "CanSendToBack" or "CanBold" or "CanItalic" or "CanTransformObject" or "CanAlign" or "CanDistribute" or "CanGroup" or "CanUngroup" or "CanBringForward" or "CanSendBackward" or "FillColor" or "CanApplyFill" or "CanFontSize" or "StrokeColor" or "StrokeWidth" or "CanApplyStroke" or "CanApplyStrokeWidth" or "IsPlaying" or "PlayStatus" or "CanPresent" or "CanAdvance" or "CanPlayPrevious" or "CanExitPresentation" or "SlideStatus" or "SlideHidden" or "CanHideSlide" or "CanShowSlide" or "TransitionKind" or "TransitionDuration" or "AnimationEffect" or "AnimationTrigger" or "CanApplyTransition" or "CanAddAnimation" or "CanRemoveAnimation" or "AnimationSteps" or "CanExportPowerPoint" or "CanImportPowerPoint" or "CanCopy" or "CanPaste" or "CanMoveObject" or "HasUnknownCommitOutcome" or "SlideTitle" or "ShowFiles" or "ShowSlides" or "EmptyDocument" or "SelectedText" or "HasTextSelection" or "HasSelection" or "CanEditSelection" or "CanSlideUp" or "CanSlideDown" or "CanPreviousSlide" or "CanNextSlide" or "HideSlideLabel" or "ShowSidebar" or "WorkspaceColumns" or "SidebarColumnSpan" or "SelectionStatus";
    }
    public bool TrySetValue(string path, object? value)
    {
        if ((TrySetEditorUiValue(path, value) ?? TrySetContentUiValue(path, value)) is { } uiResult) return uiResult;
        if (path == "TransitionDuration" && value is string durationText)
            return double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
                && TrySetValue(path, duration);
        if (path == "SelectedText" && value is string content && IsActionAvailable("EditSelectedText") == true && SelectedTextElement is { } selected && Editor is not null)
        { Editor.CommitLiveTextEdit(); Editor.SetElementText(Editor.Selection.SlideId, selected.Id, content); Editor.Document.Version = DurableRevision; return true; }
        if (path == "SlideTitle" && value is string slideTitle && IsActionAvailable("EditSlideTitle") == true && Editor is not null)
        { Editor.SetSlideTitle(Editor.Selection.SlideId, slideTitle); Editor.Document.Version = DurableRevision; return true; }
        if (path == "Title" && value is string title && !string.IsNullOrWhiteSpace(title) && IsActionAvailable("AddSlide") == true && Editor is not null)
        { Editor.SetDocumentTitle(title); Editor.Document.Version = DurableRevision; return true; }
        if (Editor is null || IsActionAvailable("AddSlide") != true) return false;
        switch (path)
        {
            case "SpeakerNotes" when value is string notes:
                Editor.SetSpeakerNotes(Editor.Selection.SlideId, notes);
                Editor.Document.Version = DurableRevision; return true;
            case "Title" when value is string documentTitle:
                Editor.SetDocumentTitle(documentTitle);
                Editor.Document.Version = DurableRevision; return true;
            case "FillColor" when value is string color && IsActionAvailable("ApplyFill") == true:
                _fillColor = color; return true;
            case "StrokeColor" when value is string strokeColor && IsActionAvailable("ApplyStroke") == true:
                _strokeColor = strokeColor; return true;
            case "StrokeWidth" when value is double strokeWidth && IsActionAvailable("ApplyStrokeWidth") == true:
                _strokeWidth = strokeWidth; return true;
            case "TransitionKind" when value is string transitionKind && IsActionAvailable("ApplyTransition") == true:
                if (!Enum.TryParse<PresentTransitionKind>(transitionKind, ignoreCase: true, out var parsedTransition) || !Enum.IsDefined(parsedTransition)) return false;
                _transitionKind = transitionKind; return true;
            case "TransitionDuration" when value is double transitionDuration && IsActionAvailable("ApplyTransition") == true:
                // The owner clamps a duration to 0.05-30 seconds, so a value
                // outside that range is refused here rather than silently rewritten.
                if (!double.IsFinite(transitionDuration) || transitionDuration is < 0.05 or > 30) return false;
                _transitionDuration = transitionDuration; return true;
            case "AnimationEffect" when value is string animationEffect && IsActionAvailable("AddAnimation") == true:
                if (!Enum.TryParse<PresentAnimationEffect>(animationEffect, ignoreCase: true, out var parsedEffect)) return false;
                _animationEffect = animationEffect; return true;
            case "AnimationTrigger" when value is string animationTrigger && IsActionAvailable("AddAnimation") == true:
                if (!Enum.TryParse<PresentAnimationTrigger>(animationTrigger, ignoreCase: true, out var parsedTrigger)) return false;
                _animationTrigger = animationTrigger; return true;
            case "StrokeWidth" when value is string strokeWidthText &&
                 double.TryParse(strokeWidthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth) &&
                 IsActionAvailable("ApplyStrokeWidth") == true:
                _strokeWidth = parsedWidth; return true;
            default: return false;
        }
    }

    /// <summary>Index of the selected slide, or -1 while no presentation is open.</summary>
    private int SlideIndex => Editor is null
        ? -1
        : Editor.Document.Slides.FindIndex(slide => slide.Id == Editor.Selection.SlideId);

    /// <summary>The first selected object with an original editable text payload.</summary>
    private PresentElement? SelectedTextElement => Editor is null
        ? null
        : Editor.SelectedElements.FirstOrDefault(element => element.Kind is PresentElementKind.Text or PresentElementKind.Shape);

    /// <summary>
    /// Toggles bold or italic on the first selected text object while keeping
    /// every other style field the object already carries, exactly as the
    /// Desktop scene does. An object that is not text reports that nothing
    /// changed instead of overwriting its style.
    /// </summary>
    private static bool ToggleTextStyle(PresentEditor editor, bool bold)
    {
        var text = editor.SelectedElements.FirstOrDefault(element => element.Kind is PresentElementKind.Text or PresentElementKind.Shape);
        if (text is null) return false;
        var source = text.TextStyle;
        return editor.SetSelectedTextStyle(new PresentTextStyle
        {
            FontFamily = source.FontFamily,
            FontSizePoints = source.FontSizePoints,
            Bold = bold ? !source.Bold : source.Bold,
            Italic = bold ? source.Italic : !source.Italic,
            Underline = source.Underline,
            Color = source.Color,
            HorizontalAlignment = source.HorizontalAlignment,
            VerticalAlignment = source.VerticalAlignment
        });
    }

    /// <summary>
    /// Moves the first selected text object's point size while keeping every
    /// other style field the object already carries, exactly as the Desktop
    /// scene does. A non-text selection reports that nothing changed.
    /// </summary>
    private static bool ResizeSelectedFont(PresentEditor editor, double delta)
    {
        var text = editor.SelectedElements.FirstOrDefault(element => element.Kind is PresentElementKind.Text or PresentElementKind.Shape);
        if (text is null) return false;
        var source = text.TextStyle;
        var size = Math.Clamp(source.FontSizePoints + delta, 6, 400);
        if (size == source.FontSizePoints) return false;
        return editor.SetSelectedTextStyle(new PresentTextStyle
        {
            FontFamily = source.FontFamily,
            FontSizePoints = size,
            Bold = source.Bold,
            Italic = source.Italic,
            Underline = source.Underline,
            Color = source.Color,
            HorizontalAlignment = source.HorizontalAlignment,
            VerticalAlignment = source.VerticalAlignment
        });
    }

    public bool? IsActionAvailable(string command) => !_disposed && !_presentationPending && !IsBusy &&
        (!IsRenamingSlide || command is "CommitRenameSlide" or "CancelRenameSlide") &&
        (!_showHistory || command is "CloseHistory" or "RestoreVersion") &&
        (!_showHelp || command is "ShowHelp") &&
        (IsWorkspaceActive || command is "Create" or "Open" or "Refresh" or "ImportPowerPoint" or "ResumePresentation" or "Inspect" or "Close") &&
        (_pending is null || command is "Inspect" or "Refresh" or "ReturnToStart" or "ResumePresentation") &&
        (_playback is null || command is "Advance" or "PlayPrevious" or "ExitPresentation") && (command switch
        {
            "Create" or "Open" or "Refresh" => true,
            "ReturnToStart" => IsWorkspaceActive,
            "ResumePresentation" => Editor is not null && !IsWorkspaceActive,
            "ToggleInspector" or "ToggleFiles" or "ToggleNotes" or "ShowHelp" or "CloseHistory" or "ZoomFit" or "ZoomIn" or "ZoomOut" or "SetZoom" => true,
            "BeginShape" => Editor is not null && !IsReadOnly && BeginShapeInsertion is not null,
            "SetTextColor" or "EditSelectedText" => Editor is not null && !IsReadOnly && SelectedTextElement is { Locked: false },
            "EditSlideTitle" or "RenameSlide" => Editor is not null && !IsReadOnly,
            "CancelRenameSlide" => IsRenamingSlide,
            "CommitRenameSlide" => _renameSlide is { } rename && ReferenceEquals(Editor, rename.Owner) && !IsReadOnly
                && !string.IsNullOrWhiteSpace(_renameSlideName) && rename.Owner.Document.Slides.Any(slide => slide.Id == rename.SlideId),
            "SetFillColor" or "SetStrokeColor" or "SetStrokeWidth" => Editor is not null && !IsReadOnly && Editor.SelectedElements.Any(element => element.Kind == PresentElementKind.Shape && !element.Locked),
            "ShowHistory" => Editor is not null && _repository is IPresentVersionRepository,
            "RestoreVersion" => Editor is not null && !IsReadOnly && _showHistory,
            "ShowSlideProperties" or "ShowObjectProperties" => Editor is not null,
            "ToggleSidebar" => true,
            "SelectSlide" => Editor is not null,
            "Present" => Editor is not null && !IsReadOnly && Editor.Document.Slides.Any(slide => !slide.Hidden),
            "Advance" => _playback is not null &&
                (_playback.AnimationStep < _playback.CurrentSlide.Animations.Count || _playback.CanGoNext),
            "PlayPrevious" => _playback is { CanGoPrevious: true },
            "ExitPresentation" => _playback is not null,
            "Save" or "AddSlide" or "DuplicateSlide" or "AddText" or "AddShape" or "DeleteSlide"
                => Editor is not null && !IsReadOnly,
            "MoveSlideEarlier" => Editor is not null && !IsReadOnly && SlideIndex > 0,
            "MoveSlideLater" => Editor is not null && !IsReadOnly &&
                SlideIndex >= 0 && SlideIndex < Editor.Document.Slides.Count - 1,
            "DeleteElement" or "BringToFront" or "SendToBack" => Editor is not null && !IsReadOnly &&
                Editor.Selection.ElementIds.Count > 0,
            "Grow" or "Shrink" or "RotateLeft" or "RotateRight" => Editor is not null && !IsReadOnly &&
                Editor.Selection.ElementIds.Count > 0,
            "Bold" or "Italic" or "Underline" or "TextAlignLeft" or "TextAlignCenter" or "TextAlignRight" => Editor is not null && !IsReadOnly && SelectedTextElement is { Locked: false },
            "FontLarger" or "FontSmaller" => Editor is not null && !IsReadOnly && SelectedTextElement is { Locked: false },
            "ApplyFill" => Editor is not null && !IsReadOnly &&
                Editor.SelectedElements.Any(element => element.Kind == PresentElementKind.Shape),
            "ApplyStroke" or "ApplyStrokeWidth" => Editor is not null && !IsReadOnly &&
                Editor.SelectedElements is [{ Kind: PresentElementKind.Shape }],
            "AlignLeft" or "AlignCenter" or "AlignTop" or "AlignMiddle" => Editor is not null && !IsReadOnly &&
                Editor.Selection.ElementIds.Count >= 2,
            "DistributeHorizontal" or "DistributeVertical" => Editor is not null && !IsReadOnly &&
                Editor.Selection.ElementIds.Count >= 3,
            "Group" => Editor is not null && !IsReadOnly &&
                Editor.SelectedElements.Count(element => element.ParentGroupId is null) >= 2,
            "Ungroup" => Editor is not null && !IsReadOnly &&
                Editor.SelectedElements.Any(element => element.Kind == PresentElementKind.Group),
            "BringForward" or "SendBackward" => Editor is not null && !IsReadOnly &&
                Editor.Selection.ElementIds.Count > 0,
            "Copy" => Editor is not null && (CanvasTextCommands?.IsTextEditing == true ? CanvasTextCommands.HasSelectedText : Editor.Selection.ElementIds.Count > 0),
            "Cut" => Editor is not null && !IsReadOnly && (CanvasTextCommands?.IsTextEditing == true ? CanvasTextCommands.HasSelectedText : Editor.Selection.ElementIds.Count > 0),
            "DuplicateObject" => Editor is not null && !IsReadOnly && Editor.Selection.ElementIds.Count > 0,
            "SelectAllText" => CanvasTextCommands?.IsTextEditing == true,
            "Paste" => Editor is not null && !IsReadOnly && (CanvasTextCommands?.IsTextEditing == true || _clipboard.Length > 0),
            "HideSlide" => Editor is not null && !IsReadOnly && SlideIndex >= 0 && !Editor.SelectedSlide.Hidden,
            "ShowSlide" => Editor is not null && !IsReadOnly && SlideIndex >= 0 && Editor.SelectedSlide.Hidden,
            "ApplyTransition" => Editor is not null && !IsReadOnly && SlideIndex >= 0,
            "AddAnimation" => Editor is not null && !IsReadOnly && Editor.Selection.ElementIds.Count > 0,
            "RemoveAnimation" => Editor is not null && !IsReadOnly && Editor.SelectedSlide.Animations
                .Any(cue => Editor.Selection.ElementIds.Contains(cue.TargetElementId)),
            "MoveObjectLeft" or "MoveObjectRight" or "MoveObjectUp" or "MoveObjectDown" => Editor is not null &&
                !IsReadOnly && Editor.Selection.ElementIds.Count > 0,
            "ExportPowerPoint" => Editor is not null && _exporter is not null && _media is not null,
            "ImportPowerPoint" => _importer is not null && _media is not null,
            "PreviousSlide" => Editor is not null && Editor.SelectedSlide.Order > 0,
            "NextSlide" => Editor is not null && Editor.SelectedSlide.Order < Editor.Document.Slides.Count - 1,
            "ToggleHidden" => Editor is not null && !IsReadOnly,
            "DeleteObject" or "RotateObject" => Editor?.SelectedElements.Count > 0 && !IsReadOnly,
            "SlideUp" => Editor is not null && !IsReadOnly && Editor.SelectedSlide.Order > 0,
            "SlideDown" => Editor is not null && !IsReadOnly && Editor.SelectedSlide.Order < Editor.Document.Slides.Count - 1,
            "Undo" => Editor?.CanUndo == true && !IsReadOnly, "Redo" => Editor?.CanRedo == true && !IsReadOnly,
            "Close" => Editor is not null, "Inspect" => _pending is not null, _ => IsContentActionAvailable(command)
        });
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsActionAvailable(command) != true) { Failure<bool>("UnsupportedFeature", "This presentation action is unavailable."); Notify(); return; }
        if (CanvasTextCommands is { IsTextEditing: true } textCommands && command is "Cut" or "Copy" or "Paste" or "SelectAllText")
        {
            try
            {
                var applied = await textCommands.ExecuteTextCommandAsync(command, cancellationToken);
                if (!applied) Status = "The clipboard action could not complete. Your text is unchanged.";
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { LastOperationError = error; Status = "Clipboard access is unavailable. Use the keyboard shortcut or try again."; }
            Notify(); return;
        }
        if (await TryExecuteContentUiAsync(command, parameter, cancellationToken)) return;
        switch (command)
        {
            case "ReturnToStart": SetStartView(true); break;
            case "ResumePresentation": SetStartView(false); break;
            case "RenameSlide": BeginRenameSlide(); break;
            case "CancelRenameSlide": _renameSlide = null; Notify(); break;
            case "CommitRenameSlide": await CommitRenameSlideAsync(cancellationToken); break;
            case "ToggleNotes": _showNotes = !_showNotes; Notify(); break;
            case "ShowHelp": _showHelp = !_showHelp; Notify(); break;
            case "ShowHistory": await ShowHistoryAsync(cancellationToken); break;
            case "CloseHistory": _showHistory = false; Notify(); break;
            case "RestoreVersion" when parameter is PresentVersionRow version && _versions.Contains(version):
                await EditAsync(editor => editor.RestoreSavedContent(version.Document), cancellationToken); _showHistory = false; Notify(); break;
            case "ZoomFit": _fitToWindow = true; Notify(); break;
            case "ZoomIn": SetCanvasZoom((_fitToWindow ? EffectiveCanvasZoom?.Invoke() ?? _zoom : _zoom) + .25); break;
            case "ZoomOut": SetCanvasZoom((_fitToWindow ? EffectiveCanvasZoom?.Invoke() ?? _zoom : _zoom) - .25); break;
            case "SetZoom" when Number(parameter, out var zoom): SetCanvasZoom(zoom); break;
            case "SetTextColor" when parameter is string textColor && Avalonia.Media.Color.TryParse(textColor, out _):
                await EditAsync(editor => UpdateTextStyle(editor, style => style.Color = textColor), cancellationToken); break;
            case "SetFillColor" when parameter is string fill && (fill == "none" || Avalonia.Media.Color.TryParse(fill, out _)):
                await EditAsync(editor => editor.SetSelectedShapeAppearance(fill: fill == "none" ? "" : fill), cancellationToken); break;
            case "SetStrokeColor" when parameter is string stroke && (stroke == "none" || Avalonia.Media.Color.TryParse(stroke, out _)):
                await EditAsync(editor => editor.SetSelectedShapeAppearance(stroke: stroke == "none" ? "" : stroke), cancellationToken); break;
            case "SetStrokeWidth" when Number(parameter, out var strokeSize) && strokeSize is >= 0 and <= 100:
                await EditAsync(editor => editor.SetSelectedShapeAppearance(strokeWidth: strokeSize), cancellationToken); break;
            case "BeginShape" when parameter is string preset && DocumentShapePresets.All.Any(shape => shape.Id == preset):
                Editor!.CommitLiveTextEdit();
                _shapeInsertion = (Editor, Editor.Selection.SlideId, preset);
                BeginShapeInsertion?.Invoke(preset, DocumentShapePresets.Create(preset));
                Status = "Drag on the slide to draw, or click to insert. Escape cancels."; Notify(); break;
            case "ShowSlideProperties": Editor!.CommitLiveTextEdit(); Editor.SelectElements([]); _showInspector = true; Notify(); break;
            case "ShowObjectProperties": _showInspector = true; Notify(); break;
            case "ToggleInspector": _showInspector = !_showInspector; Notify(); break;
            case "ToggleFiles": _showFiles = !_showFiles; _showSidebar = true; Notify(); break;
            case "ToggleSidebar": _showSidebar = !_showSidebar; Notify(); break;
            case "PreviousSlide": Editor!.CommitLiveTextEdit(); Editor.SelectSlide(Editor.Document.Slides[Editor.SelectedSlide.Order - 1].Id); Notify(); break;
            case "NextSlide": Editor!.CommitLiveTextEdit(); Editor.SelectSlide(Editor.Document.Slides[Editor.SelectedSlide.Order + 1].Id); Notify(); break;
            case "ToggleHidden": await EditAsync(editor => editor.SetSlideHidden(editor.Selection.SlideId, !editor.SelectedSlide.Hidden), cancellationToken); break;
            case "DeleteObject": await EditAsync(editor => editor.RemoveSelectedElements(), cancellationToken); break;
            case "RotateObject": await EditAsync(editor => editor.RotateSelection(90), cancellationToken); break;
            case "SlideUp": await EditAsync(editor => editor.MoveSlide(editor.Selection.SlideId, editor.SelectedSlide.Order - 1), cancellationToken); break;
            case "SlideDown": await EditAsync(editor => editor.MoveSlide(editor.Selection.SlideId, editor.SelectedSlide.Order + 1), cancellationToken); break;
            case "Create": await CreateAsync(token: cancellationToken); break;
            case "Refresh": await RefreshAsync(cancellationToken); break;
            case "Save": await SaveAsync(cancellationToken); break;
            case "Close": await CloseAsync(cancellationToken); break;
            case "Present": StartPresentation(); break;
            case "Advance": StepPresentation(1); break;
            case "PlayPrevious": StepPresentation(-1); break;
            case "ExitPresentation": EndPresentation(); break;
            case "Inspect": await InspectSavedStateAsync(cancellationToken); break;
            case "Open" when parameter is PresentSavedRow row && _documents.Any(item => item.Id == row.Summary.Id):
                if (DocumentId == row.Summary.Id) SetStartView(false);
                else await OpenAsync(row.Summary.Id, token: cancellationToken);
                break;
            case "SelectSlide" when parameter is PresentSlideRow row && Editor!.Document.Slides.Any(slide => slide.Id == row.Slide.Id):
                Editor.CommitLiveTextEdit(); Editor.SelectSlide(row.Slide.Id); Notify(); break;
            case "AddSlide": await EditAsync(editor => { editor.AddSlide(editor.Selection.SlideId); return true; }, cancellationToken); break;
            case "DuplicateSlide": await EditAsync(editor => { editor.DuplicateSlide(editor.Selection.SlideId); return true; }, cancellationToken); break;
            case "AddText": await EditAsync(editor => { editor.AddText(editor.Selection.SlideId, "Text"); return true; }, cancellationToken); break;
            case "AddShape": await EditAsync(editor => { editor.AddShape(editor.Selection.SlideId); return true; }, cancellationToken); break;
            case "DeleteElement": await EditAsync(editor => editor.RemoveSelectedElements(), cancellationToken); break;
            case "BringToFront": await EditAsync(editor => editor.BringToFront(), cancellationToken); break;
            case "SendToBack": await EditAsync(editor => editor.SendToBack(), cancellationToken); break;
            case "Bold": await EditAsync(editor => ToggleTextStyle(editor, bold: true), cancellationToken); break;
            case "Italic": await EditAsync(editor => ToggleTextStyle(editor, bold: false), cancellationToken); break;
            case "Underline": await EditAsync(editor => UpdateTextStyle(editor, style => style.Underline = !style.Underline), cancellationToken); break;
            case "TextAlignLeft": await EditAsync(editor => UpdateTextStyle(editor, style => style.HorizontalAlignment = PresentTextHorizontalAlignment.Left), cancellationToken); break;
            case "TextAlignCenter": await EditAsync(editor => UpdateTextStyle(editor, style => style.HorizontalAlignment = PresentTextHorizontalAlignment.Center), cancellationToken); break;
            case "TextAlignRight": await EditAsync(editor => UpdateTextStyle(editor, style => style.HorizontalAlignment = PresentTextHorizontalAlignment.Right), cancellationToken); break;
            case "Grow": await EditAsync(editor => editor.ResizeSelection(0.02, 0.02), cancellationToken); break;
            case "Shrink": await EditAsync(editor => editor.ResizeSelection(-0.02, -0.02), cancellationToken); break;
            case "RotateLeft": await EditAsync(editor => editor.RotateSelection(-15), cancellationToken); break;
            case "RotateRight": await EditAsync(editor => editor.RotateSelection(15), cancellationToken); break;
            case "FontLarger": await EditAsync(editor => ResizeSelectedFont(editor, 2), cancellationToken); break;
            case "FontSmaller": await EditAsync(editor => ResizeSelectedFont(editor, -2), cancellationToken); break;
            case "ApplyFill": await EditAsync(editor => editor.SetSelectedShapeAppearance(fill: _fillColor), cancellationToken); break;
            case "ApplyStroke": await EditAsync(editor => ApplyShapeStroke(editor, _strokeColor), cancellationToken); break;
            case "ApplyStrokeWidth": await EditAsync(editor => ApplyShapeStrokeWidth(editor, _strokeWidth), cancellationToken); break;
            case "AlignLeft": await EditAsync(editor => editor.AlignSelection(PresentAlignment.Left), cancellationToken); break;
            case "AlignCenter": await EditAsync(editor => editor.AlignSelection(PresentAlignment.HorizontalCenter), cancellationToken); break;
            case "AlignTop": await EditAsync(editor => editor.AlignSelection(PresentAlignment.Top), cancellationToken); break;
            case "AlignMiddle": await EditAsync(editor => editor.AlignSelection(PresentAlignment.VerticalCenter), cancellationToken); break;
            case "DistributeHorizontal": await EditAsync(editor => editor.DistributeSelection(PresentDistribution.Horizontal), cancellationToken); break;
            case "DistributeVertical": await EditAsync(editor => editor.DistributeSelection(PresentDistribution.Vertical), cancellationToken); break;
            case "Group": await EditAsync(editor => editor.GroupSelection() is not null, cancellationToken); break;
            case "Ungroup": await EditAsync(editor => editor.UngroupSelection(), cancellationToken); break;
            case "BringForward": await EditAsync(editor => editor.BringForward(), cancellationToken); break;
            case "SendBackward": await EditAsync(editor => editor.SendBackward(), cancellationToken); break;
            case "DeleteSlide": await EditAsync(editor => editor.DeleteSlide(editor.Selection.SlideId), cancellationToken); break;
            case "MoveSlideEarlier": await MoveSlideAsync(-1, cancellationToken); break;
            case "MoveSlideLater": await MoveSlideAsync(1, cancellationToken); break;
            case "Undo": await EditAsync(editor => editor.Undo(), cancellationToken); break;
            case "Redo": await EditAsync(editor => editor.Redo(), cancellationToken); break;
            case "ExportPowerPoint": await ExportPowerPointAsync(cancellationToken); break;
            case "ImportPowerPoint": await ImportPowerPointAsync(cancellationToken); break;
            case "HideSlide": await SetSlideVisibilityAsync(true, cancellationToken); break;
            case "ShowSlide": await SetSlideVisibilityAsync(false, cancellationToken); break;
            case "ApplyTransition": await ApplyTransitionAsync(cancellationToken); break;
            case "AddAnimation": await AddAnimationAsync(cancellationToken); break;
            case "RemoveAnimation": await RemoveSelectedAnimationsAsync(cancellationToken); break;
            case "Copy": await CopySelectionAsync(cancellationToken); break;
            case "Cut":
                if ((await CopySelectionAsync(cancellationToken)).Succeeded)
                    await EditAsync(editor => editor.RemoveSelectedElements(), cancellationToken);
                break;
            case "DuplicateObject": await EditAsync(editor => editor.Paste(editor.CopySelection()).Count > 0, cancellationToken); break;
            case "Paste": await EditAsync(editor => editor.Paste(_clipboard).Count > 0, cancellationToken); break;
            case "MoveObjectLeft": await MoveObjectAsync(-0.01, 0d, cancellationToken); break;
            case "MoveObjectRight": await MoveObjectAsync(0.01, 0d, cancellationToken); break;
            case "MoveObjectUp": await MoveObjectAsync(0d, -0.01, cancellationToken); break;
            case "MoveObjectDown": await MoveObjectAsync(0d, 0.01, cancellationToken); break;
            default: Failure<bool>("InvalidArgument", "Choose a current presentation or slide."); Notify(); break;
        }
    }
    private void SetStartView(bool show)
    {
        // A view change retains the canonical editor, dirty content, selection
        // and undo history. It never runs the close/save lifecycle.
        var previous = _showStart;
        _showStart = show;
        try { Notify(); }
        catch { _showStart = previous; throw; }
    }
    private void Notify()
    {
        if (_contentEditDepth > 0) return;
        RefreshEditorUi();
        RefreshContentUi();
        // Selection, open and undo can change the owner's transition. Preserve
        // pending input through unrelated notifications, but never carry a
        // previous slide's fields into a newly selected slide.
        if (Editor is { } editor)
        {
            var transition = editor.SelectedSlide.Transition;
            var current = (editor.Selection.SlideId, transition.Kind, transition.DurationSeconds);
            if (_observedTransition != current)
            {
                _observedTransition = current;
                _transitionKind = transition.Kind.ToString();
                _transitionDuration = transition.DurationSeconds;
            }
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    internal Exception? LastOperationError { get; private set; }
    private bool _showSidebar = true;
    private bool _compact;
    public void RefreshSelection() => Notify();
    public void SetViewportWidth(double width)
    {
        var compact = width > 0 && width < 760;
        if (_viewportWidth == width) return;
        _viewportWidth = width;
        if (_compact != compact) _showSidebar = !compact;
        _compact = compact; Notify();
    }
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
    { value = path switch { "Id" => Summary.Id, "Title" => Summary.Title,
        "LocationAndModified" => $"This browser · updated {Summary.UpdatedAt:dd MMM yyyy, HH:mm}", _ => null };
        return path is "Id" or "Title" or "LocationAndModified"; }
}
public sealed record PresentSlideRow(PresentSlide Slide) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Id" => Slide.Id, "Title" => Slide.Title, _ => null }; return path is "Id" or "Title"; }
}

/// <summary>
/// CUI presentation over one animation cue on the selected slide, so the
/// animation list shows the owner's own cue rather than a copied description.
/// </summary>
public sealed record PresentAnimationRow(PresentAnimationCue Cue) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Id" => Cue.Id, "Effect" => Cue.Effect.ToString(), "Trigger" => Cue.Trigger.ToString(), _ => null }; return path is "Id" or "Effect" or "Trigger"; }
}

/// <summary>
/// Device-local file delivery for Present only: bytes leave through a download,
/// bytes arrive through a picker. Canonical content stays in the owning C# model.
/// </summary>
public interface IPresentBrowserMedia
{
    Task DownloadAsync(string name, string base64, CancellationToken cancellationToken = default);
    Task<PresentPickedFile?> PickAsync(string accept, CancellationToken cancellationToken = default);
}

/// <summary>A file the browser handed back for import; its bytes arrive as base64.</summary>
public sealed record PresentPickedFile(string Name, string Base64);
