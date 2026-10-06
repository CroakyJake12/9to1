using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private sealed record OriginalInitialTaskPresentation(NewChatOriginalInitialTaskObservation Owner,
        Guid ConversationId, long Generation);
    private readonly List<OriginalInitialTaskPresentation> _originalInitialTaskPresentations = [];
    private readonly List<Exception> _originalInitialTaskStopCauses = [];
    private readonly Dictionary<NewChatOriginalInitialTaskObservation, Task> _originalInitialTaskCloses =
        new(ReferenceEqualityComparer.Instance);
    private OriginalInitialTaskPresentation? _currentOriginalInitialTaskPresentation;
    private Task? _actualOriginalTaskStop;
    private bool _originalTaskStopAdmissionReserved;

    private NewChatOriginalInitialTaskObservation CaptureOriginalInitialTaskObservation(Conversation actualConversation,
        Func<Task<TaskRunOriginalInitialChatObservationLease>> actualSource,
        Func<ChatStreamEvent, Task> actualPublish)
    {
        var generation = _originalPresentationGeneration;
        var captured = new NewChatOriginalInitialTaskObservation(_sessions, RequireChatOriginal(), actualConversation.Id,
            actualSource, actualPublish,
            () => _originalWork.IsRetiring || _disposed || _conversation.Id != actualConversation.Id ||
                _originalPresentationGeneration != generation,
            _sessions.DemandExternalOriginalTaskObservationSourceJoin);
        lock (_originalSendGate)
        {
            foreach (var retired in _originalInitialTaskPresentations.Where(item => item.Owner.CanPruneHealthy).ToArray())
            {
                _originalInitialTaskPresentations.Remove(retired);
                _originalInitialTaskCloses.Remove(retired.Owner); // No second healthy-history archive.
            }
            var presentation = new OriginalInitialTaskPresentation(captured, actualConversation.Id, generation);
            _originalInitialTaskPresentations.Add(presentation);
            _currentOriginalInitialTaskPresentation = presentation; // Before any source callback is released.
        }
        return captured; // Caller assigns its exact callback capture before releasing the source gate.
    }

    private void RetireOriginalInitialTaskPresentationsForNewTarget()
    {
        OriginalInitialTaskPresentation[] stale;
        lock (_originalSendGate)
        {
            stale = _originalInitialTaskPresentations.Where(item => item.ConversationId != _conversation.Id ||
                item.Generation != _originalPresentationGeneration).ToArray();
            if (_currentOriginalInitialTaskPresentation is { } current && stale.Contains(current))
                _currentOriginalInitialTaskPresentation = null;
        }
        foreach (var original in stale) CaptureOriginalInitialTaskStop(original.Owner);
    }

    private bool TryStopOriginalCanonicalTaskResponse()
    {
        _originalWork.DemandAdmission();
        OriginalInitialTaskPresentation? current;
        lock (_originalSendGate) current = _currentOriginalInitialTaskPresentation;
        if (current is null || current.ConversationId != _conversation.Id ||
            current.Generation != _originalPresentationGeneration || current.Owner.CurrentAcknowledgedContext is null)
            return false; // Metadata/no context is not a Stop grant or a substitute factory.
        lock (_originalSendGate)
        {
            if (_originalTaskStopAdmissionReserved || _actualOriginalTaskStop is { IsCompleted: false }) return true;
            _originalTaskStopAdmissionReserved = true; // No source callback runs beneath this metadata gate.
        }
        var target = CaptureOriginalConversationTarget();
        try
        {
        _ = _originalWork.RunAsync(async original =>
        {
            BindOriginalConversationTarget(original, target);
            original.DemandPublication();
            try
            {
                current.Owner.InvokeOriginalPresentationCallback(() => SetProjectionStatus("Stopping task..."));
                original.DemandPublication();
                var actual = current.Owner.AcquireActualStop(original.Token);
                var result = await original.AwaitAsync(actual).ConfigureAwait(false);
                if (_originalWork.IsRetiring || _disposed || _conversation.Id != target.ConversationId ||
                    _originalPresentationGeneration != target.Generation) return;
                await original.AwaitAsync(InvokeOriginalChatUiAsync(() => current.Owner.InvokeOriginalPresentationCallback(() => SetProjectionStatus(
                    result.RequiresInspection ? "Task stop needs inspection of its original work." : "Task stop was recorded by its original owner."))));
            }
            catch (Exception cause)
            {
                original.Retain(cause); // Failure/unknown remains retained, never a successful stop label.
                if (!_originalWork.IsRetiring && !_disposed && _conversation.Id == target.ConversationId &&
                    _originalPresentationGeneration == target.Generation)
                    await original.AwaitAsync(InvokeOriginalChatUiAsync(() => current.Owner.InvokeOriginalPresentationCallback(
                        () => SetProjectionStatus("Haven could not stop that task: " + cause.Message))));
            }
        }, actual =>
        {
            lock (_originalSendGate) _actualOriginalTaskStop = actual;
            current.Owner.CaptureActualStopDriver(actual); // Whole original exists before initial/final callbacks.
        });
        }
        finally { lock (_originalSendGate) _originalTaskStopAdmissionReserved = false; }
        return true; // Actual command admission; the retained driver owns the real result.
    }

    private OriginalInitialTaskPresentation[] CaptureOriginalInitialTaskPresentations()
    { lock (_originalSendGate) return _originalInitialTaskPresentations.ToArray(); }

    private void DemandOriginalInitialTaskExternalJoins()
    {
        foreach (var original in CaptureOriginalInitialTaskPresentations()) original.Owner.DemandExternalClose();
    }
    private void CaptureOriginalInitialTaskStop(NewChatOriginalInitialTaskObservation actual)
    {
        try { actual.RequestRetirement(); }
        catch (Exception cause) { lock (_originalSendGate) AddOriginalGeneratedCause(_originalInitialTaskStopCauses, cause); }
        try
        {
            var close = actual.CloseAndDrainAsync();
            lock (_originalSendGate) _originalInitialTaskCloses.TryAdd(actual, close);
        }
        catch (Exception cause) { lock (_originalSendGate) AddOriginalGeneratedCause(_originalInitialTaskStopCauses, cause); }
    }
    private void RequestAllOriginalInitialTaskStops(List<Exception> failures)
    {
        foreach (var original in CaptureOriginalInitialTaskPresentations()) CaptureOriginalInitialTaskStop(original.Owner);
        lock (_originalSendGate)
            foreach (var cause in _originalInitialTaskStopCauses) AddOriginalGeneratedCause(failures, cause);
    }
    private async Task JoinAllOriginalInitialTaskStopsAsync(List<Exception> failures)
    {
        RequestAllOriginalInitialTaskStops(failures); // Includes a genuine late acquired resource.
        foreach (var original in CaptureOriginalInitialTaskPresentations())
        {
            Task? actual = null;
            try
            {
                lock (_originalSendGate)
                    actual = _originalInitialTaskCloses.TryGetValue(original.Owner, out var close) ? close
                        : throw new InvalidOperationException("An actual initial Tasks observation has no acquired source close.");
                await actual;
            }
            catch (Exception cause) { CaptureOriginalGeneratedCauses(failures, actual, cause); }
        }
    }

    private async Task FinishOriginalInitialTaskObservationAsync(NewChatOriginalInitialTaskObservation actual)
    {
        var original = RequireChatOriginal();
        try { actual.RequestRetirement(); }
        catch (Exception cause) { original.Retain(cause); }
        Task? close = null;
        try
        {
            close = actual.CloseAndDrainAsync(); // Joins observer originals, never the encompassing page body.
            lock (_originalSendGate) _originalInitialTaskCloses.TryAdd(actual, close);
        }
        catch (Exception cause) { original.Retain(cause); }
        if (close is not null)
            try { await original.AwaitAsync(close).ConfigureAwait(false); }
            catch (Exception cause) { original.Capture(close, cause); }
        // FinishOriginalSend still runs independently. Retained cleanup denies healthy
        // page-original pruning and remains in the existing sticky fault budget.
    }
}
