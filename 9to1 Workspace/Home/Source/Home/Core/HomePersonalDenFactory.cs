using Haven.Application;
using NineToOne.Dulche.Den;

namespace HavenOS.Home.Core;

/// <summary>Home-hosted observation of one actual Den lifetime. Creation is performed here, never asserted
/// by a caller flag. Opening an existing store always requires its existing binding or explicit Home import.</summary>
public sealed class HomeDenStoreEvidenceProvider : IHomeLocalStoreEvidenceProvider, IAsyncDisposable
{
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly AuthenticatedResourceActor _actor;
    private readonly bool _created;
    private bool _disposed;
    private HomeDenStoreEvidenceProvider(DenStore store, IAuthenticatedResourceActorSource actors,
        AuthenticatedResourceActor actor, bool created)
    { Store = store; _actors = actors; _actor = actor; _created = created; }
    public string ResourceKind => "den";
    public DenStore Store { get; }
    internal bool IsOriginalAlive => !Volatile.Read(ref _disposed);

    public static Task<HomeDenStoreEvidenceProvider> CreateAsync(string actualConfiguredRoot,
        IAuthenticatedResourceActorSource actors, CancellationToken ct = default) => OpenCoreAsync(actualConfiguredRoot, actors, true, ct);
    public static Task<HomeDenStoreEvidenceProvider> OpenAsync(string actualConfiguredRoot,
        IAuthenticatedResourceActorSource actors, CancellationToken ct = default) => OpenCoreAsync(actualConfiguredRoot, actors, false, ct);

    private static async Task<HomeDenStoreEvidenceProvider> OpenCoreAsync(string root,
        IAuthenticatedResourceActorSource actors, bool create, CancellationToken ct)
    {
        var actor = await actors.GetCurrentAsync(ct) ?? throw new UnauthorizedAccessException("A current personal Home actor is required.");
        if (actor.AccountId is not null || actor.OrganisationId is not null) throw new UnauthorizedAccessException("This Den host supports personal local profiles only.");
        var store = create ? await DenStore.CreateAsync(root, [new("personal", "personal")], ct) : await DenStore.OpenAsync(root, ct);
        try
        {
            if (await actors.GetCurrentAsync(ct) != actor)
                throw new UnauthorizedAccessException("The Home actor changed while opening the Den.");
            return new(store, actors, actor, create);
        }
        catch { await store.DisposeAsync(); throw; }
    }

    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken ct)
    {
        if (_disposed || storeId != Store.Manifest.DenId || await _actors.GetCurrentAsync(ct) != _actor) return null;
        var observed = await Store.ObserveOwnershipAsync(ct);
        if (await _actors.GetCurrentAsync(ct) != _actor || observed.DenId != storeId) return null;
        return new(ResourceKind, observed.DenId, observed.ContentRevision, _created, observed.IsEmpty, true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await Store.DisposeAsync();
    }
}

public sealed record HomePersonalDenSession(AuthenticatedResourceActor Actor, string DenId, DulcheDen Den);

/// <summary>Uses an existing canonical Home binding; never grants ownership, approves import, creates an
/// independent store, or grants Execute/Admin. The host owns/disposes the supplied provider lifetime.</summary>
public sealed class HomePersonalDenFactory(HomeDenStoreEvidenceProvider provider,
    IResourceStoreOwnershipReceiptAuthority ownership, IAuthenticatedResourceActorSource actors)
{
    public async Task<HomePersonalDenSession> OpenAsync(CancellationToken ct = default)
    {
        var actor = await actors.GetCurrentAsync(ct) ?? throw new UnauthorizedAccessException("A current Home actor is required.");
        if (actor.AccountId is not null || actor.OrganisationId is not null) throw new UnauthorizedAccessException("Personal Den access cannot infer account or organisation permissions.");
        var current = await provider.Store.ReadAuthoritySnapshotAsync(ct);
        var binding = await ownership.GetVerifiedAsync("den", current.DenId, ct);
        if (binding is null || binding.Receipt is null || binding.ResourceKind != "den" || binding.StoreId != current.DenId ||
            binding.ProfileId != actor.ProfileId || !await ownership.IsCurrentAsync(binding, actor, ct))
            throw new UnauthorizedAccessException("This Den requires a current verified Home ownership binding.");
        var policy = new PersonalPolicy(this, provider.Store, ownership, actor, binding);
        var original = new HomePersonalDenSession(actor, current.DenId, new DulcheDen(provider.Store, policy, actor.ActorId));
        policy.BindOriginal(original);
        if (!await IsCurrentOriginalAsync(original, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original personal Den authority changed while opening it.");
        return original;
    }

    /// <summary>Rechecks this factory's exact issued session without observing ownership under a Den writer lease.
    /// The initial Open still acquires genuine store evidence; retained calls use its SAME raw Home receipt.</summary>
    public async ValueTask<bool> IsCurrentOriginalAsync(HomePersonalDenSession original, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ct.ThrowIfCancellationRequested();
        if (!provider.IsOriginalAlive || original.Den.AccessPolicy is not PersonalPolicy policy ||
            !policy.IsOriginal(this, original, provider.Store)) return false;
        if (await actors.GetCurrentAsync(ct).ConfigureAwait(false) != original.Actor ||
            !await policy.IsBindingCurrentAsync(ct).ConfigureAwait(false)) return false;
        // ReadAuthoritySnapshotAsync deliberately does not acquire the writer lease held by Save.
        var current = await provider.Store.ReadAuthoritySnapshotAsync(ct).ConfigureAwait(false);
        if (current.DenId != original.DenId || !provider.IsOriginalAlive ||
            !await policy.IsBindingCurrentAsync(ct).ConfigureAwait(false) ||
            await actors.GetCurrentAsync(ct).ConfigureAwait(false) != original.Actor) return false;
        ct.ThrowIfCancellationRequested();
        return provider.IsOriginalAlive && policy.IsOriginal(this, original, provider.Store);
    }

    private sealed class PersonalPolicy(HomePersonalDenFactory issuer, DenStore store, IResourceStoreOwnershipReceiptAuthority ownership,
        AuthenticatedResourceActor actor, VerifiedResourceStoreOwnership binding) : IDenAccessPolicy
    {
        private HomePersonalDenSession? _original;
        public void BindOriginal(HomePersonalDenSession original)
        {
            if (_original is not null) throw new InvalidOperationException("The personal Den policy already has its original session.");
            _original = original;
        }
        public bool IsOriginal(HomePersonalDenFactory expectedIssuer, HomePersonalDenSession original, DenStore expectedStore) =>
            ReferenceEquals(issuer, expectedIssuer) && ReferenceEquals(_original, original) &&
            ReferenceEquals(store, expectedStore) && ReferenceEquals(original.Den.Store, store) &&
            ReferenceEquals(original.Den.AccessPolicy, this) && original.Den.PrincipalId == actor.ActorId &&
            original.Actor == actor && original.DenId == binding.StoreId && binding.ResourceKind == "den" &&
            binding.ProfileId == actor.ProfileId && actor.AccountId is null && actor.OrganisationId is null;
        public ValueTask<bool> IsBindingCurrentAsync(CancellationToken ct) => ownership.IsCurrentAsync(binding, actor, ct);

        public async ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
            DenPermission permission, CancellationToken cancellationToken = default)
        {
            if (principalId != actor.ActorId || permission is not (DenPermission.Read or DenPermission.Write) ||
                string.IsNullOrWhiteSpace(objectId) || !await ownership.IsCurrentAsync(binding, actor, cancellationToken)) return false;
            // No Den writer lease acquisition: Save invokes this callback while it already owns that lease.
            var current = await store.ReadAuthoritySnapshotAsync(cancellationToken);
            var ns = current.Namespaces.SingleOrDefault(item => item.Id == namespaceId);
            return current.DenId == binding.StoreId && ns is { Kind: "personal", Shared: false } &&
                await ownership.IsCurrentAsync(binding, actor, cancellationToken);
        }
    }
}
