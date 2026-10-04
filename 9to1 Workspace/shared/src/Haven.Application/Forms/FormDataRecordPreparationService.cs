namespace Haven.Application;

public sealed record FormDataRecordPreparationResult(bool Success, string? Code, FormDataRecordUpdatePlan? Plan);

/// <summary>Resolves both canonical stores before preparing a reviewed Data edit. Respondents supply
/// identities only, never a replacement response/project. Preparation grants no execution capability.</summary>
public sealed class FormDataRecordPreparationService(FormResponseSessionService responses, IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority dataAuthority, IAuthenticatedResourceActorSource actors)
{
    public async Task<FormDataRecordPreparationResult> PrepareAsync(Guid formID, Guid responseID, Guid workbookID,
        Guid tableID, Guid recordID, Guid operationID, CancellationToken cancellationToken = default)
    {
        if (formID == Guid.Empty || responseID == Guid.Empty || workbookID == Guid.Empty || tableID == Guid.Empty
            || recordID == Guid.Empty || operationID == Guid.Empty) return new(false, "InvalidArgument", null);
        var retained = await responses.ReadSubmittedAsync(formID, responseID, cancellationToken).ConfigureAwait(false);
        if (!retained.Success) return new(false, retained.Code, null);
        var actor = retained.Actor;
        if (actor is null || workbooks is not IDataGuardedWorkbookRepository guarded) return new(false, "PermissionDenied", null);
        // This uses the actual repository's coupled canonical identity; it may initialize that existing root identity.
        var identity = await guarded.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        var workbook = await workbooks.LoadAsync(workbookID, cancellationToken).ConfigureAwait(false);
        if (workbook is null) return new(false, "PermissionDenied", null);
        var admission = await dataAuthority.CaptureAsync(identity.StoreId, workbook.Id, workbook.Version, workbook.RevisionId,
            DataRecordUpdateIntent.ActionId, actor, cancellationToken).ConfigureAwait(false);
        if (admission is null || await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor)
            return new(false, "PermissionDenied", null);
        FormDataRecordUpdatePlan plan;
        try
        {
            plan = FormDataRecordUpdateProjection.Prepare(retained.Project!, retained.Response!, identity.StoreId,
                workbook, tableID, recordID, operationID, retained.StoreID);
        }
        catch (NotSupportedException) { return new(false, "DataBindingCapabilityUnavailable", null); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException or KeyNotFoundException)
        { return new(false, "DataBindingConflict", null); }
        var refreshed = await responses.ReadSubmittedAsync(formID, responseID, cancellationToken).ConfigureAwait(false);
        if (!refreshed.Success || refreshed.Actor != actor || refreshed.Response!.Revision != retained.Response!.Revision
            || refreshed.Response.FormVersionID != retained.Response.FormVersionID)
            return new(false, "PermissionDenied", null);
        var currentIdentity = await guarded.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (!await admission.CheckAsync(new(currentIdentity.StoreId, workbook.Id, workbook.Version, workbook.RevisionId,
            DataWorkbookCommitPhase.Publication), cancellationToken).ConfigureAwait(false)) return new(false, "PermissionDenied", null);
        return new(true, null, plan);
    }
}
