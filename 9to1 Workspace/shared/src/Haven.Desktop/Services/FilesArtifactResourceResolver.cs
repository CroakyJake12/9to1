using Haven.Application;
using HavenOS.Files;

namespace Haven.Desktop.Services;

/// <summary>Host binds actual providers to verified Home actors; caller arguments cannot select a profile or Drive.</summary>
public sealed class FilesArtifactResourceResolver : ICanonicalResourceAccessResolver
{
    private readonly Func<AuthenticatedResourceActor, CancellationToken, ValueTask<IFilesProvider?>> _providers;
    private readonly Func<AuthenticatedResourceActor, string, CancellationToken, ValueTask<HostedItemId?>>? _appFolders;
    public FilesArtifactResourceResolver(Func<AuthenticatedResourceActor, IFilesProvider?> providers)
        : this((actor, _) => ValueTask.FromResult(providers(actor))) { }
    public FilesArtifactResourceResolver(Func<AuthenticatedResourceActor, CancellationToken, ValueTask<IFilesProvider?>> providers,
        Func<AuthenticatedResourceActor, string, CancellationToken, ValueTask<HostedItemId?>>? appFolders = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _appFolders = appFolders;
    }
    public string ResourceKind => "files.item";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny(string code) => new(false, code, actor.ActorId, scope.Revision, actor.OrganisationId);
        if (scope.Kind != ResourceKind || !Guid.TryParse(scope.Id, out var id) || id == Guid.Empty || actor.OrganisationId is not null)
            return Deny("FilesScopeInvalid");
        var sitesWrite = actionId is "sites.project.create" or "sites.project.save";
        var mediaRead = actionId == "media.asset.read" && scope.Access == ResourceAccess.Read;
        var ownerApp = actionId switch
        {
            "write.file.open" or "write.file.save" or "write.file.create" => "write",
            "canvas.file.open" or "canvas.file.save" or "canvas.file.create" => "canvas",
            "games.file.open" or "games.file.save" or "games.file.create" => "games",
            "picture.file.open" or "picture.file.save" or "picture.file.create" => "picture",
            _ => null
        };
        if (sitesWrite && scope.Access != ResourceAccess.Write) return Deny("FilesActionInvalid");
        if (!sitesWrite && !mediaRead && (ownerApp is null || scope.Access != (actionId.EndsWith(".open", StringComparison.Ordinal) ? ResourceAccess.Read : ResourceAccess.Write)))
            return Deny("FilesActionInvalid");
        var provider = await _providers(actor, cancellationToken).ConfigureAwait(false);
        if (provider is null) return Deny("FilesProviderUnauthorised");
        var result = await provider.GetAsync(new(id), cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) return Deny("FilesItemUnavailable");
        var item = result.Value!;
        var owner = actor.AccountId is { } account ? account.ToString("N") : "local-profile:" + actor.ProfileId;
        if (item.OwnerPrincipalId != owner || item.Scope != "personal") return Deny("FilesOwnerDenied");
        var revision = item.CurrentRevisionId?.ToString() ?? "uncommitted";
        if (revision != scope.Revision) return Deny("FilesRevisionConflict");
        if (sitesWrite)
        {
            // This folder comes from the verified host configuration, never from caller arguments.
            var configured = _appFolders is null ? null : await _appFolders(actor, "sites", cancellationToken).ConfigureAwait(false);
            if (item.Kind != HostedItemKind.Folder || configured != item.Id)
                return Deny("FilesAppFolderDenied");
            return new(true, "Allowed", actor.ActorId, revision, null);
        }
        if (mediaRead) return item.Kind == HostedItemKind.File
            ? new(true, "Allowed", actor.ActorId, revision, null) : Deny("FilesMediaSourceInvalid");
        var creating = actionId.EndsWith(".create", StringComparison.Ordinal);
        if (creating && item.Kind != HostedItemKind.Folder) return Deny("FilesDestinationInvalid");
        if (!creating)
        {
            if (provider is not DurableDriveProvider durable) return Deny("FilesOwningAppUnavailable");
            var artifact = await durable.GetArtifactAsync(item.Id, cancellationToken).ConfigureAwait(false);
            var expectedType = ownerApp switch
            {
                "write" => nameof(FilesArtifactType.WriteDocument),
                "canvas" => nameof(FilesArtifactType.Canvas),
                "picture" => nameof(FilesArtifactType.Picture),
                "games" => nameof(FilesArtifactType.GameProject),
                _ => null
            };
            if (item.Kind != HostedItemKind.Artifact || !artifact.IsSuccess ||
                artifact.Value!.OwnerAppId != ownerApp || artifact.Value.ArtifactType != expectedType)
                return Deny("FilesOwningAppMismatch");
        }
        return new(true, "Allowed", actor.ActorId, revision, null);
    }
}
