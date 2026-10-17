using System.Text.Json;
using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    public bool CanPrepareNativeMove(FilesNativeBrowserPage source, HostedItemMetadata row,
        FilesNativeBrowserPage destination) => CanMoveNativeSelection(source, row)
        && !destination.IsTrash && destination.ParentID is { } parent && parent != row.Id.Value
        && parent != row.ParentId?.Value && source.StoreID == destination.StoreID
        && source.StoreRevision == destination.StoreRevision
        && _originalPages.TryGetValue(destination, out _);

    /// <summary>Prepare a same-store move from two actual issued pages. Choosing a destination
    /// carries no execution authority; Home separately reviews the item and both parents.</summary>
    public Task<PreparedMutation> PrepareNativeMoveAsync(FilesNativeBrowserPage originalSource,
        HostedItemMetadata originalRow, FilesNativeBrowserPage originalDestination,
        AuthenticatedResourceActor originalActor, Func<bool> originalLifetime, string sessionId,
        CancellationToken token = default)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = PrepareMoveAfterPublicationAsync(start.Task, originalSource, originalRow,
            originalDestination, originalActor, originalLifetime, sessionId, token);
        start.SetResult(); return actual;
    }

    private async Task<PreparedMutation> PrepareMoveAfterPublicationAsync(Task start,
        FilesNativeBrowserPage source, HostedItemMetadata row, FilesNativeBrowserPage destination,
        AuthenticatedResourceActor actor, Func<bool> lifetime, string sessionId, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(lifetime);
        var broker = _mutationBroker ?? throw new NotSupportedException("Home-reviewed Files editing is not configured.");
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 256)
            throw new ArgumentException("Retain the original bounded Files session.");
        if (!lifetime() || !_originalPages.TryGetValue(source, out var sourceOwner)
            || !_originalPages.TryGetValue(destination, out var destinationOwner)
            || sourceOwner.Actor != actor || destinationOwner.Actor != actor
            || !ReferenceEquals(sourceOwner.Workspace.Provider, destinationOwner.Workspace.Provider))
            throw new UnauthorizedAccessException("Retain the original source and destination pages from the same Files owner.");
        if (!CanPrepareNativeMove(source, row, destination))
            throw new InvalidOperationException("Choose a different live destination folder for the original selected item.");
        await RevalidateAsync(source, actor, token).ConfigureAwait(false);
        await RevalidateAsync(destination, actor, token).ConfigureAwait(false);
        var workspace = sourceOwner.Workspace;
        var targetId = new HostedItemId(destination.ParentID!.Value);
        var targetResult = await workspace.Provider.GetForOriginalStoreAsync(source.StoreID, targetId, token).ConfigureAwait(false);
        if (!targetResult.IsSuccess || targetResult.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: not null } target)
            throw new InvalidOperationException("The original destination folder is unavailable.");
        await RequireMetadataAsync(target, actor, source.StoreID, token).ConfigureAwait(false);
        var parents = new List<FilesItemRevisionPrecondition> { new(target.Id, target.CurrentRevisionId) };
        var scopeItems = new List<HostedItemMetadata> { row, target };
        if (row.ParentId is { } sourceParent)
        {
            var parentResult = await workspace.Provider.GetForOriginalStoreAsync(source.StoreID, sourceParent, token).ConfigureAwait(false);
            if (!parentResult.IsSuccess || parentResult.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: not null } parent)
                throw new InvalidOperationException("The original source folder is unavailable.");
            await RequireMetadataAsync(parent, actor, source.StoreID, token).ConfigureAwait(false);
            parents.Add(new(parent.Id, parent.CurrentRevisionId)); scopeItems.Add(parent);
        }
        // Fail before requesting review for a self/descendant move. The provider repeats this
        // validation inside the same durable transaction, under the captured store revision.
        var ancestor = target;
        var visited = new HashSet<HostedItemId>();
        while (true)
        {
            if (!visited.Add(ancestor.Id) || visited.Count > 256)
                throw new InvalidOperationException("The destination ancestry is invalid or exceeds the supported depth.");
            if (ancestor.Id == row.Id)
                throw new InvalidOperationException("A folder cannot be moved inside itself or one of its descendants.");
            if (ancestor.ParentId is not { } next) break;
            var result = await workspace.Provider.GetForOriginalStoreAsync(source.StoreID, next, token).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is not { Kind: HostedItemKind.Folder } current)
                throw new InvalidOperationException("The destination ancestry is unavailable.");
            await RequireMetadataAsync(current, actor, source.StoreID, token).ConfigureAwait(false);
            ancestor = current;
        }
        var now = DateTimeOffset.UtcNow;
        var operation = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, row.Id, row.ParentId, target.Id,
            "Move", row.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null);
        var scopes = Array.AsReadOnly(scopeItems.Select(item => new ResourceScope("files.item", item.Id.ToString(),
            item.CurrentRevisionId!.Value.ToString(), ResourceAccess.Write)).ToArray());
        var arguments = JsonSerializer.SerializeToElement(new { operationID = operation.Id.Value,
            storeID = source.StoreID, expectedStoreRevision = source.StoreRevision, itemID = row.Id.Value,
            sourceParentID = row.ParentId?.Value, destinationParentID = target.Id.Value,
            expectedRevision = row.CurrentRevisionId!.Value.ToString(), operation = "Move", name = row.Name });
        var approval = await ObserveOriginalAsync(broker.AuthorizeForActorAsync(actor, "files", "9to1.Files.Move",
            scopes, arguments, $"Move ‘{row.Name}’ to ‘{target.Name}’. Its identity and contents are preserved.",
            null, sessionId, token)).ConfigureAwait(false);
        await RevalidateAsync(source, actor, token).ConfigureAwait(false);
        await RevalidateAsync(destination, actor, token).ConfigureAwait(false);
        if (!lifetime()) throw new ObjectDisposedException("Original Files view");
        var prepared = new PreparedMutation(this, workspace, source, actor, operation, row.Name, scopes,
            Array.AsReadOnly(parents.ToArray()), arguments, lifetime, approval, destination, target.Name);
        _issuedNativeMutations.Add(prepared, new ActualOperationObservation());
        return prepared;
    }

    private async Task RevalidateMutationPagesAsync(PreparedMutation original, CancellationToken token)
    {
        await RevalidateAsync(original.Page, original.Actor, token).ConfigureAwait(false);
        if (original.DestinationPage is { } destination)
            await RevalidateAsync(destination, original.Actor, token).ConfigureAwait(false);
    }
}
