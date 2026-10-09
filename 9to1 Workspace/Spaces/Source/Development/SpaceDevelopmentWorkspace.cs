using System.Text.Json;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Tasks;

namespace HavenOS.Apps.Spaces.Development;

/// <summary>
/// A persisted reference in the existing Space context-reference store, not a project
/// copy or a second task store. ConversationId is the original canonical task context.
/// None of the reference, revisions, or returned view conveys execution permission.
/// </summary>
public sealed record SpaceDevelopmentLink(int SchemaVersion, Guid ConversationId, Guid TaskId,
    Guid ExecutionId, DeveloperProjectReference Project);

public sealed record SpaceDeveloperView(
    SpaceDefinition Space, Guid ContextReferenceId, SpaceDevelopmentLink Link,
    SpaceTaskObservation Task, DeveloperResolvedProject Project);

/// <summary>
/// Opens the SAME saved Dev project from the SAME existing Space task and forwards typed
/// operations to the one configured Dev owner. Root registers this with the original
/// Space registry/profile repositories and SAME Dev service used by the standalone app.
/// There is no Create/Fork/Begin/Run-new-task path here.
/// </summary>
public sealed class SpaceDevelopmentWorkspace(
    SpaceRegistry spaces, SpaceTaskWorkspaceService taskViews, DeveloperTaskWorkspaceService developer)
{
    private const string OwnerAppId = "dev";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Pure source-owned dependency guard; it neither retires nor cancels the borrowed Dev service.</summary>
    public void DemandExternalOriginalRetirementJoin() => developer.DemandExternalOriginalRetirementJoin();

    /// <summary>Attaches metadata with the existing Space revision CAS. It grants no workspace access.</summary>
    public Task<SpaceDeveloperView> AttachAsync(
        Guid spaceId, long expectedSpaceRevision, Guid originalConversationId,
        Guid expectedTaskId, Guid expectedExecutionId, DeveloperProjectReference originalProject,
        CancellationToken cancellationToken = default) => AttachAsync(spaceId, expectedSpaceRevision,
            originalConversationId, expectedTaskId, expectedExecutionId, originalProject, cancellationToken, null);

    public async Task<SpaceDeveloperView> AttachAsync(
        Guid spaceId, long expectedSpaceRevision, Guid originalConversationId,
        Guid expectedTaskId, Guid expectedExecutionId, DeveloperProjectReference originalProject,
        CancellationToken cancellationToken, Action<Action>? originalSourceCallbackScope)
    {
        ArgumentNullException.ThrowIfNull(originalProject);
        var task = await taskViews.ReadAsync(spaceId, originalConversationId, cancellationToken, originalSourceCallbackScope).ConfigureAwait(false);
        DemandTask(task, expectedTaskId, expectedExecutionId);
        var project = await ResolveProjectAsync(originalProject, cancellationToken, originalSourceCallbackScope).ConfigureAwait(false);
        var space = await Join(() => spaces.ReadExistingAsync(spaceId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original Space is unavailable.");
        if (space.IsArchived || space.Revision != expectedSpaceRevision)
            throw new SpaceRevisionConflictException(spaceId, expectedSpaceRevision, space.Revision);
        var refs = (space.ContextReferences ?? []).ToList();
        var matching = refs.Where(item => IsDevelopmentReference(item) &&
            ReadLink(item).ConversationId == originalConversationId).ToArray();
        if (matching.Length > 1) throw new InvalidDataException("This task has ambiguous Dev project references.");
        var link = new SpaceDevelopmentLink(1, originalConversationId, expectedTaskId, expectedExecutionId, project.Reference);
        var referenceId = matching.Length == 0 ? Guid.NewGuid() : matching[0].ContextId;
        // This GUID identifies only an existing Space context-reference row. It is not
        // a conversation, Task, Run, project, permission, or admission identity.
        var reference = new SpaceContextReference(referenceId, SpaceContextReferenceKind.ConnectedEntity,
            OwnerAppId, JsonSerializer.Serialize(link, Json), ProjectRevisionToken(project.Reference),
            SpaceContextPermission.Unknown, SpaceContextIndexState.NotRequired, false,
            matching.Length == 0 ? DateTimeOffset.UtcNow : matching[0].AddedAt);
        if (matching.Length == 0) refs.Add(reference);
        else refs[refs.FindIndex(item => item.ContextId == referenceId)] = reference;
        // Resolving a saved project may await storage while the task is replaced or
        // leaves this Space. Reobserve the SAME task before persisting its attachment.
        // This finite observation is metadata validation, never execution authority.
        var currentTask = await taskViews.ReadAsync(spaceId, originalConversationId,
            cancellationToken, originalSourceCallbackScope).ConfigureAwait(false);
        DemandTask(currentTask, expectedTaskId, expectedExecutionId);
        if (currentTask.SpaceRevision != space.Revision)
            throw new SpaceRevisionConflictException(spaceId, space.Revision, currentTask.SpaceRevision);
        cancellationToken.ThrowIfCancellationRequested();
        var saved = await Join(() => spaces.UpdateAsync(space with { ContextReferences = refs.ToArray() },
            expectedSpaceRevision, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        // Preserve the actual metadata ACK even when the following refresh fails.
        try { return await OpenAsync(saved.Id, referenceId, cancellationToken, originalSourceCallbackScope).ConfigureAwait(false); }
        catch (Exception error) { throw new SpaceDevelopmentAttachmentAcknowledgedException(saved, reference, error); }
    }

    /// <summary>Fresh-process reopen resolves the reference, not a project clone or stale rendered summary.</summary>
    public Task<SpaceDeveloperView> OpenAsync(
        Guid spaceId, Guid originalContextReferenceId, CancellationToken cancellationToken = default) =>
        OpenAsync(spaceId, originalContextReferenceId, cancellationToken, null);

    public async Task<SpaceDeveloperView> OpenAsync(
        Guid spaceId, Guid originalContextReferenceId, CancellationToken cancellationToken,
        Action<Action>? originalSourceCallbackScope)
    {
        if (spaceId == Guid.Empty || originalContextReferenceId == Guid.Empty)
            throw new ArgumentException("Existing Space and reference identities are required.");
        var space = await Join(() => spaces.ReadExistingAsync(spaceId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        if (space is null || space.IsArchived) throw new InvalidOperationException("The original active Space is unavailable.");
        var row = space.ContextReferences?.SingleOrDefault(item => item.ContextId == originalContextReferenceId)
            ?? throw new InvalidOperationException("The original Dev reference is unavailable.");
        var link = ReadLink(row);
        var task = await taskViews.ReadAsync(spaceId, link.ConversationId, cancellationToken, originalSourceCallbackScope).ConfigureAwait(false);
        DemandTask(task, link.TaskId, link.ExecutionId);
        var project = await ResolveProjectAsync(link.Project, cancellationToken, originalSourceCallbackScope).ConfigureAwait(false);
        var current = await Join(() => spaces.ReadExistingAsync(spaceId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        if (current is null || current.IsArchived || current.Revision != space.Revision ||
            current.ContextReferences?.SingleOrDefault(item => item.ContextId == originalContextReferenceId) != row)
            throw new InvalidOperationException("The original Space project reference changed during open; refresh before presenting it.");
        // Project resolution and the final Space read can both suspend. Publish a
        // fresh canonical observation rather than the earlier detached task snapshot.
        // Accepted work/checkpoints may advance under the SAME IDs; replacements refuse.
        var currentTask = await taskViews.ReadAsync(spaceId, link.ConversationId,
            cancellationToken, originalSourceCallbackScope).ConfigureAwait(false);
        DemandTask(currentTask, link.TaskId, link.ExecutionId);
        if (currentTask.SpaceRevision != current.Revision)
            throw new SpaceRevisionConflictException(spaceId, current.Revision, currentTask.SpaceRevision);
        cancellationToken.ThrowIfCancellationRequested();
        return new(current, row.ContextId, link, currentTask, project);
    }

    /// <summary>
    /// View retirement must not cancel this business operation. The caller supplies only its
    /// explicit business cancellation token and retains this SAME returned Task. Without an
    /// issuer-owned observation detach port, an already admitted view close may remain pending.
    /// The Dev service separately validates actual attempt issuance, accepted workspace and
    /// current permission/native fences; the UI context record is observation only.
    /// </summary>
    public Task<DeveloperOperationResult<DeveloperActionObservation>> ExecuteAsync(
        SpaceDeveloperView originalView, DeveloperCanonicalActionContext originalContext,
        SpaceDeveloperCommand command, CancellationToken originalBusinessCancellationToken = default) =>
        ExecuteAsync(originalView, originalContext, command, originalBusinessCancellationToken, null);

    public async Task<DeveloperOperationResult<DeveloperActionObservation>> ExecuteAsync(
        SpaceDeveloperView originalView, DeveloperCanonicalActionContext originalContext,
        SpaceDeveloperCommand command, CancellationToken originalBusinessCancellationToken,
        Action<Action>? originalSourceCallbackScope)
    {
        ArgumentNullException.ThrowIfNull(originalView);
        ArgumentNullException.ThrowIfNull(originalContext);
        ArgumentNullException.ThrowIfNull(command);
        var current = await OpenAsync(originalView.Space.Id, originalView.ContextReferenceId,
            originalBusinessCancellationToken, originalSourceCallbackScope).ConfigureAwait(false);
        if (current.Link != originalView.Link || current.Project.Reference != originalView.Project.Reference ||
            current.Space.Revision != originalView.Space.Revision)
            throw new InvalidOperationException("The embedded project selection changed; refresh before acting.");
        var snapshot = current.Task.Snapshot!;
        if (snapshot.TaskId != originalContext.TaskId || snapshot.ContextId != originalContext.ContextId ||
            snapshot.ExecutionId != originalContext.ExecutionId || snapshot.PersistenceRevision != originalContext.PersistenceRevision ||
            snapshot.Attempts.LastOrDefault()?.Id != originalContext.AttemptId ||
            originalContext.ActionId == Guid.Empty || originalContext.ParentActionId == Guid.Empty)
            throw new InvalidOperationException("The original canonical Task/Run/attempt observation changed.");
        var project = current.Project.Reference;
        return await Join(() => command switch
        {
            SpaceReadSource read => developer.ReadFileAsync(project, originalContext, read.Document, originalBusinessCancellationToken),
            SpacePreviewEdit preview => developer.PreviewEditAsync(project, originalContext, preview.Edit, originalBusinessCancellationToken),
            SpaceApplyEdit apply => developer.ApplyEditAsync(project, originalContext, apply.Edit, originalBusinessCancellationToken),
            SpaceRunSavedStage stage => developer.RunStageAsync(project, originalContext, stage.TaskId, stage.TaskRevision, stage.StageId, originalBusinessCancellationToken),
            SpaceRunTests test => developer.RunTestsAsync(project, originalContext, test.Command, test.TimeoutSeconds, originalBusinessCancellationToken),
            SpaceGit git => developer.GitAsync(project, originalContext, git.Operation, git.BranchName, originalBusinessCancellationToken),
            SpaceRunTerminal terminal => developer.RunTerminalAsync(project, originalContext, terminal.Command, terminal.TimeoutSeconds, originalBusinessCancellationToken),
            _ => throw new InvalidOperationException("This embedded Dev command has no configured typed operation.")
        }, originalSourceCallbackScope).ConfigureAwait(false);
    }

    private async Task<DeveloperResolvedProject> ResolveProjectAsync(DeveloperProjectReference reference, CancellationToken token, Action<Action>? originalSourceCallbackScope)
    {
        var result = await Join(() => developer.ResolveAsync(reference, token), originalSourceCallbackScope).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is null)
            throw new SpaceDevelopmentResolutionException(result);
        if (result.Value.Reference != reference || result.Value.Workspace.WorkspaceId != reference.WorkspaceId ||
            result.Value.Project.ProjectId != reference.ProjectId || result.Value.Root.RootId != reference.RootId ||
            result.Value.Workspace.Revision != reference.WorkspaceRevision || result.Value.Project.Revision != reference.ProjectRevision)
            throw new InvalidDataException("The Dev owner returned a different project identity or revision.");
        return result.Value;
    }
    private static void DemandTask(SpaceTaskObservation view, Guid taskId, Guid runId)
    {
        if (taskId == Guid.Empty || runId == Guid.Empty || view.Snapshot is not { } task ||
            task.TaskId != taskId || task.ExecutionId != runId)
            throw new InvalidOperationException("Attach requires the existing original canonical Task/Run.");
    }
    private static bool IsDevelopmentReference(SpaceContextReference row) =>
        row.Kind == SpaceContextReferenceKind.ConnectedEntity && row.OwnerAppId == OwnerAppId;
    public static SpaceDevelopmentLink ReadLink(SpaceContextReference row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!IsDevelopmentReference(row) || row.CanonicalEntityId.Length > 2048)
            throw new InvalidDataException("This is not a bounded known Dev project reference.");
        SpaceDevelopmentLink link;
        try { link = JsonSerializer.Deserialize<SpaceDevelopmentLink>(row.CanonicalEntityId, Json)
            ?? throw new InvalidDataException("The Dev project reference is empty."); }
        catch (JsonException error) { throw new InvalidDataException("The Dev project reference is malformed.", error); }
        if (link.SchemaVersion != 1 || link.ConversationId == Guid.Empty || link.TaskId == Guid.Empty ||
            link.ExecutionId == Guid.Empty || link.Project is not { } project ||
            project.WorkspaceId == Guid.Empty || project.ProjectId == Guid.Empty || project.RootId == Guid.Empty ||
            project.WorkspaceRevision < 1 || project.ProjectRevision < 1 || row.RevisionToken != ProjectRevisionToken(project))
            throw new InvalidDataException("The Dev project reference schema, identities, or revision stamp is invalid.");
        return link;
    }
    private static string ProjectRevisionToken(DeveloperProjectReference project) =>
        FormattableString.Invariant($"dev-project-v1:{project.WorkspaceRevision}:{project.ProjectRevision}");
    private static async Task<T> Join<T>(Func<Task<T>> source, Action<Action>? originalSourceCallbackScope)
    {
        // Only a synchronous, in-process caller-owned custody scope. It conveys
        // no actor, permission, persistence ACK or business-completion authority.
        Task<T>? actual = null;
        Exception? acquisitionFailure = null;
        var callbackThread = Environment.CurrentManagedThreadId;
        var scopeOpen = 1;
        var acquisitions = 0;
        void AcquireOnce()
        {
            if (Environment.CurrentManagedThreadId != callbackThread || Volatile.Read(ref scopeOpen) == 0 ||
                Interlocked.CompareExchange(ref acquisitions, 1, 0) != 0)
                throw new InvalidOperationException("The original source scope must synchronously acquire its one actual Task.");
            actual = source();
        }
        try
        {
            if (originalSourceCallbackScope is null) AcquireOnce();
            else originalSourceCallbackScope(AcquireOnce);
        }
        catch (Exception error) { acquisitionFailure = error; }
        finally { Volatile.Write(ref scopeOpen, 0); }
        if (actual is null)
        {
            var error = acquisitionFailure ?? new InvalidOperationException("No original source Task was returned.");
            // A synchronous source-thrown OCE is a fault, not a canceled Task.
            if (error is OperationCanceledException) throw new AggregateException("Original source acquisition failed.", error);
            ExceptionDispatchInfo.Capture(error).Throw();
        }
        T result = default!;
        Exception? terminalFailure = null;
        try { result = await actual!.ConfigureAwait(false); }
        catch (Exception error) { terminalFailure = error; }
        if (acquisitionFailure is null)
        {
            if (terminalFailure is null) return result;
            if (actual!.Exception is { } group) throw group;
            ExceptionDispatchInfo.Capture(terminalFailure).Throw(); // Actual canceled Task remains canceled.
        }
        // The scope may fail AFTER the callback captured a real Task. Always
        // independently join that SAME Task before exposing acquisition failure.
        var causes = new List<Exception> { acquisitionFailure! };
        if (actual!.Exception is { InnerExceptions.Count: > 0 } originalGroup)
            foreach (var cause in originalGroup.InnerExceptions)
                if (!causes.Any(value => ReferenceEquals(value, cause))) causes.Add(cause);
        else if (actual.Exception is { } opaqueGroup) causes.Add(opaqueGroup);
        else if (terminalFailure is not null && !causes.Any(value => ReferenceEquals(value, terminalFailure))) causes.Add(terminalFailure);
        throw new AggregateException("Original source acquisition and independent terminal join failed.", causes);
    }

}

public abstract record SpaceDeveloperCommand;
public sealed record SpaceReadSource(DeveloperCodeDocument Document) : SpaceDeveloperCommand;
public sealed record SpacePreviewEdit(DeveloperReviewedTextEdit Edit) : SpaceDeveloperCommand;
public sealed record SpaceApplyEdit(DeveloperReviewedTextEdit Edit) : SpaceDeveloperCommand;
public sealed record SpaceRunSavedStage(string TaskId, long TaskRevision, string StageId) : SpaceDeveloperCommand;
public sealed record SpaceRunTests(string Command, int TimeoutSeconds = 600) : SpaceDeveloperCommand;
public sealed record SpaceGit(DeveloperGitOperation Operation, string? BranchName = null) : SpaceDeveloperCommand;
public sealed record SpaceRunTerminal(string Command, int TimeoutSeconds = 120) : SpaceDeveloperCommand;

public sealed class SpaceDevelopmentResolutionException(DeveloperOperationResult<DeveloperResolvedProject> actual)
    : InvalidOperationException(actual.Error?.Message ?? "The original saved project could not be resolved.")
{ public DeveloperOperationResult<DeveloperResolvedProject> OriginalResult { get; } = actual; }

public sealed class SpaceDevelopmentAttachmentAcknowledgedException(
    SpaceDefinition actualSpace, SpaceContextReference actualReference, Exception refreshFailure)
    : InvalidOperationException("The project reference was saved, but its following presentation read failed. Refresh; do not repeat the acknowledged attachment blindly.", refreshFailure)
{
    public SpaceDefinition AcknowledgedSpace { get; } = actualSpace;
    public SpaceContextReference AcknowledgedReference { get; } = actualReference;
}
