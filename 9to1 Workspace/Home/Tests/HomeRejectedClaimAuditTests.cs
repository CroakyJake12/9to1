using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeRejectedClaimAuditTests
{
    [Theory]
    [InlineData("io", false, false)]
    [InlineData("identity", false, false)]
    [InlineData("cancel", false, false)]
    [InlineData("io", true, false)]
    [InlineData("cancel", true, true)]
    public async Task Consumed_claim_failure_is_audited_without_issuing_or_restoring_owner_permission(string failure, bool failAudit, bool unauthorizedAudit)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-claim-audit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = new AuditFaultStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            var actors = new Actors(); var owner = new Owner();
            var permissions = new HomePermissionTrustService(state, (_, _) => new(
                HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true));
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(actors, [owner]), permissions);
            ResourceScope[] scopes = [new("test-owner", "object", "revision", ResourceAccess.Write)];
            var args = JsonSerializer.SerializeToElement(new { operation = "edit" });
            var request = await broker.AuthorizeAsync("owner", "owner.edit", scopes, args, "Review exact edit", null, "session");
            Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            var capability = Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(request.RequestId, args));
            using var cancellation = new CancellationTokenSource();
            Exception expected = failure switch
            {
                "identity" => new InvalidOperationException("Actual root identity changed."),
                "cancel" => new OperationCanceledException(cancellation.Token),
                _ => new IOException("Owning resource could not be read.")
            };
            owner.Error = expected; state.Fail = failAudit; state.UnauthorizedAudit = unauthorizedAudit;
            var observed = await Record.ExceptionAsync(() => broker.ClaimExecutionAsync(capability, "owner", "owner.edit", scopes, args, cancellation.Token));
            Assert.Same(expected, observed);
            Assert.Equal(failAudit ? HomePermissionRequestState.Executing : HomePermissionRequestState.Failed,
                (await permissions.GetAuthorizationAsync(request.RequestId)).State);
            var calls = owner.Calls;
            Assert.True((await broker.RetryRejectedClaimAuditAsync(capability)).Succeeded);
            Assert.Equal(calls, owner.Calls); // Recovery must not re-resolve or execute the owner.
            var authorization = await permissions.GetAuthorizationAsync(request.RequestId);
            Assert.Equal(HomePermissionRequestState.Failed, authorization.State);
            Assert.Equal("HOME_RESOURCE_CLAIM_REJECTED", authorization.Code);
            owner.Error = null;
            Assert.Null(await broker.ClaimExecutionAsync(capability, "owner", "owner.edit", scopes, args));
            Assert.Null(await broker.BeginExecutionCapabilityAsync(request.RequestId, args));
            Assert.False((await broker.CompleteExecutionAsync(capability,
                new(HomePermissionRequestState.Succeeded, "FORGED", "No owner received a claim.", []))).Succeeded);
            var other = new HomeResourceOperationBroker(new ResourceAuthorizationService(actors, [owner]), permissions);
            Assert.False((await other.RetryRejectedClaimAuditAsync(capability)).Succeeded);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("verified-fixture", "profile", null, null, "session"));
    }
    private sealed class Owner : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "test-owner";
        public Exception? Error; public int Calls;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
        {
            Calls++;
            if (Error is not null) throw Error;
            return ValueTask.FromResult(new ResourceAccessDecision(true, "exact-owning-fixture", actor.ActorId, "revision", null));
        }
    }
    private sealed class AuditFaultStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public bool Fail; public bool UnauthorizedAudit;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            if (Fail && record.Payload.GetRawText().Contains("HOME_RESOURCE_CLAIM_REJECTED", StringComparison.Ordinal))
            { Fail = false; if (UnauthorizedAudit) throw new UnauthorizedAccessException("Injected actual audit-store permission denial."); throw new IOException("Injected unavailable audit storage."); }
            return inner.WriteAsync(record, expected, ct);
        }
    }
}
