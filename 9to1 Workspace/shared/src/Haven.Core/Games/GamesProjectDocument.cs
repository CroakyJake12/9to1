using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haven.Core.Games;

public enum GamesWorkspaceMode { Development, CreationRendering }

/// <summary>One canonical project and scene/resource identities serve both editor workspaces.
/// Files owns artifact bytes, revisions, grants and durable publication.</summary>
public sealed record GamesProjectDocument(int SchemaVersion, Guid ProjectID, long Revision,
    GamesWorkspaceMode Workspace, Guid ActiveSceneID, IReadOnlyList<GamesSceneSnapshot> Scenes,
    GamesWidgetDocument? Widgets = null)
{
    public const int CurrentSchemaVersion = 1;
    public GamesProjectDocument Capture()
    {
        if (Scenes is null) throw new InvalidDataException("Games project scenes are missing.");
        var snapshot = this with { Scenes = Scenes.Select(scene => scene?.Capture()
            ?? throw new InvalidDataException("Games project contains a missing scene.")).ToArray() };
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Games project requires a supported format or an explicit migration.");
        if (snapshot.ProjectID == Guid.Empty || snapshot.Revision < 1 || !Enum.IsDefined(snapshot.Workspace)
            || snapshot.Scenes.Count is < 1 or > 64 || snapshot.Scenes.Any(scene => scene.ProjectID != snapshot.ProjectID)
            || snapshot.Scenes.Select(scene => scene.SceneID).Distinct().Count() != snapshot.Scenes.Count
            || !snapshot.Scenes.Any(scene => scene.SceneID == snapshot.ActiveSceneID))
            throw new InvalidDataException("Games project identity, active scene or workspace is invalid.");
        if (snapshot.Scenes.Sum(scene => (long)scene.Nodes.Count) > 4096
            || snapshot.Scenes.Sum(scene => scene.Meshes.Sum(mesh => (long)(mesh.Geometry?.Vertices.Count ?? 8))) > 65536
            || snapshot.Scenes.Sum(scene => scene.Meshes.Sum(mesh => (long)(mesh.Geometry?.TriangleIndices.Count ?? 36))) > 393216)
            throw new InvalidDataException("Games project exceeds configured structure limits.");
        return snapshot;
    }
}

public static class GamesProjectEdits
{
    public static GamesProjectDocument ChangeWorkspace(GamesProjectDocument project, long expectedRevision, GamesWorkspaceMode mode)
    {
        var snapshot = project.Capture();
        if (snapshot.Revision != expectedRevision) throw new InvalidOperationException("GamesProjectRevisionConflict");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return snapshot.Workspace == mode ? snapshot : snapshot with { Workspace = mode, Revision = checked(snapshot.Revision + 1) };
    }
    public static GamesProjectDocument SetScene(GamesProjectDocument project, long expectedProjectRevision,
        long expectedSceneRevision, GamesSceneSnapshot editedScene)
    {
        var snapshot = project.Capture();
        var scene = editedScene.Capture();
        if (snapshot.Revision != expectedProjectRevision) throw new InvalidOperationException("GamesProjectRevisionConflict");
        var current = snapshot.Scenes.SingleOrDefault(candidate => candidate.SceneID == scene.SceneID)
            ?? throw new KeyNotFoundException("GamesSceneNotFound");
        if (scene.ProjectID != snapshot.ProjectID || current.Revision != expectedSceneRevision || scene.Revision != checked(expectedSceneRevision + 1))
            throw new InvalidOperationException("GamesSceneRevisionConflict");
        return (snapshot with { Revision = checked(snapshot.Revision + 1),
            Scenes = snapshot.Scenes.Select(candidate => candidate.SceneID == scene.SceneID ? scene : candidate).ToArray() }).Capture();
    }

    public static GamesProjectDocument SetWidgets(GamesProjectDocument project, long expectedProjectRevision,
        int? expectedWidgetRevision, GamesWidgetDocument editedWidgets)
    {
        var snapshot = project.Capture();
        if (snapshot.Revision != expectedProjectRevision) throw new InvalidOperationException("GamesProjectRevisionConflict");
        ArgumentNullException.ThrowIfNull(editedWidgets);
        // SetWidgets updates an existing slot only; absent widgets must be created via AddWidgets.
        var current = snapshot.Widgets ?? throw new InvalidOperationException("GamesWidgetsAbsent");
        if (current.Revision != expectedWidgetRevision)
            throw new InvalidOperationException("GamesWidgetRevisionConflict");
        // The widget slot is bound to one screen identity; an unrelated screen cannot be committed here.
        if (!string.Equals(current.ScreenId, editedWidgets.ScreenId, StringComparison.Ordinal))
            throw new InvalidOperationException("GamesWidgetScreenMismatch");
        // An edit must advance the widget revision by exactly one (stale or skipped edits are refused).
        if (editedWidgets.Revision != checked(current.Revision + 1))
            throw new InvalidOperationException("GamesWidgetRevisionConflict");
        return snapshot with { Widgets = editedWidgets, Revision = checked(snapshot.Revision + 1) };
    }

    public static GamesProjectDocument AddWidgets(GamesProjectDocument project, long expectedProjectRevision,
        GamesWidgetDocument initialWidgets)
    {
        var snapshot = project.Capture();
        if (snapshot.Revision != expectedProjectRevision) throw new InvalidOperationException("GamesProjectRevisionConflict");
        ArgumentNullException.ThrowIfNull(initialWidgets);
        if (snapshot.Widgets is not null) throw new InvalidOperationException("GamesWidgetsAlreadyPresent");
        return snapshot with { Widgets = initialWidgets, Revision = checked(snapshot.Revision + 1) };
    }
}

public static class GamesProjectCodec
{
    public const int MaximumDocumentBytes = 16777216;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public static byte[] Encode(GamesProjectDocument project)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project.Capture(), Options);
        if (bytes.Length > MaximumDocumentBytes) throw new InvalidDataException("Games project exceeds the document byte limit.");
        return bytes;
    }
    public static GamesProjectDocument Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaximumDocumentBytes) throw new InvalidDataException("Games project bytes are missing or exceed the document limit.");
        try
        {
            var decoded = JsonSerializer.Deserialize<GamesProjectDocument>(bytes, Options)
                ?? throw new InvalidDataException("Games project content is empty.");
            // Schema 1 documents predate the optional Widgets member; treat a missing payload as null so prior files reopen unchanged.
            return (decoded with { Widgets = decoded.Widgets }).Capture();
        }
        catch (JsonException error) { throw new InvalidDataException("Games project format is malformed or contains unsupported fields.", error); }
    }
}
