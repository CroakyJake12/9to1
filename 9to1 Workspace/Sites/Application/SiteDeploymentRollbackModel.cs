using HavenOS.Apps.Sites.Domain;

namespace HavenOS.Apps.Sites.Application;

/// <summary>What actually happened to the live revision after a failed deployment. Atomic providers restore the previous live; non-atomic providers report real partial state.</summary>
public enum SiteFailureOutcome { RestoredPreviousLive, PartialState }

/// <summary>Pure authored deployment/rollback boundaries over SiteDeployment. It never performs provider calls; it decides environment isolation,
/// the current live revision from actual succeeded receipts, whether a failure is recoverable to previous live, and which known deployment a rollback selects.</summary>
public static class SiteDeploymentRollbackModel
{
    /// <summary>Whether a provider supports atomic replacement (swap-then-promote). Non-atomic providers cannot guarantee the previous live survives a mid-deploy failure.</summary>
    public static bool IsAtomicReplacement(string providerId) => providerId switch
    {
        "cdn-atomic" or "edge-atomic" or "blue-green" => true,
        _ => false,
    };

    /// <summary>The current live revision for an environment: the newest Succeeded deployment's source revision. Failed/queued/cancelled never count.</summary>
    public static string? LiveRevisionFor(Guid environmentId, IEnumerable<SiteDeployment> deployments)
    {
        ArgumentNullException.ThrowIfNull(deployments);
        return deployments
            .Where(deployment => deployment.EnvironmentId == environmentId && deployment.State == SiteDeploymentState.Succeeded)
            .OrderByDescending(deployment => deployment.UpdatedAt)
            .ThenByDescending(deployment => deployment.CreatedAt)
            .Select(deployment => deployment.SourceRevision)
            .FirstOrDefault();
    }

    /// <summary>Preview and staging must never target the production environment id; a mismatch is rejected rather than silently promoting.</summary>
    public static void PreviewCannotAffectProduction(Guid productionEnvironmentId, Guid targetEnvironmentId, string targetKind)
    {
        if (string.IsNullOrWhiteSpace(targetKind)) throw new InvalidDataException("A deployment must name its target kind.");
        var nonProduction = targetKind.Equals("preview", StringComparison.OrdinalIgnoreCase) || targetKind.Equals("staging", StringComparison.OrdinalIgnoreCase);
        if (nonProduction && targetEnvironmentId == productionEnvironmentId)
            throw new InvalidOperationException("A preview or staging deployment cannot target the production environment.");
    }

    /// <summary>Decide the live outcome of a failed deployment. Atomic providers restore the previous live revision; non-atomic report the real partial state with the failing stage.</summary>
    public static SiteFailureOutcome FailureOutcome(SiteDeployment failed)
    {
        ArgumentNullException.ThrowIfNull(failed);
        if (failed.State != SiteDeploymentState.Failed) throw new InvalidOperationException("Only a failed deployment has a failure outcome.");
        return IsAtomicReplacement(failed.ProviderId) ? SiteFailureOutcome.RestoredPreviousLive : SiteFailureOutcome.PartialState;
    }

    /// <summary>The failing stage for reporting real partial state (the first Failed stage), or null when none is marked.</summary>
    public static SiteDeploymentStageKind? FailingStage(SiteDeployment failed)
    {
        ArgumentNullException.ThrowIfNull(failed);
        return failed.Stages?.FirstOrDefault(stage => stage.State == SiteStageState.Failed)?.Kind;
    }

    /// <summary>Select the most recent prior Succeeded deployment on the same environment as a rollback candidate and produce a RollingBack record that references it.</summary>
    public static SiteDeployment PlanRollback(SiteDeployment current, IEnumerable<SiteDeployment> knownDeployments)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(knownDeployments);
        var candidate = knownDeployments
            .Where(deployment => deployment.EnvironmentId == current.EnvironmentId && deployment.State == SiteDeploymentState.Succeeded && deployment.DeploymentId != current.DeploymentId)
            .OrderByDescending(deployment => deployment.UpdatedAt)
            .ThenByDescending(deployment => deployment.CreatedAt)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("There is no known successful deployment to roll back to on this environment.");

        var now = DateTimeOffset.UtcNow;
        return current with
        {
            DeploymentId = Guid.NewGuid(),
            SourceRevision = candidate.SourceRevision,
            ConfigurationRevision = candidate.ConfigurationRevision,
            ArtifactId = candidate.ArtifactId,
            PublicUrl = candidate.PublicUrl,
            State = SiteDeploymentState.RollingBack,
            Stages = [],
            CreatedAt = now,
            UpdatedAt = now,
            FailureCode = null,
            FailureMessage = null,
            RolledBackFromDeploymentId = current.DeploymentId,
        };
    }

    /// <summary>Record the actual result of a rollback attempt (success restores that revision; failure keeps the rolling-back state visible with a reason).</summary>
    public static SiteDeployment RecordRollbackResult(SiteDeployment rollingBack, bool succeeded, string? failureCode = null, string? failureMessage = null)
    {
        ArgumentNullException.ThrowIfNull(rollingBack);
        if (rollingBack.State != SiteDeploymentState.RollingBack) throw new InvalidOperationException("Only a rolling-back deployment can record a rollback result.");
        if (!succeeded && string.IsNullOrWhiteSpace(failureMessage)) throw new InvalidDataException("A failed rollback must record why.");
        return rollingBack with
        {
            State = succeeded ? SiteDeploymentState.Succeeded : SiteDeploymentState.Failed,
            UpdatedAt = DateTimeOffset.UtcNow,
            FailureCode = succeeded ? null : failureCode,
            FailureMessage = succeeded ? null : failureMessage,
        };
    }
}
