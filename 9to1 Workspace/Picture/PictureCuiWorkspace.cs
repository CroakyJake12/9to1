using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Images;

public enum PictureWorkspaceCommandKind { Open, RotateClockwise, FlipHorizontal, Crop, Resize, Save, Export }
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

    public void Refresh(PictureDocument? document, string persistenceStatus, string capabilityStatus, Guid? backingFileId = null)
    {
        _document = document;
        _backingFileId = backingFileId is { } id && id != Guid.Empty ? id : null;
        _persistence = persistenceStatus;
        _capability = capabilityStatus;
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
            "DocumentId" => _document?.DocumentId,
            "Revision" => _document?.Revision,
            _ => null
        };
        return path is "DisplayName" or "GeometrySummary" or "PersistenceStatus" or "CapabilityStatus" or "DocumentId" or "Revision";
    }

    public bool? IsActionAvailable(string command) => TryCommand(command, out var kind) && isAvailable(kind)
        && (_document is not null || kind == PictureWorkspaceCommandKind.Open)
        && (kind is not (PictureWorkspaceCommandKind.Save or PictureWorkspaceCommandKind.Export) || _backingFileId is not null);

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null) throw new ArgumentException("Picture commands do not accept raw filesystem or caller identity arguments.", nameof(parameter));
        if (!TryCommand(command, out var kind) || IsActionAvailable(command) != true) throw new NotSupportedException("The Picture action is unavailable in this host state.");
        return dispatch(new(kind, _document?.DocumentId, _document?.Revision, _document?.FileId, _backingFileId), cancellationToken);
    }

    private static bool TryCommand(string command, out PictureWorkspaceCommandKind kind)
    {
        kind = command switch
        {
            "9to1.Picture.Open" => PictureWorkspaceCommandKind.Open,
            "9to1.Picture.Rotate" => PictureWorkspaceCommandKind.RotateClockwise,
            "9to1.Picture.FlipHorizontal" => PictureWorkspaceCommandKind.FlipHorizontal,
            "9to1.Picture.Crop" => PictureWorkspaceCommandKind.Crop,
            "9to1.Picture.Resize" => PictureWorkspaceCommandKind.Resize,
            "9to1.Picture.Save" => PictureWorkspaceCommandKind.Save,
            "9to1.Picture.Export" => PictureWorkspaceCommandKind.Export,
            _ => (PictureWorkspaceCommandKind)(-1)
        };
        return Enum.IsDefined(kind);
    }
}
