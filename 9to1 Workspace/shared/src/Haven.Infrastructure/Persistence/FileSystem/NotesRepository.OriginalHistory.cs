using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

public sealed partial class NotesRepository
{
    private const long MaximumOriginalHistorySnapshotBytes = 64L * 1024 * 1024;
    private const long MaximumOriginalHistoryReadBytes = 256L * 1024 * 1024;

    // The existing Home-reviewed read transaction owns all descriptors. This never calls
    // public recovery-writing Load/GetVersions, creates files or changes version retention.
    private async Task<NotesOriginalLibrarySnapshot> ReadOriginalHistoryWithinTransactionAsync(Transaction transaction,
        OriginalLibraryReadRequest request, HomeNotesLibraryReadAdmission home, VerifiedNotesRepository verified,
        HomeClaimedResourceCommitFence fence, CancellationToken token)
    {
        if (!transaction.IsOwnedActiveBy(this) || request.DocumentId is not { } id || id == Guid.Empty ||
            request.Kind is not (NotesOriginalReadKind.VersionHistory or NotesOriginalReadKind.Version) ||
            (request.Kind == NotesOriginalReadKind.VersionHistory ? request.VersionId is not null :
                !NotesOriginalVersionIdentity.IsVersionId(request.VersionId)))
            throw new UnauthorizedAccessException("The exact original saved-version read request is required.");
        var root = transaction.OriginalRoot ?? throw new UnauthorizedAccessException("Original Notes read custody is absent.");
        using var library = root.OpenLibraryCustody();
        var libraryVersion = library.SnapshotVersion;
        var versions = new List<NotesVersionInfo>(); NotesDocument? selected = null; var homeValidated = false;
        long snapshotBytes = 0;
        if (library.EnumerateDocumentIds().Contains(id))
        {
            using var document = library.OpenDocument(id);
            transaction.OriginalRead = document;
            try
            {
                var before = document.CaptureReadWitness(id);
                try
                {
                    using var current = document.OpenFile(Path.Combine(document.DocumentPath, "current.haven-notes.json"), FileMode.Open, FileAccess.Read, 4096);
                    DemandSnapshotBound(current.Length);
                }
                catch (FileNotFoundException) { /* The original verified reader classifies absent/recovery state. */ }
                // A history read cannot conceal or repair unresolved current publication,
                // a missing current with recovery artifacts, or invalid current integrity.
                _ = await verified.ReadWithinOriginalTransactionAsync(transaction, id, token).ConfigureAwait(false);
                var leaves = document.EnumerateVersionLeaves();
                if (leaves.Count > MaximumVersionsPerDocument * 2)
                    throw new NotSupportedException("This original history exceeds the supported 100-version view bound; all files were preserved.");
                var observedFiles = new List<(string Path, PhysicalVersion Version)>();
                if (request.Kind == NotesOriginalReadKind.VersionHistory)
                {
                    var metadata = leaves.Where(value => value.EndsWith(".meta.json", StringComparison.Ordinal)).ToArray();
                    var snapshots = leaves.Where(value => value.EndsWith(".haven-notes.json", StringComparison.Ordinal)).ToArray();
                    if (metadata.Length > MaximumVersionsPerDocument || metadata.Length != snapshots.Length ||
                        metadata.Length + snapshots.Length != leaves.Count)
                        throw new InvalidDataException("The original Notes version directory has incomplete, unsupported or excessive artifacts; they were preserved.");
                    foreach (var leaf in metadata)
                    {
                        token.ThrowIfCancellationRequested();
                        var versionId = leaf[..^".meta.json".Length];
                        if (!NotesOriginalVersionIdentity.IsVersionId(versionId) ||
                            !snapshots.Contains(versionId + ".haven-notes.json", StringComparer.Ordinal))
                            throw new InvalidDataException("The original Notes version metadata has no exact canonical snapshot pair.");
                        var entry = await ReadVersionAsync(versionId).ConfigureAwait(false);
                        versions.Add(entry.Info);
                    }
                }
                else
                {
                    var versionId = request.VersionId!;
                    var hasMetadata = leaves.Contains(versionId + ".meta.json", StringComparer.Ordinal);
                    var hasSnapshot = leaves.Contains(versionId + ".haven-notes.json", StringComparer.Ordinal);
                    if (hasMetadata != hasSnapshot)
                        throw new InvalidDataException("The requested original Notes version is incomplete; no recovery was attempted.");
                    if (hasMetadata)
                    {
                        var entry = await ReadVersionAsync(versionId).ConfigureAwait(false);
                        versions.Add(entry.Info); selected = entry.Document;
                    }
                }
                await DemandReadRecoveryClearAsync(id, token).ConfigureAwait(false);
                if (!await home.ValidateCommitFenceAsync(fence, token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The original Home saved-version read fence changed before return.");
                await home.DisposeCommitFenceAsync(fence).ConfigureAwait(false);
                homeValidated = true;
                // Join actual Home cleanup callbacks before the final physical observations.
                // Outer cleanup reuses the same already-settled original fence close task.
                // The final Home await precedes the final physical observations; no external
                // source callback or await can redirect IO between these witnesses and return.
                // A directory witness alone does not detect in-place changes to an earlier file.
                // Reopen every exact retained-parent leaf and compare its actual physical version.
                foreach (var observed in observedFiles)
                {
                    token.ThrowIfCancellationRequested();
                    using var current = document.OpenFile(observed.Path, FileMode.Open, FileAccess.Read, 4096);
                    if (OriginalNotesRoot.Stat(current.SafeFileHandle) != observed.Version)
                        throw new IOException("An original saved-version file changed before its authorised snapshot was returned.");
                }
                if (document.CaptureReadWitness(id) != before ||
                    !document.EnumerateVersionLeaves().SequenceEqual(leaves, StringComparer.Ordinal) ||
                    document.HasNamedEntry("current.publication.json") || document.HasNamedEntry("current.integrity.pending.json") ||
                    document.HasNamedEntry("current.recovery.json"))
                    throw new IOException("The original Notes document/history changed during its authorised snapshot.");

                async Task<(NotesVersionInfo Info, NotesDocument Document)> ReadVersionAsync(string versionId)
                {
                    var metadataPath = Path.Combine(document.VersionsPath, versionId + ".meta.json");
                    NotesVersionManifest manifest;
                    await using (var stream = document.OpenFile(metadataPath, FileMode.Open, FileAccess.Read, 4096))
                    {
                        var physical = OriginalNotesRoot.Stat(stream.SafeFileHandle);
                        if (stream.Length is <= 0 or > 65536)
                            throw new InvalidDataException("The original Notes version metadata exceeds its supported bound.");
                        manifest = await JsonSerializer.DeserializeAsync<NotesVersionManifest>(stream, JsonOptions, token).ConfigureAwait(false)
                            ?? throw new InvalidDataException("The original Notes version metadata is absent.");
                        DemandOriginalReadCurrent(metadataPath, stream, physical); observedFiles.Add((metadataPath, physical));
                    }
                    if (manifest.Version <= 0 || manifest.CreatedAt == default || manifest.SizeBytes <= 0 ||
                        string.IsNullOrWhiteSpace(manifest.Reason) || manifest.Reason.Length > 160 ||
                        manifest.Sha256 is not { Length: 64 } hash || !hash.All(char.IsAsciiHexDigit) ||
                        VersionFileName(manifest.Version, manifest.CreatedAt) != versionId)
                        throw new InvalidDataException("The original Notes version metadata does not match its canonical saved identity.");
                    var snapshotPath = Path.Combine(document.VersionsPath, versionId + ".haven-notes.json");
                    PhysicalVersion physicalSnapshot;
                    using (var stream = document.OpenFile(snapshotPath, FileMode.Open, FileAccess.Read, 4096))
                    {
                        physicalSnapshot = OriginalNotesRoot.Stat(stream.SafeFileHandle);
                        if (stream.Length != manifest.SizeBytes)
                            throw new InvalidDataException("The original Notes saved snapshot size does not match its retained metadata.");
                        DemandSnapshotBound(stream.Length);
                    }
                    var snapshot = await InspectFileCoreAsync(snapshotPath, id, token).ConfigureAwait(false);
                    using (var stream = document.OpenFile(snapshotPath, FileMode.Open, FileAccess.Read, 4096))
                        if (OriginalNotesRoot.Stat(stream.SafeFileHandle) != physicalSnapshot)
                            throw new IOException("The original Notes saved snapshot was replaced during verification.");
                    observedFiles.Add((snapshotPath, physicalSnapshot));
                    if (snapshot.Document is null || snapshot.Version != manifest.Version ||
                        snapshot.Document.UpdatedAt != manifest.CreatedAt || snapshot.SizeBytes != manifest.SizeBytes ||
                        !string.Equals(snapshot.Sha256, hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The original Notes saved bytes do not match their version, timestamp, size and content hash.");
                    return (new(versionId, manifest.Version, manifest.CreatedAt, manifest.Reason, manifest.SizeBytes, manifest.Sha256), snapshot.Document);
                }
            }
            catch (Exception error) when (error is not NotesOriginalReadRecoveryRequiredException &&
                error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { throw new NotesOriginalReadRecoveryRequiredException(id, "The original saved-version read requires separate recovery; its files were preserved.", error); }
            finally { transaction.OriginalRead = null; }
        }
        if (!homeValidated)
        {
            if (!await home.ValidateCommitFenceAsync(fence, token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The original Home saved-version read fence changed before return.");
            await home.DisposeCommitFenceAsync(fence).ConfigureAwait(false);
        }
        library.DemandCurrent();
        if (library.SnapshotVersion != libraryVersion)
            throw new IOException("The original Notes library changed during its saved-version read.");
        return new NotesOriginalLibrarySnapshot(request.StoreObservation.StoreIdentity, [], selected)
        { Versions = Array.AsReadOnly(versions.OrderByDescending(value => value.Version).ThenByDescending(value => value.CreatedAt)
            .ThenBy(value => value.VersionId, StringComparer.Ordinal).ToArray()) };

        void DemandSnapshotBound(long size)
        {
            if (size < 0 || size > MaximumOriginalHistorySnapshotBytes ||
                snapshotBytes > MaximumOriginalHistoryReadBytes - size)
                throw new NotSupportedException("This original history read exceeds its 64 MiB snapshot or 256 MiB operation bound; the original files were preserved.");
            snapshotBytes += size;
        }
    }
}
