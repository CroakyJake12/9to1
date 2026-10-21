using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Optional originating-owner route edit port. The host retains the original authenticated actor;
/// this metadata never grants route ownership, model access, or review permission.</summary>
public interface IHomeModelPickerOriginalActorFeatureProvider : IHomeModelPickerFeatureProvider
{
    Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteForActorAsync(
        AuthenticatedResourceActor expectedActor, HomeModelRouteEdit edit, CancellationToken cancellationToken = default);

    /// <summary>Reads the genuine typed authorization decision for a retained request id without creating a
    /// new authorization, reusing trust, or beginning execution. A provider that cannot observe it returns
    /// null (treated as unknown/missing); the source invalidates any retained review in that case rather
    /// than guessing pending from an opaque Denied result.</summary>
    Task<HomePermissionAuthorization?> GetApprovalAuthorizationAsync(string requestId, CancellationToken cancellationToken = default) =>
        Task.FromResult<HomePermissionAuthorization?>(null);
}
