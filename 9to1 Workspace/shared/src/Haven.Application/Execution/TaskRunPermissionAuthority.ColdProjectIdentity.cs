using Haven.Core;
namespace Haven.Application;

public sealed partial class TaskRunPermissionAuthority
{
    private static bool HasColdProjectMaterial(TaskRunColdCapsule capsule) => capsule.OriginalProjectIdentity is not null;
    private async Task ValidateColdProjectBeforeCasAsync(RenewalWork work, CloudflareOriginalTaskLedger sources,
        ColdOwnerAdmission scope, CancellationToken token)
    {
        var capsule = sources.Invoke(() => scope.OriginalClaim.OriginalEntry.Capsule);
        if (!HasColdProjectMaterial(capsule)) return;
        DemandColdAcceptedBoundaryProof(scope);
        sources.Invoke(() => { TaskRunColdProjectBoundary.DemandOriginalProjectMaterial(capsule); return true; });
        var issuer = _coldJournal as ITaskRunColdOriginalProjectBoundarySource
            ?? throw new UnauthorizedAccessException("The SAME protected journal must own a fresh private Home project boundary.");
        var actual = await ValidateColdProjectSourceAsync(work, sources, () => issuer.ValidateOriginalProjectBoundaryWithinSourceAsync(
            scope.OriginalClaim, scope.OriginalContext, scope.Expected, ColdCaller(sources),
            raw => RetainColdRaw(work, sources, raw), token)).ConfigureAwait(false);
        scope.ProjectBoundaryIssuer = issuer; scope.ProjectBoundaryValidation = actual;
    }
    private void DemandColdProjectBoundaryProof(ColdOwnerAdmission scope)
    {
        if (!HasColdProjectMaterial(scope.OriginalClaim.OriginalEntry.Capsule)) return;
        if (!ReferenceEquals(scope.ProjectBoundaryIssuer, _coldJournal) || scope.ProjectBoundaryValidation?.IsCompletedSuccessfully != true)
            throw new UnauthorizedAccessException("Actual SAME journal/Context/expected source and successful fresh Home/native project validation are required.");
        DemandColdAcceptedBoundaryProof(scope);
    }
    private async Task ValidateColdClosedProjectAsync(RenewalWork work, CloudflareOriginalTaskLedger sources,
        ColdOwnerAdmission scope, ITaskRunColdJournalAcknowledgment acknowledgment, CancellationToken token)
    {
        if (!sources.Invoke(() => HasColdProjectMaterial(scope.OriginalClaim.OriginalEntry.Capsule))) return;
        DemandColdProjectBoundaryProof(scope);
        // Private pre-CAS proof is retained, not reminted against the owner-revised row. The
        // SAME journal checks actual successful project/Context/Claim closes, authentic ACK,
        // current input and a NEW Home/native READ before the final canonical Task actor read.
        await ValidateColdProjectSourceAsync(work, sources, () => scope.ProjectBoundaryIssuer!.ValidateOriginalClosedProjectBoundaryWithinSourceAsync(
            scope.OriginalContext, acknowledgment, ColdCaller(sources), raw => RetainColdRaw(work, sources, raw), token)).ConfigureAwait(false);
    }
    private async Task<Task> ValidateColdProjectSourceAsync(RenewalWork work, CloudflareOriginalTaskLedger sources, Func<Task> factory)
    {
        Task? actual = null;
        try
        {
            _ = sources.Invoke(() => { actual = factory(); RetainColdRaw(work, sources, actual); return actual; });
        }
        catch (Exception cause) { sources.Capture(actual, cause); }
        if (actual is not null)
            try { await sources.AwaitAsync(actual).ConfigureAwait(false); }
            catch (Exception cause) { sources.Capture(actual, cause); }
        await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (sources.OriginalErrors.Count != 0 || actual?.IsCompletedSuccessfully != true)
            throw new AggregateException("The actual private cold project source validation failed.", sources.OriginalErrors);
        return actual;
    }
    private Task ReadColdActivationPolicyForScopeAsync(CloudflareOriginalTaskLedger sources, ColdOwnerAdmission scope, CancellationToken token)
    {
        var capsule = sources.Invoke(() => scope.OriginalClaim.OriginalEntry.Capsule);
        if (!HasColdProjectMaterial(capsule)) return ReadColdActivationPolicyAsync(sources, capsule, token);
        DemandColdProjectBoundaryProof(scope);
        return ReadColdProjectPolicyAfterProofAsync(sources, capsule, token);
    }
    private async Task ReadColdProjectPolicyAfterProofAsync(CloudflareOriginalTaskLedger sources,
        TaskRunColdCapsule capsule, CancellationToken token)
    {
        sources.Invoke(() =>
        {
            TaskRunColdProjectBoundary.DemandOriginalProjectMaterial(capsule);
            var input = capsule.OriginalInput;
            if (!Enum.IsDefined(input.FilePermission) || !Enum.IsDefined(input.CommandPermission) || !Enum.IsDefined(input.BrowserPermission) ||
                (input.ExplicitCapabilities ?? []).Any(value => !Enum.IsDefined(value)))
                throw new UnauthorizedAccessException("Invalid project policy material cannot activate a Task identity.");
            _ = _privacy.Current ?? throw new InvalidOperationException("Current privacy policy is unavailable.");
            _ = RuntimeSafetyState.IsSafeMode; return true;
        });
        // Resource rights came from the private scoped journal + NEW Home READ, not these
        // fields. Activation issues no provider/model/attempt/effect lease. The fresh ordinary
        // selector and AuthorizeAttempt remain responsible for actual current model/tool use.
        var policy = await sources.AwaitAsync(sources.Invoke(() => _modelPermissions.GetOriginalPolicyAsync(token))).ConfigureAwait(false);
        if (policy is null) throw new UnauthorizedAccessException("Current central project identity policy is unavailable.");
    }
}
