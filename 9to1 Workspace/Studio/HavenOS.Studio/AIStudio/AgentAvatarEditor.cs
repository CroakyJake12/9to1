using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using NineToOne.Dulche.Den;

namespace HavenOS.AIStudio;

/// <summary>One canonical Agent draft. The host supplies the authenticated Den service;
/// presentation preview cannot dispatch Agent work.</summary>
public sealed class AgentAvatarEditor : ICuiActionDispatcher
{
    private readonly AgentPresentationService _canonical;
    private readonly AgentAvatarBuilder _builder;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private AgentDefinitionRecord? _agent;
    private AgentPresentationDefinition? _draft;
    public CuiViewModel Bindings { get; } = new();
    public AgentPresentationDefinition? Draft => _draft is null ? null : AgentAvatarPresentation.Snapshot(_draft);

    public AgentAvatarEditor(AgentPresentationService canonical)
    {
        _canonical = canonical;
        _builder = new(canonical);
        foreach (var field in new[] { "Status", "NamespaceID", "AgentID", "AgentName", "Revision", "StaticAsset", "AccessibleName", "InitialState", "StateID", "StateLabel", "StateAsset", "FromState", "ToState", "TransitionEvent", "ReactionEvent", "ReactionState", "PreviewEvent", "AvatarActivity", "PreviewAsset" })
            Bindings.Set(field, "");
        Bindings.Set("Animated", true); Bindings.Set("StateLoop", true); Bindings.Set("ReducedMotion", false);
        Bindings.Set("Projects", Array.Empty<object>());
        Bindings.Set("AvatarStates", Array.Empty<AgentAvatarState>());
        Bindings.Set("AvatarTransitions", Array.Empty<AgentAvatarTransition>());
        Bindings.Set("AvatarReactions", Array.Empty<AgentAvatarReaction>());
        Bindings.Set("Status", "Open an authorised Agent to edit its presentation.");
    }

    public async Task OpenAsync(string namespaceID, string agentID, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try { Load(await _canonical.GetAsync(namespaceID, agentID, cancellationToken)); }
        finally { _operations.Release(); }
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            if (command == "OpenAvatar") { Load(await _canonical.GetAsync(Text("NamespaceID"), Text("AgentID"), cancellationToken)); return; }
            var agent = _agent ?? throw new DenException(DenErrorCode.NotFound, "Open an Agent first.");
            var draft = _draft;
            if (draft is null && command == "SetAvatarIdentity")
                draft = new(1, AgentIconPresentation.Static, Text("StaticAsset"), Text("AccessibleName"), "", [], [], []);
            if (draft is null) throw new DenException(DenErrorCode.NotFound, "Apply a static avatar identity before adding animation states.");
            switch (command)
            {
                case "SetAvatarIdentity":
                    _draft = _builder.SetIdentity(draft, Text("StaticAsset"), Text("AccessibleName"), Flag("Animated") ? AgentIconPresentation.Animated : AgentIconPresentation.Static, Text("InitialState")); break;
                case "AddAvatarState": _draft = _builder.AddState(draft, ReadState()); break;
                case "ReplaceAvatarState": _draft = _builder.ReplaceState(draft, ReadState()); break;
                case "RemoveAvatarState": _draft = _builder.RemoveState(draft, Text("StateID"), Text("InitialState")); break;
                case "SetAvatarTransition": _draft = _builder.SetTransition(draft, new(Text("FromState"), Text("ToState"), Text("TransitionEvent"))); break;
                case "RemoveAvatarTransition": _draft = _builder.RemoveTransition(draft, Text("FromState"), Text("TransitionEvent")); break;
                case "SetAvatarReaction": _draft = _builder.SetReaction(draft, new(Text("ReactionEvent"), Text("ReactionState"))); break;
                case "RemoveAvatarReaction": _draft = _builder.RemoveReaction(draft, Text("ReactionEvent")); break;
                case "SaveAvatar":
                    Load(await _builder.SaveAsync(agent.NamespaceId, agent.Id, agent.Revision, draft, Guid.NewGuid().ToString("N"), cancellationToken)); return;
                case "PreviewAvatar":
                    var frame = await _canonical.PreviewDraftAsync(agent.NamespaceId, agent.Id, agent.Revision, draft,
                        string.IsNullOrWhiteSpace(Text("StateID")) ? null : Text("StateID"), Text("PreviewEvent"), Text("AvatarActivity"), Flag("ReducedMotion"), cancellationToken);
                    Bindings.Set("PreviewAsset", frame.AssetReference);
                    Bindings.Set("Status", $"Presentation preview: {frame.ReadableActivity}. {(frame.Animated ? "Animation" : "Static fallback")}; Agent revision {frame.DefinitionRevision}."); return;
                default: throw new InvalidOperationException("Unknown avatar action.");
            }
            PublishDraft(); Bindings.Set("Status", "Unsaved presentation changes.");
        }
        catch (DenException error) { Bindings.Set("Status", $"{error.Code}: {error.Message}"); throw; }
        finally { _operations.Release(); }
    }

    private void Load(AgentDefinitionRecord agent)
    {
        var draft = agent.Presentation is null ? null : AgentAvatarPresentation.Snapshot(agent.Presentation);
        _agent = agent; _draft = draft;
        Bindings.Set("NamespaceID", agent.NamespaceId); Bindings.Set("AgentID", agent.Id); Bindings.Set("AgentName", agent.DisplayName);
        Bindings.Set("Revision", agent.Revision.ToString()); Bindings.Set("StaticAsset", draft?.StaticFallbackAssetReference ?? "");
        Bindings.Set("AccessibleName", draft?.AccessibleName ?? agent.DisplayName); Bindings.Set("InitialState", draft?.InitialStateId ?? "");
        Bindings.Set("Animated", draft?.Mode == AgentIconPresentation.Animated);
        PublishDraft(); Bindings.Set("Status", $"Saved Agent revision {agent.Revision}.");
    }
    private void PublishDraft()
    {
        Bindings.Set("AvatarStates", _draft?.States ?? Array.Empty<AgentAvatarState>()); Bindings.Set("AvatarTransitions", _draft?.Transitions ?? Array.Empty<AgentAvatarTransition>()); Bindings.Set("AvatarReactions", _draft?.Reactions ?? Array.Empty<AgentAvatarReaction>());
    }
    private string Text(string field) => Bindings.Get(field)?.ToString() ?? "";
    private bool Flag(string field) => Bindings.Get(field) is true;
    private AgentAvatarState ReadState() => new(Text("StateID"), Text("StateLabel"), Text("StateAsset"), Flag("StateLoop"));
}
