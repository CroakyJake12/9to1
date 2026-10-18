using Avalonia.Threading;
using Haven.Desktop.Services;
using HavenOS.Apps.Spaces;

namespace Haven.Desktop.Views.Pages.Spaces;

public sealed partial class SpacesHomePage
{
    private readonly List<Task> _homeOriginals = [];
    private Task? _homeLastPipeline;
    private HomeClosePreparation? _homeCloseLease;
    private Task<HomeClosePreparation?>? _homePreparation;
    private Task? _latestOriginalMutation;
    internal Task? LatestOriginalMutation { get { lock (_gate) return _latestOriginalMutation; } }

    internal sealed class HomeClosePreparation(SpacesHomePage owner)
    {
        internal readonly SpacesHomePage Owner = owner;
        internal SpacesHomeViewState? State;
        internal bool Ready;
        internal Task? Settlement;
        internal Task? Abort;
        internal bool IsCurrent { get { lock (Owner._gate) return Owner.IsCurrentHomeLease(this); } }
    }

    private bool CanAdmitHomeMutation() => _active && !_work.IsRetiring && _homeCloseLease is null;
    private Task RunHomeOriginal(Func<DesktopOriginalWorkLifetime.Original, Task> body)
    {
        // The SAME page original is published under admission, but controller/native
        // callbacks run outside that gate. Event owners may acquire encompassing gates.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task actual;
        lock (_gate) actual = _work.RunAsync(async original =>
        {
            await start.Task.ConfigureAwait(false);
            await body(original).ConfigureAwait(false);
        }, RecordHomeOriginal);
        start.SetResult();
        return actual;
    }
    private void RecordHomeOriginal(Task actual)
    {
        _homeOriginals.RemoveAll(task => task.IsCompletedSuccessfully);
        _homeOriginals.Add(actual);
    }

    internal Task<HomeClosePreparation?> PrepareHomeCloseAsync(CancellationToken caller = default)
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate)
        {
            _work.DemandAdmission();
            if (_homeCloseLease is not null) return _homePreparation!;
            if (_controller.State.PendingRenameSpaceId.HasValue || _editorOperationCount != 0)
                return Task.FromResult<HomeClosePreparation?>(null);
            var lease = new HomeClosePreparation(this);
            _homeCloseLease = lease; // Mutation admission, independent of presentation lifetime.
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _homePreparation = _work.RunAsync(async original =>
            {
                await start.Task.ConfigureAwait(false);
                original.BindPublicationGuard(() => !_work.IsRetiring);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token);
                var accepted = false;
                try
                {
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                        original.RunAcceptedPublicationCallback(_bindings.Refresh)).GetTask()).ConfigureAwait(false);
                    Task[] originals;
                    lock (_gate) originals = _homeOriginals.ToArray();
                    foreach (var actual in originals) await original.AwaitAsync(actual).ConfigureAwait(false);
                    Task? pipeline = null;
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        original.DemandPublication();
                        lock (_gate)
                            pipeline = _active && _exactOriginalShow is { } shown
                                ? _host.CaptureOriginalActionSettlement(shown) : _homeLastPipeline;
                    }).GetTask()).ConfigureAwait(false);
                    if (pipeline is not null) await original.AwaitAsync(pipeline).ConfigureAwait(false);
                    // StateChanged may have admitted a pure refresh while an earlier mutation
                    // was finishing. No new mutation can enter after the lease was published.
                    lock (_gate) originals = _homeOriginals.ToArray();
                    lease.Settlement = Task.WhenAll(pipeline is null ? originals : originals.Append(pipeline));
                    await original.AwaitAsync(lease.Settlement).ConfigureAwait(false);
                    lock (_gate)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (!ReferenceEquals(_homeCloseLease, lease) || _controller.State.PendingRenameSpaceId.HasValue ||
                            _editorOperationCount != 0 || _work.IsRetiring) return null;
                        lease.State = _controller.State;
                        lease.Ready = true;
                        accepted = true;
                        return lease;
                    }
                }
                finally
                {
                    if (!accepted)
                    {
                        lock (_gate) if (ReferenceEquals(_homeCloseLease, lease)) _homeCloseLease = null;
                        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                            original.RunAcceptedPublicationCallback(_bindings.Refresh)).GetTask()).ConfigureAwait(false);
                    }
                }
            });
            start.SetResult();
            return _homePreparation;
        }
    }

    private bool IsCurrentHomeLease(HomeClosePreparation lease) => ReferenceEquals(lease.Owner, this) &&
        ReferenceEquals(_homeCloseLease, lease) && lease.Ready && !_work.IsRetiring &&
        lease.Settlement?.IsCompletedSuccessfully == true && ReferenceEquals(lease.State, _controller.State) &&
        !_controller.State.PendingRenameSpaceId.HasValue && _editorOperationCount == 0;

    internal Task AbortHomeCloseAsync(HomeClosePreparation lease)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(lease.Owner, this)) throw new InvalidOperationException("Foreign Home preparation.");
            if (lease.Abort is not null) return lease.Abort;
            if (_work.OriginalClose is { } closing) return lease.Abort = closing;
            if (!ReferenceEquals(_homeCloseLease, lease)) throw new InvalidOperationException("Stale Home preparation.");
            _homeCloseLease = null; _homePreparation = null;
            return lease.Abort = RunHomeOriginal(async original =>
            {
                original.BindPublicationGuard(() => !_work.IsRetiring);
                await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                    original.RunAcceptedPublicationCallback(_bindings.Refresh)).GetTask()).ConfigureAwait(false);
            });
        }
    }

    internal Task BeginHomeClosePrepared(HomeClosePreparation lease)
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate)
        {
            if (!IsCurrentHomeLease(lease)) throw new InvalidOperationException("The exact Home preparation is no longer current.");
            return _work.CloseAndDrainAsync();
        }
    }

    // The session is the sole caller. This synchronous acceptance boundary holds
    // Home admission before session admission, matching Home→StateChanged→session.
    // It creates no task or close; the callback returns the actual session original.
    internal Task AcceptHomeClosePreparation(HomeClosePreparation lease, Func<Task> acceptSession)
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate)
        {
            if (!IsCurrentHomeLease(lease)) throw new InvalidOperationException("The exact Home preparation is no longer current.");
            return acceptSession();
        }
    }
}
