using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Haven.Application;

namespace HavenOS.Apps.Canvas;

public enum CanvasWorkspaceCommandKind { Open, Import, Flush, Undo, Redo, FitView, ZoomIn, ZoomOut }
public sealed record CanvasWorkspaceCommand(CanvasWorkspaceCommandKind Kind, Guid? ArtifactId, Guid? BaseRevisionId);

/// <summary>App-owned semantic bindings; the native host supplies Home-brokered typed dispatch.</summary>
public sealed class CanvasCuiWorkspace : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly Func<CanvasWorkspaceCommand, CancellationToken, ValueTask> _dispatch;
    private readonly Func<CanvasWorkspaceCommandKind, bool> _isAvailable;
    private readonly CanvasToolState? _tools;
    private readonly ICanvasEraserInput? _eraser;
    private CanvasArtifact? _snapshot;
    private string _persistence = "No Canvas artifact is open";
    private string _compatibility = "";
    private string _selection = "No content selected";
    public event PropertyChangedEventHandler? PropertyChanged;

    public CanvasCuiWorkspace(Func<CanvasWorkspaceCommand, CancellationToken, ValueTask> dispatch,
        Func<CanvasWorkspaceCommandKind, bool> isAvailable, CanvasToolState? tools = null,ICanvasEraserInput? eraser = null)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _isAvailable = isAvailable ?? throw new ArgumentNullException(nameof(isAvailable));
        _tools = tools;_eraser=eraser;
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
        IReadOnlyList<Guid>? selectedIds = null)
    {
        _snapshot = snapshot is null ? null : CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(snapshot));
        _persistence = persistenceStatus;
        _compatibility = report is null ? "" : string.Join(" ", report.Issues.Select(issue => $"{issue.Feature}: {issue.Disposition}. {issue.Reason}"));
        _selection = selectedIds is { Count: > 0 } ? $"{selectedIds.Count} selected: {string.Join(", ", selectedIds)}" : "No content selected";
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
            "SelectionSummary" => _selection,
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
            or "CanNaturalErase" or "CanQuickErase" or "CanWholeErase" or "CanPartialErase" or "CanEditEraser" or "CanChooseEraseTarget" or "CanEraseChosenTarget" or "EraseTargetSummary" or "EraseInputStatus" or "EraserOptionsVisible" or "PenOptionsVisible"
            or "SelectedToolName" or "ToolOptionsVisible" or "ToolOptionsSummary" or "ToolCapabilityStatus" or "AiSurfaceVisible" or "AiIndicatorVisible" or "AiIndicatorLabel";
    }

    /// <summary>The owning host calls this when its live Home/read-only/capability state changes.</summary>
    public void RefreshAvailability() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public bool? IsActionAvailable(string command)
    {
        if(command.StartsWith("9to1.Canvas.Erase.",StringComparison.Ordinal))return EraserOptionAvailable(command);
        if (IsInkOption(command)) return _snapshot is not null && _tools?.Selected == CanvasPrimaryTool.Pen && _tools.Capability(CanvasPrimaryTool.Pen).Available;
        if (TryTool(command, out var tool)) return _snapshot is not null && _tools?.Capability(tool).Available == true;
        return TryCommand(command, out var kind) && _isAvailable(kind)
            && (_snapshot is not null || kind is CanvasWorkspaceCommandKind.Open or CanvasWorkspaceCommandKind.Import);
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null) throw new ArgumentException("Canvas document commands do not accept raw paths or caller identity arguments.", nameof(parameter));
        cancellationToken.ThrowIfCancellationRequested();
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
        return _dispatch(new(kind, _snapshot?.ArtifactId, _snapshot?.RevisionId), cancellationToken);
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
    private string OptionsSummary() => _tools?.Selected switch
    {
        CanvasPrimaryTool.Pen => $"{_tools.InkStyle.Kind}; colour {_tools.InkStyle.Color}; width {_tools.InkStyle.BaseWidth}; opacity {_tools.InkStyle.Opacity}",
        CanvasPrimaryTool.Eraser => $"{_tools.EraserOptions.Mode}; {_tools.EraserOptions.NaturalStyle}; width {_tools.EraserOptions.Width}",
        _ => "",
    };
    private void ToolsChanged(object? sender, EventArgs args) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose() { if (_tools is not null) _tools.Changed -= ToolsChanged; if(_eraser is not null)_eraser.Changed-=ToolsChanged; }
}
