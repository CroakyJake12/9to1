using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// The explicit Boards adapter Mesh is required to use before it may touch board data: the generic Mesh
/// resource journal says feature stores need adapters first, and this is one of them. Boards keeps ownership.
/// A synchronized resource is the whole <c>.9to1board</c> envelope, keyed by its stable DocumentId so moving or
/// renaming a file cannot fork identity, with the revision taken from the board's own version fields and the
/// content hash taken over the exact stored bytes. A received state is written through the canonical Boards
/// store, so its validation, backup and atomic replacement still happen; anything the store or the codec
/// refuses is refused here too and the local board is left untouched.
/// </summary>
public sealed class BoardsMeshResourceOwner(JsonFileHavenBoardStore boards, string boardsRootDirectory)
    : IMeshResourceMergeOwner, IMeshResourceStateCapture
{
    private readonly string _root = Path.GetFullPath(boardsRootDirectory);

    public string ResourceType => HavenBoardDocument.FormatIdentity;

    public async Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        if (!string.Equals(resourceType, ResourceType, StringComparison.Ordinal)) return null;
        var found = await FindAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (found is null) return null;
        var (document, path) = found.Value;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return new MeshResourceSnapshot(
            document.DocumentId, ResourceType, RevisionOf(document), ContentHash(bytes), Guid.Empty, document.ModifiedUtc, null);
    }

    public async Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken)
    {
        if (!string.Equals(mutation.ResourceType, ResourceType, StringComparison.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(mutation.Payload)) return false;

        HavenBoardDocument document;
        try
        {
            document = HavenBoardDocumentCodec.Deserialize(Encoding.UTF8.GetBytes(mutation.Payload));
        }
        catch (Exception refused) when (refused is JsonException or InvalidDataException or UnsupportedHavenBoardDocumentVersionException or IOException)
        {
            return false;
        }

        // Identity has to travel with the content: two separately modified boards that happen to share a title
        // must not be merged because their names match.
        if (document.DocumentId != mutation.ResourceId) return false;
        if (RevisionOf(document) != mutation.Revision) return false;

        try
        {
            var existing = await FindAsync(document.DocumentId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                await boards.SaveDocumentAtPathAsync(document, existing.Value.Path, cancellationToken).ConfigureAwait(false);
                return true;
            }

            Directory.CreateDirectory(_root);
            if (document.Snapshot is not null)
            {
                // A different local board already occupies this board id. Choosing by name is exactly what the
                // Boards identity rule forbids, so the incoming copy is refused rather than merged or replaced.
                var claimed = boards.GetDocumentPath(document.Snapshot.Id);
                if (File.Exists(claimed))
                {
                    var occupant = await ReadableAsync(claimed, cancellationToken).ConfigureAwait(false);
                    if (occupant is not null && occupant.DocumentId != document.DocumentId) return false;
                }
                await boards.SaveDocumentAsync(document, cancellationToken).ConfigureAwait(false);
                return true;
            }

            // A rich-only board has no task facet to name its file, so it is stored under a path derived from
            // its stable identity. Location never defines the board, and FindAsync resolves it by identity again
            // on the next pass.
            await boards.SaveDocumentAtPathAsync(document, Path.Combine(_root, document.DocumentId.ToString("N") + ".9to1board"), cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception refused) when (refused is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private async Task<HavenBoardDocument?> ReadableAsync(string path, CancellationToken cancellationToken)
    {
        try { return await boards.LoadDocumentAtPathAsync(path, cancellationToken).ConfigureAwait(false); }
        catch (Exception unreadable) when (unreadable is IOException or JsonException or InvalidDataException or UnsupportedHavenBoardDocumentVersionException) { return null; }
    }

    /// <summary>
    /// Produces the complete current state of one local board. The origin device is deliberately left empty: the
    /// Mesh side stamps its own identity, because an owner must not claim which device is sending.
    /// </summary>
    public async Task<MeshSyncMutation?> CaptureAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (found is null) return null;
        var (document, path) = found.Value;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength > HavenBoardDocumentCodec.MaximumDocumentBytes) return null;
        var revision = Math.Max(1, RevisionOf(document));
        return new MeshSyncMutation(
            Guid.NewGuid(),
            document.DocumentId,
            ResourceType,
            MeshSyncOperationKind.Replace,
            Math.Max(0, revision - 1),
            revision,
            ContentHash(bytes),
            Guid.Empty,
            document.ModifiedUtc,
            Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// Locates the physical board file carrying this logical identity. Files the codec will not accept are
    /// skipped rather than allowed to hide a readable copy of the same board elsewhere.
    /// </summary>
    private async Task<(HavenBoardDocument Document, string Path)?> FindAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty || !Directory.Exists(_root)) return null;
        foreach (var path in Directory.EnumerateFiles(_root, "*.9to1board", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            HavenBoardDocument? document;
            try { document = await boards.LoadDocumentAtPathAsync(path, cancellationToken).ConfigureAwait(false); }
            catch (Exception unreadable) when (unreadable is IOException or JsonException or InvalidDataException or UnsupportedHavenBoardDocumentVersionException) { continue; }
            if (document is not null && document.DocumentId == documentId) return (document, path);
        }
        return null;
    }

    private static long RevisionOf(HavenBoardDocument document) =>
        Math.Max(document.Snapshot?.Version ?? 0, document.RichNotes?.Version ?? 0);

    private static string ContentHash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
