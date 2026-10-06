using Haven.Core;

namespace Haven.Application;

/// <summary>Facts about one local process/protocol probe. Copies and metadata grant no model,
/// paid use, context, tool effect, lease, or Task/Run authority.</summary>
public sealed record LocalModelEndpointObservation(
    string ProviderId, int ProcessId, string ProcessStartIdentity, string ExecutableSha256,
    string ConfiguredModelSha256, long ModelBytes, ulong TensorCount, string ModelId,
    IReadOnlySet<ToolCapability> ObservedCapabilities, DateTimeOffset ObservedAt);

public interface ILocalModelEndpointObservationSource
{
    Task<LocalModelEndpointObservation> ObserveOriginalEndpointAsync(CancellationToken cancellationToken);
    Task<LocalModelEndpointObservation> ProbeOriginalCapabilitiesAsync(bool includeAutomaticToolProbe, CancellationToken cancellationToken);
    bool IsOriginalEndpointObservation(LocalModelEndpointObservation observation);
}
