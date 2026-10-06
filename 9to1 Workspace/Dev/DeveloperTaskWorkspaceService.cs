using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Dev;

/// <summary>
/// Typed Dev operations over the maintained workspace executor and canonical Task/Run owner.
/// This service neither starts another task nor infers authority from workspace metadata or CAKE login.
/// Register this SAME service instance for app and embedded Space views to coalesce identical submissions.
/// </summary>
public sealed class DeveloperTaskWorkspaceService(
    IDeveloperWorkspaceStore workspaces, DeveloperCanonicalWorkspaceBinding workspaceBinding,
    TaskExecutionCoordinator tasks, ITaskRunToolActionOwner toolOwner, WorkspaceToolRuntime runtime,
    IDeveloperWorkspaceTrustService? executionTrust = null) : IAsyncDisposable
{
    // Unresolved original calls remain inspectable. This is runtime custody, not a purchased allowance.
    public const int MaximumRetainedInvocations = 128;
    private readonly object _gate = new();
    private readonly AsyncLocal<Invocation?> _executing = new();
    [ThreadStatic] private static Dictionary<DeveloperTaskWorkspaceService, int>? _synchronousSources;
    private bool _retiring;
    private Task? _close;

    private readonly Dictionary<(Guid TaskId, Guid ActionId), Invocation> _originals = [];
    private sealed class Invocation(DeveloperProjectReference project, DeveloperCanonicalActionContext context, string digest)
    {
        public DeveloperProjectReference Project { get; } = project;
        public DeveloperCanonicalActionContext Context { get; } = context;
        public string Digest { get; } = digest;
        public Task<DeveloperOperationResult<DeveloperActionObservation>> Original = null!;
        public Invocation? Parent { get; init; }
        public bool Live;
        public bool Healthy;
        public readonly List<Task> Sources = [];
    }
    private readonly List<Invocation> _observations = [];

    /// <summary>Observation of the SAME configured canonical owner; this grants no authority.</summary>
    public bool IsBoundToOriginalCanonicalOwner(TaskExecutionCoordinator sameOwner) => ReferenceEquals(tasks, sameOwner);

    public void RequestRetirement() { lock (_gate) _retiring = true; }
    public void DemandExternalOriginalRetirementJoin()
    {
        workspaceBinding.DemandExternalOriginalRetirementJoin();
        if (_synchronousSources?.ContainsKey(this) == true) throw new InvalidOperationException("An original source callback cannot join its development owner.");
        for (var original = _executing.Value; original is not null; original = original.Parent)
            if (Volatile.Read(ref original.Live)) throw new InvalidOperationException("A live development original cannot join its own retirement.");
        if (executionTrust is IDeveloperWorkspaceOriginalProjectExecutionTrustService originalTrust)
            originalTrust.DemandExternalOriginalExecutionTrustJoin();
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        TaskCompletionSource start; Invocation[] originals; Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true;
            originals = _originals.Values.Concat(_observations).ToArray();
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DrainOriginalsAsync(start.Task, originals); _close = actual;
        }
        start.TrySetResult();
        return actual;
    }
    private async Task DrainOriginalsAsync(Task start, Invocation[] originals)
    {
        await start.ConfigureAwait(false);
        var errors = new List<Exception>();
        foreach (var original in originals)
        {
            try
            {
                var result = await original.Original.ConfigureAwait(false);
                if (result.Value is { } observation)
                {
                    if (observation.OriginalToolResult?.OriginalRuntimeError is { } cause) AddOriginalCause(errors, cause);
                    if (observation.RequiresOutcomeInspection)
                        errors.Add(new InvalidOperationException("The original development action has an unresolved owner outcome: " + observation.OriginalContext.ActionId.ToString("D")));
                }
            }
            catch (Exception error) { CaptureOriginalCauses(errors, original.Original, error); }
            // The actual owning driver has now completed its finally. All raw child Tasks
            // admitted by that driver are stable and independently joined, even after a fault.
            Task[] sources; lock (_gate) sources = original.Sources.ToArray();
            foreach (var source in sources)
                try { await source.ConfigureAwait(false); }
                catch (Exception error) { CaptureOriginalCauses(errors, source, error); }
        }
        if (errors.Count != 0) throw new AggregateException("Development originals did not drain cleanly.", errors);
    }
    private static void CaptureOriginalCauses(List<Exception> errors, Task original, Exception observed)
    {
        foreach (var cause in original.Exception?.InnerExceptions ?? new[] { observed }.AsEnumerable()) AddOriginalCause(errors, cause);
    }
    private static void AddOriginalCause(List<Exception> errors, Exception cause)
    { if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    public async Task<DeveloperOperationResult<DeveloperResolvedProject>> ResolveAsync(
        DeveloperProjectReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.WorkspaceId == Guid.Empty || reference.ProjectId == Guid.Empty || reference.RootId == Guid.Empty ||
            reference.WorkspaceRevision < 1 || reference.ProjectRevision < 1)
            return Fail<DeveloperResolvedProject>(DeveloperOperationErrorCode.InvalidInput, "A stable current project reference is required.", reference.WorkspaceId);
        var stored = await ObserveOriginalAsync(() => workspaces.GetAsync(reference.WorkspaceId, cancellationToken)).ConfigureAwait(false);
        if (!stored.Succeeded || stored.Value is null)
            return new(stored.RequestId, null, stored.Error ?? new(DeveloperOperationErrorCode.WorkspaceNotFound,
                "The saved workspace was not found.", reference.WorkspaceId.ToString("D"), true, false));
        var workspace = stored.Value;
        if (workspace.WorkspaceId != reference.WorkspaceId || workspace.Validate() is not null)
            return Fail<DeveloperResolvedProject>(DeveloperOperationErrorCode.InvalidStoredData, "The saved workspace identity or shape is invalid.", reference.WorkspaceId);
        if (workspace.Revision != reference.WorkspaceRevision)
            return Fail<DeveloperResolvedProject>(DeveloperOperationErrorCode.RevisionConflict, "The workspace changed; reopen its current reference before acting.", reference.WorkspaceId);
        var project = workspace.Projects.SingleOrDefault(value => value.ProjectId == reference.ProjectId);
        if (project is null) return Fail<DeveloperResolvedProject>(DeveloperOperationErrorCode.ProjectNotFound, "The project does not belong to this workspace.", reference.ProjectId);
        if (project.Revision != reference.ProjectRevision)
            return Fail<DeveloperResolvedProject>(DeveloperOperationErrorCode.RevisionConflict, "The project changed; reopen its current reference before acting.", reference.ProjectId);
        var root = workspace.Roots.SingleOrDefault(value => value.RootId == reference.RootId);
        if (root is null || !project.RootIds.Contains(reference.RootId))
            return Fail<DeveloperResolvedProject>(DeveloperOperationErrorCode.InvalidInput, "The selected root does not belong to this project.", reference.RootId);
        DeveloperSourceControlBinding? repository = null;
        if (reference.RepositoryBindingId is not null)
        {
            var candidates = workspace.SourceControlBindings.Where(value =>
                value.BindingId == reference.RepositoryBindingId && value.RootId == root.RootId).Take(2).ToArray();
            if (candidates.Length != 1 || string.IsNullOrWhiteSpace(candidates[0].CanonicalRepositoryId))
                return Fail<DeveloperResolvedProject>(DeveloperOperationErrorCode.InvalidStoredData, "The canonical repository binding is absent or ambiguous.", root.RootId);
            repository = candidates[0];
        }
        return DeveloperOperationResult<DeveloperResolvedProject>.Success(new(reference, workspace, project, root, repository));
    }

    public Task<DeveloperOperationResult<DeveloperActionObservation>> ReadFileAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, DeveloperCodeDocument document,
        CancellationToken cancellationToken = default)
    {
        DemandDocument(project, document);
        return DispatchAsync(project, context, Call("read_file", ("path", document.RelativePath)), "Read source file", cancellationToken, document.WorkspaceRoot);
    }

    public Task<DeveloperOperationResult<DeveloperActionObservation>> ListFilesAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, string relativeFolder = ".", int maximumDepth = 5,
        CancellationToken cancellationToken = default)
    {
        DemandRelativePath(relativeFolder, allowRoot: true);
        if (maximumDepth is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        return DispatchAsync(project, context, Call("list_files", ("path", relativeFolder), ("max_depth", maximumDepth)), "Inspect project tree", cancellationToken);
    }

    public Task<DeveloperOperationResult<DeveloperActionObservation>> SearchFilesAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, string query, string relativeFolder = ".", int maximumResults = 100,
        CancellationToken cancellationToken = default)
    {
        DemandRelativePath(relativeFolder, allowRoot: true);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Length > 4000 || maximumResults is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(maximumResults));
        return DispatchAsync(project, context, Call("search_files", ("path", relativeFolder), ("query", query), ("max_results", maximumResults)), "Search project source", cancellationToken);
    }

    public Task<DeveloperOperationResult<DeveloperActionObservation>> PreviewEditAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, DeveloperReviewedTextEdit edit,
        CancellationToken cancellationToken = default) => EditAsync(project, context, edit, apply: false, cancellationToken);

    public Task<DeveloperOperationResult<DeveloperActionObservation>> ApplyEditAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, DeveloperReviewedTextEdit edit,
        CancellationToken cancellationToken = default) => EditAsync(project, context, edit, apply: true, cancellationToken);

    private Task<DeveloperOperationResult<DeveloperActionObservation>> EditAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, DeveloperReviewedTextEdit edit, bool apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        DemandDocument(project, edit.Document);
        if (edit.ExpectedSha256 is not { Length: 64 } || !edit.ExpectedSha256.All(Uri.IsHexDigit) || edit.NewText is null || edit.NewText.Length > 2_000_000)
            throw new ArgumentException("A reviewed current SHA-256 and bounded complete replacement text are required.", nameof(edit));
        var json = JsonSerializer.Serialize(new[] { new { path = edit.Document.RelativePath, content = edit.NewText, expectedSha256 = edit.ExpectedSha256.ToLowerInvariant() } });
        return DispatchAsync(project, context, Call(apply ? "apply_change_set" : "preview_change_set", ("changes_json", json)),
            apply ? "Apply reviewed source edit" : "Preview source edit", cancellationToken, edit.Document.WorkspaceRoot);
    }

    public Task<DeveloperOperationResult<DeveloperActionObservation>> RunTerminalAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, string command, int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        DemandCommand(command, timeoutSeconds, 900);
        return DispatchAsync(project, context, Call("run_command", ("command", command), ("timeout_seconds", timeoutSeconds)), "Run project command", cancellationToken);
    }

    /// <summary>Explicit command avoids the legacy test detector's direct filesystem probe.</summary>
    public Task<DeveloperOperationResult<DeveloperActionObservation>> RunTestsAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, string command, int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        // The maintained runtime's actual command owner currently caps its process at 900 seconds.
        DemandCommand(command, timeoutSeconds, 900);
        return DispatchAsync(project, context, Call("run_tests", ("command", command), ("timeout_seconds", timeoutSeconds)), "Run project tests", cancellationToken);
    }

    /// <summary>Uses the exact saved task/stage revision. This is one existing stage, not another pipeline or run.</summary>
    public Task<DeveloperOperationResult<DeveloperActionObservation>> RunStageAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, string taskId, long taskRevision, string stageId,
        CancellationToken cancellationToken = default) => AdmitAsync(project, context,
            RequestDigest(new { operation = "run-stage", taskId, taskRevision, stageId }),
            async token =>
            {
                var resolved = await ResolveAsync(project, token).ConfigureAwait(false);
                if (!resolved.Succeeded || resolved.Value is null) return new(resolved.RequestId, null, resolved.Error);
                var task = resolved.Value.Workspace.Tasks.SingleOrDefault(value => value.TaskId == taskId);
                if (task is null || task.Revision != taskRevision || task.EnvironmentId != resolved.Value.Root.EnvironmentId ||
                    task.WorkingDirectoryRootId != project.RootId.ToString("D"))
                    return Fail<PreparedCall>(DeveloperOperationErrorCode.RevisionConflict, "The saved task/root/environment revision does not match this request.", context.ActionId);
                var stage = task.Stages.SingleOrDefault(value => value.StageId == stageId);
                if (stage is null || stage.DependsOn.Count > 0 || task.DependsOn.Count > 0)
                    return Fail<PreparedCall>(DeveloperOperationErrorCode.CapabilityUnavailable, "A dependent pipeline requires the existing pipeline dependency coordinator; a stage cannot bypass its prerequisites.", context.ActionId);
                if (stage.Arguments.Count != 0)
                    return Fail<PreparedCall>(DeveloperOperationErrorCode.CapabilityUnavailable, "This command provider requires an explicit saved command; it cannot reinterpret an executable argument vector.", context.ActionId);
                var timeout = stage.Timeout is { } duration ? checked((int)Math.Ceiling(duration.TotalSeconds)) : 600;
                DemandCommand(stage.Command ?? "", timeout, 900);
                return DeveloperOperationResult<PreparedCall>.Success(new(Call(stage.Kind == DeveloperBuildStageKind.Test ? "run_tests" : "run_command",
                    ("command", stage.Command!), ("timeout_seconds", timeout)), "Run saved development stage", task, stage));
            }, cancellationToken);

    public Task<DeveloperOperationResult<DeveloperActionObservation>> GitAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, DeveloperGitOperation operation,
        string? branchName = null, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        if (operation is DeveloperGitOperation.CreateBranch or DeveloperGitOperation.SwitchBranch) DemandBranch(branchName);
        return AdmitAsync(project, context, RequestDigest(new { operation = "git", kind = operation, branchName }),
            async token =>
            {
                var resolved = await ResolveAsync(project, token).ConfigureAwait(false);
                if (!resolved.Succeeded || resolved.Value is null) return new(resolved.RequestId, null, resolved.Error);
                if (resolved.Value.Repository is not { ProviderId: "git" })
                    return Fail<PreparedCall>(DeveloperOperationErrorCode.CapabilityUnavailable, "The selected root has no canonical Git provider binding.", project.RootId);
                string[] arguments = operation switch
                {
                    DeveloperGitOperation.Status => ["status", "--porcelain=v1", "--branch"],
                    DeveloperGitOperation.Diff => ["diff", "--no-ext-diff", "--no-textconv", "--"],
                    DeveloperGitOperation.Branches => ["branch", "--list", "--no-color"],
                    DeveloperGitOperation.CreateBranch => ["branch", "--", DemandBranch(branchName)],
                    DeveloperGitOperation.SwitchBranch => ["switch", "--no-guess", "--", DemandBranch(branchName)],
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                };
                // Literal arguments delegate the existing registered command executor. No client
                // fragment becomes syntax, and all actual permission/native-final fences still apply.
                var command = (OperatingSystem.IsWindows() ? "& " : "") + "'git' '--no-pager' '-C' " + Quote(resolved.Value.Root.Location) + " " + string.Join(" ", arguments.Select(Quote));
                return DeveloperOperationResult<PreparedCall>.Success(new(Call("run_command", ("command", command), ("timeout_seconds", 120)), "Inspect or update project source control"));
            }, cancellationToken);
    }

    private sealed record PreparedCall(OllamaToolCall Call, string Summary,
        DeveloperTaskDefinition? Task = null, DeveloperBuildStage? Stage = null)
    {
        public string? ExpectedDocumentRoot { get; init; }
    }

    private Task<DeveloperOperationResult<DeveloperActionObservation>> DispatchAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, OllamaToolCall call, string summary,
        CancellationToken token, string? expectedDocumentRoot = null) => AdmitAsync(project, context,
            RequestDigest(new { call = WorkspaceToolOriginalDigest.Call(call), expectedDocumentRoot }),
            _ => Task.FromResult(DeveloperOperationResult<PreparedCall>.Success(new(call, summary) { ExpectedDocumentRoot = expectedDocumentRoot })), token);

    private Task<DeveloperOperationResult<DeveloperActionObservation>> AdmitAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, string digest,
        Func<CancellationToken, Task<DeveloperOperationResult<PreparedCall>>> prepareCall, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(context);
        if (context.TaskId == Guid.Empty || context.ExecutionId == Guid.Empty || context.ContextId == Guid.Empty || context.AttemptId == Guid.Empty ||
            context.ActionId == Guid.Empty || context.PersistenceRevision < 1 || context.ParentActionId == context.ActionId)
            throw new ArgumentException("A current canonical task/run/attempt/action observation is required.", nameof(context));
        token.ThrowIfCancellationRequested();
        var key = (context.TaskId, context.ActionId);
        Invocation original; TaskCompletionSource start; Task<DeveloperOperationResult<DeveloperActionObservation>> actual;
        lock (_gate)
        {
            if (_retiring) throw new InvalidOperationException("The development owner is retiring; no new operation was admitted.");
            // Prune only after this exact driver (including its finally) actually succeeded.
            foreach (var healthy in _originals.Where(value => value.Value.Healthy && value.Value.Original.IsCompletedSuccessfully).Select(value => value.Key).ToArray()) _originals.Remove(healthy);
            _observations.RemoveAll(value => value.Original.IsCompletedSuccessfully);
            if (_originals.TryGetValue(key, out var pending))
            {
                if (pending.Project != project || pending.Context != context || pending.Digest != digest)
                    throw new InvalidOperationException("An original action identity cannot be reused for a different project, revision or call.");
                if (_observations.Count >= MaximumRetainedInvocations) throw new InvalidOperationException("Unresolved development observation custody is full.");
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var observer = new Invocation(project, context, digest) { Parent = _executing.Value };
                actual = ObserveAdmittedOriginalAsync(start.Task, pending, observer, token);
                observer.Original = actual; _observations.Add(observer); // whole original and its actual child Tasks
            }
            else
            {
                if (_originals.Count >= MaximumRetainedInvocations)
                    throw new InvalidOperationException("Unresolved development invocation custody is full; inspect the original calls before further admission.");
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                original = new(project, context, digest) { Parent = _executing.Value };
                actual = ExecuteOriginalAsync(start.Task, original, prepareCall, token);
                original.Original = actual; _originals.Add(key, original);
            }
        }
        start.TrySetResult(); // full actual driver is published before resolver/authority callbacks
        return actual;
    }

    private async Task<DeveloperOperationResult<DeveloperActionObservation>> ObserveAdmittedOriginalAsync(
        Task start, Invocation pending, Invocation observer, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var context = observer.Context;
        var previous = _executing.Value; _executing.Value = observer; Volatile.Write(ref observer.Live, true);
        try
        {
            var before = await DemandCurrentObservationAsync(context, token).ConfigureAwait(false);
            var result = await ObserveOriginalAsync(() => pending.Original).ConfigureAwait(false);
            var after = await DemandCurrentObservationAsync(context, token).ConfigureAwait(false);
            if (!ReferenceEquals(before, after)) throw new UnauthorizedAccessException("The original attempt changed before observation disclosure.");
            return result;
        }
        finally { Volatile.Write(ref observer.Live, false); _executing.Value = previous; }
    }
    private async Task<TaskRunAttemptAdmission> DemandCurrentObservationAsync(DeveloperCanonicalActionContext context, CancellationToken token)
    {
        var current = await ObserveOriginalAsync(() => tasks.GetAsync(context.TaskId, token)).ConfigureAwait(false);
        if (current is null || current.ExecutionId != context.ExecutionId || current.ContextId != context.ContextId)
            throw new UnauthorizedAccessException("The original Task/Run/context is not current for observation.");
        var attempt = await ObserveOriginalAsync(() => tasks.GetIssuedAttemptAsync(context.TaskId, context.ExecutionId, context.AttemptId, token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original issuer attempt is unavailable for observation.");
        await ObserveOriginalAsync(() => attempt.Lease.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
        return attempt;
    }

    private async Task<DeveloperOperationResult<DeveloperActionObservation>> ExecuteOriginalAsync(
        Task start, Invocation original, Func<CancellationToken, Task<DeveloperOperationResult<PreparedCall>>> prepareCall, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var previous = _executing.Value; _executing.Value = original; Volatile.Write(ref original.Live, true);
        try
        {
            var prepared = await ObserveOriginalAsync(() => prepareCall(token)).ConfigureAwait(false);
            var result = !prepared.Succeeded || prepared.Value is null
                ? new DeveloperOperationResult<DeveloperActionObservation>(prepared.RequestId, null, prepared.Error)
                : await ObserveOriginalAsync(() => ExecuteCoreAsync(original, prepared.Value.Call, prepared.Value.Summary, token,
                    prepared.Value.Task, prepared.Value.Stage, prepared.Value.ExpectedDocumentRoot)).ConfigureAwait(false);
            // The result may contain private source/process output. Revalidate this actual
            // issuer again after the owning body/ACK, before disclosing it to the caller.
            if (result.Value is not null) await DemandCurrentObservationAsync(original.Context, token).ConfigureAwait(false);
            original.Healthy = !result.Succeeded || result.Value is { RequiresOutcomeInspection: false } observation &&
                observation.OriginalToolResult?.OriginalRuntimeError is null;
            return result;
        }
        finally { Volatile.Write(ref original.Live, false); _executing.Value = previous; }
    }

    private async Task<DeveloperOperationResult<DeveloperActionObservation>> ExecuteCoreAsync(
        Invocation original, OllamaToolCall call, string summary, CancellationToken token,
        DeveloperTaskDefinition? expectedTask, DeveloperBuildStage? expectedStage, string? expectedDocumentRoot)
    {
        var project = original.Project; var context = original.Context;
        var resolved = await ResolveAsync(project, token).ConfigureAwait(false);
        if (!resolved.Succeeded || resolved.Value is null) return new(resolved.RequestId, null, resolved.Error);
        if (expectedDocumentRoot is not null && !string.Equals(Path.GetFullPath(expectedDocumentRoot),
            Path.GetFullPath(resolved.Value.Root.Location), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return Fail<DeveloperActionObservation>(DeveloperOperationErrorCode.InvalidInput, "The document root does not match this current saved project.", project.RootId);
        if (resolved.Value.Root.EnvironmentId != "local")
            return Fail<DeveloperActionObservation>(DeveloperOperationErrorCode.EnvironmentUnavailable, "No local workspace executor is registered for the selected environment.", project.RootId);
        if (!toolOwner.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, call.Name))
            return Fail<DeveloperActionObservation>(DeveloperOperationErrorCode.CapabilityUnavailable, "The configured host has no original owner for this development action.", context.ActionId);
        var current = await ObserveOriginalAsync(() => tasks.GetAsync(context.TaskId, token)).ConfigureAwait(false);
        if (current is null || current.ExecutionId != context.ExecutionId || current.ContextId != context.ContextId)
            return Fail<DeveloperActionObservation>(DeveloperOperationErrorCode.InvalidInput, "This development action does not belong to the original canonical task and conversation.", context.ActionId);
        if (!await ObserveOriginalAsync(() => workspaceBinding.IsCurrentAsync(current, resolved.Value, token, RetainOriginalSource)).ConfigureAwait(false))
            return Fail<DeveloperActionObservation>(DeveloperOperationErrorCode.PermissionDenied,
                "The project root is not the workspace selected by this task's actual conversation.", project.RootId);
        // Reading/editing source is distinct from trusting repository-controlled code to execute.
        var originalTrust = executionTrust as IDeveloperWorkspaceOriginalProjectExecutionTrustService;
        IDeveloperWorkspaceOriginalExecutionBinding? executionBinding = null;
        if (call.Name is "run_command" or "run_tests")
        {
            if (originalTrust is not null)
            {
                if (!await ObserveOriginalAsync(() => Task.FromResult(originalTrust.IsBoundToOriginalToolOwner(toolOwner))).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The exact-project trust adapter belongs to a different canonical tool owner.");
                executionBinding = await ObserveOriginalAsync(() => originalTrust.ResolveOriginalBindingAsync(resolved.Value,
                    RunOriginalSourceCallback, RetainOriginalSource, token)).ConfigureAwait(false);
            }
            else if (executionTrust is null ||
                !await ObserveOriginalAsync(() => executionTrust.IsTrustedAsync(project.WorkspaceId, token)).ConfigureAwait(false))
                return Fail<DeveloperActionObservation>(DeveloperOperationErrorCode.PermissionRequired,
                    "Workspace execution trust is required before running commands or project scripts.", project.WorkspaceId);
        }
        var attempt = await ObserveOriginalAsync(() => tasks.GetIssuedAttemptAsync(context.TaskId, context.ExecutionId, context.AttemptId, token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original live issuer-owned task attempt is unavailable.");
        // Even viewing an already accepted action uses the current original actor/attempt. Recorded
        // IDs or a receipt string cannot authorize exposing another actor's private task observation.
        await ObserveOriginalAsync(() => attempt.Lease.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
        var existing = current.Plan.FirstOrDefault(value => value.ActionId == context.ActionId);
        var intent = new TaskOriginalToolIntent(ToolRuntimeKind.Workspace.ToString(), call.Name,
            Path.GetFullPath(resolved.Value.Root.Location), WorkspaceToolOriginalDigest.Call(call));
        if (existing is not null && !(existing.State == TaskPlanNodeState.Pending && existing.OriginalToolIntent is null && existing.ParentActionId == context.ParentActionId))
        {
            if (existing.OriginalToolIntent != intent || existing.ParentActionId != context.ParentActionId)
                return Fail<DeveloperActionObservation>(DeveloperOperationErrorCode.InvalidInput, "The original action identifies different work; no operation was redispatched.", context.ActionId);
            if (existing.State == TaskPlanNodeState.Completed)
                return DeveloperOperationResult<DeveloperActionObservation>.Success(new(project, context, current, call.Name, null, AlreadyAcknowledged: true), context.ActionId);
            return Fail<DeveloperActionObservation>(DeveloperOperationErrorCode.PermissionRequired,
                "The existing action needs its original outcome inspected; it cannot be replayed from persisted status.", context.ActionId);
        }
        if (current.PersistenceRevision != context.PersistenceRevision)
            return Fail<DeveloperActionObservation>(DeveloperOperationErrorCode.RevisionConflict, "The task changed; use its current acknowledged revision.", context.ActionId);
        var preparation = await ObserveOriginalAsync(() => toolOwner.PrepareOriginalAsync(attempt, current, context.ActionId, call,
            ToolRuntimeKind.Workspace, PermissionMode.Ask, resolved.Value.Root.Location, token)).ConfigureAwait(false);
        if (!ReferenceEquals(preparation.OriginalAttempt, attempt) || preparation.ActionId != context.ActionId)
            throw new UnauthorizedAccessException("The configured tool owner did not retain the same original attempt/action.");
        // Re-read app metadata after asynchronous permission/preparation callbacks. Root/project/task edits
        // cannot silently substitute a different source tree or build definition before dispatch.
        var fresh = await ResolveAsync(project, token).ConfigureAwait(false);
        if (!fresh.Succeeded || fresh.Value is null || fresh.Value.Root != resolved.Value.Root ||
            fresh.Value.Repository != resolved.Value.Repository || expectedTask is not null &&
            (fresh.Value.Workspace.Tasks.SingleOrDefault(value => value.TaskId == expectedTask.TaskId) is not { } saved ||
             !SameDefinition(saved, expectedTask) || saved.Stages.SingleOrDefault(value => value.StageId == expectedStage!.StageId) is not { } freshStage ||
             !SameDefinition(freshStage, expectedStage)))
            throw new InvalidOperationException("The original development project/task changed during admission; no tool body was dispatched.");
        if (!await ObserveOriginalAsync(() => workspaceBinding.IsCurrentAsync(current, fresh.Value, token, RetainOriginalSource)).ConfigureAwait(false) ||
            call.Name is "run_command" or "run_tests" && originalTrust is null && (executionTrust is null ||
                !await ObserveOriginalAsync(() => executionTrust.IsTrustedAsync(project.WorkspaceId, token)).ConfigureAwait(false)))
            throw new UnauthorizedAccessException("Workspace acceptance was revoked before dispatch.");
        if (executionBinding is not null && originalTrust is not null)
            await ObserveOriginalAsync(() => originalTrust.ValidateOriginalBindingAsync(fresh.Value, executionBinding,
                RunOriginalSourceCallback, RetainOriginalSource, token)).ConfigureAwait(false);
        var latest = await ObserveOriginalAsync(() => tasks.GetAsync(context.TaskId, token)).ConfigureAwait(false);
        if (latest is null || latest.ExecutionId != context.ExecutionId || latest.ContextId != context.ContextId ||
            latest.PersistenceRevision != context.PersistenceRevision)
            throw new InvalidOperationException("The canonical task changed during development admission; no tool body was dispatched.");
        if (executionBinding is not null && originalTrust is not null)
        {
            if (preparation is not IWorkspaceToolActionPreparation workspacePreparation)
                throw new UnauthorizedAccessException("The actual original preparation is not a Workspace preparation.");
            return await ObserveOriginalAsync(() => ExecutePreparedOriginalWithConsentAsync(project, context, resolved.Value,
                fresh.Value, call, summary, preparation, workspacePreparation, current, executionBinding, originalTrust, token)).ConfigureAwait(false);
        }
        return await ObserveOriginalAsync(() => ExecutePreparedOriginalBodyAsync(project, context, resolved.Value,
            call, summary, preparation, current, token)).ConfigureAwait(false);
    }

    private async Task<DeveloperOperationResult<DeveloperActionObservation>> ExecutePreparedOriginalBodyAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, DeveloperResolvedProject resolved,
        OllamaToolCall call, string summary, ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot current, CancellationToken token)
    {
        current = await ObserveOriginalAsync(() => tasks.RegisterOriginalToolActionAsync(preparation, context.ParentActionId, summary, token)).ConfigureAwait(false);
        var owned = await ObserveOriginalAsync(() => toolOwner.ExecuteOriginalAsync(preparation,
            ct => runtime.ExecuteOriginalAsync(resolved.Root.Location, call, preparation, ct, context.ContextId), token)).ConfigureAwait(false);
        await ObserveOriginalAsync(() => toolOwner.ValidateOriginalResultAsync(preparation, owned, token).AsTask()).ConfigureAwait(false);
        // The configured canonical owner records request completion separately from business
        // success (including a known nonzero test exit), conserving the once-only receipt.
        current = await ObserveOriginalAsync(() => tasks.RecordOriginalToolActionOutcomeAsync(preparation, owned, token)).ConfigureAwait(false);
        await ObserveOriginalAsync(() => tasks.RetireAcknowledgedToolOriginalAsync(preparation, current).AsTask()).ConfigureAwait(false);
        return DeveloperOperationResult<DeveloperActionObservation>.Success(new(project, context, current, call.Name, owned.OriginalResult, AlreadyAcknowledged: false)
            { KnownNoEffect = owned.KnownNoEffect }, context.ActionId);
    }

    private async Task<DeveloperOperationResult<DeveloperActionObservation>> ExecutePreparedOriginalWithConsentAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, DeveloperResolvedProject resolved,
        DeveloperResolvedProject fresh, OllamaToolCall call, string summary, ITaskRunToolActionPreparation preparation,
        IWorkspaceToolActionPreparation workspacePreparation, TaskExecutionSnapshot current,
        IDeveloperWorkspaceOriginalExecutionBinding binding, IDeveloperWorkspaceOriginalProjectExecutionTrustService trust,
        CancellationToken token)
    {
        Task<IWorkspaceOriginalProcessStartConsent>? acquisition = null;
        Task<DeveloperOperationResult<DeveloperActionObservation>>? body = null;
        IWorkspaceOriginalProcessStartConsent? consent = null; Task? close = null;
        DeveloperOperationResult<DeveloperActionObservation>? result = null; var errors = new List<Exception>();
        try
        {
            acquisition = trust.AcquireAndBindOriginalConsentAsync(fresh, binding, workspacePreparation, current,
                RunOriginalSourceCallback, RetainOriginalSource, token);
            RetainOriginalSource(acquisition);
            consent = await acquisition.ConfigureAwait(false);
            // The real adapter has verified the private issuer and bound SAME preparation
            // before this first CAS. Entry06 rechecks the native pin at finite Start.
            body = ExecutePreparedOriginalBodyAsync(project, context, resolved, call, summary, preparation, current, token);
            RetainOriginalSource(body); result = await body.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (body is not null) CaptureOriginalCauses(errors, body, error);
            else if (acquisition is not null) CaptureOriginalCauses(errors, acquisition, error);
            else AddOriginalCause(errors, error is OperationCanceledException ? new AggregateException(error) : error);
        }
        finally
        {
            // This authentic live-issued product is cleanup-owned even if retirement
            // occurs after acquisition. Do not retire the borrowed global Home source.
            if (consent is not null)
            {
                try { close = AcquireOriginalSource(() => consent.DisposeAsync().AsTask()); RetainOriginalSource(close); }
                catch (Exception error) { AddOriginalCause(errors, error); }
                if (close is not null)
                    try { await close.ConfigureAwait(false); }
                    catch (Exception error) { CaptureOriginalCauses(errors, close, error); }
            }
        }
        if (errors.Count != 0)
        {
            if ((body ?? (Task?)acquisition)?.IsCanceled == true && (consent is null || close?.IsCompletedSuccessfully == true) &&
                errors.All(value => value is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Original development consent/body/cleanup did not settle cleanly.", errors);
        }
        return result ?? throw new InvalidOperationException("No original development body result exists.");
    }
    private void RunOriginalSourceCallback(Action callback)
        => AcquireOriginalSource(() => { callback(); return true; });

    private async Task<T> ObserveOriginalAsync<T>(Func<Task<T>> source)
    {
        Task<T> actual;
        try { actual = AcquireOriginalSource(source); RetainOriginalSource(actual); }
        catch (OperationCanceledException original) { throw new AggregateException("A synchronous original source fault is not a canceled I/O Task.", original); }
        try { return await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted) { throw actual.Exception!; }
    }
    private async Task ObserveOriginalAsync(Func<Task> source)
    {
        Task actual;
        try { actual = AcquireOriginalSource(source); RetainOriginalSource(actual); }
        catch (OperationCanceledException original) { throw new AggregateException("A synchronous original source fault is not a canceled I/O Task.", original); }
        try { await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted) { throw actual.Exception!; }
    }

    private void RetainOriginalSource(Task actual)
    {
        if (_executing.Value is not { } original) return;
        lock (_gate) if (!original.Sources.Any(value => ReferenceEquals(value, actual))) original.Sources.Add(actual);
    }

    private T AcquireOriginalSource<T>(Func<T> source)
    {
        var active = _synchronousSources ??= [];
        active.TryGetValue(this, out var depth); active[this] = depth + 1;
        try { return source(); }
        finally { if (depth == 0) active.Remove(this); else active[this] = depth; }
    }

    private static OllamaToolCall Call(string name, params (string Name, object Value)[] arguments) =>
        new(name, arguments.ToFrozenDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal));
    private static string RequestDigest<T>(T request) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
    private static bool SameDefinition<T>(T current, T expected) => CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(current)), SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(expected)));
    private static void DemandDocument(DeveloperProjectReference project, DeveloperCodeDocument document)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(document);
        if (document.Validate() is not null || document.WorkspaceId != project.WorkspaceId || document.WorkspaceRevision != project.WorkspaceRevision || document.ProjectId != project.ProjectId)
            throw new ArgumentException("The document must identify the same current saved project/workspace.", nameof(document));
        DemandRelativePath(document.RelativePath, allowRoot: false);
    }
    private static void DemandRelativePath(string path, bool allowRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (allowRoot && path == ".") return;
        if (path.Length > 4096 || Path.IsPathRooted(path) || path.IndexOf('\0') >= 0 ||
            path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(value => value is "." or ".."))
            throw new ArgumentException("A workspace-relative path without traversal is required.", nameof(path));
    }
    private static void DemandCommand(string command, int timeoutSeconds, int maximumTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (command.Length > 32_000 || command.IndexOf('\0') >= 0 || timeoutSeconds < 1 || timeoutSeconds > maximumTimeout)
            throw new ArgumentException("A bounded explicit command and supported timeout are required.", nameof(command));
    }
    private static string DemandBranch(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.Length > 256 || branch.StartsWith('-') ||
            branch.Contains("..", StringComparison.Ordinal) || branch.Any(value => char.IsControl(value) || char.IsWhiteSpace(value) || "~^:?*[\\".Contains(value)))
            throw new ArgumentException("A literal Git branch name is required.", nameof(branch));
        return branch;
    }
    private static string Quote(string value) => "'" + value.Replace("'", OperatingSystem.IsWindows() ? "''" : "'\"'\"'", StringComparison.Ordinal) + "'";
    private static DeveloperOperationResult<T> Fail<T>(DeveloperOperationErrorCode code, string message, Guid target) =>
        DeveloperOperationResult<T>.Failure(code, message, target.ToString("D"), retryable: false);
}
