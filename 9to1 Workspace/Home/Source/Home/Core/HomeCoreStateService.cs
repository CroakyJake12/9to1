namespace HavenOS.Home.Core;

/// <summary>Loads and validates durable control-plane state before Home advertises it as available.</summary>
public sealed class HomeCoreStateService(IHomeCoreStateStore store) : IHomeCoreService
{
    private readonly IHomeCoreStateStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public HomeServiceDescriptor Descriptor { get; } = new(
        "home.state", HomeCoreServiceCatalog.CurrentContractVersion,
        HomeServiceLifecycleState.Stopped, false, "Home state has not been read.");

    public IReadOnlyList<string> Dependencies { get; } = ["home.core"];

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var result = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            var failure = result.Failure!;
            throw new HomeCoreStateUnavailableException(failure.Code, failure.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class HomeCoreStateUnavailableException(HomeCoreErrorCode code, string message) : IOException(message)
{
    public HomeCoreErrorCode Code { get; } = code;
}

