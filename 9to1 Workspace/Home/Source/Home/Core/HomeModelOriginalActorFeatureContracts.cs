using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Optional originating-owner route edit port. The host retains the original authenticated actor;
/// this metadata never grants route ownership, model access, or review permission.</summary>
public interface IHomeModelPickerOriginalActorFeatureProvider : IHomeModelPickerFeatureProvider
{
    Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteForActorAsync(
        AuthenticatedResourceActor expectedActor, HomeModelRouteEdit edit, CancellationToken cancellationToken = default);
}
