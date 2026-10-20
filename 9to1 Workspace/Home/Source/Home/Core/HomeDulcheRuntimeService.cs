namespace HavenOS.Home.Core;

/// <summary>
/// The single Home-owned lifecycle owner for the shared local Dulche runtime
/// (<c>dulche.runtime</c>). It runs through the actual Home Core/native host startup and
/// composes platform discovery and verified model evidence from authorised owners. It owns or
/// attaches to process resources only through supported borrowed-ownership contracts; it never
/// launches a competing model server, downloads a model or grants taskless use.
/// </summary>
public sealed class HomeDulcheRuntimeService : IHomeCoreService
{
    private readonly IHomeDulcheRuntimeObservationSource _source;
    private readonly object _sync = new();
    private HomeDulcheRuntimeObservation? _observation;

    public HomeDulcheRuntimeService(IHomeDulcheRuntimeObservationSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public HomeServiceDescriptor Descriptor { get; } = new(
        "dulche.runtime",
        HomeCoreServiceCatalog.CurrentContractVersion,
        HomeServiceLifecycleState.Stopped,
        false,
        "The shared Dulche local runtime has not started.");

    public IReadOnlyList<string> Dependencies { get; } = [];

    /// <summary>Runs discovery once. With verified installed runtime and model artifacts the
    /// service is Ready; otherwise it reports a structured, recoverable degraded observation
    /// (missing runtime, missing model, altered/incompatible artifact or unavailable transport)
    /// that the Home registry exposes truthfully without blocking unrelated services.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var observation = await _source.ObserveAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync) _observation = observation;
        if (observation.Lifecycle == HomeServiceLifecycleState.Ready) return;
        if (observation.Deficiencies.Count != 0)
            throw new InvalidOperationException("HomeDulcheRuntimeUnavailable: " + string.Join("; ", observation.Deficiencies));
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // This service starts/attaches through supported borrowed-ownership contracts only;
        // it owns no standalone server here, so stopping publishes an honest stopped state.
        return Task.CompletedTask;
    }

    /// <summary>The last genuinely observed lifecycle/catalogue state, or null before start.</summary>
    public HomeDulcheRuntimeObservation? CurrentObservation
    {
        get { lock (_sync) return _observation; }
    }
}
