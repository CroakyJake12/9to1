using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>The actual saved-Agent caller for canonical child work. It owns its returned producer Task; the Task ledger stays authoritative.</summary>
public sealed partial class AgentTaskRuntimeService
{
    private sealed class OriginalDelegatedAgentRun(TaskRunAttemptAdmission parent, Guid agentId,
        string requestKey, string task, IReadOnlyList<string> scopes)
    {
        internal readonly object Gate = new();
        internal readonly TaskRunAttemptAdmission InitialParent = parent;
        internal readonly Guid AgentId = agentId;
        internal readonly string RequestKey = requestKey;
        internal readonly string Task = task;
        internal readonly IReadOnlyList<string> Scopes = scopes;
        internal Task<AgentRun>? Producer;
        internal TaskRunDelegatedAgentInput? Input;
        internal TaskRunDelegatedChildLinkAcknowledgment? Link;
        internal AgentCanonicalOriginal? ActualAgent;
        internal readonly List<Task> ParentCompletionAcknowledgments = [];
    }
    private readonly object _delegatedAgentAdmission = new();
    private readonly ConcurrentDictionary<(Guid Parent, string Key), OriginalDelegatedAgentRun> _delegatedAgentRuns = new();

    /// <summary>Starts one fixed child through the real existing Agent→Chat body. SAME request retries return the SAME producer, never replacement work.</summary>
    public Task<AgentRun> RunDelegatedAsync(TaskRunAttemptAdmission sameCurrentParent, Guid agentId,
        string requestKey, string task, IReadOnlyCollection<string>? requestedPermissionScopes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sameCurrentParent);
        if (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length > 256 || string.IsNullOrWhiteSpace(task))
            throw new ArgumentException("Bounded original delegation key and child work are required.");
        var scopes = Array.AsReadOnly((requestedPermissionScopes ?? []).Select(value => value.Trim())
            .Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        lock (_delegatedAgentAdmission)
        {
            var key = (sameCurrentParent.Snapshot.TaskId, requestKey);
            if (_delegatedAgentRuns.TryGetValue(key, out var previous))
            {
                if (!ReferenceEquals(previous.InitialParent, sameCurrentParent)
                    || previous.InitialParent.Snapshot.ContextId != sameCurrentParent.Snapshot.ContextId
                    || previous.InitialParent.Snapshot.ExecutionId != sameCurrentParent.Snapshot.ExecutionId)
                    throw new InvalidOperationException("Only the SAME privately issued parent may retrieve its original child result.");
                if (previous.AgentId != agentId || previous.Task != task
                    || !previous.Scopes.SequenceEqual(scopes, StringComparer.Ordinal))
                    throw new InvalidOperationException("An original child request key cannot be reused for different Agent input.");
                return previous.Producer ?? throw new InvalidOperationException("The actual original child producer is not published.");
            }
            if (_delegatedAgentRuns.Count >= 128)
                throw new InvalidOperationException("Retained child producers require owning inspection; unknown work cannot be discarded.");
            var original = new OriginalDelegatedAgentRun(sameCurrentParent, agentId, requestKey, task, scopes);
            _delegatedAgentRuns[key] = original;
            original.Producer = RunOriginalBusinessProcessOperation((operation, processToken) => RunOriginalDelegatedAgentBodyAsync(original, operation, processToken), cancellationToken);
            return original.Producer;
        }
    }

    private async Task<AgentRun> RunOriginalDelegatedAgentBodyAsync(OriginalDelegatedAgentRun original,
        AgentRuntimeOriginalCustody operation, CancellationToken token)
    {
        var agent = (await operation.AwaitAsync(() => catalog.GetAgentsAsync(token)).ConfigureAwait(false))
            .SingleOrDefault(value => value.Id == original.AgentId)
            ?? throw new InvalidOperationException("The actual saved child Agent is unavailable.");
        var installed = await operation.AwaitAsync(() => models.GetModelsAsync(token)).ConfigureAwait(false);
        var selected = ResolveModel(agent, installed)
            ?? throw new InvalidOperationException("No actual text-capable child model is available.");
        var policy = AgentExecutionPolicy.Parse(agent.PermissionsJson);
        var discovered = await operation.AwaitAsync(() => capabilityRegistry.DiscoverAsync(
            OperatingSystem.IsAndroid() ? CapabilityPlatform.Android : CapabilityPlatform.Windows, token)).ConfigureAwait(false);
        var definitions = discovered.Where(value => value.IsAgentUsable && policy.CapabilityKeys.Contains(value.Key))
            .Where(value => operation.Invoke(() => permissionEngine.Evaluate("capability:" + value.Key, value.RiskClass,
                value.Availability == CapabilityAvailability.PermissionRequired || value.RiskClass >= CapabilityRiskClass.Consequential,
                "Saved child Agent requested " + value.Name)).Kind != PermissionDecisionKind.Denied).ToArray();
        var capabilities = definitions.Select(ActiveCapability.FromDefinition).ToArray();
        var input = operation.Invoke(() => chat.CaptureOriginalDelegatedAgentInput(original.Task.Trim(), selected, capabilities,
            agent.Name, BuildExecutionInstructions(agent.Instructions, policy)));
        original.Input = input; // Detached actual input once; model/catalogue observations are never grants.
        var tasks = chat.OriginalDelegationTaskOwner;
        var intent = await operation.AwaitAsync(() => tasks.RegisterOriginalDelegationIntentAsync(original.InitialParent,
            original.RequestKey, input.Digest, original.Task, original.Scopes, token)).ConfigureAwait(false);
        var creation = await operation.AwaitAsync(() => tasks.CreateOriginalDelegatedChildAsync(intent, token)).ConfigureAwait(false);
        var link = await operation.AwaitAsync(() => tasks.LinkOriginalDelegatedChildAsync(creation, token)).ConfigureAwait(false);
        original.Link = link;
        var child = link.Child;
        var run = new AgentRun(child.TaskId, agent.Id, agent.Name, original.Task.Trim(), AgentRunStatus.Queued,
            input.Model.Name, string.Empty, string.Empty, JsonSerializer.Serialize(definitions.Select(value => value.Key).ToArray()),
            "[]", child.CreatedAt, null, null, null, null, 0)
        { CanonicalTask = new(child.TaskId, child.ContextId, child.ExecutionId, child.PersistenceRevision, child.State, null) };
        var actualAgent = new AgentCanonicalOriginal(run) { OriginalProcessOwner = this }; original.ActualAgent = actualAgent;
        operation.OriginalCanonicalAgent = actualAgent;
        actualAgent.ActualServiceOperations.Add(operation);
        lock (_canonicalAdmissionGate)
        {
            if (_canonicalRuns.Count >= 128 || !_canonicalRuns.TryAdd(run.Id, actualAgent))
                throw new InvalidOperationException("The actual child Agent projection cannot replace another original.");
        }
        await operation.AwaitAsync(() => PersistAsync(run, token)).ConfigureAwait(false);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        actualAgent.Lifetime = lifetime; actualAgent.Active = 1;
        if (!_activeRuns.TryAdd(run.Id, actualAgent))
        { lifetime.Dispose(); throw new InvalidOperationException("The original child Agent is already active."); }
        try
        {
            try
            {
                run = run with { Status = AgentRunStatus.Running, StartedAt = DateTimeOffset.UtcNow, ProgressPercent = 10 };
                await operation.AwaitAsync(() => PersistAsync(run, token)).ConfigureAwait(false);
                actualAgent.Chat = operation.Invoke(() => chat.CreateOriginalDelegatedAgentInvocation(link, input, lifetime.Token));
                run = await operation.AwaitAsync(() => ConsumeCanonicalOriginalAsync(actualAgent, run, lifetime.Token)).ConfigureAwait(false);
            }
            catch (Exception actualFailure)
            { actualAgent.Retain(actualFailure); run = ProjectFailedOriginal(actualAgent, run, lifetime.IsCancellationRequested); }
            finally
            {
                try { await operation.AwaitAsync(() => JoinOriginalCancellationAndCloseAsync(actualAgent), owningCleanup: true).ConfigureAwait(false); }
                catch (Exception closeFailure) { actualAgent.Retain(closeFailure, actualAgent.OriginalCancellation); }
            }
            if (actualAgent.Causes.Count > 0) run = ProjectFailedOriginal(actualAgent, run, token.IsCancellationRequested);
            await operation.AwaitAsync(() => PersistAsync(run, CancellationToken.None), owningCleanup: true).ConfigureAwait(false);
            RecordCompletedOriginal(actualAgent, run);
            if (actualAgent.HasSuccessfulComplete && actualAgent.Chat is { } actualChat)
            {
                var actualAcknowledgment = tasks.AcknowledgeCompletedOriginalDelegatedChildAsync(link, actualChat, CancellationToken.None);
                lock (original.Gate) original.ParentCompletionAcknowledgments.Add(actualAcknowledgment);
                await operation.AwaitAsync(() => actualAcknowledgment).ConfigureAwait(false);
            }
            return run;
        }
        finally
        {
            _activeRuns.TryRemove(new KeyValuePair<Guid, AgentCanonicalOriginal>(run.Id, actualAgent));
            actualAgent.Active = 0;
        }
    }

    private async Task AcknowledgeCompletedOriginalDelegatedRetryAsync(AgentCanonicalOriginal actualAgent,
        AgentRuntimeOriginalCustody operation, CancellationToken token)
    {
        var original = _delegatedAgentRuns.Values.SingleOrDefault(value => ReferenceEquals(value.ActualAgent, actualAgent));
        if (original is null || !actualAgent.HasSuccessfulComplete) return;
        var link = original.Link ?? throw new InvalidOperationException("The actual saved child link is unavailable after continuation.");
        var actualChat = actualAgent.Chat ?? throw new InvalidOperationException("The actual saved child producer is unavailable after continuation.");
        var actualAcknowledgment = chat.OriginalDelegationTaskOwner.AcknowledgeCompletedOriginalDelegatedChildAsync(link, actualChat, token);
        lock (original.Gate) original.ParentCompletionAcknowledgments.Add(actualAcknowledgment);
        await operation.AwaitAsync(() => actualAcknowledgment).ConfigureAwait(false);
    }

    /// <summary>Explicitly reconciles only a retained genuinely completed child after parent-ACK loss. It never redispatches or recreates the child.</summary>
    public Task<TaskRunDelegatedChildResult> ReconcileCompletedDelegatedChildAsync(Guid parentTaskId,
        string requestKey, CancellationToken token) => RunOriginalOperation<TaskRunDelegatedChildResult>(async operation =>
    {
        if (!_delegatedAgentRuns.TryGetValue((parentTaskId, requestKey), out var original)
            || original.Link is not { } link || original.ActualAgent is not { HasSuccessfulComplete: true, Chat: { } actualChat })
            throw new InvalidOperationException("The actual completed child producer custody is unavailable; historical links cannot reconstruct it.");
        var actualAcknowledgment = chat.OriginalDelegationTaskOwner.AcknowledgeCompletedOriginalDelegatedChildAsync(link, actualChat, token);
        lock (original.Gate) original.ParentCompletionAcknowledgments.Add(actualAcknowledgment);
        var acknowledged = await operation.AwaitAsync(() => actualAcknowledgment).ConfigureAwait(false);
        return new(acknowledged.Parent, acknowledged.Child,
            acknowledged.Parent.Delegations.Single(value => value.Id == link.Original.Proposed.Id));
    });
}
