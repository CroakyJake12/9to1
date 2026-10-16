using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using NineToOne.Dulche.Context;

namespace HavenOS.Apps.Assistants.Memory;

/// <summary>Authenticated field encryption in the SAME canonical rows. Only opaque routing
/// identity and lifecycle columns remain outside the envelope; no UCML text/provenance is indexed.</summary>
internal sealed class CanonicalUcmlRecordProtection(IProviderSecretStore secrets) : IAssistantMemoryRecordProtection
{
    internal const string Prefix = "ucml.v1:sealed:";
    private readonly ProviderSecretUcmlKeyCustody _custody = new(secrets);
    private readonly SemaphoreSlim _keyGate = new(1, 1);
    private static string ScopeId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string OwnerId(AuthenticatedResourceActor actor) => "home:" + ScopeId(JsonSerializer.Serialize(
        new { actor.ProfileId, actor.AccountId, actor.OrganisationId }));

    private async Task<UcmlKeyReference> KeyAsync(string owner, CancellationToken token)
    {
        // The head is an opaque retained key ID, not the key. Concurrent process creation
        // may leave an unused retained root, never overwrite/delete an older root. Every
        // envelope names its root so previous records remain decryptable after rotation.
        await _keyGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var name = ScopeId(owner).ToLowerInvariant();
            var head = await secrets.GetAsync("dulche.ucml.canonical", name, token).ConfigureAwait(false);
            if (head is not null)
            {
                if (!Guid.TryParseExact(head, "N", out var id) || id == Guid.Empty)
                    throw new CryptographicException("The retained canonical UCML key reference requires recovery; it will not be reset.");
                return new(owner, id); // Actual key read/validation is fail-closed in custody.
            }
            var created = await _custody.CreateKeyAsync(owner, token).ConfigureAwait(false);
            await secrets.SetAsync("dulche.ucml.canonical", name, created.KeyId.ToString("N"), token).ConfigureAwait(false);
            return created;
        }
        finally { _keyGate.Release(); }
    }

    internal async Task<string> SealPayloadAsync(string payload, Guid id, string owner, string scope, string purpose, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var document = new UcmlDocument(new UcmlMetadata
        {
            SchemaVersion = 1, Id = id, Revision = 1, Title = "Encrypted canonical UCML payload", OwnerId = owner,
            Scope = new("assistant", ScopeId(scope + "\n" + purpose)), Category = "canonical-storage",
            Classification = "private", CreatedAt = now, UpdatedAt = now, Retention = "until-revoked",
            Consent = new("explicit", ["9to1.assistants"], ["canonical-storage"], ["trusted-storage-owner"]),
            Sources = [new("canonical-owner", "Canonical storage owner", "system", null, now)], Confidence = 1
        }, payload);
        return await new UcmlProtection(_custody).SealAsync(document, owner, await KeyAsync(owner, token).ConfigureAwait(false), now, token).ConfigureAwait(false);
    }

    public async Task<KnowledgeRecord> ProtectAsync(KnowledgeRecord record, string ownerId, string storageScope, CancellationToken token)
    {
        if (!record.LearnedBecause.StartsWith("ucml.v1:", StringComparison.Ordinal) || record.LearnedBecause.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Only a validated plaintext UCML candidate can enter canonical encryption.");
        var sealedPayload = await SealPayloadAsync(JsonSerializer.Serialize(record), record.Id, ownerId, storageScope, "record", token).ConfigureAwait(false);
        return record with
        {
            Topic = "Encrypted UCML context", Title = "Encrypted UCML context", Summary = "", Confidence = 0,
            LearnedBecause = Prefix + sealedPayload, Sources = Array.Empty<KnowledgeSource>(), UserCorrection = null,
            LastConfirmedAt = null, LastReinforcedAt = null, LastUsedAt = null, SupersedesId = null
        };
    }

    public async Task<KnowledgeRecord> UnprotectAsync(KnowledgeRecord stored, string ownerId, string storageScope, CancellationToken token)
    {
        if (!stored.LearnedBecause.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("This legacy plaintext UCML record needs an explicit reviewed migration. It cannot enter context through encrypted storage.");
        var document = await new UcmlProtection(_custody).UnsealAsync(stored.LearnedBecause[Prefix.Length..], ownerId, token).ConfigureAwait(false);
        if (document.Metadata.Id != stored.Id || document.Metadata.Scope != new UcmlScope("assistant", ScopeId(storageScope + "\nrecord")))
            throw new CryptographicException("The encrypted UCML record belongs to a different canonical scope or record.");
        var record = JsonSerializer.Deserialize<KnowledgeRecord>(document.Markdown)
            ?? throw new CryptographicException("The encrypted canonical UCML record is invalid.");
        // Bind every mutable selection/lifecycle column that could admit a record. Content
        // fields must contain only the fixed redaction projection. A database edit cannot
        // widen scope or substitute an envelope from another record/profile.
        var expected = record with
        {
            Topic = "Encrypted UCML context", Title = "Encrypted UCML context", Summary = "", Confidence = 0,
            LearnedBecause = stored.LearnedBecause, Sources = Array.Empty<KnowledgeSource>(), UserCorrection = null,
            LastConfirmedAt = null, LastReinforcedAt = null, LastUsedAt = null, SupersedesId = null
        };
        if (record.Scope != storageScope || !record.LearnedBecause.StartsWith("ucml.v1:", StringComparison.Ordinal) ||
            record.LearnedBecause.StartsWith(Prefix, StringComparison.Ordinal) || JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(stored))
            throw new CryptographicException("The canonical UCML routing projection no longer matches its authenticated record.");
        return record;
    }
}

public sealed partial class AssistantOriginalMemorySource
{
    private CanonicalUcmlRecordProtection? _ucmlProtection;
    private bool _ucmlInputsIssued;
    /// <summary>Trusted host composition only. Call once before opening inputs; this is
    /// not a model tool, owner credential, permission grant or alternate memory store.</summary>
    public void BindOriginalUcmlSecretStore(IProviderSecretStore actualSecrets)
    {
        ArgumentNullException.ThrowIfNull(actualSecrets);
        lock (_writeGate)
        {
            if (_ucmlProtection is not null || _ucmlInputsIssued)
                throw new InvalidOperationException("The original UCML credential owner must be bound once before memory inputs are issued.");
            _ucmlProtection = new(actualSecrets);
        }
    }
    private async Task<string?> ProtectDurableReceiptAsync(Input input, MemoryWriteReceipt receipt, CancellationToken token)
    {
        if (!receipt.Record.LearnedBecause.StartsWith("ucml.v1:", StringComparison.Ordinal)) return null;
        var protection = _ucmlProtection ?? throw new UnauthorizedAccessException("Canonical UCML credential custody is not composed.");
        return await protection.SealPayloadAsync(JsonSerializer.Serialize(receipt), receipt.OperationId,
            CanonicalUcmlRecordProtection.OwnerId(input.Actor), input.ReadScope.StorageScope, "write-receipt", token).ConfigureAwait(false);
    }
}
