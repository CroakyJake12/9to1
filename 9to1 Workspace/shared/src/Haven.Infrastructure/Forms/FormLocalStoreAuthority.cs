using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core.Forms;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Observed canonical Forms domain in the actual durable settings root. Discovery supplies
/// evidence only; it never creates a Home ownership binding or imports existing content.</summary>
public sealed class FormLocalStoreEvidenceProvider(IVersionedSettingsStore settings,
    IResourceStoreIdentitySource identities) : IHomeLocalStoreEvidenceProvider
{
    public string ResourceKind => "forms";
    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken)
    {
        var identity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty || storeId != identity.StoreId.ToString("D")) return null;
        var exported = await settings.ExportAsync(cancellationToken).ConfigureAwait(false);
        if (exported.StoreIdentity is not { SchemaVersion: 1 } actual || actual.StoreId != identity.StoreId) return null;
        var entries = exported.Settings.Where(entry => entry.Key.StartsWith("forms.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        return new(ResourceKind, storeId, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entries))),
            identity.NewlyCreated, entries.Length == 0, true);
    }
}

/// <summary>Native personal Forms owner authority, backed by Home's explicit current store binding.
/// A project's declared public/invitation audience is not a grant. Shared/web respondents require
/// their separate trusted audience authority; this local port never infers account or organisation access.</summary>
public sealed class FormLocalStoreAuthority(IVersionedSettingsStore settings, IResourceStoreIdentitySource identities,
    IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipAuthority ownership) : IFormStoreCommitAuthority
{
    public async ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
        string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken cancellationToken)
    {
        if (ownership is not IResourceStoreOwnershipReceiptAuthority receipts
            || !await AuthorizeAsync(storeID, formID, revision, actionID, cancellationToken).ConfigureAwait(false)) return null;
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || expectedActor is not null && actor != expectedActor) return null;
        var verified = await ownership.GetVerifiedAsync("forms", storeID.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (verified?.Receipt is null || verified.ResourceKind != "forms" || verified.StoreId != storeID.ToString("D")
            || verified.ProfileId != actor.ProfileId
            || !await receipts.IsCurrentAsync(verified, actor, cancellationToken).ConfigureAwait(false)) return null;
        return new Admission(storeID, receipts, verified, actor);
    }

    // Home and Forms remain separate stores: this observes Home at lease admission and immediately
    // before Forms publication; it does not claim a cross-store atomic revocation transaction.
    private sealed class Admission(Guid storeID, IResourceStoreOwnershipReceiptAuthority receipts,
        VerifiedResourceStoreOwnership captured, AuthenticatedResourceActor actor) : ISettingsCommitAdmission
    {
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken) =>
            context.StoreIdentity.SchemaVersion == 1 && context.StoreIdentity.StoreId == storeID
                ? receipts.IsCurrentAsync(captured, actor, cancellationToken) : ValueTask.FromResult(false);
    }

    public async ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID, CancellationToken cancellationToken)
    {
        if (storeID == Guid.Empty || formID == Guid.Empty || revision < 0 || actionID is not
            ("forms.create" or "forms.read" or "forms.edit" or "forms.publish" or "forms.close"
            or "forms.response.create" or "forms.response.read" or "forms.response.answer" or "forms.response.advance" or "forms.response.submit" or "forms.response.data.prepare" or "forms.response.data.reconcile")) return false;
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId)
            || string.IsNullOrWhiteSpace(actor.AuthenticationRevision)
            || actor.AccountId is not null || actor.OrganisationId is not null) return false;
        var identity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId != storeID) return false;
        var verified = await ownership.GetVerifiedAsync("forms", storeID.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (verified is null || verified.ResourceKind != "forms" || verified.StoreId != storeID.ToString("D")
            || verified.ProfileId != actor.ProfileId) return false;
        var form = await settings.GetAsync<FormPublication>("forms.publication.v1." + formID.ToString("N"), cancellationToken).ConfigureAwait(false);
        if (actionID == "forms.create")
        {
            if (revision != 0 || form is not null) return false;
        }
        else
        {
            if (form is null || form.FormID != formID || form.Revision != revision) return false;
            FormPublicationValidation.Validate(form);
        }
        var currentIdentity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        return await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == actor
            && currentIdentity.SchemaVersion == 1 && currentIdentity.StoreId == storeID
            && await ownership.GetVerifiedAsync("forms", storeID.ToString("D"), cancellationToken).ConfigureAwait(false) == verified;
    }
}
