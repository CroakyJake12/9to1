using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Protects actual CLR entry points used by the retained Windows consumer.</summary>
public sealed class TaskRunLegacyApiCompatibilityTests
{
    [Fact]
    public void Existing_full_arity_Chat_constructor_and_Send_entry_points_remain_exported()
    {
        Type[] constructor = [typeof(IConversationRepository), typeof(IOllamaClient), typeof(CapabilityPreflightService),
            typeof(IConversationSafetyService), typeof(WorkspaceToolRuntime), typeof(ComputerToolRuntime),
            typeof(BrowserToolRuntime), typeof(AutomationToolRuntime), typeof(ToolAvailabilityPlanner), typeof(ChatModelInventoryCache),
            typeof(McpToolRuntime), typeof(CalendarConnectionToolRuntime), typeof(PluginToolRuntime), typeof(IExecutionEventSink),
            typeof(AutonomousRecoveryService), typeof(RemediationCoordinator), typeof(ModelPersonalityService),
            typeof(ModelPermissionEvaluator), typeof(IDefaultProviderStore), typeof(CheckpointService),
            typeof(IProjectInstructionSource), typeof(IMemoryQuerySource)];
        var oldConstructor = Assert.IsAssignableFrom<System.Reflection.ConstructorInfo>(typeof(ChatSessionService).GetConstructor(constructor));
        Assert.All(oldConstructor.GetParameters(), parameter => Assert.False(parameter.IsOptional));
        Assert.NotNull(typeof(ChatSessionService).GetConstructor([.. constructor, typeof(TaskExecutionCoordinator), typeof(ITaskRunToolActionOwner)]));

        Type[] send = [typeof(Conversation), typeof(string), typeof(ModelDescriptor), typeof(EffortLevel),
            typeof(IReadOnlyCollection<ActiveCapability>), typeof(string), typeof(string), typeof(DuoMode), typeof(string),
            typeof(string), typeof(string), typeof(IReadOnlyList<string>), typeof(CancellationToken),
            typeof(IReadOnlyCollection<ActivePrompt>), typeof(string), typeof(GenerationOptions), typeof(PermissionMode),
            typeof(PermissionMode), typeof(PermissionMode), typeof(IReadOnlyCollection<ToolCapability>),
            typeof(IReadOnlyCollection<ActiveCapability>), typeof(ComputerUseRequest)];
        var oldSend = Assert.IsAssignableFrom<System.Reflection.MethodInfo>(typeof(ChatSessionService).GetMethod(nameof(ChatSessionService.SendAsync), send));
        Assert.Equal(typeof(IAsyncEnumerable<ChatStreamEvent>), oldSend.ReturnType);
        Assert.All(oldSend.GetParameters(), parameter => Assert.False(parameter.IsOptional));
        var newSend = Assert.IsAssignableFrom<System.Reflection.MethodInfo>(typeof(ChatSessionService).GetMethod(nameof(ChatSessionService.SendAsync),
            [.. send, typeof(ProviderExecutionContext), typeof(TaskRunExecutionIntent)]));
        Assert.Equal(TaskRunExecutionIntent.OrdinaryConversation, Assert.IsType<TaskRunExecutionIntent>(newSend.GetParameters()[^1].DefaultValue));
    }

    [Fact]
    public async Task Legacy_action_entry_retains_ordinary_telemetry_but_cannot_admit_an_authority_bound_action()
    {
        var method = Assert.IsAssignableFrom<System.Reflection.MethodInfo>(typeof(TaskExecutionCoordinator).GetMethod(
            nameof(TaskExecutionCoordinator.RegisterActionAsync), [typeof(Guid), typeof(Guid), typeof(Guid?), typeof(string),
                typeof(TaskActionInterruptionPolicy), typeof(CancellationTokenSource), typeof(IReadOnlyCollection<string>), typeof(CancellationToken)]));
        Assert.All(method.GetParameters(), parameter => Assert.False(parameter.IsOptional));
        var repository = new LegacyTelemetryRepository();
        var coordinator = new TaskExecutionCoordinator(repository, new NullSink(), null);
        var task = await coordinator.BeginAsync(Guid.NewGuid(), Guid.NewGuid(), "legacy ordinary", TaskExecutionDurability.PersistedPlan, [], default);
        var actionId = Guid.NewGuid();
        var ordinary = await coordinator.RegisterActionAsync(task.TaskId, actionId, null, "ordinary observation",
            TaskActionInterruptionPolicy.ReadOnlyCancellable, null, [], default);
        Assert.Equal(TaskPlanNodeState.Running, Assert.Single(ordinary.Plan).State);
        Assert.Equal(actionId, Assert.Single(ordinary.Plan).ActionId);
        // Controlled durable provenance is deliberately insufficient to mint the missing original attempt.
        repository.Value = ordinary with { OwnerBinding = new TaskExecutionOwnerBinding(task.TaskId, task.ContextId, task.ExecutionId,
            "controlled actor", "controlled profile", null, null, "synthetic auth revision", "synthetic receipt observation") };
        var writesBefore = repository.Writes;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RegisterActionAsync(task.TaskId, Guid.NewGuid(), null,
            "must refuse before registration", TaskActionInterruptionPolicy.AtomicCommit, null, [], default));
        Assert.Equal(writesBefore, repository.Writes);
        Assert.Equal(actionId, Assert.Single(Assert.IsType<TaskExecutionSnapshot>(repository.Value).Plan).ActionId);
    }

    [Fact]
    public async Task Existing_coordinator_and_tracker_constructor_signatures_remain_exported()
    {
        var coordinator = Assert.IsAssignableFrom<System.Reflection.ConstructorInfo>(typeof(TaskExecutionCoordinator).GetConstructor(
            [typeof(ITaskExecutionRepository), typeof(IExecutionEventSink), typeof(TimeProvider)]));
        Assert.All(coordinator.GetParameters(), parameter => Assert.False(parameter.IsOptional));
        var tracker = Assert.IsAssignableFrom<System.Reflection.ConstructorInfo>(typeof(ChatExecutionTracker).GetConstructor(
            [typeof(ChatExecutionStage), typeof(Func<ChatEtaRequest, CancellationToken, Task<string?>>)]));
        Assert.All(tracker.GetParameters(), parameter => Assert.False(parameter.IsOptional));
        await using var omittedDefaults = new ChatExecutionTracker();
        await using var legacyFull = new ChatExecutionTracker(ChatExecutionStage.Preparing, null);
        var executionId = Guid.NewGuid();
        await using var bound = new ChatExecutionTracker(ChatExecutionStage.Preparing, null, executionId);
        Assert.NotEqual(omittedDefaults.OperationId, legacyFull.OperationId);
        Assert.Equal(executionId, bound.OperationId);
    }

    private sealed class LegacyTelemetryRepository : ITaskExecutionRepository
    {
        public TaskExecutionSnapshot? Value { get; set; }
        public int Writes { get; private set; }
        public Task UpsertAsync(TaskExecutionSnapshot snapshot, CancellationToken token) { Value = snapshot; Writes++; return Task.CompletedTask; }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Value?.TaskId == id ? Value : null);
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) => Task.FromResult(Value?.ContextId == id ? Value : null);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(Value is null ? [] : [Value]);
    }
    private sealed class NullSink : IExecutionEventSink
    {
        public bool TryPublish(ExecutionEvent value) => true;
    }
}
