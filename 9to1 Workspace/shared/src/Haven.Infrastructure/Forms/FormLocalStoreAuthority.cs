using System.Globalization;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Actual durable Forms settings evidence for Home's "forms" kind. Observation only: it never
/// seeds a store identity, binds ownership or grants content access.</summary>
public sealed class FormLocalStoreEvidenceProvider(IVersionedSettingsStore settings,
    IResourceStoreIdentitySource identities) : IHomeLocalStoreEvidenceProvider
{
    public string ResourceKind => "forms";

    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(storeId, out var id) || id == Guid.Empty) return null;
        var identity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId != id) return null;
        // The successful durable read is the actual accessibility proof for this store. A store that
        // cannot be read is reported as unknown evidence, never as accessible evidence.
        SettingsExportManifest manifest;
        try { manifest = await settings.ExportAsync(cancellationToken).ConfigureAwait(false); }
        catch (IOException) { return null; }
        if (manifest.StoreIdentity is not { SchemaVersion: 1 } root || root.StoreId != id) return null;
        return new(ResourceKind, id.ToString("D"), manifest.Version.ToString(CultureInfo.InvariantCulture),
            identity.NewlyCreated, manifest.Settings.Count == 0, true);
    }
}

/// <summary>Native personal Forms authority for one canonical settings root. It intersects the actual
/// durable store identity, the ambient local OS profile and Home's verified "forms" ownership binding.
/// A caller-supplied FormID, revision or action ID constrains the destination; it never grants authority,
/// and account or organisation principals are refused because this boundary is personal and device-local.
/// Nothing here reads the settings store: the captured admission runs while that lease is held.</summary>
public sealed class FormLocalStoreAuthority : IFormStoreCommitAuthority
{
    private const string ResourceKind = "forms";
    private readonly IResourceStoreIdentitySource _identities;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly IResourceStoreOwnershipAuthority _ownership;

    public FormLocalStoreAuthority(IVersionedSettingsStore settings, IResourceStoreIdentitySource identities,
        IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipAuthority ownership)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(ownership);
        // One canonical settings root only: a second identity source cannot re-point the durable store
        // that the final guarded compare/exchange will write into.
        if (!ReferenceEquals(settings, identities))
            throw new ArgumentException("The Forms authority requires the actual same settings store and identity source.", nameof(identities));
        _identities = identities; _actors = actors; _ownership = ownership;
    }

    public async ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID,
        CancellationToken cancellationToken)
    {
        if (!ValidRequest(storeID, formID, revision, actionID)) return false;
        var actor = await _actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!IsLocalPersonalActor(actor)) return false;
        if (!await CurrentRootAsync(storeID, cancellationToken).ConfigureAwait(false)) return false;
        var binding = await _ownership.GetVerifiedAsync(ResourceKind, storeID.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (!MatchesOwner(binding, storeID, actor)) return false;
        return await _actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == actor;
    }

    public async ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID,
        long revision, string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken cancellationToken)
    {
        if (!ValidRequest(storeID, formID, revision, actionID) || !IsLocalPersonalActor(expectedActor)) return null;
        if (await _actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor) return null;
        if (!await CurrentRootAsync(storeID, cancellationToken).ConfigureAwait(false)) return null;
        var captured = await _ownership.GetVerifiedAsync(ResourceKind, storeID.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (!MatchesOwner(captured, storeID, expectedActor)) return null;
        var receipts = _ownership as IResourceStoreOwnershipReceiptAuthority;
        if (receipts is not null && captured!.Receipt is null) return null;
        return new FormStoreCommitAdmission(storeID, expectedActor!, _actors, _ownership, captured!, receipts);
    }

    private async ValueTask<bool> CurrentRootAsync(Guid storeID, CancellationToken cancellationToken)
    {
        var identity = await _identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        return identity.SchemaVersion == 1 && identity.StoreId == storeID;
    }

    private static bool ValidRequest(Guid storeID, Guid formID, long revision, string actionID) =>
        storeID != Guid.Empty && formID != Guid.Empty && revision >= 0 && !string.IsNullOrWhiteSpace(actionID);

    private static bool IsLocalPersonalActor(AuthenticatedResourceActor? actor) =>
        actor is not null && actor.AccountId is null && actor.OrganisationId is null &&
        !string.IsNullOrWhiteSpace(actor.ActorId) && !string.IsNullOrWhiteSpace(actor.ProfileId) &&
        !string.IsNullOrWhiteSpace(actor.AuthenticationRevision);

    private static bool MatchesOwner(VerifiedResourceStoreOwnership? binding, Guid storeID, AuthenticatedResourceActor? actor) =>
        actor is not null && binding is not null && binding.ResourceKind == ResourceKind && binding.StoreId == storeID.ToString("D") &&
        binding.ProfileId == actor.ProfileId;

    /// <summary>Rechecks the captured authority only. Independent state (the Home binding and the current
    /// actor) is consulted under the settings lease; this admission never reads the settings store.</summary>
    private sealed class FormStoreCommitAdmission(Guid storeID, AuthenticatedResourceActor expectedActor,
        IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipAuthority ownership,
        VerifiedResourceStoreOwnership captured, IResourceStoreOwnershipReceiptAuthority? receipts) : ISettingsCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken)
        {
            if (context.StoreIdentity.SchemaVersion != 1 || context.StoreIdentity.StoreId != storeID) return false;
            if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor) return false;
            if (receipts is not null)
            {
                if (!await receipts.IsCurrentAsync(captured, expectedActor, cancellationToken).ConfigureAwait(false)) return false;
            }
            else
            {
                var key = storeID.ToString("D");
                var current = await ownership.GetVerifiedAsync(ResourceKind, key, cancellationToken).ConfigureAwait(false);
                if (current is null || current.ResourceKind != ResourceKind || current.StoreId != key ||
                    current.ProfileId != expectedActor.ProfileId) return false;
            }
            return await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == expectedActor;
        }
    }
}