using Dulche.Runtime.Agents;
using Haven.Application;
using NineToOne.Dulche.Den;

namespace HavenOS.Home.Core;

/// <summary>Owning retirement only. Studio authoring/selection cannot create an execution host,
/// acquire its lease or issue tokens. An authenticated client must retain its original remote context.</summary>
public interface IHomeAgentExecutionHost
{
    Task RetireCurrentContextAsync();
    Task CloseAndDrainAsync();
}

/// <summary>Resolves only an already configured canonical owning Den. No child path or caller Store is input.</summary>
public interface IHomeAgentCurrentDenSource
{
    ValueTask<HomePersonalDenFactory?> ResolveCurrentAsync(string canonicalDenId,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken = default);
}

/// <summary>Trusted acyclic Infrastructure composition; never an invocation or execution authority.</summary>
public interface IHomeAgentExecutionSessionComposer
{
    IAgentExecutionStateStore CreateRunReader(DulcheDen sameBoundDen, string originalNamespace);
    IDenAgentExecutionSessionFactory Compose(HomeAgentExecutionAdmissions issuer,
        HomeAgentExecutionAdmissions.OriginalInvocation sameAdmission, DenAgentReference reference);
}

/// <summary>Existing registered provider inventory is the source. The host borrows the original
/// Store and never creates/opens/disposes a second Store, imports ownership or grants Execute.</summary>
public sealed class HomeRegisteredCurrentDenSource(IEnumerable<HomeDenStoreEvidenceProvider> originalProviders,
    IResourceStoreOwnershipReceiptAuthority ownership, IAuthenticatedResourceActorSource actors) : IHomeAgentCurrentDenSource
{
    public async ValueTask<HomePersonalDenFactory?> ResolveCurrentAsync(string canonicalDenId,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(canonicalDenId) || await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != originalActor)
            return null;
        var matches = originalProviders.Where(provider => provider.Store.Manifest.DenId == canonicalDenId).Take(2).ToArray();
        if (matches.Length != 1) return null;
        var factory = new HomePersonalDenFactory(matches[0], ownership, actors);
        var bound = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (bound.DenId != canonicalDenId || bound.Actor != originalActor || !ReferenceEquals(bound.Den.Store, matches[0].Store)) return null;
        var current = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return current.DenId == bound.DenId && current.Actor == bound.Actor && ReferenceEquals(current.Den.Store, bound.Den.Store) &&
            await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == originalActor ? factory : null;
    }
}
