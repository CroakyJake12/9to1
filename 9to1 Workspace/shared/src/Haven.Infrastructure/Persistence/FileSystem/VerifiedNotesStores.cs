/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/VerifiedNotesStores.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns VerifiedNotesRepository, NotesIntegrityManifest, SecureNotesAttachmentStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents verified notes repository and keeps its related state and behavior together.
/// </summary>
public sealed class VerifiedNotesRepository(
    NotesRepository inner,
    IAppPaths paths,
    IProductionDiagnostics diagnostics) : INotesRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root = inner.VerifyOwningRoot(paths);

    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);

    public Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));
        return inner.WithTransactionAsync(async transaction =>
        {
            var document = await transaction.LoadAsync(documentId, cancellationToken).ConfigureAwait(false);
            if (document is null) return null;
            var observed = await transaction.InspectCurrentAsync(documentId, cancellationToken).ConfigureAwait(false);
            if (observed.Absent || observed.Document!.Id != document.Id || observed.Version != document.Version)
                throw new IOException("The returned Notes document does not match the transaction-observed current.");
            // An unresolved publication journal must be validated and preserved;
            // it must never be converted into a blind replacement or fresh save.
            await ReconcileIntegrityAsync(documentId, observed, cancellationToken).ConfigureAwait(false);
            if (document.Recovery.HasUnsavedRecovery && File.Exists(RecoveryPath(documentId)))
            {
                // The raw core has already checked this durable recovery marker
                // against these exact restored bytes. Keep explicit review pending.
                document.Recovery.LastValidSha256 = observed.Sha256;
                return document;
            }
            if (!File.Exists(ManifestPath(documentId))) return document;
            try
            {
                var expected = await ReadJsonAsync<NotesRepository.NotesIntegrityReceipt>(ManifestPath(documentId), cancellationToken).ConfigureAwait(false);
                if (Matches(expected, observed))
                {
                    document.Recovery.LastValidSha256 = observed.Sha256;
                    return document;
                }
                await transaction.WriteDiagnosticAsync(diagnostics, ReliabilitySeverity.Critical, "notes", "integrity-mismatch",
                    "A Haven Notes document did not match its durable integrity manifest and was not returned as current data.",
                    new Dictionary<string, string> { ["documentId"] = documentId.ToString("D"),
                        ["expected"] = expected?.Sha256 ?? "missing", ["actual"] = observed.Sha256 },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                // Quarantine only the exact invalid bytes inspected under this
                // lease, then use the original bound recovery implementation.
                var recovered = await transaction.RecoverAsync(documentId, cancellationToken, observed).ConfigureAwait(false);
                if (recovered is not null)
                {
                    recovered.Recovery.HasUnsavedRecovery = true;
                    recovered.Recovery.RecoveryReason = "Recovered because the current document failed its integrity manifest check.";
                }
                return recovered;
            }
            catch (Exception error) when (error is not NotesRepository.PendingRecoveryException &&
                error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                await transaction.WriteDiagnosticAsync(diagnostics, ReliabilitySeverity.Warning, "notes", "integrity-check-failed",
                    "Haven Notes could not verify the document integrity sidecar and entered recovery.",
                    new Dictionary<string, string> { ["documentId"] = documentId.ToString("D"), ["exceptionType"] = error.GetType().Name },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return await transaction.RecoverAsync(documentId, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
    {
        inner.ValidateSaveCandidate(document);
        var id = document.Id;
        var revision = document.Version;
        return inner.WithTransactionAsync(async transaction =>
        {
            var result = await transaction.SaveAsync(document, reason, id, revision, cancellationToken, integrityRequired: true).ConfigureAwait(false);
            try
            {
                var observed = await transaction.InspectCurrentAsync(id, cancellationToken).ConfigureAwait(false);
                if (observed.Document?.Id != result.DocumentId || observed.Version != result.Version || observed.Sha256 != result.Sha256)
                    throw new IOException("The committed Notes receipt does not match the transaction-observed current.");
                await ReconcileIntegrityAsync(id, observed, cancellationToken).ConfigureAwait(false);
                document.Recovery.LastValidSha256 = observed.Sha256;
            }
            catch (Exception error)
            {
                // The raw Save returned a durable receipt. A sidecar, cancellation
                // or tail failure retains that receipt and its pending intent.
                result = result with { PostCommitWarning = NotesRepository.JoinWarning(result.PostCommitWarning,
                    "The document was committed, but integrity publication did not complete (" + error.GetType().Name + ").") };
            }
            return result;
        }, cancellationToken);
    }

    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) => inner.DeleteAsync(documentId, cancellationToken);
    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken) => inner.GetVersionsAsync(documentId, cancellationToken);
    public Task<NotesDocument?> LoadVersionAsync(Guid documentId, string versionId, CancellationToken cancellationToken) => inner.LoadVersionAsync(documentId, versionId, cancellationToken);
    public Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken) =>
        inner.WithTransactionAsync(transaction => transaction.RecoverAsync(documentId, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken) => inner.SearchAsync(query, cancellationToken);

    private string DirectoryPath(Guid id) => Path.Combine(_root, id.ToString("D"));
    private string ManifestPath(Guid id) => Path.Combine(DirectoryPath(id), "current.integrity.json");
    private string PublicationPath(Guid id) => Path.Combine(DirectoryPath(id), "current.publication.json");
    private string PendingManifestPath(Guid id) => Path.Combine(DirectoryPath(id), "current.integrity.pending.json");
    private string RecoveryPath(Guid id) => Path.Combine(DirectoryPath(id), "current.recovery.json");

    private static bool Matches(NotesRepository.NotesIntegrityReceipt? manifest, NotesRepository.NotesCurrentSnapshot observed) =>
        manifest is not null && !observed.Absent && manifest.Version == 1 && manifest.DocumentId == observed.Document!.Id &&
        manifest.DocumentVersion == observed.Version && manifest.SizeBytes == observed.SizeBytes && manifest.CreatedAt != default &&
        string.Equals(manifest.Sha256, observed.Sha256, StringComparison.OrdinalIgnoreCase);

    private async Task ReconcileIntegrityAsync(Guid id, NotesRepository.NotesCurrentSnapshot observed, CancellationToken token)
    {
        var intent = await inner.ReadPublicationIntentAsync(id, token).ConfigureAwait(false);
        var pendingPath = PendingManifestPath(id);
        var hasPending = File.Exists(pendingPath) || Directory.Exists(pendingPath);
        if (intent is null)
        {
            if (hasPending) throw new IOException("An orphaned Notes integrity intent was preserved and must be inspected before writing.");
            return;
        }
        if (!intent.IntegrityRequired)
        {
            if (hasPending) throw new IOException("The Notes integrity intent does not belong to its publication journal.");
            return;
        }
        NotesRepository.PendingNotesIntegrity? pending = null;
        if (hasPending)
        {
            pending = await ReadJsonAsync<NotesRepository.PendingNotesIntegrity>(pendingPath, token).ConfigureAwait(false);
            if (pending is null || pending.SchemaVersion != 1 || pending.OperationId != intent.OperationId ||
                pending.Manifest is null || pending.Manifest.Version != 1 || pending.Manifest.DocumentId != id ||
                pending.Manifest.DocumentVersion != intent.Intended.DocumentVersion || pending.Manifest.SizeBytes != intent.Intended.SizeBytes ||
                pending.Manifest.Sha256 != intent.Intended.Sha256 || pending.Manifest.CreatedAt != intent.SavedAt)
                throw new IOException("The pending Notes integrity intent is invalid or mismatched and was preserved.");
        }
        // Exact prior current means publication did not become visible. Keep the
        // original intent unresolved, with no replay and no manifest promotion.
        if (observed.Binding == intent.Prior) return;
        if (!intent.Attempted || observed.Binding != intent.Intended)
            throw new IOException("The pending Notes integrity intent does not match the observed intended current and was preserved.");
        if (pending is not null)
            await WriteManifestAtomicAsync(ManifestPath(id), pending.Manifest, token).ConfigureAwait(false);
        else if (!File.Exists(ManifestPath(id)) || !Matches(await ReadJsonAsync<NotesRepository.NotesIntegrityReceipt>(ManifestPath(id), token).ConfigureAwait(false), observed))
            throw new IOException("The matching pending Notes integrity manifest is missing and its journal was preserved.");
        // A crash between these acknowledgments is recoverable from the exact
        // current+manifest match. No document rename is repeated by future loads.
        File.Delete(pendingPath);
        File.Delete(PublicationPath(id));
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, token).ConfigureAwait(false);
    }

    private static async Task WriteManifestAtomicAsync(string path, NotesRepository.NotesIntegrityReceipt manifest, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
            else File.Move(temporary, path);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// Represents secure notes attachment store and keeps its related state and behavior together.
/// </summary>
public sealed class SecureNotesAttachmentStore(
    NotesAttachmentStore inner,
    IAppPaths paths) : INotesAttachmentStore
{
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.GetFullPath(
        Path.Combine(paths.DataDirectory, "Notes", "Attachments"));

    /// <summary>
    /// Performs import asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<NotesMediaData> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken) =>
        inner.ImportAsync(sourcePath, cancellationToken);

    /// <summary>
    /// Performs resolve path asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> ResolvePathAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (attachmentId == Guid.Empty)
            throw new ArgumentException("Attachment ID cannot be empty.", nameof(attachmentId));
        if (!Directory.Exists(_root))
            throw new FileNotFoundException("The Notes attachment store does not exist.");
        var prefix = attachmentId.ToString("N");
        var rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var match = Directory.EnumerateFiles(_root, prefix + "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .FirstOrDefault(path =>
                path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);
        return match is null
            ? throw new FileNotFoundException("The Notes attachment was not found in the managed store.")
            : Task.FromResult(match);
    }

    /// <summary>
    /// Performs delete unreferenced asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteUnreferencedAsync(
        IReadOnlyCollection<Guid> referencedAttachmentIds,
        CancellationToken cancellationToken) =>
        inner.DeleteUnreferencedAsync(referencedAttachmentIds, cancellationToken);
}
