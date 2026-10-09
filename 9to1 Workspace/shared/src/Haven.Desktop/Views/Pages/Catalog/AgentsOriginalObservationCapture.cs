using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Catalog;

/// <summary>One exact service-issued observation, not its business producer. The scene
/// original owns the acquisition/wait/source-drain Tasks; the existing desktop owner
/// publishes and coalesces this child's retirement before any source callbacks.</summary>
internal sealed class AgentsOriginalObservationCapture
{
    private readonly object _gate = new();
    private readonly IAgentRunOriginalObservationSource _source;
    private readonly DesktopOriginalWorkLifetime.Original _sceneOriginal;
    private readonly Func<AgentRunOriginalObservationLease> _acquire;
    private readonly Func<bool> _sceneIsRetiring;
    private readonly DesktopOriginalWorkLifetime _retirement;
    private readonly TaskCompletionSource<bool> _start = new();
    private readonly Task<AgentRunOriginalObservationLease?> _actualAcquisition;
    private AgentRunOriginalObservationLease? _lease;
    private Task? _actualRequest;
    private Task<AgentRunObservationResult>? _actualWait;
    private Task? _actualSourceDetach;
    [ThreadStatic] private static List<AgentsOriginalObservationCapture>? _synchronousAcquisitions;

    internal AgentsOriginalObservationCapture(IAgentRunOriginalObservationSource source,
        DesktopOriginalWorkLifetime.Original sceneOriginal,
        Func<AgentRunOriginalObservationLease> acquire, Func<bool> sceneIsRetiring)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sceneOriginal = sceneOriginal ?? throw new ArgumentNullException(nameof(sceneOriginal));
        _acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
        _sceneIsRetiring = sceneIsRetiring ?? throw new ArgumentNullException(nameof(sceneIsRetiring));
        _retirement = new DesktopOriginalWorkLifetime(StopOriginalAsync, () => Task.CompletedTask);
        // Both actual drivers exist and the acquisition phase is enrolled before
        // the scene publishes this capture or permits a source callback to run.
        _actualAcquisition = AcquireOriginalAsync();
        ActualObservation = ObserveOriginalAsync();
    }

    internal Task<AgentRunObservationResult?> ActualObservation { get; }
    internal Task? OriginalClose => _retirement.OriginalClose;
    internal bool CanPruneHealthy
    {
        get
        {
            lock (_gate)
                return OriginalClose is { IsCompletedSuccessfully: true } &&
                    ActualObservation.IsCompletedSuccessfully && _actualAcquisition.IsCompletedSuccessfully &&
                    (_actualWait is null || _actualWait.IsCompletedSuccessfully) &&
                    (_actualSourceDetach is null || _actualSourceDetach.IsCompletedSuccessfully);
        }
    }

    internal void BeginOriginalAcquisition()
    {
        if (_sceneIsRetiring() || _retirement.IsRetiring)
        {
            RequestRetirement();
            return;
        }
        _start.TrySetResult(true);
    }

    private async Task<AgentRunOriginalObservationLease?> AcquireOriginalAsync()
    {
        // False is the outcome of this actual pre-callback gate: the source
        // factory was not invoked. It is not an inferred producer-completion ACK.
        if (!await _start.Task.ConfigureAwait(false)) return null;
        _sceneOriginal.DemandPublication();
        var actualLease = AcquireOriginalFactory();
        lock (_gate) _lease = actualLease; // Capture even when the factory retired the scene before returning.
        DemandIssued(actualLease);
        if (_sceneIsRetiring() || _retirement.IsRetiring) RequestRetirement();
        return actualLease;
    }

    private async Task<AgentRunObservationResult?> ObserveOriginalAsync()
    {
        try
        {
            var actualLease = await _sceneOriginal.AwaitAsync(_actualAcquisition).ConfigureAwait(false);
            if (actualLease is null) return null;
            if (_sceneIsRetiring() || _retirement.IsRetiring)
            {
                RequestRetirement();
                return null; // The caller still independently joins actual source detachment.
            }
            DemandIssued(actualLease);
            var actualWait = actualLease.WaitOriginalObservationAsync();
            lock (_gate) _actualWait = actualWait;
            var result = await _sceneOriginal.AwaitAsync(actualWait).ConfigureAwait(false);
            if (result.Disposition == AgentRunObservationDisposition.ProducerTerminal && result.TerminalRun is not null)
                return result;
            if (result.Disposition == AgentRunObservationDisposition.ObservationDetached && result.TerminalRun is null)
                return result;
            throw new InvalidOperationException("The actual Agent observation returned inconsistent terminal metadata.");
        }
        catch (Exception error)
        {
            _sceneOriginal.Retain(error);
            _sceneOriginal.ThrowRetained(); // In particular, a faulted/synchronous OCE remains a fault payload.
            throw;
        }
    }

    private AgentRunOriginalObservationLease AcquireOriginalFactory()
    {
        // A source callback can restore an ExecutionContext captured before this
        // scene original. Preserve every actual synchronous acquisition owner,
        // including an outer acquisition while another source factory is nested.
        var owners = _synchronousAcquisitions ??= [];
        owners.Add(this);
        try { return _acquire(); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }

    private void DemandIssued(AgentRunOriginalObservationLease actualLease)
    {
        ArgumentNullException.ThrowIfNull(actualLease);
        if (!_source.IsIssuedOriginalObservation(actualLease))
            throw new InvalidOperationException("The SAME Agent runtime did not issue this original observation.");
    }

    private async Task StopOriginalAsync()
    {
        var failed = false;
        // A retirement before gate release settles the actual acquisition driver
        // without invoking a business factory. A factory already in progress is
        // joined below and its returned resource cannot escape after sealing.
        _start.TrySetResult(false);
        AgentRunOriginalObservationLease? captured;
        lock (_gate) captured = _lease;
        Task? actualRequest = captured is null ? null : AcquireOriginalRequest(captured);
        try { await _sceneOriginal.AwaitAsync(_actualAcquisition).ConfigureAwait(false); }
        catch (Exception error) { _sceneOriginal.Retain(error); failed = true; }
        lock (_gate) captured = _lease;
        if (captured is not null)
        {
            // This checks permanent private issuance, not active-slot membership.
            // A genuine already-detached lease remains identifiable here.
            actualRequest ??= AcquireOriginalRequest(captured);
            Task? actualDetach = null;
            try
            {
                _retirement.RunCloseCallback(() =>
                {
                    DemandIssued(captured);
                    actualDetach = captured.DetachAndDrainOriginalObservationAsync();
                    lock (_gate) _actualSourceDetach = actualDetach;
                });
            }
            catch (Exception error) { _sceneOriginal.Retain(error); failed = true; }
            if (actualDetach is not null)
            {
                try { await _sceneOriginal.AwaitAsync(actualDetach).ConfigureAwait(false); }
                catch (Exception error) { _sceneOriginal.Retain(error); failed = true; }
            }
            else
            {
                _sceneOriginal.Retain(new InvalidOperationException("No actual Agent observation detach Task was acquired."));
                failed = true;
            }
        }
        if (actualRequest is not null)
        {
            try { await _sceneOriginal.AwaitAsync(actualRequest).ConfigureAwait(false); }
            catch (Exception error) { _sceneOriginal.Retain(error); failed = true; }
        }
        if (failed) _sceneOriginal.ThrowRetained();
    }

    private Task AcquireOriginalRequest(AgentRunOriginalObservationLease captured)
    {
        var start = new TaskCompletionSource();
        Task actual;
        lock (_gate)
        {
            if (_actualRequest is not null) return _actualRequest;
            actual = RequestOriginalAsync(start.Task, captured);
            _actualRequest = actual; // Actual driver published before the void source callback.
        }
        start.SetResult();
        return actual;
    }

    private async Task RequestOriginalAsync(Task start, AgentRunOriginalObservationLease captured)
    {
        await start.ConfigureAwait(false);
        try
        {
            _retirement.RunCloseCallback(() =>
            {
                DemandIssued(captured);
                captured.RequestOriginalObservationRetirement();
            });
        }
        catch (Exception error)
        {
            _sceneOriginal.Retain(error);
            _sceneOriginal.ThrowRetained();
            throw;
        }
    }

    internal void RequestRetirement() => _retirement.RequestRetirement();
    internal void DemandExternalClose()
    {
        if (_synchronousAcquisitions?.Any(actual => ReferenceEquals(actual, this)) == true)
            throw new InvalidOperationException("An actual Agent observation factory must return before joining its encompassing retirement.");
        _retirement.DemandExternalClose();
    }
    internal Task CloseAndDrainAsync()
    {
        DemandExternalClose(); // Also before returning an already published close.
        return _retirement.CloseAndDrainAsync();
    }
}
