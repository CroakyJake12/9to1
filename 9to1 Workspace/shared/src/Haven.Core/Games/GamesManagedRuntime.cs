namespace Haven.Core.Games;

public sealed record GamesManagedProjectObservation(Guid ProjectID, long ProjectRevision, Guid SceneID, long SceneRevision,
    string ObservedEngineVersion, string ManagedRuntimeVersion, string ExecutableSha256, string ModuleSha256,
    IReadOnlyList<GamesNativeNodeObservation> Nodes);

/// <summary>Fixed first-party managed scene driver, not a grant to load arbitrary project scripts.</summary>
public interface IGamesManagedProjectRuntime
{
    Task<GamesManagedProjectObservation> ObserveAsync(GamesProjectDocument project, Guid sceneID, CancellationToken cancellationToken = default);
}
