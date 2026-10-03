using System.Collections.Frozen;
using Haven.Core;

namespace Haven.Application;

/// <summary>Observations supplied by the registered Den adapter for one actual, durably
/// committed step. None of these identifiers, capability metadata or limits grant authority.</summary>
public sealed record CanonicalAgentChatStepRequest(Guid RootRunId, Guid AttemptId, Guid ConversationId,
    string Prompt, string AgentName, string Instructions, ModelDescriptor Model,
    IReadOnlyList<CapabilityDefinition> Capabilities, long? MaxToolCalls, TimeSpan? MaxTime);

/// <summary>Actual completed original Chat observations. This carries no owner mutation,
/// checkpoint, retry/idempotency or execution permission proof.</summary>
public sealed record CanonicalAgentChatStepResult(ChatMessage? Assistant,
    AgentActivityObservation Observation, CapabilityPreflightResult? Preflight);

public sealed partial class AgentTaskRuntimeService
{
    /// <summary>Resolve the actual compatibility descriptor exposed by this SAME existing
    /// model client. The provider-qualified/raw identity follows the maintained provider bridge;
    /// no descriptor or alias is fabricated. Home admission and its lifetime cover the inventory.</summary>
    public async Task<ModelDescriptor> ResolveCanonicalDenModelAsync(ProviderModelDescriptor selected,
        Guid conversationId, object originalExecutionAuthority, CancellationToken originalLifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (!originalLifetime.CanBeCanceled)
            throw new UnauthorizedAccessException("The original Home model lifetime is unavailable.");
        originalLifetime.ThrowIfCancellationRequested();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalLifetime, cancellationToken);
        var name = selected.ProviderId.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? selected.Name : selected.Key;
        var captured = selected.Model with { Capabilities = selected.Capabilities.ToFrozenSet() };
        using var admission = await ChatOriginalExecutionBoundary.OpenAsync(canonicalAdmissions,
            originalExecutionAuthority, conversationId, name, lifetime.Token).ConfigureAwait(false);
        var current = await models.GetModelsAsync(admission.Token).ConfigureAwait(false);
        var matches = current.Where(value => value.Name == name).ToArray();
        if (matches.Length != 1 || matches[0].SizeBytes != captured.SizeBytes ||
            matches[0].Family != captured.Family || matches[0].ParameterSize != captured.ParameterSize || matches[0].Quantization != captured.Quantization ||
            matches[0].ModifiedAt != captured.ModifiedAt || !matches[0].Capabilities.SetEquals(captured.Capabilities))
            throw new InvalidOperationException("The original current compatibility model changed or is unavailable.");
        var descriptor = matches[0] with { Capabilities = matches[0].Capabilities.ToFrozenSet() };
        await admission.DemandAsync(name, null, admission.Token).ConfigureAwait(false);
        return descriptor;
    }

    /// <summary>Collects the actual original stream and owning dispatch observations. No
    /// second AgentRun or tool executor is created. Deferred/incomplete facts remain explicit.</summary>
    public async Task<CanonicalAgentChatStepResult> RunCanonicalDenStepAsync(
        CanonicalAgentChatStepRequest request, object originalExecutionAuthority,
        CancellationToken originalLifetime, CancellationToken cancellationToken = default)
    {
        var activities = new List<ToolActivity>(); ChatMessage? assistant = null;
        CapabilityPreflightResult? preflight = null;
        await foreach (var item in ExecuteCanonicalDenStepAsync(request, originalExecutionAuthority,
            originalLifetime, cancellationToken).ConfigureAwait(false))
        {
            if (item.ToolActivity is { } activity) activities.Add(activity);
            if (item.Message is { Role: MessageRole.Assistant } message) assistant = message;
            if (item.PreflightResult is { } check) preflight = check;
        }
        var observation = AgentActivityObservation.Capture(request.RootRunId, activities,
            assistant is not null && preflight?.IsCompatible != false);
        return new(assistant, observation, preflight);
    }

    /// <summary>Runs the original canonical Den step through the existing Chat/tool loop.
    /// It never creates or persists another AgentRun. The non-null opaque Home token is
    /// checked by the registered Chat admission before model work and each actual callback.
    /// The token's original host lifetime must remain cancellable and live throughout I/O.</summary>
    public async IAsyncEnumerable<ChatStreamEvent> ExecuteCanonicalDenStepAsync(
        CanonicalAgentChatStepRequest request, object originalExecutionAuthority,
        CancellationToken originalLifetime,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(originalExecutionAuthority);
        if (request.RootRunId == Guid.Empty || request.AttemptId == Guid.Empty || request.ConversationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.AgentName) ||
            request.Instructions is null || request.Model is null || request.Capabilities is null ||
            request.MaxToolCalls is < 0 || request.MaxTime is { } invalidTime &&
                (invalidTime < TimeSpan.Zero || invalidTime.TotalMilliseconds > uint.MaxValue - 1L))
            throw new ArgumentException("An exact committed canonical run, attempt, conversation and valid step are required.", nameof(request));
        if (!originalLifetime.CanBeCanceled)
            throw new UnauthorizedAccessException("Canonical Den execution requires the original Home host lifetime.");
        originalLifetime.ThrowIfCancellationRequested(); cancellationToken.ThrowIfCancellationRequested();
        var captured = request with
        {
            Model = request.Model with { Capabilities = request.Model.Capabilities.ToFrozenSet() },
            Capabilities = Array.AsReadOnly(request.Capabilities.ToArray())
        };
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalLifetime, cancellationToken);
        if (captured.MaxTime is { } maxTime)
        {
            if (maxTime == TimeSpan.Zero) lifetime.Cancel();
            else lifetime.CancelAfter(maxTime);
        }
        lifetime.Token.ThrowIfCancellationRequested();
        var active = captured.Capabilities.Select(ActiveCapability.FromDefinition).ToArray();
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(captured.ConversationId, HavenMode.Chat, ConversationKind.Chat,
            $"Agent · {captured.AgentName}", null, null, false, true, now, now);
        // This only narrows Chat's existing loop bound. Home separately counts actual calls
        // before every callback/retry; ActionLimit's minimum of one cannot grant a zero budget.
        var actionLimit = (int)Math.Min(100L, Math.Max(1L, captured.MaxToolCalls ?? 24L));
        await foreach (var item in chat.SendAsync(conversation, captured.Prompt, captured.Model,
            EffortLevel.Medium, active, captured.AgentName, captured.Instructions, DuoMode.Solo,
            workspaceRoot: null, projectContext: null, projectInstructions: null, images: null,
            cancellationToken: lifetime.Token, generationOptions: new GenerationOptions(ActionLimit: actionLimit),
            filePermission: PermissionMode.Ask, commandPermission: PermissionMode.Ask,
            browserPermission: PermissionMode.Ask, availableCapabilities: active,
            originalExecutionAuthority: originalExecutionAuthority).ConfigureAwait(false))
            yield return item;
    }
}
