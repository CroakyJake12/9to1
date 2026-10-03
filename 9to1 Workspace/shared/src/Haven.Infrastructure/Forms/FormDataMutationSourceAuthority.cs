using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Authenticates Forms provenance against the actual retained response and published version.
/// Final Data admission observes the source root and raw Home receipt without reentering Data evidence.
/// Forms, Data and Home remain separate stores; this is not an atomic cross-store revocation protocol.</summary>
public sealed class FormDataMutationSourceAuthority(FormDataRecordPreparationService preparation,
    FormResponseSessionService responses, IResourceStoreOwnershipAuthority ownership,
    IAuthenticatedResourceActorSource actors) : IDataRecordMutationOriginAuthority
{
    public async ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(DataRecordUpdateIntent intent,
        AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken)
    {
        if (intent.Origin is not { } origin || ownership is not IResourceStoreOwnershipReceiptAuthority receipts
            || await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor) return null;
        var prepared = await preparation.PrepareAsync(origin.FormID, origin.ResponseID, intent.WorkbookID, intent.TableID,
            intent.RecordID, intent.OperationID, cancellationToken).ConfigureAwait(false);
        if (!prepared.Success || prepared.Plan!.Intent.PayloadSHA256 != intent.PayloadSHA256) return null;
        var retained = await responses.ReadSubmittedAsync(origin.FormID, origin.ResponseID, cancellationToken).ConfigureAwait(false);
        if (!retained.Success || retained.Actor != expectedActor || retained.StoreID == Guid.Empty || origin.SourceStoreID != retained.StoreID
            || retained.Response!.FormVersionID != origin.FormVersionID || retained.Response.Revision != origin.ResponseRevision) return null;
        var binding = await ownership.GetVerifiedAsync("forms", retained.StoreID.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (binding?.Receipt is null || binding.ResourceKind != "forms" || binding.StoreId != retained.StoreID.ToString("D")
            || binding.ProfileId != expectedActor.ProfileId || !await receipts.IsCurrentAsync(binding, expectedActor, cancellationToken).ConfigureAwait(false)) return null;
        return new Admission(intent, retained.StoreID, responses, receipts, binding, expectedActor);
    }

    private sealed class Admission(DataRecordUpdateIntent intent, Guid sourceStoreID, FormResponseSessionService responses,
        IResourceStoreOwnershipReceiptAuthority receipts, VerifiedResourceStoreOwnership binding,
        AuthenticatedResourceActor actor) : IDataWorkbookCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(DataWorkbookCommitContext context, CancellationToken cancellationToken)
        {
            if (context.StoreID != intent.StoreID || context.WorkbookID != intent.WorkbookID
                || context.ExpectedVersion != intent.Version || context.ExpectedRevisionID != intent.RevisionID) return false;
            var source = await responses.GetSourceStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
            return source.SchemaVersion == 1 && source.StoreId == sourceStoreID
                && await receipts.IsCurrentAsync(binding, actor, cancellationToken).ConfigureAwait(false);
        }
    }
}
