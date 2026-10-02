using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

/// <summary>Canonical Data workbook scope for the exact existing-record mutation. No Files path alias.</summary>
public sealed class DataWorkbookMutationAccessResolver(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => DataRecordUpdateIntent.ResourceKind;
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny() => new(false, "PermissionDenied", actor.ActorId, scope.Revision, actor.OrganisationId);
        if (workbooks is not IDataGuardedWorkbookRepository || actionId != DataRecordUpdateIntent.ActionId && actionId != DataTableSchemaUpdateIntent.ActionID && actionId != DataRelationshipUpdateIntent.ActionID && actionId != DataJunctionTableUpdateIntent.ActionID && actionId != DataRecordCreateIntent.ActionID
            || scope.Kind != ResourceKind || scope.Access != ResourceAccess.Write) return Deny();
        var parts = scope.Id.Split('/');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var storeID) || !Guid.TryParse(parts[1], out var workbookID)
            || !Guid.TryParse(scope.Revision, out var revisionID)) return Deny();
        try
        {
            var workbook = await workbooks.LoadAsync(workbookID, cancellationToken).ConfigureAwait(false);
            if (workbook is null || workbook.RevisionId != revisionID) return Deny();
            var admission = await authority.CaptureAsync(storeID, workbookID, workbook.Version, revisionID, actionId, actor, cancellationToken).ConfigureAwait(false);
            return admission is null ? Deny() : new(true, "Allowed", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return Deny(); }
    }
}

public sealed record DataRecordMutationResult(bool Committed, string Code, DataSaveResult? Saved, bool AuditRecorded)
{
    // False Committed alone is never evidence that an attempted publication had no effect.
    public bool OutcomeKnown { get; init; } = true;
}

/// <summary>Claims the exact one-use Home approval, updates existing canonical cells and carries the
/// claimed actor/root/receipt into durable Data admission. UI or Forms adapters cannot substitute a path.</summary>
public sealed class DataHomeRecordUpdateOperation(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority, HomeResourceOperationBroker home, IDataRecordMutationOriginAuthority? origins = null,
    IDataRecordMutationReceiptSource? recovery = null)
{
    private readonly ConditionalWeakTable<HomeResourceExecutionCapability, Attempt> _attempts = new();
    private readonly object _attemptLock = new();
    private sealed class Attempt(DataRecordUpdateIntent intent)
    {
        public DataRecordUpdateIntent Intent { get; } = intent;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public AuthenticatedResourceActor? Actor { get; set; }
        public IDataWorkbookCommitAdmission? SourceAdmission { get; set; }
        public bool SaveAttempted { get; set; }
        public DataRecordMutationResult? Known { get; set; }
        public HomeExecutionOutcome? Outcome { get; set; }
    }
    private static DataRecordMutationResult Unknown() => new(false, "DataOutcomeUnconfirmed", null, false) { OutcomeKnown = false };

    public async Task<DataRecordMutationResult> ExecuteAsync(DataRecordUpdateIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        Attempt attempt;
        lock (_attemptLock)
        {
            if (_attempts.TryGetValue(capability, out _))
                throw new UnauthorizedAccessException("This retained Data attempt may only be observed or finished, never dispatched again.");
            attempt = new(intent); _attempts.Add(capability, attempt); // Reserve BEFORE Claim/Save awaits.
        }
        await attempt.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HomeResourceClaimResult claim;
            try
            {
                claim = await home.ClaimExecutionObservedAsync(capability, DataRecordUpdateIntent.TargetAppId,
                    DataRecordUpdateIntent.ActionId, intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or OperationCanceledException)
            { return Unknown(); } // Invocation return lost: no replay or invented unconsumed evidence.
            if (claim.Disposition == HomeResourceClaimDisposition.InputNotConsumed)
            {
                // Only this invocation's exact pre-consumption rejection permits corrected input.
                lock (_attemptLock) _attempts.Remove(capability);
                throw new UnauthorizedAccessException("Home did not authorize this exact Data record edit.");
            }
            if (claim.Disposition == HomeResourceClaimDisposition.ConsumedRejected)
            {
                // Home owns the rejection audit. Retain the attempt: no second claim or Data write.
                attempt.Known = new(false, "PermissionDenied", null, false);
                return await FinishRejectedAsync(attempt, capability).ConfigureAwait(false);
            }
            if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor is null) return Unknown();
            attempt.Actor = claim.Actor;
            return await ExecuteClaimedAsync(attempt, capability, cancellationToken).ConfigureAwait(false);
        }
        finally { attempt.Gate.Release(); }
    }

    private async Task<DataRecordMutationResult> ExecuteClaimedAsync(Attempt attempt,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken)
    {
        var intent = attempt.Intent;
        var actor = attempt.Actor ?? throw new InvalidOperationException("Actual claimed Data actor required.");
        DataSaveResult saved;
        try
        {
            if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new NotSupportedException("Data guarded commit is unavailable.");
            var workbook = await workbooks.LoadAsync(intent.WorkbookID, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("WorkbookNotFound");
            if (workbook.Version != intent.Version || workbook.RevisionId != intent.RevisionID)
                throw new DataWorkbookRevisionConflictException(intent.WorkbookID, intent.Version, workbook.Version);
            var admission = await authority.CaptureAsync(intent.StoreID, intent.WorkbookID, intent.Version, intent.RevisionID,
                DataRecordUpdateIntent.ActionId, actor, cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Data owner admission was revoked.");
            if (intent.Origin is not null)
            {
                if (origins is null) throw new NotSupportedException("DataOriginAuthorityUnavailable");
                var source = await origins.CaptureAsync(intent, actor, cancellationToken).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The retained Forms source is unavailable or differs from the reviewed edit.");
                attempt.SourceAdmission = source;
                admission = new CombinedAdmission(admission, source);
            }
            if (DataRecordMutationReceipts.Read(workbook).Any(receipt => receipt.OperationID == intent.OperationID))
                throw new InvalidOperationException("DataMutationOperationAlreadyRecorded");
            DataRecordEdits.UpdateRecord(workbook, intent.TableID, intent.RecordID, intent.Values);
            DataRecordMutationReceipts.Add(workbook, new(intent.OperationID, intent.PayloadSHA256, intent.TableID, intent.RecordID,
                intent.Version, intent.RevisionID, DateTimeOffset.UtcNow, intent.Origin, intent.Origin is null ? 0 : 1));
            attempt.SaveAttempted = true; // A lost return can no longer prove no publication.
            saved = await guarded.SaveAsync(workbook, "Home-approved canonical record update", admission, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException)
        {
            if (attempt.SaveAttempted) return Unknown(); // No terminal failed audit; exact receipt observation only.
            var code = error switch
            {
                NotSupportedException { Message: "DataOriginAuthorityUnavailable" } => "SourceAuthorityUnavailable",
                OperationCanceledException => "Cancelled", UnauthorizedAccessException => "PermissionDenied",
                DataWorkbookRevisionConflictException => "RevisionConflict",
                InvalidOperationException { Message: "DataMutationOperationAlreadyRecorded" } => "DataMutationOperationAlreadyRecorded",
                InvalidOperationException { Message: "DataMutationReceiptCapacityReached" } => "DataMutationReceiptCapacityReached",
                InvalidOperationException { Message: "DataValidationFailed" } => "DataValidationFailed",
                InvalidOperationException { Message: "CalculatedFieldReadOnly" } => "CalculatedFieldReadOnly",
                KeyNotFoundException { Message: "FieldNotFound" } => "FieldNotFound",
                KeyNotFoundException { Message: "RecordNotFound" } => "RecordNotFound",
                _ => "DataUpdateFailed"
            };
            attempt.Outcome = new(error is OperationCanceledException ? HomePermissionRequestState.Cancelled
                : HomePermissionRequestState.Failed, code, "The Data record update was refused before publication was attempted.", []);
            attempt.Known = new(false, code, null, false); // First known owner outcome BEFORE audit await.
            return await FinishKnownAsync(attempt, capability).ConfigureAwait(false);
        }
        attempt.Outcome = new(HomePermissionRequestState.Succeeded, "DataRecordUpdated",
            $"Committed workbook revision {saved.Version}.", [new("data.record", intent.RecordID.ToString("D"))]);
        attempt.Known = new(true, "DataRecordUpdated", saved, false);
        return await FinishKnownAsync(attempt, capability).ConfigureAwait(false);
    }

    /// <summary>Observes only the retained original attempt; never Claim/Save/Authorize again.
    /// An unavailable receipt is unknown, never proof that publication failed.</summary>
    public async Task<DataRecordMutationResult> FinishAsync(HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capability);
        Attempt attempt;
        lock (_attemptLock)
            attempt = _attempts.TryGetValue(capability, out var retained) ? retained
                : throw new UnauthorizedAccessException("This Data owner did not retain that attempt.");
        await attempt.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (attempt.Known is not null) return attempt.Outcome is null ? await FinishRejectedAsync(attempt, capability).ConfigureAwait(false)
                : await FinishKnownAsync(attempt, capability).ConfigureAwait(false);
            if (!attempt.SaveAttempted || attempt.Actor is not { } actor || recovery is not DataRecordMutationRecovery actual
                || !actual.IsBoundTo(workbooks, authority)) return Unknown();
            try
            {
                var intent = attempt.Intent;
                var observed = await actual.ObserveAsync(intent, actor, cancellationToken).ConfigureAwait(false);
                var receipt = observed.Receipt;
                if (receipt is null) return Unknown();
                if (observed.Target.StoreID != intent.StoreID || observed.Target.WorkbookID != intent.WorkbookID
                    || receipt.BaseVersion != intent.Version || receipt.BaseRevisionID != intent.RevisionID)
                    return Unknown();
                if (intent.Origin is not null && (attempt.SourceAdmission is null ||
                    !await attempt.SourceAdmission.CheckAsync(new(intent.StoreID, intent.WorkbookID, intent.Version,
                        intent.RevisionID, DataWorkbookCommitPhase.Publication), cancellationToken).ConfigureAwait(false))) return Unknown();
                // The receipt committed atomically with the original cells. No guessed save path or current row result.
                attempt.Outcome = new(HomePermissionRequestState.Succeeded, "DataRecordUpdated",
                    "Observed the exact original durable Data mutation receipt.", [new("data.record", intent.RecordID.ToString("D"))]);
                attempt.Known = new(true, "DataRecordUpdated", null, false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException)
            { return Unknown(); }
            return await FinishKnownAsync(attempt, capability).ConfigureAwait(false);
        }
        finally { attempt.Gate.Release(); }
    }
    private async Task<DataRecordMutationResult> FinishRejectedAsync(Attempt attempt, HomeResourceExecutionCapability capability)
    {
        var known = attempt.Known ?? throw new InvalidOperationException("Known Home rejection required.");
        if (known.AuditRecorded) return known;
        try
        {
            var audit = await home.RetryRejectedClaimAuditAsync(capability, CancellationToken.None).ConfigureAwait(false);
            return attempt.Known = known with { AuditRecorded = audit.Succeeded };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or OperationCanceledException) { return known; }
    }

    private async Task<DataRecordMutationResult> FinishKnownAsync(Attempt attempt, HomeResourceExecutionCapability capability)
    {
        var known = attempt.Known ?? throw new InvalidOperationException("Known Data outcome required.");
        if (known.AuditRecorded) return known;
        var recorded = await CompleteAsync(capability, attempt.Outcome ?? throw new InvalidOperationException("Exact retained outcome required.")).ConfigureAwait(false);
        return attempt.Known = known with { AuditRecorded = recorded,
            Code = known.Committed ? recorded ? "DataRecordUpdated" : "DataCommittedAuditPending" : known.Code };
    }

    private sealed class CombinedAdmission(IDataWorkbookCommitAdmission target, IDataWorkbookCommitAdmission source) : IDataWorkbookCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(DataWorkbookCommitContext context, CancellationToken cancellationToken) =>
            await target.CheckAsync(context, cancellationToken).ConfigureAwait(false)
            && await source.CheckAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CompleteAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome)
    {
        try { return (await home.CompleteExecutionAsync(capability, outcome, CancellationToken.None).ConfigureAwait(false)).Succeeded; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { return false; } // A committed workbook remains committed even if its Home acknowledgement needs repair.
    }
}

public sealed class DataMutationActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == DataRecordUpdateIntent.TargetAppId && (actionId == DataRecordUpdateIntent.ActionId || actionId == DataTableSchemaUpdateIntent.ActionID || actionId == DataRelationshipUpdateIntent.ActionID || actionId == DataJunctionTableUpdateIntent.ActionID || actionId == DataRecordCreateIntent.ActionID)
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null;
}
