using Haven.Application;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Sites.Tests;

public sealed class SiteNativeWriteTests
{
    [Fact]
    public async Task Actual_Home_capability_creates_and_edits_one_Sites_revision_and_cannot_replay()
    {
        await using var fixture = await Fixture.Create();
        var intent = SiteNativeWriteIntent.Create(fixture.Binding, "Fixture", "9to1-native", "fixture");
        var capability = await fixture.Approve(intent);
        var created = await fixture.Owner.ExecuteAsync(intent, capability);
        Assert.Null(created.Error); var project = Assert.IsType<SiteProject>(created.Value);
        Assert.Equal(fixture.Binding.FilesFolderId, project.Source.FilesDirectoryId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(intent, capability));
        var page = SiteNativeWriteIntent.CreatePage(fixture.Binding, project.SiteId, 1, "Home", "/");
        var saved = await fixture.Owner.ExecuteAsync(page, await fixture.Approve(page));
        Assert.Null(saved.Error); Assert.Equal(2, saved.Value!.Revision); Assert.Single(saved.Value.Pages);
        var reread = await new SiteProjectService(new FileSiteWorkspaceStore(fixture.Binding.RootDirectory)).GetProjectAsync(project.SiteId);
        Assert.Single(reread.Value!.Pages);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Approve(page));
    }

    [Fact]
    public async Task Foreign_issuer_changed_arguments_and_binding_revocation_cannot_write()
    {
        await using var fixture = await Fixture.Create();
        var intent = SiteNativeWriteIntent.Create(fixture.Binding, "Fixture", "9to1-native", "fixture");
        var capability = await fixture.Approve(intent);
        var foreign = new SiteNativeWriteCoordinator(fixture, new(fixture.Resources, fixture.Permissions));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.ExecuteAsync(intent, capability));
        var changed = SiteNativeWriteIntent.Create(fixture.Binding, "Changed", "9to1-native", "changed");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(changed, capability));
        capability = await fixture.Approve(intent); // changed arguments consumed the prior handle as a negative abort
        fixture.RevokeOnRead = fixture.Reads + 2; // first owner check passes; canonical precommit binding fails
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(intent, capability));
        Assert.False(File.Exists(Path.Combine(fixture.Binding.RootDirectory, ".9to1-sites-index.json")));
        fixture.RevokeOnRead = int.MaxValue;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(intent, capability));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Admission_rejection_audit_recovers_only_same_negative_handle_without_a_project_write(bool rejectedClaim, bool afterPublication)
    {
        await using var fixture = await Fixture.Create(afterPublication);
        var intent = SiteNativeWriteIntent.Create(fixture.Binding, "Denied fixture", "9to1-native", "denied-fixture");
        var capability = await fixture.Approve(intent);
        fixture.Store.FailureCode = rejectedClaim ? "HOME_RESOURCE_CLAIM_REJECTED" : "HOME_RESOURCE_EXECUTION_ABORTED";
        fixture.Store.FailAuditWrites = rejectedClaim ? 2 : 1; // claim attempts its own negative audit before owner recovery
        if (rejectedClaim) fixture.DenyResources = true;
        else fixture.RevokeOnRead = fixture.Reads + 1;
        var admissionFailure = await Record.ExceptionAsync(() => fixture.Owner.ExecuteAsync(intent, capability));
        Assert.NotNull(admissionFailure);
        Assert.False(File.Exists(Path.Combine(fixture.Binding.RootDirectory, ".9to1-sites-index.json")));
        if (admissionFailure is SiteNativeAdmissionAuditPendingException pending)
        {
            Assert.Equal(capability.RequestId, pending.RequestId);
            var foreign = new SiteNativeWriteCoordinator(fixture, fixture.Home);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.RetryAdmissionAuditAsync(pending));
            fixture.Store.FailAuditWrites = 0;
            var original = await Assert.ThrowsAnyAsync<Exception>(() => fixture.Owner.RetryAdmissionAuditAsync(pending));
            Assert.IsNotType<SiteNativeAdmissionAuditPendingException>(original);
        }
        else
        {
            // A claim audit published before throwing can be acknowledged by the owner's
            // immediate audit-only retry. Preserve the original failure without a fake pending right.
            Assert.True(rejectedClaim && afterPublication);
            Assert.IsType<IOException>(admissionFailure);
        }
        var terminal = await fixture.Permissions.GetAuthorizationAsync(capability.RequestId);
        Assert.Equal(HomePermissionRequestState.Failed, terminal.State);
        Assert.Equal(fixture.Store.FailureCode, terminal.Code);
        Assert.False(File.Exists(Path.Combine(fixture.Binding.RootDirectory, ".9to1-sites-index.json")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(intent, capability));
        Assert.False(File.Exists(Path.Combine(fixture.Binding.RootDirectory, ".9to1-sites-index.json")));
    }

    [Theory]
    [InlineData(HomePermissionRequestState.Cancelled)]
    [InlineData(HomePermissionRequestState.Failed)]
    public async Task Admission_observes_existing_negative_Home_decision_without_rewriting_it(HomePermissionRequestState state)
    {
        await using var fixture = await Fixture.Create();
        var intent = SiteNativeWriteIntent.Create(fixture.Binding, "Stopped fixture", "9to1-native", "stopped-fixture");
        var capability = await fixture.Approve(intent);
        Assert.True((await fixture.Permissions.RecordExecutionAsync(capability.RequestId,
            new HomeExecutionOutcome(state, "ExistingHomeStop", "Existing canonical Home decision.", []))).Succeeded);
        var before = await fixture.Permissions.GetAuthorizationAsync(capability.RequestId);
        fixture.RevokeOnRead = fixture.Reads + 1;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(intent, capability));
        Assert.Equal(before, await fixture.Home.GetExecutionDecisionAsync(capability));
        Assert.False(File.Exists(Path.Combine(fixture.Binding.RootDirectory, ".9to1-sites-index.json")));
    }

    [Fact]
    public async Task Admission_does_not_treat_existing_success_as_negative_audit_acknowledgement()
    {
        await using var fixture = await Fixture.Create();
        var intent = SiteNativeWriteIntent.Create(fixture.Binding, "Uncertain fixture", "9to1-native", "uncertain-fixture");
        var capability = await fixture.Approve(intent);
        Assert.True((await fixture.Permissions.RecordExecutionAsync(capability.RequestId,
            new HomeExecutionOutcome(HomePermissionRequestState.Succeeded, "ExistingSuccess", "Existing canonical success.", []))).Succeeded);
        var before = await fixture.Permissions.GetAuthorizationAsync(capability.RequestId);
        fixture.RevokeOnRead = fixture.Reads + 1;
        var pending = await Assert.ThrowsAsync<SiteNativeAdmissionAuditPendingException>(() => fixture.Owner.ExecuteAsync(intent, capability));
        await Assert.ThrowsAsync<SiteNativeAdmissionAuditPendingException>(() => fixture.Owner.RetryAdmissionAuditAsync(pending));
        Assert.Equal(before, await fixture.Home.GetExecutionDecisionAsync(capability));
        Assert.False(File.Exists(Path.Combine(fixture.Binding.RootDirectory, ".9to1-sites-index.json")));
    }

    [Fact]
    public async Task Claimed_actor_with_different_profile_is_audited_failed_before_any_mutation()
    {
        await using var fixture = await Fixture.Create();
        fixture.UseMismatchedProfile();
        var intent = SiteNativeWriteIntent.Create(fixture.Binding, "Wrong profile", "9to1-native", "wrong-profile");
        var capability = await fixture.Approve(intent);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(intent, capability));
        var terminal = await fixture.Permissions.GetAuthorizationAsync(capability.RequestId);
        Assert.Equal(HomePermissionRequestState.Failed, terminal.State);
        Assert.Equal("SitesAdmissionRejected", terminal.Code);
        Assert.False(File.Exists(Path.Combine(fixture.Binding.RootDirectory, ".9to1-sites-index.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Committed_write_audit_recovery_preserves_exact_revision_and_never_replays(bool afterPublication)
    {
        await using var fixture = await Fixture.Create(afterPublication);
        var intent = SiteNativeWriteIntent.Create(fixture.Binding, "Audit fixture", "9to1-native", "audit-fixture");
        var capability = await fixture.Approve(intent);
        fixture.Store.FailAudit = true;
        var pending = await Assert.ThrowsAsync<SiteNativeWriteAuditPendingException>(() => fixture.Owner.ExecuteAsync(intent, capability));
        Assert.True(pending.CommitAcknowledged);
        var project = Assert.IsType<SiteProject>(pending.Result!.Value);
        var path = Path.Combine(fixture.Binding.RootDirectory, ".9to1-sites-index.json");
        var before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(intent, capability));
        var foreign = new SiteNativeWriteCoordinator(fixture, fixture.Home);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.RetryAuditAsync(pending));
        var finished = await fixture.Owner.RetryAuditAsync(pending);
        Assert.Equal(project.SiteId, finished.Value!.SiteId);
        Assert.Equal(project.Revision, finished.Value.Revision);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Equal(HomePermissionRequestState.Succeeded, (await fixture.Permissions.GetAuthorizationAsync(pending.RequestId)).State);
        Assert.Equal("SitesWriteCommitted", (await fixture.Permissions.GetAuthorizationAsync(pending.RequestId)).Code);
        _ = await fixture.Owner.RetryAuditAsync(pending); // exact audit retry is idempotent
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    private sealed class Fixture : ISiteNativeWorkspaceAuthority, ICanonicalResourceAccessResolver, IAsyncDisposable
    {
        public SiteNativeWorkspaceBinding Binding { get; private set; } = null!;
        public ResourceAuthorizationService Resources { get; private set; } = null!;
        public HomePermissionTrustService Permissions { get; private set; } = null!;
        public HomeResourceOperationBroker Home { get; private set; } = null!;
        public SiteNativeWriteCoordinator Owner { get; private set; } = null!;
        public int Reads { get; private set; }
        public int RevokeOnRead { get; set; } = int.MaxValue;
        public bool DenyResources { get; set; }
        public void UseMismatchedProfile() => Binding = Binding with { ProfileId = Guid.NewGuid().ToString() };
        public string ResourceKind => "files.item";
        public FaultStore Store { get; private set; } = null!;
        public static async Task<Fixture> Create(bool afterPublication = false)
        {
            var result = new Fixture();
            var root = Path.Combine(Path.GetTempPath(), "astra-sites-write-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var store = result.Store = new FaultStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), afterPublication);
            var actors = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var actor = await actors.GetCurrentAsync(default) ?? throw new InvalidOperationException("OS profile unavailable.");
            result.Binding = new(actor.ProfileId, actor.ActorId, actor.AuthenticationRevision, Guid.NewGuid(), "fixture-folder-revision", root);
            result.Resources = new(actors, [result, new SiteNativeProjectAccessResolver(result)]);
            result.Permissions = new(store, new SiteNativeActionPolicies().TryGet);
            result.Home = new(result.Resources, result.Permissions); result.Owner = new(result, result.Home); return result;
        }
        public async Task<HomeResourceExecutionCapability> Approve(SiteNativeWriteIntent intent)
        {
            var pending = await Home.AuthorizeAsync("sites", intent.ActionId, intent.Scopes, intent.Arguments,
                "Explicit fixture Sites edit", null, Binding.AuthenticationRevision);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await Home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments));
        }
        public Task<SiteNativeWorkspaceBinding?> GetCurrentAsync(CancellationToken ct = default)
            => Task.FromResult(++Reads >= RevokeOnRead ? null : Binding);
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
            => ValueTask.FromResult(new ResourceAccessDecision(!DenyResources && actor.ActorId == Binding.ActorId && actor.AuthenticationRevision == Binding.AuthenticationRevision &&
                scope.Id == Binding.FilesFolderId.ToString() && scope.Revision == Binding.FolderRevision && scope.Access == ResourceAccess.Write &&
                actionId is "sites.project.create" or "sites.project.save", "explicit-fixture-files-authority", actor.ActorId, scope.Revision, null));
        public ValueTask DisposeAsync() { Directory.Delete(Binding.RootDirectory, true); return ValueTask.CompletedTask; }
    }

    private sealed class FaultStore(IHomeCoreStateStore inner, bool afterPublication) : IHomeCoreStateStore
    {
        public bool FailAudit;
        public string FailureCode = "SitesWriteCommitted";
        public int FailAuditWrites;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            if ((FailAudit || FailAuditWrites > 0) && record.RecordType == "home.permissions-trust" && record.Payload.GetRawText().Contains(FailureCode, StringComparison.Ordinal))
            {
                FailAudit = false;
                if (FailAuditWrites > 0) FailAuditWrites--;
                if (afterPublication) _ = await inner.WriteAsync(record, expected, ct);
                throw new IOException("Injected actual audit storage failure.");
            }
            return await inner.WriteAsync(record, expected, ct);
        }
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
            => inner.WriteGuardedAsync(record, expected, actor, guard, ct);
    }
}
