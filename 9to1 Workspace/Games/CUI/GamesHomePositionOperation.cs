using System.Text.Json;
using Haven.Application;
using Haven.Application.Games;
using Haven.Core.Games;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Games;

/// <summary>Exact canonical target and position shown in the Home request; no supplied actor or path.</summary>
public sealed class GamesPositionWriteIntent
{
    public const string TargetAppId = "games";
    public const string ActionId = "games.file.save";
    private readonly JsonElement _arguments;
    private GamesPositionWriteIntent(Guid fileID, Guid structuralRevisionID, Guid projectID, long projectRevision,
        Guid sceneID, long sceneRevision, Guid nodeID, GamesVector3 position)
    {
        if (fileID == Guid.Empty || structuralRevisionID == Guid.Empty || projectID == Guid.Empty || sceneID == Guid.Empty
            || nodeID == Guid.Empty || projectRevision < 1 || sceneRevision < 1 || position is null || !position.IsValid)
            throw new ArgumentException("A canonical Games target, revision and finite position are required.");
        FileID = fileID; StructuralRevisionID = structuralRevisionID; ProjectID = projectID; ProjectRevision = projectRevision;
        SceneID = sceneID; SceneRevision = sceneRevision; NodeID = nodeID; Position = position;
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", fileID.ToString("N"), structuralRevisionID.ToString("N"), ResourceAccess.Write) });
        _arguments = JsonSerializer.SerializeToElement(new { operation = "scene.node.position", fileID, structuralRevisionID,
            projectID, projectRevision, sceneID, sceneRevision, nodeID, position });
    }
    public Guid FileID { get; }
    public Guid StructuralRevisionID { get; }
    public Guid ProjectID { get; }
    public long ProjectRevision { get; }
    public Guid SceneID { get; }
    public long SceneRevision { get; }
    public Guid NodeID { get; }
    public GamesVector3 Position { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    public static GamesPositionWriteIntent Capture(Guid fileID, Guid structuralRevisionID, Guid projectID,
        long projectRevision, Guid sceneID, long sceneRevision, Guid nodeID, GamesVector3 position) =>
        new(fileID, structuralRevisionID, projectID, projectRevision, sceneID, sceneRevision, nodeID, position);
}

/// <summary>Consumes one exact Home capability and keeps its claimed actor bound through the Files commit.</summary>
public sealed class GamesHomePositionOperation(IActorBoundGamesProjectStore projects, HomeResourceOperationBroker home)
{
    public async Task<GamesStoredProject> ExecuteAsync(GamesPositionWriteIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var actor = await home.ClaimExecutionAsync(capability, GamesPositionWriteIntent.TargetAppId,
            GamesPositionWriteIntent.ActionId, intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home did not authorize this exact Games position edit.");
        var opened = await projects.OpenAsync(intent.FileID, cancellationToken).ConfigureAwait(false);
        if (opened.FileID != intent.FileID || opened.StructuralRevisionID != intent.StructuralRevisionID
            || opened.Project.ProjectID != intent.ProjectID || opened.Project.Revision != intent.ProjectRevision)
            throw new InvalidOperationException("GamesProjectRevisionConflict");
        var scene = opened.Project.Scenes.SingleOrDefault(scene => scene.SceneID == intent.SceneID)
            ?? throw new KeyNotFoundException("GamesSceneNotFound");
        var edited = GamesSceneEdits.SetPosition(scene, intent.SceneRevision, intent.NodeID, intent.Position);
        var project = GamesProjectEdits.SetScene(opened.Project, intent.ProjectRevision, intent.SceneRevision, edited);
        return await projects.SaveForActorAsync(actor, intent.FileID, intent.StructuralRevisionID,
            intent.ProjectRevision, project, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class GamesNativeActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == GamesPositionWriteIntent.TargetAppId && actionId == GamesPositionWriteIntent.ActionId
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null;
}
