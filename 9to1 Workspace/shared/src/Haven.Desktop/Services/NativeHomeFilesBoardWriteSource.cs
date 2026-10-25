using System.Collections.Concurrent;
using CakeOS.Apps.Boards.Contract;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Services;

/// <summary>Creates a writable presentation only from the same verified Home Library selection
/// used for READ. The selected row, actor, source id and Home review callback remain captured here.</summary>
internal sealed class NativeHomeFilesBoardWriteSource(FilesNativeBrowserService browser,
    FilesNativeBrowserService.BoardDocumentWriteOwner writer, AuthenticatedResourceActor actor)
{
    private readonly ConcurrentDictionary<Guid, NativeHomeFilesBoardWriteLease> _prepared = new();

    internal Guid RegisterPrepared(FilesNativeBrowserPage page, HostedItemMetadata row,
        NativeHomeFilesBoardReadLease read, Func<bool> lifetime,
        Func<HomePermissionAuthorization, CancellationToken, Task> presentReview,
        string sessionId, Action<Action> originalScope, Action<Task> retainOriginalTask)
    {
        ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(presentReview); ArgumentNullException.ThrowIfNull(originalScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        if (!browser.HasOriginalBoardDocumentReadOwner || page.IsTrash || !page.Items.Any(item => ReferenceEquals(item, row)) ||
            !FilesNativeBrowserService.IsBoardDocumentCandidate(row) || row.ParentId?.Value != page.ParentID ||
            read.StoreId != page.StoreID || read.ParentFolderId != row.ParentId?.Value ||
            read.FileId != row.Id.Value || read.RevisionId != row.CurrentRevisionId?.Value ||
            !StringComparer.Ordinal.Equals(read.ProfileId, actor.ProfileId) || !lifetime())
            throw new UnauthorizedAccessException("Prepare editing from the same current Home-selected Files board row and its verified bytes.");
        var document = HavenBoardDocumentCodec.Deserialize(read.Bytes);
        if (document.DocumentId == Guid.Empty)
            throw new InvalidDataException("The selected board document has no canonical identity.");
        var sourceId = $"files/{page.StoreID:D}/{row.ParentId!.Value.Value:D}/{row.Id.Value:D}";
        var leaseId = Guid.NewGuid();
        var session = sessionId + ":board:" + leaseId.ToString("N");
        var lease = new NativeHomeFilesBoardWriteLease(read, sourceId, document.DocumentId,
            (actualSource, expectedRevision, expectedHash, documentId, payload, token) =>
                writer.SaveSelectedWithinSourceAsync(page, row, actor, actualSource, expectedRevision,
                    expectedHash, documentId, payload, lifetime, presentReview, session,
                    originalScope, retainOriginalTask, token));
        if (_prepared.Count >= 64 || !_prepared.TryAdd(leaseId, lease))
            throw new InvalidOperationException("Settle prepared writable Files board opens before opening more documents.");
        return leaseId;
    }

    internal NativeHomeFilesBoardWriteLease TakePrepared(Guid id, Guid storeId, Guid parentFolderId,
        Guid fileId, Guid revisionId)
    {
        if (id == Guid.Empty || storeId == Guid.Empty || parentFolderId == Guid.Empty || fileId == Guid.Empty ||
            revisionId == Guid.Empty || !_prepared.TryRemove(id, out var lease))
            throw new UnauthorizedAccessException("Open the same current Files board from Home Library first.");
        if (!lease.IsOpen || lease.StoreId != storeId || lease.ParentFolderId != parentFolderId ||
            lease.FileId != fileId || lease.RevisionId != revisionId)
        {
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw new UnauthorizedAccessException("The prepared writable board does not match the exact original Files revision.");
        }
        return lease;
    }

    internal Task DiscardPreparedAsync(Guid id) => _prepared.TryRemove(id, out var lease)
        ? lease.DisposeAsync().AsTask() : Task.CompletedTask;

    internal async Task DrainPreparedAsync()
    {
        foreach (var pair in _prepared.ToArray())
            if (_prepared.TryRemove(pair.Key, out var lease)) await lease.DisposeAsync().ConfigureAwait(false);
    }
}
