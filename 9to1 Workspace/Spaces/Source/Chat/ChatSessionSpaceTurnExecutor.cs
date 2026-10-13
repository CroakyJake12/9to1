using System.Runtime.ExceptionServices;
using System.Net;
using System.Net.Http;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Chat;

/// <summary>Captured from the host's existing configuration/permission owners for this exact
/// send. It contains no default tool grant. Attachments and approval requests must originate
/// from their actual live owners before the session is invoked.</summary>
public sealed record ChatSpaceTurnContext(
    EffortLevel Effort,
    string AgentName,
    string AgentInstructions,
    PermissionMode FilePermission,
    PermissionMode CommandPermission,
    PermissionMode BrowserPermission)
{
    public Action<Action>? OriginalSynchronousScope { get; init; }
    public IReadOnlyCollection<ActiveCapability> Capabilities { get; init; } = [];
    public IReadOnlyCollection<ActiveCapability> AvailableCapabilities { get; init; } = [];
    public IReadOnlyCollection<ToolCapability>? ExplicitCapabilities { get; init; }
    public IReadOnlyCollection<ActivePrompt> Prompts { get; init; } = [];
    public string? RegisteredContext { get; init; }
    public GenerationOptions? GenerationOptions { get; init; }
    public IReadOnlyList<ChatMessage>? TransientHistory { get; init; }
    public IReadOnlyList<string>? Images { get; init; }
}

/// <summary>The shared ordinary Chat adapter. The injected service retains its real
/// model governance, capability/approval loop, persistence and source admission.</summary>
public sealed class ChatSessionSpaceTurnExecutor(
    ChatSessionService sessions,
    IConversationRepository conversations,
    Func<ChatSpaceTurnRequest, CancellationToken, Task<ChatSpaceTurnContext>> captureContext)
    : IChatSpaceStreamingTurnExecutor
{
    public Task ExecuteAsync(ChatSpaceTurnRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request, static (_, _) => Task.CompletedTask, cancellationToken);

    public async Task ExecuteAsync(ChatSpaceTurnRequest request,
        Func<ChatStreamEvent, CancellationToken, Task> observe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observe);
        cancellationToken.ThrowIfCancellationRequested();
        var conversation = request.Conversation;
        if (!ConversationScope.GeneralChat.Matches(conversation) || conversation.IsArchived ||
            conversation.SpaceId is { } space && space != SpaceRegistry.ChatSpaceId)
            throw new InvalidOperationException("The ordinary Chat executor requires the active canonical Chat conversation.");
        if (!conversation.IsTemporary)
        {
            var current = await conversations.GetAsync(conversation.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The original Chat conversation is no longer available.");
            if (current != conversation)
                throw new InvalidOperationException("The Chat conversation changed before dispatch. Reload it before sending.");
            conversation = current;
        }
        var actualRequest = request with { Conversation = conversation };
        var context = await captureContext(actualRequest, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original Chat turn context is unavailable.");
        cancellationToken.ThrowIfCancellationRequested();
        if (request.AttachmentIds.Count > 0 && context.GenerationOptions?.OriginalAttachmentInput is null)
            throw new InvalidOperationException("The selected attachments require their live canonical input owner.");
        var scope = context.OriginalSynchronousScope
            ?? throw new InvalidOperationException("The original Chat host scope is unavailable.");
        var options = context.GenerationOptions
            ?? throw new InvalidOperationException("The original governed Chat generation options are unavailable.");
        // Recheck after potentially asynchronous context/approval preparation. The captured
        // object remains the exact one to which any live input/approval request was issued.
        if (!conversation.IsTemporary &&
            await conversations.GetAsync(conversation.Id, cancellationToken).ConfigureAwait(false) != conversation)
            throw new InvalidOperationException("The original Chat changed during turn preparation.");
        var original = sessions.CreateOriginalOrdinaryConversation(conversation, request.Prompt,
            request.Model, context.Effort, context.Capabilities, context.AgentName, context.AgentInstructions,
            DuoMode.Solo, null, null, null, context.Images, options, cancellationToken, scope,
            prompts: context.Prompts, registeredContext: context.RegisteredContext,
            filePermission: context.FilePermission, commandPermission: context.CommandPermission,
            browserPermission: context.BrowserPermission, explicitCapabilities: context.ExplicitCapabilities,
            availableCapabilities: context.AvailableCapabilities, transientHistory: context.TransientHistory);
        var completed = false;
        var failures = new List<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var canceled = new List<(Task Actual, OperationCanceledException Cause, bool Body)>();
        void Retain(Exception error, Task? actual = null, bool body = false)
        {
            if (error is OperationCanceledException cancel && actual?.IsCanceled == true)
            { canceled.Add((actual, cancel, body)); return; }
            void Add(Exception cause)
            {
                if (cause is AggregateException { InnerExceptions.Count: > 0 } group)
                { foreach (var child in group.InnerExceptions) Add(child); }
                else if (seen.Add(cause)) failures.Add(cause);
            }
            if (actual?.Exception is { } envelope)
                foreach (var cause in envelope.InnerExceptions) Add(cause);
            else Add(error);
        }
        IAsyncEnumerator<ChatStreamEvent>? iterator = null;
        Task? actual = null;
        var bodyMove = false;
        try
        {
            iterator = original.ConsumeOriginal().GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bodyMove = true;
                var move = iterator.MoveNextAsync().AsTask(); actual = move;
                if (!await move.ConfigureAwait(false)) break;
                var item = iterator.Current;
                bodyMove = false;
                actual = observe(item, cancellationToken);
                await actual.ConfigureAwait(false);
                if (item.Kind == ChatStreamEventKind.AssistantCompleted) completed = true;
                if (item.Kind == ChatStreamEventKind.PreflightFailed)
                    throw new InvalidOperationException("The model's required capabilities are unavailable for this turn.");
            }
        }
        catch (Exception error) { Retain(error, actual, bodyMove); }
        // Disposal and source settlement are independent originals. A failed presenter or
        // provider must not bypass either; aggregate all actual causes without replaying work.
        Task? close = null;
        try
        {
            var ownedIterator = (IAsyncDisposable?)iterator ?? original.OriginalBodyIterator;
            if (ownedIterator is not null)
            { close = ownedIterator.DisposeAsync().AsTask(); await close.ConfigureAwait(false); }
        }
        catch (Exception error) { Retain(error, close); }
        Task? join = null;
        try { join = original.JoinOriginalSourcesAsync(); await join.ConfigureAwait(false); }
        catch (Exception error) { Retain(error, join); }
        OperationCanceledException? expectedCancellation = null;
        foreach (var row in canceled)
        {
            if (row.Body && original.IsExpectedOriginalBodyCancellation(row.Actual, row.Cause) ||
                !row.Body && row.Cause.CancellationToken == cancellationToken && cancellationToken.IsCancellationRequested && join?.IsCompletedSuccessfully == true)
                expectedCancellation ??= row.Cause;
            else if (seen.Add(row.Cause)) failures.Add(row.Cause);
        }
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0)
            throw new AggregateException("The original Chat sources did not settle successfully.", failures);
        if (expectedCancellation is not null) ExceptionDispatchInfo.Capture(expectedCancellation).Throw();
        if (!completed)
            throw new InvalidOperationException("The original Chat stream ended without a completed response.");

    }
}

/// <summary>Uses the same governed provider inventory cache and preference owner as the host.</summary>
public sealed class ChatSessionSpaceModelCatalog(
    ChatModelInventoryCache inventory,
    Func<CancellationToken, Task<string?>> readPreferredModel) : IChatSpaceModelCatalog
{
    public async Task<ChatSpaceModelInventory> GetInventoryAsync(CancellationToken cancellationToken)
    {
        // A failed account/preferences owner is not a provider outage.
        var preferred = await readPreferredModel(cancellationToken).ConfigureAwait(false);
        try
        {
            var models = await inventory.GetAsync(false, cancellationToken).ConfigureAwait(false);
            return new(models, preferred)
            {
                UnavailableReason = models.Count == 0
                    ? "No model is available with the current provider and privacy settings. Saved chats remain available."
                    : null
            };
        }
        catch (HttpRequestException error) when (!cancellationToken.IsCancellationRequested &&
            (error.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout
                || error.StatusCode is null && error.HttpRequestError is
                    HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError))
        {
            // Authentication, authorization, malformed replies and unknown failures
            // remain errors. No model descriptor or response is fabricated here.
            return new([], preferred)
            {
                UnavailableReason = "Models are temporarily unavailable. Saved chats remain available; refresh to retry model discovery."
            };
        }
    }
}
