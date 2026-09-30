namespace NineToOne.Dulche.Den;

/// <summary>Authoring and preview operate on the same canonical Den-backed Agent used by Connect and Dulche.</summary>
public sealed class AgentPresentationService(DulcheDen den, IAgentPresentationAssetAccess assets)
{
    public async Task<AgentDefinitionRecord> SetAsync(string namespaceId, string agentId, long expectedRevision,
        AgentPresentationDefinition presentation, string operationId, CancellationToken cancellationToken = default)
    {
        AgentAvatarPresentation.Validate(presentation);
        var agent = await den.GetAsync<AgentDefinitionRecord>(namespaceId, agentId, cancellationToken).ConfigureAwait(false)
            ?? throw new DenException(DenErrorCode.NotFound, "The Agent was not found.");
        if (agent.Revision != expectedRevision) throw new DenException(DenErrorCode.Conflict, "The Agent changed before presentation was saved.");
        foreach (var asset in presentation.States.Select(s => s.AssetReference).Append(presentation.StaticFallbackAssetReference).Distinct(StringComparer.Ordinal))
            if (!await assets.CanReadAsync(den.PrincipalId, namespaceId, asset, cancellationToken).ConfigureAwait(false))
                throw new DenException(DenErrorCode.Forbidden, "A referenced presentation asset is missing or inaccessible.");
        return await den.SaveAsync(agent with { Presentation = presentation }, expectedRevision, operationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentPresentationFrame> PreviewAsync(string namespaceId, string agentId, string? stateId,
        string? presentationEvent, string readableActivity, bool reducedMotion, CancellationToken cancellationToken = default)
    {
        var agent = await den.GetAsync<AgentDefinitionRecord>(namespaceId, agentId, cancellationToken).ConfigureAwait(false)
            ?? throw new DenException(DenErrorCode.NotFound, "The Agent was not found.");
        var definition = agent.Presentation ?? throw new DenException(DenErrorCode.NotFound, "Presentation is not configured.");
        var available = true;
        foreach (var asset in definition.States.Select(s => s.AssetReference))
            available &= await assets.CanReadAsync(den.PrincipalId, namespaceId, asset, cancellationToken).ConfigureAwait(false);
        if (!await assets.CanReadAsync(den.PrincipalId, namespaceId, definition.StaticFallbackAssetReference, cancellationToken).ConfigureAwait(false))
            throw new DenException(DenErrorCode.Forbidden, "The static fallback asset is inaccessible.");
        return AgentAvatarPresentation.Present(agent, stateId, presentationEvent, readableActivity, reducedMotion, available);
    }
}
