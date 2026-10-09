#if !ANDROID
using HavenOS.Apps.Assistants.Canonical;

namespace Haven.Desktop.Services;

/// <summary>App-owned ordinary Chat lifetime. A presentation borrows OriginalHost;
/// only actual process retirement seals admission and cancels this SAME business token.</summary>
internal sealed class OriginalAssistantConversationProcessOwner :
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _business = new();
    private readonly List<Exception> _stopFailures = [];
    [ThreadStatic] private static OriginalAssistantConversationProcessOwner? _physicalSource;
    private Task? _actualStop;
    private Task? _close;
    private Task? _actualHostClose;

    internal OriginalAssistantConversationProcessOwner() => OriginalHost = new(_business.Token);
    internal AssistantOriginalConversationHost OriginalHost { get; }
    internal Task? OriginalClose { get { lock (_gate) return _close; } }
    internal Task? OriginalHostClose { get { lock (_gate) return _actualHostClose; } }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (ReferenceEquals(_physicalSource, this))
            throw new InvalidOperationException("An actual ordinary Chat process callback cannot join its own owner.");
        OriginalHost.DemandExternalOriginalRetirementJoin();
    }

    public void RequestRetirement()
    {
        DemandExternalOriginalRetirementJoin();
        TaskCompletionSource settlement;
        lock (_gate)
        {
            if (_actualStop is not null) return;
            settlement = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _actualStop = settlement.Task; // Retained before actual synchronous cancellation callbacks.
        }
        try
        {
            Invoke(() =>
            {
                try { OriginalHost.RequestRetirement(); }
                catch (Exception cause) { lock (_gate) Add(_stopFailures, cause); }
                // Cancel runs the actual registered callbacks synchronously. Preserve
                // their exact failures and still acquire/join the host original below.
                try { _business.Cancel(); }
                catch (Exception cause) { lock (_gate) Add(_stopFailures, cause); }
                return true;
            });
        }
        finally { settlement.SetResult(); } // Callback settlement only; faults remain separately retained.
        lock (_gate) Throw(_stopFailures);
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        Task actual;
        TaskCompletionSource? start = null;
        lock (_gate)
        {
            if (_close is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = JoinOriginalHostAsync(start.Task);
            }
            actual = _close;
        }
        if (start is not null)
        {
            // SAME actual close is published before any synchronous token callback.
            try { RequestRetirement(); }
            catch (Exception cause) { lock (_gate) Add(_stopFailures, cause); }
            start.SetResult(); // Outside the physical callback; no self-join exemption.
        }
        return actual;
    }

    private async Task JoinOriginalHostAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        Task actualStop;
        lock (_gate) actualStop = _actualStop
            ?? throw new InvalidOperationException("The actual process stop callback was not acquired.");
        await actualStop.ConfigureAwait(false); // SAME stop callback is terminal before failure/cohort capture.
        lock (_gate) foreach (var cause in _stopFailures) Add(failures, cause);
        Task? actual = null;
        try
        {
            actual = Invoke(OriginalHost.CloseAndDrainAsync);
            lock (_gate) _actualHostClose = actual;
        }
        catch (Exception cause) { Add(failures, cause); }
        if (actual is not null)
            try { await actual.ConfigureAwait(false); }
            catch (Exception observed)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } envelope)
                    foreach (var direct in envelope.InnerExceptions) Add(failures, direct);
                else Add(failures, observed);
            }
        // Cancellation is NOT normalized: this host cannot currently prove that
        // an ordinary Chat durable writer acknowledged before MoveNext withdrew.
        // Failure retains this token/host and keeps Den/Home dependencies alive.
        Throw(failures);
        Invoke(() => { _business.Dispose(); return true; });
    }

    private T Invoke<T>(Func<T> source)
    {
        var previous = _physicalSource; _physicalSource = this;
        try { return source(); }
        finally { _physicalSource = previous; }
    }
    private static void Add(List<Exception> failures, Exception cause)
    { if (!failures.Any(actual => ReferenceEquals(actual, cause))) failures.Add(cause); }
    private static void Throw(List<Exception> failures)
    {
        if (failures.Count != 0)
            throw new AggregateException("The actual ordinary Chat process did not settle cleanly.", failures);
    }
}
#endif
