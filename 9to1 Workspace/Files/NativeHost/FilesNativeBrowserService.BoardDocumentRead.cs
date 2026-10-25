using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Files.NativeHost;

/// <summary>Verified bytes from one exact selected Files revision. The array is private to
/// this result and copied by Home before it is passed to another owner.</summary>
public sealed record FilesOriginalBoardDocumentRead(Guid StoreId, Guid FileId, Guid ParentFolderId,
    Guid RevisionId, string Sha256, byte[] Bytes);

public sealed partial class FilesNativeBrowserService
{
    private sealed class OriginalBoardRead { internal Exception? Denied; }
    private readonly ConditionalWeakTable<Task, OriginalBoardRead> _originalBoardReads = new();
    /// <summary>Expected denial metadata only, for THIS producer's exact failed task.
    /// It grants no read, path, actor or replacement task authority.</summary>
    public bool IsOriginalBoardDocumentReadRefusal(Task original, Exception failure)
    {
        if (!_originalBoardReads.TryGetValue(original, out var operation) || operation.Denied is not { } denied || original.Exception is null) return false;
        var pending = new Stack<Exception>(); pending.Push(failure); var count = 0;
        while (pending.TryPop(out var taskFailure))
        {
            if (++count > 4096) return false;
            if (ReferenceEquals(taskFailure, denied)) continue;
            if (taskFailure is not AggregateException { InnerExceptions.Count: > 0 } group) return false;
            foreach (var child in group.InnerExceptions) pending.Push(child);
        }
        // Require that the task itself failed for that same exact issued denial.
        pending.Push(original.Exception); count = 0;
        while (pending.TryPop(out var current))
        {
            if (++count > 4096) return false;
            if (ReferenceEquals(current, denied)) continue;
            if (current is not AggregateException { InnerExceptions.Count: > 0 } group) return false;
            foreach (var child in group.InnerExceptions) pending.Push(child);
        }
        return true;
    }
    // Match the Boards document codec's maximum supported envelope size. Files rejects larger
    // revisions before opening their registered physical materialisation.
    public const int MaximumBoardDocumentBytes = 40 * 1024 * 1024;
    public bool HasOriginalBoardDocumentReadOwner => _originalFolders is not null;

    public static bool IsBoardDocumentCandidate(HostedItemMetadata item) => item.Kind == HostedItemKind.File &&
        string.Equals(Path.GetExtension(item.Name), ".9to1board", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a bounded rich Boards document from the SAME privately issued current
    /// Home Library page and row. It uses the existing exact Files READ action, verifies the
    /// immutable revision and registered content anchor before and after reading, and returns
    /// a copied byte buffer. No local path or write capability escapes.</summary>
    public Task<FilesOriginalBoardDocumentRead> ReadOriginalBoardDocumentWithinSourceAsync(
        FilesNativeBrowserPage page, HostedItemMetadata row, AuthenticatedResourceActor actor,
        Func<bool> lifetime, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var operation = new OriginalBoardRead();
        var original = new CloudflareOriginalTaskLedger(); original.BindOriginalOwner(this); original.BindOriginalCallerCallback(scope);
        var source = new FilesOriginalReadSourceScope(body => original.Invoke(() => { body(); return true; }),
            child => { _originalBoardReads.GetValue(child, _ => operation); original.Track(child); retain(child); });
        var actual = original.RunToOriginalSettlementAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var retained = source.Invoke(() => _originalPages.TryGetValue(page, out var issued) && issued.Actor == actor &&
                !page.IsTrash && page.Items.Any(item => ReferenceEquals(item, row)) && IsBoardDocumentCandidate(row) &&
                row.CurrentRevisionId is not null && page.ParentID is { } parent && parent == row.ParentId?.Value && lifetime()
                    ? issued : throw new UnauthorizedAccessException("Select the SAME current original Files board document."));
            var workspace = await source.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(page.StoreID, source, token)).ConfigureAwait(false);
            if (workspace?.Actor != actor || actor.AccountId is not null || actor.OrganisationId is not null ||
                !ReferenceEquals(workspace.Provider, retained.Workspace.Provider) ||
                !ReferenceEquals(workspace.Directories, retained.Workspace.Directories) ||
                !ReferenceEquals(workspace.Materializations, retained.Workspace.Materializations))
                throw new UnauthorizedAccessException("The original Files board-document workspace changed.");
            var owner = _originalFolders?.BorrowOriginalItemReadIssuer()
                ?? throw new NotSupportedException("The original Files content-read owner is unavailable.");
            var read = await source.Observe(() => owner.CaptureOriginalBoardDocumentReadAsync(workspace, row, lifetime, source, token)).ConfigureAwait(false);
            async Task Demand(CancellationToken currentToken)
            {
                currentToken.ThrowIfCancellationRequested();
                if (!source.Invoke(lifetime) || !resources.IsIssuedOriginalReadOwnerBinding(actor, read, "files.board.read",
                    read.OriginalScope, workspace.Provider, workspace.Directories, page.StoreID))
                    throw new UnauthorizedAccessException("The original Home READ no longer permits this selected board document.");
                var authorized = await source.Observe(() => resources.AuthorizeOriginalReadForActorAsync(actor, read, "files.board.read",
                    [read.OriginalScope], currentToken).AsTask()).ConfigureAwait(false);
                if (authorized != actor)
                {
                    var denied = new UnauthorizedAccessException("The current Files resource owner denied reading this board.");
                    if (source.Invoke(lifetime) && await source.Observe(() => actors.GetCurrentAsync(currentToken).AsTask()).ConfigureAwait(false) == actor)
                    {
                        Interlocked.CompareExchange(ref operation.Denied, denied, null);
                        throw operation.Denied ?? denied;
                    }
                    throw denied;
                }
                if (!source.Invoke(lifetime)) throw new UnauthorizedAccessException("The original Home board read lifetime retired.");
                var evidence = await source.Observe(() => workspace.Provider.GetStoreEvidenceAsync(page.StoreID,
                    source.OriginalSynchronousScope, source.RetainOriginalTask, currentToken)).ConfigureAwait(false);
                if (evidence.StoreId != page.StoreID || evidence.Revision != page.StoreRevision)
                    throw new InvalidOperationException("The displayed Files page changed. Refresh before opening the board.");
            }
            await Demand(token).ConfigureAwait(false);
            var content = await source.Observe(() => workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(
                page.StoreID, row.Id, row.CurrentRevisionId, token)).ConfigureAwait(false);
            await Demand(token).ConfigureAwait(false);
            if (!content.IsSuccess || content.Value is not { } committed || committed.Revision.ItemId != row.Id ||
                committed.Revision.Id != row.CurrentRevisionId || committed.Revision.SizeBytes != row.SizeBytes ||
                NormalizeTextPreviewHash(committed.Revision.ContentHash) is not { } hash ||
                hash != NormalizeTextPreviewHash(row.ContentHash))
                throw new InvalidDataException("The selected board has no matching immutable Files content revision.");
            if (committed.Revision.SizeBytes is not { } size || size <= 0 || size > MaximumBoardDocumentBytes)
                throw new InvalidDataException("The selected board has no supported bounded document size.");
            if (committed.UploadAnchorFolderId is not { } anchor ||
                committed.ProviderContentReference is not { Length: > 0 } relative ||
                !Guid.TryParse(actor.ProfileId, out var profile))
                throw new InvalidDataException("The selected board has no supported registered Files content materialisation.");
            var roots = workspace.Configuration.AppFolders.Where(pair => pair.Value == anchor).ToArray();
            if (roots.Length != 1)
                throw new UnauthorizedAccessException("The original board has no unique registered content anchor.");
            var mapping = await source.Observe(() => workspace.Directories.ResolveOriginalRegisteredFolderAsync(profile,
                roots[0].Key, anchor, workspace.Provider, page.StoreID, true,
                ct => new ValueTask(Demand(ct)), token)).ConfigureAwait(false);
            await Demand(token).ConfigureAwait(false);
            if (!mapping.IsSuccess || mapping.Value is not { } binding || binding.FolderId != anchor ||
                binding.LocationId != row.LocationId || binding.ProfileId != profile)
                throw new UnauthorizedAccessException("The original Files board materialisation changed.");
            var path = source.Invoke(() => OriginalTextPreviewPath(binding.DirectoryPath, relative));
            FileStream? stream = null;
            var bytes = new byte[checked((int)size)];
            var transferred = false;
            try
            {
                try
                {
                    source.Invoke(() => { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        65536, FileOptions.Asynchronous | FileOptions.SequentialScan); return true; });
                    if (stream!.Length != size) throw new InvalidDataException("The original board materialisation changed size.");
                    await source.ObserveVoid(() => stream.ReadExactlyAsync(bytes, token).AsTask()).ConfigureAwait(false);
                    var extra = new byte[1];
                    if (await source.Observe(() => stream.ReadAsync(extra, token).AsTask()).ConfigureAwait(false) != 0 ||
                        Convert.ToHexString(SHA256.HashData(bytes)) != hash)
                        throw new InvalidDataException("The selected board bytes do not match the immutable Files checksum.");
                    await Demand(token).ConfigureAwait(false);
                }
                catch (Exception cause) { original.Retain(cause); throw; }
                finally
                {
                    if (stream is not null) await source.ObserveVoid(() => stream.DisposeAsync().AsTask()).ConfigureAwait(false);
                }
                var after = await source.Observe(() => workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(
                    page.StoreID, row.Id, row.CurrentRevisionId, token)).ConfigureAwait(false);
                if (!after.IsSuccess || after.Value != committed)
                    throw new InvalidOperationException("The original Files board content changed during its read.");
                var finalMapping = await source.Observe(() => workspace.Directories.ResolveOriginalRegisteredFolderAsync(profile,
                    roots[0].Key, anchor, workspace.Provider, page.StoreID, true,
                    ct => new ValueTask(Demand(ct)), token)).ConfigureAwait(false);
                if (!finalMapping.IsSuccess || finalMapping.Value != binding ||
                    source.Invoke(() => OriginalTextPreviewPath(binding.DirectoryPath, relative)) != path)
                    throw new UnauthorizedAccessException("The original board materialisation changed during its read.");
                await Demand(token).ConfigureAwait(false);
                transferred = true;
                return new FilesOriginalBoardDocumentRead(page.StoreID, row.Id.Value, row.ParentId!.Value.Value,
                    row.CurrentRevisionId!.Value.Value, hash, bytes);
            }
            finally
            {
                if (!transferred) CryptographicOperations.ZeroMemory(bytes);
            }
        });
        _originalBoardReads.GetValue(actual, _ => operation);
        return actual;
    }
}
