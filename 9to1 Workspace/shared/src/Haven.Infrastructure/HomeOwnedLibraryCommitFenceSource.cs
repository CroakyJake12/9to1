using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Same local Home composition for final Maps/Shelf claimed publication. Capture grants no write;
/// the owner settings transaction acquires the retained raw Home fence before physical publication.</summary>
public sealed class HomeOwnedLibraryCommitFenceSource(FileHomeCoreStateStore actualHome,
    HomeLocalProfileIdentity actualProfiles, HomeResourceStoreOwnershipAuthority actualOwnership,
    HomeResourceOperationBroker actualBroker)
{
    internal bool IsFor(HomeResourceOperationBroker broker, IResourceStoreOwnershipAuthority ownership) =>
        ReferenceEquals(actualBroker, broker) && ReferenceEquals(actualOwnership, ownership);

    internal ValueTask<HomeClaimedResourceCommitFence?> CaptureAsync(string resourceKind, Guid originalStoreID,
        HomeResourceExecutionCapability actualCapability, AuthenticatedResourceActor originalActor,
        Func<bool> pureOriginalLifetime, CancellationToken token) =>
        HomeClaimedResourceCommitFence.CaptureSettingsAsync(actualBroker, actualHome, actualProfiles,
            actualOwnership, resourceKind, originalStoreID.ToString("D"), actualCapability, originalActor,
            pureOriginalLifetime, token);
}
