using NineToOne.Dulche.Den;

namespace HavenOS.AIStudio;

/// <summary>Visual editor state uses the canonical Den presentation schema. Preview never executes an Agent.</summary>
public sealed class AgentAvatarBuilder(AgentPresentationService canonical)
{
    public AgentPresentationDefinition AddState(AgentPresentationDefinition draft, AgentAvatarState state)
    {
        var next=draft with {States=draft.States.Append(state).ToArray()};
        return AgentAvatarPresentation.Snapshot(next);
    }
    public AgentPresentationDefinition ReplaceState(AgentPresentationDefinition draft, AgentAvatarState state)
    {
        if(!draft.States.Any(s=>s.StateId==state.StateId))throw new InvalidOperationException("StateNotFound");
        var next=draft with {States=draft.States.Select(s=>s.StateId==state.StateId?state:s).ToArray()};
        return AgentAvatarPresentation.Snapshot(next);
    }
    public AgentPresentationDefinition RemoveState(AgentPresentationDefinition draft,string stateID,string replacementInitialStateID)
    {
        var next=draft with {States=draft.States.Where(s=>s.StateId!=stateID).ToArray(),
            Transitions=draft.Transitions.Where(t=>t.FromStateId!=stateID&&t.ToStateId!=stateID).ToArray(),
            Reactions=draft.Reactions.Where(r=>r.StateId!=stateID).ToArray(),
            InitialStateId=draft.InitialStateId==stateID?replacementInitialStateID:draft.InitialStateId};
        return AgentAvatarPresentation.Snapshot(next);
    }
    public AgentPresentationDefinition SetTransition(AgentPresentationDefinition draft,AgentAvatarTransition transition)
    {
        var next=draft with {Transitions=draft.Transitions.Where(t=>t.FromStateId!=transition.FromStateId||t.EventId!=transition.EventId).Append(transition).ToArray()};
        return AgentAvatarPresentation.Snapshot(next);
    }
    public AgentPresentationDefinition SetReaction(AgentPresentationDefinition draft,AgentAvatarReaction reaction)
    {
        var next=draft with {Reactions=draft.Reactions.Where(r=>r.EventId!=reaction.EventId).Append(reaction).ToArray()};
        return AgentAvatarPresentation.Snapshot(next);
    }
    public AgentPresentationDefinition SetIdentity(AgentPresentationDefinition draft, string staticFallbackAssetReference,
        string accessibleName, AgentIconPresentation mode, string initialStateID)
        => AgentAvatarPresentation.Snapshot(draft with { StaticFallbackAssetReference = staticFallbackAssetReference,
            AccessibleName = accessibleName, Mode = mode, InitialStateId = initialStateID });

    public AgentPresentationDefinition RemoveTransition(AgentPresentationDefinition draft, string fromStateID, string eventID)
        => AgentAvatarPresentation.Snapshot(draft with {
            Transitions = draft.Transitions.Where(t => t.FromStateId != fromStateID || t.EventId != eventID).ToArray() });

    public AgentPresentationDefinition RemoveReaction(AgentPresentationDefinition draft, string eventID)
        => AgentAvatarPresentation.Snapshot(draft with {
            Reactions = draft.Reactions.Where(r => r.EventId != eventID).ToArray() });

    public Task<AgentDefinitionRecord> OpenAsync(string namespaceID, string agentID,
        CancellationToken cancellationToken = default) => canonical.GetAsync(namespaceID, agentID, cancellationToken);
    public Task<AgentDefinitionRecord> AssignAsync(string namespaceID, string agentID, long expectedRevision,
        AgentPresentationDefinition draft, string operationID, CancellationToken cancellationToken = default)
        => canonical.SetAsync(namespaceID, agentID, expectedRevision, draft, operationID, cancellationToken);
    public Task<AgentPresentationFrame> PreviewDraftAsync(string namespaceID, string agentID, long expectedRevision,
        AgentPresentationDefinition draft, string? stateID, string? presentationEvent, string readableActivity,
        bool reducedMotion, CancellationToken cancellationToken = default)
        => canonical.PreviewDraftAsync(namespaceID, agentID, expectedRevision, draft, stateID, presentationEvent,
            readableActivity, reducedMotion, cancellationToken);

    public Task<AgentDefinitionRecord> SaveAsync(string namespaceID,string agentID,long expectedRevision,
        AgentPresentationDefinition draft,string operationID,CancellationToken cancellationToken=default)
        =>canonical.SetAsync(namespaceID,agentID,expectedRevision,draft,operationID,cancellationToken);
    public Task<AgentPresentationFrame> PreviewAsync(string namespaceID,string agentID,string? stateID,string? presentationEvent,
        string readableActivity,bool reducedMotion,CancellationToken cancellationToken=default)
        =>canonical.PreviewAsync(namespaceID,agentID,stateID,presentationEvent,readableActivity,reducedMotion,cancellationToken);
}
