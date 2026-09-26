using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        CancellationToken cancellationToken = default) where T : DenRecord =>
        Store.SaveAsync(record, expectedRevision, operationId, AccessPolicy, PrincipalId, cancellationToken);

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
        if (entry.Provenance is MemoryProvenanceKind.Inference && !explicitUserAction)
        {
            var policy = await GetMemoryPolicyAsync(entry.NamespaceId, cancellationToken);
            if (!ShouldAutomaticallyRemember(policy.Frequency, durableSignalScore, independentMentions))
                throw new DenException(DenErrorCode.RetentionBlocked, "The interaction does not meet the configured Memory Frequency threshold.", recoverable: true);
        }
        var existing = await GetAsync<MemoryEntry>(entry.NamespaceId, entry.Id, cancellationToken);
        if (existing?.Locked == true) throw new DenException(DenErrorCode.Forbidden, "This memory is locked against edits.");
        var sanitized = entry with { Content = RejectSecrets(entry.Content) };
        if (sanitized.StructuredValue is { } structured) RejectSecrets(structured.GetRawText());
        return await SaveAsync(sanitized, existing?.Revision ?? 0, operationId, cancellationToken);
    }

    public async Task<MemoryEntry> CorrectMemoryAsync(string namespaceId, string id, string replacementContent,
        string operationId, string? scopeId = null, CancellationToken cancellationToken = default)
    {
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
        return await SaveAsync(corrected, 0, operationId, cancellationToken);
    }

    public async Task<MemoryEntry> SoftDeleteMemoryAsync(string namespaceId, string id, long expectedRevision,
        string operationId, CancellationToken cancellationToken = default)
    {
        var entry = await GetAsync<MemoryEntry>(namespaceId, id, cancellationToken)
            ?? throw new DenException(DenErrorCode.NotFound, "The memory entry does not exist.");
        return await SaveAsync(entry with { Retention = MemoryRetentionState.SoftDeleted, IsTombstone = true },
            expectedRevision, operationId, cancellationToken);
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

    public async Task<IReadOnlyList<DenRecord>> ImportAsync(ReadOnlyMemory<byte> bytes, string operationPrefix,
        IReadOnlyDictionary<string, long>? expectedRevisions = null, CancellationToken cancellationToken = default)
    {
        if (bytes.Length is 0 or > 64 * 1024 * 1024) throw new DenException(DenErrorCode.InvalidArchive, "The archive is empty or too large.");
        PortableDenArchive archive;
        try { archive = JsonSerializer.Deserialize<PortableDenArchive>(bytes.Span, DenJson.Options) ?? throw new JsonException("Empty archive."); }
        catch (JsonException ex) { throw new DenException(DenErrorCode.InvalidArchive, $"The archive is invalid: {ex.Message}", recoverable: true); }
        if (archive.FormatVersion != 1 || archive.Manifest.FormatVersion != 1 || archive.Records.Count > 100_000)
            throw new DenException(DenErrorCode.InvalidArchive, "The archive version or record count is unsupported.");
        var allowedNamespaces = archive.Manifest.Namespaces.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        if (archive.Records.Any(r => !allowedNamespaces.Contains(r.NamespaceId))) throw new DenException(DenErrorCode.InvalidArchive, "A record references an undeclared namespace.");
        RejectSecrets(Encoding.UTF8.GetString(bytes.Span));
        var results = new List<DenRecord>();
        foreach (var group in archive.Records.GroupBy(r => r.NamespaceId, StringComparer.Ordinal))
        {
            foreach (var record in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await AccessPolicy.IsAllowedAsync(PrincipalId, record.NamespaceId, record.Id, DenPermission.Write, cancellationToken))
                    throw new DenException(DenErrorCode.Forbidden, "The principal cannot import one or more archive objects.");
                var current = await Store.GetAsync<DenRecord>(record.NamespaceId, record.Id, AccessPolicy, PrincipalId, cancellationToken);
                var operation = operationPrefix + "-" + Hash(Encoding.UTF8.GetBytes(record.NamespaceId + "/" + record.Id))[..16];
                if (current is not null && expectedRevisions is null)
                {
                    var localJson = JsonSerializer.Serialize(current, DenJson.Options);
                    var importedJson = JsonSerializer.Serialize(record, DenJson.Options);
                    if (localJson == importedJson) { results.Add(current); continue; }
                    var conflict = new ConflictRecord
                    {
                        Id = Guid.NewGuid().ToString("D"), NamespaceId = record.NamespaceId,
                        ObjectId = record.Id, BaseRevision = current.Revision,
                        LocalVersionJson = localJson, RemoteVersionJson = importedJson
                    };
                    results.Add(await Store.SaveAsync(conflict, 0, operation + "-conflict", AccessPolicy, PrincipalId, cancellationToken));
                    continue;
                }
                var expected = expectedRevisions?.GetValueOrDefault(record.NamespaceId + "/" + record.Id) ?? current?.Revision ?? 0;
                results.Add(await Store.SaveAsync(record, expected, operation, AccessPolicy, PrincipalId, cancellationToken));
            }
        }
        return results;
    }

    public Task<byte[]> BackupAsync(IEnumerable<string> namespaceIds, CancellationToken cancellationToken = default) =>
        ExportAsync(namespaceIds, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<DenRecord>> RestoreAsync(ReadOnlyMemory<byte> backup, string operationPrefix,
        CancellationToken cancellationToken = default) => await ImportAsync(backup, operationPrefix, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<MemoryEntry>> ApplyRetentionAsync(string namespaceId, DateTimeOffset nowUtc,
        string operationPrefix, CancellationToken cancellationToken = default)
    {
        var policy = await GetMemoryPolicyAsync(namespaceId, cancellationToken);
        var changed = new List<MemoryEntry>();
        foreach (var entry in await ListAsync<MemoryEntry>(namespaceId, cancellationToken))
        {
            if (entry.Retention != MemoryRetentionState.Active || entry.RetainUntilUtc is null || entry.RetainUntilUtc > nowUtc) continue;
            var next = entry with { Retention = MemoryRetentionState.Expired, IsTombstone = true };
            changed.Add(await SaveAsync(next, entry.Revision, operationPrefix + "-" + entry.Id, cancellationToken));
        }
        _ = policy;
        return changed;
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

    private static string RejectSecrets(string value)
    {
        if (SecretMaterial.IsMatch(value) || Regex.IsMatch(value, "(?i)\"(?:api[_-]?key|password|access[_-]?token|refresh[_-]?token|private[_-]?key)\"\\s*:\\s*\"[^\"]+\""))
            throw new DenException(DenErrorCode.SecretMaterialRejected, "Plaintext credential-like data cannot be stored, imported or exported.");
        return value;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
