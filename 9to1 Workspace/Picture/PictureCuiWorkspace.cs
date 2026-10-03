using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Images;

public enum PictureWorkspaceCommandKind { Open, RotateClockwise, FlipHorizontal, Crop, Resize, Save, Export, NextFrame, PlayAnimation, PauseAnimation, ShowInformation, Undo, Redo, SaveCopy, ZoomIn, ZoomOut, FitView, PanLeft, PanRight, PanUp, PanDown, FlipVertical, ShowOriginal, ShowEdited }
public sealed record PictureWorkspaceCommand(PictureWorkspaceCommandKind Kind, Guid? DocumentId, long? BaseRevision, string? FileId,
    Guid? BackingFileId = null)
{
    // Retain the source identifier for existing readers; it is never the
    // destination of an editable document save.
    public string? SourceFileId => FileId;
}

/// <summary>Canonical Picture CUI bindings over the same editable document used for raster replay.</summary>
public sealed class PictureCuiWorkspace(
    Func<PictureWorkspaceCommand, CancellationToken, ValueTask> dispatch,
    Func<PictureWorkspaceCommandKind, bool> isAvailable) : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private PictureDocument? _document;
    private Guid? _backingFileId;
    private string _persistence = "No Picture document is open";
    private string _capability = "";
    private string _viewportSummary = "Fit view";
    private long? _frameDelayMicroseconds;
    private bool _playing;
    private string _information = "";
    private bool _informationVisible;
    private bool _canUndo, _canRedo;
    private string _historyStatus = "History is unavailable for this revision.";
    public event PropertyChangedEventHandler? PropertyChanged;

    public static CuiDocument LoadDocument()
    {
        const string name = "HavenOS.Images.UI.PictureWorkspace.cui";
        using var stream = typeof(PictureCuiWorkspace).Assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Canonical Picture CUI source is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), name);
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }

    public void Refresh(PictureDocument? document, string persistenceStatus, string capabilityStatus, Guid? backingFileId = null, long? frameDelayMicroseconds = null, bool playing = false, string information = "", bool informationVisible = false, bool canUndo = false, bool canRedo = false, string historyStatus = "History is unavailable for this revision.", string viewportSummary = "Fit view")
    {
        _document = document;
        _backingFileId = backingFileId is { } id && id != Guid.Empty ? id : null;
        _persistence = persistenceStatus;
        _capability = capabilityStatus;
        _frameDelayMicroseconds = frameDelayMicroseconds;
        _playing = playing; _information = information; _informationVisible = informationVisible;
        _canUndo = canUndo; _canRedo = canRedo; _historyStatus = historyStatus; _viewportSummary=viewportSummary;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "DisplayName" => _document?.DisplayName ?? "Picture",
            "GeometrySummary" => _document is null ? "" : $"{_document.CanvasWidth} × {_document.CanvasHeight} pixels; revision {_document.Revision}",
            "PersistenceStatus" => _persistence,
            "CapabilityStatus" => _capability,
            "ViewportSummary" => _viewportSummary,
            "CanCompare" => IsActionAvailable("9to1.Picture.View.ShowOriginal") == true,
            "CanView" => IsActionAvailable("9to1.Picture.View.ZoomIn") == true,
            "DocumentId" => _document?.DocumentId,
            "Revision" => _document?.Revision,
            "AnimationSummary" => _frameDelayMicroseconds is > 0 ? $"{(_playing ? "Playing" : "Paused")} · Frame delay: {_frameDelayMicroseconds.Value / 1000d:0.###} ms" : "",
            "InformationSummary" => _information,
            "InformationVisible" => _informationVisible,
            "CanShowInformation" => IsActionAvailable("9to1.Picture.Information") == true,
            "CanOpen" => IsActionAvailable("9to1.Picture.Open") == true,
            "CanRotate" => IsActionAvailable("9to1.Picture.Rotate") == true,
            "CanFlipVertical" => IsActionAvailable("9to1.Picture.FlipVertical") == true,
            "CanFlip" => IsActionAvailable("9to1.Picture.FlipHorizontal") == true,
            "CanCrop" => IsActionAvailable("9to1.Picture.Crop") == true,
            "CanResize" => IsActionAvailable("9to1.Picture.Resize") == true,
            "CanSave" => IsActionAvailable("9to1.Picture.Save") == true,
            "CanSaveCopy" => IsActionAvailable("9to1.Picture.SaveCopy") == true,
            "CanExport" => IsActionAvailable("9to1.Picture.Export") == true,
            "CanUndo" => IsActionAvailable("9to1.Picture.History.Undo") == true,
            "CanRedo" => IsActionAvailable("9to1.Picture.History.Redo") == true,
            "HistoryStatus" => _historyStatus,
            "CanPlay" => IsActionAvailable("9to1.Picture.Animation.Play") == true,
            "CanPause" => IsActionAvailable("9to1.Picture.Animation.Pause") == true,
            "CanAdvanceFrames" => IsActionAvailable("9to1.Picture.Animation.NextFrame") == true,
            _ => null
        };
        return path is "DisplayName" or "GeometrySummary" or "PersistenceStatus" or "CapabilityStatus" or "DocumentId" or "Revision" or "AnimationSummary"
            or "CanUndo" or "CanRedo" or "HistoryStatus" or "CanOpen" or "CanRotate" or "CanFlip" or "CanCrop" or "CanResize" or "CanSave" or "CanSaveCopy" or "CanExport" or "CanAdvanceFrames" or "CanPlay" or "CanPause" or "InformationSummary" or "InformationVisible" or "CanShowInformation";
    }

    public void RefreshAvailability() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public bool? IsActionAvailable(string command) => TryCommand(command, out var kind) && isAvailable(kind)
        && (_document is not null || kind == PictureWorkspaceCommandKind.Open)
        && (kind is not (PictureWorkspaceCommandKind.Save or PictureWorkspaceCommandKind.SaveCopy or PictureWorkspaceCommandKind.Export or PictureWorkspaceCommandKind.Undo or PictureWorkspaceCommandKind.Redo) || _backingFileId is not null)
        && (kind != PictureWorkspaceCommandKind.Undo || _canUndo) && (kind != PictureWorkspaceCommandKind.Redo || _canRedo);

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parameter is not null) throw new ArgumentException("Picture commands do not accept raw filesystem or caller identity arguments.", nameof(parameter));
        if (!TryCommand(command, out var kind) || IsActionAvailable(command) != true) throw new NotSupportedException("The Picture action is unavailable in this host state.");
        return dispatch(new(kind, _document?.DocumentId, _document?.Revision, _document?.FileId, _backingFileId), cancellationToken);
    }

    private static bool TryCommand(string command, out PictureWorkspaceCommandKind kind)
    {
        kind = command switch
        {
            "9to1.Picture.View.ShowOriginal" => PictureWorkspaceCommandKind.ShowOriginal,
            "9to1.Picture.View.ShowEdited" => PictureWorkspaceCommandKind.ShowEdited,
            "9to1.Picture.View.ZoomIn" => PictureWorkspaceCommandKind.ZoomIn,
            "9to1.Picture.View.ZoomOut" => PictureWorkspaceCommandKind.ZoomOut,
            "9to1.Picture.View.FitView" => PictureWorkspaceCommandKind.FitView,
            "9to1.Picture.View.PanLeft" => PictureWorkspaceCommandKind.PanLeft,
            "9to1.Picture.View.PanRight" => PictureWorkspaceCommandKind.PanRight,
            "9to1.Picture.View.PanUp" => PictureWorkspaceCommandKind.PanUp,
            "9to1.Picture.View.PanDown" => PictureWorkspaceCommandKind.PanDown,
            "9to1.Picture.History.Undo" => PictureWorkspaceCommandKind.Undo,
            "9to1.Picture.History.Redo" => PictureWorkspaceCommandKind.Redo,
            "9to1.Picture.Information" => PictureWorkspaceCommandKind.ShowInformation,
            "9to1.Picture.Open" => PictureWorkspaceCommandKind.Open,
            "9to1.Picture.Rotate" => PictureWorkspaceCommandKind.RotateClockwise,
            "9to1.Picture.FlipVertical" => PictureWorkspaceCommandKind.FlipVertical,
            "9to1.Picture.FlipHorizontal" => PictureWorkspaceCommandKind.FlipHorizontal,
            "9to1.Picture.Crop" => PictureWorkspaceCommandKind.Crop,
            "9to1.Picture.Resize" => PictureWorkspaceCommandKind.Resize,
            "9to1.Picture.Save" => PictureWorkspaceCommandKind.Save,
            "9to1.Picture.SaveCopy" => PictureWorkspaceCommandKind.SaveCopy,
            "9to1.Picture.Export" => PictureWorkspaceCommandKind.Export,
            "9to1.Picture.Animation.Play" => PictureWorkspaceCommandKind.PlayAnimation,
            "9to1.Picture.Animation.Pause" => PictureWorkspaceCommandKind.PauseAnimation,
            "9to1.Picture.Animation.NextFrame" => PictureWorkspaceCommandKind.NextFrame,
            _ => (PictureWorkspaceCommandKind)(-1)
        };
        return Enum.IsDefined(kind);
    }
}
