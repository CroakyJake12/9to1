namespace NineToOne.Dulche.Den;

public enum AgentPresentationActivityKind { Idle, Working, Coding, Completed, Failed, Cancelled, UserJoke }

/// <summary>A display context bound to an actual Run or Conversation identity by the host.
/// Knowing these IDs does not authorize activity reads.</summary>
public sealed record AgentPresentationContext(string NamespaceId, string AgentId, string SourceId);

/// <summary>Content-free presentation input. This DTO establishes no authenticity or execution authority.
/// Sequence is monotonic within the exact context, including its source identity.</summary>
public sealed record AgentPresentationObservation(AgentPresentationContext Context, long Sequence,
    AgentPresentationActivityKind Kind);

/// <summary>The authenticated host must check current caller permission for the Agent AND the actual
/// Run/Conversation on every read, and expose only observed activity or explicitly authorized conversational
/// events. A joke event must not be inferred from another caller's private text. No default source grants access.</summary>
public interface IAgentPresentationObservationSource
{
    ValueTask<AgentPresentationObservation?> GetCurrentAsync(AgentPresentationContext context,
        CancellationToken cancellationToken);
}

public static class AgentPresentationEvents
{
    public const string Coding = "activity.coding";
    public const string UserJoke = "conversation.joke";

    public static (string EventId, string ReadableActivity) Describe(AgentPresentationActivityKind kind) => kind switch
    {
        AgentPresentationActivityKind.Idle => ("activity.idle", "Idle"),
        AgentPresentationActivityKind.Working => ("activity.working", "Working"),
        AgentPresentationActivityKind.Coding => (Coding, "Coding"),
        AgentPresentationActivityKind.Completed => ("activity.completed", "Completed"),
        AgentPresentationActivityKind.Failed => ("activity.failed", "Needs attention"),
        AgentPresentationActivityKind.Cancelled => ("activity.cancelled", "Cancelled"),
        AgentPresentationActivityKind.UserJoke => (UserJoke, "Joke reaction"),
        _ => throw new DenException(DenErrorCode.InvalidRecord, "Unknown Agent presentation activity.")
    };
}
