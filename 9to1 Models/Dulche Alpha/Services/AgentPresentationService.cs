namespace NineToOne.Dulche.Den;

/// <summary>Authoring and preview operate on the same canonical Den-backed Agent used by Connect and Dulche.</summary>
public sealed class AgentPresentationService(DulcheDen den, IAgentPresentationAssetAccess assets)
{
    public async Task<AgentDefinitionRecord> GetAsync(string namespaceId, string agentId, CancellationToken cancellationToken = default) =>
        await den.GetAsync<AgentDefinitionRecord>(namespaceId, agentId, cancellationToken).ConfigureAwait(false)
        ?? throw new DenException(DenErrorCode.NotFound, "The Agent was not found.");

    public async Task<AgentPresentationFrame> PreviewDraftAsync(string namespaceId, string agentId, long expectedRevision,
        AgentPresentationDefinition draft, string? stateId, string? presentationEvent, string readableActivity,
        bool reducedMotion, CancellationToken cancellationToken = default)
    {
        var definition = AgentAvatarPresentation.Snapshot(draft);
        var agent = await GetAsync(namespaceId, agentId, cancellationToken).ConfigureAwait(false);
        if (agent.Revision != expectedRevision)
            throw new DenException(DenErrorCode.Conflict, "The Agent changed before presentation preview.");
        foreach (var asset in definition.States.Select(s => s.AssetReference).Append(definition.StaticFallbackAssetReference).Distinct(StringComparer.Ordinal))
            if (!await assets.CanReadForAgentAsync(den.PrincipalId, namespaceId, agentId, asset, cancellationToken).ConfigureAwait(false))
                throw new DenException(DenErrorCode.Forbidden, "A referenced presentation asset is missing or inaccessible.");
        var current = await GetAsync(namespaceId, agentId, cancellationToken).ConfigureAwait(false);
        if (current.Revision != expectedRevision)
            throw new DenException(DenErrorCode.Conflict, "The Agent changed during presentation preview.");
        return AgentAvatarPresentation.Present(current with { Presentation = definition }, stateId, presentationEvent,
            readableActivity, reducedMotion, true);
    }

    public async Task<AgentDefinitionRecord> SetAsync(string namespaceId, string agentId, long expectedRevision,
        AgentPresentationDefinition presentation, string operationId, CancellationToken cancellationToken = default)
    {
        presentation = AgentAvatarPresentation.Snapshot(presentation);
        var agent = await den.GetAsync<AgentDefinitionRecord>(namespaceId, agentId, cancellationToken).ConfigureAwait(false)
            ?? throw new DenException(DenErrorCode.NotFound, "The Agent was not found.");
        if (agent.Revision != expectedRevision) throw new DenException(DenErrorCode.Conflict, "The Agent changed before presentation was saved.");
        foreach (var asset in presentation.States.Select(s => s.AssetReference).Append(presentation.StaticFallbackAssetReference).Distinct(StringComparer.Ordinal))
            if (!await assets.CanReadForAgentAsync(den.PrincipalId, namespaceId, agentId, asset, cancellationToken).ConfigureAwait(false))
                throw new DenException(DenErrorCode.Forbidden, "A referenced presentation asset is missing or inaccessible.");
        return await den.SaveAsync(agent with { Presentation = presentation }, expectedRevision, operationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Read a current canonical presentation for an observed host context. A frame is only
    /// display data; it conveys neither asset access nor permission to execute Agent work.</summary>
    public async Task<AgentPresentationFrame> PresentCurrentAsync(string namespaceId, string agentId,
        long expectedRevision, string? stateId, string? presentationEvent, string readableActivity,
        bool reducedMotion, CancellationToken cancellationToken = default)
    {
        var before = await GetAsync(namespaceId, agentId, cancellationToken).ConfigureAwait(false);
        if (before.Revision != expectedRevision)
            throw new DenException(DenErrorCode.Conflict, "The Agent changed before activity presentation.");
        var frame = await PreviewAsync(namespaceId, agentId, stateId, presentationEvent, readableActivity,
            reducedMotion, cancellationToken).ConfigureAwait(false);
        var after = await GetAsync(namespaceId, agentId, cancellationToken).ConfigureAwait(false);
        if (frame.DefinitionRevision != expectedRevision || after.Revision != expectedRevision)
            throw new DenException(DenErrorCode.Conflict, "The Agent changed during activity presentation.");
        cancellationToken.ThrowIfCancellationRequested();
        return frame;
    }

    public async Task<AgentPresentationFrame> PreviewAsync(string namespaceId, string agentId, string? stateId,
        string? presentationEvent, string readableActivity, bool reducedMotion, CancellationToken cancellationToken = default)
    {
        var agent = await den.GetAsync<AgentDefinitionRecord>(namespaceId, agentId, cancellationToken).ConfigureAwait(false)
            ?? throw new DenException(DenErrorCode.NotFound, "The Agent was not found.");
        var definition = AgentAvatarPresentation.Snapshot(agent.Presentation ?? throw new DenException(DenErrorCode.NotFound, "Presentation is not configured."));
        var available = true;
        foreach (var asset in definition.States.Select(s => s.AssetReference))
            available &= await assets.CanReadForAgentAsync(den.PrincipalId, namespaceId, agentId, asset, cancellationToken).ConfigureAwait(false);
        if (!await assets.CanReadForAgentAsync(den.PrincipalId, namespaceId, agentId, definition.StaticFallbackAssetReference, cancellationToken).ConfigureAwait(false))
            throw new DenException(DenErrorCode.Forbidden, "The static fallback asset is inaccessible.");
        return AgentAvatarPresentation.Present(agent with { Presentation = definition }, stateId, presentationEvent, readableActivity, reducedMotion, available);
    }
}
