using Haven.Application;
using Haven.Core;
using Avalonia.Threading;

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

    private OriginalInitialTaskPresentation? CurrentTaskComposerPresentation()
    {
        lock (_originalSendGate)
        {
            var current = _currentOriginalInitialTaskPresentation;
            return !_disposed && !_originalWork.IsRetiring && _conversation.Mode == HavenMode.Tasks &&
                current is not null && current.ConversationId == _conversation.Id &&
                current.Generation == _originalPresentationGeneration && !current.Owner.IsPresentationRetiring
                ? current : null;
        }
    }
    private bool CanSubmitNativeTaskFollowUp()
    {
        if (_safetyLocked || !_isSending || string.IsNullOrWhiteSpace(_composerInput.Text) ||
            _pendingAttachmentIds.Count != 0 || _attachedImages.Count != 0 || _attachedContext.Count != 0) return false;
        var current = CurrentTaskComposerPresentation();
        if (current?.Owner.CanSubmitFollowUp != true) return false;
        var target = CaptureOriginalConversationTarget();
        lock (_originalSendGate)
            return !_nativeTaskFollowUpAdmissions.Contains(target) &&
                (!_nativeTaskFollowUpAcks.TryGetValue(target, out var accepted) || accepted.Revision != Volatile.Read(ref _originalComposerRevision));
    }
    private bool CanStopNativeComposerTask()
    {
        var current = CurrentTaskComposerPresentation();
        if (!_isSending || current?.Owner.CurrentAcknowledgedContext is null) return false;
        lock (_originalSendGate) return !_originalTaskStopAdmissionReserved && _actualOriginalTaskStop is not { IsCompleted: false };
    }
    private ValueTask StopNativeComposerTask()
    {
        if (!CanStopNativeComposerTask() || !TryStopOriginalCanonicalTaskResponse())
            return ValueTask.FromException(new InvalidOperationException("This task has no current stop operation."));
        lock (_originalSendGate) return new(_actualOriginalTaskStop
            ?? throw new InvalidOperationException("The original task stop has not published its driver."));
    }
    private Task AdmitNativeTaskFollowUpAsync(TaskFollowUpMode mode)
    {
        DemandOriginalChatCurrent();
        if (!CanSubmitNativeTaskFollowUp()) throw new InvalidOperationException("This task cannot receive the current follow-up. The draft is retained.");
        var current = CurrentTaskComposerPresentation() ?? throw new InvalidOperationException("The actual task observation is unavailable.");
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
                    var actual = current.Owner.AcquireActualFollowUp(text, mode, original.Token);
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
    private sealed class NativeTaskComposerActions(NewChatPage page) : IChatComposerTaskActions
    {
        public bool IsApplicable => page._conversation.Mode == HavenMode.Tasks && (page._isSending || page.HasStarted);
        public bool CanSteer => page.CanSubmitNativeTaskFollowUp();
        public bool CanQueue => page.CanSubmitNativeTaskFollowUp();
        public bool CanStop => page.CanStopNativeComposerTask();
        public ValueTask Steer() => new(page.AdmitNativeTaskFollowUpAsync(TaskFollowUpMode.Steer));
        public ValueTask Queue() => new(page.AdmitNativeTaskFollowUpAsync(TaskFollowUpMode.Queue));
        public ValueTask Stop() => page.StopNativeComposerTask();
    }
}
