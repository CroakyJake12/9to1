using System.Collections;
using System.Reflection;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual owning Chat/Agent process Tasks. Actor/repositories/provider selections are the
/// explicitly synthetic existing Rig; no account, remote effect or host final-clean acceptance.</summary>
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Canonical_outer_dispose_is_the_same_actual_task_and_keeps_current_after_real_suspension_ack()
    {
        await using var rig = new Rig();
        var actual = StartProcessChat(rig).GetAsyncEnumerator();
        Assert.True(await actual.MoveNextAsync());
        var published = actual.Current;
        Assert.Equal(ChatStreamEventKind.UserMessage, published.Kind);
        var binding = rig.Service.CurrentCanonicalTask!;
        var dispose = actual.DisposeAsync().AsTask();
        Assert.Same(dispose, actual.DisposeAsync().AsTask());
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(published, actual.Current);
        var acknowledged = (await rig.Tasks.GetAsync(binding.TaskId, default))!;
        Assert.Equal(binding.TaskId, acknowledged.TaskId);
        Assert.Equal(binding.ContextId, acknowledged.ContextId);
        Assert.Equal(binding.ExecutionId, acknowledged.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Suspended, acknowledged.State);
        Assert.NotNull(acknowledged.RecoveryObservation);
        Assert.Empty(acknowledged.Attempts);
        Assert.Equal(0, rig.Client.Dispatches);
        var close = rig.Tasks.CloseAndSuspendOriginalProducersAsync();
        Assert.Same(close, rig.Tasks.CloseAndSuspendOriginalProducersAsync());
        await close.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Whole_process_request_refuses_existing_moves_enumeration_and_new_chat_before_any_body()
    {
        await using var rig = new Rig();
        var claimed = StartProcessChat(rig).GetAsyncEnumerator();
        var unclaimed = StartProcessChat(rig);
        rig.Tasks.RequestOriginalProcessRetirement();
        Assert.Throws<InvalidOperationException>(() => { claimed.MoveNextAsync(); });
        Assert.Throws<InvalidOperationException>(() => { unclaimed.GetAsyncEnumerator(); });
        Assert.Throws<InvalidOperationException>(() => { StartProcessChat(rig); });
        var close = rig.Tasks.CloseAndSuspendOriginalProducersAsync();
        Assert.Same(close, rig.Tasks.CloseAndSuspendOriginalProducersAsync());
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(rig.Service.CurrentCanonicalTask);
        Assert.Empty(rig.Conversations.Messages);
        Assert.Equal(0, rig.Capture.ChatCaptures);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(0, rig.Provider.Starts);
    }

    [Fact]
    public async Task Request_all_reaches_both_actual_cancellation_callbacks_before_either_whole_close_is_joined()
    {
        await using var rig = new Rig();
        var first = StartProcessChat(rig).GetAsyncEnumerator();
        var second = StartProcessChat(rig).GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstRelease = new ManualResetEventSlim();
        using var secondRelease = new ManualResetEventSlim();
        var firstLifetime = ReadProcessMember<CancellationTokenSource>(first, "_lifetime");
        var secondLifetime = ReadProcessMember<CancellationTokenSource>(second, "_lifetime");
        using var firstCallback = firstLifetime.Token.Register(() => { firstEntered.TrySetResult(); firstRelease.Wait(); });
        using var secondCallback = secondLifetime.Token.Register(() => { secondEntered.TrySetResult(); secondRelease.Wait(); });
        rig.Tasks.RequestOriginalProcessRetirement();
        var close = rig.Tasks.CloseAndSuspendOriginalProducersAsync();
        try
        {
            await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(close.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => { first.MoveNextAsync(); });
            Assert.Throws<InvalidOperationException>(() => { second.MoveNextAsync(); });
            Assert.Same(ReadProcessMember<Task>(first, "_dispose"), first.DisposeAsync().AsTask());
            Assert.Same(ReadProcessMember<Task>(second, "_dispose"), second.DisposeAsync().AsTask());
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally { firstRelease.Set(); secondRelease.Set(); await close.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(TaskExecutionLifecycle.Suspended, rig.Service.CurrentCanonicalTask!.State);
    }

    [Fact]
    public async Task Actual_snapshot_callback_restoring_foreign_execution_context_cannot_join_its_own_process_original()
    {
        await using var rig = new Rig();
        var restored = ExecutionContext.Capture()!;
        Exception? refusal = null;
        var calls = 0;
        rig.Tasks.SnapshotChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref calls) != 1) return;
            ExecutionContext.Run(restored, _ =>
            {
                refusal = Record.Exception(() => { rig.Tasks.CloseAndSuspendOriginalProducersAsync(); });
            }, null);
        };
        var actual = StartProcessChat(rig).GetAsyncEnumerator();
        Assert.True(await actual.MoveNextAsync());
        Assert.IsType<InvalidOperationException>(refusal);
        Assert.Equal(ChatStreamEventKind.UserMessage, actual.Current.Kind);
        await actual.DisposeAsync();
        await rig.Tasks.CloseAndSuspendOriginalProducersAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.NotNull(rig.Service.CurrentCanonicalTask!.RecoveryObservation);
    }

    [Fact]
    public async Task Detached_presentation_keeps_same_discovery_driver_in_process_custody_until_late_full_faults_settle()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var raw = new TaskCompletionSource<IReadOnlyList<ModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, definition) = CreateAgentCaller(rig, rows, _ => new ControlledAgentDiscovery(() =>
        { entered.TrySetResult(); return raw.Task; }));
        var lease = service.StartObservedOriginalRun(definition.Id, "Actual process discovery", CancellationToken.None);
        var presentation = Assert.Single(ReadProcessMember<IEnumerable>(service, "ActualOriginalObservations").Cast<object>());
        var actualProducer = ReadProcessMember<Task<AgentRun>>(presentation, "Producer");
        var first = new OperationCanceledException("Actual faulted discovery OCE, independent from process stop");
        var second = new IOException("Actual late discovery sibling");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lease.RequestOriginalObservationRetirement();
        await lease.DetachAndDrainOriginalObservationAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AgentRunObservationDisposition.ObservationDetached, (await lease.WaitOriginalObservationAsync()).Disposition);
        Assert.False(actualProducer.IsCompleted);
        service.RequestOriginalProcessRetirement();
        var close = service.CloseAndSuspendOriginalProducersAsync();
        Assert.Same(close, service.CloseAndSuspendOriginalProducersAsync());
        try
        {
            Assert.False(close.IsCompleted);
            Assert.False(raw.Task.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => { service.StartObservedOriginalRun(definition.Id, "Denied after seal", default); });
        }
        finally { raw.TrySetException([first, second]); }
        var failed = await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(failed);
        Assert.True(close.IsFaulted);
        Assert.True(actualProducer.IsFaulted);
        Assert.Contains(Leaves(failed!), cause => ReferenceEquals(cause, first));
        Assert.Contains(Leaves(failed!), cause => ReferenceEquals(cause, second));
        var processes = ReadProcessMember<IEnumerable>(service, "_agentProcessOperations").Cast<object>().ToArray();
        var process = Assert.Single(processes);
        var custody = ReadProcessMember<object>(process, "Original");
        Assert.Same(ReadProcessMember<Task<AgentRun>>(presentation, "ActualProducer"), ReadProcessMember<Task>(custody, "OriginalDriver"));
        Assert.Contains(ReadProcessMember<IReadOnlyList<Task>>(custody, "Sources"), task => ReferenceEquals(task, raw.Task));
        Assert.Empty(rows.Values);
        Assert.Null(rig.Service.CurrentCanonicalTask);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Actual_agent_terminal_callback_cannot_join_service_process_driver_under_restored_context()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var restored = ExecutionContext.Capture()!;
        Exception? refusal = null;
        service.RunChanged += _ =>
        {
            if (refusal is not null) return;
            ExecutionContext.Run(restored, _ =>
            {
                refusal = Record.Exception(() => { service.CloseAndSuspendOriginalProducersAsync(); });
            }, null);
        };
        var returned = await service.RunAsync(definition.Id, "Real canonical terminal source", CancellationToken.None);
        Assert.IsType<InvalidOperationException>(refusal);
        Assert.Equal(AgentRunStatus.Suspended, returned.Status);
        Assert.Equal(TaskExecutionLifecycle.Suspended, returned.CanonicalTask!.State);
        Assert.Null(await service.GetRecordedInvocationEvidenceAsync(returned.Id));
        var close = service.CloseAndSuspendOriginalProducersAsync();
        Assert.Same(close, service.CloseAndSuspendOriginalProducersAsync());
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        await rig.Tasks.CloseAndSuspendOriginalProducersAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Finite_actual_outer_move_custody_refuses_next_callback_with_same_sticky_cause_and_retains_partial_output()
    {
        await using var rig = new Rig();
        rig.Capture.AskDuringCapture = false;
        // Deliberately unissued raw provider fixture: this is a no-effect custody negative,
        // never a positive canonical/provider authorization or completion witness.
        var raw = new ProcessBurstClient();
        var service = new ChatSessionService(rig.Conversations, raw, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()),
            taskCoordinator: rig.Tasks, taskToolOwner: rig.ToolOwner, taskProviderContextCapture: rig.Capture,
            taskCloudPermissionRemediation: rig.Owner);
        var stream = service.SendAsync(rig.Conversation, "Bounded controlled raw output", rig.Provider.Model.Model with
            { Name = rig.Provider.Model.Key, Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } },
            EffortLevel.Medium, [], "controlled", "", DuoMode.Solo, null, null, null, null, default,
            taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask).GetAsyncEnumerator();
        for (var index = 0; index < 4096; index++) Assert.True(await stream.MoveNextAsync());
        var retainedCurrent = stream.Current;
        var priorRawMoves = raw.Moves;
        var refused = Assert.Throws<InvalidOperationException>(() => { stream.MoveNextAsync(); });
        Assert.Same(refused, Assert.Throws<InvalidOperationException>(() => { stream.MoveNextAsync(); }));
        Assert.Equal(priorRawMoves, raw.Moves);
        var actualMoves = ReadProcessMember<IReadOnlyList<Task>>(stream, "ActualMoves");
        Assert.Equal(4096, actualMoves.Count);
        Assert.All(actualMoves, actual => Assert.True(actual.IsCompletedSuccessfully));
        var dispose = stream.DisposeAsync().AsTask();
        Assert.Same(dispose, stream.DisposeAsync().AsTask());
        var failedClose = await Record.ExceptionAsync(() => dispose.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(failedClose);
        Assert.Contains(Leaves(failedClose!), cause => ReferenceEquals(cause, refused));
        Assert.Same(retainedCurrent, stream.Current);
        Assert.Equal(1, raw.Disposals);
        var current = (await rig.Tasks.GetAsync(service.CurrentCanonicalTask!.TaskId, default))!;
        Assert.Equal(TaskExecutionLifecycle.Suspended, current.State);
        Assert.NotNull(current.RecoveryObservation);
        Assert.Empty(current.Attempts);
        Assert.Equal(0, rig.Client.Dispatches);
        var processClose = rig.Tasks.CloseAndSuspendOriginalProducersAsync();
        var processFailure = await Record.ExceptionAsync(() => processClose.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(processFailure);
        Assert.Contains(Leaves(processFailure!), cause => ReferenceEquals(cause, refused));
    }

    private sealed class ProcessBurstClient : IOllamaClient
    {
        public int Moves; public int Disposals;
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) =>
            throw new InvalidOperationException("No positive unissued canonical completion");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) =>
            throw new InvalidOperationException("No unissued canonical tools");
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => new Burst(this);
        private sealed class Burst(ProcessBurstClient owner) : IAsyncEnumerable<string>, IAsyncEnumerator<string>
        {
            public string Current => "controlled raw partial token";
            public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken token = default) => this;
            public ValueTask<bool> MoveNextAsync() { owner.Moves++; return ValueTask.FromResult(owner.Moves <= 5000); }
            public ValueTask DisposeAsync() { owner.Disposals++; return ValueTask.CompletedTask; }
        }
    }

    [Fact]
    public async Task Actual_history_acquisition_and_activity_callbacks_after_held_read_guard_restored_context_without_losing_raw_write()
    {
        await using var rig = new Rig();
        var rows = new ProcessPublicationRows();
        var activity = new FloatingActivityStateStore();
        var (service, definition) = CreateProcessPublicationAgent(rig, rows, activity);
        var restored = ExecutionContext.Capture()!;
        var refusals = new List<Exception>();
        void ChallengeOriginalJoin()
        {
            ExecutionContext.Run(restored, _ =>
            {
                var refused = Record.Exception(() => { service.CloseAndSuspendOriginalProducersAsync(); });
                refusals.Add(Assert.IsType<InvalidOperationException>(refused));
            }, null);
        }
        rows.OnSave = value => { if (value.Status == AgentRunStatus.Running) ChallengeOriginalJoin(); };
        activity.Changed += (_, value) => { if (value.State == FloatingActivityState.Presented) ChallengeOriginalJoin(); };
        service.RunChanged += value => { if (value.Status == AgentRunStatus.Running) ChallengeOriginalJoin(); };
        var actual = service.RunAsync(definition.Id, "Real held history read then callbacks", CancellationToken.None);
        try
        {
            await rows.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(actual.IsCompleted);
            rows.ReleaseSameAcknowledgedRead();
            var returned = await actual.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(AgentRunStatus.Suspended, returned.Status);
            Assert.NotEmpty(refusals);
            Assert.Equal(returned, await rows.Inner.GetAsync(returned.Id, default));
            Assert.True(actual.IsCompletedSuccessfully);
            var process = Assert.Single(ReadProcessMember<IEnumerable>(service, "_agentProcessOperations").Cast<object>());
            var original = ReadProcessMember<object>(ReadProcessMember<object>(process, "Original"), "OriginalCanonicalAgent");
            Assert.Contains(ReadProcessMember<IEnumerable>(original, "ActualPersistence").Cast<Task>(),
                task => ReferenceEquals(task, rows.LastWrite));
            var histories = ReadProcessMember<IEnumerable>(original, "ActualHistoryOperations").Cast<object>();
            Assert.Contains(histories, history => ReadProcessMember<IReadOnlyList<Task>>(history, "Sources")
                .Any(task => ReferenceEquals(task, rows.LastWrite)));
            Assert.Empty(ReadProcessMember<IEnumerable>(service, "OriginalObservationFailures").Cast<object>());
            await service.CloseAndSuspendOriginalProducersAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await rig.Tasks.CloseAndSuspendOriginalProducersAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            rows.ReleaseSameAcknowledgedRead();
            _ = await Record.ExceptionAsync(() => actual.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task Genuine_history_ack_and_business_result_survive_observer_faults_while_process_drain_retains_exact_causes()
    {
        await using var rig = new Rig();
        var rows = new ProcessPublicationRows(holdFirstRead: false);
        var activity = new FloatingActivityStateStore();
        var (service, definition) = CreateProcessPublicationAgent(rig, rows, activity);
        var activityCause = new OperationCanceledException("Actual synchronous activity observer fault, caller remains live");
        var changedCause = new IOException("Actual synchronous terminal RunChanged observer fault");
        activity.Changed += (_, value) =>
        { if (value.State == FloatingActivityState.Presented) throw activityCause; };
        service.RunChanged += value =>
        { if (value.Status == AgentRunStatus.Suspended) throw changedCause; };
        var actual = service.RunAsync(definition.Id, "Durable ACK distinct from observer outcome", CancellationToken.None);
        var returned = await actual.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(actual.IsCompletedSuccessfully);
        Assert.Equal(AgentRunStatus.Suspended, returned.Status);
        Assert.Equal(TaskExecutionLifecycle.Suspended, returned.CanonicalTask!.State);
        Assert.Equal(returned, await rows.Inner.GetAsync(returned.Id, default));
        Assert.True(rows.LastWrite!.IsCompletedSuccessfully);
        var errors = ReadProcessMember<IEnumerable>(service, "OriginalObservationFailures").Cast<object>().ToArray();
        Assert.Contains(errors, value => ReferenceEquals(ReadProcessMember<Exception>(value, "Item2"), activityCause));
        Assert.Contains(errors, value => ReferenceEquals(ReadProcessMember<Exception>(value, "Item2"), changedCause));
        var processClose = service.CloseAndSuspendOriginalProducersAsync();
        var failure = await Record.ExceptionAsync(() => processClose.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(failure);
        Assert.True(processClose.IsFaulted);
        Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, activityCause));
        Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, changedCause));
        Assert.Equal(returned, await rows.Inner.GetAsync(returned.Id, default));
        Assert.Single(ReadProcessMember<IEnumerable>(service, "_agentProcessOperations").Cast<object>());
        await rig.Tasks.CloseAndSuspendOriginalProducersAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, rig.Client.Dispatches);
    }

    private static (AgentTaskRuntimeService Runtime, AgentDefinition Definition) CreateProcessPublicationAgent(
        Rig rig, ProcessPublicationRows rows, FloatingActivityStateStore activity)
    {
        var model = rig.Provider.Model.Model with
        { Name = rig.Provider.Model.Key, Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        var definition = new AgentDefinition(Guid.NewGuid(), "Synthetic actual process source", "No real account/provider",
            "Observe actual originals", "agent", model.Name, null, "[]", "{}", false, true, DateTimeOffset.UnixEpoch);
        return (new(new AgentCatalog(definition), rows, new AgentModelDiscovery(model),
            new CapabilityRegistryService(new EmptyAgentCapabilities()), rig.Service, rig.Policy, activity), definition);
    }

    private sealed class ProcessPublicationRows(bool holdFirstRead = true) : IAgentRunRepository
    {
        internal readonly AgentRows Inner = new();
        internal Action<AgentRun>? OnSave = null;
        internal Task? LastWrite;
        internal readonly TaskCompletionSource ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<AgentRun?> _originalRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;
        public Task UpsertAsync(AgentRun value, CancellationToken token)
        {
            OnSave?.Invoke(value);
            return LastWrite = Inner.UpsertAsync(value, token); // SAME actual write, not a notification substitute.
        }
        public Task<AgentRun?> GetAsync(Guid id, CancellationToken token)
        {
            if (holdFirstRead && Interlocked.Increment(ref _reads) == 1)
            { ReadEntered.TrySetResult(); return _originalRead.Task; }
            return Inner.GetAsync(id, token);
        }
        internal void ReleaseSameAcknowledgedRead()
        {
            var row = Inner.Values.Values.Select(value => System.Text.Json.JsonSerializer.Deserialize<AgentRun>(value)!).SingleOrDefault();
            _originalRead.TrySetResult(row); // Actual preceding ACK row; no reconstructed custody or authority.
        }
        public Task<IReadOnlyList<AgentRun>> GetRecentAsync(int count, CancellationToken token) => Inner.GetRecentAsync(count, token);
        public Task<IReadOnlyList<AgentRun>> GetByAgentAsync(Guid id, int count, CancellationToken token) => Inner.GetByAgentAsync(id, count, token);
    }

    private static IAsyncEnumerable<ChatStreamEvent> StartProcessChat(Rig rig) => rig.Service.SendAsync(
        rig.Conversation, "Controlled actual process original", rig.Provider.Model.Model with
        { Name = rig.Provider.Model.Key, Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } },
        EffortLevel.Medium, [], "controlled", "", DuoMode.Solo, null, null, null, null, default,
        taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask);

    // Inspects only the actual source instance and its privately retained Tasks; these values are
    // test diagnostics, never reconstructed custody, an issuer capability or completion authority.
    private static T ReadProcessMember<T>(object actual, string member)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var property = actual.GetType().GetProperty(member, flags);
        if (property is not null) return (T)property.GetValue(actual)!;
        return (T)actual.GetType().GetField(member, flags)!.GetValue(actual)!;
    }
}
