using Haven.Core;
namespace Haven.Application;

public sealed partial class TaskRunPermissionAuthority
{
    private async Task ValidateColdAcceptedBoundaryAsync(RenewalWork work, CloudflareOriginalTaskLedger sources,
        ColdOwnerAdmission scope, CancellationToken token)
    {
        var capsule = sources.Invoke(() => scope.OriginalClaim.OriginalEntry.Capsule);
        if (capsule.Boundary == TaskRunColdBoundaryKind.NeverStartedAcceptedInput)
        { TaskRunColdRecoveryBoundary.DemandNeverStarted(capsule, scope.Expected); return; }
        if (capsule.Boundary != TaskRunColdBoundaryKind.SettledUnfinishedToolResponse || capsule.SchemaVersion != 2 ||
            _coldJournal is not ITaskRunColdAcceptedBoundarySource sameIssuer)
            throw new UnauthorizedAccessException("The SAME configured protected journal must validate its private accepted boundary.");
        Task? actual = null;
        try
        {
            _ = sources.Invoke(() =>
            {
                actual = sameIssuer.ValidateOriginalAcceptedBoundaryWithinSourceAsync(scope.OriginalClaim,
                    scope.Expected, ColdCaller(sources), raw => RetainColdRaw(work, sources, raw), token);
                RetainColdRaw(work, sources, actual); return actual;
            });
            await sources.AwaitAsync(actual!).ConfigureAwait(false);
        }
        catch (Exception error) { sources.Capture(actual, error); }
        // Original source faults never become material-only authority.
        if (sources.OriginalErrors.Count != 0 || actual?.IsCompletedSuccessfully != true)
            throw new AggregateException("The actual protected accepted-boundary validation failed.", sources.OriginalErrors);
        sources.Invoke(() => { TaskRunColdRecoveryBoundary.DemandRestorableBoundary(capsule, scope.Expected); return true; });
        scope.AcceptedBoundaryIssuer = sameIssuer; scope.AcceptedBoundaryValidation = actual;
    }
    private void DemandColdAcceptedBoundaryProof(ColdOwnerAdmission scope)
    {
        var capsule = scope.OriginalClaim.OriginalEntry.Capsule;
        if (capsule.Boundary == TaskRunColdBoundaryKind.NeverStartedAcceptedInput) return;
        if (capsule.Boundary != TaskRunColdBoundaryKind.SettledUnfinishedToolResponse || capsule.SchemaVersion != 2 ||
            !ReferenceEquals(scope.AcceptedBoundaryIssuer, _coldJournal) || scope.AcceptedBoundaryValidation?.IsCompletedSuccessfully != true)
            throw new UnauthorizedAccessException("Actual same-issuer successful pre-CAS accepted-boundary validation is required.");
        // ClosedContext validates the exact private Claim/ACK, protected HMAC state, unchanged
        // capsule/input and owner-revised current row after actual context/claim cleanup.
        // The closed claim is never reopened or validated against the owner-revised material.
    }
    private async Task ReadColdActivationPolicyAsync(CloudflareOriginalTaskLedger sources,
        TaskRunColdCapsule capsule, CancellationToken token)
    {
        if (capsule.Boundary == TaskRunColdBoundaryKind.NeverStartedAcceptedInput)
        { await ReadColdPolicyAsync(sources, capsule.OriginalInput, token).ConfigureAwait(false); return; }
        sources.Invoke(() =>
        {
            var input = capsule.OriginalInput;
            if (capsule.SchemaVersion != 2 || capsule.Boundary != TaskRunColdBoundaryKind.SettledUnfinishedToolResponse ||
                input.WorkspaceRoot is not null || input.ProjectContext is not null || input.ProjectInstructions is not null ||
                input.RegisteredContext is not null || input.ComputerUseRequest is not null || input.Images is { Count: > 0 } ||
                !Enum.IsDefined(input.FilePermission) || !Enum.IsDefined(input.CommandPermission) || !Enum.IsDefined(input.BrowserPermission) ||
                (input.ExplicitCapabilities ?? []).Any(value => !Enum.IsDefined(value)))
                throw new UnauthorizedAccessException("The identity-only cold boundary has no fresh owner for additional resources or invalid policy material.");
            _ = _privacy.Current ?? throw new InvalidOperationException("Current privacy policy is unavailable.");
            _ = RuntimeSafetyState.IsSafeMode; return true;
        });
        // Current central policy is observed as a raw source Task. No old failed model is
        // selected/evaluated here and no provider/attempt/effect lease is issued. After identity
        // activation the actual LOCAL selector and ordinary AuthorizeAttempt validate new use.
        var policy = await sources.AwaitAsync(sources.Invoke(() => _modelPermissions.GetOriginalPolicyAsync(token))).ConfigureAwait(false);
        if (policy is null) throw new UnauthorizedAccessException("Current central identity policy is unavailable.");
    }
    private async Task<AuthenticatedResourceActor?> ReadColdActivationActorAsync(RenewalWork work,
        CloudflareOriginalTaskLedger sources, ColdOwnerAdmission scope, CancellationToken token)
    {
        if (scope.OriginalClaim.OriginalEntry.Capsule.Boundary == TaskRunColdBoundaryKind.NeverStartedAcceptedInput)
            return await sources.AwaitAsync(sources.Invoke(() => _actors.GetCurrentAsync(token))).ConfigureAwait(false);
        if (_actors is not ITaskRunOriginalTaskActorObservationSource originalActors)
            throw new InvalidOperationException("Accepted-boundary activation requires the actual scoped Task actor source.");
        return await sources.AwaitAsync(sources.Invoke(() => originalActors.GetOriginalCurrentWithinSourceAsync(
            ColdCaller(sources), raw => RetainColdRaw(work, sources, raw), token))).ConfigureAwait(false);
    }
}
