using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Saved_agent_source_issued_permission_pause_is_suspended_not_completed_or_observation_complete()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var paused = await service.RunAsync(definition.Id, "Original Agent task", TestContext.Current.CancellationToken);
        var binding = Assert.IsType<AgentRunCanonicalBinding>(paused.CanonicalTask);
        var actual = (await rig.Tasks.GetAsync(binding.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(AgentRunStatus.Suspended, paused.Status);
        Assert.Null(paused.CompletedAt);
        Assert.InRange(paused.ProgressPercent, 0, 90);
        Assert.Equal(TaskExecutionLifecycle.Suspended, actual.State);
        Assert.Equal(actual.ContextId, binding.ContextId);
        Assert.Equal(actual.ExecutionId, binding.ExecutionId);
        Assert.False(JsonSerializer.Deserialize<AgentActivityObservation>(paused.ActivityJson)!.ObservationComplete);
        Assert.Null(await service.GetRecordedInvocationEvidenceAsync(paused.Id, TestContext.Current.CancellationToken));
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.False(service.HasOriginalUnstartedRetrySource(paused.Id));
        Assert.Equal(1, JsonSerializer.Deserialize<AgentActivityObservation>(paused.ActivityJson)!.CanonicalBindingVersion);
        Assert.Single(rows.Values);
    }

    [Fact]
    public async Task Saved_agent_preapproval_retry_refuses_then_real_original_approval_resumes_same_ids_without_new_work()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var paused = await service.RunAsync(definition.Id, "Same Agent task", TestContext.Current.CancellationToken);
        var binding = paused.CanonicalTask!;
        var failure = await Record.ExceptionAsync(() => service.RetryAsync(paused.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(failure);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Single(rows.Values);
        var waiting = Assert.Single(rig.RemediationRows.Rows.Values);
        await rig.Owner.ApproveOriginalAsync(waiting.Id, TestContext.Current.CancellationToken);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.True(service.HasOriginalUnstartedRetrySource(paused.Id));
        rig.Client.RunApprovedOwnedFrame = true;
        var completed = await service.RetryAsync(paused.Id, TestContext.Current.CancellationToken);
        Assert.Equal(paused.Id, completed.Id);
        Assert.Equal(binding.TaskId, completed.CanonicalTask!.TaskId);
        Assert.Equal(binding.ContextId, completed.CanonicalTask.ContextId);
        Assert.Equal(binding.ExecutionId, completed.CanonicalTask.ExecutionId);
        Assert.Equal(AgentRunStatus.Completed, completed.Status);
        Assert.Equal(TaskExecutionLifecycle.Completed, completed.CanonicalTask.State);
        Assert.Equal(100, completed.ProgressPercent);
        Assert.True(JsonSerializer.Deserialize<AgentActivityObservation>(completed.ActivityJson)!.ObservationComplete);
        Assert.NotNull(await service.GetRecordedInvocationEvidenceAsync(completed.Id, TestContext.Current.CancellationToken));
        Assert.Single(rows.Values);
        Assert.Single(await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken));
        Assert.Single((await rig.Tasks.GetAsync(binding.TaskId, TestContext.Current.CancellationToken))!.RecoveryHistory);
        Assert.Equal(1, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Saved_agent_retry_never_uses_singleton_current_task_from_an_unrelated_chat_invocation()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var first = await service.RunAsync(definition.Id, "Retained Agent input", TestContext.Current.CancellationToken);
        var firstBinding = first.CanonicalTask!;
        var firstPermission = Assert.Single(rig.RemediationRows.Rows.Values);
        await rig.RunAsync(false); // Changes the singleton display to a distinct real canonical task.
        var unrelated = rig.Service.CurrentCanonicalTask!;
        Assert.NotEqual(firstBinding.TaskId, unrelated.TaskId);
        await rig.Owner.ApproveOriginalAsync(firstPermission.Id, TestContext.Current.CancellationToken);
        rig.Client.RunApprovedOwnedFrame = true;
        var completed = await service.RetryAsync(first.Id, TestContext.Current.CancellationToken);
        Assert.Equal(firstBinding.TaskId, completed.CanonicalTask!.TaskId);
        Assert.Equal(firstBinding.ExecutionId, completed.CanonicalTask.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Suspended, (await rig.Tasks.GetAsync(unrelated.TaskId, TestContext.Current.CancellationToken))!.State);
        Assert.Equal(2, (await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Saved_agent_invoked_attempt_refuses_retry_instead_of_creating_a_replacement_task_or_chat()
    {
        await using var rig = new Rig();
        rig.Capture.OpenActualAttemptBeforeAsk = true;
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var paused = await service.RunAsync(definition.Id, "Actual admitted attempt", TestContext.Current.CancellationToken);
        Assert.NotNull(paused.CanonicalTask!.AttemptId);
        await rig.Owner.ApproveOriginalAsync(Assert.Single(rig.RemediationRows.Rows.Values).Id, TestContext.Current.CancellationToken);
        var failure = await Record.ExceptionAsync(() => service.RetryAsync(paused.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(failure);
        Assert.Single(rows.Values);
        Assert.Single(await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Null(await service.GetRecordedInvocationEvidenceAsync(paused.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Saved_agent_historical_display_binding_cannot_reconstruct_original_input_or_retry_authority()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (original, definition) = CreateAgentCaller(rig, rows);
        var paused = await original.RunAsync(definition.Id, "Historical binding only", TestContext.Current.CancellationToken);
        var (restarted, _) = CreateAgentCaller(rig, rows);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.RetryAsync(paused.Id, TestContext.Current.CancellationToken));
        Assert.Null(await restarted.GetRecordedInvocationEvidenceAsync(paused.Id, TestContext.Current.CancellationToken));
        Assert.Single(rows.Values);
        Assert.Single(await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Saved_agent_failed_actual_provider_dispose_preserves_same_task_and_refuses_completed_receipt()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var paused = await service.RunAsync(definition.Id, "Actual failed cleanup", TestContext.Current.CancellationToken);
        await rig.Owner.ApproveOriginalAsync(Assert.Single(rig.RemediationRows.Rows.Values).Id, TestContext.Current.CancellationToken);
        rig.Client.RunApprovedOwnedFrame = true;
        var cause = new IOException("Exact original provider close failure");
        rig.Client.OriginalDispose = Task.FromException(cause);
        var failed = await service.RetryAsync(paused.Id, TestContext.Current.CancellationToken);
        Assert.Equal(paused.Id, failed.Id);
        Assert.Equal(paused.CanonicalTask!.TaskId, failed.CanonicalTask!.TaskId);
        Assert.Equal(AgentRunStatus.Suspended, failed.Status);
        Assert.Contains(cause.Message, failed.Error, StringComparison.Ordinal);
        Assert.False(JsonSerializer.Deserialize<AgentActivityObservation>(failed.ActivityJson)!.ObservationComplete);
        Assert.Null(await service.GetRecordedInvocationEvidenceAsync(failed.Id, TestContext.Current.CancellationToken));
        Assert.Single(rows.Values);
        Assert.Single(await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Explicit_new_agent_work_is_separate_while_retry_never_creates_a_fresh_task()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var first = await service.RunAsync(definition.Id, "Explicit first work", TestContext.Current.CancellationToken);
        var second = await service.RunAsync(definition.Id, "Explicit second work", TestContext.Current.CancellationToken);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.CanonicalTask!.TaskId, second.CanonicalTask!.TaskId);
        Assert.NotEqual(first.CanonicalTask.ContextId, second.CanonicalTask.ContextId);
        Assert.NotEqual(first.CanonicalTask.ExecutionId, second.CanonicalTask.ExecutionId);
        Assert.Equal(AgentRunStatus.Suspended, first.Status);
        Assert.Equal(AgentRunStatus.Suspended, second.Status);
        Assert.Equal(2, rows.Values.Count);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_agent_retry_refuses_changed_original_identity_or_input_with_same_canonical_binding(bool changeIdentity)
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var paused = await service.RunAsync(definition.Id, "Actual immutable original Agent input", TestContext.Current.CancellationToken);
        await rig.Owner.ApproveOriginalAsync(Assert.Single(rig.RemediationRows.Rows.Values).Id, TestContext.Current.CancellationToken);
        var altered = changeIdentity ? paused with { AgentId = Guid.NewGuid() }
            : paused with { Task = "Foreign task claiming the same canonical binding" };
        rows.Values[paused.Id] = JsonSerializer.Serialize(altered);
        var failure = await Record.ExceptionAsync(() => service.RetryAsync(paused.Id, TestContext.Current.CancellationToken));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Single(rows.Values);
        Assert.Equal(altered, await rows.GetAsync(paused.Id, TestContext.Current.CancellationToken));
        Assert.Equal(TaskExecutionLifecycle.Suspended, (await rig.Tasks.GetAsync(paused.CanonicalTask!.TaskId, TestContext.Current.CancellationToken))!.State);
        Assert.Null(await service.GetRecordedInvocationEvidenceAsync(paused.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Saved_agent_model_discovery_preserves_all_direct_fault_siblings_and_same_raw_Task()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var first = new IOException("Actual discovery first direct failure");
        var second = new InvalidOperationException("Actual discovery second direct failure");
        var source = new TaskCompletionSource<IReadOnlyList<ModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetException([first, second]);
        var (service, definition) = CreateAgentCaller(rig, rows, model => new ControlledAgentDiscovery(() => source.Task));
        var actual = service.RunAsync(definition.Id, "Discovery has not admitted a canonical Task", TestContext.Current.CancellationToken);
        var failure = await Record.ExceptionAsync(() => actual);
        Assert.NotNull(failure);
        Assert.Contains(Leaves(failure), cause => ReferenceEquals(cause, first));
        Assert.Contains(Leaves(failure), cause => ReferenceEquals(cause, second));
        Assert.True(actual.IsFaulted);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var originalOperations = typeof(AgentTaskRuntimeService).GetProperty("OriginalRuntimeOperations", flags)!
            .GetValue(service) as System.Collections.IEnumerable ?? throw new InvalidOperationException("Actual private Agent custody is absent.");
        var custody = Assert.Single(originalOperations.Cast<object>());
        Assert.Same(actual, custody.GetType().GetField("OriginalDriver", flags)!.GetValue(custody));
        var retainedSources = (IReadOnlyList<Task>)custody.GetType().GetProperty("Sources", flags)!.GetValue(custody)!;
        Assert.Contains(retainedSources, original => ReferenceEquals(original, source.Task));
        Assert.Empty(rows.Values);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_agent_sync_or_faulted_model_discovery_OCE_preserves_fault_and_exact_cause(bool synchronous)
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var cause = new OperationCanceledException("Actual failed discovery source, caller token remains live");
        var raw = Task.FromException<IReadOnlyList<ModelDescriptor>>(cause);
        var (service, definition) = CreateAgentCaller(rig, rows,
            model => new ControlledAgentDiscovery(() => synchronous ? throw cause : raw));
        var actual = service.RunAsync(definition.Id, "No canonical effect", TestContext.Current.CancellationToken);
        var failure = await Record.ExceptionAsync(() => actual);
        Assert.NotNull(failure);
        Assert.Contains(Leaves(failure), retained => ReferenceEquals(retained, cause));
        Assert.True(actual.IsFaulted);
        Assert.False(actual.IsCanceled);
        Assert.Empty(rows.Values);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Saved_agent_actual_canceled_discovery_keeps_genuine_canceled_status_without_canonical_effect()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        using var canceledSource = new CancellationTokenSource();
        canceledSource.Cancel();
        var raw = Task.FromCanceled<IReadOnlyList<ModelDescriptor>>(canceledSource.Token);
        var (service, definition) = CreateAgentCaller(rig, rows, model => new ControlledAgentDiscovery(() => raw));
        var actual = service.RunAsync(definition.Id, "No canonical task was admitted", TestContext.Current.CancellationToken);
        var failure = await Record.ExceptionAsync(() => actual);
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.True(raw.IsCanceled);
        Assert.True(actual.IsCanceled);
        Assert.False(actual.IsFaulted);
        Assert.Empty(rows.Values);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Saved_agent_held_terminal_history_keeps_same_original_active_until_acknowledged()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rows.BeforeSave = value =>
        {
            if (value.Status != AgentRunStatus.Suspended) return Task.CompletedTask;
            entered.TrySetResult(); return release.Task;
        };
        var (service, definition) = CreateAgentCaller(rig, rows);
        var actual = service.RunAsync(definition.Id, "Held actual terminal history", TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var previous = Assert.Single(rows.Values);
            var id = previous.Key;
            await rig.Owner.ApproveOriginalAsync(Assert.Single(rig.RemediationRows.Rows.Values).Id, TestContext.Current.CancellationToken);
            var refusal = await Record.ExceptionAsync(() => service.RetryAsync(id, TestContext.Current.CancellationToken));
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.False(actual.IsCompleted);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally { release.TrySetResult(); await JoinIndependentFixtureCleanupAsync(actual, TimeSpan.FromSeconds(5)); }
        var completedOriginal = await actual;
        Assert.Equal(AgentRunStatus.Suspended, completedOriginal.Status);
        Assert.True(service.HasOriginalUnstartedRetrySource(completedOriginal.Id)); // Real approval is now available; it never dispatched.
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Saved_agent_suspended_terminal_observer_cannot_reenter_same_original_retry()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        Exception? actualRefusal = null;
        var callbacks = 0;
        service.RunChanged += value =>
        {
            if (value.Status != AgentRunStatus.Suspended) return;
            callbacks++;
            rig.Owner.ApproveOriginalAsync(Assert.Single(rig.RemediationRows.Rows.Values).Id, default).GetAwaiter().GetResult();
            try { service.RetryAsync(value.Id, default).GetAwaiter().GetResult(); }
            catch (Exception cause) { actualRefusal = cause; }
        };
        var paused = await service.RunAsync(definition.Id, "Actual synchronous observer original", TestContext.Current.CancellationToken);
        Assert.Equal(1, callbacks);
        Assert.IsType<InvalidOperationException>(actualRefusal);
        Assert.Equal(AgentRunStatus.Suspended, paused.Status);
        Assert.Equal(paused, await rows.GetAsync(paused.Id, TestContext.Current.CancellationToken));
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.True(service.HasOriginalUnstartedRetrySource(paused.Id));
    }

    [Fact]
    public async Task Saved_agent_changed_row_during_actual_provider_cleanup_refuses_terminal_history_relabeling()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var paused = await service.RunAsync(definition.Id, "Genuine original input remains private", TestContext.Current.CancellationToken);
        await rig.Owner.ApproveOriginalAsync(Assert.Single(rig.RemediationRows.Rows.Values).Id, TestContext.Current.CancellationToken);
        rig.Client.RunApprovedOwnedFrame = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.NextDisposeEntered = entered;
        rig.Client.OriginalDispose = release.Task;
        var actual = service.RetryAsync(paused.Id, TestContext.Current.CancellationToken);
        AgentRun? foreign = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(actual.IsCompleted);
            Assert.Equal(1, rig.Client.Dispatches);
            var prior = Assert.IsType<AgentRun>(await rows.GetAsync(paused.Id, TestContext.Current.CancellationToken));
            foreign = prior with { AgentId = Guid.NewGuid(), Task = "Foreign input reusing canonical IDs" };
            rows.Values[paused.Id] = JsonSerializer.Serialize(foreign);
        }
        finally { release.TrySetResult(); }
        var failure = await Record.ExceptionAsync(() => actual.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(foreign, await rows.GetAsync(paused.Id, TestContext.Current.CancellationToken));
        Assert.Single(rows.Values);
        Assert.Equal(1, rig.Client.Dispatches);
        Assert.Equal(TaskExecutionLifecycle.Completed, (await rig.Tasks.GetAsync(paused.CanonicalTask!.TaskId, TestContext.Current.CancellationToken))!.State);
        Assert.Null(await service.GetRecordedInvocationEvidenceAsync(paused.Id, TestContext.Current.CancellationToken));
        Assert.False(service.HasOriginalUnstartedRetrySource(paused.Id));
    }

    // Owning teardown joins stay independent of runner withdrawal; the actual cleanup Task is never cancelled here.
    private static Task JoinIndependentFixtureCleanupAsync(Task actual, TimeSpan timeout) =>
        actual.WaitAsync(timeout, CancellationToken.None);

    private static (AgentTaskRuntimeService Runtime, AgentDefinition Definition) CreateAgentCaller(Rig rig, AgentRows rows, Func<ModelDescriptor, IOllamaClient>? discovery = null)
    {
        var model = rig.Provider.Model.Model with
        { Name = rig.Provider.Model.Key, Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        var definition = new AgentDefinition(Guid.NewGuid(), "Synthetic owning Agent", "No real account/provider", "Observe actual task state",
            "agent", model.Name, null, "[]", "{}", false, true, DateTimeOffset.UnixEpoch);
        return (new(new AgentCatalog(definition), rows, discovery?.Invoke(model) ?? new AgentModelDiscovery(model),
            new CapabilityRegistryService(new EmptyAgentCapabilities()), rig.Service, rig.Policy), definition);
    }

    private sealed class AgentRows : IAgentRunRepository
    {
        internal readonly Dictionary<Guid, string> Values = [];
        internal Func<AgentRun, Task>? BeforeSave = null;
        public async Task UpsertAsync(AgentRun value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (BeforeSave is { } source) await source(value);
            Values[value.Id] = JsonSerializer.Serialize(value);
        }
        public Task<AgentRun?> GetAsync(Guid id, CancellationToken token) =>
            Task.FromResult(Values.TryGetValue(id, out var json) ? JsonSerializer.Deserialize<AgentRun>(json) : null);
        public Task<IReadOnlyList<AgentRun>> GetRecentAsync(int count, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<AgentRun>>(Values.Values.Select(json => JsonSerializer.Deserialize<AgentRun>(json)!).Take(count).ToArray());
        public async Task<IReadOnlyList<AgentRun>> GetByAgentAsync(Guid id, int count, CancellationToken token) =>
            (await GetRecentAsync(count, token)).Where(value => value.AgentId == id).ToArray();
    }

    private sealed class AgentCatalog(AgentDefinition original) : ICatalogRepository
    {
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AgentDefinition>>([original]);
        public Task<IReadOnlyList<PromptDefinition>> GetPromptsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<PromptDefinition>>([]);
        public Task UpsertAgentAsync(AgentDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertPromptAsync(PromptDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task SetAgentEnabledAsync(Guid id, bool enabled, CancellationToken token) => throw new NotSupportedException();
        public Task SetPromptEnabledAsync(Guid id, bool enabled, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteCustomAgentAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteCustomPromptAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class EmptyAgentCapabilities : ICapabilityRepository
    {
        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CapabilityDefinition>>([]);
        public Task UpsertCapabilityAsync(CapabilityDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task SetCapabilityEnabledAsync(Guid id, bool enabled, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteCustomCapabilityAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class ControlledAgentDiscovery(Func<Task<IReadOnlyList<ModelDescriptor>>> actualSource) : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => actualSource();
        public Task<string> CompleteAsync(OllamaChatRequest value, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest value, CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest value, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class AgentModelDiscovery(ModelDescriptor original) : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([original]);
        public Task<string> CompleteAsync(OllamaChatRequest value, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest value, CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest value, CancellationToken token) => throw new NotSupportedException();
    }
}
