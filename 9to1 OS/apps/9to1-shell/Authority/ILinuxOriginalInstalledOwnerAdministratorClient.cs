using Haven.Application;
using HavenOS.Home.Core;
namespace NineToOne.Os.Shell.Authority;

// Trusted actual root-client implementation only; no default implementation or DTO issuance.
internal interface ILinuxOriginalInstalledOwnerAdministratorClient :
    IHomeNativeControlledLaunchOriginalHomeActorObservationSource, IAsyncDisposable
{
    CancellationToken OriginalLifetime { get; }
    IHomeNativeInstalledPeerOriginalActorVerifier CreateInstalledOwnerVerifier(IInstalledApplicationRegistry registry);
    IHomeNativeSessionHostOriginalActorVerifier CreateOriginalHomeHostVerifier();
}
