using Haven.Application;
using HavenOS.Files;

namespace Haven.Desktop.Services;

/// <summary>Host binds actual providers to verified Home actors; caller arguments cannot select a profile or Drive.</summary>
public sealed class FilesArtifactResourceResolver : ICanonicalResourceAccessResolver
{
    private readonly Func<AuthenticatedResourceActor, CancellationToken, ValueTask<IFilesProvider?>> _providers;
    public FilesArtifactResourceResolver(Func<AuthenticatedResourceActor, IFilesProvider?> providers)
        : this((actor, _) => ValueTask.FromResult(providers(actor))) { }
    public FilesArtifactResourceResolver(Func<AuthenticatedResourceActor, CancellationToken, ValueTask<IFilesProvider?>> providers)
    { _providers = providers ?? throw new ArgumentNullException(nameof(providers)); }
    public string ResourceKind => "files.item";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny(string code) => new(false, code, actor.ActorId, scope.Revision, actor.OrganisationId);
        if (scope.Kind != ResourceKind || !Guid.TryParse(scope.Id, out var id) || id == Guid.Empty || actor.OrganisationId is not null)
            return Deny("FilesScopeInvalid");
        var mediaRead = actionId == "media.asset.read" && scope.Access == ResourceAccess.Read;
        var ownerApp = actionId switch
        {
            "write.file.open" or "write.file.save" or "write.file.create" => "write",
            "canvas.file.open" or "canvas.file.save" or "canvas.file.create" => "canvas",
            "picture.file.open" or "picture.file.save" or "picture.file.create" => "picture",
            _ => null
        };
        if (!mediaRead && (ownerApp is null || scope.Access != (actionId.EndsWith(".open", StringComparison.Ordinal) ? ResourceAccess.Read : ResourceAccess.Write)))
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
                _ => null
            };
            if (item.Kind != HostedItemKind.Artifact || !artifact.IsSuccess ||
                artifact.Value!.OwnerAppId != ownerApp || artifact.Value.ArtifactType != expectedType)
                return Deny("FilesOwningAppMismatch");
        }
        return new(true, "Allowed", actor.ActorId, revision, null);
    }
}
