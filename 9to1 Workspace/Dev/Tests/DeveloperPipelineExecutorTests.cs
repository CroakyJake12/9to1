using Xunit;

namespace HavenOS.Apps.Dev.Tests;

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
        Assert.Equal("workspace.execute", gate.Request!.RequiredPermissionScope);
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
        public Task<DeveloperMutationAuthorization> AuthorizeAsync(DeveloperMutationRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class Trust(bool trusted) : IDeveloperWorkspaceTrustService
    {
        public Task<bool> IsTrustedAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.FromResult(trusted);
    }

    private sealed class Runner(string? failingStage = null) : IDeveloperBuildStageRunner
    {
        public List<string> Ran { get; } = [];
        public Task<DeveloperStageExecutionResult> RunAsync(DeveloperTaskDefinition task, DeveloperBuildStage stage, CancellationToken cancellationToken)
        {
            Ran.Add(stage.StageId);
            return Task.FromResult(new DeveloperStageExecutionResult(stage.StageId == failingStage ? 1 : 0, []));
        }
    }
}
