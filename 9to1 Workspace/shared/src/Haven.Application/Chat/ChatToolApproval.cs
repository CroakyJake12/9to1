using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Per-chat prompting intent. It never replaces account, tool, model,
/// resource or canonical Task permission authority.</summary>
public enum ChatToolApprovalMode { Ask = 0, ApproveForMe = 1, RunAutomatically = 2, FullAccess = 3 }
public enum ChatToolApprovalDecision { Deny = 0, Allow = 1, AllowForSession = 2 }
public sealed record ChatToolApprovalAvailability(ChatToolApprovalMode Mode, bool Available, string Reason);

/// <summary>One exact pending tool call. Only its original presenter receives this
/// object; identifiers or deserialized copies cannot answer another pending call.</summary>
public sealed class ChatToolApprovalLease
{
    private readonly object _sync = new();
    private readonly CancellationToken _token;
    private ChatToolApprovalDecision? _decision;
    private bool _ended;
    internal ChatToolApprovalLease(Guid conversationId, Guid actionId, OllamaToolCall call, CancellationToken token)
    {
        ConversationId = conversationId; ActionId = actionId; ToolName = call.Name;
        Arguments = call.Arguments.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        _token = token;
    }
    public Guid ConversationId { get; }
    public Guid ActionId { get; }
    public string ToolName { get; }
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; }
    public bool IsPending { get { lock (_sync) return !_ended && !_token.IsCancellationRequested && _decision is null; } }
    public bool TryResolve(ChatToolApprovalDecision decision)
    {
        if (!Enum.IsDefined(decision)) throw new ArgumentOutOfRangeException(nameof(decision));
        lock (_sync)
        {
            if (_ended || _token.IsCancellationRequested || _decision is not null) return false;
            _decision = decision;
            return true;
        }
    }
    internal ChatToolApprovalDecision? Finish()
    { lock (_sync) { _ended = true; return _decision; } }
}

/// <summary>Opaque, single-use receipt for an actual send. It is not serializable
/// authority; generation options retain only the restrictive mode on persistence.</summary>
public sealed class ChatToolApprovalRequest : IDisposable
{
    internal ChatToolApprovalRequest(ChatSessionService issuer, ChatToolApprovalSession session,
        Conversation conversation, string prompt, CancellationToken token, ChatToolApprovalMode mode,
        Func<ChatToolApprovalLease, CancellationToken, Task> present, TimeSpan maximumWait)
    { Issuer = issuer; Session = session; Conversation = conversation; Prompt = prompt; Token = token;
        Mode = mode; Present = present; MaximumWait = maximumWait; }
    internal ChatSessionService Issuer { get; }
    internal ChatToolApprovalSession Session { get; }
    internal Conversation Conversation { get; }
    internal string Prompt { get; }
    internal CancellationToken Token { get; }
    internal Func<ChatToolApprovalLease, CancellationToken, Task> Present { get; }
    internal TimeSpan MaximumWait { get; }
    internal int Consumed;
    private int _ended;
    public ChatToolApprovalMode Mode { get; }
    internal bool Ended => Volatile.Read(ref _ended) != 0;
    public void Dispose() => Interlocked.Exchange(ref _ended, 1);
}

/// <summary>Ephemeral chat-session prompt exceptions only. Closing joins the
/// original admitted presenter drivers; it never claims that a pending UI settled.</summary>
public sealed class ChatToolApprovalSession : IAsyncDisposable
{
    private static readonly AsyncLocal<ChatToolApprovalSession?> Presenting = new();
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime;
    private readonly HashSet<string> _allowedTools = new(StringComparer.Ordinal);
    private readonly List<(Task<ChatToolApprovalOutcome> Actual, CancellationToken Business)> _originals = new();
    private bool _closed;
    private Task? _close;
    internal ChatToolApprovalSession(ChatSessionService issuer, Guid conversationId, CancellationToken lifetime)
    { Issuer = issuer; ConversationId = conversationId; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime); }
    internal ChatSessionService Issuer { get; }
    public Guid ConversationId { get; }
    public bool IsOpen { get { lock (_sync) return !_closed && !_lifetime.IsCancellationRequested; } }

    internal Task<ChatToolApprovalOutcome> DecideOriginalAsync(ChatToolApprovalRequest request,
        Guid actionId, OllamaToolCall call, CancellationToken token)
    {
        TaskCompletionSource start;
        Task<ChatToolApprovalOutcome> actual;
        lock (_sync)
        {
            token.ThrowIfCancellationRequested();
            if (_closed || _lifetime.IsCancellationRequested || request.Ended || !ReferenceEquals(request.Session, this))
                throw new InvalidOperationException("The original chat approval session has closed.");
            // Publish the actual driver before user presentation can synchronously reenter Close.
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DecideBodyAsync(request, actionId, call, token, start.Task);
            _originals.RemoveAll(item => item.Actual.IsCompletedSuccessfully);
            _originals.Add((actual, token));
        }
        start.SetResult();
        return actual;
    }
    private async Task<ChatToolApprovalOutcome> DecideBodyAsync(ChatToolApprovalRequest request,
        Guid actionId, OllamaToolCall call, CancellationToken token, Task start)
    {
        await start.ConfigureAwait(false);
        lock (_sync)
        {
            if (_closed || _lifetime.IsCancellationRequested || request.Ended)
                throw new InvalidOperationException("The original chat approval session has closed.");
            if (_allowedTools.Contains(call.Name)) return ChatToolApprovalOutcome.Allowed;
        }
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        wait.CancelAfter(request.MaximumWait);
        var lease = new ChatToolApprovalLease(ConversationId, actionId, call, wait.Token);
        Task? presentation = null;
        var previousPresenter = Presenting.Value;
        Presenting.Value = this;
        try
        {
            presentation = request.Present(lease, wait.Token)
                ?? throw new InvalidOperationException("The original approval presenter returned no Task.");
            await presentation.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            lease.Finish();
            // Preserve foreign faulted OCE and compound failure as faults, not cancellation.
            if (presentation is { IsFaulted: true }) ThrowOriginalFault(presentation.Exception!);
            if (presentation is null && error is OperationCanceledException)
                throw new AggregateException("The original approval presenter synchronously faulted.", error);
            if (presentation is { IsCanceled: true } && wait.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
                if (_lifetime.IsCancellationRequested) return ChatToolApprovalOutcome.SessionClosed;
                return ChatToolApprovalOutcome.Expired;
            }
            throw;
        }
        finally { Presenting.Value = previousPresenter; }
        var decision = lease.Finish();
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_closed || _lifetime.IsCancellationRequested || request.Ended) return ChatToolApprovalOutcome.SessionClosed;
            if (wait.IsCancellationRequested) return ChatToolApprovalOutcome.Expired;
            if (decision == ChatToolApprovalDecision.AllowForSession) _allowedTools.Add(call.Name);
        }
        return decision is ChatToolApprovalDecision.Allow or ChatToolApprovalDecision.AllowForSession
            ? ChatToolApprovalOutcome.Allowed : decision is null ? ChatToolApprovalOutcome.PromptClosed : ChatToolApprovalOutcome.Denied;
    }

    public Task CloseAsync()
    {
        if (ReferenceEquals(Presenting.Value, this))
            throw new InvalidOperationException("An approval presenter cannot join its own original session. Close the prompt and let its external owner join the send and session.");
        TaskCompletionSource start;
        Task actual;
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closed = true;
            _allowedTools.Clear();
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _close = CloseBodyAsync(_originals.ToArray(), start.Task);
        }
        start.SetResult();
        return actual;
    }
    private async Task CloseBodyAsync((Task<ChatToolApprovalOutcome> Actual, CancellationToken Business)[] originals, Task start)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        try { _lifetime.Cancel(); }
        catch (Exception error) { Retain(failures, error); }
        foreach (var captured in originals)
        {
            var original = captured.Actual;
            try { await original.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (original.IsFaulted) Retain(failures, original.Exception!);
                else if (!original.IsCanceled || error is not OperationCanceledException canceled ||
                    canceled.CancellationToken != captured.Business || !captured.Business.IsCancellationRequested) Retain(failures, error);
            }
        }
        _lifetime.Dispose();
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("The original chat approval source did not settle cleanly.", failures);
    }
    public ValueTask DisposeAsync() => new(CloseAsync());
    private static void ThrowOriginalFault(AggregateException actual)
    {
        if (actual.InnerExceptions.Count == 1 && actual.InnerExceptions[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(actual.InnerExceptions[0]).Throw();
        throw new AggregateException("The original approval presentation faulted.", actual.InnerExceptions);
    }
    private static void Retain(List<Exception> failures, Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } aggregate)
        { foreach (var inner in aggregate.InnerExceptions) Retain(failures, inner); return; }
        if (!failures.Any(prior => ReferenceEquals(prior, error))) failures.Add(error);
    }
}

internal enum ChatToolApprovalOutcome { Allowed, Denied, PromptClosed, SessionClosed, Expired }
