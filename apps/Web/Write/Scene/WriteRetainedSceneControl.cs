using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Write;
using Haven.UI;

namespace NineToOne.Web.Write;

public enum WriteSemanticMark { Unavailable, Off, On, Mixed }
public enum WriteSemanticRangeKind { Unavailable, Caret, Block, Document }
public enum WriteSemanticActionCode
{
    Applied, AppliedOwnerRetired, InvalidArgument, UnsupportedVersion, WrongThread, Disposed,
    NoEditor, ReadOnly, InputSuspended, StaleOwner, RevisionConflict,
    StaleSelection, UnsupportedFormat, Unavailable
}

/// <summary>Transient versioned context, never persisted as document content.
/// LocalStart/LocalEnd retain legacy block ranges even if their direction is unknown.
/// Owner and generation identify this adapter lifetime, not an authority grant.</summary>
public sealed record WriteSemanticSelection(
    int ContractVersion, Guid OwnerId, long Generation, Guid ArtifactId, long Revision,
    Guid? PageScopeId, Guid? AnchorPageId, Guid? CaretPageId, Guid? SelectedBlockId,
    WriteDocumentPosition? Anchor, WriteDocumentPosition? Caret,
    WriteSemanticRangeKind RangeKind, int LocalStart, int LocalEnd,
    WriteSemanticMark Bold, WriteSemanticMark Italic, WriteSemanticMark Underline, WriteSemanticMark StrikeThrough,
    bool CanEdit, bool InputAllowed, bool CanUndo, bool CanRedo, bool EditorHadFocus);

public sealed record WriteSemanticActionResult(
    bool Succeeded, WriteSemanticActionCode Code, WriteSemanticSelection? Selection);

/// <summary>
/// Browser input/lifetime composition over the original retained Write surface
/// and original scene renderer. No text layout, document model or editing logic
/// is recreated here. The owner supplies the real motion preference capability.
/// </summary>
public sealed class WriteRetainedSceneControl : Panel, IDisposable
{
    private readonly HavenSceneControl _scene;
    private WriteDocumentSurface? _surface;
    private WriteDocumentEditor? _editor;
    private bool _inputAllowed;
    private bool _restoreFocus;
    private bool _readOnly;
    private bool _workspaceLayoutLocked;
    private HavenBrush? _workspaceForeground;
    private bool _disposed;
    private Guid _semanticOwner = Guid.NewGuid();
    private long _semanticGeneration;
    public event EventHandler? SelectionChanged;

    public WriteRetainedSceneControl(Func<bool> reduceMotion)
    {
        ArgumentNullException.ThrowIfNull(reduceMotion);
        _scene = new HavenSceneControl(new HavenAvaloniaImageResolver(),
            new HavenAvaloniaNativeControlResolver(), reduceMotion)
        {
            // Shared owner currently has no Web platform enum. Never claim Windows.
            Platform = HavenPlatform.Unknown,
            IsEnabled = false
        };
        Children.Add(_scene);
        AddHandler(KeyDownEvent, GateKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, GateKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(TextInputEvent, GateText, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void SetEditor(WriteDocumentEditor? editor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(editor, _editor)) return;
        _semanticOwner = Guid.NewGuid();
        ++_semanticGeneration;
        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        var hadFocus = _scene.IsKeyboardFocusWithin;
        if (_surface is not null) _surface.SelectionChanged -= OnSelectionChanged;
        _editor = editor;
        _scene.Root = null;
        _surface = null;
        if (editor is null) return;
        _surface = new WriteDocumentSurface();
        _surface.SetWorkspaceAppearance(_workspaceForeground);
        _surface.SetReadOnly(_readOnly);
        _surface.SetWorkspaceLayoutLocked(_workspaceLayoutLocked);
        _surface.SetEditor(editor);
        _surface.SelectionChanged += OnSelectionChanged;
        _surface.SetValue(HavenProperties.Enabled, _inputAllowed);
        _surface.Accessibility.Enabled = _inputAllowed;
        _scene.Root = _surface;
        editor.Changed += OnEditorChanged;
        if (_inputAllowed && hadFocus) _scene.FocusElement(_surface);
    }

    public void SetWorkspaceAppearance(HavenBrush? foreground)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Equals(_workspaceForeground, foreground)) return;
        _workspaceForeground = foreground;
        _surface?.SetWorkspaceAppearance(foreground);
    }

    public void SetReadOnly(bool readOnly)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_readOnly == readOnly) return;
        ++_semanticGeneration;
        _readOnly = readOnly;
        _surface?.SetReadOnly(readOnly);
    }

    public void SetWorkspaceLayoutLocked(bool locked)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_workspaceLayoutLocked == locked) return;
        _workspaceLayoutLocked = locked;
        _surface?.SetWorkspaceLayoutLocked(locked);
    }

    private void OnSelectionChanged(object? sender, EventArgs args)
    {
        if (_disposed || !ReferenceEquals(sender, _surface)) return;
        ++_semanticGeneration;
        SelectionChanged?.Invoke(this, args);
    }

    /// <summary>Read on the owning UI thread. Uses the original editor's semantic
    /// positions and marks; it does not split runs, alter history or infer pixels.</summary>
    public WriteSemanticSelection CaptureSemanticSelection()
    {
        var editor = _editor;
        if (_disposed || editor is null)
            return new(1, _semanticOwner, _semanticGeneration, Guid.Empty, 0,
                null, null, null, null, null, null, WriteSemanticRangeKind.Unavailable, 0, 0,
                WriteSemanticMark.Unavailable, WriteSemanticMark.Unavailable, WriteSemanticMark.Unavailable, WriteSemanticMark.Unavailable,
                false, false, false, false, false);
        var selected = editor.SelectedBlock;
        var textAvailable = selected is not null &&
            editor.TextBlocks().Any(block => block.Id == selected.Id);
        var kind = !textAvailable ? WriteSemanticRangeKind.Unavailable : editor.HasDocumentSelection
            ? WriteSemanticRangeKind.Document : editor.HasSelection ? WriteSemanticRangeKind.Block : WriteSemanticRangeKind.Caret;
        WriteDocumentPosition? caret = textAvailable ? editor.DocumentCaret : null;
        WriteDocumentPosition? anchor = textAvailable ? editor.DocumentAnchor : null;
        if (kind == WriteSemanticRangeKind.Block)
            anchor = caret?.Offset == editor.SelectionStart ? new WriteDocumentPosition(selected!.Id, editor.SelectionEnd)
                : caret?.Offset == editor.SelectionEnd ? new WriteDocumentPosition(selected!.Id, editor.SelectionStart) : null;
        else if (kind == WriteSemanticRangeKind.Caret) anchor = caret;
        Guid? PageFor(WriteDocumentPosition? position) => position is { } value
            ? editor.Document.Sections.SelectMany(section => section.Pages)
                .FirstOrDefault(page => page.Blocks.Any(block => block.Id == value.BlockId))?.Id : null;
        var runs = new List<NotesTextRun>();
        if (textAvailable)
        {
            if (kind == WriteSemanticRangeKind.Caret)
            {
                if (editor.ActiveRun is { } run) runs.Add(run);
            }
            else foreach (var block in editor.TextBlocks())
            {
                var range = kind == WriteSemanticRangeKind.Document ? editor.SelectionForBlock(block.Id)
                    : block.Id == selected!.Id ? (editor.SelectionStart, editor.SelectionEnd) : ((int, int)?)null;
                if (range is not { } bounds) continue;
                var offset = 0;
                foreach (var run in block.Runs)
                {
                    var end = offset + run.Text.Length;
                    if (end > bounds.Item1 && offset < bounds.Item2) runs.Add(run);
                    offset = end;
                }
            }
        }
        WriteSemanticMark Mark(Func<NotesTextRun, bool> enabled) => runs.Count == 0 ? WriteSemanticMark.Unavailable
            : runs.All(enabled) ? WriteSemanticMark.On : runs.Any(enabled) ? WriteSemanticMark.Mixed : WriteSemanticMark.Off;
        return new(1, _semanticOwner, _semanticGeneration, editor.Document.Id,
            editor.Document.Version, editor.PageScopeId, PageFor(anchor), PageFor(caret), selected?.Id,
            anchor, caret, kind, editor.SelectionStart, editor.SelectionEnd,
            Mark(run => run.Bold), Mark(run => run.Italic), Mark(run => run.Underline), Mark(run => run.StrikeThrough),
            textAvailable && !_readOnly && _inputAllowed, _inputAllowed && !_disposed,
            editor.CanUndo, editor.CanRedo, _scene.IsKeyboardFocusWithin);
    }

    /// <summary>Forward one validated operation to the original engine/history.
    /// The owning app must still admit permissions and set input/read-only state.
    /// Unexpected engine/observer faults retain their original exception; they are not success.</summary>
    public WriteSemanticActionResult ToggleSemanticCharacter(
        WriteSemanticSelection? expected, WriteCharacterFormat format, bool restoreEditorFocus = false)
    {
        WriteSemanticActionResult Refuse(WriteSemanticActionCode code) => new(false, code,
            Dispatcher.UIThread.CheckAccess() && !_disposed ? CaptureSemanticSelection() : null);
        if (!Dispatcher.UIThread.CheckAccess()) return Refuse(WriteSemanticActionCode.WrongThread);
        if (_disposed) return Refuse(WriteSemanticActionCode.Disposed);
        if (expected is null) return Refuse(WriteSemanticActionCode.InvalidArgument);
        if (expected.ContractVersion != 1) return Refuse(WriteSemanticActionCode.UnsupportedVersion);
        if (_editor is null || _surface is null) return Refuse(WriteSemanticActionCode.NoEditor);
        if (_readOnly) return Refuse(WriteSemanticActionCode.ReadOnly);
        if (!_inputAllowed) return Refuse(WriteSemanticActionCode.InputSuspended);
        var current = CaptureSemanticSelection();
        if (expected.OwnerId != current.OwnerId || expected.ArtifactId != current.ArtifactId)
            return Refuse(WriteSemanticActionCode.StaleOwner);
        if (expected.Revision != current.Revision) return Refuse(WriteSemanticActionCode.RevisionConflict);
        if (expected.Generation != current.Generation || expected.SelectedBlockId != current.SelectedBlockId ||
            expected.PageScopeId != current.PageScopeId || expected.AnchorPageId != current.AnchorPageId ||
            expected.CaretPageId != current.CaretPageId || expected.Anchor != current.Anchor || expected.Caret != current.Caret ||
            expected.RangeKind != current.RangeKind || expected.LocalStart != current.LocalStart || expected.LocalEnd != current.LocalEnd)
            return Refuse(WriteSemanticActionCode.StaleSelection);
        if (format is not (WriteCharacterFormat.Bold or WriteCharacterFormat.Italic or WriteCharacterFormat.Underline or WriteCharacterFormat.StrikeThrough))
            return Refuse(WriteSemanticActionCode.UnsupportedFormat);
        if (!current.CanEdit || current.Bold == WriteSemanticMark.Unavailable)
            return Refuse(WriteSemanticActionCode.Unavailable);
        var editor = _editor!;
        var owner = _semanticOwner;
        editor.ToggleSelectionCharacter(format);
        // A synchronous owner observer may retire/rebind this view after the
        // validated mutation. It was applied to that editor, never the new one.
        if (_disposed || owner != _semanticOwner || !ReferenceEquals(editor, _editor))
            return new(true, WriteSemanticActionCode.AppliedOwnerRetired, null);
        // Selection remains engine-owned. Restore only focus the captured editor
        // actually held; otherwise a title/menu/toolbar keeps its own focus.
        if (restoreEditorFocus && expected.EditorHadFocus && !_disposed && !_readOnly && _inputAllowed && _surface is not null)
            _scene.FocusElement(_surface);
        return new(true, WriteSemanticActionCode.Applied, CaptureSemanticSelection());
    }

    public void SetInputAllowed(bool allowed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inputAllowed == allowed) return;
        ++_semanticGeneration;
        if (!allowed) _restoreFocus = _scene.IsKeyboardFocusWithin;
        _inputAllowed = allowed;
        _scene.IsEnabled = allowed;
        if (_surface is null) return;
        _surface.SetValue(HavenProperties.Enabled, allowed);
        _surface.Accessibility.Enabled = allowed;
        if (!allowed)
        {
            // The original async clipboard code checks its captured router identity.
            // Renewing that router invalidates a pending paste before save/read-only.
            // Reattach the same retained node so this does not replace document state.
            _scene.Root = null;
            _scene.Root = _surface;
        }
        else if (_restoreFocus)
        {
            // Only restore an editor focus we actually held before temporary input suspension.
            // A title box/menu/toolbar action must keep its own keyboard focus.
            _scene.FocusElement(_surface);
            _restoreFocus = false;
        }
    }

    private void GateKey(object? sender, KeyEventArgs args) { if (!_inputAllowed) args.Handled = true; }
    private void GateText(object? sender, TextInputEventArgs args) { if (!_inputAllowed) args.Handled = true; }
    private void GatePointer(object? sender, PointerEventArgs args) { if (!_inputAllowed) args.Handled = true; }
    private void OnEditorChanged(object? sender, EventArgs args)
    {
        if (_disposed || !ReferenceEquals(sender, _editor)) return;
        ++_semanticGeneration;
        _surface?.InvalidateDocument();
    }

    public void Dispose()
    {
        if (_disposed) return;
        ++_semanticGeneration;
        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        if (_surface is not null) _surface.SelectionChanged -= OnSelectionChanged;
        _scene.Root = null;
        _editor = null;
        _surface = null;
        _disposed = true;
    }
}
