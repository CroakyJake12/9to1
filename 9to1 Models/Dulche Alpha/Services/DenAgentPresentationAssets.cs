using System.Security.Cryptography;

namespace NineToOne.Dulche.Den;

/// <summary>Detached, hash-verified attachment bytes. This value confers no future read authority.</summary>
public sealed class AgentPresentationAssetBytes
{
    internal AgentPresentationAssetBytes(string id, long revision, string hash, string mediaType, byte[] content)
    { ReferenceId = id; ReferenceRevision = revision; Sha256 = hash; MediaType = mediaType; Content = content; }
    public string ReferenceId { get; }
    public long ReferenceRevision { get; }
    public string Sha256 { get; }
    public string MediaType { get; }
    public byte[] Content { get; }
}

/// <summary>Uses the existing Den attachment identity and the current Den principal; no path or URI parsing.</summary>
public sealed class DenAgentPresentationAssets(DulcheDen den) : IAgentPresentationAssetAccess
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    public const string AgentOwnerKind = "agent";

    public async ValueTask<bool> CanReadAsync(string principalId, string namespaceId, string assetReference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(principalId, den.PrincipalId, StringComparison.Ordinal)) return false;
        try
        {
            var reference = await RequireReferenceAsync(namespaceId, assetReference, cancellationToken).ConfigureAwait(false);
            _ = await RequireAgentAsync(namespaceId, reference.OwnerId, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DenException error) when (error.Code is DenErrorCode.NotFound or DenErrorCode.Forbidden or DenErrorCode.InvalidRecord)
        { return false; }
    }

    public async ValueTask<bool> CanReadForAgentAsync(string principalId, string namespaceId, string agentId,
        string assetReference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(principalId, den.PrincipalId, StringComparison.Ordinal)) return false;
        try
        {
            var reference = await RequireReferenceAsync(namespaceId, assetReference, cancellationToken).ConfigureAwait(false);
            if (reference.OwnerId != agentId) return false;
            _ = await RequireAgentAsync(namespaceId, agentId, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DenException error) when (error.Code is DenErrorCode.NotFound or DenErrorCode.Forbidden or DenErrorCode.InvalidRecord)
        { return false; }
    }

    public async Task<AgentPresentationAssetBytes> ReadAsync(string namespaceId, string agentId, string referenceId,
        long expectedAgentRevision, CancellationToken cancellationToken = default)
    {
        var agent = await RequireAgentAsync(namespaceId, agentId, cancellationToken).ConfigureAwait(false);
        if (agent.Revision != expectedAgentRevision) throw new DenException(DenErrorCode.Conflict, "The Agent changed before asset acquisition.");
        var reference = await RequireReferenceAsync(namespaceId, referenceId, cancellationToken).ConfigureAwait(false);
        if (reference.OwnerId != agentId) throw new DenException(DenErrorCode.Forbidden, "The presentation attachment belongs to another Agent.");
        var content = await den.ReadAttachmentAsync(namespaceId, referenceId, MaximumBytes, cancellationToken).ConfigureAwait(false);
        try
        {
            var after = await RequireReferenceAsync(namespaceId, referenceId, cancellationToken).ConfigureAwait(false);
            var currentAgent = await RequireAgentAsync(namespaceId, agentId, cancellationToken).ConfigureAwait(false);
            if (after.Revision != reference.Revision || after.Sha256 != reference.Sha256 || after.Length != reference.Length ||
                after.MediaType != reference.MediaType || after.OwnerId != agentId || currentAgent.Revision != expectedAgentRevision)
                throw new DenException(DenErrorCode.Conflict, "The Agent or attachment changed during asset acquisition.");
            cancellationToken.ThrowIfCancellationRequested();
            return new(reference.Id, reference.Revision, reference.Sha256, reference.MediaType, content);
        }
        catch { Array.Clear(content); throw; }
    }

    /// <summary>Rechecks current authority and exact source after asynchronous decoding, without acquiring another blob.</summary>
    public async Task ValidateAsync(string namespaceId, string agentId, long expectedAgentRevision,
        AgentPresentationAssetBytes captured, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captured);
        cancellationToken.ThrowIfCancellationRequested();
        var agent = await RequireAgentAsync(namespaceId, agentId, cancellationToken).ConfigureAwait(false);
        var reference = await RequireReferenceAsync(namespaceId, captured.ReferenceId, cancellationToken).ConfigureAwait(false);
        if (reference.OwnerId != agentId) throw new DenException(DenErrorCode.Forbidden, "The attachment belongs to another Agent.");
        if (agent.Revision != expectedAgentRevision || reference.Revision != captured.ReferenceRevision ||
            reference.Sha256 != captured.Sha256 || reference.MediaType != captured.MediaType || reference.Length != captured.Content.LongLength ||
            captured.Content.Length > MaximumBytes || Convert.ToHexString(SHA256.HashData(captured.Content)) != reference.Sha256)
            throw new DenException(DenErrorCode.Conflict, "The Agent, attachment, or acquired bytes changed during presentation decoding.");
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<AgentDefinitionRecord> RequireAgentAsync(string ns, string id, CancellationToken token) =>
        await den.GetAsync<AgentDefinitionRecord>(ns, id, token).ConfigureAwait(false)
        ?? throw new DenException(DenErrorCode.NotFound, "The attachment's Agent is unavailable.");

    private async Task<BlobReferenceRecord> RequireReferenceAsync(string ns, string id, CancellationToken token)
    {
        var reference = await den.GetAsync<BlobReferenceRecord>(ns, id, token).ConfigureAwait(false)
            ?? throw new DenException(DenErrorCode.NotFound, "The presentation attachment is unavailable.");
        if (reference.Deleted) throw new DenException(DenErrorCode.NotFound, "The presentation attachment was removed.");
        if (reference.OwnerKind != AgentOwnerKind || reference.Length is <= 0 or > MaximumBytes ||
            string.IsNullOrWhiteSpace(reference.MediaType))
            throw new DenException(DenErrorCode.InvalidRecord, "The attachment is not a bounded Agent presentation asset.");
        return reference;
    }
}
