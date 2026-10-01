using Haven.Core;
using Haven.Core.Forms;

namespace Haven.Application;

public sealed partial class FormResponseSessionService
{
    internal async Task<FormDataWriteJournalResult> QueuePreparedDataWriteAsync(FormDataRecordUpdatePlan plan, CancellationToken token)
    {
        var (context, code) = await ReadDataContextAsync(plan.FormID, plan.ResponseID, token).ConfigureAwait(false);
        if (context is null) return new(false, code, null);
        if (context.Response.Revision != plan.ResponseRevision || context.Response.FormVersionID != plan.FormVersionID)
            return new(false, "RevisionConflict", null);
        var intent = plan.Intent;
        var canonical = FormDataRecordUpdateProjection.CaptureIntent(context.Project, context.Response, intent.StoreID,
            intent.WorkbookID, intent.Version, intent.RevisionID, intent.TableID, intent.RecordID, intent.OperationID, context.RootID);
        if (canonical.Intent.PayloadSHA256 != intent.PayloadSHA256 || !canonical.BindingIDs.Order().SequenceEqual(plan.BindingIDs.Order()))
            return new(false, "DataBindingConflict", null);
        var existing = context.Entry.DataWrites ?? [];
        var previous = existing.SingleOrDefault(item => item.Operation.OperationID == intent.OperationID);
        if (previous is not null)
            return previous.Operation.PayloadSHA256 == intent.PayloadSHA256
                ? new(true, null, CaptureAttempt(previous, context.Response, context.RootID)) : new(false, "DataOperationConflict", null);
        if (existing.Count >= 256) return new(false, "DataWriteCapacityReached", null);
        var attempt = new FormDataWriteAttempt(DataRecordUpdateSnapshot.Capture(intent), FormsDataWriteStatus.Pending, null, 1);
        var entry = context.Entry with { DataWrites = existing.Append(attempt).ToArray() };
        var state = context.State with { Responses = context.State.Responses.Select(item => item == context.Entry ? entry : item).ToArray() };
        var committed = await CommitAsync(context.Publication, "forms.response.data.prepare", context.Actor, context.RootID,
            context.Json, state, context.Response, token).ConfigureAwait(false);
        return committed.Success ? new(true, null, CaptureAttempt(attempt, context.Response, context.RootID)) : new(false, committed.Code, null);
    }

    public async Task<FormDataWriteJournalReadResult> ReadDataWritesAsync(Guid formID, Guid responseID, CancellationToken token = default)
    {
        var (context, code) = await ReadDataContextAsync(formID, responseID, token).ConfigureAwait(false);
        return context is null ? new(false, code, []) : new(true, null,
            Array.AsReadOnly((context.Entry.DataWrites ?? []).Select(item => CaptureAttempt(item, context.Response, context.RootID)).ToArray()));
    }

    internal async Task<FormDataWriteJournalResult> ReconcileDataWriteAsync(Guid formID, Guid responseID, Guid operationID,
        IDataRecordMutationReceiptSource receipts, CancellationToken token)
    {
        var (context, code) = await ReadDataContextAsync(formID, responseID, token).ConfigureAwait(false);
        if (context is null) return new(false, code, null);
        var attempt = (context.Entry.DataWrites ?? []).SingleOrDefault(item => item.Operation.OperationID == operationID);
        if (attempt is null) return new(false, "DataOperationNotFound", null);
        if (attempt.Status == FormsDataWriteStatus.Succeeded) return new(true, null, CaptureAttempt(attempt, context.Response, context.RootID));
        var intent = attempt.Operation.Restore();
        var receipt = await receipts.ReadAsync(intent, context.Actor, token).ConfigureAwait(false);
        if (receipt is null) return new(true, "DataWritePending", CaptureAttempt(attempt, context.Response, context.RootID));
        var completed = CaptureAttempt(attempt with { Status = FormsDataWriteStatus.Succeeded, Receipt = receipt,
            Revision = checked(attempt.Revision + 1) }, context.Response, context.RootID);
        var entry = context.Entry with { DataWrites = context.Entry.DataWrites!.Select(item => item == attempt ? completed : item).ToArray() };
        var state = context.State with { Responses = context.State.Responses.Select(item => item == context.Entry ? entry : item).ToArray() };
        var committed = await CommitAsync(context.Publication, "forms.response.data.reconcile", context.Actor, context.RootID,
            context.Json, state, context.Response, token).ConfigureAwait(false);
        return committed.Success ? new(true, null, completed) : new(false, committed.Code, null);
    }

    private static FormDataWriteAttempt CaptureAttempt(FormDataWriteAttempt item, FormResponse response, Guid sourceStoreID) =>
        FormDataWriteAttemptValidation.Capture(item, response.FormID, response.ResponseID, response.FormVersionID, response.Revision, sourceStoreID);

    private async Task<(DataContext? Context, string? Code)> ReadDataContextAsync(Guid formID, Guid responseID, CancellationToken token)
    {
        var retained = await ReadSubmittedAsync(formID, responseID, token).ConfigureAwait(false);
        if (!retained.Success || retained.Actor is null) return (null, retained.Code ?? "PermissionDenied");
        var publication = await publications.ReadAsync(formID, token).ConfigureAwait(false);
        if (!publication.Success) return (null, publication.Code);
        var (state, json, rootID) = await LoadAsync(formID, token).ConfigureAwait(false);
        if (rootID != retained.StoreID) return (null, "PermissionDenied");
        var entry = state.Responses.SingleOrDefault(item => item.Checkpoint.ResponseID == responseID);
        if (entry is null || entry.Owner != Owner.From(retained.Actor)) return (null, "ResponseUnavailable");
        if (entry.Checkpoint.Revision != retained.Response!.Revision || entry.Checkpoint.FormVersionID != retained.Response.FormVersionID)
            return (null, "RevisionConflict");
        if (await AuthorizeAsync(rootID, publication.Publication!, "forms.response.read", token).ConfigureAwait(false) != retained.Actor)
            return (null, "PermissionDenied");
        foreach (var attempt in entry.DataWrites ?? [])
        {
            var captured = CaptureAttempt(attempt, retained.Response, rootID).Operation.Restore();
            var canonical = FormDataRecordUpdateProjection.CaptureIntent(retained.Project!, retained.Response, captured.StoreID,
                captured.WorkbookID, captured.Version, captured.RevisionID, captured.TableID, captured.RecordID, captured.OperationID, rootID);
            if (canonical.Intent.PayloadSHA256 != captured.PayloadSHA256)
                throw new InvalidDataException("Stored Data operation differs from the immutable submitted answers.");
        }
        return (new(publication.Publication!, state, json, rootID, entry, retained.Project!, retained.Response, retained.Actor), null);
    }
    private sealed record DataContext(FormPublication Publication, State State, string? Json, Guid RootID, Entry Entry,
        FormProject Project, FormResponse Response, AuthenticatedResourceActor Actor);
}
