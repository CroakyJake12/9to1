using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Haven.Application;

namespace HavenOS.Apps.Canvas;

public enum CanvasWorkspaceCommandKind { Open, Import, Flush, Undo, Redo, FitView, ZoomIn, ZoomOut }
public sealed record CanvasWorkspaceCommand(CanvasWorkspaceCommandKind Kind, Guid? ArtifactId, Guid? BaseRevisionId);
public sealed record CanvasDocumentLocationInfo(string FilesLocationName, string ParentFolderId);

/// <summary>Read-only accessible projection of canonical Canvas content; it grants no object or Files authority.</summary>
public sealed record CanvasAccessibleObjectRow(string Id, string AccessibleName, string Type, string Details)
{
    public string InspectAccessibleName => $"Inspect {AccessibleName}";
}

/// <summary>App-owned semantic bindings; the native host supplies Home-brokered typed dispatch.</summary>
public sealed class CanvasCuiWorkspace : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly record struct AccessibleDescriptor(int PageIndex, bool IsStroke, int EntityIndex,
        int EntityOrder, int LayerIndex, int LayerOrder);
    private readonly Func<CanvasWorkspaceCommand, CancellationToken, ValueTask> _dispatch;
    private readonly Func<CanvasWorkspaceCommandKind, bool> _isAvailable;
    private readonly CanvasToolState? _tools;
    private readonly ICanvasEraserInput? _eraser;
    private readonly ICanvasSelectionInput? _selectionInput;
    private CanvasArtifact? _snapshot;
    private CanvasDocumentLocationInfo? _documentLocation;
    private CanvasPage[] _accessiblePages = [];
    private AccessibleDescriptor[] _accessibleDescriptors = [];
    private string _persistence = "No Canvas artifact is open";
    private string _compatibility = "";
    private string _selection = "No content selected";
    private CanvasAccessibleObjectRow[] _accessibleObjects = [];
    private bool _accessibleListVisible;
    private int _accessiblePage;
    private string? _accessibleInspectionSummary;
    private bool _commandSurfaceVisible;
    private bool _documentInfoVisible;
    private const int AccessiblePageSize = 50;
    public event PropertyChangedEventHandler? PropertyChanged;

    public CanvasCuiWorkspace(Func<CanvasWorkspaceCommand, CancellationToken, ValueTask> dispatch,
        Func<CanvasWorkspaceCommandKind, bool> isAvailable, CanvasToolState? tools = null,ICanvasEraserInput? eraser = null, ICanvasSelectionInput? selection = null)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _isAvailable = isAvailable ?? throw new ArgumentNullException(nameof(isAvailable));
        _tools = tools;_eraser=eraser;_selectionInput=selection;
        if (_selectionInput is not null) _selectionInput.Changed += ToolsChanged;
        if(_eraser is not null)_eraser.Changed+=ToolsChanged;
        if (_tools is not null) _tools.Changed += ToolsChanged;
    }

    public static CuiDocument LoadDocument()
    {
        const string name = "HavenOS.Apps.Canvas.UI.CanvasWorkspace.cui";
        using var stream = typeof(CanvasCuiWorkspace).Assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Canonical Canvas CUI source is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), name);
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }

    public void Refresh(CanvasArtifact? snapshot, string persistenceStatus, CanvasImportCompatibilityReport? report = null,
        IReadOnlyList<Guid>? selectedIds = null, CanvasDocumentLocationInfo? documentLocation = null)
    {
        (Guid ArtifactId, Guid RevisionId)? previousIdentity = _snapshot is { } previous
            ? (previous.ArtifactId, previous.RevisionId) : null;
        (Guid ArtifactId, Guid RevisionId)? nextIdentity = snapshot is { } incoming
            ? (incoming.ArtifactId, incoming.RevisionId) : null;
        _persistence = persistenceStatus;
        _compatibility = report is null ? "" : string.Join(" ", report.Issues.Select(issue => $"{issue.Feature}: {issue.Disposition}. {issue.Reason}"));
        _selection = selectedIds is { Count: > 0 } ? $"{selectedIds.Count} selected: {string.Join(", ", selectedIds)}" : "No content selected";
        _documentLocation = documentLocation;
        if (previousIdentity != nextIdentity)
        {
            // A semantic revision is immutable. Transient tool/status refreshes
            // must not clone/serialize every page and object in the document.
            _snapshot = snapshot is null ? null : CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(snapshot));
            (_accessiblePages, _accessibleDescriptors) = BuildAccessibleIndex(_snapshot);
            _accessibleInspectionSummary = null;
            _commandSurfaceVisible = false;
            _documentInfoVisible = false;
            _accessiblePage = Math.Clamp(_accessiblePage, 0, Math.Max(0, (_accessibleDescriptors.Length - 1) / AccessiblePageSize));
            MaterializeAccessiblePage();
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public bool TryGetValue(string path, out object? value)
    {
        foreach (var definition in CanvasToolState.Definitions)
        {
            if (path == "Can" + definition.DisplayName + "Tool")
            {
                value = IsActionAvailable(definition.ToolId) == true;
                return true;
            }
            if (path == definition.DisplayName + "ToolLabel")
            {
                var glyph = definition.Tool switch
                {
                    CanvasPrimaryTool.Select => "↖", CanvasPrimaryTool.Pan => "↔", CanvasPrimaryTool.Pen => "✎",
                    CanvasPrimaryTool.Eraser => "⌫", CanvasPrimaryTool.Insert => "+", CanvasPrimaryTool.Tools => "⚒", _ => "✦",
                };
                value = (_tools?.Selected == definition.Tool ? "● " : "") + glyph + " " + definition.DisplayName;
                return true;
            }
        }
        value = path switch
        {
            "DisplayName" => _snapshot?.DisplayName ?? "Canvas",
            "PersistenceStatus" => _persistence,
            "CompatibilityStatus" => _compatibility,
            "SelectionSummary" => _selectionInput?.TargetSummary ?? _selection,
            "SelectionOptionsVisible" => _tools?.Selected == CanvasPrimaryTool.Select,
            "CanChooseSelection" => SelectionActionAvailable("9to1.Canvas.Selection.Next"),
            "CanClearSelection" => SelectionActionAvailable("9to1.Canvas.Selection.Clear"),
            "CanNudgeSelection" => SelectionActionAvailable("9to1.Canvas.Selection.Right"),
            "CanReviewSelectionMove" => SelectionActionAvailable("9to1.Canvas.Selection.Move"),
            "CanDeleteSelection" => SelectionActionAvailable("9to1.Canvas.Selection.Delete"),
            "SelectedStrokeSummary" => _selectionInput?.TargetSummary ?? "No stroke selected",
            "SelectionMoveSummary" => _selectionInput?.MoveSummary ?? "No move preview",
            "SelectionInputStatus" => _selectionInput?.Status ?? "The native selection port is unavailable",
            "SelectionFeedbackStatus" => _selectionInput?.FeedbackStatus ?? "Selection feedback is unavailable",
            "AccessibleListVisible" => _accessibleListVisible,
            "CanToggleAccessibleList" => _snapshot is not null,
            "AccessibleObjects" => _accessibleObjects,
            "AccessibleObjectCount" => _accessibleDescriptors.Length,
            "AccessibleInspectionSummary" => _accessibleInspectionSummary ?? "Tab to an object row and press Enter to inspect its canonical metadata.",
            "AccessiblePageSummary" => _accessibleDescriptors.Length == 0 ? "No inspectable Canvas objects" :
                $"Objects {(_accessiblePage * AccessiblePageSize) + 1}–{Math.Min((_accessiblePage + 1) * AccessiblePageSize, _accessibleDescriptors.Length)} of {_accessibleDescriptors.Length}",
            "CanPreviousAccessiblePage" => _accessiblePage > 0,
            "CanNextAccessiblePage" => (_accessiblePage + 1) * AccessiblePageSize < _accessibleDescriptors.Length,
            "CommandSurfaceVisible" => _commandSurfaceVisible,
            "CanToggleCommandSurface" => _snapshot is not null,
            "DocumentInfoVisible" => _documentInfoVisible,
            "CanToggleDocumentInfo" => _snapshot is not null,
            "DocumentInfoAccessibleName" => $"Canvas document details for {_snapshot?.DisplayName ?? "Canvas"}",
            "CanvasMode" => _snapshot?.CanvasMode.ToString() ?? "Unknown",
            "PageCount" => _snapshot?.PageOrder.Count ?? 0,
            "FilesLocationName" => _documentLocation?.FilesLocationName ?? "Unavailable",
            "ParentFolderId" => _documentLocation?.ParentFolderId ?? "Unavailable",
            "ArtifactId" => _snapshot?.ArtifactId,
            "RevisionId" => _snapshot?.RevisionId,
            "CanOpen" => IsActionAvailable("9to1.Canvas.Artifact.Open") == true,
            "CanImport" => IsActionAvailable("9to1.Canvas.Import") == true,
            "CanFlush" => IsActionAvailable("9to1.Canvas.Artifact.Save") == true,
            "CanUndo" => IsActionAvailable("9to1.Canvas.History.Undo") == true,
            "CanFitView" => IsActionAvailable("9to1.Canvas.View.Fit") == true,
            "CanZoomIn" => IsActionAvailable("9to1.Canvas.View.ZoomIn") == true,
            "CanZoomOut" => IsActionAvailable("9to1.Canvas.View.ZoomOut") == true,
            "CanRedo" => IsActionAvailable("9to1.Canvas.History.Redo") == true,
            "CanNaturalErase" => EraserOptionAvailable("9to1.Canvas.Erase.Natural"),
            "CanQuickErase" => EraserOptionAvailable("9to1.Canvas.Erase.Quick"),
            "CanWholeErase" => EraserOptionAvailable("9to1.Canvas.Erase.Whole"),
            "CanPartialErase" => EraserOptionAvailable("9to1.Canvas.Erase.Partial"),
            "CanEditEraser" => _eraser?.CanCapture==true && _tools?.EraserOptions.Mode==CanvasEraserMode.Natural,
            "CanChooseEraseTarget" => _eraser?.CanCapture==true && _tools?.EraserOptions.Mode==CanvasEraserMode.Quick,
            "CanEraseChosenTarget" => _eraser?.CanCapture==true && _eraser.HasTarget && _tools?.EraserOptions.Mode==CanvasEraserMode.Quick,
            "EraseTargetSummary" => _eraser?.TargetSummary ?? "No stroke chosen",
            "EraseInputStatus" => _eraser?.Status ?? "Eraser input is unavailable",
            "PenOptionsVisible" => _tools?.Selected==CanvasPrimaryTool.Pen,
            "EraserOptionsVisible" => _tools?.Selected==CanvasPrimaryTool.Eraser,
            "CanEditInk" => IsActionAvailable("9to1.Canvas.Ink.Solid") == true,
            "SelectedToolName" => _tools?.Selected.ToString() ?? "Select",
            "ToolOptionsVisible" => _tools is not null && _tools.Selected != CanvasPrimaryTool.AI && !CanvasToolState.Definitions[(int)_tools.Selected].Options.IsEmpty,
            "ToolOptionsSummary" => OptionsSummary(),
            "ToolCapabilityStatus" => _tools is null ? "Native tool input is unavailable" : _tools.Capability(_tools.Selected).Reason,
            "AiSurfaceVisible" => _tools?.Ai?.Snapshot.Visibility == CanvasAiSurfaceVisibility.Expanded,
            "AiIndicatorVisible" => _tools?.Ai?.Snapshot.Indicator is { } indicator && indicator != CanvasAiSessionIndicator.None,
            "AiIndicatorLabel" => _tools?.Ai?.Snapshot.Indicator.ToString() ?? "AI",
            _ => null
        };
        return path is "DisplayName" or "PersistenceStatus" or "CompatibilityStatus" or "SelectionSummary" or "ArtifactId" or "RevisionId"
            or "CanOpen" or "CanImport" or "CanFlush" or "CanUndo" or "CanRedo" or "CanEditInk"
            or "CanFitView" or "CanZoomIn" or "CanZoomOut"
            or "CanNaturalErase" or "CanQuickErase" or "CanWholeErase" or "CanPartialErase" or "CanEditEraser" or "CanChooseEraseTarget" or "CanEraseChosenTarget" or "EraseTargetSummary" or "EraseInputStatus" or "EraserOptionsVisible" or "PenOptionsVisible"
            or "SelectionOptionsVisible" or "CanChooseSelection" or "CanClearSelection" or "CanNudgeSelection" or "CanReviewSelectionMove" or "CanDeleteSelection"
            or "SelectedStrokeSummary" or "SelectionMoveSummary" or "SelectionInputStatus" or "SelectionFeedbackStatus"
            or "AccessibleListVisible" or "CanToggleAccessibleList" or "AccessibleObjects" or "AccessibleObjectCount" or "AccessiblePageSummary"
            or "AccessibleInspectionSummary" or "CanPreviousAccessiblePage" or "CanNextAccessiblePage"
            or "CommandSurfaceVisible" or "CanToggleCommandSurface" or "DocumentInfoVisible" or "CanToggleDocumentInfo"
            or "DocumentInfoAccessibleName" or "CanvasMode" or "PageCount" or "FilesLocationName" or "ParentFolderId"
            or "SelectedToolName" or "ToolOptionsVisible" or "ToolOptionsSummary" or "ToolCapabilityStatus" or "AiSurfaceVisible" or "AiIndicatorVisible" or "AiIndicatorLabel";
    }

    /// <summary>The owning host calls this when its live Home/read-only/capability state changes.</summary>
    public void RefreshAvailability() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public bool? IsActionAvailable(string command)
    {
        if (command == "9to1.Canvas.Accessibility.ToggleObjects") return _snapshot is not null;
        if (command == "9to1.Canvas.Accessibility.Inspect") return _snapshot is not null && _accessibleListVisible;
        if (command == "9to1.Canvas.Accessibility.PreviousPage") return _snapshot is not null && _accessiblePage > 0;
        if (command == "9to1.Canvas.Accessibility.NextPage") return _snapshot is not null && (_accessiblePage + 1) * AccessiblePageSize < _accessibleDescriptors.Length;
        if (command is "9to1.Canvas.DocumentMenu.Toggle" or "9to1.Canvas.DocumentInfo.Toggle") return _snapshot is not null;
        if (command.StartsWith("9to1.Canvas.Selection.", StringComparison.Ordinal)) return SelectionActionAvailable(command);
        if(command.StartsWith("9to1.Canvas.Erase.",StringComparison.Ordinal))return EraserOptionAvailable(command);
        if (IsInkOption(command)) return _snapshot is not null && _tools?.Selected == CanvasPrimaryTool.Pen && _tools.Capability(CanvasPrimaryTool.Pen).Available;
        if (TryTool(command, out var tool)) return _snapshot is not null && _tools?.Capability(tool).Available == true;
        return TryCommand(command, out var kind) && _isAvailable(kind)
            && (_snapshot is not null || kind is CanvasWorkspaceCommandKind.Open or CanvasWorkspaceCommandKind.Import);
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null && command != "9to1.Canvas.Accessibility.Inspect")
            throw new ArgumentException("Canvas document commands do not accept raw paths or caller identity arguments.", nameof(parameter));
        cancellationToken.ThrowIfCancellationRequested();
        if (command == "9to1.Canvas.Accessibility.Inspect")
        {
            if (parameter is not string id || !_accessibleListVisible || _snapshot is null)
                throw new ArgumentException("Accessible inspection must target a visible row from the current list page.", nameof(parameter));
            var row = _accessibleObjects.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (row is null) throw new InvalidOperationException("The accessible object row is stale or belongs to another page.");
            _accessibleInspectionSummary = $"{row.AccessibleName}. {row.Type}. {row.Details}";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            return ValueTask.CompletedTask;
        }
        if (command == "9to1.Canvas.DocumentMenu.Toggle")
        {
            if (IsActionAvailable(command) != true) throw new NotSupportedException("The Canvas command surface requires an open document.");
            _commandSurfaceVisible = !_commandSurfaceVisible;
            if (_commandSurfaceVisible) _documentInfoVisible = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            return ValueTask.CompletedTask;
        }
        if (command == "9to1.Canvas.DocumentInfo.Toggle")
        {
            if (IsActionAvailable(command) != true) throw new NotSupportedException("Canvas document information requires an open document.");
            _documentInfoVisible = !_documentInfoVisible;
            if (_documentInfoVisible) _commandSurfaceVisible = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            return ValueTask.CompletedTask;
        }
        if (command == "9to1.Canvas.Accessibility.ToggleObjects")
        {
            if (IsActionAvailable(command) != true) throw new NotSupportedException("The accessible object list requires an open Canvas artifact.");
            _accessibleListVisible = !_accessibleListVisible;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            return ValueTask.CompletedTask;
        }
        if (command is "9to1.Canvas.Accessibility.PreviousPage" or "9to1.Canvas.Accessibility.NextPage")
        {
            var next = command.EndsWith("PreviousPage", StringComparison.Ordinal) ? _accessiblePage - 1 : _accessiblePage + 1;
            var max = Math.Max(0, (_accessibleDescriptors.Length - 1) / AccessiblePageSize);
            if (next < 0 || next > max) throw new NotSupportedException("There is no additional accessible-object page.");
            _accessiblePage = next;
            _accessibleInspectionSummary = null;
            MaterializeAccessiblePage();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            return ValueTask.CompletedTask;
        }
        if (command.StartsWith("9to1.Canvas.Selection.", StringComparison.Ordinal))
        {
            if (!SelectionActionAvailable(command)) throw new NotSupportedException("This selection action is unavailable in the current original revision.");
            switch (command)
            {
                case "9to1.Canvas.Selection.Previous": _selectionInput!.ChooseTarget(-1); break;
                case "9to1.Canvas.Selection.Next": _selectionInput!.ChooseTarget(1); break;
                case "9to1.Canvas.Selection.Clear": _selectionInput!.ClearTarget(); break;
                case "9to1.Canvas.Selection.Left": _selectionInput!.AdjustMove(-10, 0); break;
                case "9to1.Canvas.Selection.Right": _selectionInput!.AdjustMove(10, 0); break;
                case "9to1.Canvas.Selection.Up": _selectionInput!.AdjustMove(0, -10); break;
                case "9to1.Canvas.Selection.Down": _selectionInput!.AdjustMove(0, 10); break;
                case "9to1.Canvas.Selection.Move": return new(_selectionInput!.RequestMoveAsync(cancellationToken));
                case "9to1.Canvas.Selection.Delete": return new(_selectionInput!.RequestDeleteAsync(cancellationToken));
            }
            return ValueTask.CompletedTask;
        }
        if(command.StartsWith("9to1.Canvas.Erase.",StringComparison.Ordinal))
        {
            if(!EraserOptionAvailable(command))throw new NotSupportedException("This eraser action is unavailable in the current document.");
            if(command=="9to1.Canvas.Erase.Previous" || command=="9to1.Canvas.Erase.Next")
                _eraser!.ChooseTarget(command.EndsWith("Previous",StringComparison.Ordinal) ? -1 : 1);
            else if(command=="9to1.Canvas.Erase.Chosen")return new ValueTask(_eraser!.RequestChosenTargetAsync(cancellationToken));
            else _tools!.SetEraserOptions(ProposedEraserOptions(command));
            return ValueTask.CompletedTask;
        }
        if (IsInkOption(command))
        {
            if (IsActionAvailable(command) != true) throw new NotSupportedException("Rnote pen options require the active native pen adapter");
            var style = _tools!.InkStyle;
            _tools.SetInkStyle(command switch
            {
                "9to1.Canvas.Ink.Solid" => style with { Kind = CanvasRnoteInkKind.Solid },
                "9to1.Canvas.Ink.Marker" => style with { Kind = CanvasRnoteInkKind.Marker },
                "9to1.Canvas.Ink.Black" => style with { Color = "#FF000000" },
                "9to1.Canvas.Ink.Red" => style with { Color = "#FFFF0000" },
                "9to1.Canvas.Ink.Blue" => style with { Color = "#FF0000FF" },
                "9to1.Canvas.Ink.Narrower" => style with { BaseWidth = Math.Max(0.5, style.BaseWidth - 0.5) },
                "9to1.Canvas.Ink.Wider" => style with { BaseWidth = Math.Min(200, style.BaseWidth + 0.5) },
                "9to1.Canvas.Ink.LessOpaque" => style with { Opacity = Math.Max(0, style.Opacity - 0.1) },
                "9to1.Canvas.Ink.MoreOpaque" => style with { Opacity = Math.Min(1, style.Opacity + 0.1) },
                _ => throw new NotSupportedException(),
            });
            return ValueTask.CompletedTask;
        }
        if (TryTool(command, out var tool))
        {
            if (IsActionAvailable(command) != true) throw new NotSupportedException(_tools?.Capability(tool).Reason ?? "Native tool input is unavailable");
            _tools!.Select(tool);
            return ValueTask.CompletedTask;
        }
        if (!TryCommand(command, out var kind) || IsActionAvailable(command) != true) throw new NotSupportedException("The Canvas action is unavailable in this host state.");
        // Capture the target at invocation. Later focus/selection changes cannot
        // silently retarget the operation while Home is authorizing it.
        var dispatch = _dispatch(new(kind, _snapshot?.ArtifactId, _snapshot?.RevisionId), cancellationToken);
        if (_commandSurfaceVisible)
        {
            _commandSurfaceVisible = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
        return dispatch;
    }

    private static bool TryCommand(string command, out CanvasWorkspaceCommandKind kind)
    {
        kind = command switch
        {
            "9to1.Canvas.Artifact.Open" => CanvasWorkspaceCommandKind.Open,
            "9to1.Canvas.Import" => CanvasWorkspaceCommandKind.Import,
            "9to1.Canvas.Artifact.Save" => CanvasWorkspaceCommandKind.Flush,
            "9to1.Canvas.History.Undo" => CanvasWorkspaceCommandKind.Undo,
            "9to1.Canvas.History.Redo" => CanvasWorkspaceCommandKind.Redo,
            "9to1.Canvas.View.Fit" => CanvasWorkspaceCommandKind.FitView,
            "9to1.Canvas.View.ZoomIn" => CanvasWorkspaceCommandKind.ZoomIn,
            "9to1.Canvas.View.ZoomOut" => CanvasWorkspaceCommandKind.ZoomOut,
            _ => (CanvasWorkspaceCommandKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    private static bool TryTool(string command, out CanvasPrimaryTool tool)
    {
        foreach (var definition in CanvasToolState.Definitions)
            if (command == definition.ToolId) { tool = definition.Tool; return true; }
        tool = default;
        return false;
    }

    private static bool IsInkOption(string command) => command is "9to1.Canvas.Ink.Solid" or "9to1.Canvas.Ink.Marker"
        or "9to1.Canvas.Ink.Black" or "9to1.Canvas.Ink.Red" or "9to1.Canvas.Ink.Blue" or "9to1.Canvas.Ink.Narrower"
        or "9to1.Canvas.Ink.Wider" or "9to1.Canvas.Ink.LessOpaque" or "9to1.Canvas.Ink.MoreOpaque";

    private CanvasEraserOptions ProposedEraserOptions(string command)
    {
        var options=_tools!.EraserOptions;
        return command switch
        {
            "9to1.Canvas.Erase.Natural"=>options with {Mode=CanvasEraserMode.Natural},
            "9to1.Canvas.Erase.Quick"=>options with {Mode=CanvasEraserMode.Quick},
            "9to1.Canvas.Erase.Whole"=>options with {Mode=CanvasEraserMode.Natural,NaturalStyle=CanvasNaturalEraserStyle.WholeStroke},
            "9to1.Canvas.Erase.Partial"=>options with {Mode=CanvasEraserMode.Natural,NaturalStyle=CanvasNaturalEraserStyle.PartialStroke},
            "9to1.Canvas.Erase.Narrower"=>options with {Width=Math.Max(1,options.Width-1)},
            "9to1.Canvas.Erase.Wider"=>options with {Width=Math.Min(500,options.Width+1)},
            _=>throw new NotSupportedException("Unknown eraser option.")
        };
    }
    private bool EraserOptionAvailable(string command)
    {
        if(_snapshot is null || _tools is null || _tools.Selected!=CanvasPrimaryTool.Eraser || _eraser?.HasSubmittedOperation==true)return false;
        if(command is "9to1.Canvas.Erase.Previous" or "9to1.Canvas.Erase.Next")return _eraser?.CanCapture==true && _tools.EraserOptions.Mode==CanvasEraserMode.Quick;
        if(command=="9to1.Canvas.Erase.Chosen")return _eraser?.CanCapture==true && _eraser.HasTarget && _tools.EraserOptions.Mode==CanvasEraserMode.Quick;
        if(command is not ("9to1.Canvas.Erase.Natural" or "9to1.Canvas.Erase.Quick" or "9to1.Canvas.Erase.Whole" or "9to1.Canvas.Erase.Partial" or "9to1.Canvas.Erase.Narrower" or "9to1.Canvas.Erase.Wider"))return false;
        if((command is "9to1.Canvas.Erase.Narrower" or "9to1.Canvas.Erase.Wider") && _tools.EraserOptions.Mode!=CanvasEraserMode.Natural)return false;
        return _tools.EraserCapability(ProposedEraserOptions(command)).Available;
    }
    private bool SelectionActionAvailable(string command)
    {
        if (_snapshot is null || _tools?.Selected != CanvasPrimaryTool.Select || _selectionInput?.CanCapture != true) return false;
        return command switch
        {
            "9to1.Canvas.Selection.Previous" or "9to1.Canvas.Selection.Next" => true,
            "9to1.Canvas.Selection.Clear" => _selectionInput.HasTarget,
            "9to1.Canvas.Selection.Left" or "9to1.Canvas.Selection.Right" or "9to1.Canvas.Selection.Up" or "9to1.Canvas.Selection.Down" => _selectionInput.CanMove,
            "9to1.Canvas.Selection.Move" => _selectionInput.CanMove && _selectionInput.HasMovePreview,
            "9to1.Canvas.Selection.Delete" => _selectionInput.CanDelete,
            _ => false
        };
    }
    private string OptionsSummary() => _tools?.Selected switch
    {
        CanvasPrimaryTool.Select => _selectionInput?.TargetSummary ?? "No stroke selected",
        CanvasPrimaryTool.Pen => $"{_tools.InkStyle.Kind}; colour {_tools.InkStyle.Color}; width {_tools.InkStyle.BaseWidth}; opacity {_tools.InkStyle.Opacity}",
        CanvasPrimaryTool.Eraser => $"{_tools.EraserOptions.Mode}; {_tools.EraserOptions.NaturalStyle}; width {_tools.EraserOptions.Width}",
        _ => "",
    };

    private static (CanvasPage[] Pages, AccessibleDescriptor[] Descriptors) BuildAccessibleIndex(CanvasArtifact? artifact)
    {
        if (artifact is null) return ([], []);
        var pagesById = artifact.Pages.GroupBy(page => page.PageId).ToDictionary(group => group.Key, group => group.First());
        var pages = artifact.PageOrder.Where(pagesById.ContainsKey).Select(id => pagesById[id]).ToArray();
        var descriptors = new List<AccessibleDescriptor>();
        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var page = pages[pageIndex];
            var layers = page.Layers.Select((layer, index) => (Layer: layer, Index: index))
                .GroupBy(item => item.Layer.LayerId).ToDictionary(group => group.Key, group => group.First());
            var layerPositions = page.LayerOrder.Select((id, index) => (Id: id, Position: index + 1))
                .GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First().Position);
            var objectsById = page.Objects.Select((value, index) => (Value: value, Index: index))
                .GroupBy(item => item.Value.ObjectId).ToDictionary(group => group.Key, group => group.First().Index);
            var strokesById = page.Strokes.Select((value, index) => (Value: value, Index: index))
                .GroupBy(item => item.Value.StrokeId).ToDictionary(group => group.Key, group => group.First().Index);
            for (var index = 0; index < page.ObjectOrder.Count; index++)
            {
                var id = page.ObjectOrder[index];
                if (!objectsById.TryGetValue(id, out var item)) continue;
                var itemLayer = page.Objects[item].LayerId;
                descriptors.Add(new(pageIndex, false, item, index + 1,
                    layers.TryGetValue(itemLayer, out var layer) ? layer.Index : -1,
                    layerPositions.TryGetValue(itemLayer, out var position) ? position : 0));
            }
            for (var index = 0; index < page.StrokeOrder.Count; index++)
            {
                var id = page.StrokeOrder[index];
                if (!strokesById.TryGetValue(id, out var stroke)) continue;
                var strokeLayer = page.Strokes[stroke].LayerId;
                descriptors.Add(new(pageIndex, true, stroke, index + 1,
                    layers.TryGetValue(strokeLayer, out var layer) ? layer.Index : -1,
                    layerPositions.TryGetValue(strokeLayer, out var position) ? position : 0));
            }
        }
        return (pages, descriptors.ToArray());
    }

    private void MaterializeAccessiblePage()
    {
        _accessibleObjects = _accessibleDescriptors.Skip(_accessiblePage * AccessiblePageSize)
            .Take(AccessiblePageSize).Select(MaterializeAccessibleRow).ToArray();
    }

    private CanvasAccessibleObjectRow MaterializeAccessibleRow(AccessibleDescriptor descriptor)
    {
        var page = _accessiblePages[descriptor.PageIndex];
        var layer = descriptor.LayerIndex >= 0 ? page.Layers[descriptor.LayerIndex].Name : "Unknown layer";
        var layerPosition = descriptor.LayerOrder > 0 ? descriptor.LayerOrder.ToString() : "unlisted";
        if (!descriptor.IsStroke)
        {
            var item = page.Objects[descriptor.EntityIndex];
            var name = Display(item.Accessibility.Name, item.ObjectTypeId);
            var alt = Display(item.Accessibility.AltText, "No alt text authored");
            var description = Display(item.Accessibility.Description, "No description authored");
            return new(item.ObjectId.ToString("D"), $"Page {descriptor.PageIndex + 1}: {name}", item.ObjectTypeId,
                $"Page {descriptor.PageIndex + 1}; object order {descriptor.EntityOrder}; layer {layer} (layer order {layerPosition}); geometry position ({item.Geometry.X:G6}, {item.Geometry.Y:G6}), size {item.Geometry.Width:G6} × {item.Geometry.Height:G6}; transform translation ({item.Transform.TranslateX:G6}, {item.Transform.TranslateY:G6}), scale ({item.Transform.ScaleX:G6}, {item.Transform.ScaleY:G6}), rotation {item.Transform.RotationDegrees:G6} degrees; alt text: {alt}; description: {description}. Shared payload is preserved but not interpreted as authored text.");
        }

        var stroke = page.Strokes[descriptor.EntityIndex];
        var points = stroke.Samples;
        var extent = points.Count == 0 ? "no sample geometry" :
            $"sample-coordinate bounds ({points.Min(point => point.X):G6}, {points.Min(point => point.Y):G6})–({points.Max(point => point.X):G6}, {points.Max(point => point.Y):G6})";
        return new(stroke.StrokeId.ToString("D"), $"Page {descriptor.PageIndex + 1}: Ink stroke {descriptor.EntityOrder}", "Freehand ink",
            $"Page {descriptor.PageIndex + 1}; stroke order {descriptor.EntityOrder}; layer {layer} (layer order {layerPosition}); {points.Count} samples; {extent}; transform translation ({stroke.Transform.TranslateX:G6}, {stroke.Transform.TranslateY:G6}), scale ({stroke.Transform.ScaleX:G6}, {stroke.Transform.ScaleY:G6}), rotation {stroke.Transform.RotationDegrees:G6} degrees. Freehand ink has no automatically inferred authored text or alt text.");
    }

    private static string Display(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var normalized = value.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..497] + "…";
    }

    private void ToolsChanged(object? sender, EventArgs args) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose() { if (_tools is not null) _tools.Changed -= ToolsChanged; if(_eraser is not null)_eraser.Changed-=ToolsChanged; if (_selectionInput is not null) _selectionInput.Changed -= ToolsChanged; }
}
