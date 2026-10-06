using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Spaces.Tasks;

namespace Haven.Desktop.Views.Pages.Tasks;

public sealed partial class SpaceTaskWidgetPage
{
    private TaskRunOriginalRunControlAvailability? _runControlAvailability;
    private DesktopOriginalWorkLifetime.Original? _runControlCommand;
    private readonly List<SpaceOriginalRunResumeObservation> _resumeObservations = [];
    private Exception? _resumeObservationCapacityRefusal;
    public TaskRunOriginalRunControlResult? AcknowledgedOriginalRunControl { get; private set; }
    public TaskRunOriginalResumeObservationResult? OriginalRunResumeObservation { get; private set; }

    private bool IsOriginalRunControlAvailable(string command)
    {
        var snapshot = _observation?.Snapshot;
        var available = _runControlAvailability;
        if (!_active || _work.IsRetiring || snapshot is null || available is null ||
            _runControlCommand is { HasLiveWork: true } ||
            available.CanonicalTaskContext.TaskId != snapshot.TaskId ||
            available.CanonicalTaskContext.ExecutionId != snapshot.ExecutionId ||
            available.CanonicalTaskContext.ContextId != _conversationId) return false;
        return command switch
        {
            "task.pause" => available.CanPause,
            "task.stop" => available.CanStop,
            "task.resume-original" => available.CanResumeUnstartedOriginal || available.CanResumeOriginalToolCheckpoint,
            _ => false
        }; // Detached availability is never a command grant; the owner freshly validates every call.
    }

    private async Task RefreshOriginalRunControlsAsync(DesktopOriginalWorkLifetime.Original original,
        long generation, SpaceTaskObservation observation, CancellationToken token)
    {
        TaskRunOriginalRunControlAvailability? availability = null;
        if (_source.HasConfiguredCommandOwner && observation.Snapshot?.OwnerBinding is not null)
        {
            var snapshot = observation.Snapshot;
            original.DemandPublication();
            availability = await original.AwaitAsync(InvokeOriginalSource(() =>
                _canonical.GetOriginalRunControlAvailabilityAsync(snapshot.TaskId, snapshot.ExecutionId, token))).ConfigureAwait(false);
            DemandSameRunControlContext(availability.CanonicalTaskContext, snapshot);
        }
        var actual = InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            OwnNestedOriginalSource(() =>
            {
                original.DemandPublication(); DemandCurrent(generation);
                _runControlAvailability = availability;
                NotifyOriginal(original, generation);
            })).GetTask());
        await original.AwaitAsync(actual).ConfigureAwait(false);
    }

    private Task DispatchOriginalRunControlAsync(DesktopOriginalWorkLifetime.Original original,
        long generation, string command, TaskExecutionSnapshot snapshot, CancellationToken commandToken)
        => RunOriginalControlBodyAsync(original, generation, command, snapshot, commandToken);

    private async Task RunOriginalControlBodyAsync(DesktopOriginalWorkLifetime.Original original,
        long generation, string command, TaskExecutionSnapshot captured, CancellationToken commandToken)
    {
        lock (_gate)
        {
            if (_runControlCommand is { HasLiveWork: true })
                throw new InvalidOperationException("A SAME original run control is already pending.");
            _runControlCommand = original; // Complete factor Task already published before any owner callback.
        }
        original.DemandPublication();
        var current = await original.AwaitAsync(InvokeOriginalSource(() =>
            _source.ReadAsync(_spaceId, _conversationId, commandToken, OwnNestedOriginalSource))).ConfigureAwait(false);
        if (current.Snapshot is not { } same || same.TaskId != captured.TaskId || same.ExecutionId != captured.ExecutionId)
            throw new InvalidOperationException("The original Task/Run changed before its control was admitted.");
        original.DemandPublication();
        if (command == "task.resume-original")
        {
            SpaceOriginalRunResumeObservation observer;
            lock (_gate)
            {
                _resumeObservations.RemoveAll(static actual => actual.CanPruneHealthy);
                if (_resumeObservationCapacityRefusal is { } refusal) throw refusal;
                if (_resumeObservations.Count >= 128)
                {
                    _resumeObservationCapacityRefusal = new InvalidOperationException("Retained original resume observations require external retirement.");
                    throw _resumeObservationCapacityRefusal;
                }
                observer = new(_canonical, original, same.TaskId, same.ExecutionId, same.ContextId,
                    () => InvokeOriginalSource(() => _canonical.StartObservedOriginalRunResumeAsync(
                        same.TaskId, same.ExecutionId, commandToken)), () => _work.IsRetiring);
                _resumeObservations.Add(observer); // Retain before releasing its callback gate.
            }
            observer.BeginOriginalAcquisition();
            var observation = await original.AwaitAsync(observer.ActualObservation).ConfigureAwait(false);
            OriginalRunResumeObservation = observation; // Actual observation retained even after view withdrawal.
            if (observation?.Disposition == TaskRunOriginalResumeObservationDisposition.ProducerTerminal)
                DemandSameRunControlContext(observation.CanonicalTaskContext!, same);
            // No business iterator, producer token, Cancel, new Task, or replay factory exists in this view.
        }
        else
        {
            var actual = InvokeOriginalSource(() => command == "task.pause"
                ? _canonical.PauseOriginalRunAsync(same.TaskId, same.ExecutionId, commandToken)
                : _canonical.StopOriginalRunAsync(same.TaskId, same.ExecutionId, commandToken));
            var reply = await original.AwaitAsync(actual).ConfigureAwait(false);
            AcknowledgedOriginalRunControl = reply; // Real owner ACK survives a later UI callback failure.
            DemandSameRunControlContext(reply.CanonicalTaskContext, same);
            if (!Enum.IsDefined(reply.Kind) || !Enum.IsDefined(reply.Disposition) || !Enum.IsDefined(reply.State))
                throw new InvalidDataException("The real original control returned invalid observation metadata.");
        }
        // The owning effect/wait returned; full publication/cleanup Tasks stay in the
        // factor ledger. A subsequent command still revalidates the real source.
        lock (_gate) if (ReferenceEquals(_runControlCommand, original)) _runControlCommand = null;
        if (original.IsPublicationCurrent && IsCurrent(generation))
        {
            var actual = InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
                OwnNestedOriginalSource(() =>
                {
                    if (!original.IsPublicationCurrent || !IsCurrent(generation)) return;
                    _status = command == "task.resume-original"
                        ? OriginalRunResumeObservation is null || OriginalRunResumeObservation.Disposition == TaskRunOriginalResumeObservationDisposition.ObservationDetached
                            ? "This observation detached. The durable producer remains owned by its service."
                            : $"The real resumed producer returned its terminal observation: {OriginalRunResumeObservation.State}."
                        : AcknowledgedOriginalRunControl is { } result
                            ? $"Original {result.Kind}: {result.Disposition}; {result.State}." : "No actual original control acknowledgment was returned.";
                    NotifyOriginal(original, generation);
                })).GetTask());
            await original.AwaitAsync(actual).ConfigureAwait(false);
            await RefreshOriginalAsync(original, generation, commandToken).ConfigureAwait(false);
        }
    }

    private void DemandSameRunControlContext(ProviderExecutionContext context, TaskExecutionSnapshot same)
    {
        if (context.TaskId != same.TaskId || context.ExecutionId != same.ExecutionId ||
            context.ContextId != _conversationId || context.PersistenceRevision < same.PersistenceRevision)
            throw new InvalidDataException("The real control observation belongs to a different or older Task/Run/context.");
    }

    private void DemandExternalOriginalResumeObservationJoin()
    {
        _canonical.DemandExternalOriginalProcessJoin(); // Pure actual source/ancestor guard; no global seal/stop.
        SpaceOriginalRunResumeObservation[] originals;
        lock (_gate) originals = _resumeObservations.ToArray();
        foreach (var actual in originals) actual.DemandExternalClose();
    }
    private void RequestOriginalResumeObservationRetirement(List<Exception> failures)
    {
        SpaceOriginalRunResumeObservation[] originals;
        lock (_gate) originals = _resumeObservations.ToArray();
        foreach (var actual in originals)
            try { actual.RequestRetirement(); } catch (Exception error) { failures.Add(error); }
    }
    private Task JoinOriginalResumeObservationRetirementAsync()
    {
        var failures = new List<Exception>(); var closes = new List<Task>();
        SpaceOriginalRunResumeObservation[] originals;
        lock (_gate) originals = _resumeObservations.ToArray();
        foreach (var actual in originals)
        {
            try { actual.RequestRetirement(); } catch (Exception error) { failures.Add(error); }
            try { closes.Add(actual.CloseAndDrainAsync()); } catch (Exception error) { failures.Add(error); }
        }
        return JoinOriginalPresentationChildrenAsync(closes.Cast<Task?>().ToArray(), failures);
    }
}
