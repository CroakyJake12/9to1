using System.Text.Json;
using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesArtifactResourceResolver : IOriginalScopedCanonicalResourceAccessResolver
{
    /// <summary>The SAME registered Files policy with actual Home/workspace and repository
    /// producers under the caller's finite source scope. This observation grants no mutation
    /// or physical path authority; delegate-only resolvers have no original scoped fallback.</summary>
    public async Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor sameActor,
        string actionId, ResourceScope sameScope, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        var source = FilesOriginalParentSourceCallbacks.Create(originalSynchronousScope, retainOriginalTask);
        ResourceAccessDecision Deny() => new(false, "FilesOriginalReadChanged", sameActor.ActorId,
            sameScope.Revision, sameActor.OrganisationId);
        if (_originalAuthority is null)
            throw new NotSupportedException("The SAME registered original Home Files authority is required.");
        var workspace = await source.Observe(() =>
            _originalAuthority.GetOriginalCurrentWithinSourceAsync(null, source, token)).ConfigureAwait(false);
        if (workspace is null || workspace.Actor != sameActor) return source.Invoke(Deny);
        // Keep immutable configuration bytes before any subsequent await. The cached
        // configuration contains a dictionary, so comparing it to itself is insufficient.
        var configuration = source.Invoke(() => JsonSerializer.Serialize(workspace.Configuration));
        var folders = source.Invoke(() => JsonSerializer.Deserialize<NativeFilesWorkspaceConfiguration>(configuration)!
            .AppFolders.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
        async ValueTask<bool> Current(CancellationToken cancellationToken)
        {
            var current = await source.Observe(() => _originalAuthority.GetOriginalCurrentWithinSourceAsync(
                workspace.Configuration.StoreId, source, cancellationToken)).ConfigureAwait(false);
            return source.Invoke(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return current is not null && current.Actor == sameActor &&
                    ReferenceEquals(current.Provider, workspace.Provider) &&
                    ReferenceEquals(current.Directories, workspace.Directories) &&
                    ReferenceEquals(current.Materializations, workspace.Materializations) &&
                    JsonSerializer.Serialize(current.Configuration) == configuration &&
                    JsonSerializer.Serialize(workspace.Configuration) == configuration;
            });
        }
        if (!await Current(token).ConfigureAwait(false)) return source.Invoke(Deny);
        try
        {
            var decision = await EvaluateCoreAsync(sameActor, actionId, sameScope, token, workspace.Provider,
                Current, workspace.Configuration.StoreId, source, folders).ConfigureAwait(false);
            if (!await Current(token).ConfigureAwait(false)) return source.Invoke(Deny);
            return source.Invoke(() =>
            {
                token.ThrowIfCancellationRequested();
                return JsonSerializer.Serialize(workspace.Configuration) == configuration ? decision : Deny();
            });
        }
        catch (FilesOriginalStoreReadChangedException) { return source.Invoke(Deny); }
    }
}
