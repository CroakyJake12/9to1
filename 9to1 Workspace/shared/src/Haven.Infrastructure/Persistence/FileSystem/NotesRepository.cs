/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/NotesRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns NotesRepository, NotesVersionManifest, NotesAttachmentStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents notes repository and keeps its related state and behavior together.
/// </summary>
public sealed class NotesRepository(
    IAppPaths paths,
    INotesDocumentValidator validator,
    IProductionDiagnostics diagnostics) : INotesRepository
{
    /// <summary>
    /// Stores maximum versions per document locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumVersionsPerDocument = 100;
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Transaction? _currentTransaction;
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(paths.DataDirectory, "Notes", "Documents");
    /// <summary>
    /// Stores trash locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _trash = Path.Combine(paths.DataDirectory, "Notes", "Trash");

    // Never replace or delete this file: all instances and processes must lock the same
    // physical object, including while a document directory is moved to recoverable trash.
    private readonly string _repositoryLock = Path.Combine(paths.DataDirectory, "Notes", ".repository.lock");
    private static readonly TimeSpan RepositoryLockTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Performs list asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken)
    {
        return WithTransactionAsync<IReadOnlyList<NotesDocumentSummary>>(async transaction =>
        {
            if (!Directory.Exists(_root)) return [];
            var result = new List<NotesDocumentSummary>();
            foreach (var directory in Directory.EnumerateDirectories(_root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(Path.GetFileName(directory), out var id)) continue;
                try
                {
                    var document = await LoadCoreAsync(transaction, id, allowRecovery: true, cancellationToken).ConfigureAwait(false);
                    if (document is not null) result.Add(Summarize(document));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                    await transaction.WriteDiagnosticAsync(diagnostics,
                        ReliabilitySeverity.Warning,
                        "notes",
                        "document-list-skip",
                        "A Notes document could not be included in the library.",
                        new Dictionary<string, string>
                        {
                            ["documentId"] = id.ToString("D"),
                            ["exceptionType"] = ex.GetType().Name
                        },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
            return result.OrderByDescending(item => item.UpdatedAt).ToArray();
        }, cancellationToken);
    }

    /// <summary>
    /// Performs load asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));
        return WithTransactionAsync(transaction => transaction.LoadAsync(documentId, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
    {
        ValidateSaveCandidate(document);
        var id = document.Id;
        var version = document.Version;
        return WithTransactionAsync(transaction => transaction.SaveAsync(document, reason, id, version,
            cancellationToken), cancellationToken);
    }

    internal void ValidateSaveCandidate(NotesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var validation = validator.Validate(document);
        if (!validation.IsValid)
            throw new InvalidDataException("Notes document validation failed: " + string.Join(" | ", validation.Issues.Where(issue => issue.IsError).Take(12).Select(issue => issue.Path + ": " + issue.Message)));
    }

    internal string VerifyOwningRoot(IAppPaths claimedPaths)
    {
        ArgumentNullException.ThrowIfNull(claimedPaths);
        var owned = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var claimed = Path.GetFullPath(Path.Combine(claimedPaths.DataDirectory, "Notes", "Documents"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(owned, claimed, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("The verified Notes paths must belong to the inner repository's exact storage root.", nameof(claimedPaths));
        return owned;
    }

    private async Task<NotesSaveResult> SaveCoreAsync(Transaction transaction, NotesDocument document, string reason,
        Guid expectedId, long expectedVersion, CancellationToken cancellationToken, bool integrityRequired)
    {
        if (document.Id != expectedId || document.Version != expectedVersion)
            throw new InvalidOperationException("The pending Notes document identity or revision changed while waiting to save.");
        await GuardPendingWriteAsync(expectedId, cancellationToken).ConfigureAwait(false);
        var prior = await InspectCurrentCoreAsync(expectedId, cancellationToken, applyPendingRecovery: true).ConfigureAwait(false);
        if (prior.Document is null && await ReadPendingRecoveryAsync(expectedId, cancellationToken).ConfigureAwait(false) is not null)
            throw new PendingRecoveryException("An existing Notes recovery must be inspected before creating this canonical identity.");
        if (prior.Version != expectedVersion)
            throw new NotesRevisionConflictException(expectedId, expectedVersion, prior.Version);

        var currentPath = CurrentPath(expectedId);
        var versions = VersionsDirectory(expectedId);
        Directory.CreateDirectory(DocumentDirectory(expectedId));
        Directory.CreateDirectory(versions);
        var previousUpdatedAt = document.UpdatedAt;
        var previousAutosaveAt = document.Recovery.LastAutosaveAt;
        var previousHash = document.Recovery.LastValidSha256;
        var previousHasRecovery = document.Recovery.HasUnsavedRecovery;
        var previousRecoveryReason = document.Recovery.RecoveryReason;
        var now = DateTimeOffset.UtcNow;
        document.Version = checked(expectedVersion + 1);
        document.UpdatedAt = now;
        document.Recovery.LastAutosaveAt = now;
        document.Recovery.HasUnsavedRecovery = false;
        document.Recovery.RecoveryReason = string.Empty;

        var operationId = Guid.NewGuid();
        var temporary = currentPath + ".tmp-" + operationId.ToString("N");
        var backup = BackupPath(expectedId);
        var versionId = VersionFileName(document.Version, now);
        var versionPath = Path.Combine(versions, versionId + ".haven-notes.json");
        var committed = false;
        var attempted = false;
        var unresolved = false;
        var ownsIntent = false;
        var historyComplete = false;
        var hash = string.Empty;
        string? warning = null;
        try
        {
            await WriteJsonDurablyAsync(temporary, document, cancellationToken).ConfigureAwait(false);
            document.Recovery.LastValidSha256 = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
            await WriteJsonDurablyAsync(temporary, document, cancellationToken, overwrite: true).ConfigureAwait(false);
            var intended = await InspectFileCoreAsync(temporary, expectedId, cancellationToken).ConfigureAwait(false);
            hash = intended.Sha256;
            var intent = new NotesPublicationIntent(1, operationId, expectedId, prior.Binding, intended.Binding,
                Path.GetFileName(temporary), Path.GetFileName(backup), now, false, integrityRequired);
            await WriteJsonDurablyAsync(PublicationPath(expectedId), intent, cancellationToken).ConfigureAwait(false);
            ownsIntent = true;
            if (integrityRequired)
                await WriteJsonDurablyAsync(PendingIntegrityPath(expectedId), new PendingNotesIntegrity(1, operationId,
                    new NotesIntegrityReceipt(1, expectedId, intended.Version, hash, intended.SizeBytes, now)), cancellationToken).ConfigureAwait(false);
            if (!prior.Absent)
                await PreservePreviousVersionAsync(expectedId, currentPath, expectedVersion, reason, cancellationToken).ConfigureAwait(false);
            // The prepared file is an externally visible filesystem artifact.
            // Recheck its exact durable binding after journal/history work and
            // immediately before admitting the publication effect.
            var prepared = await InspectFileCoreAsync(temporary, expectedId, cancellationToken).ConfigureAwait(false);
            if (prepared.Binding != intent.Intended)
                throw new IOException("The prepared Notes bytes changed after their durable publication intent was staged.");
            await MarkPublicationAttemptedAsync(intent, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            attempted = true;
            try
            {
                if (!prior.Absent) File.Replace(temporary, currentPath, backup, ignoreMetadataErrors: true);
                else File.Move(temporary, currentPath);
                committed = true;
            }
            catch (Exception publicationError)
            {
                // A platform rename may publish and then throw. Never infer failure
                // from its exception, and never cancel the bounded observation.
                NotesCurrentSnapshot? observed = null;
                using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { observed = await InspectCurrentCoreAsync(expectedId, observation.Token).ConfigureAwait(false); }
                catch (Exception observationError) when (observationError is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or OperationCanceledException) { }
                if (observed is not null && observed.Binding == intent.Intended)
                {
                    committed = true;
                    warning = "The intended document was observed committed after publication reported " + publicationError.GetType().Name + ".";
                }
                else if (observed is not null && observed.Binding == intent.Prior)
                {
                    attempted = false; // exact prior-state observation justifies rollback
                    throw;
                }
                else
                {
                    unresolved = true;
                    throw new IOException("Notes publication outcome could not be established; its intent, prepared bytes and backup were preserved. Inspect recovery before retrying.", publicationError);
                }
            }
            File.Delete(PendingRecoveryPath(expectedId));
            await CopyDurablyAsync(currentPath, versionPath, cancellationToken).ConfigureAwait(false);
            await WriteJsonDurablyAsync(Path.Combine(versions, versionId + ".meta.json"),
                new NotesVersionManifest(document.Version, now, NormalizeReason(reason), new FileInfo(versionPath).Length, hash), cancellationToken).ConfigureAwait(false);
            ApplyRetention(versions);
            historyComplete = true;
            await transaction.WriteDiagnosticAsync(diagnostics, ReliabilitySeverity.Information, "notes", "document-saved",
                "A Haven Notes document was written atomically and versioned.",
                new Dictionary<string, string> { ["documentId"] = expectedId.ToString("D"),
                    ["version"] = document.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["sha256"] = hash, ["reason"] = NormalizeReason(reason) }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (committed)
        {
            warning = JoinWarning(warning, "The document was committed, but " + (historyComplete ? "save diagnostics" : "version history") + " did not complete (" + error.GetType().Name + ").");
        }
        catch
        {
            if (!unresolved && !attempted)
            {
                document.Version = expectedVersion;
                document.UpdatedAt = previousUpdatedAt;
                document.Recovery.LastAutosaveAt = previousAutosaveAt;
                document.Recovery.LastValidSha256 = previousHash;
                document.Recovery.HasUnsavedRecovery = previousHasRecovery;
                document.Recovery.RecoveryReason = previousRecoveryReason;
                if (ownsIntent) { File.Delete(PendingIntegrityPath(expectedId)); File.Delete(PublicationPath(expectedId)); }
            }
            throw;
        }
        finally
        {
            if (!unresolved && (!attempted || committed)) TryDelete(temporary);
        }
        // Verified saves retain the receipt journal until the matching sidecar is
        // durable. Raw successful saves have no sidecar tail to reconcile.
        if (!integrityRequired)
        {
            try { File.Delete(PublicationPath(expectedId)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { warning = JoinWarning(warning, "The committed publication intent could not be acknowledged (" + error.GetType().Name + ")."); }
        }
        return new NotesSaveResult(expectedId, document.Version, now, hash, currentPath, versionPath)
        { VersionHistoryComplete = historyComplete, PostCommitWarning = warning };
    }

    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));
        return WithTransactionAsync<bool>(async transaction =>
        {
            await GuardPendingWriteAsync(documentId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var directory = DocumentDirectory(documentId);
            if (!Directory.Exists(directory)) return true;
            Directory.CreateDirectory(_trash);
            var destination = Path.Combine(_trash, documentId + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
            Directory.Move(directory, destination);
            await transaction.WriteDiagnosticAsync(diagnostics,
                ReliabilitySeverity.Information,
                "notes",
                "document-trashed",
                "A Haven Notes document was moved to recoverable trash.",
                new Dictionary<string, string> { ["documentId"] = documentId.ToString("D") },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Retrieves versions async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return WithTransactionAsync<IReadOnlyList<NotesVersionInfo>>(async transaction =>
        {
            return await GetVersionsCoreAsync(documentId, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>
    /// Performs load version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<NotesDocument?> LoadVersionAsync(Guid documentId, string versionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(versionId) || versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || versionId.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("A managed Notes version ID is required.", nameof(versionId));
        return WithTransactionAsync<NotesDocument?>(async transaction =>
        {
            var root = Path.GetFullPath(VersionsDirectory(documentId)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, versionId + ".haven-notes.json"));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
            return await ReadAndValidateAsync(path, documentId, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>
    /// Performs recover latest asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken) =>
        WithTransactionAsync(transaction => transaction.RecoverAsync(documentId, cancellationToken), cancellationToken);

    /// <summary>
    /// Performs search asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();
        if (normalized.Length < 2) return Task.FromResult<IReadOnlyList<NotesSearchHit>>([]);
        return WithTransactionAsync<IReadOnlyList<NotesSearchHit>>(async transaction =>
        {
            if (!Directory.Exists(_root)) return [];
            var result = new List<NotesSearchHit>();
            foreach (var directory in Directory.EnumerateDirectories(_root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(Path.GetFileName(directory), out var id)) continue;
                NotesDocument? document;
                try { document = await LoadCoreAsync(transaction, id, allowRecovery: false, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { continue; }
                if (document is null) continue;
                foreach (var section in document.Sections)
                foreach (var page in section.Pages)
                foreach (var block in page.Blocks)
                {
                    var searchable = BlockSearchText(block);
                    var offset = searchable.IndexOf(normalized, StringComparison.CurrentCultureIgnoreCase);
                    if (offset < 0) continue;
                    result.Add(new NotesSearchHit(
                        document.Id,
                        document.Title,
                        section.Id,
                        page.Id,
                        block.Id,
                        block.Kind.ToString(),
                        Snippet(searchable, offset, normalized.Length),
                        offset));
                    if (result.Count >= 500) return result;
                }
            }
            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Performs load core asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<NotesDocument?> LoadCoreAsync(Transaction transaction, Guid documentId, bool allowRecovery, CancellationToken cancellationToken)
    {
        var intent = await ReadPublicationIntentAsync(documentId, cancellationToken).ConfigureAwait(false);
        await ReadPendingIntegrityAsync(documentId, intent, cancellationToken).ConfigureAwait(false);
        var path = CurrentPath(documentId);
        if (intent is not null && !File.Exists(path))
            throw new PendingRecoveryException("A pending Notes publication must be inspected before recovery.");
        if (intent is not null)
        {
            var observed = await InspectCurrentCoreAsync(documentId, cancellationToken).ConfigureAwait(false);
            if (observed.Binding != intent.Prior && observed.Binding != intent.Intended)
                throw new PendingRecoveryException("The pending Notes publication does not match either exact observed state and was preserved.");
        }
        if (!File.Exists(path)) return allowRecovery ? await RecoverCoreAsync(transaction, documentId, cancellationToken).ConfigureAwait(false) : null;
        try { return await ReadAndValidateAsync(path, documentId, cancellationToken, applyPendingRecovery: true).ConfigureAwait(false); }
        catch (Exception ex) when (allowRecovery && intent is null && ex is not PendingRecoveryException &&
                                   ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Quarantine(path, "corrupt-current");
            var recovered = await RecoverCoreAsync(transaction, documentId, cancellationToken).ConfigureAwait(false);
            await transaction.WriteDiagnosticAsync(diagnostics,
                recovered is null ? ReliabilitySeverity.Critical : ReliabilitySeverity.Warning,
                "notes",
                recovered is null ? "recovery-failed" : "document-recovered",
                recovered is null ? "A corrupt Notes document had no valid recovery copy." : "A corrupt Notes document was recovered from its last valid copy.",
                new Dictionary<string, string>
                {
                    ["documentId"] = documentId.ToString("D"),
                    ["exceptionType"] = ex.GetType().Name
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return recovered;
        }
    }

    /// <summary>
    /// Performs recover core asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<NotesDocument?> RecoverCoreAsync(Transaction transaction, Guid documentId, CancellationToken cancellationToken)
    {
        var current = CurrentPath(documentId);
        var intent = await ReadPublicationIntentAsync(documentId, cancellationToken).ConfigureAwait(false);
        await ReadPendingIntegrityAsync(documentId, intent, cancellationToken).ConfigureAwait(false);
        if (intent is not null)
            throw new PendingRecoveryException("A pending Notes publication must be inspected before recovery.");
        var pending = !File.Exists(current)
            ? await ReadPendingRecoveryAsync(documentId, cancellationToken).ConfigureAwait(false)
            : null;
        var candidates = new List<string>();
        var backup = BackupPath(documentId);
        if (File.Exists(backup)) candidates.Add(backup);
        var versions = VersionsDirectory(documentId);
        if (Directory.Exists(versions))
            candidates.AddRange(Directory.EnumerateFiles(versions, "*.haven-notes.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc));

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validatedCandidate = false;
            try
            {
                var document = await ReadAndValidateAsync(candidate, documentId, cancellationToken).ConfigureAwait(false);
                validatedCandidate = true;
                var recoveredAt = DateTimeOffset.UtcNow;
                var recoveryId = Guid.NewGuid();
                if (!File.Exists(current))
                {
                    var temporary = current + ".tmp-recovery-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        await CopyDurablyAsync(candidate, temporary, cancellationToken).ConfigureAwait(false);
                        var copied = await ReadAndValidateAsync(temporary, documentId, cancellationToken).ConfigureAwait(false);
                        var hash = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
                        if (copied.Version != document.Version)
                            throw new InvalidDataException("The recovery candidate changed while it was copied.");
                        if (pending is not null && (pending.Version != copied.Version || pending.Sha256 != hash))
                            continue;
                        pending ??= new PendingNotesRecovery(1, documentId, copied.Version, hash, recoveredAt, recoveryId);
                        recoveredAt = pending.RecoveredAt;
                        recoveryId = pending.RecoveryRevisionId;
                        if (!File.Exists(PendingRecoveryPath(documentId)))
                            await WritePendingRecoveryAsync(pending, cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        // Never replace a current file that appeared outside this owned recovery.
                        File.Move(temporary, current);
                        document = copied;
                    }
                    finally { TryDelete(temporary); }
                }
                MarkPendingRecovery(document, recoveredAt, recoveryId);
                return document;
            }
            catch (Exception ex) when (!validatedCandidate && ex is not PendingRecoveryException &&
                                       ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
        }
        if (pending is not null)
            throw new PendingRecoveryException("The pending Notes recovery has no matching validated copy.");
        return null;
    }

    /// <summary>
    /// Performs read and validate asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<NotesDocument> ReadAndValidateAsync(string path, Guid expectedId, CancellationToken cancellationToken,
        bool applyPendingRecovery = false)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<NotesDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidDataException("The Notes document was empty.");
        if (document.Id != expectedId)
            throw new InvalidDataException("The Notes file does not belong to its canonical document identity.");
        var validation = validator.Validate(document);
        if (!validation.IsValid)
            throw new InvalidDataException("The Notes document failed validation: " + string.Join(" | ", validation.Issues.Where(issue => issue.IsError).Take(8).Select(issue => issue.Path + ": " + issue.Message)));
        if (applyPendingRecovery)
        {
            var pending = await ReadPendingRecoveryAsync(expectedId, cancellationToken).ConfigureAwait(false);
            if (pending is not null && pending.Version >= document.Version)
            {
                stream.Position = 0;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
                if (pending.Version != document.Version || pending.Sha256 != hash)
                    throw new PendingRecoveryException("The pending Notes recovery does not match the observed current bytes.");
                MarkPendingRecovery(document, pending.RecoveredAt, pending.RecoveryRevisionId);
            }
        }
        return document;
    }

    private string PendingRecoveryPath(Guid id) => Path.Combine(DocumentDirectory(id), "current.recovery.json");

    private async Task<PendingNotesRecovery?> ReadPendingRecoveryAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(PendingRecoveryPath(id), FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var pending = await JsonSerializer.DeserializeAsync<PendingNotesRecovery>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (pending is null || pending.SchemaVersion != 1 || pending.DocumentId != id || id == Guid.Empty ||
                pending.Version <= 0 || pending.RecoveryRevisionId == Guid.Empty || pending.RecoveredAt == default ||
                pending.Sha256 is not { Length: 64 } || pending.Sha256.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
                throw new PendingRecoveryException("The pending Notes recovery marker is invalid and was preserved.");
            return pending;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is not PendingRecoveryException &&
                                      error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new PendingRecoveryException("The pending Notes recovery marker could not be verified and was preserved.", error);
        }
    }

    private async Task WritePendingRecoveryAsync(PendingNotesRecovery pending, CancellationToken cancellationToken)
    {
        var target = PendingRecoveryPath(pending.DocumentId);
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await WriteJsonDurablyAsync(temporary, pending, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target);
        }
        finally { TryDelete(temporary); }
    }

    private static void MarkPendingRecovery(NotesDocument document, DateTimeOffset recoveredAt, Guid recoveryId)
    {
        document.Recovery.HasUnsavedRecovery = true;
        document.Recovery.LastRecoveredAt = recoveredAt;
        document.Recovery.RecoveryReason = "Recovered after the current file failed validation.";
        if (document.Revisions.All(revision => revision.Id != recoveryId))
            document.Revisions.Add(new NotesRevision
            {
                Id = recoveryId,
                Kind = NotesRevisionKind.Restored,
                Summary = "Recovered the last valid Notes version",
                CreatedAt = recoveredAt,
                Author = "Haven recovery"
            });
    }

    private sealed record PendingNotesRecovery(int SchemaVersion, Guid DocumentId, long Version, string Sha256,
        DateTimeOffset RecoveredAt, Guid RecoveryRevisionId);

    internal sealed class PendingRecoveryException(string message, Exception? inner = null) : IOException(message, inner);

    // An issuer-bound capability: construction and admission belong exclusively
    // to WithTransactionAsync. Every original task is retained and settled.
    internal sealed class Transaction
    {
        private readonly NotesRepository _owner;
        private readonly object _admission = new();
        private readonly List<Task> _tasks = [];
        private readonly List<Func<ValueTask>> _diagnostics = [];
        private Task _tail = Task.CompletedTask;
        private bool _active = true;
        private bool _diagnosticsClosed;
        internal NotesSaveResult? CommittedReceipt { get; private set; }
        private Transaction(NotesRepository owner) => _owner = owner;
        // Issued instances are inert until their owner registers the exact
        // instance while holding its gate and physical lease.
        internal static Transaction Issue(NotesRepository owner) => new(owner);

        private Task<T> Admit<T>(Func<Task<T>> operation)
        {
            lock (_admission)
            {
                EnsureActive(_owner);
                var predecessor = _tail;
                var task = ExecuteAsync(predecessor, operation);
                _tasks.Add(task);
                _tail = task;
                return task;
            }
        }
        private static async Task<T> ExecuteAsync<T>(Task predecessor, Func<Task<T>> operation)
        {
            try { await predecessor.ConfigureAwait(false); } catch (Exception) { /* retained by drain */ }
            Task<T>? original = null;
            try
            {
                original = operation() ?? throw new InvalidOperationException("The original Notes operation returned no task.");
                return await original.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                // Await exposes one member of a multiply-faulted Task. Retain its
                // original compound before this admitted wrapper can replace it.
                if (original?.Exception is { InnerExceptions.Count: > 1 } compound)
                    throw new AggregateException("The original Notes operation failed.", compound.InnerExceptions);
                // A faulted or synchronous OCE is an original fault, not evidence of
                // an actually canceled Task. Preserve its cause and faulted status.
                if (error is OperationCanceledException && original?.IsCanceled != true)
                    throw new AggregateException("The original Notes operation faulted with a cancellation exception.", error);
                throw;
            }
        }
        private void EnsureActive(NotesRepository issuer)
        {
            if (!_active || !ReferenceEquals(issuer, _owner) || !ReferenceEquals(_owner._currentTransaction, this))
                throw new InvalidOperationException("The Notes transaction is no longer owned and active.");
        }
        internal Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, Guid expectedId,
            long expectedVersion, CancellationToken token, bool integrityRequired = false) =>
            Admit(async () => CommittedReceipt = await _owner.SaveCoreAsync(this, document, reason, expectedId, expectedVersion, token, integrityRequired).ConfigureAwait(false));
        internal Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) =>
            Admit(() => _owner.LoadCoreAsync(this, id, allowRecovery: true, token));
        internal Task<NotesCurrentSnapshot> InspectCurrentAsync(Guid id, CancellationToken token) =>
            Admit(() => _owner.InspectCurrentCoreAsync(id, token));
        internal Task<NotesDocument?> RecoverAsync(Guid id, CancellationToken token, NotesCurrentSnapshot? invalidCurrent = null) =>
            Admit(async () =>
            {
                if (invalidCurrent is not null)
                {
                    // An exact prior under an unresolved publication is valid
                    // recovery evidence. Refuse every pending write state before
                    // quarantine can move that canonical prior out of place.
                    await _owner.GuardPendingWriteAsync(id, token).ConfigureAwait(false);
                    var actual = await _owner.InspectCurrentCoreAsync(id, token).ConfigureAwait(false);
                    if (actual.Binding != invalidCurrent.Binding || actual.Absent)
                        throw new IOException("The invalid Notes current changed before quarantine.");
                    Quarantine(_owner.CurrentPath(id), "integrity-mismatch");
                }
                return await _owner.RecoverCoreAsync(this, id, token).ConfigureAwait(false);
            });
        internal ValueTask WriteDiagnosticAsync(IProductionDiagnostics target, ReliabilitySeverity severity,
            string component, string eventName, string message, IReadOnlyDictionary<string, string>? data = null,
            string? correlationId = null, CancellationToken cancellationToken = default)
        {
            lock (_admission)
            {
                // Core work admitted before sealing may still buffer its diagnostics.
                if (_diagnosticsClosed || !ReferenceEquals(_owner._currentTransaction, this))
                    throw new InvalidOperationException("The Notes transaction diagnostic buffer is no longer active.");
                var values = data is null ? null : new Dictionary<string, string>(data);
                _diagnostics.Add(() => target.WriteAsync(severity, component, eventName, message, values, correlationId, cancellationToken));
            }
            return ValueTask.CompletedTask;
        }
        internal async Task<List<Exception>> SealAndDrainAsync()
        {
            Task[] tasks;
            lock (_admission) { _active = false; tasks = _tasks.ToArray(); }
            var errors = new List<Exception>();
            foreach (var task in tasks)
                try { await task.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalTaskFailures(errors, task, error); }
            lock (_admission) { _diagnosticsClosed = true; }
            return errors;
        }
        internal async Task<List<Exception>> ReplayDiagnosticsAsync()
        {
            var errors = new List<Exception>();
            foreach (var diagnostic in _diagnostics)
            {
                Task? original = null;
                try
                {
                    // Consume each ValueTask once; retain the same wrapped Task,
                    // including all original faults supplied by the diagnostics owner.
                    original = diagnostic().AsTask();
                    await original.ConfigureAwait(false);
                }
                catch (Exception error) { AddOriginalTaskFailures(errors, original, error); }
            }
            return errors;
        }
    }

    internal async Task<T> WithTransactionAsync<T>(Func<Transaction, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Transaction? transaction = null;
        FileStream? lease = null;
        T? result = default;
        var errors = new List<Exception>();
        try
        {
            lease = await AcquireRepositoryLeaseAsync(cancellationToken).ConfigureAwait(false);
            transaction = Transaction.Issue(this);
            _currentTransaction = transaction;
            Task<T>? original = null;
            try
            {
                original = operation(transaction) ?? throw new InvalidOperationException("The original Notes transaction returned no task.");
                result = await original.ConfigureAwait(false);
            }
            catch (Exception error) { AddOriginalTaskFailures(errors, original, error); }
            errors.AddRange(await transaction.SealAndDrainAsync().ConfigureAwait(false));
        }
        finally
        {
            _currentTransaction = null;
            Task? originalDispose = null;
            try
            {
                if (lease is not null)
                {
                    originalDispose = lease.DisposeAsync().AsTask();
                    await originalDispose.ConfigureAwait(false);
                }
            }
            catch (Exception error) { AddOriginalTaskFailures(errors, originalDispose, error); }
            finally { _gate.Release(); }
        }
        if (transaction is not null) errors.AddRange(await transaction.ReplayDiagnosticsAsync().ConfigureAwait(false));
        errors = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToList();
        if (errors.Count > 0)
        {
            if (typeof(T) == typeof(NotesSaveResult) && (result as NotesSaveResult ?? transaction?.CommittedReceipt) is { } receipt)
                return (T)(object)(receipt with { PostCommitWarning = JoinWarning(receipt.PostCommitWarning,
                    "The committed document has transaction or diagnostic warnings (" + string.Join(", ", errors.Select(error => error.GetType().Name)) + ").") });
            if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Notes transaction operations or diagnostics failed.", errors);
        }
        return result!;
    }

    private static void AddOriginalTaskFailures(List<Exception> errors, Task? original, Exception observed)
    {
        if (original?.Exception is { } compound)
        {
            // Keep opaque empty aggregates as original objects. A sole faulted OCE
            // needs a nonempty envelope so our async owner does not reclassify it.
            foreach (var failure in compound.InnerExceptions)
                errors.Add(failure is OperationCanceledException
                    ? new AggregateException("The original Notes task faulted with a cancellation exception.", failure)
                    : failure);
        }
        else if (original is null && observed is OperationCanceledException)
            errors.Add(new AggregateException("The original Notes callback synchronously faulted with a cancellation exception.", observed));
        else errors.Add(observed); // Actual canceled tasks remain canceled; no token-flag inference.
    }

    internal static string JoinWarning(string? first, string next) => string.IsNullOrWhiteSpace(first) ? next : first + " " + next;
    internal sealed record NotesStateBinding(Guid DocumentId, long DocumentVersion, string Sha256, long SizeBytes, bool Absent);
    internal sealed record NotesCurrentSnapshot(NotesDocument? Document, string Sha256, long SizeBytes)
    {
        internal bool Absent => Document is null;
        internal long Version => Document?.Version ?? 0;
        internal NotesStateBinding Binding => new(Document?.Id ?? Guid.Empty, Version, Sha256, SizeBytes, Absent);
    }
    internal sealed record NotesPublicationIntent(int SchemaVersion, Guid OperationId, Guid DocumentId,
        NotesStateBinding Prior, NotesStateBinding Intended, string TemporaryFile, string BackupFile,
        DateTimeOffset SavedAt, bool Attempted, bool IntegrityRequired);
    internal sealed record NotesIntegrityReceipt(int Version, Guid DocumentId, long DocumentVersion,
        string Sha256, long SizeBytes, DateTimeOffset CreatedAt)
    {
        public long VersionNumber => DocumentVersion;
    }
    internal sealed record PendingNotesIntegrity(int SchemaVersion, Guid OperationId, NotesIntegrityReceipt Manifest);
    private string PublicationPath(Guid id) => Path.Combine(DocumentDirectory(id), "current.publication.json");
    private string PendingIntegrityPath(Guid id) => Path.Combine(DocumentDirectory(id), "current.integrity.pending.json");
    private async Task GuardPendingWriteAsync(Guid id, CancellationToken token)
    {
        if (await ReadPublicationIntentAsync(id, token).ConfigureAwait(false) is not null ||
            File.Exists(PendingIntegrityPath(id)) || Directory.Exists(PendingIntegrityPath(id)))
            throw new PendingRecoveryException("An unresolved Notes publication or integrity intent was preserved; inspect recovery before writing.");
        await ReadPendingRecoveryAsync(id, token).ConfigureAwait(false);
    }
    internal async Task<NotesPublicationIntent?> ReadPublicationIntentAsync(Guid id, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(PublicationPath(id), FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var intent = await JsonSerializer.DeserializeAsync<NotesPublicationIntent>(stream, JsonOptions, token).ConfigureAwait(false);
            if (intent is null || intent.SchemaVersion != 1 || intent.OperationId == Guid.Empty || intent.DocumentId != id ||
                id == Guid.Empty || intent.SavedAt == default || intent.Intended is null || intent.Prior is null ||
                !ValidBinding(intent.Intended, id, allowAbsent: false) || !ValidBinding(intent.Prior, id, allowAbsent: true) ||
                intent.Intended.DocumentVersion != checked(intent.Prior.DocumentVersion + 1) ||
                intent.TemporaryFile != "current.haven-notes.json.tmp-" + intent.OperationId.ToString("N") ||
                intent.BackupFile != "backup.haven-notes.json")
                throw new PendingRecoveryException("The pending Notes publication intent is invalid and was preserved.");
            return intent;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is not PendingRecoveryException && error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or OverflowException)
        { throw new PendingRecoveryException("The pending Notes publication intent could not be verified and was preserved.", error); }
    }
    private static bool ValidBinding(NotesStateBinding binding, Guid id, bool allowAbsent) => binding.Absent
        ? allowAbsent && binding.DocumentId == Guid.Empty && binding.DocumentVersion == 0 && binding.SizeBytes == 0 && binding.Sha256 == string.Empty
        : binding.DocumentId == id && binding.DocumentVersion > 0 && binding.SizeBytes > 0 && binding.Sha256 is { Length: 64 } &&
            binding.Sha256.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal async Task<PendingNotesIntegrity?> ReadPendingIntegrityAsync(Guid id, NotesPublicationIntent? intent, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(PendingIntegrityPath(id), FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var pending = await JsonSerializer.DeserializeAsync<PendingNotesIntegrity>(stream, JsonOptions, token).ConfigureAwait(false);
            if (intent is null || !intent.IntegrityRequired || pending is null || pending.SchemaVersion != 1 ||
                pending.OperationId != intent.OperationId || pending.Manifest is null || pending.Manifest.Version != 1 ||
                pending.Manifest.DocumentId != id || pending.Manifest.DocumentVersion != intent.Intended.DocumentVersion ||
                pending.Manifest.SizeBytes != intent.Intended.SizeBytes || pending.Manifest.Sha256 != intent.Intended.Sha256 ||
                pending.Manifest.CreatedAt != intent.SavedAt)
                throw new PendingRecoveryException("The pending Notes integrity intent is invalid or mismatched and was preserved.");
            return pending;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is not PendingRecoveryException && error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { throw new PendingRecoveryException("The pending Notes integrity intent could not be verified and was preserved.", error); }
    }
    private async Task MarkPublicationAttemptedAsync(NotesPublicationIntent intent, CancellationToken token)
    {
        var path = PublicationPath(intent.DocumentId);
        var temporary = path + ".tmp-" + intent.OperationId.ToString("N");
        try
        {
            await WriteJsonDurablyAsync(temporary, intent with { Attempted = true }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Replace(temporary, path, null, ignoreMetadataErrors: true);
        }
        finally { TryDelete(temporary); }
    }
    private async Task<NotesCurrentSnapshot> InspectCurrentCoreAsync(Guid id, CancellationToken token, bool applyPendingRecovery = false)
    {
        try
        {
            var result = await InspectFileCoreAsync(CurrentPath(id), id, token).ConfigureAwait(false);
            if (applyPendingRecovery)
                await ReadAndValidateAsync(CurrentPath(id), id, token, applyPendingRecovery: true).ConfigureAwait(false);
            return result;
        }
        catch (FileNotFoundException) { return new(null, string.Empty, 0); }
        catch (DirectoryNotFoundException) { return new(null, string.Empty, 0); }
    }
    private async Task<NotesCurrentSnapshot> InspectFileCoreAsync(string path, Guid id, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<NotesDocument>(stream, JsonOptions, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The Notes document was empty.");
        if (document.Id != id) throw new InvalidDataException("The Notes file does not belong to its canonical document identity.");
        var validation = validator.Validate(document);
        if (!validation.IsValid) throw new InvalidDataException("The observed Notes document failed validation.");
        stream.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
        return new(document, hash, stream.Length);
    }

    private async Task<FileStream> AcquireRepositoryLeaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(_repositoryLock)!);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream? lease = null;
            try
            {
                lease = new FileStream(_repositoryLock, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.Asynchronous);
                // Some Unix/network filesystems can accept an open without enforcing flock.
                // Refuse them rather than treating an unenforced handle as a transaction fence.
                try
                {
                    using var unenforced = new FileStream(_repositoryLock, FileMode.Open, FileAccess.ReadWrite,
                        FileShare.None, 1, FileOptions.Asynchronous);
                    throw new IOException("Notes storage cannot enforce its exclusive repository lock.");
                }
                catch (IOException error) when (IsRepositoryLockContention(error)) { }
                cancellationToken.ThrowIfCancellationRequested();
                return lease;
            }
            catch (IOException error) when (IsRepositoryLockContention(error))
            {
                lease?.Dispose();
                if (elapsed.Elapsed >= RepositoryLockTimeout)
                    throw new IOException("Notes storage is busy in another repository operation.", new TimeoutException("The bounded repository lock wait expired.", error));
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lease?.Dispose();
                throw;
            }
        }
    }

    private static bool IsRepositoryLockContention(IOException error)
    {
        var code = error.HResult & 0xffff;
        return OperatingSystem.IsWindows() ? code is 32 or 33
            : OperatingSystem.IsMacOS() ? code == 35
            : (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) && code == 11;
    }

    /// <summary>
    /// Performs preserve previous version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task PreservePreviousVersionAsync(Guid documentId, string currentPath, long version, string reason, CancellationToken cancellationToken)
    {
        if (version <= 0 || !File.Exists(currentPath)) return;
        var createdAt = File.GetLastWriteTimeUtc(currentPath);
        var versionId = VersionFileName(version, createdAt);
        var path = Path.Combine(VersionsDirectory(documentId), versionId + ".haven-notes.json");
        if (!File.Exists(path)) await CopyDurablyAsync(currentPath, path, cancellationToken).ConfigureAwait(false);
        var hash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        var meta = Path.Combine(VersionsDirectory(documentId), versionId + ".meta.json");
        if (!File.Exists(meta))
            await WriteJsonDurablyAsync(meta, new NotesVersionManifest(version, createdAt, NormalizeReason(reason), new FileInfo(path).Length, hash), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves versions core async for the current operation.
    /// </summary>
    private async Task<IReadOnlyList<NotesVersionInfo>> GetVersionsCoreAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var directory = VersionsDirectory(documentId);
        if (!Directory.Exists(directory)) return [];
        var result = new List<NotesVersionInfo>();
        foreach (var meta in Directory.EnumerateFiles(directory, "*.meta.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(meta);
                var manifest = await JsonSerializer.DeserializeAsync<NotesVersionManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (manifest is null) continue;
                result.Add(new NotesVersionInfo(Path.GetFileName(meta)[..^".meta.json".Length], manifest.Version, manifest.CreatedAt, manifest.Reason, manifest.SizeBytes, manifest.Sha256));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result;
    }

    /// <summary>
    /// Performs the apply retention step owned by this component.
    /// </summary>
    private void ApplyRetention(string versionsDirectory)
    {
        var files = Directory.EnumerateFiles(versionsDirectory, "*.haven-notes.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        foreach (var file in files.Skip(MaximumVersionsPerDocument))
        {
            TryDelete(file);
            TryDelete(Path.Combine(versionsDirectory, Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file)) + ".meta.json"));
        }
        foreach (var temporary in Directory.EnumerateFiles(versionsDirectory, "*.tmp-*", SearchOption.TopDirectoryOnly)) TryDelete(temporary);
    }

    /// <summary>
    /// Performs the summarize step owned by this component.
    /// </summary>
    private static NotesDocumentSummary Summarize(NotesDocument document)
    {
        var blocks = document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).ToArray();
        return new NotesDocumentSummary(
            document.Id,
            document.Title,
            document.UpdatedAt,
            document.Version,
            document.Sections.Count,
            blocks.Length,
            NotesTextStatistics.Calculate(document).Words,
            document.Recovery.HasUnsavedRecovery);
    }

    /// <summary>
    /// Performs the block search text step owned by this component.
    /// </summary>
    private static string BlockSearchText(NotesBlock block)
    {
        var builder = new StringBuilder(block.PlainText);
        if (block.List is not null) foreach (var item in block.List.Items) builder.AppendLine(item.Text);
        if (block.Table is not null) foreach (var cell in block.Table.Rows.SelectMany(row => row.Cells)) builder.AppendLine(cell.Text);
        if (block.Media is not null) builder.AppendLine(block.Media.Caption).AppendLine(block.Media.AltText);
        if (block.Equation is not null) builder.AppendLine(block.Equation.Source).AppendLine(block.Equation.AccessibleAlternative);
        if (block.Html is not null) builder.AppendLine(block.Html.FallbackText).AppendLine(block.Html.HtmlSource);
        if (block.Flashcard is not null) builder.AppendLine(block.Flashcard.Front).AppendLine(block.Flashcard.Back).AppendLine(block.Flashcard.Hint);
        return builder.ToString();
    }

    /// <summary>
    /// Performs the snippet step owned by this component.
    /// </summary>
    private static string Snippet(string text, int offset, int length)
    {
        var start = Math.Max(0, offset - 70);
        var end = Math.Min(text.Length, offset + length + 110);
        return (start > 0 ? "…" : string.Empty) + text[start..end].ReplaceLineEndings(" ") + (end < text.Length ? "…" : string.Empty);
    }

    /// <summary>
    /// Performs the document directory step owned by this component.
    /// </summary>
    private string DocumentDirectory(Guid id) => Path.Combine(_root, id.ToString("D"));
    /// <summary>
    /// Performs the current path step owned by this component.
    /// </summary>
    private string CurrentPath(Guid id) => Path.Combine(DocumentDirectory(id), "current.haven-notes.json");
    /// <summary>
    /// Performs the backup path step owned by this component.
    /// </summary>
    private string BackupPath(Guid id) => Path.Combine(DocumentDirectory(id), "backup.haven-notes.json");
    /// <summary>
    /// Performs the versions directory step owned by this component.
    /// </summary>
    private string VersionsDirectory(Guid id) => Path.Combine(DocumentDirectory(id), "Versions");
    /// <summary>
    /// Performs the version file name step owned by this component.
    /// </summary>
    private static string VersionFileName(long version, DateTimeOffset createdAt) => $"v{version:D10}-{createdAt.UtcDateTime:yyyyMMdd-HHmmssfff}";
    /// <summary>
    /// Performs the normalize reason step owned by this component.
    /// </summary>
    private static string NormalizeReason(string reason) => string.IsNullOrWhiteSpace(reason) ? "Save" : reason.Trim()[..Math.Min(reason.Trim().Length, 160)];

    private static async Task WriteJsonDurablyAsync<T>(string path, T value, CancellationToken cancellationToken, bool overwrite = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Performs copy durably asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task CopyDurablyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Performs compute sha256 asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    /// <summary>
    /// Performs the quarantine step owned by this component.
    /// </summary>
    private static void Quarantine(string path, string reason)
    {
        if (!File.Exists(path)) return;
        var destination = path + "." + reason + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        File.Move(path, destination);
    }

    /// <summary>
    /// Attempts to delete and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Represents notes version manifest and keeps its related state and behavior together.
    /// </summary>
    private sealed record NotesVersionManifest(long Version, DateTimeOffset CreatedAt, string Reason, long SizeBytes, string Sha256);
}

/// <summary>
/// Represents notes attachment store and keeps its related state and behavior together.
/// </summary>
public sealed class NotesAttachmentStore(IAppPaths paths, IProductionDiagnostics diagnostics) : INotesAttachmentStore
{
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(paths.DataDirectory, "Notes", "Attachments");
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Performs import asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesMediaData> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("The selected attachment does not exist.", sourcePath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            var id = Guid.NewGuid();
            var extension = Path.GetExtension(sourcePath);
            var fileName = id.ToString("N") + (extension.Length <= 12 ? extension.ToLowerInvariant() : string.Empty);
            var destination = Path.Combine(_root, fileName);
            var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                // Close both handles before the atomic rename. Windows correctly
                // refuses to move a file still held by our own FileShare.None stream.
                await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
                File.Move(temporary, destination);
                var hash = await ComputeAsync(destination, cancellationToken).ConfigureAwait(false);
                return new NotesMediaData
                {
                    AttachmentId = id,
                    OriginalName = Path.GetFileName(sourcePath),
                    StoredPath = fileName,
                    MediaType = GuessMediaType(extension),
                    SizeBytes = new FileInfo(destination).Length,
                    Sha256 = hash,
                    AltText = Path.GetFileNameWithoutExtension(sourcePath)
                };
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs resolve path asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> ResolvePathAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_root)) throw new FileNotFoundException("The Notes attachment store does not exist.");
        var prefix = attachmentId.ToString("N");
        var match = Directory.EnumerateFiles(_root, prefix + ".*", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return match is null ? throw new FileNotFoundException("The Notes attachment was not found.") : Task.FromResult(match);
    }

    /// <summary>
    /// Performs delete unreferenced asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteUnreferencedAsync(IReadOnlyCollection<Guid> referencedAttachmentIds, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_root)) return;
            var keep = referencedAttachmentIds.Select(id => id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stem = Path.GetFileNameWithoutExtension(path);
                if (keep.Contains(stem)) continue;
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await diagnostics.WriteAsync(ReliabilitySeverity.Warning, "notes", "attachment-cleanup-failed", "An unreferenced Notes attachment could not be removed.", cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs compute asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<string> ComputeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    /// <summary>
    /// Performs the guess media type step owned by this component.
    /// </summary>
    private static string GuessMediaType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}
