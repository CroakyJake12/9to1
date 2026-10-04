using System.Collections.Frozen;
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

/// <summary>Opaque HOME-only binding issued by the accepted private Session after acquiring
/// its SAME Files owner transaction. Public fields are observations, not grants or mutable
/// store snapshots. No wire constructor or replacement owner/actor/reply exists. A supported
/// installed adapter must obtain genuine factory-issued retained state from the original
/// owner guard; this object's existence or IsHeld alone cannot establish installed authority.</summary>
public sealed class HomeNativeFilesOriginalPublicationContext
{
    private readonly HomeNativeCoreApiSessions _issuer;
    private readonly object _originalSessionContext;
    private int _retired;
    public HomeNativeFilesOriginalConnection OriginalConnection { get; }
    public HomeNativeFilesReply OriginalReply { get; }
    public IHomeNativeFilesPublicationGuard OriginalOwnerGuard { get; }
    public HomeNativeObservedPeer OriginalObservedPeer { get; }
    public AuthenticatedResourceActor OriginalActor => OriginalConnection.OriginalActor;
    public HomeNativeInstalledPeer OriginalInstalledPeer { get; }
    public CancellationToken OriginalLifetime => OriginalConnection.OriginalLifetime;

    internal HomeNativeFilesOriginalPublicationContext(HomeNativeCoreApiSessions issuer,
        object originalSessionContext, HomeNativeFilesOriginalConnection originalConnection,
        HomeNativeFilesReply originalReply, IHomeNativeFilesPublicationGuard originalOwnerGuard,
        HomeNativeObservedPeer originalObservedPeer, HomeNativeInstalledPeer originalInstalledPeer)
    {
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _originalSessionContext = originalSessionContext ?? throw new ArgumentNullException(nameof(originalSessionContext));
        OriginalConnection = originalConnection ?? throw new ArgumentNullException(nameof(originalConnection));
        OriginalReply = originalReply ?? throw new ArgumentNullException(nameof(originalReply));
        OriginalOwnerGuard = originalOwnerGuard ?? throw new ArgumentNullException(nameof(originalOwnerGuard));
        OriginalObservedPeer = originalObservedPeer ?? throw new ArgumentNullException(nameof(originalObservedPeer));
        ArgumentNullException.ThrowIfNull(originalInstalledPeer);
        OriginalInstalledPeer = originalInstalledPeer with
            { AllowedServiceIds = originalInstalledPeer.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal),
                Roles = (originalInstalledPeer.Roles ?? FrozenSet<string>.Empty).ToFrozenSet(StringComparer.Ordinal) };
        if (OriginalConnection.InstalledAppId != OriginalInstalledPeer.AppId ||
            OriginalConnection.InstalledApplicationId != OriginalInstalledPeer.InstalledApplicationId)
            throw new UnauthorizedAccessException("Retain the SAME originally installed Files connection.");
        DemandOriginalOwnerTransaction();
    }

    // This is a deny-only lifetime/reference fence. It does not replace genuine held actor,
    // package/receipt/launch checks, nor reenter Home/Files to manufacture a current witness.
    public void DemandOriginalOwnerTransaction()
    {
        if (Volatile.Read(ref _retired) != 0 ||
            !OriginalConnection.IsOriginalIssuer(_issuer) ||
            !OriginalConnection.IsOriginalContext(_originalSessionContext) ||
            !OriginalOwnerGuard.IsHeld)
            throw new UnauthorizedAccessException("The original Files publication transaction retired.");
        OriginalLifetime.ThrowIfCancellationRequested();
    }
    internal bool IsOriginalBinding(HomeNativeCoreApiSessions issuer, object context,
        HomeNativeFilesOriginalConnection connection, HomeNativeFilesReply reply,
        IHomeNativeFilesPublicationGuard ownerGuard) =>
        ReferenceEquals(_issuer, issuer) && ReferenceEquals(_originalSessionContext, context) &&
        ReferenceEquals(OriginalConnection, connection) && ReferenceEquals(OriginalReply, reply) &&
        ReferenceEquals(OriginalOwnerGuard, ownerGuard);
    internal void Retire() => Interlocked.Exchange(ref _retired, 1);
}

/// <summary>Trusted installed verifier extension, never supplied by a wire frame.
/// Acquire follows the SAME retained owner transaction: Files, claimed completion gate if
/// applicable, then profile/device state and authentic receipt/launch/native transactions.
/// It must consume genuinely held raw
/// state through a supported owner-issued capability rather than reacquire Home/Files.
/// Unsupported aliases, writer order or physical installation/launch authority return null.</summary>
public interface IHomeNativeFilesInstalledPublicationVerifier : IHomeNativeInstalledPeerOriginalActorVerifier
{
    ValueTask<IHomeNativeFilesPublicationGuard?> AcquireOriginalFilesPublicationAsync(
        HomeNativeFilesOriginalPublicationContext originalContext, CancellationToken cancellationToken);
}

/// <summary>The SAME registered Home-only Files owner, with a genuine retained read transaction.
/// Missing actual transactions must remain unavailable. Presence of this interface is no grant.
/// Acquire Files first, then any original claimed-resource completion gate BEFORE the
/// supported original profile/device state in established writer
/// order. Aliased stores require genuine owner identity/deduplication or refusal. A guard may not
/// borrow a mutable snapshot as installed authority. Installed guard releases before this guard.</summary>
public interface IHomeNativeFilesPublicationOwner : IHomeNativeFilesDomainOwner
{
    bool SupportsOriginalPublication { get; }
    ValueTask<IHomeNativeFilesPublicationGuard?> AcquireOriginalReplyPublicationAsync(
        HomeNativeFilesOriginalConnection originalConnection, HomeNativeFilesReply originalReply,
        CancellationToken cancellationToken);
}
