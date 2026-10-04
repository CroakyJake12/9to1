using Haven.Core;

namespace Haven.Application;

/// <summary>Profiles actually served by the configured routing engine/dataset, not profile labels
/// accepted in a request URL. Host UI/AI discovery must omit unsupported route operations.</summary>
public interface IMapRoutingCapabilities
{
    IReadOnlySet<MapTravelProfile> SupportedProfiles { get; }
}
