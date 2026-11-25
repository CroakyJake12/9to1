using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

/// <summary>ED-SITES-003: preview/staging do not alter production; a failed deployment leaves the previous live revision intact where the provider
/// supports atomic replacement (otherwise report real partial state); rollback selects a known deployment and records its actual result.</summary>
public sealed class SiteDeploymentRollbackModelTests
{
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Production = Guid.NewGuid();
    private static readonly Guid Staging = Guid.NewGuid();

    private static SiteDeployment Deployment(Guid environmentId, string revision, SiteDeploymentState state, string provider = "cdn-atomic", DateTimeOffset? updated = null) =>
        new(Guid.NewGuid(), Site, Project, null, revision, "cfg-1", environmentId, provider, "artifact-" + revision, null, state, [], updated ?? DateTimeOffset.UtcNow, updated ?? DateTimeOffset.UtcNow);

    [Fact]
    public void LiveRevisionUsesOnlySucceededDeploymentsOfThatEnvironment()
    {
        var deployments = new[]
        {
            Deployment(Production, "rev-1", SiteDeploymentState.Succeeded, updated: DateTimeOffset.UtcNow.AddMinutes(-20)),
            Deployment(Production, "rev-2", SiteDeploymentState.Failed, updated: DateTimeOffset.UtcNow.AddMinutes(-10)),
            Deployment(Staging, "rev-9", SiteDeploymentState.Succeeded),
        };
        Assert.Equal("rev-1", SiteDeploymentRollbackModel.LiveRevisionFor(Production, deployments));
    }

    [Fact]
    public void PreviewCannotTargetProduction()
    {
        Assert.Throws<InvalidOperationException>(() => SiteDeploymentRollbackModel.PreviewCannotAffectProduction(Production, Production, "preview"));
        // A legitimate staging deploy to the staging environment is allowed.
        SiteDeploymentRollbackModel.PreviewCannotAffectProduction(Production, Staging, "staging");
    }

    [Fact]
    public void AtomicFailureRestoresPreviousLive()
    {
        var failed = Deployment(Production, "rev-2", SiteDeploymentState.Failed, provider: "blue-green");
        Assert.Equal(SiteFailureOutcome.RestoredPreviousLive, SiteDeploymentRollbackModel.FailureOutcome(failed));
    }

    [Fact]
    public void NonAtomicFailureReportsPartialStateWithFailingStage()
    {
        var stage = new SiteDeploymentStage(SiteDeploymentStageKind.Deploy, SiteStageState.Failed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "E1", "boom");
        var failed = Deployment(Production, "rev-2", SiteDeploymentState.Failed, provider: "plain-upload") with { Stages = [stage] };

        Assert.Equal(SiteFailureOutcome.PartialState, SiteDeploymentRollbackModel.FailureOutcome(failed));
        Assert.Equal(SiteDeploymentStageKind.Deploy, SiteDeploymentRollbackModel.FailingStage(failed));
    }

    [Fact]
    public void RollbackSelectsMostRecentPriorSucceededAndReferencesCurrent()
    {
        var current = Deployment(Production, "rev-3", SiteDeploymentState.Failed);
        var known = new[]
        {
            Deployment(Production, "rev-1", SiteDeploymentState.Succeeded, updated: DateTimeOffset.UtcNow.AddMinutes(-30)),
            Deployment(Production, "rev-2", SiteDeploymentState.Succeeded, updated: DateTimeOffset.UtcNow.AddMinutes(-15)),
            current,
        };

        var rollback = SiteDeploymentRollbackModel.PlanRollback(current, known);
        Assert.Equal(SiteDeploymentState.RollingBack, rollback.State);
        Assert.Equal("rev-2", rollback.SourceRevision);
        Assert.Equal(current.DeploymentId, rollback.RolledBackFromDeploymentId);
    }

    [Fact]
    public void RollbackWithNoKnownSucceededIsRejected()
    {
        var current = Deployment(Production, "rev-1", SiteDeploymentState.Failed);
        Assert.Throws<InvalidOperationException>(() => SiteDeploymentRollbackModel.PlanRollback(current, [current]));
    }

    [Fact]
    public void RecordRollbackResultCapturesActualOutcome()
    {
        var current = Deployment(Production, "rev-3", SiteDeploymentState.Failed);
        var rollback = SiteDeploymentRollbackModel.PlanRollback(current, [Deployment(Production, "rev-2", SiteDeploymentState.Succeeded), current]);

        var ok = SiteDeploymentRollbackModel.RecordRollbackResult(rollback, succeeded: true);
        Assert.Equal(SiteDeploymentState.Succeeded, ok.State);

        var bad = SiteDeploymentRollbackModel.RecordRollbackResult(rollback, succeeded: false, "RB01", "provider rejected");
        Assert.Equal(SiteDeploymentState.Failed, bad.State);
        Assert.Equal("provider rejected", bad.FailureMessage);
    }

    [Fact]
    public void FailureOutcomeRequiresFailedState()
    {
        var ok = Deployment(Production, "rev-1", SiteDeploymentState.Succeeded);
        Assert.Throws<InvalidOperationException>(() => SiteDeploymentRollbackModel.FailureOutcome(ok));
    }
}
