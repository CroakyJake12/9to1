using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Hosting;
using HavenOS.Apps.Sites.Infrastructure;

namespace HavenOS.Apps.Sites;

public sealed record StartSiteDeploymentRequest(Guid SiteId, string SourceRevision, Guid EnvironmentId, string ProviderId, string Initiator);

public sealed class SiteDeploymentService(
    FileSiteWorkspaceStore store,
    ISiteCanonicalSourceResolver sourceResolver,
    ISiteBuildPipeline buildPipeline,
    ISiteArtifactArchive artifactArchive,
    ISiteHostingProviderRegistry providers,
    ISiteProductionSourcePolicy productionPolicy,
    ISiteActionAuthorizer authorizer)
{
    public async Task<SiteApiResult<SiteDeployment>> BuildAndDeployAsync(StartSiteDeploymentRequest request, CancellationToken cancellationToken = default)
    {
        Guid? activeDeploymentId = null;
        if (request.SiteId == Guid.Empty || request.EnvironmentId == Guid.Empty || string.IsNullOrWhiteSpace(request.SourceRevision) || string.IsNullOrWhiteSpace(request.ProviderId))
            return SiteApiResult<SiteDeployment>.Failure(new SiteApiError("InvalidInput", "SiteID, source revision, environment and hosting provider are required.", "Sites.Deploy", false));
        try
        {
            var context = await store.ReadAsync(state =>
            {
                var project = state.Projects.SingleOrDefault(candidate => candidate.SiteId == request.SiteId);
                var environment = project?.Environments.SingleOrDefault(candidate => candidate.EnvironmentId == request.EnvironmentId);
                return (project, environment);
            }, cancellationToken).ConfigureAwait(false);
            if (context.project is null) return Failure("SiteNotFound", "The Sites project was not found.", request.SiteId.ToString());
            if (context.environment is null) return Failure("EnvironmentNotFound", "The deployment environment is not configured for this site.", request.EnvironmentId.ToString());

            var provider = providers.Find(request.ProviderId);
            if (provider is null) return Failure("ProviderUnavailable", "The selected hosting provider is not connected or registered.", request.ProviderId, true);
            if (!provider.Descriptor.IsConnected) return Failure("ProviderUnavailable", provider.Descriptor.UnavailableReason ?? "Connect the hosting provider before deploying.", request.ProviderId, true);
            if (!provider.Descriptor.SupportedFrameworkIds.Contains(context.project.Source.FrameworkId, StringComparer.Ordinal))
                return Failure("CapabilityUnavailable", "The selected hosting provider does not support this site's framework.", request.ProviderId);

            if (context.environment.IsProduction)
            {
                var release = await productionPolicy.IsApprovedProductionSourceAsync(context.project, request.SourceRevision, cancellationToken).ConfigureAwait(false);
                if (release.Error is { } releaseError) return SiteApiResult<SiteDeployment>.Failure(releaseError);
                if (release.Value != true) return Failure("ProductionSourceNotApproved", "Production deployment requires an approved source revision under Stack policy.", request.SourceRevision);
                var hasApprovedPublicIdentity = await store.ReadAsync(state =>
                    state.SlugReservations.Any(reservation => reservation.SiteId == request.SiteId && reservation.ReleasedAt is null && reservation.NameSafetyState == SiteNameVerificationState.Passed) ||
                    state.Domains.Any(domain => domain.SiteId == request.SiteId && domain.IsCanonical && domain.OwnershipState == SiteDomainVerificationState.Verified && domain.NameSafetyState == SiteNameVerificationState.Passed),
                    cancellationToken).ConfigureAwait(false);
                if (!hasApprovedPublicIdentity)
                    return Failure("PublicIdentityNotVerified", "Production routing requires a verified first-party slug or verified canonical custom domain that passed name-safety screening.", request.SiteId.ToString());
            }

            var source = await sourceResolver.ResolveAsync(new SiteSourceRevisionRequest(context.project.SiteId, context.project.ProjectId, context.project.Source.StackDomainId, request.SourceRevision), cancellationToken).ConfigureAwait(false);
            if (!string.Equals(source.SourceRevision, request.SourceRevision, StringComparison.Ordinal))
                return Failure("RevisionConflict", "The canonical source resolver returned a different revision than requested.", request.SourceRevision);
            if (!context.environment.IsProduction && !context.project.Source.StackDomainId.HasValue && !source.IsWorkingTreeClean)
                return Failure("RevisionConflict", "A preview deployment must be pinned to a committed or otherwise immutable source revision.", request.SourceRevision);

            var authorization = await AuthorizeAsync(context.project, context.environment, source, cancellationToken).ConfigureAwait(false);
            if (authorization.Error is { } authorizationError) return SiteApiResult<SiteDeployment>.Failure(authorizationError);

            var now = DateTimeOffset.UtcNow;
            var deployment = new SiteDeployment(
                Guid.NewGuid(), context.project.SiteId, context.project.ProjectId, context.project.Source.StackDomainId, source.SourceRevision, source.ConfigurationRevision,
                context.environment.EnvironmentId, provider.Descriptor.ProviderId, "", null, SiteDeploymentState.Queued,
                Enum.GetValues<SiteDeploymentStageKind>().Select(kind => new SiteDeploymentStage(kind, SiteStageState.Pending, now, null, null, null)).ToArray(),
                now, now, request.Initiator, null, null);
            await SaveDeploymentAsync(deployment, cancellationToken).ConfigureAwait(false);
            activeDeploymentId = deployment.DeploymentId;

            var artifact = await buildPipeline.BuildAndPackageAsync(
                new SiteBuildRequest(deployment.SiteId, deployment.DeploymentId, source, context.environment.EnvironmentId),
                (update, token) => SetStageAsync(deployment.DeploymentId, update.Stage, update.State, update.Code, update.Message, token),
                cancellationToken).ConfigureAwait(false);
            if (artifact.ArtifactId == Guid.Empty || !string.Equals(artifact.SourceRevision, source.SourceRevision, StringComparison.Ordinal) ||
                !string.Equals(artifact.ConfigurationRevision, source.ConfigurationRevision, StringComparison.Ordinal))
                throw new SiteDeploymentException(new SiteApiError("BuildFailed", "The build artifact is not pinned to the requested source and configuration revisions.", deployment.DeploymentId.ToString(), false));
            if (!artifact.SecretScanPassed)
                throw new SiteDeploymentException(new SiteApiError("PrivateDataExposureBlocked", "The artifact did not pass secret and private-data checks; it was not uploaded.", deployment.DeploymentId.ToString(), false));
            await artifactArchive.SaveAsync(artifact, cancellationToken).ConfigureAwait(false);

            var validation = await buildPipeline.ValidateArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
                throw new SiteDeploymentException(validation.Diagnostics.FirstOrDefault() ?? new SiteApiError("BuildValidationFailed", "The build artifact failed validation.", deployment.DeploymentId.ToString(), false));
            await SetStageAsync(deployment.DeploymentId, SiteDeploymentStageKind.Validate, SiteStageState.Succeeded, null, null, cancellationToken).ConfigureAwait(false);

            var candidate = await provider.UploadCandidateAsync(artifact, deployment.SiteId, deployment.EnvironmentId, deployment.DeploymentId, cancellationToken).ConfigureAwait(false);
            if (candidate.ProviderDeploymentId == Guid.Empty || !string.Equals(candidate.SourceRevision, source.SourceRevision, StringComparison.Ordinal) ||
                !string.Equals(candidate.ConfigurationRevision, source.ConfigurationRevision, StringComparison.Ordinal) ||
                !string.Equals(candidate.ArtifactId, artifact.ArtifactId.ToString("D"), StringComparison.Ordinal))
                throw new SiteDeploymentException(new SiteApiError("DeploymentFailed", "The provider did not confirm the uploaded candidate's pinned source, configuration and artifact identities.", deployment.DeploymentId.ToString(), false));
            await SetStageAsync(deployment.DeploymentId, SiteDeploymentStageKind.Deploy, SiteStageState.Succeeded, null, null, cancellationToken).ConfigureAwait(false);

            var providerVerification = await provider.VerifyCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (!providerVerification.IsReachable || !providerVerification.ContentMatches ||
                !string.Equals(providerVerification.SourceRevision, source.SourceRevision, StringComparison.Ordinal) ||
                !string.Equals(providerVerification.ArtifactId, artifact.ArtifactId.ToString("D"), StringComparison.Ordinal))
                throw new SiteDeploymentException(providerVerification.Diagnostics.FirstOrDefault() ?? new SiteApiError("DeploymentVerificationFailed", "The deployed candidate did not verify against the selected source revision and artifact.", deployment.DeploymentId.ToString(), true));
            await SetStageAsync(deployment.DeploymentId, SiteDeploymentStageKind.Verify, SiteStageState.Succeeded, null, null, cancellationToken).ConfigureAwait(false);

            SiteHostingState hostingState;
            if (context.environment.IsProduction)
            {
                hostingState = await provider.PromoteAsync(candidate, deployment.SiteId, deployment.EnvironmentId, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(hostingState.ActiveSourceRevision, source.SourceRevision, StringComparison.Ordinal) || hostingState.ActiveDeploymentId != deployment.DeploymentId)
                    throw new SiteDeploymentException(new SiteApiError("PromotionFailed", "The provider did not confirm production routing to the verified deployment.", deployment.DeploymentId.ToString(), true));
            }
            else
            {
                hostingState = await provider.GetStateAsync(deployment.SiteId, deployment.EnvironmentId, cancellationToken).ConfigureAwait(false);
            }
            await SetStageAsync(deployment.DeploymentId, SiteDeploymentStageKind.Route, SiteStageState.Succeeded, null, null, cancellationToken).ConfigureAwait(false);
            var succeeded = await CompleteDeploymentAsync(deployment.DeploymentId, current => current with
            {
                ArtifactId = artifact.ArtifactId.ToString("D"), PublicUrl = context.environment.IsProduction ? hostingState.ActiveUrl : candidate.PreviewUrl,
                State = SiteDeploymentState.Succeeded, UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
            return SiteApiResult<SiteDeployment>.Success(succeeded);
        }
        catch (SiteOperationException ex)
        {
            var failed = await FailDeploymentAsync(activeDeploymentId, ex.Error, CancellationToken.None).ConfigureAwait(false);
            return failed is null ? SiteApiResult<SiteDeployment>.Failure(ex.Error) : SiteApiResult<SiteDeployment>.Success(failed);
        }
        catch (SiteDeploymentException ex)
        {
            var failed = await FailDeploymentAsync(activeDeploymentId, ex.Error, cancellationToken).ConfigureAwait(false);
            return failed is null ? SiteApiResult<SiteDeployment>.Failure(ex.Error) : SiteApiResult<SiteDeployment>.Success(failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var cancelled = await FailDeploymentAsync(activeDeploymentId, new SiteApiError("Cancelled", "The deployment was cancelled. Check hosting state before relying on the active route.", "Sites.Deploy", true), CancellationToken.None, SiteDeploymentState.Cancelled).ConfigureAwait(false);
            return cancelled is null
                ? SiteApiResult<SiteDeployment>.Failure(new SiteApiError("Cancelled", "The deployment was cancelled.", "Sites.Deploy", true))
                : SiteApiResult<SiteDeployment>.Success(cancelled);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TimeoutException)
        {
            var error = new SiteApiError("ProviderUnavailable", "The deployment provider or source storage is unavailable. The previous production deployment was not deliberately removed.", "Sites.Deploy", true, Detail: ex.GetType().Name);
            var failed = await FailDeploymentAsync(activeDeploymentId, error, CancellationToken.None).ConfigureAwait(false);
            return failed is null ? SiteApiResult<SiteDeployment>.Failure(error) : SiteApiResult<SiteDeployment>.Success(failed);
        }
        catch (Exception ex)
        {
            var error = new SiteApiError("DeploymentFailed", "The deployment stopped unexpectedly. Review hosting state before retrying; Sites retained the deployment history and artifact when available.", activeDeploymentId?.ToString() ?? "Sites.Deploy", true, Detail: ex.GetType().Name);
            var failed = await FailDeploymentAsync(activeDeploymentId, error, CancellationToken.None).ConfigureAwait(false);
            return failed is null ? SiteApiResult<SiteDeployment>.Failure(error) : SiteApiResult<SiteDeployment>.Success(failed);
        }
    }

    public async Task<SiteApiResult<SiteDeployment>> RollbackDeploymentAsync(Guid deploymentId, string initiator, bool userConfirmed, CancellationToken cancellationToken = default)
    {
        if (deploymentId == Guid.Empty) return SiteApiResult<SiteDeployment>.Failure(new SiteApiError("InvalidInput", "A DeploymentID is required.", "RollbackDeployment", false));
        if (!userConfirmed) return SiteApiResult<SiteDeployment>.Failure(new SiteApiError("ConfirmationRequired", "Rolling back production requires explicit confirmation.", "RollbackDeployment", false));
        try
        {
            var target = await store.ReadAsync(state => state.Deployments.SingleOrDefault(deployment => deployment.DeploymentId == deploymentId), cancellationToken).ConfigureAwait(false);
            if (target is null) return SiteApiResult<SiteDeployment>.Failure(new SiteApiError("DeploymentNotFound", "The deployment was not found.", deploymentId.ToString(), false));
            if (target.State != SiteDeploymentState.Succeeded || string.IsNullOrWhiteSpace(target.ArtifactId))
                return SiteApiResult<SiteDeployment>.Failure(new SiteApiError("RollbackUnavailable", "Rollback requires a previously successful deployment with a retained artifact.", deploymentId.ToString(), false));
            var artifact = await artifactArchive.GetAsync(Guid.Parse(target.ArtifactId), cancellationToken).ConfigureAwait(false);
            if (artifact is null) return SiteApiResult<SiteDeployment>.Failure(new SiteApiError("ArtifactUnavailable", "The deployment artifact is no longer available. Sites will not rebuild from the live website.", target.ArtifactId, false));
            var project = await store.ReadAsync(state => state.Projects.SingleOrDefault(candidate => candidate.SiteId == target.SiteId), cancellationToken).ConfigureAwait(false);
            var environment = project?.Environments.SingleOrDefault(candidate => candidate.EnvironmentId == target.EnvironmentId);
            if (project is null || environment is null || !environment.IsProduction)
                return SiteApiResult<SiteDeployment>.Failure(new SiteApiError("RollbackTargetInvalid", "The selected deployment is not bound to a configured production environment.", deploymentId.ToString(), false));
            var provider = providers.Find(target.ProviderId);
            if (provider is null || !provider.Descriptor.IsConnected)
                return SiteApiResult<SiteDeployment>.Failure(new SiteApiError("ProviderUnavailable", "The deployment provider is unavailable; no rollback was attempted.", target.ProviderId, true));
            var auth = await authorizer.RequestAsync(new SiteAuthorizationRequest("sites.deployment.rollback", SiteActionRisk.ExternalConsequence,
                [target.SiteId.ToString("D"), target.DeploymentId.ToString("D"), artifact.ArtifactId.ToString("D")], ["sites.deployment.rollback", "sites.deployment.promote"],
                $"Re-deploy the successful source revision {target.SourceRevision} from deployment {target.DeploymentId} to production. Newer source history will be retained."), cancellationToken).ConfigureAwait(false);
            if (auth.State != SiteAuthorizationState.Allowed)
                return SiteApiResult<SiteDeployment>.Failure(new SiteApiError(auth.State == SiteAuthorizationState.Denied ? "PermissionDenied" : "PermissionRequired", auth.Reason ?? "Home did not authorize this rollback.", "RollbackDeployment", auth.State != SiteAuthorizationState.Denied, Detail: auth.RequestId));

            var now = DateTimeOffset.UtcNow;
            var rollback = target with
            {
                DeploymentId = Guid.NewGuid(), State = SiteDeploymentState.RollingBack, CreatedAt = now, UpdatedAt = now, Initiator = initiator,
                Stages = Enum.GetValues<SiteDeploymentStageKind>().Select(kind => new SiteDeploymentStage(kind, SiteStageState.Pending, now, null, null, null)).ToArray(),
                PublicUrl = null, RolledBackFromDeploymentId = target.DeploymentId, FailureCode = null, FailureMessage = null
            };
            await SaveDeploymentAsync(rollback, cancellationToken).ConfigureAwait(false);
            try
            {
                var candidate = await provider.UploadCandidateAsync(artifact, rollback.SiteId, rollback.EnvironmentId, rollback.DeploymentId, cancellationToken).ConfigureAwait(false);
                var verification = await provider.VerifyCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (!verification.IsReachable || !verification.ContentMatches || verification.SourceRevision != target.SourceRevision || verification.ArtifactId != artifact.ArtifactId.ToString("D"))
                    throw new SiteDeploymentException(verification.Diagnostics.FirstOrDefault() ?? new SiteApiError("RollbackVerificationFailed", "The retained deployment artifact did not verify.", deploymentId.ToString(), true));
                var active = await provider.PromoteAsync(candidate, rollback.SiteId, rollback.EnvironmentId, cancellationToken).ConfigureAwait(false);
                if (active.ActiveDeploymentId != rollback.DeploymentId || active.ActiveSourceRevision != target.SourceRevision)
                    throw new SiteDeploymentException(new SiteApiError("RollbackPromotionFailed", "The provider did not confirm production rollback.", rollback.DeploymentId.ToString(), true));
                var succeeded = await CompleteDeploymentAsync(rollback.DeploymentId, current => current with { State = SiteDeploymentState.Succeeded, PublicUrl = active.ActiveUrl, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
                return SiteApiResult<SiteDeployment>.Success(succeeded);
            }
            catch (SiteDeploymentException ex)
            {
                var failed = await CompleteDeploymentAsync(rollback.DeploymentId, current => current with { State = SiteDeploymentState.Failed, FailureCode = ex.Error.Code, FailureMessage = ex.Error.Message, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None).ConfigureAwait(false);
                return SiteApiResult<SiteDeployment>.Failure(ex.Error);
            }
            catch (Exception ex)
            {
                var error = new SiteApiError("RollbackFailed", "Rollback did not complete. Check the provider's active hosting state before retrying.", rollback.DeploymentId.ToString(), true, Detail: ex.GetType().Name);
                await CompleteDeploymentAsync(rollback.DeploymentId, current => current with { State = SiteDeploymentState.Failed, FailureCode = error.Code, FailureMessage = error.Message, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None).ConfigureAwait(false);
                return SiteApiResult<SiteDeployment>.Failure(error);
            }
        }
        catch (SiteOperationException ex) { return SiteApiResult<SiteDeployment>.Failure(ex.Error); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return SiteApiResult<SiteDeployment>.Failure(new SiteApiError("Cancelled", "Rollback was cancelled. The previous active production route remains unchanged unless the provider reports otherwise.", deploymentId.ToString(), true)); }
    }

    private async Task<SiteApiResult<bool>> AuthorizeAsync(SiteProject project, SiteEnvironment environment, SiteCanonicalSource source, CancellationToken cancellationToken)
    {
        var action = environment.IsProduction ? "sites.deployment.production" : "sites.deployment.preview";
        var risk = SiteActionRisk.ExternalConsequence;
        var impact = $"Build and deploy SiteID {project.SiteId} from revision {source.SourceRevision} to {environment.Name} via a revision-pinned provider artifact. Current production history will be retained.";
        var decision = await authorizer.RequestAsync(new SiteAuthorizationRequest(action, risk, [project.SiteId.ToString("D"), environment.EnvironmentId.ToString("D")], [action], impact), cancellationToken).ConfigureAwait(false);
        return decision.State == SiteAuthorizationState.Allowed
            ? SiteApiResult<bool>.Success(true)
            : SiteApiResult<bool>.Failure(new SiteApiError(decision.State == SiteAuthorizationState.Denied ? "PermissionDenied" : decision.State == SiteAuthorizationState.PendingApproval ? "PermissionRequired" : "HomeServiceUnavailable", decision.Reason ?? "Home did not authorize this deployment.", action, decision.State is SiteAuthorizationState.PendingApproval or SiteAuthorizationState.Unavailable, Detail: decision.RequestId));
    }

    private Task<bool> SetStageAsync(Guid id, SiteDeploymentStageKind kind, SiteStageState stageState, string? code, string? message, CancellationToken cancellationToken) =>
        store.MutateAsync(state =>
        {
            var index = IndexOf(state.Deployments, deployment => deployment.DeploymentId == id);
            if (index < 0) throw new SiteOperationException(new SiteApiError("DeploymentNotFound", "The deployment history entry was not found.", id.ToString(), false));
            var deployment = state.Deployments[index];
            var now = DateTimeOffset.UtcNow;
            var stages = deployment.Stages.Select(stage => stage.Kind == kind
                ? stage with { State = stageState, StartedAt = stageState == SiteStageState.Running ? now : stage.StartedAt, CompletedAt = stageState is SiteStageState.Succeeded or SiteStageState.Failed or SiteStageState.Skipped or SiteStageState.Cancelled ? now : stage.CompletedAt, Code = code, Message = message }
                : stage).ToArray();
            var updated = deployment with { Stages = stages, State = stageState switch
            {
                SiteStageState.Running when kind is SiteDeploymentStageKind.Restore or SiteDeploymentStageKind.Build or SiteDeploymentStageKind.Validate or SiteDeploymentStageKind.Package => SiteDeploymentState.Building,
                SiteStageState.Running when kind == SiteDeploymentStageKind.Deploy => SiteDeploymentState.Deploying,
                SiteStageState.Running when kind == SiteDeploymentStageKind.Verify => SiteDeploymentState.Verifying,
                SiteStageState.Running when kind == SiteDeploymentStageKind.Route => SiteDeploymentState.Deploying,
                _ => deployment.State
            }, UpdatedAt = now };
            var deployments = state.Deployments.ToArray();
            deployments[index] = updated;
            return (state with { Deployments = deployments }, true);
        }, cancellationToken);

    private Task<SiteDeployment> SaveDeploymentAsync(SiteDeployment deployment, CancellationToken cancellationToken) =>
        store.MutateAsync(state =>
        {
            if (state.Deployments.Any(existing => existing.DeploymentId == deployment.DeploymentId))
                throw new SiteOperationException(new SiteApiError("Conflict", "The deployment ID already exists.", deployment.DeploymentId.ToString(), false));
            return (state with { Deployments = [.. state.Deployments, deployment] }, deployment);
        }, cancellationToken);

    private Task<SiteDeployment> CompleteDeploymentAsync(Guid id, Func<SiteDeployment, SiteDeployment> update, CancellationToken cancellationToken) =>
        store.MutateAsync(state =>
        {
            var index = IndexOf(state.Deployments, deployment => deployment.DeploymentId == id);
            if (index < 0) throw new SiteOperationException(new SiteApiError("DeploymentNotFound", "The deployment history entry was not found.", id.ToString(), false));
            var current = state.Deployments[index];
            var completed = update(current);
            var allStages = completed.Stages.Select(stage => stage.State is SiteStageState.Pending or SiteStageState.Running
                ? stage with { State = completed.State == SiteDeploymentState.Succeeded ? SiteStageState.Skipped : stage.State, CompletedAt = completed.State == SiteDeploymentState.Succeeded ? DateTimeOffset.UtcNow : stage.CompletedAt }
                : stage).ToArray();
            completed = completed with { Stages = allStages };
            var deployments = state.Deployments.ToArray();
            deployments[index] = completed;
            return (state with { Deployments = deployments }, completed);
        }, cancellationToken);

    private async Task<SiteDeployment?> FailDeploymentAsync(Guid? deploymentId, SiteApiError error, CancellationToken cancellationToken, SiteDeploymentState state = SiteDeploymentState.Failed)
    {
        if (deploymentId is null) return null;
        try
        {
            return await CompleteDeploymentAsync(deploymentId.Value, current => current with { State = state, FailureCode = error.Code, FailureMessage = error.Message, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
        }
        catch (SiteOperationException) { return null; }
        catch (OperationCanceledException) { return null; }
    }

    private static SiteApiResult<SiteDeployment> Failure(string code, string message, string target, bool recoverable = false) =>
        SiteApiResult<SiteDeployment>.Failure(new SiteApiError(code, message, target, recoverable));

    private static int IndexOf<T>(IReadOnlyList<T> values, Func<T, bool> predicate)
    {
        for (var index = 0; index < values.Count; index++) if (predicate(values[index])) return index;
        return -1;
    }
}
