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

    [Theory]
    [InlineData("site")]
    [InlineData("project")]
    [InlineData("revision")]
    [InlineData("valid-old-revision")]
    [InlineData("legacy-native-object")]
    [InlineData("legacy-target-revision")]
    public async Task Rollback_binds_native_artifact_to_historical_identity_not_current_project_revision(string binding)
    {
        using var fixture=await Fixture.CreateAsync();
        var retainedRevision=binding=="legacy-target-revision"?0L:1L;
        await fixture.Store.MutateAsync(state=>(state with { Deployments=[fixture.Target with { ProjectRevision=retainedRevision }] },true));
        fixture.Artifact=fixture.Artifact with { FrameworkId="9to1-native",
            SiteId=binding=="legacy-native-object"?(Guid?)null:binding=="site"?Guid.NewGuid():fixture.Target.SiteId,
            ProjectId=binding=="legacy-native-object"?(Guid?)null:binding=="project"?Guid.NewGuid():fixture.Target.ProjectId,
            ProjectRevision=binding=="legacy-native-object"?(long?)null:binding=="revision"?2L:1L };
        if(binding=="legacy-native-object")fixture.ValidationOverride=new(true,[],fixture.Target.SiteId,fixture.Target.ProjectId,fixture.Target.StackDomainId,1L);

        var result=await fixture.Service.RollbackDeploymentAsync(fixture.Target.DeploymentId,"test",true);

        if(binding is "valid-old-revision" or "legacy-native-object" or "legacy-target-revision")
        {
            Assert.Null(result.Error);
            Assert.Equal(retainedRevision,result.Value!.ProjectRevision);
            Assert.Equal(1,fixture.UploadCount);Assert.Equal(1,fixture.PromotionCount);
        }
        else
        {
            Assert.Equal("RollbackTargetInvalid",result.Error?.Code);
            Assert.Equal(0,fixture.UploadCount);Assert.Equal(0,fixture.PromotionCount);
        }
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

    [Theory]
    [InlineData("site")]
    [InlineData("project")]
    [InlineData("domain")]
    [InlineData("revision")]
    public async Task Deployment_refuses_prepared_artifact_with_wrong_canonical_identity_before_archive_or_upload(string fault)
    {
        using var fixture=await Fixture.CreateAsync();
        var development=fixture.Environment with { Kind=SiteEnvironmentKind.Development,IsProduction=false };
        var buildDomainId=Guid.NewGuid();var sourceBuildProjectId=Guid.NewGuid();
        var buildProject=fixture.Project with { Source=fixture.Project.Source with
            { FrameworkId="9to1-native",StackProjectId=sourceBuildProjectId,StackDomainId=buildDomainId },Environments=[development] };
        await fixture.Store.MutateAsync(state=>(state with { Projects=[buildProject] },true));
        fixture.ResolvedSource=new("newer-source","config-1","9to1-native","build-source",true,true);
        fixture.BuiltArtifact=fixture.Artifact with { FrameworkId="9to1-native",SourceRevision="newer-source",ConfigurationRevision="config-1",SecretScanPassed=true,
            SiteId=fault=="site"?Guid.NewGuid():fixture.Project.SiteId,
            ProjectId=fault=="project"?Guid.NewGuid():fixture.Project.ProjectId,
            DomainId=fault=="domain"?Guid.NewGuid():buildDomainId,
            ProjectRevision=fault=="revision"?fixture.Project.Revision+1:fixture.Project.Revision };

        var result=await fixture.Service.BuildAndDeployAsync(new(fixture.Project.SiteId,"newer-source",development.EnvironmentId,"test","test"));

        Assert.Equal("BuildFailed",result.Error?.Code);
        Assert.Equal<Guid?>(fixture.Project.ProjectId,fixture.LastBuildRequest?.ProjectId);
        Assert.Equal<long?>(fixture.Project.Revision,fixture.LastBuildRequest?.ProjectRevision);
        Assert.Equal<Guid?>(buildDomainId,fixture.LastBuildRequest?.DomainId);
        Assert.Equal(fixture.Project.Revision,(await fixture.Store.ReadAsync(state=>state.Deployments)).Last().ProjectRevision);
        var authorization=fixture.LastAuthorization??throw new InvalidOperationException("Build preview did not request Home authorization.");
        Assert.Contains($"DomainID {buildDomainId:D}",authorization.ImpactSummary);
        Assert.Contains("source revision newer-source",authorization.ImpactSummary);
        Assert.Contains(buildDomainId.ToString("D"),authorization.ObjectIds);
        Assert.Contains(sourceBuildProjectId.ToString("D"),authorization.ObjectIds);
        Assert.Contains($"source binding ProjectID {sourceBuildProjectId:D}",authorization.ImpactSummary);
        Assert.Equal(0,fixture.ArtifactSaveCount);Assert.Equal(0,fixture.UploadCount);Assert.Equal(0,fixture.PromotionCount);
    }

    [Fact]
    public async Task Registered_external_framework_may_retain_legacy_artifact_without_unavailable_identity_claims()
    {
        using var fixture=await Fixture.CreateAsync();
        var development=fixture.Environment with { Kind=SiteEnvironmentKind.Development,IsProduction=false };
        await fixture.Store.MutateAsync(state=>(state with { Projects=[fixture.Project with { Environments=[development] }] },true));
        fixture.ResolvedSource=new("newer-source","config-1","html","registered-external-source",true,true);
        fixture.BuiltArtifact=fixture.Artifact with { SourceRevision="newer-source",ConfigurationRevision="config-1",FrameworkId="html",SecretScanPassed=true };
        fixture.VerificationSourceRevision="newer-source";

        var result=await fixture.Service.BuildAndDeployAsync(new(fixture.Project.SiteId,"newer-source",development.EnvironmentId,"test","test"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1,fixture.ArtifactSaveCount);Assert.Equal(1,fixture.UploadCount);Assert.Equal(0,fixture.PromotionCount);
        var authorization=fixture.LastAuthorization??throw new InvalidOperationException("External framework negotiation did not request Home authorization.");
        Assert.Contains($"ProjectID {fixture.Project.ProjectId:D}",authorization.ImpactSummary);
    }

    [Fact]
    public async Task Invalid_prepared_manifest_is_not_archived_or_uploaded()
    {
        using var fixture=await Fixture.CreateAsync();
        var development=fixture.Environment with { Kind=SiteEnvironmentKind.Development,IsProduction=false };
        var project=fixture.Project with { Source=fixture.Project.Source with { FrameworkId="9to1-native" },Environments=[development] };
        await fixture.Store.MutateAsync(state=>(state with { Projects=[project] },true));
        fixture.ResolvedSource=new("newer-source","config-1","9to1-native","build-source",true,true);
        fixture.BuiltArtifact=fixture.Artifact with { FrameworkId="9to1-native",SourceRevision="newer-source",ConfigurationRevision="config-1",
            SecretScanPassed=true,SiteId=project.SiteId,ProjectId=project.ProjectId,DomainId=project.Source.StackDomainId,ProjectRevision=project.Revision };
        fixture.ValidationPassed=false;

        var result=await fixture.Service.BuildAndDeployAsync(new(project.SiteId,"newer-source",development.EnvironmentId,"test","test"));

        Assert.Equal("BuildValidationFailed",result.Error?.Code);
        Assert.Equal(0,fixture.ArtifactSaveCount);Assert.Equal(0,fixture.UploadCount);Assert.Equal(0,fixture.PromotionCount);
    }

    [Fact]
    public async Task Self_consistent_native_manifest_for_another_site_is_rejected_before_archive()
    {
        using var fixture=await Fixture.CreateAsync();
        var development=fixture.Environment with { Kind=SiteEnvironmentKind.Development,IsProduction=false };
        var project=fixture.Project with { Source=fixture.Project.Source with { FrameworkId="9to1-native" },Environments=[development] };
        await fixture.Store.MutateAsync(state=>(state with { Projects=[project] },true));
        fixture.ResolvedSource=new("newer-source","config-1","9to1-native","build-source",true,true);
        fixture.BuiltArtifact=fixture.Artifact with { FrameworkId="9to1-native",SourceRevision="newer-source",ConfigurationRevision="config-1",
            SecretScanPassed=true,SiteId=project.SiteId,ProjectId=project.ProjectId,DomainId=project.Source.StackDomainId,ProjectRevision=project.Revision };
        fixture.ValidationOverride=new(true,[],Guid.NewGuid(),project.ProjectId,project.Source.StackDomainId,project.Revision);

        var result=await fixture.Service.BuildAndDeployAsync(new(project.SiteId,"newer-source",development.EnvironmentId,"test","test"));

        Assert.Equal("BuildValidationFailed",result.Error?.Code);
        Assert.Equal(0,fixture.ArtifactSaveCount);Assert.Equal(0,fixture.UploadCount);Assert.Equal(0,fixture.PromotionCount);
    }

    [Fact]
    public async Task External_artifact_identity_claim_requires_provider_validation_before_archive()
    {
        using var fixture=await Fixture.CreateAsync();
        var development=fixture.Environment with { Kind=SiteEnvironmentKind.Development,IsProduction=false };
        var project=fixture.Project with { Environments=[development] };
        await fixture.Store.MutateAsync(state=>(state with { Projects=[project] },true));
        fixture.ResolvedSource=new("newer-source","config-1","html","registered-external-source",true,true);
        fixture.BuiltArtifact=fixture.Artifact with { SourceRevision="newer-source",ConfigurationRevision="config-1",FrameworkId="html",
            SecretScanPassed=true,SiteId=project.SiteId,ProjectId=project.ProjectId,ProjectRevision=project.Revision };
        fixture.ValidationOverride=new(true,[]);

        var result=await fixture.Service.BuildAndDeployAsync(new(project.SiteId,"newer-source",development.EnvironmentId,"test","test"));

        Assert.Equal("BuildValidationFailed",result.Error?.Code);
        Assert.Equal(0,fixture.ArtifactSaveCount);Assert.Equal(0,fixture.UploadCount);Assert.Equal(0,fixture.PromotionCount);
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
        public SiteBuildValidation? ValidationOverride { get; set; }
        public CancellationTokenSource? CancelUpload { get; set; }
        public int UploadCount { get; private set; }
        public int ArtifactSaveCount { get; private set; }
        public SiteCanonicalSource? ResolvedSource { get; set; }
        public SiteBuildArtifact? BuiltArtifact { get; set; }
        public SiteBuildRequest? LastBuildRequest { get; private set; }
        public string? VerificationSourceRevision { get; set; }
        public SiteAuthorizationRequest? LastAuthorization { get; private set; }
        public int VerificationCount { get; private set; }
        public int PromotionCount { get; private set; }
        private Guid _uploadedDeployment;
        public SiteHostingProviderDescriptor Descriptor { get; } = new("test", "Isolated test provider", SiteHostingCapabilities.StaticDeployment | SiteHostingCapabilities.Rollback | SiteHostingCapabilities.AtomicPromotion, ["html","9to1-native"], true, null);

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
        public Task SaveAsync(SiteBuildArtifact artifact, CancellationToken token) { ArtifactSaveCount++; return Task.CompletedTask; }
        public Task<SiteBuildArtifact> BuildAndPackageAsync(SiteBuildRequest request, Func<SitePipelineUpdate,CancellationToken,ValueTask> progress, CancellationToken token)
        {
            LastBuildRequest=request;
            var artifact=BuiltArtifact;
            return artifact is null ? throw new InvalidOperationException("No build should run in these rollback tests.") : Task.FromResult(artifact);
        }
        public Task<SiteBuildValidation> ValidateArtifactAsync(SiteBuildArtifact artifact, CancellationToken token) =>
            Task.FromResult(ValidationOverride??new SiteBuildValidation(ValidationPassed, [],artifact.SiteId,artifact.ProjectId,artifact.DomainId,artifact.ProjectRevision));
        public Task<SiteAuthorizationDecision> RequestAsync(SiteAuthorizationRequest request, CancellationToken token)
        { LastAuthorization=request; return Task.FromResult(new SiteAuthorizationDecision(SiteAuthorizationState.Allowed, "test", null)); }
        public Task<SiteCanonicalSource> ResolveAsync(SiteSourceRevisionRequest request, CancellationToken token)
        {
            var source=ResolvedSource;
            return source is null ? throw new InvalidOperationException("No source resolution should run.") : Task.FromResult(source);
        }
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
            return Task.FromResult(new SiteProviderVerification(true, true, VerificationSourceRevision??Target.SourceRevision, Target.ArtifactId, SiteTlsState.Ready, []));
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
