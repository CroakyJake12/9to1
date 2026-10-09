using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;

namespace Haven.Desktop.Views.Pages.Tasks;

/// <summary>
/// Native view of the existing Space task. The shell passes actual Space/conversation
/// identities and borrows the SAME coordinator/Dev service. View close stops presentation
/// admission and joins its actual originals; it never calls business Cancel or starts work.
/// An acquired Dev command keeps its explicit business token. A missing observation-transfer
/// port may leave close pending on that original, rather than inventing detachment.
/// Constructor-before-return failures and root shell acquisition remain separate prerequisites.
/// </summary>
public sealed partial class SpaceTaskWidgetPage : UserControl, IActivatablePage,
    ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard, IAsyncDisposable
{
    private readonly SpaceTaskWorkspaceService _source;
    private readonly TaskExecutionCoordinator _canonical;
    private readonly SpaceDevelopmentWorkspace? _development;
    private readonly Func<SpaceDeveloperView, Task>? _openDev;
    private readonly Guid _spaceId, _conversationId;
    private readonly Guid? _projectReferenceId;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CuiSceneHost _host;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly object _gate = new();
    [ThreadStatic] private static List<SpaceTaskWidgetPage>? _sourceCallbacks;
    private bool _active, _subscribed;
    private long _generation;
    private Guid? _originalTaskId, _originalRunId;
    private SpaceTaskObservation? _observation;
    private SpaceDeveloperView? _developerView;
    private string _instruction = "", _status = "";
    private Task? _hostClose, _nativeReadinessClose;
    private NativeCanonicalTaskSceneReadiness? _nativeReadinessOwner;
    private DesktopOriginalWorkLifetime.Original? _nativeReadinessAcquisition;

    public SpaceTaskWidgetPage(SpaceTaskWorkspaceService source, TaskExecutionCoordinator canonical,
        Guid actualSpaceId, Guid actualConversationId, ICuiSceneReadiness readiness,
        SpaceDevelopmentWorkspace? development = null, Guid? originalProjectReferenceId = null,
        Func<SpaceDeveloperView, Task>? openSameProjectInDev = null,
        Guid? expectedOriginalTaskId = null, Guid? expectedOriginalExecutionId = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        if (actualSpaceId == Guid.Empty || actualConversationId == Guid.Empty || originalProjectReferenceId == Guid.Empty)
            throw new ArgumentException("Existing Space/conversation/reference identities are required.");
        if (expectedOriginalTaskId.HasValue != expectedOriginalExecutionId.HasValue ||
            expectedOriginalTaskId == Guid.Empty || expectedOriginalExecutionId == Guid.Empty)
            throw new ArgumentException("Supply the actual original Task/Run pair together, or neither for an unstarted context.");
        _originalTaskId = expectedOriginalTaskId; _originalRunId = expectedOriginalExecutionId;
        _spaceId = actualSpaceId; _conversationId = actualConversationId;
        _development = development; _projectReferenceId = originalProjectReferenceId; _openDev = openSameProjectInDev;
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        Document = SpaceTaskWidgetCuiDocument.Load();
        _host = new CuiSceneHost();
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        AutomationProperties.SetAutomationId(this, "SpaceCanonicalTaskWidget");
        AutomationProperties.SetName(this, "Space task and development workspace");
        Content = _host;
    }

    internal SpaceTaskWorkspaceService OriginalTaskSource => _source;
    internal (Guid SpaceId, Guid ContextId, Guid? TaskId, Guid? RunId) OriginalReadinessContext
    { get { lock (_gate) return (_spaceId, _conversationId, _originalTaskId, _originalRunId); } }
    internal long OriginalReadinessGeneration { get { lock (_gate) return _generation; } }
    internal bool IsOriginalReadinessPresentationCurrent(CuiSceneHost actualHost, long generation)
    { lock (_gate) return ReferenceEquals(actualHost, _host) && _active && !_work.IsRetiring && _generation == generation; }
    internal Task<NativeCanonicalTaskSceneReadiness> AcquireOriginalNativeReadinessAsync(
        Func<SpaceTaskWidgetPage, NativeCanonicalTaskSceneReadiness> actualFactory) =>
        _work.RunAsync(async original =>
        {
            ArgumentNullException.ThrowIfNull(actualFactory);
            original.BindPublicationGuard(() => !_work.IsRetiring);
            original.DemandPublication();
            lock (_gate)
            {
                if (_nativeReadinessAcquisition is not null)
                    throw new InvalidOperationException("The original native Task readiness acquisition is already reserved.");
                // This SAME factor original was published before its body opened. Reserve
                // it before any factory callback; a failed/late source is not replaceable.
                _nativeReadinessAcquisition = original;
            }
            NativeCanonicalTaskSceneReadiness? acquired = null;
            InvokeOriginalSource(() =>
            {
                acquired = actualFactory(this) ?? throw new InvalidOperationException("No actual native readiness owner was returned.");
                lock (_gate) _nativeReadinessOwner = acquired;
                return true;
            });
            var actual = acquired!; // Returned product captured before leaving its source scope.
            // Capture BEFORE validation/retirement publication. The factory is called only
            // inside this prepublished page original and its actual physical source scope.
            if (!actual.IsBoundToOriginalPage(this))
                throw new InvalidOperationException("The native readiness source belongs to a different original page.");
            if (_work.IsRetiring)
            {
                actual.RequestRetirement();
                Task close; lock (_gate) close = _nativeReadinessClose ??= actual.CloseAndDrainAsync();
                await original.AwaitAsync(close).ConfigureAwait(false);
            }
            original.DemandPublication();
            return actual;
        });

    internal NativeCanonicalTaskSceneReadiness GetOriginalDevelopmentReadinessIssuer()
    {
        lock (_gate)
        {
            _work.DemandAdmission();
            return _nativeReadinessOwner ?? throw new InvalidOperationException("No SAME source-owned Task native readiness binder is attached.");
        }
    }

    public CuiDocument Document { get; }
    internal CuiSceneHost OriginalHost => _host; // Root's genuine native-frame producer binds this exact host.
    public new event PropertyChangedEventHandler? PropertyChanged;
    public FollowUpDecision? AcknowledgedFollowUp { get; private set; }
    public DeveloperOperationResult<DeveloperActionObservation>? OriginalDeveloperReply { get; private set; }
    private bool IsCurrent(long generation)
    { lock (_gate) return !_work.IsRetiring && _active && _generation == generation; }
    private void DemandCurrent(long generation)
    { if (!IsCurrent(generation)) throw new InvalidOperationException("This original task presentation was withdrawn."); }

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        long generation;
        lock (_gate) { _work.DemandAdmission(); _active = true; generation = ++_generation; }
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            original.DemandPublication();
            lock (_gate)
            {
                original.DemandPublication();
                if (!_subscribed)
                    InvokeOriginalSource(() => { _canonical.SnapshotChanged += OnSnapshotChanged; _subscribed = true; return true; });
            }
            await RefreshOriginalAsync(original, generation, scope.Token).ConfigureAwait(false);
            Task? showJoin = null;
            var dispatcher = Dispatcher.UIThread.InvokeAsync(() =>
            {
                original.DemandPublication();
                var show = InvokeOriginalSource(() => _host.ShowAsync(new("spaces.task-development", "Task and Dev", "Tasks",
                    Document, this, this, _readiness) { IsPublicationCurrent = () => IsCurrent(generation) }, scope.Token));
                // Capture even if a subsequent publication callback retires the page.
                showJoin = original.AwaitAsync(show);
            }).GetTask();
            try { await original.AwaitAsync(dispatcher).ConfigureAwait(false); }
            catch (Exception error) { original.Capture(dispatcher, error); }
            if (showJoin is not null)
                try { await original.AwaitAsync(showJoin).ConfigureAwait(false); }
                catch (Exception error) { original.Capture(showJoin, error); }
            else original.Retain(new InvalidOperationException("No original task-widget Show was acquired."));
            original.ThrowRetained();
        });
    }
    public void Deactivate() { lock (_gate) { _active = false; _generation++; } }

    private async Task RefreshOriginalAsync(DesktopOriginalWorkLifetime.Original original, long generation, CancellationToken token)
    {
        original.DemandPublication();
        var actual = InvokeOriginalSource(() => _source.ReadAsync(_spaceId, _conversationId, token, OwnNestedOriginalSource));
        var observation = await original.AwaitAsync(actual).ConfigureAwait(false);
        SpaceDeveloperView? project = null;
        if (_development is not null && _projectReferenceId is { } id)
        {
            original.DemandPublication();
            project = await original.AwaitAsync(InvokeOriginalSource(() => _development.OpenAsync(_spaceId, id, token, OwnNestedOriginalSource))).ConfigureAwait(false);
            if (project.Task.Snapshot?.TaskId != observation.Snapshot?.TaskId ||
                project.Task.Snapshot?.ExecutionId != observation.Snapshot?.ExecutionId ||
                project.Task.Snapshot?.ContextId != observation.Snapshot?.ContextId ||
                project.Task.Conversation.Id != observation.Conversation.Id)
                throw new InvalidDataException("The Dev project belongs to a different original Task/Run.");
            // Dev reopening resolves the project and then reads the SAME task again.
            // Present that fresh canonical revision, including acknowledged actions/checkpoint.
            observation = project.Task;
        }
        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
        {
            original.DemandPublication();
            if (observation.Snapshot is { } snapshot)
            {
                if (_originalTaskId is { } task && (task != snapshot.TaskId || _originalRunId != snapshot.ExecutionId))
                    throw new InvalidOperationException("A different Task/Run now occupies this context; reopen its explicit task view.");
                if (_observation?.Snapshot is { } previous && previous.PersistenceRevision > snapshot.PersistenceRevision) return;
                _originalTaskId = snapshot.TaskId; _originalRunId = snapshot.ExecutionId;
            }
            _observation = observation; _developerView = project;
            NotifyOriginal(original, generation);
        }).GetTask()).ConfigureAwait(false);
        await RefreshOriginalRunControlsAsync(original, generation, observation, token).ConfigureAwait(false);
    }

    private void OnSnapshotChanged(object? sender, TaskExecutionSnapshot snapshot)
    {
        if (snapshot.ContextId != _conversationId) return;
        long generation;
        bool originalPairReplaced;
        lock (_gate)
        {
            if (!_active || _work.IsRetiring) return;
            originalPairReplaced = _originalTaskId is { } task &&
                (snapshot.TaskId != task || snapshot.ExecutionId != _originalRunId);
            if (originalPairReplaced) { _active = false; _generation++; }
            generation = _generation;
        }
        if (originalPairReplaced)
        {
            // This SAME coordinator's successful ACK withdraws the old presentation
            // before any queued refresh or held native readiness can publish it.
            // Request-only retirement does not cancel the durable Task/Run or Dev owner.
            _work.RequestRetirement();
            return;
        }
        // SAME real publication/read driver retained before the canonical callback returns.
        _ = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            await RefreshOriginalAsync(original, generation, original.Token).ConfigureAwait(false);
        });
    }

    public bool TryGetValue(string path, out object? value)
    {
        var view = _observation; var task = view?.Snapshot;
        value = path switch
        {
            "Title" => view?.Conversation.Title ?? "Space task",
            "Identities" => task is null ? "No canonical task is recorded." : $"Task {task.TaskId:D}\nRun {task.ExecutionId:D}\nContext {task.ContextId:D}\nSaved revision {task.PersistenceRevision}",
            "Objective" => task?.PromptSummary ?? "Open an existing task conversation; this widget does not start a new task.",
            "Observation" => view?.ExecutionObservation ?? "The original task observation has not been loaded.",
            "Plan" => task is null || task.Plan.Count == 0 ? "No plan action is recorded." : string.Join("\n", task.Plan.Select(action =>
                $"{action.Summary} — {action.State}; action {action.ActionId:D}" +
                (action.Acceptance is null ? " (no mutation acceptance)" : $"; accepted by original attempt {action.Acceptance.AttemptId:D} at {action.Acceptance.ObservedAt:g}") +
                (action.RemediationId is { } request ? $"; recorded remediation {request:D} (not a grant)" : "") +
                (action.RequiredPermissionScopes is { Count: > 0 } scopes ? "; requested scopes: " + string.Join(", ", scopes) : ""))),
            "Checkpoint" => view?.Projection?.CheckpointId is { } checkpoint ? $"Acknowledged checkpoint {checkpoint:D}" : "No acknowledged checkpoint is recorded.",
            "Attempts" => task is null || task.Attempts.Count == 0 ? "No provider attempt is recorded." : string.Join("\n", task.Attempts.Select(attempt =>
                $"{attempt.Id:D}: {attempt.Candidate.ProviderId}/{attempt.Candidate.ModelId}; {attempt.State}; retry of {attempt.RetryOfAttemptId}")),
            "Recovery" => view?.RecoveryObservation ?? "No recovery observation has been loaded.",
            "Continuation" => _runControlAvailability is null
                ? "The genuine original run controls are unavailable. Saved state or approval cannot authorize replay."
                : $"Original-owner observation: pause {_runControlAvailability.CanPause}; stop {_runControlAvailability.CanStop}; same-input unstarted resume {_runControlAvailability.CanResumeUnstartedOriginal}. Every command revalidates fresh authority; accepted/invoked checkpoint continuation is unavailable.",
            "CanPauseOriginal" => IsActionAvailable("task.pause") == true,
            "CanStopOriginal" => IsActionAvailable("task.stop") == true,
            "CanResumeOriginal" => IsActionAvailable("task.resume-original") == true,
            "Instruction" => _instruction,
            "CanSubmit" => IsActionAvailable("task.steer") == true,
            "FollowUps" => task is null ? "" : string.Join("\n", task.Steers.Select(steer => $"Steer: {steer.Summary} — {steer.State}")
                .Concat(task.Queue.Select(queued => $"Queued: {queued.Summary} — {queued.State}"))),
            "Project" => _developerView is { } project ? $"{project.Project.Project.Name}; project {project.Project.Reference.ProjectId:D}, revision {project.Project.Reference.ProjectRevision}\nWorkspace {project.Project.Reference.WorkspaceId:D}, revision {project.Project.Reference.WorkspaceRevision}\nRoot {project.Project.Reference.RootId:D}; environment {project.Project.Root.EnvironmentId}\nRepository {project.Project.Repository?.CanonicalRepositoryId ?? "not configured"}" : "No resolved Dev project is attached to this original task.",
            "CanOpenDev" => IsActionAvailable("dev.open") == true,
            "Status" => _status,
            _ => null
        };
        return path is "Title" or "Identities" or "Objective" or "Observation" or "Plan" or "Checkpoint" or "Attempts" or
            "Recovery" or "Continuation" or "CanPauseOriginal" or "CanStopOriginal" or "CanResumeOriginal" or "Instruction" or "CanSubmit" or "FollowUps" or "Project" or "CanOpenDev" or "Status";
    }
    public bool TrySetValue(string path, object? value)
    {
        if (path != "Instruction" || value is not string instruction) return false;
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            _instruction = instruction; NotifyOriginal(original, generation);
        });
        return true;
    }
    public bool? IsActionAvailable(string command)
    {
        lock (_gate)
        {
            if (!_active || _work.IsRetiring) return false;
            return command switch
            {
                "task.refresh" => true,
                "task.pause" or "task.stop" or "task.resume-original" => IsOriginalRunControlAvailable(command),
                "task.steer" or "task.queue" => _source.HasConfiguredCommandOwner && _observation?.Snapshot?.OwnerBinding is not null,
                "dev.open" => _openDev is not null && _developerView is not null,
                _ => false
            };
        }
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This task operation is unavailable.");
        long generation; lock (_gate) generation = _generation;
        var capturedInstruction = _instruction; var captured = _observation; var project = _developerView;
        return new(_work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            if (command == "task.refresh") await RefreshOriginalAsync(original, generation, scope.Token).ConfigureAwait(false);
            else if (command is "task.steer" or "task.queue")
            {
                var snapshot = captured?.Snapshot ?? throw new InvalidOperationException("No original canonical task was captured.");
                var reply = await original.AwaitAsync(InvokeOriginalSource(() => _source.SubmitFollowUpAsync(_spaceId, _conversationId,
                    snapshot.TaskId, snapshot.ExecutionId, snapshot.PersistenceRevision, capturedInstruction,
                    command == "task.steer" ? TaskFollowUpMode.Steer : TaskFollowUpMode.Queue, scope.Token, OwnNestedOriginalSource))).ConfigureAwait(false);
                AcknowledgedFollowUp = reply; // Actual CAS reply survives subsequent UI failure/withdrawal.
                await RefreshOriginalAsync(original, generation, scope.Token).ConfigureAwait(false);
            }
            else if (command is "task.pause" or "task.stop" or "task.resume-original")
            {
                var same = captured?.Snapshot ?? throw new InvalidOperationException("No original Task/Run was captured.");
                await DispatchOriginalRunControlAsync(original, generation, command, same, scope.Token).ConfigureAwait(false);
            }
            else if (command == "dev.open")
            {
                var current = await original.AwaitAsync(InvokeOriginalSource(() => _development!.OpenAsync(_spaceId,
                    project!.ContextReferenceId, scope.Token, OwnNestedOriginalSource))).ConfigureAwait(false);
                original.DemandPublication();
                await original.AwaitAsync(InvokeOriginalSource(() => _openDev!(current))).ConfigureAwait(false);
            }
        }));
    }

    public Task<DeveloperOperationResult<DeveloperActionObservation>> ExecuteOriginalDeveloperCommandAsync(
        DeveloperCanonicalActionContext originalContext, SpaceDeveloperCommand originalCommand,
        CancellationToken explicitBusinessCancellationToken = default)
    {
        var view = _developerView ?? throw new InvalidOperationException("No original Dev project is attached.");
        if (_development is null) throw new InvalidOperationException("The genuine Dev owner is unavailable.");
        long generation; lock (_gate) generation = _generation;
        return _work.RunAsync(async original =>
        {
            // Admission is checked before acquisition; after acquisition, view retirement
            // cannot replace the explicit business token or discard the actual producer.
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            var actual = InvokeOriginalSource(() => _development.ExecuteAsync(view, originalContext,
                originalCommand, explicitBusinessCancellationToken, OwnNestedOriginalSource));
            var reply = await original.AwaitAsync(actual).ConfigureAwait(false);
            OriginalDeveloperReply = reply;
            return reply;
        });
    }

    private void NotifyOriginal(DesktopOriginalWorkLifetime.Original original, long generation)
    {
        foreach (var callback in PropertyChanged?.GetInvocationList() ?? [])
        {
            original.DemandPublication(); DemandCurrent(generation);
            try { InvokeOriginalSource(() => { ((PropertyChangedEventHandler)callback)(this, new(null)); return true; }); }
            catch (Exception error) { original.Retain(error); }
            original.DemandPublication(); DemandCurrent(generation);
        }
        original.ThrowRetained();
    }
    private void OwnNestedOriginalSource(Action callback) =>
        InvokeOriginalSource(() => { callback(); return true; });
    private T InvokeOriginalSource<T>(Func<T> callback)
    {
        var stack = _sourceCallbacks ??= []; stack.Add(this);
        try { return callback(); }
        finally { stack.RemoveAt(stack.Count - 1); }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_sourceCallbacks?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual task-widget source callback cannot join its encompassing close.");
        _work.DemandExternalClose();
        DemandExternalOriginalResumeObservationJoin();
        NativeCanonicalTaskSceneReadiness? readiness; lock (_gate) readiness = _nativeReadinessOwner;
        readiness?.DemandExternalOriginalRetirementJoin();
        _development?.DemandExternalOriginalRetirementJoin(); // Pure borrowed-source dependency guard only.
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopOriginalPresentationAsync()
    {
        NativeCanonicalTaskSceneReadiness? readiness;
        lock (_gate) { _active = false; _generation++; readiness = _nativeReadinessOwner; }
        if (_subscribed) { _canonical.SnapshotChanged -= OnSnapshotChanged; _subscribed = false; }
        var failures = new List<Exception>();
        RequestOriginalResumeObservationRetirement(failures); // Request every observer, never the business producer.
        Task? nativeClose = null, hostClose = null;
        if (readiness is not null)
            try { lock (_gate) nativeClose = _nativeReadinessClose ??= readiness.RequestOriginalRetirementTask(); }
            catch (Exception error) { failures.Add(error); }
        try { hostClose = _hostClose ??= _host.CloseOriginalAsync(); } catch (Exception error) { failures.Add(error); }
        return JoinOriginalPresentationChildrenAsync([nativeClose, hostClose], failures);
    }
    private static async Task JoinOriginalPresentationChildrenAsync(Task?[] actualTasks, List<Exception> failures)
    {
        foreach (var actual in actualTasks.OfType<Task>().Distinct<Task>(ReferenceEqualityComparer.Instance))
        {
            try { await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                { foreach (var cause in group.InnerExceptions) if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause); }
                else if (!failures.Any(value => ReferenceEquals(value, actual.Exception ?? error))) failures.Add(actual.Exception ?? error);
            }
        }
        if (failures.Count > 0) throw new AggregateException("Original Task readiness and host retirement failed.", failures);
    }
    private async Task DetachOriginalPresentationAsync()
    {
        NativeCanonicalTaskSceneReadiness? readiness; Task? actualReadinessClose;
        lock (_gate) { readiness = _nativeReadinessOwner; actualReadinessClose = _nativeReadinessClose; }
        // A child may be captured after Stop while its factory original was pending.
        // Join that same child before any resource detach; no borrowed Home service close.
        var childFailures = new List<Exception>();
        Task? observerClose = null;
        try { observerClose = JoinOriginalResumeObservationRetirementAsync(); } catch (Exception error) { childFailures.Add(error); }
        if (readiness is not null && actualReadinessClose is null)
            try { lock (_gate) actualReadinessClose = _nativeReadinessClose ??= readiness.RequestOriginalRetirementTask(); }
            catch (Exception error) { childFailures.Add(error); }
        await JoinOriginalPresentationChildrenAsync([actualReadinessClose, observerClose], childFailures).ConfigureAwait(false);
        if (_hostClose is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual task-widget host close did not acknowledge retirement.");
        var actual = InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            OwnNestedOriginalSource(() => { if (ReferenceEquals(Content, _host)) Content = null; })).GetTask());
        await JoinOriginalDispatcherCloseAsync(actual).ConfigureAwait(false);
    }
    internal static async Task JoinOriginalDispatcherCloseAsync(Task actual)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception error)
        {
            if (actual.Exception is { } group) throw group;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
