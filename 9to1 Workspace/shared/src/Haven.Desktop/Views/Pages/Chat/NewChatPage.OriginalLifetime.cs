using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage : Haven.Desktop.Services.IDesktopOriginalRetirementParticipant, Haven.Desktop.Services.IDesktopOriginalRetirementJoinGuard
{
    private readonly NewChatOriginalWorkLifetime _originalWork;
    private readonly object _originalSendGate = new();
    private Task? _originalSend;
    private long _originalPresentationGeneration;
    private sealed record OriginalConversationTarget(Guid ConversationId, long Generation);
    private readonly ConditionalWeakTable<Task, OriginalConversationTarget> _originalContextTransitions = new();

    private OriginalConversationTarget CaptureOriginalConversationTarget() =>
        new(_conversation.Id, _originalPresentationGeneration);

    private void BindOriginalConversationTarget(NewChatOriginalWorkLifetime.Original original,
        OriginalConversationTarget target, bool explicitTransition = false)
    {
        original.BindPublicationGuard(() => !_disposed && _conversation.Id == target.ConversationId &&
            _originalPresentationGeneration == target.Generation);
        if (explicitTransition)
        {
            // Only actual owning Load/fresh/branch operations create this private reply.
            // An awaiter may adopt this exact captured target after the SAME task succeeds;
            // arbitrary reentry or a fresh read of mutable page fields is not a transition.
            _originalContextTransitions.Remove(original.Task);
            _originalContextTransitions.Add(original.Task, target);
        }
    }

    private void RecordOriginalConversationTransition()
    {
        RetireOriginalInitialTaskPresentationsForNewTarget();
        if (_originalWork.Executing is not { } original) return;
        if (!original.AllowsSourceContextTransition)
            throw new InvalidOperationException("An original stream cannot acquire a different conversation presentation.");
        BindOriginalConversationTarget(original, CaptureOriginalConversationTarget(), explicitTransition: true);
        original.DemandPublication();
    }

    private Task AdmitOriginalConversationLoad(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        OriginalConversationTarget? target = null;
        return _originalWork.RunAsync(original =>
        {
            BindOriginalConversationTarget(original, target ?? throw new InvalidOperationException("No original Load target was admitted."));
            original.DemandPublication();
            return LoadConversationOriginalBodyAsync(conversation);
        }, actual =>
        {
            // Plain owner state is assigned during the SAME original admission, before
            // the start gate; an earlier queued Load cannot later replace a newer target.
            target = new(conversation.Id, ++_originalPresentationGeneration);
            _conversation = conversation;
            _originalContextTransitions.Add(actual, target);
        });
    }

    private void AcceptOriginalAwaitedTransition(NewChatOriginalWorkLifetime.Original original, Task actual)
    {
        if (original.AllowsSourceContextTransition && _originalContextTransitions.TryGetValue(actual, out var target))
            BindOriginalConversationTarget(original, target, explicitTransition: true);
        original.DemandPublication();
    }

    /// <summary>Permanent admission seal. An admitted action requests this and returns;
    /// its tab/window owner joins CloseAndDrainAsync later with UI and borrowed services alive.</summary>
    public void RequestRetirement()
    {
        _originalWork.RequestRetirement();
        _disposed = true;
    }

    /// <summary>The SAME coalesced original task. It joins acquired generated mounts
    /// as well as page originals; external shell/native/constructor ownership is separate.</summary>
    public void DemandExternalOriginalRetirementJoin()
    {
        _originalWork.DemandExternalClose();
        DemandOriginalInitialTaskExternalJoins();
        foreach (var mount in _originalGeneratedMounts.ToArray()) mount.DemandOriginalExternalClose();
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        var original = _originalWork.CloseAndDrainAsync();
        _disposed = true;
        return original;
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    /// <summary>Owns the original composer submission callback, published before native
    /// setters. A queued instruction with no model is NOT provider completion or a Run.
    /// Any stream subsequently admitted is separately retained in this page's same cohort.</summary>
    public Task SubmitOriginalInstructionAsync(string instruction) => RunChatOriginalAsync(() =>
    {
        var pending = instruction?.Trim();
        if (string.IsNullOrWhiteSpace(pending)) return Task.CompletedTask;
        DemandOriginalChatPublication();
        _scene.Instruction.Text = pending;
        DemandOriginalChatPublication();
        _pendingInstruction = pending;
        _pendingInstructionPreservesDraft = false;
        TrySubmitPendingInstruction();
        DemandOriginalChatPublication();
        return Task.CompletedTask;
    });

    private NewChatOriginalWorkLifetime.Original RequireChatOriginal() =>
        _originalWork.Executing ?? throw new InvalidOperationException("This operation has no admitted original page task.");

    private Task RunChatOriginalAsync(Func<Task> body)
    {
        var target = CaptureOriginalConversationTarget();
        return _originalWork.RunAsync(original =>
        {
            BindOriginalConversationTarget(original, target);
            original.DemandPublication();
            return body();
        });
    }

    private Task<T> RunChatOriginalAsync<T>(Func<Task<T>> body)
    {
        var target = CaptureOriginalConversationTarget();
        return _originalWork.RunAsync(original =>
        {
            BindOriginalConversationTarget(original, target);
            original.DemandPublication();
            return body();
        });
    }

    private Task RunChatNestedOriginalAsync(Func<Task> body)
    {
        // Nested work owns its captured target too. Its caller separately joins the
        // actual reply; only a private, successful explicit transition can update that caller.
        var target = CaptureOriginalConversationTarget();
        return _originalWork.RunAsync(original =>
        {
            BindOriginalConversationTarget(original, target);
            original.DemandPublication();
            return body();
        });
    }

    private Task<T> RunChatNestedOriginalAsync<T>(Func<Task<T>> body)
    {
        // Nested work owns its captured target too. Its caller separately joins the
        // actual reply; only a private, successful explicit transition can update that caller.
        var target = CaptureOriginalConversationTarget();
        return _originalWork.RunAsync(original =>
        {
            BindOriginalConversationTarget(original, target);
            original.DemandPublication();
            return body();
        });
    }

    private async Task AwaitChatOriginalAsync(Task actual)
    {
        var original = RequireChatOriginal();
        await original.AwaitAsync(actual).ConfigureAwait(false);
        AcceptOriginalAwaitedTransition(original, actual);
    }

    private async Task<T> AwaitChatOriginalAsync<T>(Task<T> actual)
    {
        var original = RequireChatOriginal();
        var value = await original.AwaitAsync(actual).ConfigureAwait(false);
        AcceptOriginalAwaitedTransition(original, actual);
        return value;
    }

    private void DemandOriginalChatPublication() => RequireChatOriginal().DemandPublication();

    private void DemandOriginalChatCurrent()
    {
        _originalWork.DemandAdmission();
        _originalWork.Executing?.DemandPublication();
    }

    private void RetainHandledOriginalChatCause(Exception actualCause) =>
        _originalWork.Executing?.Retain(actualCause);

    private void RunOriginalChatNotification(Action actualNotification)
    {
        if (_originalWork.IsRetiring) return;
        _ = _originalWork.RunAsync(original =>
        {
            original.BindPublicationGuard(() => !_disposed);
            DemandOriginalChatPublication();
            actualNotification();
            DemandOriginalChatPublication();
            return Task.CompletedTask;
        });
    }

    private void RunOriginalChatTargetNotification(Action actualNotification)
    {
        var target = CaptureOriginalConversationTarget();
        _originalWork.RunSynchronous(original =>
        {
            BindOriginalConversationTarget(original, target);
            original.DemandPublication();
            actualNotification();
            original.DemandPublication();
        });
    }

    private void OnOriginalPointerPressedOutside() => RunOriginalChatNotification(_scene.HideAddMenu);

    private Task InvokeOriginalChatUiAsync(Action actualPublication, DispatcherPriority priority = default)
    {
        var original = RequireChatOriginal();
        original.DemandPublication();
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            original.DemandPublication();
            actualPublication();
            original.DemandPublication();
        }, priority).GetTask();
    }

    private Task AdmitOriginalSendAsync()
    {
        lock (_originalSendGate)
        {
            _originalWork.DemandAdmission();
            if (_originalSend is { IsCompleted: false }) return _originalSend;
            var conversation = _conversation;
            var generation = _originalPresentationGeneration;
            var instruction = _scene.Instruction.Text.Trim();
            var returned = _originalWork.RunAsync(
                original => SubmitCurrentInstructionOriginalBodyAsync(conversation, generation, instruction),
                actual => _originalSend = actual);
            // This observation owns its actual task as well. The earlier send must unwind
            // before a pending NEW instruction can be admitted; no encompassing self-join.
            if (!_originalWork.IsRetiring)
            {
                try { _ = RunChatOriginalAsync(() => ObserveOriginalSendTerminalAsync(returned)); }
                catch (ObjectDisposedException) when (_originalWork.IsRetiring)
                { /* No observer was admitted or acquired. Preserve the SAME already owned send. */ }
                catch (Exception error) when (_originalWork.IsOriginalAdmissionRefusal(error))
                { /* The actual capacity refusal is retained by this owner's admission ledger. */ }
            }
            return returned;
        }
    }

    private async Task ObserveOriginalSendTerminalAsync(Task actualSend)
    {
        var observation = RequireChatOriginal();
        try { await observation.AwaitAsync(actualSend).ConfigureAwait(false); }
        catch (Exception error) { observation.Retain(error); }
        if (_originalWork.IsRetiring) return;
        await observation.AwaitAsync(InvokeOriginalChatUiAsync(TrySubmitPendingInstruction)).ConfigureAwait(false);
    }

    private async Task FinishOriginalSendAsync(CancellationTokenSource? actualCancellation)
    {
        var original = RequireChatOriginal();
        var failures = new List<Exception>();
        try { _sendProgressTimer.Stop(); } catch (Exception error) { failures.Add(error); }
        if (ReferenceEquals(_sendCancellation, actualCancellation)) _sendCancellation = null;
        try { actualCancellation?.Dispose(); } catch (Exception error) { failures.Add(error); }
        _isSending = false;
        if (!_originalWork.IsRetiring)
        {
            Task? safety = null;
            try { safety = RefreshSafetyStateAsync(); }
            catch (Exception error) { failures.Add(error); }
            if (safety is not null)
                try { await original.AwaitAsync(safety); } catch (Exception error) { original.Retain(error); }
            Task? publication = null;
            try { publication = InvokeOriginalChatUiAsync(RefreshVisualState); }
            catch (Exception error) { failures.Add(error); }
            if (publication is not null)
                try { await original.AwaitAsync(publication); } catch (Exception error) { original.Retain(error); }
        }
        foreach (var failure in failures) original.Retain(failure);
        // RunCore publishes all retained actual faults after the existing body's policy.
    }

    private Task StopOriginalPageAsync()
    {
        // This real dispatcher task is captured by the close core; stop precedes original joins.
        return Dispatcher.UIThread.InvokeAsync(() => _originalWork.RunCloseCallback(() =>
        {
            var failures = new List<Exception>();
            void Attempt(Action action)
            { try { action(); } catch (Exception error) { failures.Add(error); } }
            Attempt(() => _sendCancellation?.Cancel());
            Attempt(_sendProgressTimer.Stop);
            Attempt(() => _scene.StopRequested -= OnStopRequested);
            Attempt(() => _scene.Instruction.Invalidated -= OnComposerEditCompleted);
            Attempt(() => _scene.AttachmentRemoveRequested -= OnAttachmentRemoveRequested);
            Attempt(() => _scene.MessageActionRequested -= OnMessageActionRequested);
            Attempt(() => _scene.MarkdownCodeActionRequested -= OnMarkdownCodeActionRequested);
            Attempt(() => _scene.DualToggleRequested -= OnDualToggleRequested);
            Attempt(() => _scene.DualModelPickerRequested -= OnDualModelPickerRequested);
            Attempt(() => _scene.DualSecondModelChosen -= OnDualSecondModelChosen);
            Attempt(() => Scene.InputSubmitted -= OnInputSubmitted);
            Attempt(() => Scene.PointerPressedOutside -= OnOriginalPointerPressedOutside);
            RequestAllOriginalInitialTaskStops(failures); // Observation stop only, never Task business stop.
            foreach (var mount in _originalGeneratedMounts.ToArray())
                Attempt(() => CaptureOriginalGeneratedStop(mount)); // Start all actual child stops before page originals join.
            foreach (var cause in _originalGeneratedStopCauses) AddOriginalGeneratedCause(failures, cause);
            ThrowIndependentCleanup(failures);
        })).GetTask();
    }

    private async Task CleanupOriginalPageAsync()
    {
        // Page originals are terminal. Capture any constructor-acquired late mount
        // too, then independently join every SAME child task even if a sibling fails.
        var failures = new List<Exception>();
        await JoinAllOriginalInitialTaskStopsAsync(failures); // SAME late lease/driver, independent of mount faults.
        foreach (var mount in _originalGeneratedMounts.ToArray()) CaptureOriginalGeneratedStop(mount);
        foreach (var cause in _originalGeneratedStopCauses) AddOriginalGeneratedCause(failures, cause);
        foreach (var mount in _originalGeneratedMounts.ToArray())
        {
            Task? actual = null;
            try
            {
                actual = _originalGeneratedCloses.TryGetValue(mount, out var close) ? close
                    : throw new InvalidOperationException("An acquired generated mount has no retained original close.");
                await actual;
            }
            catch (Exception cause) { CaptureOriginalGeneratedCauses(failures, actual, cause); }
        }
        var actualDispatcher = Dispatcher.UIThread.InvokeAsync(() => _originalWork.RunCloseCallback(() =>
        {
            try { _sendCancellation?.Dispose(); } catch (Exception cause) { AddOriginalGeneratedCause(failures, cause); }
            _sendCancellation = null;
            // Failed child close retains physical scene/resolver/store ownership.
            // A reversible earlier visual detach was never treated as a close ACK.
            if (failures.Count == 0)
            {
                foreach (var instanceId in _originalGeneratedInstanceIds.ToArray())
                    try { _genUiInstances.Remove(instanceId); _originalGeneratedInstanceIds.Remove(instanceId); }
                    catch (Exception cause) { AddOriginalGeneratedCause(failures, cause); }
                _generatedSurfaces.Clear(); _generatedInstanceIds.Clear(); _generatedSignatures.Clear();
                _originalGeneratedMounts.Clear(); _originalGeneratedCloses.Clear();
                if (failures.Count == 0)
                    try { _scene.Dispose(); } catch (Exception cause) { AddOriginalGeneratedCause(failures, cause); }
            }
        })).GetTask();
        try { await actualDispatcher; }
        catch (Exception cause) { CaptureOriginalGeneratedCauses(failures, actualDispatcher, cause); }
        ThrowIndependentCleanup(failures);
    }

    private static void ThrowIndependentCleanup(List<Exception> failures)
    {
        if (failures.Count == 0) return;
        if (failures.Count > 1 || failures[0] is OperationCanceledException)
            throw new AggregateException("Independent original Chat cleanup failed.", failures);
        ExceptionDispatchInfo.Capture(failures[0]).Throw();
    }
}
