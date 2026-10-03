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
        ReferenceEquals(actualBroker, broker) && ReferenceEquals(actualOwnership, ownership) &&
        HomeLocalReadComposition.IsBound(actualHome, actualProfiles, actualOwnership);

    internal ValueTask<HomeClaimedResourceCommitFence?> CaptureAsync(string resourceKind, Guid originalStoreID,
        HomeResourceExecutionCapability actualCapability, AuthenticatedResourceActor originalActor,
        Func<bool> pureOriginalLifetime, CancellationToken token) =>
        HomeClaimedResourceCommitFence.CaptureSettingsAsync(actualBroker, actualHome, actualProfiles,
            actualOwnership, resourceKind, originalStoreID.ToString("D"), actualCapability, originalActor,
            pureOriginalLifetime, token);
    internal ValueTask<HomeClaimedResourceCommitFence?> CaptureShelfItemEditAsync(Guid originalStoreID,
        long originalRevision, string originalAction, System.Text.Json.JsonElement originalArguments,
        HomeResourceExecutionCapability actualCapability, AuthenticatedResourceActor originalActor,
        Func<bool> pureOriginalLifetime, CancellationToken token) =>
        HomeClaimedResourceCommitFence.CaptureShelfItemEditAsync(actualBroker, actualHome, actualProfiles,
            actualOwnership, originalStoreID.ToString("D"), originalRevision, originalAction, originalArguments,
            actualCapability, originalActor, pureOriginalLifetime, token);
    internal ValueTask<HomeClaimedResourceCommitFence?> CaptureShelfCollectionAsync(Guid originalStoreID,
        long originalRevision, string originalAction, System.Text.Json.JsonElement originalArguments,
        HomeResourceExecutionCapability actualCapability, AuthenticatedResourceActor originalActor,
        Func<bool> pureOriginalLifetime, CancellationToken token) =>
        HomeClaimedResourceCommitFence.CaptureShelfCollectionAsync(actualBroker, actualHome, actualProfiles,
            actualOwnership, originalStoreID.ToString("D"), originalRevision, originalAction, originalArguments,
            actualCapability, originalActor, pureOriginalLifetime, token);
}
