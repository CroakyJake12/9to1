using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace NineToOne.Web.Productivity.Boards;

// Proposal only: the original public editor owns rich text on the exact notebook
// document passed to the existing Boards service/repository. No parallel model.
public sealed partial class BoardsBrowserCore
{
    private WriteDocumentEditor? _richEditor;
    internal WriteDocumentEditor? NativeRichEditor => _richEditor;
    internal bool RichEditingPermitted => _document is not null && _richEditor is not null &&
        !_disposed && !_presentationPending && !IsReadOnly && _pending is null &&
        _document.Sections.SelectMany(section => section.Pages)
            .All(page => _boards.GetEditMode(_document, page.Id) == BoardsPageEditMode.Edit);
    internal bool AllowRetainedRichInput => !IsBusy && RichEditingPermitted;

    private void BindRichEditor()
    {
        if (_document is null) return;
        var before = System.Text.Json.JsonSerializer.Serialize(_document, Json);
        _richEditor = new WriteDocumentEditor(_document);
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
        ++_generation; IsDirty = true; Status = "Unsaved changes"; Notify();
    }

    public Task<HomeCoreOperationResult<bool>> SelectRichTextAsync(Guid blockId, int start, int end,
        CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (_presentationPending || _richEditor is null)
            return Task.FromResult(Failure<bool>("PresentationNotAdmitted", "Open a notebook first."));
        var block = _richEditor.Blocks().FirstOrDefault(item => item.Id == blockId);
        if (block is null) return Task.FromResult(Failure<bool>("NotFound", "Choose an existing notebook block."));
        var length = string.Concat(block.Runs.Select(run => run.Text)).Length;
        if (start < 0 || end < start || end > length)
            return Task.FromResult(Failure<bool>("InvalidArgument", "Choose a valid text range."));
        _richEditor.SelectBlock(blockId, end, start, end);
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
}
