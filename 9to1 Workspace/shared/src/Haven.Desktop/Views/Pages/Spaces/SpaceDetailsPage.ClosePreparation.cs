using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using HavenOS.Apps.Spaces.Builder;

namespace Haven.Desktop.Views.Pages.Spaces;

public sealed partial class SpaceDetailsPage
{
    /// <summary>A reversible page-only preparation, not an umbrella permission or save receipt.
    /// The future host must abort every acquired page lease when another participant vetoes.</summary>
    public sealed class ClosePreparation
    {
        internal ClosePreparation(SpaceDetailsPage owner, SpaceDetailsState state, long epoch)
        { Owner = owner; Accepted = state; Epoch = epoch; }
        internal SpaceDetailsPage Owner { get; }
        internal SpaceDetailsState Accepted { get; }
        internal long Epoch { get; }
        internal Task Settlement { get; set; } = Task.CompletedTask;
        public Guid SpaceId => Accepted.SpaceId;
        public long? Revision => Accepted.LoadedRevision;
        public SpaceDetailsDraft Draft => Accepted.Draft;
    }

    public Task<ClosePreparation?> PrepareCloseAsync(CancellationToken caller = default)
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate)
        {
            _work.DemandAdmission();
            if (_preparation is not null && (!_preparation.IsCompleted || _lease is not null)) return _preparation;
            return _preparation = _work.RunAsync(async original =>
            {
                try { return await PrepareCoreAsync(original, caller).ConfigureAwait(false); }
                catch
                {
                    // This also covers failures in the decision's finally/restore path.
                    // No lease escaped to the caller, so it cannot be left freezing edits.
                    lock (_gate) _lease = null;
                    RefreshBindings();
                    throw;
                }
            });
        }
    }

    private async Task<ClosePreparation?> PrepareCoreAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        original.BindPublicationGuard(IsPresentationCurrent);
        lock (_gate)
        {
            if (caller.IsCancellationRequested || !_activated || State.IsBusy || _controllerOperations != 0) return null;
            if (!State.HasChanges && !State.HasConflict) return SealPreparation();
        }
        Decision? decision = null;
        try
        {
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
            {
                original.DemandPublication();
                lock (_gate)
                {
                    decision = new Decision(this);
                    _decisions.Add(decision); // Retain the actual host before Show can fail.
                    _decision = decision;
                    decision.Show = Source(() => decision.Host.ShowAsync(new("spaces.details.close", "Unsaved Space details",
                        "Unsaved Space details", LoadDecisionDocument(), decision.Bindings, decision.Bindings, _readiness)
                        { IsPublicationCurrent = IsPresentationCurrent }, original.Token));
                }
            }).GetTask()).ConfigureAwait(false);
            var actual = decision ?? throw new InvalidOperationException("No original details decision was acquired.");
            var shown = await original.AwaitAsync(actual.Show!).ConfigureAwait(false);
            if (shown.State != CuiSceneAvailabilityState.Ready) return null;
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
            {
                original.RunAcceptedPublicationCallback(() => Content = actual.Host);
            }).GetTask()).ConfigureAwait(false);

            // Caller cancellation withdraws this decision only. It does not cancel an
            // already acquired canonical save; that original must settle and retain faults.
            using var registration = caller.Register(() => WithdrawDecision(actual));
            using var retirement = original.Token.Register(() => WithdrawDecision(actual));
            var accepted = await original.AwaitAsync(actual.Answer.Task).ConfigureAwait(false);
            var pipeline = await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync<Task?>(() =>
            {
                lock (_gate)
                    // Retirement has already withdrawn capture admission. Its exact
                    // host Close below still joins all actual loader actions.
                    return _live ? actual.Host.CaptureOriginalActionSettlement(actual.Show!) : null;
            }).GetTask()).ConfigureAwait(false);
            Task[] commands;
            lock (_gate) commands = actual.Commands.ToArray();
            var settlement = Task.WhenAll(pipeline is null ? commands : commands.Append(pipeline));
            if (actual.Lease is { } lease) lease.Settlement = settlement;
            await original.AwaitAsync(settlement).ConfigureAwait(false);
            lock (_gate)
            {
                if (!accepted || caller.IsCancellationRequested || !_live)
                {
                    if (ReferenceEquals(_lease, actual.Lease)) _lease = null;
                    return null;
                }
                return IsExactLease(actual.Lease) ? actual.Lease : null;
            }
        }
        finally
        {
            if (decision is not null)
            {
                try
                {
                    await original.AwaitAsync(CloseDecisionAsync(decision)).ConfigureAwait(false);
                    lock (_gate)
                        if (decision.Show?.IsCompletedSuccessfully == true && decision.Commands.All(task => task.IsCompletedSuccessfully))
                            _decisions.Remove(decision); // Exact host close joined loader actions; failed/pending custody remains.
                }
                finally
                {
                    lock (_gate) if (ReferenceEquals(_decision, decision)) _decision = null;
                    if (_live)
                        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                            original.RunAcceptedPublicationCallback(() => { Content = _editorHost; _bindings.Refresh(); })).GetTask()).ConfigureAwait(false);
                }
            }
        }
    }

    private void WithdrawDecision(Decision decision)
    {
        lock (_gate)
        {
            decision.Done = true;
            if (ReferenceEquals(_lease, decision.Lease)) _lease = null;
            decision.Answer.TrySetResult(false);
        }
    }

    private ClosePreparation SealPreparation()
    {
        var state = State;
        if (!_live || state.IsBusy || state.HasChanges || state.HasConflict || _lease is not null)
            throw new InvalidOperationException("The details draft did not remain ready for preparation.");
        _lease = new(this, state, _editEpoch);
        RefreshBindings();
        return _lease;
    }

    private bool IsExactLease(ClosePreparation? lease) => lease is not null &&
        ReferenceEquals(lease.Owner, this) && ReferenceEquals(_lease, lease) && _live &&
        lease.Epoch == _editEpoch && lease.Accepted.SpaceId == State.SpaceId &&
        lease.Accepted.LoadedRevision == State.LoadedRevision && lease.Accepted.Draft == State.Draft &&
        !State.IsBusy && !State.HasChanges && !State.HasConflict;

    public async Task<bool> AbortCloseAsync(ClosePreparation lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_gate)
        {
            if (!IsExactLease(lease) || _work.IsRetiring) return false;
            _lease = null;
            _preparation = null;
        }
        await RefreshBindingsAsync().ConfigureAwait(false);
        lock (_gate) return _live && !_work.IsRetiring;
    }

    public async Task<bool> CommitCloseAsync(ClosePreparation lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        DemandExternalOriginalRetirementJoin();
        lock (_gate) if (!IsExactLease(lease)) return false;
        // This is an EXTERNAL driver, never an admitted page/CUI action joining itself.
        // The lease contains the exact decision original and loader settlement, not a delay.
        await lease.Settlement.ConfigureAwait(false);
        Task close;
        lock (_gate)
        {
            if (!IsExactLease(lease)) return false; // Abort/foreign/stale never caches retirement.
            close = _work.CloseAndDrainAsync(); // Atomically fence admission while exact lease holds.
        }
        await close.ConfigureAwait(false);
        return true;
    }

    private Task CloseDecisionAsync(Decision decision)
    {
        lock (_gate) return decision.Close ??= Source(decision.Host.CloseOriginalAsync);
    }

    private Task DispatchDecisionAsync(Decision decision, string command)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_decision, decision) || decision.Done || decision.Busy || !_live)
                throw new InvalidOperationException("This details decision no longer admits an action.");
            decision.Busy = true;
            return _work.RunAsync(async original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                try
                {
                    if (command == "space-details.close.cancel") { WithdrawDecision(decision); return; }
                    var operation = command switch
                    {
                        "space-details.close.save" => Source(() => _controller.SaveAsync(original.Token)),
                        "space-details.close.discard" => Source(() => _controller.DiscardChangesAsync(original.Token)),
                        _ => throw new InvalidOperationException("This details decision command is not owned.")
                    };
                    var succeeded = await original.AwaitAsync(operation).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (succeeded && !decision.Done && _live && !State.HasChanges && !State.HasConflict && !State.IsBusy)
                        {
                            decision.Lease = SealPreparation();
                            decision.Done = true;
                            decision.Answer.TrySetResult(true);
                        }
                    }
                }
                catch { WithdrawDecision(decision); throw; }
                finally { lock (_gate) decision.Busy = false; RefreshBindings(); }
            }, task => decision.Commands.Add(task));
        }
    }
}
