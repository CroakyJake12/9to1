using System.Text.Json;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Hosting;
using HavenOS.Apps.Sites.Infrastructure;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

public sealed class SiteDeploymentSafetyTests
{
    [Theory]
    [InlineData("artifact")]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("secret")]
    [InlineData("invalid-id")]
    [InlineData("validation")]
    public async Task Rollback_rejects_invalid_retained_artifacts_before_upload(string fault)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Artifact = fault switch
        {
            "artifact" => fixture.Artifact with { ArtifactId = Guid.NewGuid() },
            "source" => fixture.Artifact with { SourceRevision = "different-source" },
            "configuration" => fixture.Artifact with { ConfigurationRevision = "different-config" },
            "secret" => fixture.Artifact with { SecretScanPassed = false },
            _ => fixture.Artifact
        };
        if (fault == "invalid-id") await fixture.Store.MutateAsync(state => (state with { Deployments = [fixture.Target with { ArtifactId = "invalid" }] }, true));
        fixture.ValidationPassed = fault != "validation";

        var result = await fixture.Service.RollbackDeploymentAsync(fixture.Target.DeploymentId, "test", true);

        Assert.NotNull(result.Error);
        Assert.Equal(0, fixture.UploadCount);
        Assert.Equal(0, fixture.PromotionCount);
        Assert.Single(await fixture.Store.ReadAsync(state => state.Deployments));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("artifact")]
    [InlineData("provider-id")]
    public async Task Rollback_rejects_wrong_candidate_identity_without_promoting(string fault)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.CandidateFault = fault;

        var result = await fixture.Service.RollbackDeploymentAsync(fixture.Target.DeploymentId, "test", true);

        Assert.Equal("RollbackVerificationFailed", result.Error?.Code);
        Assert.Equal(1, fixture.UploadCount);
        Assert.Equal(0, fixture.VerificationCount);
        Assert.Equal(0, fixture.PromotionCount);
        var history = await fixture.Store.ReadAsync(state => state.Deployments);
        Assert.Equal(2, history.Count);
        Assert.Equal(SiteDeploymentState.Succeeded, history[0].State);
        Assert.Equal(SiteDeploymentState.Failed, history[1].State);
        Assert.Equal(SiteStageState.Failed, history[1].Stages.Single(stage => stage.Kind == SiteDeploymentStageKind.Deploy).State);
        Assert.DoesNotContain(history[1].Stages, stage => stage.State is SiteStageState.Pending or SiteStageState.Running);
    }

    [Theory]
    [InlineData("site")]
    [InlineData("environment")]
    [InlineData("provider")]
    public async Task Rollback_rejects_promotion_confirmation_from_other_context(string fault)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.PromotionFault = fault;
        var result = await fixture.Service.RollbackDeploymentAsync(fixture.Target.DeploymentId, "test", true);
        Assert.Equal("RollbackPromotionFailed", result.Error?.Code);
        Assert.Equal(SiteDeploymentState.Failed, (await fixture.Store.ReadAsync(state => state.Deployments)).Last().State);
    }

    [Fact]
    public async Task Rollback_preserves_newer_source_and_records_successful_retained_deployment()
    {
        using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.RollbackDeploymentAsync(fixture.Target.DeploymentId, "test", true);
        Assert.Null(result.Error);
        Assert.Equal(SiteDeploymentState.Succeeded, result.Value!.State);
        Assert.Equal(fixture.Target.DeploymentId, result.Value.RolledBackFromDeploymentId);
        Assert.Equal(fixture.Target.ConfigurationRevision, result.Value.ConfigurationRevision);
        Assert.Equal(fixture.Target.ArtifactId, result.Value.ArtifactId);
        foreach (var kind in new[] { SiteDeploymentStageKind.Validate, SiteDeploymentStageKind.Deploy, SiteDeploymentStageKind.Verify, SiteDeploymentStageKind.Route })
            Assert.Equal(SiteStageState.Succeeded, result.Value.Stages.Single(stage => stage.Kind == kind).State);
        var reopened = new FileSiteWorkspaceStore(fixture.Directory);
        Assert.Equal("newer-source", (await reopened.ReadAsync(state => state.Projects)).Single().Source.SourceRevision);
        Assert.Equal(2, (await reopened.ReadAsync(state => state.Deployments)).Count);
    }

    [Fact]
    public async Task Rollback_cancellation_is_persisted_and_does_not_promote()
    {
        using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.CancelUpload = cancellation;
        var result = await fixture.Service.RollbackDeploymentAsync(fixture.Target.DeploymentId, "test", true, cancellation.Token);
        Assert.Equal("Cancelled", result.Error?.Code);
        Assert.Equal(0, fixture.PromotionCount);
        var cancelled = (await fixture.Store.ReadAsync(state => state.Deployments)).Last();
        Assert.Equal(SiteDeploymentState.Cancelled, cancelled.State);
        Assert.Equal(SiteStageState.Cancelled, cancelled.Stages.Single(stage => stage.Kind == SiteDeploymentStageKind.Deploy).State);
        Assert.DoesNotContain(cancelled.Stages, stage => stage.State is SiteStageState.Pending or SiteStageState.Running);
    }

    [Fact]
    public async Task Disabled_environment_prevents_new_deployment_and_rollback()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.Store.MutateAsync(state => (state with { Projects = [fixture.Project with { Environments = [fixture.Environment with { IsEnabled = false }] }] }, true));
        var deploy = await fixture.Service.BuildAndDeployAsync(new(fixture.Project.SiteId, "newer-source", fixture.Environment.EnvironmentId, "test", "test"));
        Assert.Equal("CapabilityUnavailable", deploy.Error?.Code);
        var rollback = await fixture.Service.RollbackDeploymentAsync(fixture.Target.DeploymentId, "test", true);
        Assert.Equal("RollbackTargetInvalid", rollback.Error?.Code);
        Assert.Equal(0, fixture.UploadCount);
    }

    private sealed class Fixture : IDisposable, ISiteArtifactArchive, ISiteHostingProvider, ISiteHostingProviderRegistry,
        ISiteBuildPipeline, ISiteActionAuthorizer, ISiteCanonicalSourceResolver, ISiteProductionSourcePolicy
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "sites-deploy-safety-" + Guid.NewGuid().ToString("N"));
        public FileSiteWorkspaceStore Store { get; }
        public SiteProject Project { get; }
        public SiteEnvironment Environment { get; } = new(Guid.NewGuid(), "production", SiteEnvironmentKind.Production, true, true);
        public SiteBuildArtifact Artifact { get; set; } = new(Guid.NewGuid(), "retained-source", "retained-config", "html", "hash", "files-artifact", 10, true);
        public SiteDeployment Target { get; }
        public SiteDeploymentService Service { get; }
        public string? CandidateFault { get; set; }
        public string? PromotionFault { get; set; }
        public bool ValidationPassed { get; set; } = true;
        public CancellationTokenSource? CancelUpload { get; set; }
        public int UploadCount { get; private set; }
        public int VerificationCount { get; private set; }
        public int PromotionCount { get; private set; }
        private Guid _uploadedDeployment;
        public SiteHostingProviderDescriptor Descriptor { get; } = new("test", "Isolated test provider", SiteHostingCapabilities.StaticDeployment | SiteHostingCapabilities.Rollback | SiteHostingCapabilities.AtomicPromotion, ["html"], true, null);

        private Fixture()
        {
            Store = new(Directory);
            var now = DateTimeOffset.UtcNow;
            Project = new(Guid.NewGuid(), Guid.NewGuid(), "test", new(Guid.NewGuid(), null, null, "newer-source", "html", "site"), 2, now, now,
                [], [], [], [], new(new Dictionary<string,string>(), new Dictionary<string,string>(), new Dictionary<string,string>(), new Dictionary<string,string>(), new Dictionary<string,string>(), new Dictionary<string,int>(), "system", 1),
                [], [], [Environment], [], [], new Dictionary<string,JsonElement>());
            Target = new(Guid.NewGuid(), Project.SiteId, Project.ProjectId, null, Artifact.SourceRevision, Artifact.ConfigurationRevision, Environment.EnvironmentId, "test", Artifact.ArtifactId.ToString("D"), new("https://example.invalid/old"), SiteDeploymentState.Succeeded, [], now, now, "test", null, null);
            Service = new(Store, this, this, this, this, this, this);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.Store.MutateAsync(state => (state with { Projects = [fixture.Project], Deployments = [fixture.Target] }, true));
            return fixture;
        }
        public Task<SiteBuildArtifact?> GetAsync(Guid artifactId, CancellationToken token) => Task.FromResult<SiteBuildArtifact?>(Artifact);
        public Task SaveAsync(SiteBuildArtifact artifact, CancellationToken token) => Task.CompletedTask;
        public Task<SiteBuildArtifact> BuildAndPackageAsync(SiteBuildRequest request, Func<SitePipelineUpdate,CancellationToken,ValueTask> progress, CancellationToken token) => throw new InvalidOperationException("No build should run in these rollback tests.");
        public Task<SiteBuildValidation> ValidateArtifactAsync(SiteBuildArtifact artifact, CancellationToken token) => Task.FromResult(new SiteBuildValidation(ValidationPassed, []));
        public Task<SiteAuthorizationDecision> RequestAsync(SiteAuthorizationRequest request, CancellationToken token) => Task.FromResult(new SiteAuthorizationDecision(SiteAuthorizationState.Allowed, "test", null));
        public Task<SiteCanonicalSource> ResolveAsync(SiteSourceRevisionRequest request, CancellationToken token) => throw new InvalidOperationException("No source resolution should run.");
        public Task<SiteApiResult<bool>> IsApprovedProductionSourceAsync(SiteProject project, string revision, CancellationToken token) => Task.FromResult(SiteApiResult<bool>.Success(true));
        public IReadOnlyList<SiteHostingProviderDescriptor> ListProviders() => [Descriptor];
        public ISiteHostingProvider? Find(string providerId) => providerId == "test" ? this : null;
        public Task<SiteCandidateDeployment> UploadCandidateAsync(SiteBuildArtifact artifact, Guid siteId, Guid environmentId, Guid deploymentId, CancellationToken token)
        {
            UploadCount++;
            _uploadedDeployment = deploymentId;
            if (CancelUpload is not null) { CancelUpload.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.FromResult(new SiteCandidateDeployment(CandidateFault == "provider-id" ? Guid.Empty : Guid.NewGuid(), new("https://example.invalid/preview"), CandidateFault == "source" ? "wrong" : artifact.SourceRevision,
                CandidateFault == "configuration" ? "wrong" : artifact.ConfigurationRevision, CandidateFault == "artifact" ? Guid.NewGuid().ToString("D") : artifact.ArtifactId.ToString("D")));
        }
        public Task<SiteProviderVerification> VerifyCandidateAsync(SiteCandidateDeployment candidate, CancellationToken token)
        {
            VerificationCount++;
            return Task.FromResult(new SiteProviderVerification(true, true, Target.SourceRevision, Target.ArtifactId, SiteTlsState.Ready, []));
        }
        public Task<SiteHostingState> GetStateAsync(Guid siteId, Guid environmentId, CancellationToken token) => Task.FromResult(new SiteHostingState(siteId, environmentId, "test", new("https://example.invalid/active"), Target.SourceRevision, _uploadedDeployment, SiteTlsState.Ready, "active", DateTimeOffset.UtcNow));
        public Task<SiteHostingState> PromoteAsync(SiteCandidateDeployment candidate, Guid siteId, Guid environmentId, CancellationToken token)
        {
            PromotionCount++;
            return GetStateAsync(PromotionFault == "site" ? Guid.NewGuid() : siteId, PromotionFault == "environment" ? Guid.NewGuid() : environmentId, token).ContinueWith(task => task.Result with { ProviderId = PromotionFault == "provider" ? "other" : "test" }, token);
        }
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
    }
}
