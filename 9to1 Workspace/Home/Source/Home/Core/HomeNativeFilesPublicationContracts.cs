using Haven.Application;
namespace HavenOS.Home.Core;

/// <summary>Retains actual original authority transactions through the physical reply write.
/// A check observes those retained owners; it must not reacquire Home, Files, Context.Gate
/// or another owner lock. A previously successful asynchronous check is not this guard.</summary>
public interface IHomeNativeFilesPublicationGuard : IAsyncDisposable
{
    bool IsHeld { get; }
    ValueTask DemandOriginalCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Trusted installed verifier extension, never supplied by a wire frame.
/// Acquire must retain the SAME observed process, installed tuple and actor authority until
/// Dispose, independently of later owner awaits. Guard checks may not reenter Home/Files.
/// Unsupported physical installation/launch authorities return null.</summary>
public interface IHomeNativeFilesInstalledPublicationVerifier : IHomeNativeInstalledPeerOriginalActorVerifier
{
    ValueTask<IHomeNativeFilesPublicationGuard?> AcquireOriginalFilesPublicationAsync(
        HomeNativeObservedPeer originalObservedPeer, AuthenticatedResourceActor originalActor,
        HomeNativeInstalledPeer originalInstalledPeer, CancellationToken originalLifetime,
        CancellationToken cancellationToken);
}

/// <summary>The SAME registered Home-only Files owner, with a genuine retained read transaction.
/// Missing actual transactions must remain unavailable. Presence of this interface is no grant.</summary>
public interface IHomeNativeFilesPublicationOwner : IHomeNativeFilesDomainOwner
{
    bool SupportsOriginalPublication { get; }
    ValueTask<IHomeNativeFilesPublicationGuard?> AcquireOriginalReplyPublicationAsync(
        HomeNativeFilesOriginalConnection originalConnection, HomeNativeFilesReply originalReply,
        CancellationToken cancellationToken);
}
