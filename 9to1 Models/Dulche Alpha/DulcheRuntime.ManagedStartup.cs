using System.Collections.Frozen;
using Haven.Application;

namespace Dulche.Runtime;

/// <summary>Actual runtime acquisition and startup originals. This observation is not a model,
/// permission, context or residency grant; failed/canceled startup still has an owned endpoint.</summary>
public sealed class DulcheEndpointStartupOriginal
{
    private readonly TaskCompletionSource _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DulcheEndpointStartupOriginal _originalSelf;
    internal DulcheEndpointStartupOriginal(DulcheEndpoint actual)
    { OriginalAcquisition = actual; _originalSelf = this; }
    public DulcheEndpoint OriginalAcquisition { get; }
    private Task<OperationResult<DulcheEndpoint>>? _originalStartup;
    public Task<OperationResult<DulcheEndpoint>> OriginalStartup => _originalStartup
        ?? throw new InvalidOperationException("The actual startup task has not been published.");
    private readonly TaskCompletionSource _capture = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task OriginalCaptureSignal => _capture.Task;
    internal Task OriginalStartSignal => _start.Task;
    internal CancellationTokenRegistration OriginalRetirementSignal, OriginalCallerSignal;
    internal Task? OriginalRetirementRelease, OriginalCallerRelease;
    internal void CaptureOriginalSignals(CancellationToken retirement, CancellationToken caller)
    {
        try
        {
            OriginalRetirementSignal = retirement.Register(static state => ((DulcheEndpointStartupOriginal)state!).StartOriginal(), this);
            OriginalCallerSignal = caller.Register(static state => ((DulcheEndpointStartupOriginal)state!).StartOriginal(), this);
        }
        catch (Exception error) { _start.TrySetException(error); }
        finally { _capture.TrySetResult(); } // Whole startup/finally cannot finish before both actual handles are captured.
    }
    internal void Publish(Task<OperationResult<DulcheEndpoint>> actual)
    { if (Interlocked.CompareExchange(ref _originalStartup, actual, null) is not null) throw new InvalidOperationException("Original startup is already published."); }
    internal bool OriginalRuntimeAcquired;
    public bool WasAcquired => OriginalRuntimeAcquired;
    internal void RefuseAcquisition(Exception originalCause) => _start.TrySetException(originalCause);
    /// <summary>Releases only this already-published startup. Repeated calls have no new effect.</summary>
    public void StartOriginal()
    {
        if (!ReferenceEquals(_originalSelf, this)) throw new InvalidOperationException("This is not the original startup receipt.");
        _ = OriginalStartup;
        _start.TrySetResult();
    }
    internal Task<OperationResult<Unit>>? OriginalAdapterStart;
}

public sealed partial class DulcheRuntime
{
    private readonly AsyncLocal<OriginalEndpointPhase?> _originalStartingEndpoint = new();

    /// <summary>Publishes the actual acquisition and whole startup task before provider callbacks.
    /// The returned startup stays gated until its owner retains the receipt and calls StartOriginal; no model load or fake readiness.</summary>
    public DulcheEndpointStartupOriginal PrepareManagedProviderOriginal(string providerId,
        CancellationToken cancellationToken = default)
        => PrepareManagedProviderOriginalCore(providerId, null, cancellationToken);

    /// <summary>Model-bearing original acquisition. The model is an observation to the actual
    /// engine source; this method neither loads a model nor supplies context/permission authority.</summary>
    public DulcheEndpointStartupOriginal PrepareManagedProviderOriginal(string providerId,
        ModelIdentity sameModel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sameModel);
        if (!StringComparer.Ordinal.Equals(providerId, sameModel.ProviderId) || string.IsNullOrWhiteSpace(sameModel.ModelId))
            throw new ArgumentException("A SAME provider-scoped model is required.", nameof(sameModel));
        return PrepareManagedProviderOriginalCore(providerId, sameModel, cancellationToken);
    }

    private DulcheEndpointStartupOriginal PrepareManagedProviderOriginalCore(string providerId,
        ModelIdentity? sameModel, CancellationToken cancellationToken)
    {
        if (!_adapters.TryGetValue(providerId, out var registered) || registered is not IDulcheOriginalProviderAdapter adapter)
            throw new InvalidOperationException("No original managed provider is composed.");
        var target = adapter.OriginalConfiguredTarget;
        if (!target.IsAbsoluteUri || target.Scheme is not ("http" or "https")
            || target.Scheme != Uri.UriSchemeHttps && !target.IsLoopback)
            throw new UnauthorizedAccessException("The actual configured provider target is unavailable.");
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = new DulcheEndpoint(Guid.NewGuid().ToString("N"), adapter.ProviderId,
            target.GetLeftPart(UriPartial.Path), adapter.IsLocal ? target.Port : null,
            EndpointState.Starting, sameModel, adapter.Capabilities.ToFrozenSet(StringComparer.Ordinal), !adapter.IsLocal, DateTimeOffset.UtcNow);
        var slot = new EndpointSlot(endpoint, adapter, _maximumQueueDepth);
        var receipt = new DulcheEndpointStartupOriginal(endpoint);
        var original = StartManagedEndpointOriginalAsync(slot, receipt, receipt.OriginalStartSignal, cancellationToken);
        receipt.Publish(original);
        slot.RetainOriginalStartup(original); // Enrollment precedes public endpoint visibility.
        if (!_endpoints.TryAdd(endpoint.EndpointId, slot))
            receipt.RefuseAcquisition(new InvalidOperationException("The original runtime endpoint identity collided."));
        else receipt.OriginalRuntimeAcquired = true;
        receipt.CaptureOriginalSignals(slot.Stopping.Token, cancellationToken);
        return receipt;
    }

    private async Task<OperationResult<DulcheEndpoint>> StartManagedEndpointOriginalAsync(EndpointSlot slot,
        DulcheEndpointStartupOriginal receipt, Task start, CancellationToken cancellationToken)
    {
        await receipt.OriginalCaptureSignal.ConfigureAwait(false);
        var previous = _originalStartingEndpoint.Value;
        var phase = new OriginalEndpointPhase(slot, previous); _originalStartingEndpoint.Value = phase;
        var failures = new List<Exception>();
        OperationResult<DulcheEndpoint>? result = null;
        try
        {
            await start.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            receipt.OriginalAdapterStart = (Task<OperationResult<Unit>>)InvokePhysicalOriginalEndpoint(slot,
                () => slot.CaptureOriginalSubmissionCallback(() => slot.Adapter.StartAsync(slot.Endpoint, cancellationToken).AsTask()));
            var observed = await receipt.OriginalAdapterStart.ConfigureAwait(false);
            if (!observed.Succeeded)
            {
                slot.Endpoint = slot.Endpoint with { State = EndpointState.Failed, LastError = observed.Error,
                    UpdatedAt = DateTimeOffset.UtcNow };
                result = OperationResult<DulcheEndpoint>.Failure(observed.Error!);
            }
            else
            {
                slot.PublishOriginalManagedReady(); // SAME retirement gate refuses publication after stop seals admission.
                result = OperationResult<DulcheEndpoint>.Success(slot.Endpoint);
            }
        }
        catch (Exception error)
        {
            AddOriginalStopCause(failures, error);
            if (receipt.OriginalAdapterStart?.Exception is { } group)
                foreach (var cause in group.InnerExceptions) AddOriginalStopCause(failures, cause);
            slot.Endpoint = slot.Endpoint with { State = EndpointState.Failed, UpdatedAt = DateTimeOffset.UtcNow };
        }
        finally
        {
            try { receipt.OriginalRetirementRelease = receipt.OriginalRetirementSignal.DisposeAsync().AsTask(); }
            catch (Exception error) { AddOriginalStopCause(failures, error); }
            try { receipt.OriginalCallerRelease = receipt.OriginalCallerSignal.DisposeAsync().AsTask(); }
            catch (Exception error) { AddOriginalStopCause(failures, error); }
            if (receipt.OriginalRetirementRelease is { } retirement) await JoinOriginalStopTaskAsync(retirement, failures).ConfigureAwait(false);
            if (receipt.OriginalCallerRelease is { } caller) await JoinOriginalStopTaskAsync(caller, failures).ConfigureAwait(false);
            phase.Retire(); _originalStartingEndpoint.Value = previous;
        }
        if (failures.Count != 0)
            slot.Endpoint = slot.Endpoint with { State = EndpointState.Failed, UpdatedAt = DateTimeOffset.UtcNow };
        ThrowOriginalStopCauses(failures);
        return result ?? throw new InvalidOperationException("No original startup result was produced.");
    }

    /// <summary>Denies a live dependency before a higher owner seals its own close. No work is
    /// stopped or joined by this finite check; missing IDs do not become settlement proof.</summary>
    public void RequireIndependentOriginalEndpointJoin(string endpointId)
    {
        if (!_endpoints.TryGetValue(endpointId, out var endpoint))
            throw new InvalidOperationException("The actual acquired runtime endpoint is absent.");
        if (IsLiveOriginalEndpointCall(endpoint))
            throw new InvalidOperationException("An original endpoint callback cannot join the endpoint containing it.");
    }
}
