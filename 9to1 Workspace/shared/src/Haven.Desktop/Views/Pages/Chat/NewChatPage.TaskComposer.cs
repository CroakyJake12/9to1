using Haven.Application;
using Haven.Core;
using Avalonia.Threading;
using Haven.Desktop.Views.Pages.Tasks;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private readonly HashSet<OriginalConversationTarget> _nativeTaskFollowUpAdmissions = [];
    private readonly Dictionary<OriginalConversationTarget, (long Revision, string Text, FollowUpDecision Ack)> _nativeTaskFollowUpAcks = [];
    private readonly List<Task> _nativeTaskFollowUpDrivers = [];

    internal ChatComposerCuiView OriginalTaskComposer => _scene.ComposerView;

    public FollowUpDecision? CanonicalTaskFollowUp
    {
        get { lock (_originalSendGate) return _nativeTaskFollowUpAcks.GetValueOrDefault(CaptureOriginalConversationTarget()).Ack; }
    }

    private OriginalInitialTaskPresentation? CurrentTaskComposerPresentation(bool requireLive = true)
    {
        lock (_originalSendGate)
        {
            var current = _currentOriginalInitialTaskPresentation;
            return !_disposed && !_originalWork.IsRetiring && _conversation.Mode == HavenMode.Tasks &&
                current is not null && current.ConversationId == _conversation.Id &&
                current.Generation == _originalPresentationGeneration && (!requireLive || !current.Owner.IsPresentationRetiring)
                ? current : null;
        }
    }
    private sealed record NativeTaskResumeEntry(SpaceOriginalRunResumeObservation Observer,
        TaskRunOriginalResumeObservationLease Lease);
    private readonly Dictionary<OriginalConversationTarget, NativeTaskResumeEntry> _nativeTaskLiveResumes = [];
    private NativeTaskResumeEntry? LiveNativeTaskResume()
    {
        var target = CaptureOriginalConversationTarget();
        var presentation = CurrentTaskComposerPresentation(requireLive: false);
        if (presentation is null) return null;
        NativeTaskResumeEntry? same;
        lock (_originalSendGate) _nativeTaskLiveResumes.TryGetValue(target, out same);
        return same is not null && same.Observer.CanUseComposerLease(lease => ReferenceEquals(lease, same.Lease) &&
            _sessions.CanSubmitObservedResumedTaskFollowUp(presentation.Owner.ComposerIdentity, lease)) ? same : null;
    }
    private object CaptureOriginalComposerActionIdentity()
    {
        var target = CaptureOriginalConversationTarget();
        lock (_originalSendGate) return (target, _nativeTaskLiveResumes.GetValueOrDefault(target)?.Lease);
    }
    private void RetireNativeTaskResumePresentationsForNewTarget()
    {
        var target = CaptureOriginalConversationTarget();
        SpaceOriginalRunResumeObservation[] obsolete;
        lock (_originalSendGate) obsolete = _nativeTaskLiveResumes.Where(entry => entry.Key != target)
            .Select(entry => entry.Value.Observer).Distinct().ToArray();
        foreach (var observer in obsolete) CaptureNativeTaskResumeRetirement(observer);
    }
    private void RefreshNativeResumedComposerSurface()
    {
        var resumed = LiveNativeTaskResume();
        if (resumed is not null && _canonicalPermissionWaiting?.CanonicalTaskContext.TaskId == resumed.Lease.CanonicalTaskContext.TaskId &&
            _canonicalPermissionWaiting.CanonicalTaskContext.ExecutionId == resumed.Lease.CanonicalTaskContext.ExecutionId)
        {
            _canonicalPermissionWaiting = null; // Actual newly bound continuation, never a permission grant.
            SetProjectionStatus("Original task continuation is active.");
        }
        _scene.SetSending(_isSending || resumed is not null, CanUseSelectedModel);
    }
    /// <summary>Owns only same-lease invalidations and accepted optional UI projections.
    /// It never joins, cancels or replaces the business producer. The parent Resume original
    /// closes the actual event registration and independently joins every captured dispatcher Task.</summary>
    private sealed class NativeResumeComposerUpdates(DesktopOriginalWorkLifetime.Original original, Action refresh)
    {
        private readonly object _gate = new();
        private readonly List<Task> _posts = [];
        private IDisposable? _subscription;
        private Task? _pending, _close;
        private bool _withdrawn;
        private Exception? _capacityFailure;
        internal void Attach(Func<Action, IDisposable> actualSource)
        {
            var actual = actualSource(Invalidate);
            lock (_gate) _subscription = actual;
        }
        private void Invalidate()
        {
            lock (_gate)
            {
                if (_withdrawn || !original.IsPublicationCurrent || _pending is { IsCompleted: false }) return;
                _posts.RemoveAll(static post => post.IsCompletedSuccessfully);
                if (_posts.Count >= 128)
                {
                    _capacityFailure ??= new InvalidOperationException("Retained task composer projections require view retirement.");
                    original.Retain(_capacityFailure);
                    _withdrawn = true;
                    return;
                }
                try
                {
                    _pending = Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(refresh)).GetTask();
                    _posts.Add(_pending); // Never refuse a raw projection after it was acquired.
                }
                catch (Exception cause) { original.Retain(cause); }
            }
        }
        internal Task CloseAndDrainAsync()
        {
            TaskCompletionSource start;
            Task actual;
            lock (_gate)
            {
                if (_close is not null) return _close;
                _withdrawn = true; // Also fences an event delegate captured before unsubscription.
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = _close = CloseOriginalAsync(start.Task, _subscription, _posts.ToArray());
                _subscription = null;
            }
            start.SetResult();
            return actual;
        }
        private async Task CloseOriginalAsync(Task start, IDisposable? subscription, Task[] posts)
        {
            await start.ConfigureAwait(false);
            try { subscription?.Dispose(); } catch (Exception cause) { original.Retain(cause); }
            foreach (var post in posts)
                try { await original.AwaitAsync(post).ConfigureAwait(false); } catch (Exception cause) { original.Retain(cause); }
            original.ThrowRetained();
        }
    }
    private bool CanSubmitNativeTaskFollowUp()
    {
        var resumed = LiveNativeTaskResume();
        if (_safetyLocked || !_isSending && resumed is null || string.IsNullOrWhiteSpace(_composerInput.Text) ||
            _pendingAttachmentIds.Count != 0 || _attachedImages.Count != 0 || _attachedContext.Count != 0) return false;
        var current = CurrentTaskComposerPresentation(requireLive: resumed is null);
        if (current is null || resumed is null && !current.Owner.CanSubmitFollowUp) return false;
        var target = CaptureOriginalConversationTarget();
        lock (_originalSendGate)
            return !_nativeTaskEndPassAdmissions.Contains(target) && !_nativeTaskPauseAdmissions.Contains(target) && !_nativeTaskFollowUpAdmissions.Contains(target) &&
                (!_nativeTaskFollowUpAcks.TryGetValue(target, out var accepted) || accepted.Revision != Volatile.Read(ref _originalComposerRevision));
    }
    private bool CanStopNativeComposerTask()
    {
        var current = CurrentTaskComposerPresentation(requireLive: false);
        if (current?.Owner.CurrentAcknowledgedContext is null) return false;
        var target = CaptureOriginalConversationTarget();
        var resumed = LiveNativeTaskResume() is not null;
        lock (_originalSendGate) return !_nativeTaskEndPassAdmissions.Contains(target) && !_nativeTaskPauseAdmissions.Contains(target) &&
            (!_nativeTaskResumeAdmissions.Contains(target) || resumed) && !_originalTaskStopAdmissionReserved && _actualOriginalTaskStop is not { IsCompleted: false } &&
            (_isSending || resumed || _nativeTaskControls.TryGetValue(target, out var available) && available.CanStop);
    }
    private ValueTask StopNativeComposerTask()
    {
        if (!CanStopNativeComposerTask() || !TryStopOriginalCanonicalTaskResponse())
            return ValueTask.FromException(new InvalidOperationException("This task has no current stop operation."));
        lock (_originalSendGate) return new(_actualOriginalTaskStop
            ?? throw new InvalidOperationException("The original task stop has not published its driver."));
    }
    private readonly HashSet<OriginalConversationTarget> _nativeTaskPauseAdmissions = [];
    private Task? _actualNativeTaskPause;
    private (OriginalConversationTarget Target, TaskRunOriginalRunControlResult Result)? _nativeTaskPauseResult;
    public TaskRunOriginalRunControlResult? CanonicalTaskPauseAcknowledgement =>
        _nativeTaskPauseResult is { } actual && actual.Target == CaptureOriginalConversationTarget() ? actual.Result : null;

    private bool CanPauseNativeComposerTask()
    {
        var current = CurrentTaskComposerPresentation(requireLive: false);
        if (_safetyLocked || current?.Owner.CurrentAcknowledgedContext is null) return false;
        var target = CaptureOriginalConversationTarget();
        var resumed = LiveNativeTaskResume() is not null;
        lock (_originalSendGate) return !_nativeTaskEndPassAdmissions.Contains(target) && !_nativeTaskPauseAdmissions.Contains(target) &&
            (!_nativeTaskResumeAdmissions.Contains(target) || resumed) && !_originalTaskStopAdmissionReserved &&
            _actualOriginalTaskStop is not { IsCompleted: false } &&
            (_isSending || resumed || _nativeTaskControls.TryGetValue(target, out var controls) && controls.CanPause);
    }
    private Task PauseNativeComposerTaskAsync()
    {
        DemandOriginalChatCurrent();
        if (!CanPauseNativeComposerTask()) throw new InvalidOperationException("The original task has no supported pause operation.");
        var current = CurrentTaskComposerPresentation(requireLive: false)!;
        var resumed = LiveNativeTaskResume();
        var target = CaptureOriginalConversationTarget();
        lock (_originalSendGate)
            if (!_nativeTaskPauseAdmissions.Add(target)) throw new InvalidOperationException("A pause is already pending for this task.");
        try
        {
            return _originalWork.RunAsync(async original =>
            {
                BindOriginalConversationTarget(original, target);
                try
                {
                    original.DemandPublication();
                    // Claim the same issued command while the pressed continuation is still
                    // live, before an optional dispatcher yield can retire that observer.
                    var actual = resumed is null ? current.Owner.AcquireActualComposerPause(original.Token)
                        : resumed.Observer.AcquireActualComposerCommand(lease =>
                            _sessions.PauseObservedResumedTaskComposerAsync(current.Owner.ComposerIdentity, lease, original.Token));
                    var terminal = original.ObserveTerminalAsync(actual); // Enrol the raw command before any UI callback.
                    Task? projection = null;
                    try { projection = Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() =>
                    {
                        SetProjectionStatus("Stopping task at its supported pause boundary...");
                        _scene.ComposerView.RefreshSurface();
                    })).GetTask(); }
                    catch (Exception cause) { original.Retain(cause); }
                    var settled = await terminal.ConfigureAwait(false);
                    if (settled.Succeeded && settled.Value is { } acknowledged)
                        _nativeTaskPauseResult = (target, acknowledged); // Real ACK survives UI withdrawal/failure.
                    else foreach (var cause in settled.Causes) original.Retain(cause); // No source cancellation/fault is waived.
                    if (projection is not null)
                        try { await original.AwaitAsync(projection).ConfigureAwait(false); }
                        catch (Exception cause) { original.Retain(cause); }
                    original.ThrowRetained();
                    var result = settled.Value ?? throw new InvalidDataException("The original pause returned no acknowledgement.");
                    if (!original.IsPublicationCurrent) return;
                    await RefreshNativeTaskControlsAsync(original, current).ConfigureAwait(false);
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() =>
                        SetProjectionStatus(result.Disposition == TaskRunOriginalRunControlDisposition.AlreadyCompletedByOriginal
                            ? "The original task completed before its pause was acknowledged."
                            : result.CanResumeUnstartedOriginal || result.CanResumeOriginalToolCheckpoint
                                ? "Task paused. Its original continuation is available."
                                : "The original task is suspended and requires inspection before continuation."))).GetTask()).ConfigureAwait(false);
                }
                catch (Exception cause) { original.Retain(cause); throw; }
                finally
                {
                    lock (_originalSendGate) _nativeTaskPauseAdmissions.Remove(target);
                    if (original.IsPublicationCurrent)
                        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                            original.RunAcceptedPublicationCallback(_scene.ComposerView.RefreshSurface)).GetTask()).ConfigureAwait(false);
                }
            }, actual =>
            {
                lock (_originalSendGate) _actualNativeTaskPause = actual; // Before any source/UI callback, including keyboard Stop.
            });
        }
        catch { lock (_originalSendGate) _nativeTaskPauseAdmissions.Remove(target); throw; }
    }
    private readonly HashSet<OriginalConversationTarget> _nativeTaskEndPassAdmissions = [];
    private (OriginalConversationTarget Target, TaskRunOriginalRunControlResult Result)? _nativeTaskEndPassResult;
    public TaskRunOriginalRunControlResult? CanonicalTaskEndPassAcknowledgement =>
        _nativeTaskEndPassResult is { } actual && actual.Target == CaptureOriginalConversationTarget() ? actual.Result : null;
    private bool CanEndNativeComposerPass()
    {
        if (_safetyLocked || CurrentTaskComposerPresentation(requireLive: false) is null) return false;
        var target = CaptureOriginalConversationTarget();
        lock (_originalSendGate) return !_nativeTaskEndPassAdmissions.Contains(target) &&
            !_nativeTaskPauseAdmissions.Contains(target) && !_nativeTaskResumeAdmissions.Contains(target) &&
            _actualOriginalTaskStop is not { IsCompleted: false } && !_originalTaskStopAdmissionReserved &&
            _nativeTaskControls.TryGetValue(target, out var controls) && controls.CanEndPass;
    }
    private Task EndNativeComposerPassAsync()
    {
        DemandOriginalChatCurrent();
        if (!CanEndNativeComposerPass()) throw new InvalidOperationException("The same paused pass cannot currently be ended safely.");
        var presentation = CurrentTaskComposerPresentation(requireLive: false)!;
        var target = CaptureOriginalConversationTarget();
        TaskRunOriginalRunControlAvailability controls;
        lock (_originalSendGate)
        {
            if (!_nativeTaskEndPassAdmissions.Add(target)) throw new InvalidOperationException("End Pass is already pending.");
            controls = _nativeTaskControls[target];
            _nativeTaskControls.Remove(target); // A failed/unknown terminal ACK must never restore stale Resume grants.
        }
        try
        {
            return _originalWork.RunAsync(async original =>
            {
                BindOriginalConversationTarget(original, target);
                try
                {
                    original.DemandPublication();
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() =>
                    {
                        SetProjectionStatus("Ending the paused pass; waiting for its original owner's terminal acknowledgement...");
                        _scene.ComposerView.RefreshSurface();
                    })).GetTask()).ConfigureAwait(false);
                    original.DemandPublication();
                    var actual = presentation.Owner.AcquireActualComposerEndPass(controls, original.Token);
                    var result = await original.AwaitAsync(actual).ConfigureAwait(false);
                    _nativeTaskEndPassResult = (target, result);
                    if (!original.IsPublicationCurrent) return;
                    await RefreshNativeTaskControlsAsync(original, presentation).ConfigureAwait(false);
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() =>
                        SetProjectionStatus("Pass ended. Draft and queued instructions were retained."))).GetTask()).ConfigureAwait(false);
                }
                catch (Exception cause) { original.Retain(cause); throw; }
                finally
                {
                    lock (_originalSendGate) _nativeTaskEndPassAdmissions.Remove(target);
                    if (original.IsPublicationCurrent)
                        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                            original.RunAcceptedPublicationCallback(_scene.ComposerView.RefreshSurface)).GetTask()).ConfigureAwait(false);
                }
            });
        }
        catch { lock (_originalSendGate) _nativeTaskEndPassAdmissions.Remove(target); throw; }
    }
    private Task AdmitNativeTaskFollowUpAsync(TaskFollowUpMode mode)
    {
        DemandOriginalChatCurrent();
        if (!CanSubmitNativeTaskFollowUp()) throw new InvalidOperationException("This task cannot receive the current follow-up. The draft is retained.");
        var resumed = LiveNativeTaskResume();
        var current = CurrentTaskComposerPresentation(requireLive: resumed is null) ?? throw new InvalidOperationException("The actual task observation is unavailable.");
        var target = CaptureOriginalConversationTarget();
        var text = _composerInput.Text;
        var revision = Volatile.Read(ref _originalComposerRevision);
        var acceptedDraft = CaptureOriginalAcceptedSend(_conversation, revision, text, []);
        lock (_originalSendGate)
        {
            if (!_nativeTaskFollowUpAdmissions.Add(target)) throw new InvalidOperationException("A follow-up is already pending for this task.");
            foreach (var old in _nativeTaskFollowUpAcks.Keys.Where(key => key != target && !_nativeTaskFollowUpAdmissions.Contains(key)).ToArray())
                _nativeTaskFollowUpAcks.Remove(old);
            _nativeTaskFollowUpDrivers.RemoveAll(static actual => actual.IsCompletedSuccessfully);
        }
        try
        {
            return _originalWork.RunAsync(async original =>
            {
                BindOriginalConversationTarget(original, target);
                try
                {
                    original.DemandPublication();
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                        original.RunAcceptedPublicationCallback(_scene.ComposerView.RefreshSurface)).GetTask()).ConfigureAwait(false);
                    original.DemandPublication();
                    var actual = resumed is null ? current.Owner.AcquireActualFollowUp(text, mode, original.Token)
                        : resumed.Observer.AcquireActualComposerCommand(lease =>
                            _sessions.SubmitObservedResumedTaskFollowUpAsync(current.Owner.ComposerIdentity, lease, text, mode, original.Token));
                    var reply = await original.AwaitAsync(actual).ConfigureAwait(false);
                    // Retain the real owner ACK before any publication or draft-write failure.
                    lock (_originalSendGate) _nativeTaskFollowUpAcks[target] = (revision, text, reply);
                    if (original.IsPublicationCurrent)
                        await original.AwaitAsync(RetireCommittedSendDraftAsync(acceptedDraft)).ConfigureAwait(false);
                    if (!original.IsPublicationCurrent) return;
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() =>
                    {
                        if (Volatile.Read(ref _originalComposerRevision) == revision && _composerInput.Text == text)
                            ClearOriginalComposerDraft();
                        SetProjectionStatus(reply.Mode == TaskFollowUpMode.Steer
                            ? $"Task instruction recorded: {reply.Steer?.State}."
                            : $"Task follow-up recorded: {reply.QueuedTask?.State}.");
                        _scene.ComposerView.RefreshSurface();
                    })).GetTask()).ConfigureAwait(false);
                }
                catch (Exception error) { original.Retain(error); throw; }
                finally
                {
                    lock (_originalSendGate) _nativeTaskFollowUpAdmissions.Remove(target);
                    // Settle the same command and refresh live availability after admission
                    // releases. The accepted-publication scope skips a retired presentation.
                    try
                    {
                        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                            original.RunAcceptedPublicationCallback(_scene.ComposerView.RefreshSurface)).GetTask()).ConfigureAwait(false);
                    }
                    catch (Exception error) { original.Retain(error); }
                }
                original.ThrowRetained();
            }, actual =>
            {
                // The actual whole command is published before any source/owner callback.
                lock (_originalSendGate) _nativeTaskFollowUpDrivers.Add(actual);
            });
        }
        catch { lock (_originalSendGate) _nativeTaskFollowUpAdmissions.Remove(target); throw; }
    }
    private string CaptureNativeTaskRetirementDraftText()
    {
        var target = CaptureOriginalConversationTarget();
        (long Revision, string Text, FollowUpDecision Ack) accepted;
        bool acknowledged;
        lock (_originalSendGate) acknowledged = _nativeTaskFollowUpAcks.TryGetValue(target, out accepted);
        lock (_originalDraftGate)
        {
            // A durability snapshot only: do not mutate an old TextBox or its mirror after
            // retirement. An exact canonical ACK can consume the unchanged text even if
            // its UI clear failed/withdrew; a later genuine edit always keeps its own draft.
            return acknowledged && OriginalDraftInputOwner.Id == target.ConversationId &&
                Volatile.Read(ref _originalComposerRevision) == accepted.Revision && _originalComposerText == accepted.Text &&
                _pendingAttachmentIds.Count == 0 ? "" : _originalComposerText;
        }
    }
    private Task? CaptureNativeTaskFollowUpDraftJoin()
    {
        lock (_originalSendGate)
        {
            var actual = _nativeTaskFollowUpDrivers.ToArray();
            return actual.Length == 0 ? null : Task.WhenAll(actual);
        }
    }
    private readonly Dictionary<OriginalConversationTarget, TaskRunOriginalRunControlAvailability> _nativeTaskControls = [];
    private readonly HashSet<OriginalConversationTarget> _nativeTaskResumeAdmissions = [];
    private readonly List<SpaceOriginalRunResumeObservation> _nativeTaskResumeObservers = [];
    private readonly Dictionary<SpaceOriginalRunResumeObservation, Task> _nativeTaskResumeCloses = [];
    private (OriginalConversationTarget Target, TaskRunOriginalResumeObservationResult Result)? _nativeTaskResumeResult;
    public TaskRunOriginalResumeObservationResult? CanonicalTaskResumeObservation =>
        _nativeTaskResumeResult is { } actual && actual.Target == CaptureOriginalConversationTarget() ? actual.Result : null;

    private async Task RefreshNativeTaskControlsAsync(DesktopOriginalWorkLifetime.Original original,
        OriginalInitialTaskPresentation presentation)
    {
        if (!original.IsPublicationCurrent || presentation.ConversationId != _conversation.Id ||
            presentation.Generation != _originalPresentationGeneration) return;
        var target = CaptureOriginalConversationTarget();
        var context = presentation.Owner.CurrentAcknowledgedContext;
        if (context is null) return;
        var actual = presentation.Owner.AcquireActualComposerControls(original.Token);
        var controls = await original.AwaitAsync(actual).ConfigureAwait(false);
        if (controls is not null && (controls.CanonicalTaskContext.TaskId != context.TaskId ||
            controls.CanonicalTaskContext.ExecutionId != context.ExecutionId || controls.CanonicalTaskContext.ContextId != target.ConversationId))
            throw new InvalidDataException("The task controls belong to a different original Task/Run.");
        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() =>
        {
            lock (_originalSendGate)
            {
                if (controls is not null && _nativeTaskControls.TryGetValue(target, out var newer) &&
                    newer.CanonicalTaskContext.PersistenceRevision > controls.CanonicalTaskContext.PersistenceRevision) return;
                _nativeTaskControls.Remove(target);
                if (controls is not null) _nativeTaskControls[target] = controls;
            }
            RefreshNativeResumedComposerSurface();
        })).GetTask()).ConfigureAwait(false);
    }
    private readonly HashSet<OriginalConversationTarget> _nativeTaskControlRefreshAdmissions = [];
    private bool CanRefreshNativeComposerTaskControls()
    {
        var current = CurrentTaskComposerPresentation(requireLive: false);
        if (current?.Owner.CurrentAcknowledgedContext is null) return false;
        lock (_originalSendGate) return !_nativeTaskControlRefreshAdmissions.Contains(CaptureOriginalConversationTarget());
    }
    private Task RefreshNativeComposerTaskControlsAsync()
    {
        DemandOriginalChatCurrent();
        var current = CurrentTaskComposerPresentation(requireLive: false)
            ?? throw new InvalidOperationException("No original task identity is available for refresh.");
        var target = CaptureOriginalConversationTarget();
        lock (_originalSendGate)
            if (!_nativeTaskControlRefreshAdmissions.Add(target)) throw new InvalidOperationException("The original controls read is already pending.");
        try { return _originalWork.RunAsync(async original =>
        {
            BindOriginalConversationTarget(original, target);
            try
            {
                original.DemandPublication();
                await RefreshNativeTaskControlsAsync(original, current).ConfigureAwait(false);
            }
            catch (Exception cause)
            {
                original.Retain(cause);
                lock (_originalSendGate) _nativeTaskControls.Remove(target);
                throw;
            }
            finally
            {
                lock (_originalSendGate) _nativeTaskControlRefreshAdmissions.Remove(target);
                if (original.IsPublicationCurrent)
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                        original.RunAcceptedPublicationCallback(_scene.ComposerView.RefreshSurface)).GetTask()).ConfigureAwait(false);
            }
        }); }
        catch { lock (_originalSendGate) _nativeTaskControlRefreshAdmissions.Remove(target); throw; }
    }
    private bool CanResumeNativeComposerTask()
    {
        if (_safetyLocked || CurrentTaskComposerPresentation(requireLive: false) is null) return false;
        var target = CaptureOriginalConversationTarget();
        lock (_originalSendGate) return !_nativeTaskEndPassAdmissions.Contains(target) && !_nativeTaskPauseAdmissions.Contains(target) && !_nativeTaskResumeAdmissions.Contains(target) &&
            _nativeTaskControls.TryGetValue(target, out var controls) &&
            (controls.CanResumeUnstartedOriginal || controls.CanResumeOriginalToolCheckpoint || controls.CanResumeColdOriginal);
    }
    private Task ResumeNativeComposerTaskAsync()
    {
        DemandOriginalChatCurrent();
        if (!CanResumeNativeComposerTask()) throw new InvalidOperationException("No original task continuation is available.");
        var presentation = CurrentTaskComposerPresentation(requireLive: false)!;
        var target = CaptureOriginalConversationTarget();
        TaskRunOriginalRunControlAvailability controls;
        lock (_originalSendGate)
        {
            if (!_nativeTaskResumeAdmissions.Add(target)) throw new InvalidOperationException("This original task resume is already pending.");
            controls = _nativeTaskControls[target];
        }
        try { return _originalWork.RunAsync(async original =>
        {
            BindOriginalConversationTarget(original, target);
            SpaceOriginalRunResumeObservation? observer = null;
            NativeResumeComposerUpdates? updates = null;
            try
            {
                original.DemandPublication();
                var context = controls.CanonicalTaskContext;
                lock (_originalSendGate)
                {
                    foreach (var healthy in _nativeTaskResumeObservers.Where(actual => actual.CanPruneHealthy).ToArray())
                    { _nativeTaskResumeObservers.Remove(healthy); _nativeTaskResumeCloses.Remove(healthy); }
                    if (_nativeTaskResumeObservers.Count >= 128)
                        throw new InvalidOperationException("Retained task continuation observations require recovery before another resume.");
                    observer = new(presentation.Owner.IsIssuedComposerResume, presentation.Owner.DemandComposerSourceJoin,
                        original, context.TaskId, context.ExecutionId, context.ContextId,
                        () => presentation.Owner.AcquireActualComposerResume(controls, original.Token),
                        () => _originalWork.IsRetiring || _disposed || _conversation.Id != target.ConversationId ||
                            _originalPresentationGeneration != target.Generation);
                    _nativeTaskResumeObservers.Add(observer); // Before releasing this same observer's source gate.
                }
                observer.BeginOriginalAcquisition();
                var acquired = await original.AwaitAsync(observer.ActualAcquisition).ConfigureAwait(false);
                // This actual NEW lease, not the retired initial observer, owns live continued commands.
                // The helper has already retained the lease before any optional projection can fail.
                if (acquired is not null && original.IsPublicationCurrent)
                {
                    lock (_originalSendGate)
                    {
                        _nativeTaskLiveResumes[target] = new(observer, acquired);
                    }
                    updates = new(original, RefreshNativeResumedComposerSurface);
                    updates.Attach(changed => _sessions.WatchObservedResumedTaskComposer(presentation.Owner.ComposerIdentity, acquired, changed));
                    await RefreshNativeTaskControlsAsync(original, presentation).ConfigureAwait(false);
                }
                var result = await original.AwaitAsync(observer.ActualObservation).ConfigureAwait(false);
                if (result is not null) _nativeTaskResumeResult = (target, result); // Actual observation survives withdrawal.
                if (!original.IsPublicationCurrent || result is null) return;
                IReadOnlyList<ChatMessage>? messages = null;
                if (result.Disposition == TaskRunOriginalResumeObservationDisposition.ProducerTerminal)
                {
                    messages = await original.AwaitAsync(_conversations.GetMessagesAsync(target.ConversationId, original.Token)).ConfigureAwait(false);
                    if (messages.Any(message => message.ConversationId != target.ConversationId) || messages.Select(message => message.Id).Distinct().Count() != messages.Count)
                        throw new InvalidDataException("The resumed task returned foreign or duplicate conversation records.");
                }
                await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() =>
                {
                    if (messages is not null) { _messages.Clear(); _messages.AddRange(messages.OrderBy(message => message.CreatedAt)); RefreshMessages(); }
                    if (result.Disposition == TaskRunOriginalResumeObservationDisposition.ProducerTerminal &&
                        result.CanonicalTaskContext is { } acknowledged && _canonicalPermissionWaiting?.CanonicalTaskContext.TaskId == acknowledged.TaskId &&
                        _canonicalPermissionWaiting.CanonicalTaskContext.ExecutionId == acknowledged.ExecutionId) _canonicalPermissionWaiting = null;
                    SetProjectionStatus(result.Disposition == TaskRunOriginalResumeObservationDisposition.ProducerTerminal
                        ? $"Task continuation finished: {result.State}." : "This task view detached; its owner retains the continuation.");
                })).GetTask()).ConfigureAwait(false);
                await RefreshNativeTaskControlsAsync(original, presentation).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                original.Retain(error);
                lock (_originalSendGate) _nativeTaskControls.Remove(target);
                throw;
            }
            finally
            {
                lock (_originalSendGate)
                {
                    _nativeTaskResumeAdmissions.Remove(target);
                    if (_nativeTaskLiveResumes.TryGetValue(target, out var same) && ReferenceEquals(same.Observer, observer))
                        _nativeTaskLiveResumes.Remove(target);
                }
                Task? updatesClose = updates?.CloseAndDrainAsync();
                if (observer is not null)
                {
                    CaptureNativeTaskResumeRetirement(observer);
                    Task? close; lock (_originalSendGate) _nativeTaskResumeCloses.TryGetValue(observer, out close);
                    if (close is not null)
                        try { await original.AwaitAsync(close).ConfigureAwait(false); } catch (Exception error) { original.Retain(error); }
                    else original.Retain(new InvalidOperationException("The actual native task resume observer has no retained close."));
                }
                if (updatesClose is not null)
                    try { await original.AwaitAsync(updatesClose).ConfigureAwait(false); } catch (Exception error) { original.Retain(error); }
                if (original.IsPublicationCurrent)
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                        original.RunAcceptedPublicationCallback(RefreshNativeResumedComposerSurface)).GetTask()).ConfigureAwait(false);
            }
            original.ThrowRetained();
        }); }
        catch
        {
            lock (_originalSendGate) _nativeTaskResumeAdmissions.Remove(target);
            throw;
        }
    }
    private void CaptureNativeTaskResumeRetirement(SpaceOriginalRunResumeObservation observer)
    {
        try { observer.RequestRetirement(); }
        catch (Exception error) { lock (_originalSendGate) AddOriginalGeneratedCause(_originalInitialTaskStopCauses, error); }
        try
        {
            var actual = observer.CloseAndDrainAsync();
            lock (_originalSendGate) _nativeTaskResumeCloses.TryAdd(observer, actual);
        }
        catch (Exception error) { lock (_originalSendGate) AddOriginalGeneratedCause(_originalInitialTaskStopCauses, error); }
    }
    private SpaceOriginalRunResumeObservation[] CaptureNativeTaskResumeObservers()
    { lock (_originalSendGate) return _nativeTaskResumeObservers.ToArray(); }
    private async Task JoinNativeTaskResumeRetirementAsync(List<Exception> failures)
    {
        foreach (var observer in CaptureNativeTaskResumeObservers())
        {
            CaptureNativeTaskResumeRetirement(observer);
            Task? actual = null;
            try
            {
                lock (_originalSendGate) _nativeTaskResumeCloses.TryGetValue(observer, out actual);
                if (actual is null) throw new InvalidOperationException("No task resume observer close was retained.");
                await actual.ConfigureAwait(false);
            }
            catch (Exception error) { CaptureOriginalGeneratedCauses(failures, actual, error); }
        }
    }
    private TaskFollowUpMode? NativePreferredSubmitMode => _conversation.SpaceId is { } space && space != Guid.Empty
        ? _preferences.GetSpaceSubmitMode(space, space == SpaceRegistry.ResearchSpaceId) switch
        { SpaceSubmitMode.Steer => TaskFollowUpMode.Steer, SpaceSubmitMode.Queue => TaskFollowUpMode.Queue, _ => null }
        : null;
    private ValueTask SetNativePreferredSubmitMode(TaskFollowUpMode mode)
    {
        if (!Enum.IsDefined(mode) || _conversation.SpaceId is not { } space || space == Guid.Empty)
            return ValueTask.FromException(new InvalidOperationException("This task has no canonical Space preference scope."));
        return new(RunChatOriginalAsync(() =>
        {
            _preferences.SetSpaceSubmitMode(space, mode == TaskFollowUpMode.Steer ? SpaceSubmitMode.Steer : SpaceSubmitMode.Queue);
            _scene.ComposerView.RefreshSurface();
            return Task.CompletedTask;
        }));
    }
    private sealed class NativeTaskComposerActions(NewChatPage page) : IChatComposerTaskActions
    {
        public bool IsApplicable => page._conversation.Mode == HavenMode.Tasks && (page._isSending || page.HasStarted);
        public bool IsRunning => page._isSending || page.LiveNativeTaskResume() is not null;
        public bool CanRefreshControls => page.CanRefreshNativeComposerTaskControls();
        public ValueTask RefreshControls() => new(page.RefreshNativeComposerTaskControlsAsync());
        public bool CanSteer => page.CanSubmitNativeTaskFollowUp();
        public bool CanQueue => page.CanSubmitNativeTaskFollowUp();
        public bool CanStop => page.CanStopNativeComposerTask();
        public bool CanPause => !IsPassPaused && page.CanPauseNativeComposerTask();
        public bool IsPassPaused
        {
            get
            {
                var target = page.CaptureOriginalConversationTarget();
                lock (page._originalSendGate) return !page._nativeTaskResumeAdmissions.Contains(target) &&
                    page.CanonicalTaskPauseAcknowledgement is { Disposition: TaskRunOriginalRunControlDisposition.Suspended } ack &&
                    page._nativeTaskControls.TryGetValue(target, out var controls) &&
                    controls.CanonicalTaskContext.PersistenceRevision == ack.CanonicalTaskContext.PersistenceRevision;
            }
        }
        public ValueTask Pause() => new(page.PauseNativeComposerTaskAsync());
        public bool HasSendModePreference => page._conversation.SpaceId is { } space && space != Guid.Empty &&
            !page._safetyLocked && (page.CurrentTaskComposerPresentation()?.Owner.CanSubmitFollowUp == true || page.LiveNativeTaskResume() is not null);
        public TaskFollowUpMode? PreferredSendMode => page.NativePreferredSubmitMode;
        public ValueTask SetPreferredSendMode(TaskFollowUpMode mode) => page.SetNativePreferredSubmitMode(mode);
        public bool CanEndPass => page.CanEndNativeComposerPass();
        public ValueTask EndPass() => new(page.EndNativeComposerPassAsync());
        public bool CanResume => page.CanResumeNativeComposerTask();
        public ValueTask Resume() => new(page.ResumeNativeComposerTaskAsync());
        public ValueTask Steer() => new(page.AdmitNativeTaskFollowUpAsync(TaskFollowUpMode.Steer));
        public ValueTask Queue() => new(page.AdmitNativeTaskFollowUpAsync(TaskFollowUpMode.Queue));
        public ValueTask Stop() => page.StopNativeComposerTask();
    }
}
