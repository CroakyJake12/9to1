#if !ANDROID
using HavenOS.Apps.Assistants.Contracts;

namespace Haven.Desktop;

/// <summary>
/// Concrete <see cref="IAssistantRailIconSource"/> for the Haven host. It reads the authorised
/// configured-Assistant catalogue from the SAME composed canonical bridge (when one is available),
/// and truthfully reports that the running/usage sources are not yet composed: it never invents a
/// running assistant or a most-used aggregate. This drives the projection's distinct
/// unavailable / usage-not-recorded states until the genuine storage owners exist.
/// </summary>
public sealed class HostAssistantRailIconSource(Func<IAssistantCanonicalBridge?> bridgeFactory)
    : IAssistantRailIconSource
{
    private readonly Func<IAssistantCanonicalBridge?> _bridgeFactory = bridgeFactory
        ?? throw new ArgumentNullException(nameof(bridgeFactory));

    public async Task<AssistantCatalogueObservation?> ReadConfiguredCatalogueAsync(CancellationToken token)
    {
        var bridge = _bridgeFactory();
        if (bridge is null) return null; // unavailable: no composed canonical bridge
        return await bridge.ListAsync(token).ConfigureAwait(false);
    }

    /// <summary>No genuine running-run source is composed for the rail icon; a running read must
    /// come from real Task/Den execution ownership (next storage-owner task), never inference.</summary>
    public Task<AssistantDefinitionSnapshot?> ReadCurrentRunningAsync(
        IReadOnlyList<AssistantDefinitionSnapshot> configured, CancellationToken token)
        => Task.FromResult<AssistantDefinitionSnapshot?>(null);

    /// <summary>No canonical Assistant usage aggregate is recorded; most-used is not invented.</summary>
    public Task<AssistantDefinitionSnapshot?> ReadMostUsedAsync(
        IReadOnlyList<AssistantDefinitionSnapshot> configured, CancellationToken token)
        => Task.FromResult<AssistantDefinitionSnapshot?>(null);
}
#endif
