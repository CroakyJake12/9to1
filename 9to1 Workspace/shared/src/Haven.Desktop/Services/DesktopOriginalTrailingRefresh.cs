namespace Haven.Desktop.Services;

/// <summary>One retained trailing driver, not a per-keystroke cancellation owner.
/// The caller supplies the SAME parent original registry and exact refresh Task.
/// Test clock/delay probes certify custody only, never GUI presentation or timing.</summary>
internal sealed class DesktopOriginalTrailingRefresh(
    DesktopOriginalWorkLifetime parent,
    TimeSpan quietPeriod,
    Func<DesktopOriginalWorkLifetime.Original, int, Task> refresh,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, Task>? originalDelaySource = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private bool _stopped;
    private object? _driverToken;
    private long _lastInput;
    private int _generation;
    private Task? _actualDriver;

    internal Task? OriginalDriver { get { lock (_gate) return _actualDriver; } }
    internal void Schedule(int generation)
    {
        parent.DemandAdmission();
        var actualTimestamp = _clock.GetTimestamp();
        object driverToken;
        lock (_gate)
        {
            if (_stopped) throw new ObjectDisposedException(nameof(DesktopOriginalTrailingRefresh));
            _lastInput = actualTimestamp;
            _generation = generation;
            if (_driverToken is not null) return;
            _driverToken = driverToken = new object();
        }
        try
        {
            // Never hold driver metadata while taking the parent registry gate.
            // The parent's publication callback precedes every driver callback.
            parent.RunAsync(original => RunOriginalDriverAsync(original, driverToken),
                actual => { lock (_gate) _actualDriver = actual; });
        }
        catch
        {
            lock (_gate) if (ReferenceEquals(_driverToken, driverToken)) _driverToken = null;
            throw;
        }
    }
    internal void RequestRetirement() { lock (_gate) _stopped = true; }

    private async Task RunOriginalDriverAsync(DesktopOriginalWorkLifetime.Original original, object driverToken)
    {
        try
        {
            while (true)
            {
                long actualInput; int generation;
                lock (_gate)
                {
                    if (_stopped) return;
                    actualInput = _lastInput; generation = _generation;
                }
                var remaining = quietPeriod - AcquireValue(original,
                    () => _clock.GetElapsedTime(actualInput, _clock.GetTimestamp()));
                if (remaining > TimeSpan.Zero)
                {
                    var delay = AcquireActual(original, () => originalDelaySource is null
                        ? Task.Delay(remaining, _clock, CancellationToken.None) : originalDelaySource(remaining));
                    await original.AwaitAsync(delay);
                    continue; // Re-read actual last input after the SAME delay settles.
                }
                lock (_gate)
                {
                    if (_stopped) return;
                    if (_generation != generation || _lastInput != actualInput) continue;
                }
                await original.AwaitAsync(AcquireActual(original, () => refresh(original, generation)));
                lock (_gate)
                {
                    if (_stopped || (_generation == generation && _lastInput == actualInput))
                    {
                        if (ReferenceEquals(_driverToken, driverToken)) _driverToken = null;
                        return;
                    }
                }
                // Input arriving while the actual repository/dispatcher is held
                // gets another trailing refresh; it cannot be lost on completion.
            }
        }
        finally { lock (_gate) if (ReferenceEquals(_driverToken, driverToken)) _driverToken = null; }
    }
    private static T AcquireValue<T>(DesktopOriginalWorkLifetime.Original original, Func<T> source)
    {
        try { return source(); }
        catch (Exception error)
        {
            original.Retain(error);
            if (error is OperationCanceledException)
                throw new AggregateException("A synchronous trailing value supplied no canceled original Task.", error);
            throw;
        }
    }
    private static Task AcquireActual(DesktopOriginalWorkLifetime.Original original, Func<Task> source)
    {
        try { return source() ?? throw new InvalidOperationException("The original trailing driver source returned no Task."); }
        catch (Exception error)
        {
            original.Retain(error);
            if (error is OperationCanceledException)
                throw new AggregateException("A synchronous trailing source supplied no canceled original Task.", error);
            throw;
        }
    }
}
