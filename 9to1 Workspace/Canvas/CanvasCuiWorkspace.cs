using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Haven.Application;

namespace HavenOS.Apps.Canvas;

public enum CanvasWorkspaceCommandKind { Open, Import, Flush, Undo, Redo }
public sealed record CanvasWorkspaceCommand(CanvasWorkspaceCommandKind Kind, Guid? ArtifactId, Guid? BaseRevisionId);

/// <summary>App-owned semantic bindings; the native host supplies Home-brokered typed dispatch.</summary>
public sealed class CanvasCuiWorkspace(
    Func<CanvasWorkspaceCommand, CancellationToken, ValueTask> dispatch,
    Func<CanvasWorkspaceCommandKind, bool> isAvailable) : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private CanvasArtifact? _snapshot;
    private string _persistence = "No Canvas artifact is open";
    private string _compatibility = "";
    private string _selection = "No content selected";
    public event PropertyChangedEventHandler? PropertyChanged;

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
        value = path switch
        {
            "DisplayName" => _snapshot?.DisplayName ?? "Canvas",
            "PersistenceStatus" => _persistence,
            "CompatibilityStatus" => _compatibility,
            "SelectionSummary" => _selection,
            "ArtifactId" => _snapshot?.ArtifactId,
            "RevisionId" => _snapshot?.RevisionId,
            _ => null
        };
        return path is "DisplayName" or "PersistenceStatus" or "CompatibilityStatus" or "SelectionSummary" or "ArtifactId" or "RevisionId";
    }

    public bool? IsActionAvailable(string command) => TryCommand(command, out var kind) && isAvailable(kind)
        && (_snapshot is not null || kind is CanvasWorkspaceCommandKind.Open or CanvasWorkspaceCommandKind.Import);

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null) throw new ArgumentException("Canvas document commands do not accept raw paths or caller identity arguments.", nameof(parameter));
        if (!TryCommand(command, out var kind) || IsActionAvailable(command) != true) throw new NotSupportedException("The Canvas action is unavailable in this host state.");
        // Capture the target at invocation. Later focus/selection changes cannot
        // silently retarget the operation while Home is authorizing it.
        return dispatch(new(kind, _snapshot?.ArtifactId, _snapshot?.RevisionId), cancellationToken);
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
            _ => (CanvasWorkspaceCommandKind)(-1)
        };
        return Enum.IsDefined(kind);
    }
}
