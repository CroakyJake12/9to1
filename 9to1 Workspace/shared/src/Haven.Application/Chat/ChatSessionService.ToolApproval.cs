using System.Collections.Frozen;
using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    /// <summary>Capabilities of the actual configured execution boundary. Existing
    /// AutoSafe hints are neither a risk classifier nor proof of OS isolation.</summary>
    public IReadOnlyList<ChatToolApprovalAvailability> GetToolApprovalAvailability() =>
        Array.AsReadOnly(new[]
        {
            new ChatToolApprovalAvailability(ChatToolApprovalMode.Ask, true, "Ask before each selected tool call. Existing permissions still apply."),
            new(ChatToolApprovalMode.ApproveForMe, true, "Run known bounded reads automatically; ask for sensitive, consequential or unknown tool calls. Existing permissions still apply."),
            new(ChatToolApprovalMode.RunAutomatically, true, "Run known scoped built-in operations automatically. Commands, tests and unverified runtimes still ask when isolation cannot be established."),
            new(ChatToolApprovalMode.FullAccess, false, "Full access needs an authorized host runtime and a fresh session confirmation. This chat has no such capability; use Ask for approval.")
        });

    public ChatToolApprovalSession CreateToolApprovalSession(Guid conversationId, CancellationToken sessionLifetime)
    {
        if (conversationId == Guid.Empty) throw new ArgumentException("The actual conversation is required.", nameof(conversationId));
        if (!sessionLifetime.CanBeCanceled) throw new ArgumentException("An actual cancellable chat lifetime is required.", nameof(sessionLifetime));
        sessionLifetime.ThrowIfCancellationRequested();
        return new(this, conversationId, sessionLifetime);
    }

    /// <summary>Creates a receipt after the UI snapshots its final send inputs.
    /// The presenter must finish only after answering or closing its actual prompt,
    /// and honor the supplied cancellation. It must not await its own session close.</summary>
    public ChatToolApprovalRequest CreateToolApprovalRequest(ChatToolApprovalSession session,
        Conversation conversation, string prompt, CancellationToken originalSendCancellation,
        ChatToolApprovalMode mode, Func<ChatToolApprovalLease, CancellationToken, Task> present,
        TimeSpan? maximumWait = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(present);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!ReferenceEquals(session.Issuer, this) || session.ConversationId != conversation.Id || !session.IsOpen)
            throw new InvalidOperationException("The original open approval session for this conversation is required.");
        if (!originalSendCancellation.CanBeCanceled)
            throw new ArgumentException("The actual cancellable send lifetime is required.", nameof(originalSendCancellation));
        originalSendCancellation.ThrowIfCancellationRequested();
        var wait = maximumWait ?? RecoveryPolicyDefaults.MaximumInteractiveWait;
        if (wait <= TimeSpan.Zero || wait.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(maximumWait), "A finite positive approval wait is required.");
        return new(this, session, conversation, prompt, originalSendCancellation, mode, present, wait);
    }

    private ChatToolApprovalRequest? ConsumeToolApprovalRequest(Conversation conversation, string prompt,
        GenerationOptions? options, CancellationToken originalSendCancellation)
    {
        var receipt = options?.OriginalToolApprovalRequest;
        var mode = options?.RequestedToolApprovalMode;
        if (receipt is null && mode is null) return null; // Exact legacy behavior.
        originalSendCancellation.ThrowIfCancellationRequested();
        if (receipt is null || mode is null || !Enum.IsDefined(mode.Value) || mode != receipt.Mode ||
            !ReferenceEquals(receipt.Issuer, this) || !ReferenceEquals(receipt.Conversation, conversation) ||
            receipt.Prompt != prompt || receipt.Token != originalSendCancellation || receipt.Ended ||
            !receipt.Session.IsOpen || Interlocked.CompareExchange(ref receipt.Consumed, 1, 0) != 0)
            throw new InvalidOperationException("A fresh original tool-approval request for this exact send is required. Reopen the composer and send again.");
        var available = GetToolApprovalAvailability().Single(item => item.Mode == mode.Value);
        if (!available.Available)
        {
            receipt.Dispose();
            throw new InvalidOperationException(available.Reason);
        }
        return receipt;
    }

    private static OllamaToolCall SnapshotApprovalToolCall(OllamaToolCall call) =>
        call with { Arguments = call.Arguments.ToFrozenDictionary(pair => pair.Key,
            pair => pair.Value.Clone(), StringComparer.Ordinal) };

    private static WorkspaceToolResult DeclinedToolApproval(Guid actionId, OllamaToolCall call,
        ChatToolApprovalOutcome outcome, DateTimeOffset started)
    {
        var detail = outcome switch
        {
            ChatToolApprovalOutcome.Denied => "The user declined this tool call. It was not executed.",
            ChatToolApprovalOutcome.Expired => "This tool call was not executed because its approval request expired unanswered.",
            ChatToolApprovalOutcome.SessionClosed => "This tool call was not executed because its original chat session closed.",
            _ => "This tool call was not executed because its approval prompt closed without a decision."
        };
        return new(new ToolActivity(actionId, call.Name.Replace('_', ' '), detail, false,
            TimeSpan.Zero, DateTimeOffset.UtcNow)
        { InvocationEvidence = Array.AsReadOnly(new[] { new ToolInvocationEvidence(actionId, call.Name,
            null, ToolInvocationObservationStatus.DeniedBeforeDispatch, null, started, DateTimeOffset.UtcNow,
            ReportedFailureCode: "CHAT_TOOL_APPROVAL_" + outcome.ToString().ToUpperInvariant()) }) }, detail);
    }
}
