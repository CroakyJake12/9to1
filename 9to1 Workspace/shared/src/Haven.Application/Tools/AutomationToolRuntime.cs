/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/AutomationToolRuntime.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns AutomationToolRuntime. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.Text.Json;
using Haven.Core;
using Haven.Application.Automations;

namespace Haven.Application;

/// <summary>
/// Represents automation tool runtime and keeps its related state and behavior together.
/// </summary>
public sealed class AutomationToolRuntime(
    IAutomationRepository automations,
    IWorkspaceStateRepository workspaceState,
    IPermissionDecisionEngine permissions,
    IConversationSafetyService safety,
    IAuthenticatedResourceActorSource? actors = null,
    IAutomationDefinitionReviewCaller? ownerCaller = null)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (IAutomationDefinitionCallerReview Review, IAutomationDefinitionCallerSelection Selection, string PermissionScope)> _reviews = new();

    private readonly SemaphoreSlim _invocations = new(1, 1);
    private readonly Dictionary<(Guid ConversationId, string CallId), (string Digest, Guid StoreId, AuthenticatedResourceActor Actor, string Output)> _calls = [];
    private IAutomationDefinitionCallerReview? _lastIssuedReview;

    public Task<AutomationDefinitionCallerOutcome> FinishReviewAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        throw new UnauthorizedAccessException("The original issued automation selection is required.");

    public async Task<AutomationDefinitionCallerOutcome> FinishReviewAsync(Guid operationId,
        IAutomationDefinitionCallerSelection originalSelection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalSelection);
        if (ownerCaller is null) throw new InvalidOperationException("Automation definition ownership is unavailable.");
        await ownerCaller.RequireCurrentAsync(originalSelection, cancellationToken).ConfigureAwait(false);
        if (!_reviews.TryGetValue(operationId, out var retained) || !ReferenceEquals(retained.Selection, originalSelection))
            throw new UnauthorizedAccessException("The exact original review selection is unavailable; no creation is replayed.");
        return await retained.Review.FinishAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves definitions for the current operation.
    /// </summary>
    public IReadOnlyList<OllamaToolDefinition> GetDefinitions(bool enableAutomations, bool enableReusableTasks)
    {
        _ = automations; _ = workspaceState; // Existing constructor compatibility; these raw writers confer no owning grant.
        var result = new List<OllamaToolDefinition>();
        if (enableAutomations)
        {
            result.Add(Definition("automation_create", "Create a reviewable Haven Scheduled Action after the user has supplied or confirmed its schedule.",
                new()
                {
                    ["name"] = StringProperty("Short action name."),
                    ["instruction"] = StringProperty("Complete action instruction."),
                    ["schedule_kind"] = StringProperty("Once, Hourly, Daily, Weekly, or ConditionWatch."),
                    ["schedule_json"] = StringProperty("Schedule configuration JSON, for example {\"time\":\"08:00\"}.")
                }, "name", "instruction", "schedule_kind", "schedule_json"));
        }
        if (enableReusableTasks)
        {
            result.Add(Definition("task_create", "Create a reusable Haven Task that runs only when the user chooses it.",
                new()
                {
                    ["name"] = StringProperty("Short task name."),
                    ["description"] = StringProperty("The task outcome."),
                    ["instruction"] = StringProperty("Complete reusable task instructions.")
                }, "name", "instruction"));
            result.Add(Definition("task_list", "List enabled reusable Haven workflows available to this task group or project.", new()));
        }
        if (enableAutomations || enableReusableTasks)
            result.Add(Definition("automation_finish_review", "Finish only the retained original Home-reviewed definition operation; no new proposal, scheduled run or enabled graph is authorized.",
                new() { ["operation_id"] = StringProperty("Exact retained definition review operation UUID.") }, "operation_id"));
        return result;
    }

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public Task<WorkspaceToolResult> ExecuteAsync(OllamaToolCall call, HavenMode mode, Guid conversationId,
        Guid? containerId, CancellationToken cancellationToken) =>
        Task.FromResult(new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), Label(call.Name),
            "Original automation selection unavailable", false, TimeSpan.Zero, DateTimeOffset.UtcNow),
            "Automation tool unavailable: an original issuer-owned selection must be captured before model dispatch."));

    public async Task<WorkspaceToolResult> ExecuteAsync(
        OllamaToolCall call,
        HavenMode mode,
        Guid conversationId,
        Guid? containerId,
        IAutomationDefinitionCallerSelection originalSelection,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var held = false;
        try
        {
            call = CaptureCall(call); // Detached original invocation before any actor, safety or approval await.
            ArgumentNullException.ThrowIfNull(originalSelection);
            _ = actors; // Compatibility parameter; never adopt ambient actor after model dispatch.
            if (ownerCaller is null) throw new InvalidOperationException("Automation definition ownership is unavailable.");
            await ownerCaller.RequireCurrentAsync(originalSelection, cancellationToken).ConfigureAwait(false);
            if (call.Name is "automation_create" or "task_create")
            {
                if (string.IsNullOrWhiteSpace(call.Id)) throw new InvalidOperationException("An original tool invocation ID is required for review; no creation is replayed.");
                await _invocations.WaitAsync(cancellationToken).ConfigureAwait(false); held = true;
                var digest = JsonSerializer.Serialize(new { call.Name, Arguments = call.Arguments.OrderBy(item => item.Key, StringComparer.Ordinal) });
                if (_calls.TryGetValue((conversationId, call.Id), out var retained))
                {
                    if (retained.Digest != digest || retained.Actor != originalSelection.Actor || retained.StoreId != originalSelection.StoreId)
                        throw new UnauthorizedAccessException("The original tool invocation context changed.");
                    return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), Label(call.Name), "Original review retained", false,
                        Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow), retained.Output);
                }
                if (_calls.Count >= 1000) throw new InvalidOperationException("Automation review invocation capacity reached.");
                _lastIssuedReview = null;
                await safety.EnsureMayActAsync(conversationId, $"tool.{call.Name}", cancellationToken).ConfigureAwait(false);
                var decision = permissions.Evaluate(
                    call.Name == "automation_create" ? "tasks.automation.create" : "tasks.reusable.create",
                    CapabilityRiskClass.Consequential,
                    requiresPermission: true,
                    $"Allow Haven to execute {call.Name}.");
                if (decision.Kind != PermissionDecisionKind.Allowed)
                    throw new InvalidOperationException("Explicit scoped approval is required before this action can persist changes.");
            }

            var output = call.Name switch
            {
                "automation_create" => await CreateAutomationAsync(call, mode, containerId, originalSelection!, cancellationToken).ConfigureAwait(false),
                "task_create" => await CreateReusableTaskAsync(call, containerId, originalSelection!, cancellationToken).ConfigureAwait(false),
                "task_list" => await ListReusableTasksAsync(containerId, originalSelection!, cancellationToken).ConfigureAwait(false),
                "automation_finish_review" => await FinishRetainedReviewAsync(call, conversationId, originalSelection, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unknown automation tool '{call.Name}'.")
            };
            if (call.Name is ("automation_create" or "task_create") && originalSelection is not null && _lastIssuedReview is not null)
                _calls.Add((conversationId, call.Id!), (JsonSerializer.Serialize(new { call.Name, Arguments = call.Arguments.OrderBy(item => item.Key, StringComparer.Ordinal) }),
                    originalSelection.StoreId, originalSelection.Actor, output));
            return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), Label(call.Name), output.Split('\n')[0], call.Name == "task_list" || call.Name == "automation_finish_review" && IsKnownCommitted(output),
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow), output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), Label(call.Name), ex.Message, false,
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow), "Automation tool error: " + ex.Message);
        }
        finally { if (held) _invocations.Release(); }
    }

    /// <summary>
    /// Creates automation async with the invariants required by its callers.
    /// </summary>
    private static OllamaToolCall CaptureCall(OllamaToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.Name.Length > 128 || call.Id?.Length > 256) throw new ArgumentException("Automation tool identity exceeds its bound.");
        var captured = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var total = 0;
        foreach (var entry in call.Arguments)
        {
            if (captured.Count >= 8 || entry.Key.Length > 64 || entry.Value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Automation tool arguments exceed the supported scalar bound.");
            var text = entry.Value.GetString() ?? string.Empty;
            total = checked(total + text.Length);
            if (text.Length > 65536 || total > 131072) throw new ArgumentException("Automation tool text exceeds its bound.");
            captured.Add(entry.Key, JsonSerializer.SerializeToElement(text));
        }
        return new OllamaToolCall(call.Name, captured, call.Id);
    }

    private async Task<string> CreateAutomationAsync(OllamaToolCall call, HavenMode mode, Guid? containerId, IAutomationDefinitionCallerSelection selection, CancellationToken cancellationToken)
    {
        var name = RequiredText(call, "name");
        var instruction = RequiredText(call, "instruction");
        if (!Enum.TryParse<AutomationScheduleKind>(RequiredText(call, "schedule_kind"), true, out var kind))
            throw new ArgumentException("schedule_kind must be Once, Hourly, Daily, Weekly, or ConditionWatch.");
        var scheduleJson = RequiredText(call, "schedule_json");
        using var schedule = JsonDocument.Parse(scheduleJson);
        var now = DateTimeOffset.UtcNow;
        // Schedule is retained authored data; no scheduler/publication authority follows review.
        DateTimeOffset? next = null;
        var item = new AutomationDefinition(Guid.NewGuid(), name, mode, instruction, kind, scheduleJson, next, containerId, false, now, now);
        var review = await ownerCaller!.ReviewAsync(selection, item, 0, AutomationDefinitionChangeKind.Create, cancellationToken).ConfigureAwait(false);
        _reviews.TryAdd(review.OperationId, (review, selection, "tasks.automation.create"));
        _lastIssuedReview = review;
        return $"Review pending for disabled Automation '{name}'. Operation: {review.OperationId:D}; Home request: {review.RequestId}. No definition or scheduled run has been created.";
    }

    /// <summary>
    /// Creates a reusable task while retaining compatibility with the existing local task rows.
    /// </summary>
    private async Task<string> CreateReusableTaskAsync(OllamaToolCall call, Guid? containerId, IAutomationDefinitionCallerSelection selection, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var item = new ReusableTaskDefinition(Guid.NewGuid(), RequiredText(call, "name"), Text(call, "description"), RequiredText(call, "instruction"),
            containerId, false, now, now);
        var review = await ownerCaller!.ReviewAsync(selection, item, 0, AutomationDefinitionChangeKind.Create, cancellationToken).ConfigureAwait(false);
        _reviews.TryAdd(review.OperationId, (review, selection, "tasks.reusable.create"));
        _lastIssuedReview = review;
        return $"Review pending for disabled workflow '{item.Name}'. Operation: {review.OperationId:D}; Home request: {review.RequestId}. No workflow execution is authorized.";
    }

    /// <summary>
    /// Lists reusable tasks asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<string> FinishRetainedReviewAsync(OllamaToolCall call, Guid conversationId,
        IAutomationDefinitionCallerSelection originalSelection, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(RequiredText(call, "operation_id"), out var operationId) || operationId == Guid.Empty)
            throw new ArgumentException("operation_id must identify the retained original definition review.");
        if (!_reviews.TryGetValue(operationId, out var retained) || !ReferenceEquals(retained.Selection, originalSelection))
            throw new UnauthorizedAccessException("The exact original review selection is unavailable.");
        await safety.EnsureMayActAsync(conversationId, "tool.automation_finish_review", cancellationToken).ConfigureAwait(false);
        var decision = permissions.Evaluate(retained.PermissionScope, CapabilityRiskClass.Consequential,
            requiresPermission: true, "Finish the retained original definition review.");
        if (decision.Kind != PermissionDecisionKind.Allowed)
            throw new InvalidOperationException("The original scoped permission is required to finish this review.");
        var result = await FinishReviewAsync(operationId, originalSelection, cancellationToken).ConfigureAwait(false);
        // Nullable outcome and exact audit status remain explicit. The original owner operation never replays known effects.
        return JsonSerializer.Serialize(new { result.Committed, result.Code });
    }

    private static bool IsKnownCommitted(string output)
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("Committed").ValueKind == JsonValueKind.True;
    }

    private async Task<string> ListReusableTasksAsync(Guid? containerId, IAutomationDefinitionCallerSelection selection, CancellationToken cancellationToken)
    {
        var library = await ownerCaller!.LoadLibraryAsync(selection, new(IncludeDisabled: false, Limit: 100), cancellationToken).ConfigureAwait(false);
        var items = library.Tasks.Items.Where(item => item.Value.ContainerId is null || item.Value.ContainerId == containerId).ToArray();
        return items.Length == 0 ? "No enabled reusable definitions are available for inspection." : string.Join('\n',
            items.Select(item => $"{item.Value.Name}: {item.Value.Description}\nInstruction: {item.Value.Instruction}\nState: {(item.RequiresRecovery ? "NeedsRecovery" : item.Value.OperationalState.ToString())}; execution requires separate owning run authority."));
    }

    /// <summary>
    /// Performs the calculate initial run step owned by this component.
    /// </summary>
    private static DateTimeOffset? CalculateInitialRun(AutomationScheduleKind kind, JsonElement schedule, DateTimeOffset now)
    {
        if (kind == AutomationScheduleKind.ConditionWatch) return now.AddMinutes(5);
        if (kind == AutomationScheduleKind.Hourly) return now.AddHours(1);
        if (kind == AutomationScheduleKind.Once)
        {
            if (schedule.TryGetProperty("at", out var at) && DateTimeOffset.TryParse(at.GetString(), out var timestamp)) return timestamp;
            return now.AddMinutes(5);
        }
        var time = schedule.TryGetProperty("time", out var timeElement) && TimeOnly.TryParse(timeElement.GetString(), out var parsed) ? parsed : new TimeOnly(8, 0);
        var local = now.ToLocalTime();
        var candidate = new DateTimeOffset(local.Year, local.Month, local.Day, time.Hour, time.Minute, 0, local.Offset);
        if (candidate <= local) candidate = candidate.AddDays(kind == AutomationScheduleKind.Weekly ? 7 : 1);
        return candidate.ToUniversalTime();
    }

    /// <summary>
    /// Performs the definition step owned by this component.
    /// </summary>
    private static OllamaToolDefinition Definition(string name, string description, Dictionary<string, object> properties, params string[] required) => new(name, description, properties, required);
    /// <summary>
    /// Performs the string property step owned by this component.
    /// </summary>
    private static Dictionary<string, object> StringProperty(string description) => new() { ["type"] = "string", ["description"] = description };
    /// <summary>
    /// Performs the required text step owned by this component.
    /// </summary>
    private static string RequiredText(OllamaToolCall call, string name) => string.IsNullOrWhiteSpace(Text(call, name)) ? throw new ArgumentException($"{name} is required.") : Text(call, name);
    /// <summary>
    /// Performs the text step owned by this component.
    /// </summary>
    private static string Text(OllamaToolCall call, string name) => call.Arguments.TryGetValue(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString() : string.Empty;
    /// <summary>
    /// Performs the label step owned by this component.
    /// </summary>
    private static string Label(string name) => name switch { "automation_create" => "Automation review", "task_create" => "Workflow review", "task_list" => "Listed reusable workflows", "automation_finish_review" => "Original definition review completion", _ => name };
}
