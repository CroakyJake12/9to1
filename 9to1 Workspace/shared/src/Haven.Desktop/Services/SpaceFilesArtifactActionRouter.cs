using System.Globalization;
using Haven.Application;
using HavenOS.Files;

namespace Haven.Desktop.Services;

public sealed record SpaceFilesArtifactAction(Guid SpaceId, long ExpectedSpaceRevision, Guid ContextId,
    Guid FileId, string ArtifactId, FilesRevisionId ExpectedFilesRevision, bool Writing);
public sealed record SpaceFilesArtifactTarget(AuthenticatedResourceActor Actor, SpaceContextReference Source,
    FilesArtifactReference Artifact, HostedItemMetadata Metadata, IReadOnlyList<ResourceScope> Scopes, string ActionId);

/// <summary>Resolves a captured Space action through both canonical stores. Returned targets are not grants;
/// owning-app dispatch must still use Home's broker and revalidate these scopes at execution.</summary>
public sealed class SpaceFilesArtifactActionRouter(SpaceRegistry spaces, NativeFilesWorkspaceAuthority files,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources)
{
    public async Task<SpaceFilesArtifactTarget> ResolveAsync(SpaceFilesArtifactAction action, CancellationToken cancellationToken = default)
    {
        if (action.SpaceId == Guid.Empty || action.ContextId == Guid.Empty || action.FileId == Guid.Empty ||
            action.ExpectedSpaceRevision < 1 || string.IsNullOrWhiteSpace(action.ArtifactId))
            throw new ArgumentException("A captured canonical Space source and Files artifact identity are required.", nameof(action));
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ?? throw new UnauthorizedAccessException();
        var workspace = await files.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (workspace?.Actor != actor) throw new UnauthorizedAccessException("Current Home ownership does not permit this Files workspace.");
        var space = await spaces.GetAsync(action.SpaceId, cancellationToken).ConfigureAwait(false);
        if (space is null || space.IsArchived) throw new InvalidOperationException("The Space is unavailable.");
        if (space.Revision != action.ExpectedSpaceRevision)
            throw new SpaceRevisionConflictException(space.Id, action.ExpectedSpaceRevision, space.Revision);
        var sources = space.ContextReferences?.Where(source => source.ContextId == action.ContextId).ToArray() ?? [];
        if (sources.Length != 1) throw new UnauthorizedAccessException("The selected Space source is unavailable.");
        var source = sources[0];
        var expectedOwner = source.Kind switch
        {
            SpaceContextReferenceKind.WriteArtifact => (App: "write", Type: nameof(FilesArtifactType.WriteDocument)),
            SpaceContextReferenceKind.CanvasArtifact => (App: "canvas", Type: nameof(FilesArtifactType.Canvas)),
            SpaceContextReferenceKind.PictureArtifact => (App: "picture", Type: nameof(FilesArtifactType.Picture)),
            SpaceContextReferenceKind.GamesProject => (App: "games", Type: nameof(FilesArtifactType.GameProject)),
            _ => throw new NotSupportedException("The owning native app has no registered Space artifact route.")
        };
        if (source.HostedFileId != action.FileId || source.OwnerAppId != expectedOwner.App ||
            !SameEntity(source.CanonicalEntityId, action.ArtifactId) ||
            source.Permission is not (SpaceContextPermission.Read or SpaceContextPermission.ReadWrite) ||
            (action.Writing && source.Permission != SpaceContextPermission.ReadWrite))
            throw new UnauthorizedAccessException("The captured action does not have current Space source access.");
        var fileId = new HostedItemId(action.FileId);
        var metadata = await workspace.Provider.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        var reference = await workspace.Provider.GetArtifactAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (!metadata.IsSuccess || !reference.IsSuccess || metadata.Value!.Kind != HostedItemKind.Artifact ||
            reference.Value!.OwnerAppId != expectedOwner.App || reference.Value.ArtifactType != expectedOwner.Type ||
            !SameEntity(reference.Value.ArtifactId, source.CanonicalEntityId))
            throw new UnauthorizedAccessException("The canonical Files artifact differs from the selected Space source.");
        if (metadata.Value.CurrentRevisionId != action.ExpectedFilesRevision)
            throw new InvalidOperationException("The Files artifact changed. Reopen its current revision before this action.");
        var content = await workspace.Provider.GetCurrentArtifactContentAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (!content.IsSuccess || (source.RevisionToken is not null &&
            !SameEntity(source.RevisionToken, content.Value!.Revision.OwningAppRevisionId ?? "")))
            throw new InvalidOperationException("The owning artifact revision differs from the pinned Space source.");
        var access = action.Writing ? ResourceAccess.Write : ResourceAccess.Read;
        var actionId = expectedOwner.App + (action.Writing ? ".file.save" : ".file.open");
        ResourceScope[] scopes = [new("spaces.source", $"{space.Id:N}:{source.ContextId:N}",
            space.Revision.ToString(CultureInfo.InvariantCulture), access),
            new("files.item", fileId.ToString(), action.ExpectedFilesRevision.ToString(), access)];
        if (await resources.AuthorizeAsync(actionId, scopes, cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Current Space and Files resource permissions do not permit this action.");
        if ((await spaces.GetAsync(space.Id, cancellationToken).ConfigureAwait(false))?.Revision != space.Revision ||
            (await workspace.Provider.GetAsync(fileId, cancellationToken).ConfigureAwait(false)).Value?.CurrentRevisionId != action.ExpectedFilesRevision ||
            actor != await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Space or Files access changed before the action could open its target.");
        return new(actor, source, reference.Value, metadata.Value, scopes, actionId);
    }

    private static bool SameEntity(string left, string right) =>
        Guid.TryParse(left, out var a) && Guid.TryParse(right, out var b) ? a == b : string.Equals(left, right, StringComparison.Ordinal);
}
