using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeCompletionAuditRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_retains_exact_detached_owner_outcome_across_audit_publication_fault(bool afterPublication)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-completion-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FaultStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), afterPublication);
            var owner = new Owner(); var actors = new Actors();
            var permissions = new HomePermissionTrustService(store, (_, _) => new(
                HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true));
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(actors, [owner]), permissions);
            ResourceScope[] scopes = [new("test-owner", "object", "revision", ResourceAccess.Write)];
            var arguments = JsonSerializer.SerializeToElement(new { edit = "observed" });
            var request = await broker.AuthorizeAsync("owner", "owner.edit", scopes, arguments, "Exact edit", null, "session");
            Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            var handle = Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(request.RequestId, arguments));
            Assert.False((await broker.RetryCompletionAuditAsync(handle)).Succeeded);
            Assert.NotNull(await broker.ClaimExecutionAsync(handle, "owner", "owner.edit", scopes, arguments));
            Assert.False((await broker.RetryCompletionAuditAsync(handle)).Succeeded);
            HomeObjectReference[] affected = [new("test-owner", "object")];
            var outcome = new HomeExecutionOutcome(HomePermissionRequestState.PartiallyCompleted, "OWNER_OBSERVED", "Actual partial owner outcome.", affected);
            store.Fail = true;
            await Assert.ThrowsAsync<IOException>(() => broker.CompleteExecutionAsync(handle, outcome));
            affected[0] = new("test-owner", "forged-after-await");
            var calls = owner.Calls;
            var foreign = new HomeResourceOperationBroker(new ResourceAuthorizationService(actors, [owner]), permissions);
            Assert.False((await foreign.RetryCompletionAuditAsync(handle)).Succeeded);
            Assert.Null(await foreign.GetExecutionDecisionAsync(handle));
            Assert.True((await broker.RetryCompletionAuditAsync(handle)).Succeeded);
            Assert.True((await broker.RetryCompletionAuditAsync(handle)).Succeeded);
            Assert.Equal(calls, owner.Calls);
            Assert.False((await broker.CompleteExecutionAsync(handle, outcome)).Succeeded);
            Assert.Null(await broker.ClaimExecutionAsync(handle, "owner", "owner.edit", scopes, arguments));
            Assert.Equal(HomePermissionRequestState.PartiallyCompleted, (await permissions.GetAuthorizationAsync(request.RequestId)).State);
            Assert.Equal(HomePermissionRequestState.PartiallyCompleted, (await broker.GetExecutionDecisionAsync(handle))!.State);
            var audit = Assert.Single((await permissions.GetSnapshotAsync()).RecentAuditEvents,
                item => item.RequestId == request.RequestId && item.Kind == HomePermissionAuditKind.ExecutionCompleted);
            Assert.Equal("OWNER_OBSERVED", audit.ResultCode);
            Assert.Equal("object", Assert.Single(audit.AffectedObjects).ObjectId);
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
        public int Calls;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
        { Calls++; return ValueTask.FromResult(new ResourceAccessDecision(true, "owning-fixture", actor.ActorId, "revision", null)); }
    }
    private sealed class FaultStore(IHomeCoreStateStore inner, bool afterPublication) : IHomeCoreStateStore
    {
        public bool Fail;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            if (Fail && record.Payload.GetRawText().Contains("OWNER_OBSERVED", StringComparison.Ordinal))
            {
                Fail = false;
                if (afterPublication) await inner.WriteAsync(record, expected, ct);
                throw new IOException("Injected durable completion acknowledgement fault.");
            }
            return await inner.WriteAsync(record, expected, ct);
        }
    }
}
