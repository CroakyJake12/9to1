using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Chat;

public sealed partial class ChatSpaceController
{
    private const int MaxSendQueue = 8;
    private sealed record Submission(View View, ChatSpaceTurnRequest Request, ChatSpaceComposer Composer);
    private sealed class TurnQueue
    {
        public Queue<Submission> Pending { get; } = new();
        public Dictionary<Guid, ChatSpaceMessage> Overlay { get; } = [];
        public bool AcceptProgress;
        public CancellationTokenSource? ActiveCancellation;
        public bool HasActiveItem;
        public bool StopRequested;
    }
    private readonly Dictionary<Guid, TurnQueue> _turns = [];
    private sealed class InlineProgress(Action<ChatStreamEvent> report) : IProgress<ChatStreamEvent>
    { public void Report(ChatStreamEvent value) => report(value); }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        if (State.Composer.IsEditing && State.Composer.EditingMessageId is { } editId)
        { await ConfirmEditAsync(editId, cancellationToken).ConfigureAwait(false); return; }
        var ensured = await EnsureComposerConversationAsync(cancellationToken).ConfigureAwait(false);
        if (ensured is not { } view) return;
        TurnQueue? queue = null; Submission? submission = null; var start = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Change(state =>
            {
                if (state.IsLoading || state.Conversation is null) throw new InvalidOperationException("Wait for the conversation to finish opening.");
                if (state.IsSafetyLocked) throw new InvalidOperationException("This conversation is safety-locked. Copy and delete remain available.");
                if (_mutations.Contains(state.Conversation.Id)) throw new InvalidOperationException("Wait for the current conversation action to finish before sending.");
                if (_dualModel.IsActive) throw new NotSupportedException("Dual-model execution requires canonical conversation persistence and is not available on this CUI surface yet.");
                var composer = state.Composer;
                if (string.IsNullOrWhiteSpace(composer.Draft) && composer.Attachments.Count == 0) return state;
                var model = _modelDescriptors.FirstOrDefault(item => item.Name.Equals(composer.SelectedModelName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Choose an available model before sending.");
                var id = state.Conversation.Id;
                if (!_turns.TryGetValue(id, out queue)) { queue = new(); _turns.Add(id, queue); start = true; }
                if (queue.Pending.Count >= MaxSendQueue) throw new InvalidOperationException($"Message queue is full ({MaxSendQueue}).");
                submission = new(view, new(_conversations[id], composer.Draft.Trim(), model,
                    composer.Attachments.Select(item => item.Id).ToArray(), _threadSettings.ActionMode, _threadSettings.ResponseMode, BranchId: view.BranchId),
                    composer);
                _composerVersion++;
                queue.Pending.Enqueue(submission);
                return state with { Composer = composer with { Draft = string.Empty, Attachments = [], IsSending = true },
                    Status = new(start ? "Sending message" : "Message queued for this conversation", ChatSpaceStatusTone.Progress, true) };
            }, view);
            if (submission is null || queue is null) return;
            if (start) await RunQueueAsync(queue, submission.Request.Conversation.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, view); }
    }

    /// <summary>Requests cancellation of the active operation in the current conversation.</summary>
    public bool StopActiveResponse()
    {
        CancellationTokenSource? active;
        lock (_gate)
        {
            if (_state.Conversation is not { } conversation || !_turns.TryGetValue(conversation.Id, out var queue)) return false;
            active = queue.ActiveCancellation;
            if (active is null)
            {
                if (!queue.HasActiveItem && queue.Pending.Count == 0) return false;
                queue.StopRequested = true;
                return true;
            }
            if (active.IsCancellationRequested) return false;
        }
        try { active.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }
    }

    public bool CanStopActiveResponse
    {
        get
        {
            lock (_gate)
            {
                if (_state.Conversation is not { } conversation || !_turns.TryGetValue(conversation.Id, out var queue)) return false;
                return queue.ActiveCancellation is { } active
                    ? !active.IsCancellationRequested
                    : queue.HasActiveItem || queue.Pending.Count > 0;
            }
        }
    }

    private async Task RunQueueAsync(TurnQueue queue, Guid conversationId, CancellationToken token)
    {
        while (true)
        {
            Submission item;
            lock (_gate)
            {
                if (queue.Pending.Count == 0) { RemoveQueue(conversationId, queue); return; }
                item = queue.Pending.Dequeue(); queue.Overlay.Clear(); queue.AcceptProgress = true; queue.HasActiveItem = true;
            }
            using var activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            bool stopRequested;
            lock (_gate)
            {
                queue.ActiveCancellation = activeCancellation;
                stopRequested = queue.StopRequested;
                queue.StopRequested = false;
            }
            var operationToken = activeCancellation.Token;
            var acknowledged = false;
            var inputAccepted = 0;
            ChatMessage? acceptedUserInput = null;
            string? terminalRefusal = null;
            try
            {
                if (stopRequested) activeCancellation.Cancel();
                // A queued operation must not erase text composed after its admission.
                await SaveCurrentDraftAsync(item.View, operationToken).ConfigureAwait(false);
                var originalRequest = item.Request with { InputAccepted = (message, _) =>
                {
                    if (message.ConversationId != conversationId || message.Role != MessageRole.User)
                        throw new InvalidOperationException("The accepted input does not belong to this submitted conversation.");
                    Interlocked.Exchange(ref acceptedUserInput, message);
                    Interlocked.Exchange(ref inputAccepted, 1);
                    return Task.CompletedTask;
                } };
                await _backend.SendAsync(originalRequest, new InlineProgress(value =>
                {
                    if (value.Kind == ChatStreamEventKind.PreflightFailed)
                        terminalRefusal = "Capability check failed. " + string.Join("; ", value.PreflightResult?.Missing.Select(requirement => requirement.Reason) ?? []);
                    else if (value.Kind == ChatStreamEventKind.PermissionRequired)
                        terminalRefusal = "Permission is required before this request can continue.";
                    Stream(queue, item, value);
                }), operationToken).ConfigureAwait(false);
                if (terminalRefusal is not null) throw new InvalidOperationException(terminalRefusal);
                acknowledged = true;
                lock (_gate) queue.AcceptProgress = false;
                bool finished;
                lock (_gate)
                {
                    finished = queue.Pending.Count == 0;
                    if (finished) RemoveQueue(conversationId, queue);
                }
                await RefreshAsync(item.View, finished ? "Response complete" : "Sending queued message", operationToken).ConfigureAwait(false);
                await SaveCurrentDraftAsync(item.View, operationToken).ConfigureAwait(false);
                if (finished) return;
            }
            catch (Exception exception)
            {
                Submission[] pending;
                lock (_gate)
                { queue.AcceptProgress = false; pending = queue.Pending.ToArray(); queue.Pending.Clear(); RemoveQueue(conversationId, queue); }
                // Recover all admitted text; never overwrite a draft edited since admission.
                // Once execution acknowledged, a later presentation/read failure cannot make
                // that completed request eligible for recovery and accidental replay.
                var hasAcceptedInput = Volatile.Read(ref inputAccepted) != 0;
                var explicitlyStopped = exception is OperationCanceledException && activeCancellation.IsCancellationRequested && !token.IsCancellationRequested;
                var recovered = acknowledged || hasAcceptedInput ? pending : new[] { item }.Concat(pending).ToArray();
                var text = string.Join("\n\n", recovered.Select(value => value.Composer.Draft).Where(value => value.Length > 0));
                var attachments = recovered.SelectMany(value => value.Composer.Attachments).DistinctBy(value => value.Id).ToArray();
                ChatSpaceMessage[] partialMessagesToPersist = hasAcceptedInput && !acknowledged && exception is OperationCanceledException
                    ? queue.Overlay.Values.Where(message => message.Role == MessageRole.Assistant && message.IsStreaming &&
                        !string.IsNullOrWhiteSpace(message.Content)).ToArray()
                    : [];
                Exception? partialPersistenceFailure = null;
                foreach (var partial in partialMessagesToPersist)
                {
                    try
                    {
                        // This marks the durable message only. The ordinary Chat path has
                        // no canonical resumable RequestID/checkpoint here, so this must
                        // never be interpreted as permission to offer Resume.
                        var metadata = JsonSerializer.Serialize(new
                        {
                            responseState = explicitlyStopped ? "Stopped" : "Interrupted",
                            isPartial = true,
                            toolActivities = partial.ToolActivities
                        });
                        var saved = new ChatMessage(partial.Id, conversationId, MessageRole.Assistant, partial.Content,
                            partial.SpeakerLabel, partial.ModelName, metadata, DateTimeOffset.UtcNow);
                        await _backend.PersistStoppedPartialAsync(conversationId,
                            item.View.BranchId ?? throw new InvalidOperationException("The stopped response has no canonical branch."),
                            Volatile.Read(ref acceptedUserInput) ?? throw new InvalidOperationException("The canonical accepted user input is unavailable."),
                            saved, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception persistFailure)
                    {
                        partialPersistenceFailure = persistFailure;
                        break;
                    }
                }
                var failureStatus = acknowledged
                    ? "The response completed, but its view could not refresh. " + exception.Message
                    : exception is OperationCanceledException && partialMessagesToPersist.Length > 0
                        ? partialPersistenceFailure is null
                            ? hasAcceptedInput
                                ? (explicitlyStopped ? "Response stopped." : "Response interrupted.") + " Its partial text was saved in this conversation. The accepted user message remains saved and will not be replayed automatically."
                                : (explicitlyStopped ? "Response stopped." : "Response interrupted.") + " Its partial text was saved in this conversation; draft content was recovered. Review before resending."
                            : hasAcceptedInput
                                ? (explicitlyStopped ? "Response stopped." : "Response interrupted.") + " Partial text remains visible here but could not be saved. The accepted user message remains saved and will not be replayed automatically. Storage error: " + partialPersistenceFailure.Message
                                : (explicitlyStopped ? "Response stopped" : "Response interrupted") + ", but its partial text could not be saved. It remains visible here; draft content was recovered. Storage error: " + partialPersistenceFailure.Message
                        : exception is OperationCanceledException && explicitlyStopped && hasAcceptedInput
                            ? "Response stopped. The accepted user message remains saved; no partial reply was available to retain. It will not be replayed automatically."
                        : exception is OperationCanceledException && explicitlyStopped
                            ? "Response stopped before the user input was accepted; its draft content was recovered."
                        : hasAcceptedInput
                            ? "Your message was saved, but the response did not complete. Review the conversation before retrying. " + exception.Message
                            : exception is OperationCanceledException
                                ? "Sending interrupted. Draft content was recovered; review the conversation before resending."
                                : exception.Message;
                var failureTone = exception is OperationCanceledException && !acknowledged && partialPersistenceFailure is null
                    ? ChatSpaceStatusTone.Neutral
                    : ChatSpaceStatusTone.Error;
                Change(state =>
                {
                    var composer = state.Composer;
                    _composerVersion++;
                    // Cancellation can arrive after one or more streamed deltas. Do not
                    // leave that visible partial reply looking like a live response once
                    // the queue has been retired; retain its text and expose the actual
                    // stopped state instead of silently discarding the activity witness.
                    if (exception is OperationCanceledException)
                    {
                        foreach (var (messageId, message) in queue.Overlay.ToArray())
                            if (message.Role == MessageRole.Assistant && message.IsStreaming)
                            {
                                queue.Overlay[messageId] = message with
                                { IsStreaming = false, ProgressLabel = message.Content.Length > 0
                                    ? explicitlyStopped ? "Stopped · partial response" : "Interrupted · partial response"
                                    : explicitlyStopped ? "Stopped" : "Interrupted" };
                            }
                        if (state.Conversation is { } current && current.Id == conversationId)
                        {
                            var messages = current.Messages.Select(message => queue.Overlay.GetValueOrDefault(message.Id) ?? message).ToList();
                            messages.AddRange(queue.Overlay.Values.Where(message => !current.Messages.Any(existing => existing.Id == message.Id)));
                            state = state with { Conversation = current with { Messages = messages } };
                        }
                    }
                    return state with { Composer = composer with
                        { Draft = string.IsNullOrEmpty(text) ? composer.Draft : string.IsNullOrEmpty(composer.Draft) ? text : composer.Draft + "\n\n" + text,
                          Attachments = composer.Attachments.Concat(attachments).DistinctBy(value => value.Id).ToArray(), IsSending = _turns.ContainsKey(conversationId) },
                        Status = new(failureStatus, failureTone, true) };
                }, item.View);
                bool current; lock (_gate) current = Matches(item.View);
                if (current) await SaveCurrentDraftAsync(item.View, CancellationToken.None).ConfigureAwait(false);
                else if (recovered.Length > 0)
                {
                    ChatSpaceComposer previous;
                    lock (_gate) previous = _draftWrites.TryGetValue((conversationId, item.View.BranchId), out var saved)
                        ? saved.Composer : item.Composer with { Draft = string.Empty, Attachments = [] };
                    await SaveDraftSnapshotAsync(conversationId, item.View.BranchId, previous with
                    {
                        Draft = string.IsNullOrEmpty(previous.Draft) ? text : previous.Draft + "\n\n" + text,
                        Attachments = previous.Attachments.Concat(attachments).DistinctBy(value => value.Id).ToArray()
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                if (exception is OperationCanceledException && token.IsCancellationRequested) throw;
                return;
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(queue.ActiveCancellation, activeCancellation)) queue.ActiveCancellation = null;
                    queue.HasActiveItem = false;
                }
            }
        }
    }
    private void RemoveQueue(Guid conversationId, TurnQueue queue)
    {
        if (_turns.TryGetValue(conversationId, out var actual) && ReferenceEquals(actual, queue))
            _turns.Remove(conversationId);
    }
    private void Stream(TurnQueue queue, Submission submission, ChatStreamEvent value)
    {
        Change(state =>
        {
            if (!queue.AcceptProgress || state.Conversation is not { } conversation) return state;
            var overlay = queue.Overlay;
            if (value.Kind is ChatStreamEventKind.UserMessage or ChatStreamEventKind.AssistantCompleted && value.Message is { } message)
            {
                if (message.ConversationId != conversation.Id) return state;
                overlay[message.Id] = new(message.Id, message.Role, message.Content,
                    message.Role == MessageRole.User ? "You" : message.AgentName ?? "Haven",
                    message.CreatedAt.LocalDateTime.ToString("g"), message.ModelName, [], [], [], message.Role == MessageRole.Assistant);
            }
            else if (value.Kind == ChatStreamEventKind.AssistantStarted && value.MessageId is { } started)
                overlay[started] = new(started, MessageRole.Assistant, string.Empty, value.Agent ?? "Haven", "Now", value.Model,
                    [], [], [], false, IsStreaming: true, ProgressLabel: "Preparing response…");
            else if (value.MessageId is { } id && overlay.TryGetValue(id, out var current))
                overlay[id] = value.Kind switch
                {
                    ChatStreamEventKind.AssistantDelta => current with { Content = current.Content + value.Delta, ProgressLabel = "Writing response…" },
                    ChatStreamEventKind.ThinkingDelta => current with { Thinking = (current.Thinking ?? string.Empty) + value.Thinking, ProgressLabel = "Reasoning…" },
                    ChatStreamEventKind.ToolActivity when value.ToolActivity is { } activity => current with
                        { ToolActivities = current.ToolActivities.Where(item => item.Id != activity.Id).Append(activity).ToArray(), ProgressLabel = activity.Title },
                    _ => current
                };
            var merged = conversation.Messages.Select(message => overlay.GetValueOrDefault(message.Id) ?? message).ToList();
            merged.AddRange(overlay.Values.Where(message => !conversation.Messages.Any(existing => existing.Id == message.Id)));
            var status = value.Kind == ChatStreamEventKind.PermissionRequired ? "Permission required for the next action."
                : value.Kind == ChatStreamEventKind.PreflightFailed ? "The request could not pass capability checks." : "Receiving response";
            return state with { Conversation = conversation with { Messages = merged }, Status = new(status, ChatSpaceStatusTone.Progress, false) };
        }, submission.View);
    }
}
