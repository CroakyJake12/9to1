namespace HavenOS.Apps.Dev;

public sealed record DeveloperStageExecutionResult(
    int ExitCode,
    IReadOnlyList<DeveloperDiagnostic> Diagnostics,
    string? OutputSummary = null);

public sealed record DeveloperDiagnostic(
    string ProviderId,
    string Severity,
    string Message,
    string? Code,
    Guid? FileId,
    string? CanonicalResourceId,
    int? StartLine,
    int? StartCharacter,
    int? EndLine,
    int? EndCharacter);

public sealed record DeveloperPipelineStageResult(
    string StageId,
    DeveloperBuildStageState State,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int? ExitCode,
    IReadOnlyList<DeveloperDiagnostic> Diagnostics,
    string? FailureMessage);

public sealed record DeveloperPipelineResult(
    Guid RequestId,
    Guid WorkspaceId,
    string TaskId,
    long WorkspaceRevision,
    IReadOnlyList<DeveloperPipelineStageResult> Stages,
    bool Succeeded,
    bool Cancelled);

public sealed record DeveloperTaskExecutionRequest(
    Guid WorkspaceId,
    long WorkspaceRevision,
    string TaskId,
    string CallerIdentity);

public interface IDeveloperWorkspaceTrustService
{
    Task<bool> IsTrustedAsync(Guid workspaceId, CancellationToken cancellationToken);
}

public interface IDeveloperBuildStageRunner
{
    Task<DeveloperStageExecutionResult> RunAsync(
        DeveloperTaskDefinition task, DeveloperBuildStage stage, CancellationToken cancellationToken);
}

/// <summary>Runs inspectable task stages in dependency order and routes execution through the Home permission gate.</summary>
public sealed class DeveloperPipelineExecutor(
    IDeveloperBuildStageRunner runner,
    IDeveloperWorkspaceMutationGate mutationGate,
    IDeveloperWorkspaceTrustService workspaceTrust)
{
    public async Task<DeveloperOperationResult<DeveloperPipelineResult>> RunAsync(
        DeveloperTaskExecutionRequest request,
        DeveloperTaskDefinition task,
        IReadOnlyDictionary<string, Guid> stageResourceIds,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();
        if (request is null || task is null || stageResourceIds is null || request.WorkspaceId == Guid.Empty ||
            request.WorkspaceRevision < 1 || !StringComparer.Ordinal.Equals(request.TaskId, task.TaskId) ||
            string.IsNullOrWhiteSpace(request.CallerIdentity))
            return Fail(DeveloperOperationErrorCode.InvalidInput, "A valid task execution request is required.", "task", requestId);
        if (task.RequiresWorkspaceTrust)
        {
            bool trusted;
            try { trusted = await workspaceTrust.IsTrustedAsync(request.WorkspaceId, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Fail(DeveloperOperationErrorCode.Cancelled, "Task execution was cancelled while checking workspace trust.", task.TaskId, requestId);
            }
            catch (Exception)
            {
                return Fail(DeveloperOperationErrorCode.CapabilityUnavailable,
                    "Workspace trust could not be verified; the task was not started.", task.TaskId, requestId);
            }
            if (!trusted)
                return Fail(DeveloperOperationErrorCode.PermissionRequired,
                    "This task executes workspace-controlled code and requires workspace trust.", task.TaskId, requestId);
        }
        if (ValidateTask(task, stageResourceIds) is { } taskError)
            return Fail(DeveloperOperationErrorCode.InvalidInput, taskError, task.TaskId, requestId);

        var resourceIds = task.Stages.Select(stage => stageResourceIds[stage.StageId]).Distinct().ToArray();
        DeveloperMutationAuthorization authorization;
        try
        {
            authorization = await mutationGate.AuthorizeAsync(new DeveloperMutationRequest(
                requestId, request.CallerIdentity.Trim(), "9to1.Dev.RunTask", "workspace.execute",
                request.WorkspaceId, request.WorkspaceRevision, task.TaskId, resourceIds.Select(id => id.ToString("D")).ToArray(),
                IsReversible: false, HasExternalSideEffects: true), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail(DeveloperOperationErrorCode.Cancelled, "Task execution was cancelled before authorization completed.", task.TaskId, requestId);
        }
        catch (Exception)
        {
            return Fail(DeveloperOperationErrorCode.CapabilityUnavailable, "Home authorization could not be reached; the task was not started.", task.TaskId, requestId);
        }
        if (authorization.Decision != DeveloperAuthorizationDecision.Granted || string.IsNullOrWhiteSpace(authorization.GrantId))
            return Fail(authorization.Decision == DeveloperAuthorizationDecision.PendingApproval
                    ? DeveloperOperationErrorCode.PermissionRequired : DeveloperOperationErrorCode.PermissionDenied,
                authorization.UserMessage ?? "Home did not authorize task execution.", task.TaskId, requestId);

        var completed = new Dictionary<string, DeveloperPipelineStageResult>(StringComparer.Ordinal);
        foreach (var stage in TopologicalOrder(task.Stages))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                foreach (var pending in task.Stages.Where(candidate => !completed.ContainsKey(candidate.StageId)))
                    completed[pending.StageId] = Result(pending, DeveloperBuildStageState.Cancelled, null, [], "The task was cancelled before this stage started.");
                break;
            }
            if (stage.DependsOn.Any(dependency => completed[dependency].State != DeveloperBuildStageState.Succeeded))
            {
                completed[stage.StageId] = Result(stage, DeveloperBuildStageState.Blocked, null, [], "A dependency stage did not succeed.");
                continue;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (stage.Timeout is { } duration && duration > TimeSpan.Zero) timeout.CancelAfter(duration);
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                var outcome = await runner.RunAsync(task, stage, timeout.Token).ConfigureAwait(false);
                completed[stage.StageId] = Result(stage, startedAt,
                    outcome.ExitCode == 0 ? DeveloperBuildStageState.Succeeded : DeveloperBuildStageState.Failed,
                    outcome.ExitCode, outcome.Diagnostics, outcome.ExitCode == 0 ? null : outcome.OutputSummary ?? "The stage failed.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                completed[stage.StageId] = Result(stage, startedAt, DeveloperBuildStageState.Failed, null, [], "The stage timed out.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completed[stage.StageId] = Result(stage, startedAt, DeveloperBuildStageState.Cancelled, null, [], "The task was cancelled.");
                foreach (var pending in task.Stages.Where(candidate => !completed.ContainsKey(candidate.StageId)))
                    completed[pending.StageId] = Result(pending, DeveloperBuildStageState.Cancelled, null, [], "The task was cancelled before this stage started.");
                break;
            }
            catch (Exception)
            {
                completed[stage.StageId] = Result(stage, startedAt, DeveloperBuildStageState.Failed, null, [], "The stage provider failed unexpectedly.");
            }
        }

        var stageResults = TopologicalOrder(task.Stages).Select(stage => completed[stage.StageId]).ToArray();
        var cancelled = stageResults.Any(static stage => stage.State == DeveloperBuildStageState.Cancelled);
        var succeeded = stageResults.All(static stage => stage.State == DeveloperBuildStageState.Succeeded);
        var result = new DeveloperPipelineResult(requestId, request.WorkspaceId, task.TaskId,
            request.WorkspaceRevision, stageResults, succeeded, cancelled);
        return DeveloperOperationResult<DeveloperPipelineResult>.Success(result, requestId);
    }

    private static DeveloperPipelineStageResult Result(DeveloperBuildStage stage, DeveloperBuildStageState state,
        int? exitCode, IReadOnlyList<DeveloperDiagnostic> diagnostics, string? failure) =>
        Result(stage, DateTimeOffset.UtcNow, state, exitCode, diagnostics, failure);

    private static DeveloperPipelineStageResult Result(DeveloperBuildStage stage, DateTimeOffset startedAt,
        DeveloperBuildStageState state, int? exitCode, IReadOnlyList<DeveloperDiagnostic> diagnostics, string? failure) =>
        new(stage.StageId, state, startedAt, DateTimeOffset.UtcNow, exitCode, diagnostics, failure);

    private static string? ValidateTask(DeveloperTaskDefinition task, IReadOnlyDictionary<string, Guid> resourceIds)
    {
        if (string.IsNullOrWhiteSpace(task.TaskId) || task.Revision < 1 || task.Stages is null || task.Stages.Count == 0)
            return "A task needs a stable ID, positive revision, and at least one stage.";
        if (task.Stages.Any(stage => stage is null || string.IsNullOrWhiteSpace(stage.StageId) ||
                                     stage.DependsOn is null || stage.Arguments is null || stage.Timeout < TimeSpan.Zero))
            return "Every stage must have an ID and valid dependencies, arguments, and timeout.";
        if (task.Stages.Select(static stage => stage.StageId).Distinct(StringComparer.Ordinal).Count() != task.Stages.Count)
            return "Stage IDs must be unique.";
        var ids = task.Stages.Select(static stage => stage.StageId).ToHashSet(StringComparer.Ordinal);
        if (task.Stages.Any(stage => stage.DependsOn.Any(dependency => !ids.Contains(dependency) || dependency == stage.StageId)))
            return "Every stage dependency must identify a different stage in this task.";
        if (task.Stages.Any(stage => !resourceIds.TryGetValue(stage.StageId, out var id) || id == Guid.Empty))
            return "Every stage needs a stable affected resource identity for the Home approval preview.";
        try { _ = TopologicalOrder(task.Stages); }
        catch (InvalidOperationException) { return "Task dependencies contain a cycle."; }
        return null;
    }

    private static IReadOnlyList<DeveloperBuildStage> TopologicalOrder(IReadOnlyList<DeveloperBuildStage> stages)
    {
        var remaining = stages.ToDictionary(static stage => stage.StageId, static stage => stage.DependsOn.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var result = new List<DeveloperBuildStage>(stages.Count);
        while (remaining.Count > 0)
        {
            var ready = stages.Where(stage => remaining.TryGetValue(stage.StageId, out var dependencies) && dependencies.Count == 0).ToArray();
            if (ready.Length == 0) throw new InvalidOperationException("Task dependencies contain a cycle.");
            foreach (var stage in ready)
            {
                result.Add(stage);
                remaining.Remove(stage.StageId);
                foreach (var dependencies in remaining.Values) dependencies.Remove(stage.StageId);
            }
        }
        return result;
    }

    private static DeveloperOperationResult<DeveloperPipelineResult> Fail(
        DeveloperOperationErrorCode code, string message, string targetId, Guid requestId) =>
        DeveloperOperationResult<DeveloperPipelineResult>.Failure(code, message, targetId, retryable: code == DeveloperOperationErrorCode.PermissionRequired, requestId: requestId);
}
