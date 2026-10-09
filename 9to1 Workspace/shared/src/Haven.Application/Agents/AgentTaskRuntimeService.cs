using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Executes saved Agents through Haven's existing Chat/tool loop. Agent configuration can
/// narrow discoverable capabilities, but never creates a second tool executor or approval path.
/// </summary>
public sealed partial class AgentTaskRuntimeService(
    ICatalogRepository catalog,
    IAgentRunRepository runs,
    IOllamaClient models,
    CapabilityRegistryService capabilityRegistry,
    ChatSessionService chat,
    IPermissionDecisionEngine permissionEngine,
    FloatingActivityStateStore? activityStore = null) : IRecordedAgentInvocationSource
{
    private readonly ConcurrentDictionary<Guid, AgentCanonicalOriginal> _activeRuns = new();
    private readonly ConcurrentDictionary<Guid, (AgentRun Expected, AgentActivityObservation Observation, AgentCanonicalOriginal Original)> _recordedObservations = new();

    private readonly ConcurrentDictionary<Guid, AgentCanonicalOriginal> _canonicalRuns = new();
    private readonly object _canonicalAdmissionGate = new();
    private readonly ConcurrentQueue<(Guid RunId, Exception Cause)> _observerFailures = new();
    public event Action<AgentRun>? RunChanged;
    internal IReadOnlyList<(Guid RunId, Exception Cause)> OriginalObservationFailures => _observerFailures.ToArray();

    /// <summary>Deny-only owning-source availability for UI. True never authorizes dispatch or policy.</summary>
    public bool HasOriginalUnstartedRetrySource(Guid runId)
    {
        if (!_canonicalRuns.TryGetValue(runId, out var owning) || Volatile.Read(ref owning.Active) != 0
            || owning.Chat is not { } chatOriginal || chatOriginal.AcknowledgedObservation is not { } snapshot
            || snapshot.State != TaskExecutionLifecycle.Suspended
            || chatOriginal.Original.AttemptAdmissionInvoked || chatOriginal.Original.OriginalProviderInvocationInvoked
            || !chatOriginal.Original.CanReturnPublishedPermissionRefusal(snapshot)) return false;
        var (ask, publication, owner) = chatOriginal.Original.RequireOriginalPublishedPermission();
        return owner.HasOriginalAllowedUnstartedResponse(ask, publication);
    }


    public Task<RecordedAgentInvocationEvidence?> GetRecordedInvocationEvidenceAsync(Guid runId, CancellationToken cancellationToken = default) =>
        RunOriginalOperation(operation => GetRecordedOriginalAsync(operation, runId, cancellationToken));

    private async Task<RecordedAgentInvocationEvidence?> GetRecordedOriginalAsync(AgentRuntimeOriginalCustody operation, Guid runId, CancellationToken cancellationToken)
    {
        if (!_recordedObservations.TryGetValue(runId, out var recorded)) return null;
        var current = await operation.AwaitAsync(() => runs.GetAsync(runId, cancellationToken)).ConfigureAwait(false);
        if (current is null || current != recorded.Expected || current.Status != AgentRunStatus.Completed
            || !recorded.Observation.ObservationComplete || !recorded.Original.HasSuccessfulComplete
            || current.CanonicalTask != recorded.Original.Chat?.BindingObservation())
            return null;
        return new(runId, current.AgentId, recorded.Observation);
    }


    public Task<AgentRun> RunAsync(
        Guid agentId, string task, CancellationToken cancellationToken,
        Guid? retryOfRunId = null, string? resourceReference = null) =>
        RunOriginalBusinessProcessOperation((operation, processToken) => RunOriginalAgentAsync(operation, agentId, task,
            processToken, retryOfRunId, resourceReference), cancellationToken);

    private async Task<AgentRun> RunOriginalAgentAsync(
        AgentRuntimeOriginalCustody operation, Guid agentId, string task,
        CancellationToken cancellationToken, Guid? retryOfRunId, string? resourceReference)
    {
        if (string.IsNullOrWhiteSpace(task))
            throw new ArgumentException("An Agent task is required.", nameof(task));

        var agent = (await operation.AwaitAsync(() => catalog.GetAgentsAsync(cancellationToken)).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id == agentId)
            ?? throw new InvalidOperationException("The selected Agent is disabled or no longer exists.");

        var installed = await operation.AwaitAsync(() => models.GetModelsAsync(cancellationToken)).ConfigureAwait(false);
        var model = ResolveModel(agent, installed)
            ?? throw new InvalidOperationException("No text-capable model is installed for this Agent.");

        var policy = AgentExecutionPolicy.Parse(agent.PermissionsJson);
        var discovered = await operation.AwaitAsync(() => capabilityRegistry.DiscoverAsync(OperatingSystem.IsAndroid() ? CapabilityPlatform.Android : CapabilityPlatform.Windows, cancellationToken))
            .ConfigureAwait(false);
        var allowedDefinitions = discovered
            .Where(item => item.IsAgentUsable)
            .Where(item => policy.CapabilityKeys.Contains(item.Key))
            .Where(IsGloballyAllowed)
            .ToArray();
        var activeCapabilities = allowedDefinitions.Select(ActiveCapability.FromDefinition).ToArray();

        var run = new AgentRun(
            Guid.NewGuid(),
            agent.Id,
            agent.Name,
            task.Trim(),
            AgentRunStatus.Queued,
            model.Name,
            string.Empty,
            string.Empty,
            JsonSerializer.Serialize(allowedDefinitions.Select(item => item.Key).ToArray()),
            "[]",
            DateTimeOffset.UtcNow,
            null,
            null,
            retryOfRunId,
            string.IsNullOrWhiteSpace(resourceReference) ? null : resourceReference.Trim(),
            0);

        var invocation = new AgentCanonicalOriginal(run) { OriginalProcessOwner = this };
        operation.OriginalCanonicalAgent = invocation;
        invocation.ActualServiceOperations.Add(operation);
        lock (_canonicalAdmissionGate)
        {
            if (_canonicalRuns.Count >= 128 || _observerFailures.Count >= 1024 || !_canonicalRuns.TryAdd(run.Id, invocation))
                throw new InvalidOperationException("Retained Agent original capacity is exhausted; unknown work cannot be discarded.");
        }
        await operation.AwaitAsync(() => PersistAsync(run, cancellationToken)).ConfigureAwait(false);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        invocation.Lifetime = linked;
        if (!_activeRuns.TryAdd(run.Id, invocation))
        { linked.Dispose(); throw new InvalidOperationException("Could not register the Agent run."); }
        invocation.Active = 1;
        try
        {
            run = run with { Status = AgentRunStatus.Running, StartedAt = DateTimeOffset.UtcNow, ProgressPercent = 10 };
            await operation.AwaitAsync(() => PersistAsync(run, cancellationToken)).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var conversation = new Conversation(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Chat,
                $"Agent · {agent.Name}", null, null, false, true, now, now);
            invocation.Chat = operation.Invoke(() => chat.CreateOriginalAgentInvocation(conversation, BuildExecutionTask(run), model,
                activeCapabilities, agent.Name, BuildExecutionInstructions(agent.Instructions, policy), linked.Token));
            run = await operation.AwaitAsync(() => ConsumeCanonicalOriginalAsync(invocation, run, linked.Token)).ConfigureAwait(false);
        }
        catch (Exception actualFailure)
        {
            invocation.Retain(actualFailure);
            run = ProjectFailedOriginal(invocation, run, linked.IsCancellationRequested);
        }
        finally
        {
            try { await operation.AwaitAsync(() => JoinOriginalCancellationAndCloseAsync(invocation), owningCleanup: true).ConfigureAwait(false); }
            catch (Exception cancellationCleanupFailure) { invocation.Retain(cancellationCleanupFailure, invocation.OriginalCancellation); }
        }
        if (invocation.Causes.Count > 0) run = ProjectFailedOriginal(invocation, run, cancellationToken.IsCancellationRequested);
        // The exact returned iterator and its Dispose have settled before terminal history.
        // Canonical completion itself is issued only by the retained Chat coordinator receipt.
        try
        {
            await operation.AwaitAsync(() => PersistAsync(run, CancellationToken.None), owningCleanup: true).ConfigureAwait(false);
            RecordCompletedOriginal(invocation, run);
            return run;
        }
        finally
        {
            _activeRuns.TryRemove(new KeyValuePair<Guid, AgentCanonicalOriginal>(run.Id, invocation));
            invocation.Active = 0;
        }

        bool IsGloballyAllowed(CapabilityDefinition definition)
        {
            var requiresPermission =
                definition.Availability == CapabilityAvailability.PermissionRequired ||
                definition.RiskClass >= CapabilityRiskClass.Consequential;
            var decision = operation.Invoke(() => permissionEngine.Evaluate(
                $"capability:{definition.Key}",
                definition.RiskClass,
                requiresPermission,
                $"Agent '{agent.Name}' requested {definition.Name}."));
            return decision.Kind != PermissionDecisionKind.Denied;
        }
    }

    /// <summary>Requests the actual owned cancellation; completion is observed by the original caller drain.</summary>
    public bool Cancel(Guid runId)
    {
        if (!_activeRuns.TryGetValue(runId, out var original)) return false;
        TaskCompletionSource start;
        lock (original.Gate)
        {
            if (original.CancellationSealed) return false;
            if (original.OriginalCancellation is not null) return true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original.OriginalCancellation = CancelPublishedOriginalAsync(original, start.Task);
        }
        start.TrySetResult();
        return true;
    }

    public Task<AgentRun> RetryAsync(Guid runId, CancellationToken cancellationToken) =>
        RunOriginalBusinessProcessOperation((operation, processToken) => RetryOriginalAgentAsync(operation, runId, processToken), cancellationToken);

    private async Task<AgentRun> RetryOriginalAgentAsync(
        AgentRuntimeOriginalCustody operation, Guid runId, CancellationToken cancellationToken)
    {
        if (!_canonicalRuns.TryGetValue(runId, out var original) || original.Chat is null)
            throw new InvalidOperationException("The SAME original Agent Task/Run custody is unavailable. Start explicit new work; historical recovery is not implemented.");
        operation.OriginalCanonicalAgent = original;
        lock (original.Gate) original.ActualServiceOperations.Add(operation);
        var expected = original.Expected;
        var previous = await operation.AwaitAsync(() => runs.GetAsync(runId, cancellationToken)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Agent run no longer exists.");
        DemandOriginalAgentRow(previous, expected);
        if (previous.CanonicalTask is null)
            throw new InvalidOperationException("The acknowledged original Agent Task/Run binding is unavailable.");
        if (Interlocked.CompareExchange(ref original.Active, 1, 0) != 0)
            throw new InvalidOperationException("The original Agent invocation is already active.");
        CancellationTokenSource? linked = null;
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (original.Gate)
            {
                original.Lifetime = linked;
                original.CancellationSealed = false;
                original.OriginalCancellation = null;
                original.LifetimeDisposed = false;
            }
            _activeRuns[runId] = original;
            Task<ChatOriginalAgentInvocation>? actualPreparation = null;
            var failures = new List<Exception>();
            AgentRun? acknowledged = null;
            try
            {
                var beforePreparation = await operation.AwaitAsync(() => runs.GetAsync(runId, linked.Token)).ConfigureAwait(false);
                DemandOriginalAgentRow(beforePreparation, expected);
                actualPreparation = chat.ContinueOriginalAgentInvocationAsync(original.Chat, linked.Token);
                original.ActualPreparations.Add(actualPreparation);
                var next = await operation.AwaitAsync(() => actualPreparation ?? throw new InvalidOperationException("No actual Agent preparation Task exists.")).ConfigureAwait(false);
                original.Chat = next; // Retain the genuine prepared successor even if a later row check refuses dispatch.
                var beforeDispatch = await operation.AwaitAsync(() => runs.GetAsync(runId, linked.Token)).ConfigureAwait(false);
                DemandOriginalAgentRow(beforeDispatch, expected);
                acknowledged = await operation.AwaitAsync(() => ConsumeCanonicalOriginalAsync(original, expected, linked.Token)).ConfigureAwait(false);
            }
            catch (Exception bodyFailure)
            {
                failures.AddRange(actualPreparation?.Exception is { } actualFaults ? actualFaults.InnerExceptions : new[] { bodyFailure });
            }
            finally
            {
                try { await operation.AwaitAsync(() => JoinOriginalCancellationAndCloseAsync(original), owningCleanup: true).ConfigureAwait(false); }
                catch (Exception closeFailure)
                { failures.AddRange(original.OriginalCancellation?.Exception is { } cancelFaults ? cancelFaults.InnerExceptions : new[] { closeFailure }); }
            }
            if (failures.Count > 0)
                throw new AggregateException("The SAME original Agent continuation or its cleanup was refused.", failures);
            var run = acknowledged ?? throw new InvalidOperationException("No actual Agent continuation result exists.");
            if (original.Causes.Count > 0) run = ProjectFailedOriginal(original, run, cancellationToken.IsCancellationRequested);
            await operation.AwaitAsync(() => PersistAsync(run, CancellationToken.None), owningCleanup: true).ConfigureAwait(false);
            RecordCompletedOriginal(original, run);
            await AcknowledgeCompletedOriginalDelegatedRetryAsync(original, operation, CancellationToken.None).ConfigureAwait(false);
            return run;
        }
        finally
        {
            _activeRuns.TryRemove(new KeyValuePair<Guid, AgentCanonicalOriginal>(runId, original));
            original.Active = 0; // Only after terminal history, observers and the genuine recorded receipt return.
        }
    }

    private static void DemandOriginalAgentRow(AgentRun? current, AgentRun expected)
    {
        if (current is null || current != expected)
            throw new InvalidOperationException("The Agent history no longer matches the SAME acknowledged original identity, input and binding.");
    }

    public Task<IReadOnlyList<AgentRun>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
        RunOriginalOperation(operation => GetRecentOriginalAsync(operation, limit, cancellationToken));

    private async Task<IReadOnlyList<AgentRun>> GetRecentOriginalAsync(AgentRuntimeOriginalCustody operation, int limit, CancellationToken cancellationToken)
    {
        var recent = await operation.AwaitAsync(() => runs.GetRecentAsync(Math.Clamp(limit, 1, 100), cancellationToken)).ConfigureAwait(false);
        var recovered = false;
        for (var index = 0; index < recent.Count; index++)
        {
            var run = recent[index];
            if (run.Status is not (AgentRunStatus.Queued or AgentRunStatus.Running) || _activeRuns.ContainsKey(run.Id))
                continue;

            run = run with
            {
                Status = AgentRunStatus.Failed,
                Error = run.CanonicalTask is null
                    ? "Interrupted because Haven stopped before this Agent run completed. Explicit new work is available; historical recovery is not implemented."
                    : "Interrupted while the SAME canonical task remains preserved. Original recovery custody is unavailable; automatic fresh-task retry is refused.",
                CompletedAt = DateTimeOffset.UtcNow
            };
            await operation.AwaitAsync(() => runs.UpsertAsync(run, cancellationToken)).ConfigureAwait(false);
            recovered = true;
        }

        return recovered
            ? await operation.AwaitAsync(() => runs.GetRecentAsync(Math.Clamp(limit, 1, 100), cancellationToken)).ConfigureAwait(false)
            : recent;
    }

    private Task PersistAsync(AgentRun run, CancellationToken cancellationToken)
    {
        if (!_canonicalRuns.TryGetValue(run.Id, out var owning))
            throw new InvalidOperationException("No actual original Agent owns this history publication.");
        AgentRuntimeOriginalCustody operation;
        lock (owning.Gate)
        {
            if (owning.ActualHistoryOperations.Count >= 256)
                throw new InvalidOperationException("Retained original Agent history capacity requires owning inspection.");
            operation = new() { OriginalProcessOwner = this, OriginalAcknowledgedHistoryStage = true };
            owning.ActualHistoryOperations.Add(operation);
        }
        return operation.Start(async actual =>
        {
            await PersistOriginalHistoryAsync(actual, owning, run, cancellationToken).ConfigureAwait(false);
            return true;
        });
    }

    private async Task PersistOriginalHistoryAsync(AgentRuntimeOriginalCustody operation,
        AgentCanonicalOriginal acknowledgedOriginal, AgentRun run, CancellationToken cancellationToken)
    {
        if (acknowledgedOriginal.ActualPersistence.Any(actual => actual.IsCompletedSuccessfully))
        {
            var expected = acknowledgedOriginal.Expected;
            var current = await operation.AwaitAsync(() => runs.GetAsync(run.Id, cancellationToken)).ConfigureAwait(false);
            DemandOriginalAgentRow(current, expected);
        }
        Task? actualWrite = null;
        try
        {
            actualWrite = operation.Invoke(() => runs.UpsertAsync(run, cancellationToken));
            if (_canonicalRuns.TryGetValue(run.Id, out var owning)) owning.ActualPersistence.Add(actualWrite);
            await operation.AwaitAsync(() => actualWrite ?? throw new InvalidOperationException("No actual original Agent history write Task exists.")).ConfigureAwait(false);
        }
        catch (Exception originalWriteFailure)
        {
            if (_canonicalRuns.TryGetValue(run.Id, out var owning)) owning.Retain(originalWriteFailure, actualWrite);
            if (actualWrite?.IsFaulted == true && actualWrite.Exception is { } faults) throw faults;
            if (actualWrite is null && originalWriteFailure is OperationCanceledException)
                throw new AggregateException("The original synchronous Agent history acquisition failed.", originalWriteFailure);
            throw;
        }
        try
        {
            if (activityStore is not null)
            {
                var activityState = run.Status switch
                {
                    AgentRunStatus.Queued => FloatingActivityState.Created,
                    AgentRunStatus.Running or AgentRunStatus.Suspended => FloatingActivityState.Presented,
                    AgentRunStatus.Failed => FloatingActivityState.Failed,
                    _ => FloatingActivityState.Dismissed
                };
                TaskRunProcessProducerContext.Invoke(operation.OriginalProcessOwner, () =>
                {
                    activityStore.Set(new FloatingActivitySnapshot(
                        run.Id, activityState, 0, 0, 0, 0,
                        run.Status == AgentRunStatus.Failed ? run.Error : null));
                    return true;
                });
            }
        }
        catch (Exception actualActivityObserverFailure) { _observerFailures.Enqueue((run.Id, actualActivityObserverFailure)); }
        if (_canonicalRuns.TryGetValue(run.Id, out var original)) original.Expected = run;
        if (RunChanged is { } observers)
            foreach (Action<AgentRun> observer in observers.GetInvocationList())
                try { TaskRunProcessProducerContext.Invoke(operation.OriginalProcessOwner, () => { observer(run); return true; }); }
                catch (Exception actualObserverFailure) { _observerFailures.Enqueue((run.Id, actualObserverFailure)); }
    }

    private static ModelDescriptor? ResolveModel(
        AgentDefinition agent,
        IReadOnlyList<ModelDescriptor> installed)
    {
        var textModels = installed.Where(item => item.Supports(ToolCapability.Text)).ToArray();
        if (textModels.Length == 0) return null;

        if (!string.IsNullOrWhiteSpace(agent.PreferredModel) &&
            !agent.PreferredModel.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            var preferred = textModels.FirstOrDefault(item =>
                item.Name.Equals(agent.PreferredModel, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null) return preferred;
        }

        if (!string.IsNullOrWhiteSpace(agent.FallbackModel))
        {
            var fallback = textModels.FirstOrDefault(item =>
                item.Name.Equals(agent.FallbackModel, StringComparison.OrdinalIgnoreCase));
            if (fallback is not null) return fallback;
        }

        return textModels.OrderByDescending(item => item.ModifiedAt).First();
    }

    private static string BuildExecutionTask(AgentRun run)
    {
        if (string.IsNullOrWhiteSpace(run.ResourceReference)) return run.Task;
        return run.Task + Environment.NewLine + Environment.NewLine + "Resource reference: " + run.ResourceReference + Environment.NewLine + "Use semantic Haven operations when a compatible capability is available; do not simulate mouse/keyboard interaction for Haven-owned app data.";
    }

    private static string BuildExecutionInstructions(string instructions, AgentExecutionPolicy policy)
    {
        if (policy.KnowledgeResources.Count == 0) return instructions;
        return instructions + Environment.NewLine + Environment.NewLine + "Configured knowledge/resource references:" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", policy.KnowledgeResources);
    }

    internal sealed record AgentExecutionPolicy(IReadOnlySet<string> CapabilityKeys, IReadOnlyList<string> KnowledgeResources)
    {
        public static AgentExecutionPolicy Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);

                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (document.RootElement.TryGetProperty("capabilities", out var capabilities) &&
                    capabilities.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in capabilities.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                            keys.Add(item.GetString()!);
                    }
                }

                AddLegacyFlag(document.RootElement, "webSearch", "web-search", keys);
                AddLegacyFlag(document.RootElement, "browserUse", "browser-use", keys);
                AddLegacyFlag(document.RootElement, "computerUse", "computer-device-use", keys);
                var resources = document.RootElement.TryGetProperty("knowledgeResources", out var configuredResources) && configuredResources.ValueKind == JsonValueKind.Array
                    ? configuredResources.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray()
                    : [];
                return new(keys, resources);
            }
            catch (JsonException)
            {
                return new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);
            }
        }

        private static void AddLegacyFlag(JsonElement root, string propertyName, string capabilityKey, ISet<string> keys)
        {
            if (root.TryGetProperty(propertyName, out var flag) &&
                flag.ValueKind == JsonValueKind.True)
            {
                keys.Add(capabilityKey);
            }
        }
    }
}
