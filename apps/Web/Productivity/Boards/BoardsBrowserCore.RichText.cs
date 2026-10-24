using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace NineToOne.Web.Productivity.Boards;

// The original public editor owns rich text on the exact notebook
// document passed to the existing Boards service/repository. No parallel model.
public sealed partial class BoardsBrowserCore
{
    private WriteDocumentEditor? _richEditor;
    private bool _sharedInkAvailable;
    private long _sharedInkBindingEpoch, _sharedInkSelectionEpoch;
    internal WriteDocumentEditor? NativeRichEditor => _richEditor;
    internal bool RichEditingPermitted => _document is not null && _richEditor is not null &&
        !_disposed && !_presentationPending && !IsHistoryPreview && !IsReadOnly && _pending is null &&
        ActivePageId is { } pageId && VisiblePages.Any(page => page.Id == pageId) &&
        _boards.GetEditMode(_document, pageId) == BoardsPageEditMode.Edit;
    internal bool AllowRetainedRichInput => !IsBusy && RichEditingPermitted;
    internal bool AllowRetainedSelection => !IsBusy && !_disposed && !_presentationPending && !IsHistoryPreview && _pending is null && _richEditor is not null;

    private void BindRichEditor(bool initializeWithoutNormalization = false)
    {
        if (_document is null || ActivePageId is not { } pageId) return;
        var before = System.Text.Json.JsonSerializer.Serialize(_document, Json);
        _richEditor = new WriteDocumentEditor(_document, pageId, initializeWithoutNormalization);
        _richEditor.InitializeSharedInkTracking();
        checked { ++_sharedInkBindingEpoch; }
        _richEditor.Changed += OnRichEditorChanged;
        // Owning initialization may create missing run IDs in a legacy document.
        // Such changes are unsaved, never attributed to an acknowledged revision.
        if (!IsReadOnly && before != System.Text.Json.JsonSerializer.Serialize(_document, Json))
        { ++_generation; IsDirty = true; Status = "Initialized content; review and save"; }
    }
    private void UnbindRichEditor()
    {
        if (_richEditor is not null) _richEditor.Changed -= OnRichEditorChanged;
        _richEditor = null;
    }
    private void OnRichEditorChanged(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _richEditor) || _document is null) return;
        if (!ReferenceEquals(_richEditor!.Document, _document))
            throw new InvalidOperationException("The retained editor must own this exact notebook document.");
        _document.Version = DurableRevision; // Undo restores authored content, never the durable receipt revision.
        ++_generation; IsDirty = true; Status = "Unsaved changes"; Notify();
    }

    public Task<HomeCoreOperationResult<bool>> SelectRichTextAsync(Guid blockId, int start, int end,
        CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (_presentationPending || IsHistoryPreview || _richEditor is null)
            return Task.FromResult(Failure<bool>("PresentationNotAdmitted", "Open a notebook first."));
        var block = _richEditor.Blocks().FirstOrDefault(item => item.Id == blockId);
        if (block is null) return Task.FromResult(Failure<bool>("NotFound", "Choose an existing notebook block."));
        var length = string.Concat(block.Runs.Select(run => run.Text)).Length;
        if (start < 0 || end < start || end > length)
            return Task.FromResult(Failure<bool>("InvalidArgument", "Choose a valid text range."));
        _richEditor.SelectBlock(blockId, end, start, end);
        NotifySelectionChanged();
        return Task.FromResult(Success(true));
    }, token);
    private Task<HomeCoreOperationResult<bool>> RichOperation(Func<WriteDocumentEditor, bool> operation,
        CancellationToken token) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (!RichEditingPermitted)
            return Task.FromResult(Failure<bool>("OperationDenied", "This notebook cannot be edited in its current state."));
        var changed = operation(_richEditor!);
        return Task.FromResult(Success(changed));
    }, token);
    public Task<HomeCoreOperationResult<bool>> InsertRichTextAsync(string text, CancellationToken token = default) =>
        RichOperation(editor => editor.InsertDocumentText(text), token);
    public Task<HomeCoreOperationResult<bool>> BoldRichSelectionAsync(CancellationToken token = default) =>
        RichOperation(editor => { editor.ToggleSelectionCharacter(WriteCharacterFormat.Bold); return true; }, token);
    public Task<HomeCoreOperationResult<bool>> UndoRichTextAsync(CancellationToken token = default) => RichOperation(editor => editor.Undo(), token);
    public Task<HomeCoreOperationResult<bool>> RedoRichTextAsync(CancellationToken token = default) => RichOperation(editor => editor.Redo(), token);

    // Capability observation only. The original host still owns Home admission, the
    // native renderer, every protected save and the lifetime of accepted callbacks.
    internal void ConfigureSharedInk(bool available) { _sharedInkAvailable = available; Notify(); }
    internal void ReportSharedInkUnavailable(string message)
    { Failure<bool>("SharedEngineUnavailable", message); Notify(); }
    internal bool SharedInkAvailable => _sharedInkAvailable;
    internal bool CanEditSharedDrawing => _sharedInkAvailable && AllowRetainedRichInput;

    public BoardsSharedInkSnapshot? CaptureSharedDrawing()
    {
        if (_disposed || _presentationPending || IsHistoryPreview || _pending is not null ||
            _richEditor is null || ActivePageId is not { } page ||
            _richEditor.SelectedBlockId is not { } id || _richEditor.CaptureSharedInk(id) is not { } content) return null;
        return new(content, _generation, _sharedInkBindingEpoch, _sharedInkSelectionEpoch, page);
    }
    private bool IsSharedDrawingCurrent(BoardsSharedInkSnapshot expected) =>
        RichEditingPermitted && _sharedInkAvailable && _richEditor is { } editor &&
        ActivePageId == expected.PageId && _generation == expected.Generation &&
        _sharedInkBindingEpoch == expected.BindingEpoch && _sharedInkSelectionEpoch == expected.SelectionEpoch &&
        editor.IsSharedInkCurrent(expected.Content);

    public Task<HomeCoreOperationResult<Guid>> InsertSharedDrawingAsync(CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (!_sharedInkAvailable || !RichEditingPermitted)
            return Task.FromResult(Failure<Guid>("OperationDenied", "Shared drawing is unavailable in this page's current state."));
        return Task.FromResult(Success(_richEditor!.InsertSharedInk().Id));
    }, token);

    public Task<HomeCoreOperationResult<bool>> SelectSharedDrawingAsync(Guid blockId, CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (_disposed || _presentationPending || IsHistoryPreview || _pending is not null ||
            _richEditor?.CaptureSharedInk(blockId) is null)
            return Task.FromResult(Failure<bool>("DrawingNotFound", "Choose an existing shared drawing on this page."));
        _richEditor.SelectBlock(blockId); NotifySelectionChanged();
        return Task.FromResult(Success(true));
    }, token);

    public Task<HomeCoreOperationResult<bool>> AppendSharedDrawingAsync(BoardsSharedInkSnapshot expected,
        System.Text.Json.JsonElement proposed, Func<System.Text.Json.JsonElement, System.Text.Json.JsonElement> captureCanonical,
        CancellationToken token = default) => Operate(async ct =>
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(captureCanonical);
        ct.ThrowIfCancellationRequested();
        if (!IsSharedDrawingCurrent(expected))
            return Failure<bool>("StaleDrawing", "The drawing, selection or authoring mode changed; no stroke was added.");
        var editor = _richEditor!;
        System.Text.Json.JsonElement Capture(System.Text.Json.JsonElement source)
        {
            var captured = captureCanonical(source);
            ct.ThrowIfCancellationRequested();
            if (!IsSharedDrawingCurrent(expected)) throw new SharedDrawingStaleException();
            return captured;
        }
        try
        {
            if (!editor.AppendSharedInk(expected.Content, proposed, Capture))
                return Failure<bool>("StaleDrawing", "The drawing changed; no stroke was added.");
        }
        catch (SharedDrawingStaleException)
        { return Failure<bool>("StaleDrawing", "The drawing changed during validation; no stroke was added."); }
        // A refused save retains the accepted undoable mutation as a dirty draft.
        // Only the original repository's acknowledged receipt establishes Saved.
        return await SaveCore(ct);
    }, token);
    private sealed class SharedDrawingStaleException : Exception { }
}

public sealed record BoardsSharedInkSnapshot(WriteSharedInkSnapshot Content, long Generation,
    long BindingEpoch, long SelectionEpoch, Guid PageId);
