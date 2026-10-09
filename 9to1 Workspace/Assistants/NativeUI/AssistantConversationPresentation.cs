using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed record AssistantMessagePresentation(Guid Id, string RoleLabel, string Content,
    string Thinking = "", string Activity = "")
{
    public bool HasThinking => Thinking.Length != 0;
    public bool HasActivity => Activity.Length != 0;
}

/// <summary>Canonical message/event projection only. Targets reject stale rendering and grant no authority.</summary>
public sealed partial class AssistantConversationPresentation
{
    private readonly List<AssistantMessagePresentation> _messages = [];
    private Target? _target;
    public IReadOnlyList<AssistantMessagePresentation> Messages => Array.AsReadOnly(_messages.ToArray());

    public Target Bind(AssistantConversationData data, bool includeCompacted = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        _target = new(data.Conversation.Id);
        _messages.Clear();
        foreach (var message in data.Messages.Where(message => includeCompacted || !message.IsCompacted))
        {
            if (message.ConversationId != data.Conversation.Id)
                throw new InvalidDataException("A message differs from the actual displayed conversation.");
            _messages.Add(message.IsCompacted
                ? Project(message) with { Activity = "Earlier message · summarized for model context." }
                : Project(message));
        }
        return _target;
    }

    public void Clear() { _target = null; _messages.Clear(); }

    public bool Apply(Target target, ChatStreamEvent observed)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(observed);
        if (!ReferenceEquals(target, _target)) return false;
        if (observed.Message is { } message)
        {
            if (message.ConversationId != target.ConversationId)
                throw new InvalidDataException("The observed message belongs to another canonical conversation.");
            Upsert(Project(message)); return true;
        }
        if (observed.MessageId is not { } id) return false;
        var current = _messages.FirstOrDefault(message => message.Id == id)
            ?? new AssistantMessagePresentation(id, observed.Agent ?? "Assistant", "");
        var replacement = observed.Kind switch
        {
            ChatStreamEventKind.AssistantStarted => current with { RoleLabel = observed.Agent ?? "Assistant" },
            ChatStreamEventKind.AssistantDelta => current with { Content = current.Content + observed.Delta },
            ChatStreamEventKind.ThinkingDelta => current with { Thinking = current.Thinking + observed.Thinking },
            ChatStreamEventKind.ToolActivity when observed.ToolActivity is { } activity => current with
            { Activity = $"{activity.Title}: {activity.Detail}" },
            ChatStreamEventKind.PermissionRequired => current with { Activity = "Waiting for an authorized approval." },
            _ => null
        };
        if (replacement is null) return false;
        Upsert(replacement); return true;
    }

    private void Upsert(AssistantMessagePresentation message)
    {
        var index = _messages.FindIndex(existing => existing.Id == message.Id);
        if (index < 0) _messages.Add(message); else _messages[index] = message;
    }

    private static AssistantMessagePresentation Project(ChatMessage message) => new(message.Id,
        message.Role == MessageRole.User ? "You" : message.AgentName ?? message.Role.ToString(), message.Content);

    public sealed class Target
    {
        internal Target(Guid conversationId) => ConversationId = conversationId;
        public Guid ConversationId { get; }
    }
}
