using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Evidence from the actual workbook repository and its coupled canonical root.
/// Discovery initializes no ownership binding and never imports existing workbooks.</summary>
public sealed class DataLocalStoreEvidenceProvider(IDataWorkbookRepository workbooks) : IHomeLocalStoreEvidenceProvider
{
    public string ResourceKind => "data";
    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken)
    {
        if (workbooks is not IDataWorkbookStoreEvidenceSource actual) return null;
        try
        {
            var evidence = await actual.ReadStoreEvidenceAsync(cancellationToken).ConfigureAwait(false);
            return evidence is { Identity.SchemaVersion: 1 } && evidence.Identity.StoreId != Guid.Empty
                && storeId == evidence.Identity.StoreId.ToString("D")
                ? new(ResourceKind, storeId, evidence.Fingerprint, evidence.Identity.NewlyCreated, evidence.IsEmpty, true) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>Personal local owner admission. This is not an AI approval or a public respondent grant;
/// consequential invocation approval and exact mutation arguments belong to the owning operation.</summary>
public sealed class DataLocalStoreAuthority(IDataWorkbookRepository workbooks, IAuthenticatedResourceActorSource actors,
    IResourceStoreOwnershipAuthority ownership) : IDataWorkbookCommitAuthority
{
    public async ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(Guid storeID, Guid workbookID, int expectedVersion,
        Guid expectedRevisionID, string actionID, AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken)
    {
        if (workbooks is not IDataGuardedWorkbookRepository actual || ownership is not IResourceStoreOwnershipReceiptAuthority receipts
            || storeID == Guid.Empty || workbookID == Guid.Empty || expectedVersion < 0
            || expectedVersion == 0 && expectedRevisionID != Guid.Empty || expectedVersion > 0 && expectedRevisionID == Guid.Empty
            || actionID is not ("data.workbook.create" or "data.workbook.save" or "data.records.append" or "data.records.update" or "data.table.schema.update" or "data.relationships.edit" or "data.junctions.create" or "data.records.create")) return null;
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor != expectedActor || string.IsNullOrWhiteSpace(actor.ActorId)
            || string.IsNullOrWhiteSpace(actor.ProfileId) || string.IsNullOrWhiteSpace(actor.AuthenticationRevision)
            || actor.AccountId is not null || actor.OrganisationId is not null) return null;
        var identity = await actual.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId != storeID) return null;
        var verified = await ownership.GetVerifiedAsync("data", storeID.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (verified?.Receipt is null || verified.ResourceKind != "data" || verified.StoreId != storeID.ToString("D")
            || verified.ProfileId != actor.ProfileId) return null;
        var workbook = await workbooks.LoadAsync(workbookID, cancellationToken).ConfigureAwait(false);
        if (actionID == "data.workbook.create")
        { if (workbook is not null || expectedVersion != 0) return null; }
        else if (workbook is null || workbook.Version != expectedVersion || workbook.RevisionId != expectedRevisionID) return null;
        if (!await receipts.IsCurrentAsync(verified, actor, cancellationToken).ConfigureAwait(false)) return null;
        return new Admission(storeID, workbookID, expectedVersion, expectedRevisionID, receipts, verified, actor);
    }

    private sealed class Admission(Guid storeID, Guid workbookID, int version, Guid revision,
        IResourceStoreOwnershipReceiptAuthority receipts, VerifiedResourceStoreOwnership binding,
        AuthenticatedResourceActor actor) : IDataWorkbookCommitAdmission
    {
        public ValueTask<bool> CheckAsync(DataWorkbookCommitContext context, CancellationToken cancellationToken) =>
            context.StoreID == storeID && context.WorkbookID == workbookID && context.ExpectedVersion == version
                && context.ExpectedRevisionID == revision
                ? receipts.IsCurrentAsync(binding, actor, cancellationToken) : ValueTask.FromResult(false);
    }
}
