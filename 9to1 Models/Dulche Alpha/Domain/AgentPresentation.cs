namespace NineToOne.Dulche.Den;

public enum AgentIconPresentation { Static, Animated }
public sealed record AgentAvatarState(string StateId, string Label, string AssetReference, bool Loop);
public sealed record AgentAvatarTransition(string FromStateId, string ToStateId, string EventId);
public sealed record AgentAvatarReaction(string EventId, string StateId);
public sealed record AgentPresentationDefinition(int SchemaVersion, AgentIconPresentation Mode,
    string StaticFallbackAssetReference, string AccessibleName, string InitialStateId,
    IReadOnlyList<AgentAvatarState> States, IReadOnlyList<AgentAvatarTransition> Transitions,
    IReadOnlyList<AgentAvatarReaction> Reactions);
public sealed record AgentPresentationFrame(string AgentId, long DefinitionRevision, string AssetReference,
    string AccessibleName, string ReadableActivity, bool Animated, string? StateId);

/// <summary>Presentation events are verified observed activity or authorised preview; no task execution or permission changes.</summary>
public static class AgentAvatarPresentation
{
    public static AgentPresentationDefinition Snapshot(AgentPresentationDefinition? definition)
    {
        if (definition is null || definition.States is null || definition.Transitions is null || definition.Reactions is null ||
            definition.States.Count > 128 || definition.Transitions.Count > 512 || definition.Reactions.Count > 128)
            throw new DenException(DenErrorCode.InvalidRecord, "Agent presentation has an invalid or oversized structure.");
        AgentPresentationDefinition snapshot;
        try
        {
            snapshot = definition with { States = Array.AsReadOnly(definition.States.ToArray()),
                Transitions = Array.AsReadOnly(definition.Transitions.ToArray()), Reactions = Array.AsReadOnly(definition.Reactions.ToArray()) };
        }
        catch (InvalidOperationException)
        { throw new DenException(DenErrorCode.InvalidRecord, "Agent presentation changed while being captured."); }
        Validate(snapshot);
        return snapshot;
    }

    public static void Validate(AgentPresentationDefinition? definition)
    {
        if (definition is null || definition.States is null || definition.Transitions is null || definition.Reactions is null ||
            definition.States.Count > 128 || definition.Transitions.Count > 512 || definition.Reactions.Count > 128 ||
            definition.States.Any(state => state is null) || definition.Transitions.Any(transition => transition is null) || definition.Reactions.Any(reaction => reaction is null))
            throw new DenException(DenErrorCode.InvalidRecord, "Agent presentation has an invalid structure.");
        if (definition.SchemaVersion != 1) throw new DenException(DenErrorCode.InvalidRecord, "Unsupported Agent presentation schema.");
        if (!Enum.IsDefined(definition.Mode) || string.IsNullOrWhiteSpace(definition.StaticFallbackAssetReference) || string.IsNullOrWhiteSpace(definition.AccessibleName))
            throw new DenException(DenErrorCode.InvalidRecord, "A static fallback and accessible identity are required.");
        if (definition.States.Count > 128 || definition.Transitions.Count > 512 || definition.Reactions.Count > 128)
            throw new DenException(DenErrorCode.InvalidRecord, "Agent presentation exceeds supported state limits.");
        if (definition.States.Any(s => string.IsNullOrWhiteSpace(s.StateId) || string.IsNullOrWhiteSpace(s.Label) || string.IsNullOrWhiteSpace(s.AssetReference)) ||
            definition.States.Select(s => s.StateId).Distinct(StringComparer.Ordinal).Count() != definition.States.Count)
            throw new DenException(DenErrorCode.InvalidRecord, "Animation states need unique stable IDs, labels and assets.");
        var states = definition.States.Select(s => s.StateId).ToHashSet(StringComparer.Ordinal);
        if (definition.Mode == AgentIconPresentation.Animated && !states.Contains(definition.InitialStateId))
            throw new DenException(DenErrorCode.InvalidRecord, "The initial animation state is unavailable.");
        if (definition.Transitions.Any(t => !states.Contains(t.FromStateId) || !states.Contains(t.ToStateId) || string.IsNullOrWhiteSpace(t.EventId)) ||
            definition.Transitions.Select(t => (t.FromStateId, t.EventId)).Distinct().Count() != definition.Transitions.Count ||
            definition.Reactions.Any(r => !states.Contains(r.StateId) || string.IsNullOrWhiteSpace(r.EventId)) ||
            definition.Reactions.Select(r => r.EventId).Distinct(StringComparer.Ordinal).Count() != definition.Reactions.Count)
            throw new DenException(DenErrorCode.InvalidRecord, "Transitions and reactions must resolve uniquely to declared states.");
    }

    public static AgentPresentationFrame Present(AgentDefinitionRecord agent, string? currentStateId, string? observedEventId,
        string readableActivity, bool reducedMotion, bool assetsAvailable = true)
    {
        var presentation = agent.Presentation ?? throw new DenException(DenErrorCode.NotFound, "Agent presentation is not configured.");
        Validate(presentation);
        var stateId = currentStateId ?? presentation.InitialStateId;
        if (observedEventId is not null)
            stateId = presentation.Reactions.SingleOrDefault(r => r.EventId == observedEventId)?.StateId ??
                presentation.Transitions.SingleOrDefault(t => t.FromStateId == stateId && t.EventId == observedEventId)?.ToStateId ?? stateId;
        var state = presentation.States.SingleOrDefault(s => s.StateId == stateId);
        var animated = presentation.Mode == AgentIconPresentation.Animated && !reducedMotion && assetsAvailable && state is not null;
        return new(agent.Id, agent.Revision, animated ? state!.AssetReference : presentation.StaticFallbackAssetReference,
            presentation.AccessibleName, readableActivity, animated, animated ? stateId : null);
    }
}

public interface IAgentPresentationAssetAccess
{
    ValueTask<bool> CanReadAsync(string principalId, string namespaceId, string assetReference, CancellationToken cancellationToken);
}
