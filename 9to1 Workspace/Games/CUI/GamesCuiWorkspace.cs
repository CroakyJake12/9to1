using System.ComponentModel;
using System.Globalization;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application.Games;
using Haven.Core.Games;

namespace HavenOS.Games;

/// <summary>Owning Games CUI bindings call the same canonical editor operations as typed APIs.
/// The host supplies its current Files selection and action availability; neither grants permission.</summary>
public sealed class GamesCuiWorkspace(GamesProjectEditorService editor, GamesSceneSessionService scenes,
    Func<Guid?> currentFilesSelection, Func<string, bool> available) : ICuiWritableBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private GamesStoredProject? _opened;
    private int _selected;
    private string _x = "0", _y = "0", _z = "0";
    private string _status = "Open a Games project through Files";
    private bool _busy;
    public event PropertyChangedEventHandler? PropertyChanged;
    private GamesSceneSnapshot? Scene => _opened?.Project.Scenes.Single(scene => scene.SceneID == _opened.Project.ActiveSceneID);
    private GamesSceneNode? Selected => Scene is { Nodes.Count: > 0 } scene ? scene.Nodes[Math.Min(_selected, scene.Nodes.Count - 1)] : null;

    public static CuiDocument LoadDocument()
    {
        const string name = "HavenOS.Games.UI.GamesWorkspace.cui";
        using var stream = typeof(GamesCuiWorkspace).Assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Games CUI source is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), name);
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Project" => _opened?.Project.ProjectID.ToString("D") ?? "No project",
            "Workspace" => _opened?.Project.Workspace.ToString() ?? "Games",
            "Scene" => Scene?.SceneID.ToString("D") ?? "No scene",
            "Node" => Selected?.Name ?? "No node selected",
            "NodeIdentity" => Selected?.NodeID.ToString("D") ?? "",
            "PositionX" => _x, "PositionY" => _y, "PositionZ" => _z, "Status" => _status,
            "CanOpen" => IsActionAvailable("9to1.Games.Open"), "CanEdit" => IsActionAvailable("9to1.Games.SetPosition"),
            "CanObserve" => IsActionAvailable("9to1.Games.Observe"), "CanSelect" => !_busy && Selected is not null,
            "CanWorkspace" => IsActionAvailable("9to1.Games.Development"), _ => null
        };
        return path is "Project" or "Workspace" or "Scene" or "Node" or "NodeIdentity" or "PositionX" or "PositionY" or "PositionZ"
            or "Status" or "CanOpen" or "CanEdit" or "CanObserve" or "CanSelect" or "CanWorkspace";
    }
    public bool TrySetValue(string path, object? value)
    {
        if (IsActionAvailable("9to1.Games.SetPosition") != true || value is not string text || text.Length > 64 || path is not ("PositionX" or "PositionY" or "PositionZ")) return false;
        if (path == "PositionX") _x = text; else if (path == "PositionY") _y = text; else _z = text;
        Changed(); return true;
    }
    public bool? IsActionAvailable(string command) => !_busy && available(command) && (command switch
    {
        "9to1.Games.Open" => currentFilesSelection() is not null,
        "9to1.Games.SetPosition" or "9to1.Games.NextNode" => Selected is not null,
        "9to1.Games.Development" or "9to1.Games.CreationRendering" or "9to1.Games.Observe" => _opened is not null,
        _ => false
    });
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null || IsActionAvailable(command) != true) throw new InvalidOperationException("Games action is unavailable.");
        var target = _opened;
        var node = Selected;
        var scene = Scene;
        var selectedFile = currentFilesSelection();
        var x = _x; var y = _y; var z = _z;
        _busy = true; Changed();
        try
        {
            switch (command)
            {
                case "9to1.Games.Open": _opened = await editor.OpenAsync(selectedFile!.Value, cancellationToken); _selected = 0; break;
                case "9to1.Games.NextNode": _selected = (_selected + 1) % scene!.Nodes.Count; break;
                case "9to1.Games.Development":
                case "9to1.Games.CreationRendering":
                    _opened = await editor.ChangeWorkspaceAsync(target!.FileID, target.StructuralRevisionID, target.Project.Revision,
                        command.EndsWith(".Development", StringComparison.Ordinal) ? GamesWorkspaceMode.Development : GamesWorkspaceMode.CreationRendering, cancellationToken); break;
                case "9to1.Games.SetPosition":
                    var position = new GamesVector3(Number(x), Number(y), Number(z));
                    var edited = GamesSceneEdits.SetPosition(scene!, scene!.Revision, node!.NodeID, position);
                    _opened = await editor.SetSceneAsync(target!.FileID, target.StructuralRevisionID, target.Project.Revision, scene.Revision, edited, cancellationToken); break;
                case "9to1.Games.Observe":
                    var observation = await scenes.ObserveAsync(target!.Project.ProjectID, scene!.SceneID, scene.Revision, cancellationToken);
                    _status = $"Godot {observation.ObservedEngineVersion}: {observation.Nodes.Count} native nodes observed";
                    return;
            }
            if (Selected is { } selected)
            { _x = selected.Spatial.Position.X.ToString(CultureInfo.InvariantCulture); _y = selected.Spatial.Position.Y.ToString(CultureInfo.InvariantCulture); _z = selected.Spatial.Position.Z.ToString(CultureInfo.InvariantCulture); }
            _status = _opened is null ? "No project" : $"Saved project revision {_opened.Project.Revision}; scene revision {Scene!.Revision}";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        { _status = error.Message; throw; }
        finally { _busy = false; Changed(); }
    }
    private static float Number(string text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
        ? value : throw new ArgumentException("Enter a finite position coordinate.");
    public void RefreshAvailability() => Changed();
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
}
