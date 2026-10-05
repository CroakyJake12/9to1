using Haven.Core.Games;

namespace Haven.Application.Games;

/// <summary>Files identities and revision tokens remain opaque to the owning editor; no mutable path is identity.</summary>
public sealed record GamesStoredProject(Guid FileID, Guid StructuralRevisionID, Guid ContentRevisionID, GamesProjectDocument Project);
public interface ICanonicalGamesProjectStore
{
    Task<GamesStoredProject> OpenAsync(Guid fileID, CancellationToken cancellationToken = default);
    Task<GamesStoredProject> SaveAsync(Guid fileID, Guid expectedStructuralRevisionID, long expectedProjectRevision,
        GamesProjectDocument project, CancellationToken cancellationToken = default);
}

/// <summary>Owning Home operation carries the already claimed actor to the final canonical write.
/// This actor is an expected identity, not an independent permission grant.</summary>
public interface IActorBoundGamesProjectStore : ICanonicalGamesProjectStore
{
    Task<GamesStoredProject> SaveForActorAsync(AuthenticatedResourceActor expectedActor, Guid fileID,
        Guid expectedStructuralRevisionID, long expectedProjectRevision, GamesProjectDocument project,
        CancellationToken cancellationToken = default);
}

/// <summary>Editor and typed API operations use the same captured canonical project and Files compare/exchange.</summary>
public sealed class GamesProjectEditorService(ICanonicalGamesProjectStore store)
{
    public Task<GamesStoredProject> OpenAsync(Guid fileID, CancellationToken token = default) => store.OpenAsync(fileID, token);
    public async Task<GamesStoredProject> ChangeWorkspaceAsync(Guid fileID, Guid structuralRevision, long projectRevision,
        GamesWorkspaceMode workspace, CancellationToken token = default)
    {
        var opened = await store.OpenAsync(fileID, token).ConfigureAwait(false);
        if (opened.StructuralRevisionID != structuralRevision || opened.Project.Revision != projectRevision)
            throw new InvalidOperationException("GamesProjectRevisionConflict");
        var changed = GamesProjectEdits.ChangeWorkspace(opened.Project, projectRevision, workspace);
        return changed.Revision == opened.Project.Revision ? opened
            : await store.SaveAsync(fileID, structuralRevision, projectRevision, changed, token).ConfigureAwait(false);
    }
    public async Task<GamesStoredProject> SetSceneAsync(Guid fileID, Guid structuralRevision, long projectRevision,
        long sceneRevision, GamesSceneSnapshot scene, CancellationToken token = default)
    {
        var captured = scene.Capture();
        var opened = await store.OpenAsync(fileID, token).ConfigureAwait(false);
        if (opened.StructuralRevisionID != structuralRevision) throw new InvalidOperationException("GamesProjectRevisionConflict");
        var changed = GamesProjectEdits.SetScene(opened.Project, projectRevision, sceneRevision, captured);
        return await store.SaveAsync(fileID, structuralRevision, projectRevision, changed, token).ConfigureAwait(false);
    }
}
