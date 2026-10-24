using Xunit;

namespace HavenOS.Apps.Dev.Tests;

// Synthetic owner-contract controls only; no real Home authorization, workspace trust, or
// build tool runs here.
public sealed class DeveloperPipelineExecutorTests
{
    [Fact]
    public async Task RunAsync_ExecutesStagesInDependencyOrderAndReturnsStageResults()
    {
        var gate = new Gate(new(DeveloperAuthorizationDecision.Granted, "grant", null));
        var runner = new Runner();
        var task = CreateTask([Stage("restore"), Stage("compile", "restore"), Stage("test", "compile")]);

        var result = await new DeveloperPipelineExecutor(runner, gate, new Trust(true)).RunAsync(
            new(Guid.NewGuid(), 3, task.TaskId, "caller"), task, ResourceIds(task));

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Succeeded);
        Assert.Equal(["restore", "compile", "test"], runner.Ran);
        Assert.All(result.Value.Stages, stage => Assert.Equal(DeveloperBuildStageState.Succeeded, stage.State));
        var request = gate.Request!;
        Assert.Equal("workspace.execute", request.RequiredPermissionScope);
        Assert.True(Assert.IsAssignableFrom<IList<string>>(request.AffectedRelativePaths).IsReadOnly);
    }

    [Fact]
    public async Task RunAsync_DoesNotRunWhenHomeApprovalIsPending()
    {
        var runner = new Runner();
        var gate = new Gate(new(DeveloperAuthorizationDecision.PendingApproval, null, "Approve in Home."));
        var task = CreateTask([Stage("compile")]);

        var result = await new DeveloperPipelineExecutor(runner, gate, new Trust(true)).RunAsync(
            new(Guid.NewGuid(), 1, task.TaskId, "caller"), task, ResourceIds(task));

        Assert.False(result.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.PermissionRequired, result.Error!.Code);
        Assert.Empty(runner.Ran);
    }

    [Fact]
    public async Task RunAsync_RequiresTrustBeforeCallingPermissionBrokerOrRunner()
    {
        var runner = new Runner();
        var gate = new Gate(new(DeveloperAuthorizationDecision.Granted, "grant", null));
        var task = CreateTask([Stage("compile")], requiresTrust: true);

        var result = await new DeveloperPipelineExecutor(runner, gate, new Trust(false)).RunAsync(
            new(Guid.NewGuid(), 1, task.TaskId, "caller"), task, ResourceIds(task));

        Assert.False(result.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.PermissionRequired, result.Error!.Code);
        Assert.Null(gate.Request);
        Assert.Empty(runner.Ran);
    }

    [Fact]
    public async Task RunAsync_PreCancelledRequestDoesNotCheckTrustOrRequestHomeAuthorization()
    {
        var runner = new Runner();
        var gate = new Gate(new(DeveloperAuthorizationDecision.Granted, "grant", null));
        var trust = new Trust(true);
        var task = CreateTask([Stage("compile")], requiresTrust: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new DeveloperPipelineExecutor(runner, gate, trust).RunAsync(
            new(Guid.NewGuid(), 1, task.TaskId, "caller"), task, ResourceIds(task), cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(0, trust.Calls);
        Assert.Null(gate.Request);
        Assert.Empty(runner.Ran);
    }

    [Fact]
    public async Task RunAsync_CancellationDuringTrustDoesNotProceedToHomeAuthorization()
    {
        var runner = new Runner();
        var gate = new Gate(new(DeveloperAuthorizationDecision.Granted, "grant", null));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trust = new Trust(true)
        {
            BeforeResponse = async _ =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return true;
            }
        };
        var task = CreateTask([Stage("compile")], requiresTrust: true);
        using var cancellation = new CancellationTokenSource();
        var pending = new DeveloperPipelineExecutor(runner, gate, trust).RunAsync(
            new(Guid.NewGuid(), 1, task.TaskId, "caller"), task, ResourceIds(task), cancellation.Token);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
        }
        finally { release.TrySetResult(); }
        var result = await pending;

        Assert.False(result.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(1, trust.Calls);
        Assert.Null(gate.Request);
        Assert.Empty(runner.Ran);
    }

    [Fact]
    public async Task RunAsync_CancellationDuringHomeAuthorizationIsNotReportedAsPendingApproval()
    {
        var runner = new Runner();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new Gate(new(DeveloperAuthorizationDecision.PendingApproval, null, "Pending in Home."))
        {
            BeforeResponse = async () =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
            }
        };
        var task = CreateTask([Stage("compile")]);
        using var cancellation = new CancellationTokenSource();
        var pending = new DeveloperPipelineExecutor(runner, gate, new Trust(true)).RunAsync(
            new(Guid.NewGuid(), 1, task.TaskId, "caller"), task, ResourceIds(task), cancellation.Token);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
        }
        finally { release.TrySetResult(); }
        var result = await pending;

        Assert.False(result.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.Cancelled, result.Error!.Code);
        Assert.Empty(runner.Ran);
    }

    [Fact]
    public async Task RunAsync_DoesNotMisreportProviderCancellationAsStageTimeout()
    {
        var task = CreateTask([Stage("compile")]);
        var result = await new DeveloperPipelineExecutor(new CancellingRunner(),
                new Gate(new(DeveloperAuthorizationDecision.Granted, "grant", null)), new Trust(true))
            .RunAsync(new(Guid.NewGuid(), 1, task.TaskId, "caller"), task, ResourceIds(task));

        Assert.True(result.Succeeded);
        var pipeline = result.Value!;
        Assert.False(pipeline.Cancelled);
        var stage = Assert.Single(pipeline.Stages);
        Assert.Equal(DeveloperBuildStageState.Failed, stage.State);
        Assert.Equal("The stage provider cancelled unexpectedly.", stage.FailureMessage);
    }

    [Fact]
    public async Task RunAsync_ReportsLinkedStageTimeoutAsFailure()
    {
        var task = CreateTask([Stage("compile") with { Timeout = TimeSpan.FromMilliseconds(10) }]);
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new DeveloperPipelineExecutor(new WaitingRunner(),
                new Gate(new(DeveloperAuthorizationDecision.Granted, "grant", null)), new Trust(true))
            .RunAsync(new(Guid.NewGuid(), 1, task.TaskId, "caller"), task, ResourceIds(task), safety.Token);

        Assert.True(result.Succeeded);
        var pipeline = result.Value!;
        Assert.False(pipeline.Cancelled);
        var stage = Assert.Single(pipeline.Stages);
        Assert.Equal(DeveloperBuildStageState.Failed, stage.State);
        Assert.Equal("The stage timed out.", stage.FailureMessage);
    }

    [Fact]
    public async Task RunAsync_ExecutesTheSameDetachedStagesThatHomeAuthorized()
    {
        var mutableArguments = new List<string> { "original-argument" };
        var stages = new List<DeveloperBuildStage> { Stage("compile") with { Arguments = mutableArguments } };
        var mutableTaskDependencies = new List<string> { "upstream-task" };
        var task = CreateTask(stages) with { DependsOn = mutableTaskDependencies };
        var stageResources = ResourceIds(task);
        var runner = new Runner();
        var gate = new Gate(new(DeveloperAuthorizationDecision.Granted, "grant", null));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.BeforeResponse = async () =>
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
        };
        var pending = new DeveloperPipelineExecutor(runner, gate, new Trust(true)).RunAsync(
            new(Guid.NewGuid(), 1, task.TaskId, "caller"), task, stageResources);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stages.Add(Stage("sign"));
            mutableArguments[0] = "mutated-after-authorization-started";
            mutableTaskDependencies[0] = "mutated-upstream-task";
        }
        finally { release.TrySetResult(); }

        var result = await pending;

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(1, gate.Request!.AffectedRelativePaths.Count);
        Assert.Equal(["compile"], runner.Ran);
        Assert.Equal(["compile"], result.Value!.Stages.Select(stage => stage.StageId));
        var executedTask = runner.LastTask!;
        Assert.Equal("original-argument", executedTask.Stages[0].Arguments[0]);
        Assert.Equal("upstream-task", executedTask.DependsOn[0]);
    }

    [Fact]
    public async Task RunAsync_UsesOneCapturedStageResourceMapForValidationAndAuthorization()
    {
        var task = CreateTask([Stage("compile")]);
        var validatedResource = Guid.NewGuid();
        var laterResource = Guid.NewGuid();
        var resources = new DivergentResourceMap("compile", validatedResource, laterResource);
        var gate = new Gate(new(DeveloperAuthorizationDecision.Granted, "grant", null));
        var result = await new DeveloperPipelineExecutor(new Runner(), gate, new Trust(true)).RunAsync(
            new(Guid.NewGuid(), 1, task.TaskId, "caller"), task, resources);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(validatedResource.ToString("D"), Assert.Single(gate.Request!.AffectedRelativePaths));
    }

    [Fact]
    public async Task RunAsync_ReturnsReadOnlyStageAndDiagnosticSnapshots()
    {
        var diagnostic = new DeveloperDiagnostic("compiler", "Error", "original diagnostic", null,
            null, null, 1, 2, 1, 3);
        var mutableDiagnostics = new List<DeveloperDiagnostic> { diagnostic };
        var runner = new Runner { Diagnostics = mutableDiagnostics };
        var task = CreateTask([Stage("compile")]);
        var result = await new DeveloperPipelineExecutor(runner,
                new Gate(new(DeveloperAuthorizationDecision.Granted, "grant", null)), new Trust(true))
            .RunAsync(new(Guid.NewGuid(), 1, task.TaskId, "caller"), task, ResourceIds(task));

        Assert.True(result.Succeeded, result.Error?.Message);
        var pipeline = result.Value!;
        Assert.True(pipeline.Succeeded);
        var stageList = Assert.IsAssignableFrom<IList<DeveloperPipelineStageResult>>(pipeline.Stages);
        Assert.True(stageList.IsReadOnly);
        var stage = Assert.Single(pipeline.Stages);
        Assert.True(Assert.IsAssignableFrom<IList<DeveloperDiagnostic>>(stage.Diagnostics).IsReadOnly);

        mutableDiagnostics[0] = diagnostic with { Message = "mutated provider result" };
        mutableDiagnostics.Add(diagnostic);
        Assert.Single(stage.Diagnostics);
        Assert.Equal("original diagnostic", stage.Diagnostics[0].Message);
    }

    [Fact]
    public async Task RunAsync_BlocksDependentsAfterFailedStageAndKeepsFailureResult()
    {
        var task = CreateTask([Stage("restore"), Stage("compile", "restore"), Stage("independent")]);
        var runner = new Runner(failingStage: "restore");
        var result = await new DeveloperPipelineExecutor(runner,
                new Gate(new(DeveloperAuthorizationDecision.Granted, "grant", null)), new Trust(true))
            .RunAsync(new(Guid.NewGuid(), 2, task.TaskId, "caller"), task, ResourceIds(task));

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.Succeeded);
        Assert.Equal(DeveloperBuildStageState.Failed, result.Value.Stages.Single(stage => stage.StageId == "restore").State);
        Assert.Equal(DeveloperBuildStageState.Blocked, result.Value.Stages.Single(stage => stage.StageId == "compile").State);
        Assert.Contains("independent", runner.Ran);
    }

    private static DeveloperTaskDefinition CreateTask(IReadOnlyList<DeveloperBuildStage> stages, bool requiresTrust = false) =>
        new("build", "Build", "user", "local", "root", [], stages, requiresTrust);
    private static DeveloperBuildStage Stage(string id, params string[] dependsOn) =>
        new(id, DeveloperBuildStageKind.Custom, id, dependsOn, "tool", [], TimeSpan.FromSeconds(3));
    private static IReadOnlyDictionary<string, Guid> ResourceIds(DeveloperTaskDefinition task) =>
        task.Stages.ToDictionary(stage => stage.StageId, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private sealed class Gate(DeveloperMutationAuthorization result) : IDeveloperWorkspaceMutationGate
    {
        public DeveloperMutationRequest? Request { get; private set; }
        public Func<Task>? BeforeResponse { get; set; }
        public async Task<DeveloperMutationAuthorization> AuthorizeAsync(DeveloperMutationRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            if (BeforeResponse is not null) await BeforeResponse().ConfigureAwait(false);
            return result;
        }
    }

    private sealed class Trust(bool trusted) : IDeveloperWorkspaceTrustService
    {
        public int Calls { get; private set; }
        public Func<CancellationToken, Task<bool>>? BeforeResponse { get; set; }
        public async Task<bool> IsTrustedAsync(Guid workspaceId, CancellationToken cancellationToken)
        {
            Calls++;
            return BeforeResponse is null ? trusted : await BeforeResponse(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class Runner(string? failingStage = null) : IDeveloperBuildStageRunner
    {
        public List<string> Ran { get; } = [];
        public DeveloperTaskDefinition? LastTask { get; private set; }
        public IReadOnlyList<DeveloperDiagnostic> Diagnostics { get; set; } = [];
        public Task<DeveloperStageExecutionResult> RunAsync(DeveloperTaskDefinition task, DeveloperBuildStage stage, CancellationToken cancellationToken)
        {
            LastTask = task;
            Ran.Add(stage.StageId);
            return Task.FromResult(new DeveloperStageExecutionResult(stage.StageId == failingStage ? 1 : 0, Diagnostics));
        }
    }

    private sealed class CancellingRunner : IDeveloperBuildStageRunner
    {
        public Task<DeveloperStageExecutionResult> RunAsync(
            DeveloperTaskDefinition task, DeveloperBuildStage stage, CancellationToken cancellationToken) =>
            Task.FromException<DeveloperStageExecutionResult>(new OperationCanceledException("Provider stopped without a task timeout."));
    }

    private sealed class WaitingRunner : IDeveloperBuildStageRunner
    {
        public async Task<DeveloperStageExecutionResult> RunAsync(
            DeveloperTaskDefinition task, DeveloperBuildStage stage, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new DeveloperStageExecutionResult(0, []);
        }
    }

    private sealed class DivergentResourceMap(string stageId, Guid validated, Guid later) : IReadOnlyDictionary<string, Guid>
    {
        public Guid this[string key] => key == stageId ? later : throw new KeyNotFoundException();
        public IEnumerable<string> Keys => new[] { stageId };
        public IEnumerable<Guid> Values => new[] { validated };
        public int Count => 1;
        public bool ContainsKey(string key) => key == stageId;
        public bool TryGetValue(string key, out Guid value)
        {
            value = validated;
            return key == stageId;
        }
        public IEnumerator<KeyValuePair<string, Guid>> GetEnumerator()
        {
            yield return new KeyValuePair<string, Guid>(stageId, validated);
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
