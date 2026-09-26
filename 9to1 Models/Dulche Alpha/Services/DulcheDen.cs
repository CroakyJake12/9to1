using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NineToOne.Dulche.Den;

public sealed class DulcheDen(DenStore store, IDenAccessPolicy access, string principalId)
{
    private static readonly Regex SecretMaterial = new(
        "(?i)(-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\\bBearer\\s+[A-Za-z0-9._~+/=-]{16,}|\\bsk-[A-Za-z0-9_-]{20,}|\\bAKIA[0-9A-Z]{16}\\b)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public DenStore Store { get; } = store;
    public string PrincipalId { get; } = principalId;
    public IDenAccessPolicy AccessPolicy { get; } = access;

    public Task<T?> GetAsync<T>(string namespaceId, string id, CancellationToken cancellationToken = default) where T : DenRecord =>
        Store.GetAsync<T>(namespaceId, id, AccessPolicy, PrincipalId, cancellationToken);

    public Task<IReadOnlyList<T>> ListAsync<T>(string namespaceId, CancellationToken cancellationToken = default) where T : DenRecord =>
        Store.ListAsync<T>(namespaceId, AccessPolicy, PrincipalId, cancellationToken);

    public Task<T> SaveAsync<T>(T record, long expectedRevision, string operationId,
        CancellationToken cancellationToken = default) where T : DenRecord
    {
        if (record is MemoryEntry or BlobReferenceRecord or StorageQuotaRecord)
            throw new DenException(DenErrorCode.Forbidden, "This record type must use its policy- and quota-enforcing Den operation.");
        RejectSecrets(JsonSerializer.Serialize(record, DenJson.Options));
        return Store.SaveAsync(record, expectedRevision, operationId, AccessPolicy, PrincipalId, cancellationToken);
    }

    public Task<DenManifest> CreateNamespaceAsync(string id, string kind, bool sharedOptIn,
        long expectedManifestRevision, string operationId, CancellationToken cancellationToken = default) =>
        Store.CreateNamespaceAsync(new DenNamespace(id, kind, sharedOptIn), expectedManifestRevision,
            operationId, AccessPolicy, PrincipalId, cancellationToken);

    public Task<DenManifest> SetNamespaceSharingAsync(string namespaceId, bool shared,
        long expectedManifestRevision, string operationId, CancellationToken cancellationToken = default) =>
        Store.SetNamespaceSharingAsync(namespaceId, shared, expectedManifestRevision, operationId,
            AccessPolicy, PrincipalId, cancellationToken);

    public async Task<MessageRevisionRecord> AddMessageRevisionAsync(MessageRevisionRecord revision,
        string operationId, CancellationToken cancellationToken = default)
    {
        RejectSecrets(JsonSerializer.Serialize(revision, DenJson.Options));
        if (revision.Revision != 1) throw new DenException(DenErrorCode.InvalidRecord, "A new message revision must have a stable ID and start at revision 1.");
        return await SaveAsync(revision, 0, operationId, cancellationToken);
    }

    public async Task<bool> IsActionCompletedAsync(string namespaceId, string actionId,
        CancellationToken cancellationToken = default)
    {
        var action = await GetAsync<ToolActionRecord>(namespaceId, actionId, cancellationToken);
        if (action is null) return false;
        return action.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
            action.Status.Equals("succeeded", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanResumeCheckpoint(CheckpointRecord checkpoint, string backendId,
        string modelId, string compatibilityVersion, out IReadOnlyList<string> actionsNotToRepeat)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        actionsNotToRepeat = checkpoint.CompletedActionIds.Distinct(StringComparer.Ordinal).ToArray();
        return string.Equals(checkpoint.BackendId, backendId, StringComparison.Ordinal) &&
            string.Equals(checkpoint.ModelId, modelId, StringComparison.Ordinal) &&
            string.Equals(checkpoint.CompatibilityVersion, compatibilityVersion, StringComparison.Ordinal) &&
            string.Equals(checkpoint.ContinuationKind, "portable-continuation", StringComparison.Ordinal);
    }

    public async Task<ModelRecord?> GetModelByID(string id, string namespaceId = "personal", CancellationToken cancellationToken = default) =>
        await GetAsync<ModelRecord>(namespaceId, id, cancellationToken);

    public async Task<IReadOnlyList<ModelRecord>> ListModelsByName(string name, string namespaceId = "personal", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var all = await ListAsync<ModelRecord>(namespaceId, cancellationToken);
        return all.Where(m => string.Equals(m.DisplayName, name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<ModelRecord>> FindModelsByAliasAsync(string alias, string namespaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        return (await ListAsync<ModelRecord>(namespaceId, cancellationToken))
            .Where(m => m.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            .OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
    }

    public async Task<ModelRecord> ReplaceModelAsync(ModelRecord replacement, long expectedRevision,
        IReadOnlyList<string> resetSettings, string operationId, CancellationToken cancellationToken = default)
    {
        var prior = await GetModelByID(replacement.Id, replacement.NamespaceId, cancellationToken)
            ?? throw new DenException(DenErrorCode.NotFound, "The model to replace does not exist.");
        if (prior.Revision != expectedRevision) throw new DenException(DenErrorCode.Conflict, "The model changed before replacement.", recoverable: true, retryable: true);
        var history = prior.ReplacementHistory.Append(new ModelReplacement(prior.ArtifactRevision,
            replacement.ArtifactRevision, DateTimeOffset.UtcNow,
            prior.ConfigurationOverrides?.GetRawText(), resetSettings.ToArray())).ToArray();
        var updated = replacement with { ReplacementHistory = history };
        return await SaveAsync(updated, expectedRevision, operationId, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryEntry>> SearchMemoryAsync(DenSearchQuery query,
        string? requestingApp = null, string? requestingAgent = null, CancellationToken cancellationToken = default)
    {
        if (query.Limit is < 1 or > 500) throw new DenException(DenErrorCode.InvalidRecord, "Search limit must be from 1 to 500.");
        var normalized = Tokens(query.Query);
        if (normalized.Length == 0) return [];
        var entries = await ListAsync<MemoryEntry>(query.NamespaceId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var shadowedIds = entries.Where(e => e.Retention == MemoryRetentionState.Active && !e.IsTombstone)
            .Select(e => e.SupersedesId).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        return entries.Where(e => e.Retention == MemoryRetentionState.Active && !e.IsTombstone &&
                (e.RetainUntilUtc is null || e.RetainUntilUtc > now) && !shadowedIds.Contains(e.Id) &&
                ScopeMatches(e, query, requestingApp, requestingAgent))
            .Select(e => (Entry: e, Score: Score(e, normalized)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Entry.UpdatedAtUtc).ThenBy(x => x.Entry.Id, StringComparer.Ordinal)
            .Take(query.Limit).Select(x => x.Entry).ToArray();
    }

    public async Task<MemoryEntry> RememberAsync(MemoryEntry entry, bool temporaryChat, bool explicitUserAction,
        int durableSignalScore, int independentMentions, string operationId, CancellationToken cancellationToken = default)
    {
        if (temporaryChat) throw new DenException(DenErrorCode.TemporaryChatMemoryWriteBlocked,
            "Persistent Memory cannot be written from a Temporary Chat.");
        if (!explicitUserAction)
        {
            var policy = await GetMemoryPolicyAsync(entry.NamespaceId, cancellationToken);
            if (!ShouldAutomaticallyRemember(policy.Frequency, durableSignalScore, independentMentions))
                throw new DenException(DenErrorCode.RetentionBlocked, "The interaction does not meet the configured Memory Frequency threshold.", recoverable: true);
        }
        if (explicitUserAction && entry.Provenance == MemoryProvenanceKind.Inference)
            entry = entry with { Provenance = MemoryProvenanceKind.UserEdited, UserEdited = true };
        if (!explicitUserAction && entry.Provenance == MemoryProvenanceKind.Inference)
        {
            var duplicate = (await ListAsync<MemoryEntry>(entry.NamespaceId, cancellationToken)).FirstOrDefault(candidate =>
                candidate.Retention == MemoryRetentionState.Active && !candidate.IsTombstone &&
                candidate.Category.Equals(entry.Category, StringComparison.OrdinalIgnoreCase) &&
                candidate.ScopeKind == entry.ScopeKind && candidate.ScopeId == entry.ScopeId &&
                candidate.SubjectEntity == entry.SubjectEntity &&
                string.Equals(Normalize(candidate.Content), Normalize(entry.Content), StringComparison.Ordinal));
            if (duplicate is not null)
                entry = duplicate with { Confidence = Math.Max(duplicate.Confidence ?? 0, entry.Confidence ?? 0), SourceId = entry.SourceId ?? duplicate.SourceId };
        }
        var existing = await GetAsync<MemoryEntry>(entry.NamespaceId, entry.Id, cancellationToken);
        if (existing?.Locked == true) throw new DenException(DenErrorCode.Forbidden, "This memory is locked against edits.");
        var retentionPolicy = await GetMemoryPolicyAsync(entry.NamespaceId, cancellationToken);
        var days = retentionPolicy.RetentionDaysByCategory.TryGetValue(entry.Category, out var configuredDays)
            ? configuredDays : retentionPolicy.RetentionDaysByCategory.GetValueOrDefault("default", 365);
        var sanitized = entry with
        {
            Content = RejectSecrets(entry.Content),
            RetainUntilUtc = entry.RetainUntilUtc ?? (days is null ? null : DateTimeOffset.UtcNow.AddDays(days.Value))
        };
        if (sanitized.StructuredValue is { } structured) RejectSecrets(structured.GetRawText());
        return await SaveMemoryEntryAsync(sanitized, existing?.Revision ?? 0, operationId, cancellationToken);
    }

    public async Task<MemoryEntry> CorrectMemoryAsync(string namespaceId, string id, string replacementContent,
        string operationId, string? scopeId = null, bool temporaryChat = false, CancellationToken cancellationToken = default)
    {
        if (temporaryChat) throw new DenException(DenErrorCode.TemporaryChatMemoryWriteBlocked, "Persistent Memory cannot be changed from a Temporary Chat.");
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementContent);
        var previous = await GetAsync<MemoryEntry>(namespaceId, id, cancellationToken)
            ?? throw new DenException(DenErrorCode.NotFound, "The memory entry does not exist.");
        if (previous.Locked) throw new DenException(DenErrorCode.Forbidden, "This memory is locked against edits.");
        var corrected = previous with
        {
            Id = Guid.NewGuid().ToString("D"), Content = RejectSecrets(replacementContent),
            Provenance = MemoryProvenanceKind.ExplicitUserStatement, UserEdited = true,
            SupersedesId = previous.Id, SupersededByIds = [], Revision = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
            ScopeId = scopeId ?? previous.ScopeId, Retention = MemoryRetentionState.Active, IsTombstone = false
        };
        return await SaveMemoryEntryAsync(corrected, 0, operationId, cancellationToken);
    }

    public async Task<MemoryEntry> SoftDeleteMemoryAsync(string namespaceId, string id, long expectedRevision,
        string operationId, CancellationToken cancellationToken = default)
    {
        var entry = await GetAsync<MemoryEntry>(namespaceId, id, cancellationToken)
            ?? throw new DenException(DenErrorCode.NotFound, "The memory entry does not exist.");
        return await SaveMemoryEntryAsync(entry with { Retention = MemoryRetentionState.SoftDeleted, IsTombstone = true },
            expectedRevision, operationId, cancellationToken);
    }

    public async Task<MemoryEntry> RestoreMemoryAsync(string namespaceId, string id, long expectedRevision,
        string operationId, CancellationToken cancellationToken = default)
    {
        var entry = await GetAsync<MemoryEntry>(namespaceId, id, cancellationToken)
            ?? throw new DenException(DenErrorCode.NotFound, "The memory entry does not exist.");
        if (entry.Retention is not (MemoryRetentionState.SoftDeleted or MemoryRetentionState.Expired))
            throw new DenException(DenErrorCode.Conflict, "Only a soft-deleted or expired memory can be restored.", recoverable: true);
        if (entry.RetainUntilUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
            throw new DenException(DenErrorCode.RetentionBlocked, "This memory has passed its retention expiry and cannot be restored.");
        return await SaveMemoryEntryAsync(entry with { Retention = MemoryRetentionState.Active, IsTombstone = false }, expectedRevision, operationId, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryEntry>> InspectMemoryAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        var entries = await ListAsync<MemoryEntry>(namespaceId, cancellationToken);
        var reverse = entries.Where(e => e.SupersedesId is not null).GroupBy(e => e.SupersedesId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(e => e.Id).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        return entries.Select(entry => reverse.TryGetValue(entry.Id, out var replacements)
            ? entry with { SupersededByIds = replacements }
            : entry).OrderBy(e => e.CreatedAtUtc).ToArray();
    }

    public async Task<IReadOnlyList<DenRecord>> SyncWithAsync(DulcheDen peer, IEnumerable<string> namespaceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peer);
        var results = new List<DenRecord>();
        foreach (var namespaceId in namespaceIds.Distinct(StringComparer.Ordinal))
        {
            var localNamespace = Store.Manifest.Namespaces.FirstOrDefault(n => n.Id == namespaceId);
            var remoteNamespace = peer.Store.Manifest.Namespaces.FirstOrDefault(n => n.Id == namespaceId);
            if (localNamespace is null || remoteNamespace is null) throw new DenException(DenErrorCode.NotFound, "Both Dens must declare the selected sync namespace.");
            if (!localNamespace.Shared || !remoteNamespace.Shared) throw new DenException(DenErrorCode.Forbidden, "Namespace sync is disabled unless both Den namespaces are explicitly shared.");
            var left = await Store.ListAllPermittedAsync(namespaceId, AccessPolicy, PrincipalId, cancellationToken);
            var right = await peer.Store.ListAllPermittedAsync(namespaceId, peer.AccessPolicy, peer.PrincipalId, cancellationToken);
            var leftById = left.ToDictionary(record => record.Id, StringComparer.Ordinal);
            var rightById = right.ToDictionary(record => record.Id, StringComparer.Ordinal);
            foreach (var remote in right)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!leftById.TryGetValue(remote.Id, out var local))
                {
                    results.Add(await Store.SaveAsync(remote, 0, SyncOperation(namespaceId, remote.Id, remote.Revision), AccessPolicy, PrincipalId, cancellationToken, preserveOrigin: true));
                    continue;
                }
                if (ContentFingerprint(local) == ContentFingerprint(remote)) continue;
                if (local.Revision > remote.Revision && ContentFingerprint(await Store.ReadRevisionAsync(namespaceId, local.Id, remote.Revision, AccessPolicy, PrincipalId, cancellationToken) ?? local) == ContentFingerprint(remote))
                {
                    _ = await peer.Store.SaveAsync(local, remote.Revision, SyncOperation(namespaceId, local.Id, local.Revision), peer.AccessPolicy, peer.PrincipalId, cancellationToken, preserveOrigin: true);
                    results.Add(local);
                    continue;
                }
                if (remote.Revision > local.Revision && ContentFingerprint(await peer.Store.ReadRevisionAsync(namespaceId, remote.Id, local.Revision, peer.AccessPolicy, peer.PrincipalId, cancellationToken) ?? remote) == ContentFingerprint(local))
                {
                    var caughtUp = await Store.SaveAsync(remote, local.Revision, SyncOperation(namespaceId, remote.Id, remote.Revision), AccessPolicy, PrincipalId, cancellationToken, preserveOrigin: true);
                    results.Add(caughtUp);
                    continue;
                }
                var merged = await TryMergeDisjointAsync(this, peer, namespaceId, local, remote, cancellationToken);
                if (merged is not null)
                {
                    var mergeOperation = SyncOperation(namespaceId, local.Id, long.Parse(ContentFingerprint(merged)[..15], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
                    var savedLocal = await Store.SaveAsync(merged, local.Revision, mergeOperation, AccessPolicy, PrincipalId, cancellationToken);
                    _ = await peer.Store.SaveAsync(merged, remote.Revision, mergeOperation, peer.AccessPolicy, peer.PrincipalId, cancellationToken);
                    results.Add(savedLocal);
                    continue;
                }
                results.Add(await EnsureConflictAsync(this, peer, namespaceId, local, remote, cancellationToken));
            }
            foreach (var local in left)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!rightById.ContainsKey(local.Id))
                    _ = await peer.Store.SaveAsync(local, 0, SyncOperation(namespaceId, local.Id, local.Revision), peer.AccessPolicy, peer.PrincipalId, cancellationToken, preserveOrigin: true);
            }
        }
        return results;
    }

    public async Task<MemoryPolicyRecord> SetMemoryRetentionAsync(string namespaceId, string category,
        int? retentionDays, string operationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (retentionDays is < 0) throw new DenException(DenErrorCode.InvalidRecord, "Retention days must be nonnegative or explicitly null.");
        var current = await GetMemoryPolicyAsync(namespaceId, cancellationToken);
        var values = new Dictionary<string, int?>(current.RetentionDaysByCategory, StringComparer.OrdinalIgnoreCase) { [category] = retentionDays };
        return await SaveAsync(current with { RetentionDaysByCategory = values }, current.Revision, operationId, cancellationToken);
    }

    public async Task PurgeMemoryAsync(string namespaceId, string id, string operationId,
        bool explicitlyConfirmed, CancellationToken cancellationToken = default)
    {
        if (!explicitlyConfirmed) throw new DenException(DenErrorCode.PurgeConfirmationRequired, "Permanent purge requires explicit confirmation.");
        if (!await AccessPolicy.IsAllowedAsync(PrincipalId, namespaceId, id, DenPermission.Administer, cancellationToken))
            throw new DenException(DenErrorCode.Forbidden, "The principal cannot permanently purge this Den object.");
        await Store.PurgeAsync<MemoryEntry>(namespaceId, id, operationId, cancellationToken);
    }

    public async Task<MemoryPolicyRecord> GetMemoryPolicyAsync(string namespaceId = "personal", CancellationToken cancellationToken = default) =>
        await GetAsync<MemoryPolicyRecord>(namespaceId, "memory-frequency", cancellationToken) ??
        new MemoryPolicyRecord { Id = "memory-frequency", NamespaceId = namespaceId };

    public async Task<MemoryPolicyRecord> SetMemoryFrequencyAsync(string namespaceId, MemoryFrequency frequency,
        string operationId, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync<MemoryPolicyRecord>(namespaceId, "memory-frequency", cancellationToken);
        var updated = existing is null
            ? new MemoryPolicyRecord { Id = "memory-frequency", NamespaceId = namespaceId, Frequency = frequency }
            : existing with { Frequency = frequency };
        return await SaveAsync(updated, existing?.Revision ?? 0, operationId, cancellationToken);
    }

    public async Task<MemoryPolicyRecord> SetBackgroundLearningEnabledAsync(string namespaceId, bool enabled,
        string operationId, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync<MemoryPolicyRecord>(namespaceId, "memory-frequency", cancellationToken);
        var updated = existing is null
            ? new MemoryPolicyRecord { Id = "memory-frequency", NamespaceId = namespaceId, BackgroundLearningEnabled = enabled }
            : existing with { BackgroundLearningEnabled = enabled };
        return await SaveAsync(updated, existing?.Revision ?? 0, operationId, cancellationToken);
    }

    public static bool ShouldAutomaticallyRemember(MemoryFrequency frequency, int durableSignalScore, int independentMentions)
    {
        if (durableSignalScore is < 0 or > 100 || independentMentions < 0) throw new ArgumentOutOfRangeException(nameof(durableSignalScore));
        return frequency switch
        {
            MemoryFrequency.Never => false,
            MemoryFrequency.Sometimes => durableSignalScore >= 85 && independentMentions >= 3,
            MemoryFrequency.Often => durableSignalScore >= 65 && independentMentions >= 2,
            MemoryFrequency.Always => durableSignalScore >= 40,
            _ => false
        };
    }

    public async Task<byte[]> ExportAsync(IEnumerable<string> namespaceIds, IReadOnlySet<string>? selectedBlobIds = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = await Store.ReadPortableSnapshotAsync(namespaceIds, AccessPolicy, PrincipalId, selectedBlobIds, cancellationToken);
        RejectSecrets(Encoding.UTF8.GetString(bytes));
        return bytes;
    }

    public async Task<DenImportResult> ImportAsync(ReadOnlyMemory<byte> bytes, string operationPrefix,
        IReadOnlyDictionary<string, long>? expectedRevisions = null, bool temporaryChat = false,
        CancellationToken cancellationToken = default)
    {
        if (bytes.Length is 0 or > 64 * 1024 * 1024) throw new DenException(DenErrorCode.InvalidArchive, "The archive is empty or too large.");
        PortableDenArchive archive;
        try { archive = JsonSerializer.Deserialize<PortableDenArchive>(bytes.Span, DenJson.Options) ?? throw new JsonException("Empty archive."); }
        catch (JsonException ex) { throw new DenException(DenErrorCode.InvalidArchive, $"The archive is invalid: {ex.Message}", recoverable: true); }
        if (archive.FormatVersion != 1 || archive.Manifest.FormatVersion != 1 || archive.Records.Count > 100_000)
            throw new DenException(DenErrorCode.InvalidArchive, "The archive version or record count is unsupported.");
        if (temporaryChat && archive.Records.Any(record => record is MemoryEntry))
            throw new DenException(DenErrorCode.TemporaryChatMemoryWriteBlocked, "Persistent Memory cannot be imported from a Temporary Chat.");
        var allowedNamespaces = archive.Manifest.Namespaces.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        if (archive.Records.Any(r => !allowedNamespaces.Contains(r.NamespaceId))) throw new DenException(DenErrorCode.InvalidArchive, "A record references an undeclared namespace.");
        RejectSecrets(Encoding.UTF8.GetString(bytes.Span));
        var archiveDigest = Hash(JsonSerializer.SerializeToUtf8Bytes(archive with { ArchiveSha256 = "" }, DenJson.Options));
        if (!string.Equals(archiveDigest, archive.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new DenException(DenErrorCode.InvalidArchive, "The archive checksum does not match its contents.");
        var results = new List<DenImportItemResult>();
        foreach (var record in archive.Records)
        {
            DenStore.ValidateSegment(record.NamespaceId, "namespace");
            DenStore.ValidateSegment(record.Id, "record ID");
            if (!Store.Manifest.Namespaces.Any(ns => ns.Id == record.NamespaceId))
                throw new DenException(DenErrorCode.InvalidArchive, $"Target Den does not contain namespace '{record.NamespaceId}'.");
            RejectSecrets(JsonSerializer.Serialize(record, DenJson.Options));
            if (!await AccessPolicy.IsAllowedAsync(PrincipalId, record.NamespaceId, record.Id, DenPermission.Write, cancellationToken))
                throw new DenException(DenErrorCode.Forbidden, "The principal cannot import one or more archive objects.");
        }
        if (archive.Blobs.Select(blob => blob.Id).Distinct(StringComparer.Ordinal).Count() != archive.Blobs.Count ||
            !archive.IncludedBlobIds.ToHashSet(StringComparer.Ordinal).SetEquals(archive.Blobs.Select(blob => blob.Id)))
            throw new DenException(DenErrorCode.InvalidArchive, "The selected attachment list does not match the archive contents.");
        var verifiedBlobs = new List<(string Id, byte[] Bytes)>();
        foreach (var blob in archive.Blobs)
        {
            byte[] data;
            try { data = Convert.FromBase64String(blob.ContentBase64); }
            catch (FormatException ex) { throw new DenException(DenErrorCode.InvalidArchive, $"An attachment is not valid base64: {ex.Message}"); }
            if (data.LongLength != blob.Length || blob.Length > 32 * 1024 * 1024 || Hash(data) != blob.Sha256 || blob.Id != blob.Sha256)
                throw new DenException(DenErrorCode.InvalidArchive, "An attachment failed length or SHA-256 validation.");
            verifiedBlobs.Add((blob.Id, data));
        }
        foreach (var blob in verifiedBlobs)
        {
            var archiveReferences = archive.Records.OfType<BlobReferenceRecord>().Where(reference => !reference.Deleted && reference.Sha256 == blob.Id).ToArray();
            if (archiveReferences.Length == 0) throw new DenException(DenErrorCode.InvalidArchive, "An attachment has no authorised Den reference.");
            foreach (var namespaceGroup in archiveReferences.GroupBy(reference => reference.NamespaceId, StringComparer.Ordinal))
            {
                var ids = await Store.GetActiveBlobIdsAsync(namespaceGroup.Key, cancellationToken);
                if (ids.Contains(blob.Id)) continue;
                var used = await SumBlobBytesAsync(ids, cancellationToken);
                var quota = await GetStorageQuotaAsync(namespaceGroup.Key, cancellationToken);
                if (used + blob.Bytes.LongLength > quota.AttachmentBytes)
                    throw new DenException(DenErrorCode.RetentionBlocked, "The imported attachment would exceed the namespace quota.", recoverable: true);
            }
        }
        foreach (var (id, data) in verifiedBlobs) await Store.WriteBlobAsync(id, data, cancellationToken);

        foreach (var group in archive.Records.GroupBy(r => r.NamespaceId, StringComparer.Ordinal))
        {
            foreach (var record in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var current = await Store.GetAsync<DenRecord>(record.NamespaceId, record.Id, AccessPolicy, PrincipalId, cancellationToken);
                    var operation = operationPrefix + "-" + Hash(Encoding.UTF8.GetBytes(record.NamespaceId + "/" + record.Id))[..16];
                    if (current is not null && expectedRevisions is null)
                    {
                        var localJson = JsonSerializer.Serialize(current, DenJson.Options);
                        var importedJson = JsonSerializer.Serialize(record, DenJson.Options);
                        if (localJson == importedJson) { results.Add(new(record.NamespaceId, record.Id, "unchanged", current)); continue; }
                        var conflict = new ConflictRecord
                        {
                            Id = Hash(Encoding.UTF8.GetBytes(record.NamespaceId + "/" + record.Id + "/" + Hash(Encoding.UTF8.GetBytes(importedJson))))[..32].ToLowerInvariant(),
                            NamespaceId = record.NamespaceId, ObjectId = record.Id, BaseRevision = current.Revision,
                            LocalVersionJson = localJson, RemoteVersionJson = importedJson
                        };
                        var savedConflict = await Store.SaveAsync(conflict, 0, operation + "-conflict", AccessPolicy, PrincipalId, cancellationToken);
                        results.Add(new(record.NamespaceId, record.Id, "conflict-preserved", savedConflict));
                        continue;
                    }
                    var expected = expectedRevisions?.GetValueOrDefault(record.NamespaceId + "/" + record.Id) ?? current?.Revision ?? 0;
                    var saved = await Store.SaveAsync(record, expected, operation, AccessPolicy, PrincipalId, cancellationToken, preserveOrigin: true);
                    results.Add(new(record.NamespaceId, record.Id, "imported", saved));
                }
                catch (DenException ex) { results.Add(new(record.NamespaceId, record.Id, "failed", ErrorCode: ex.Code, Message: ex.Message, Recoverable: ex.Recoverable, Retryable: ex.Retryable)); }
            }
        }
        return new DenImportResult(results);
    }

    public Task<byte[]> BackupAsync(IEnumerable<string> namespaceIds, CancellationToken cancellationToken = default) =>
        ExportAsync(namespaceIds, cancellationToken: cancellationToken);

    public async Task<DenImportResult> RestoreAsync(ReadOnlyMemory<byte> backup, string operationPrefix,
        CancellationToken cancellationToken = default) => await ImportAsync(backup, operationPrefix, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<MemoryEntry>> ApplyRetentionAsync(string namespaceId, DateTimeOffset nowUtc,
        string operationPrefix, CancellationToken cancellationToken = default)
    {
        var policy = await GetMemoryPolicyAsync(namespaceId, cancellationToken);
        var changed = new List<MemoryEntry>();
        foreach (var entry in await ListAsync<MemoryEntry>(namespaceId, cancellationToken))
        {
            var categoryDays = policy.RetentionDaysByCategory.TryGetValue(entry.Category, out var configuredDays)
                ? configuredDays
                : policy.RetentionDaysByCategory.GetValueOrDefault("default", 365);
            var expiry = entry.RetainUntilUtc ?? (categoryDays is null ? null : entry.CreatedAtUtc.AddDays(categoryDays.Value));
            if (entry.Retention != MemoryRetentionState.Active || expiry is null || expiry > nowUtc) continue;
            var next = entry with { RetainUntilUtc = expiry, Retention = MemoryRetentionState.Expired, IsTombstone = true };
            changed.Add(await SaveMemoryEntryAsync(next, entry.Revision, operationPrefix + "-" + entry.Id, cancellationToken));
        }
        _ = policy;
        return changed;
    }

    public async Task<BlobReferenceRecord> AddAttachmentAsync(string namespaceId, string ownerId, string ownerKind,
        string mediaType, ReadOnlyMemory<byte> content, string operationId, CancellationToken cancellationToken = default)
    {
        if (content.Length is 0 or > 32 * 1024 * 1024) throw new DenException(DenErrorCode.InvalidRecord, "An attachment must be from 1 byte to 32 MiB.");
        if (string.IsNullOrWhiteSpace(mediaType) || mediaType.Length > 128 || mediaType.Contains('\r') || mediaType.Contains('\n'))
            throw new DenException(DenErrorCode.InvalidRecord, "The attachment media type is invalid.");
        DenStore.ValidateSegment(ownerId, "attachment owner ID");
        DenStore.ValidateSegment(ownerKind, "attachment owner type");
        var digest = Hash(content.Span);
        var referenceId = Hash(Encoding.UTF8.GetBytes(namespaceId + "/" + ownerKind + "/" + ownerId + "/" + digest))[..32].ToLowerInvariant();
        if (!await AccessPolicy.IsAllowedAsync(PrincipalId, namespaceId, ownerId, DenPermission.Write, cancellationToken))
            throw new DenException(DenErrorCode.Forbidden, "The principal cannot attach a blob to this owner.");
        var quota = await GetStorageQuotaAsync(namespaceId, cancellationToken);
        var references = await Store.ListBlobReferencesAsync(namespaceId, AccessPolicy, PrincipalId, cancellationToken);
        var existingHashes = await Store.GetActiveBlobIdsAsync(namespaceId, cancellationToken);
        var used = await SumBlobBytesAsync(existingHashes, cancellationToken);
        if (!existingHashes.Contains(digest) && used + content.Length > quota.AttachmentBytes)
            throw new DenException(DenErrorCode.RetentionBlocked, "The Den attachment quota would be exceeded.", recoverable: true);
        await Store.WriteBlobAsync(digest, content, cancellationToken);
        var prior = await GetAsync<BlobReferenceRecord>(namespaceId, referenceId, cancellationToken);
        var record = new BlobReferenceRecord
        {
            Id = referenceId, NamespaceId = namespaceId, Sha256 = digest, MediaType = mediaType,
            Length = content.Length, OwnerId = ownerId, OwnerKind = ownerKind,
            Deleted = false, Revision = prior?.Revision ?? 1
        };
        return await Store.SaveAsync(record, prior?.Revision ?? 0, operationId, AccessPolicy, PrincipalId, cancellationToken);
    }

    public async Task<byte[]> ReadAttachmentAsync(string namespaceId, string referenceId,
        CancellationToken cancellationToken = default)
    {
        var reference = await GetAsync<BlobReferenceRecord>(namespaceId, referenceId, cancellationToken)
            ?? throw new DenException(DenErrorCode.NotFound, "The attachment reference does not exist.");
        if (reference.Deleted) throw new DenException(DenErrorCode.NotFound, "The attachment reference was removed.", recoverable: true);
        var bytes = await Store.ReadBlobAsync(reference.Sha256, cancellationToken);
        if (bytes.LongLength != reference.Length) throw new DenException(DenErrorCode.InvalidRecord, "The attachment length does not match its reference.", recoverable: true);
        return bytes;
    }

    public async Task<BlobReferenceRecord> DeleteAttachmentReferenceAsync(string namespaceId, string referenceId,
        long expectedRevision, string operationId, CancellationToken cancellationToken = default)
    {
        var reference = await GetAsync<BlobReferenceRecord>(namespaceId, referenceId, cancellationToken)
            ?? throw new DenException(DenErrorCode.NotFound, "The attachment reference does not exist.");
        return await Store.SaveAsync(reference with { Deleted = true }, expectedRevision, operationId,
            AccessPolicy, PrincipalId, cancellationToken);
    }

    public async Task<StorageQuotaRecord> GetStorageQuotaAsync(string namespaceId = "personal", CancellationToken cancellationToken = default) =>
        await GetAsync<StorageQuotaRecord>(namespaceId, "storage-quota", cancellationToken) ??
        new StorageQuotaRecord { Id = "storage-quota", NamespaceId = namespaceId };

    public async Task<StorageQuotaRecord> SetStorageQuotaAsync(string namespaceId, StorageQuotaRecord quota,
        long expectedRevision, string operationId, CancellationToken cancellationToken = default)
    {
        if (quota.MetadataBytes < 1024 || quota.JournalBytes < 1024 || quota.CacheBytes < 0 || quota.AttachmentBytes < 0 || quota.ModelWeightBytes < 0)
            throw new DenException(DenErrorCode.InvalidRecord, "Storage quota limits are invalid.");
        if (quota.Id != "storage-quota" || quota.NamespaceId != namespaceId)
            throw new DenException(DenErrorCode.InvalidRecord, "Quota records use the stable storage-quota ID in their namespace.");
        if (!await AccessPolicy.IsAllowedAsync(PrincipalId, namespaceId, quota.Id, DenPermission.Administer, cancellationToken))
            throw new DenException(DenErrorCode.Forbidden, "Only a Den administrator can change storage quotas.");
        return await Store.SaveAsync(quota, expectedRevision, operationId, AccessPolicy, PrincipalId, cancellationToken);
    }

    public async Task<DenStorageUsage> GetStorageUsageAsync(string namespaceId = "personal", CancellationToken cancellationToken = default)
    {
        var recordsPath = Path.Combine(Store.RootPath, "records", namespaceId);
        var historyPath = Path.Combine(Store.RootPath, "history", namespaceId);
        var metadata = Directory.Exists(recordsPath) ? Directory.EnumerateFiles(recordsPath, "*.json", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) : 0;
        var history = Directory.Exists(historyPath) ? Directory.EnumerateFiles(historyPath, "*.json", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) : 0;
        var journalPath = Path.Combine(Store.RootPath, "journal");
        var journal = Directory.Exists(journalPath) ? Directory.EnumerateFiles(journalPath, "*.json").Sum(path => new FileInfo(path).Length) : 0;
        var blobIds = await Store.GetActiveBlobIdsAsync(namespaceId, cancellationToken);
        var attachments = await SumBlobBytesAsync(blobIds, cancellationToken);
        var localModels = new LocalModelRegistry(Store.RootPath, Store.Manifest.DeviceId);
        var modelWeights = await localModels.GetEnabledWeightUsageAsync(cancellationToken);
        return new DenStorageUsage(metadata, history, journal, attachments, 0, modelWeights,
            await GetStorageQuotaAsync(namespaceId, cancellationToken));
    }

    public async Task<LocalModelInstallation> RegisterLocalModelAsync(string namespaceId, string modelId,
        string absolutePath, string artifactSha256, CancellationToken cancellationToken = default)
    {
        var model = await GetModelByID(modelId, namespaceId, cancellationToken)
            ?? throw new DenException(DenErrorCode.NotFound, "The model catalogue entry does not exist.");
        if (!string.Equals(model.ArtifactSha256, artifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new DenException(DenErrorCode.InvalidRecord, "The installed artifact hash differs from the catalogue record.");
        if (!await AccessPolicy.IsAllowedAsync(PrincipalId, namespaceId, modelId, DenPermission.Write, cancellationToken))
            throw new DenException(DenErrorCode.Forbidden, "The principal cannot change this model's local installation state.");
        var quota = await GetStorageQuotaAsync(namespaceId, cancellationToken);
        var registry = new LocalModelRegistry(Store.RootPath, Store.Manifest.DeviceId, quota.ModelWeightBytes);
        return await registry.RegisterAsync(modelId, absolutePath, artifactSha256, cancellationToken);
    }

    public async Task<long> CollectUnreferencedBlobsAsync(string namespaceId, bool explicitlyConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!explicitlyConfirmed) throw new DenException(DenErrorCode.PurgeConfirmationRequired, "Blob collection requires explicit confirmation.");
        if (!await AccessPolicy.IsAllowedAsync(PrincipalId, namespaceId, "storage-quota", DenPermission.Administer, cancellationToken))
            throw new DenException(DenErrorCode.Forbidden, "Only a Den administrator can collect unreferenced attachments.");
        var references = await Store.ListBlobReferencesAsync(namespaceId, AccessPolicy, PrincipalId, cancellationToken);
        var activeHashes = references.Where(r => !r.Deleted).Select(r => r.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return await Store.DeleteUnreferencedBlobsAsync(activeHashes, cancellationToken);
    }

    private static bool ScopeMatches(MemoryEntry entry, DenSearchQuery query, string? app, string? agent) =>
        (query.ScopeKind is null || entry.ScopeKind == query.ScopeKind) &&
        (query.ScopeId is null || entry.ScopeId == query.ScopeId) &&
        (query.Category is null || string.Equals(entry.Category, query.Category, StringComparison.OrdinalIgnoreCase)) &&
        (entry.ScopeKind switch
        {
            MemoryScopeKind.User => true,
            MemoryScopeKind.Project => query.ScopeId is not null && entry.ScopeId == query.ScopeId,
            MemoryScopeKind.AppSurface => app is not null && entry.ScopeId == app,
            MemoryScopeKind.Agent => agent is not null && entry.ScopeId == agent,
            MemoryScopeKind.Team or MemoryScopeKind.Organisation or MemoryScopeKind.Custom => query.ScopeId is not null && query.ScopeId == entry.ScopeId,
            _ => false
        });

    private static double Score(MemoryEntry entry, string[] queryTokens)
    {
        var contentTokens = Tokens(entry.Content + " " + entry.Category + " " + entry.SubjectEntity);
        var unique = contentTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = queryTokens.Count(unique.Contains);
        if (matches == 0) return 0;
        var coverage = matches / (double)queryTokens.Length;
        var exact = entry.Content.Contains(string.Join(' ', queryTokens), StringComparison.OrdinalIgnoreCase) ? 0.5 : 0;
        var confidence = entry.Confidence ?? 0.5;
        return coverage + exact + Math.Clamp(confidence, 0, 1) * 0.05;
    }

    private static string[] Tokens(string? value) => Regex.Matches(value ?? "", "[\\p{L}\\p{N}]{2,}", RegexOptions.CultureInvariant)
        .Select(m => m.Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
    private static string Normalize(string value) => Regex.Replace(value.Trim(), "\\s+", " ", RegexOptions.CultureInvariant).ToLowerInvariant();

    private async Task<long> SumBlobBytesAsync(IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var id in ids) total = checked(total + await Store.GetBlobLengthAsync(id, cancellationToken));
        return total;
    }

    internal static string RejectSecrets(string value)
    {
        if (SecretMaterial.IsMatch(value) || Regex.IsMatch(value,
            "(?i)\\b(?:api[_ -]?key|password|access[_ -]?token|refresh[_ -]?token|client[_ -]?secret|private[_ -]?key)\\b\\s*(?:=|:|\\bis\\b)\\s*[\"']?[^\\s,\"'};]{6,}"))
            throw new DenException(DenErrorCode.SecretMaterialRejected, "Plaintext credential-like data cannot be stored, imported or exported.");
        return value;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string ContentFingerprint(DenRecord record)
    {
        var normalized = record with { Revision = 1, CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch, OriginDeviceId = null };
        return Hash(JsonSerializer.SerializeToUtf8Bytes<DenRecord>(normalized, DenJson.Options));
    }

    private Task<MemoryEntry> SaveMemoryEntryAsync(MemoryEntry entry, long expectedRevision,
        string operationId, CancellationToken cancellationToken) =>
        Store.SaveAsync(entry, expectedRevision, operationId, AccessPolicy, PrincipalId, cancellationToken);

    private static string SyncOperation(string namespaceId, string objectId, long revision) =>
        "sync-" + Hash(Encoding.UTF8.GetBytes(namespaceId + "/" + objectId + "/" + revision))[..40];

    private static async Task<ConflictRecord> EnsureConflictAsync(DulcheDen localDen, DulcheDen peerDen, string namespaceId,
        DenRecord left, DenRecord right, CancellationToken cancellationToken)
    {
        var ordered = new[] { left, right }.OrderBy(ContentFingerprint, StringComparer.Ordinal).ToArray();
        var id = Hash(Encoding.UTF8.GetBytes(namespaceId + "/" + left.Id + "/" + string.Join('/', ordered.Select(ContentFingerprint))))[..32].ToLowerInvariant();
        var conflict = new ConflictRecord
        {
            Id = id, NamespaceId = namespaceId, ObjectId = left.Id,
            BaseRevision = Math.Min(left.Revision, right.Revision),
            LocalVersionJson = JsonSerializer.Serialize(left, DenJson.Options),
            RemoteVersionJson = JsonSerializer.Serialize(right, DenJson.Options)
        };
        var savedLocal = conflict;
        foreach (var den in new[] { localDen, peerDen })
        {
            var existing = await den.GetAsync<ConflictRecord>(namespaceId, id, cancellationToken);
            if (existing is not null) { savedLocal = existing; continue; }
            var saved = await den.Store.SaveAsync(conflict, 0, SyncOperation(namespaceId, "conflict-" + id, 1),
                den.AccessPolicy, den.PrincipalId, cancellationToken);
            if (ReferenceEquals(den, localDen)) savedLocal = saved;
        }
        return savedLocal;
    }

    private static async Task<DenRecord?> TryMergeDisjointAsync(DulcheDen localDen, DulcheDen peerDen,
        string namespaceId, DenRecord local, DenRecord remote, CancellationToken cancellationToken)
    {
        if (local.GetType() != remote.GetType() || local.Revision != remote.Revision || local.Revision <= 1) return null;
        var baselineRevision = local.Revision - 1;
        var localBase = await localDen.Store.ReadRevisionAsync(namespaceId, local.Id, baselineRevision,
            localDen.AccessPolicy, localDen.PrincipalId, cancellationToken);
        var remoteBase = await peerDen.Store.ReadRevisionAsync(namespaceId, remote.Id, baselineRevision,
            peerDen.AccessPolicy, peerDen.PrincipalId, cancellationToken);
        if (localBase is null || remoteBase is null || ContentFingerprint(localBase) != ContentFingerprint(remoteBase)) return null;
        var baseNode = JsonNode.Parse(JsonSerializer.Serialize<DenRecord>(localBase, DenJson.Options));
        var localNode = JsonNode.Parse(JsonSerializer.Serialize<DenRecord>(local, DenJson.Options));
        var remoteNode = JsonNode.Parse(JsonSerializer.Serialize<DenRecord>(remote, DenJson.Options));
        if (!TryMergeNode(baseNode, localNode, remoteNode, out var mergedNode)) return null;
        if (mergedNode is not JsonObject mergedObject) return null;
        mergedObject["revision"] = local.Revision;
        mergedObject["updatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        mergedObject["originDeviceId"] = null;
        try { return JsonSerializer.Deserialize<DenRecord>(mergedObject.ToJsonString(), DenJson.Options); }
        catch (JsonException) { return null; }
    }

    private static bool TryMergeNode(JsonNode? baseline, JsonNode? local, JsonNode? remote, out JsonNode? merged)
    {
        if (JsonNode.DeepEquals(local, remote)) { merged = local?.DeepClone(); return true; }
        if (JsonNode.DeepEquals(local, baseline)) { merged = remote?.DeepClone(); return true; }
        if (JsonNode.DeepEquals(remote, baseline)) { merged = local?.DeepClone(); return true; }
        if (baseline is JsonObject baseObject && local is JsonObject localObject && remote is JsonObject remoteObject)
        {
            var result = new JsonObject();
            var keys = baseObject.Select(item => item.Key).Concat(localObject.Select(item => item.Key)).Concat(remoteObject.Select(item => item.Key)).Distinct(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (key is "revision" or "updatedAtUtc" or "originDeviceId")
                {
                    var chosen = localObject[key] ?? remoteObject[key] ?? baseObject[key];
                    result[key] = chosen?.DeepClone();
                    continue;
                }
                if (!TryMergeNode(baseObject[key], localObject[key], remoteObject[key], out var property)) { merged = null; return false; }
                result[key] = property;
            }
            merged = result;
            return true;
        }
        merged = null;
        return false;
    }
}
