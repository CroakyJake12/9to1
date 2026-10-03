using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeRejectedBeginAuditTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public async Task Admission_store_failure_never_issues_owner_capability_and_recovery_only_records_negative_outcome(
        bool afterPublication, bool denyAudit, bool cancelled)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-begin-audit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FaultStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            var owner = new Owner(); var actors = new Actors();
            var permissions = new HomePermissionTrustService(store, (_, _) => new(
                HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true));
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(actors, [owner]), permissions);
            ResourceScope[] scopes = [new("test-owner", "canonical", "revision", ResourceAccess.Write)];
            var arguments = JsonSerializer.SerializeToElement(new { operation = "exact edit" });
            var request = await broker.AuthorizeAsync("owner", "owner.edit", scopes, arguments, "Exact edit", null, "session");
            Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            using var cancellation = new CancellationTokenSource();
            Exception original = cancelled ? new OperationCanceledException(cancellation.Token) : new IOException("Dispatch state storage failed.");
            store.Failure = original; store.AfterPublication = afterPublication; store.DenyAudit = denyAudit;
            Assert.Same(original, await Record.ExceptionAsync(() => broker.BeginExecutionCapabilityAsync(request.RequestId, arguments)));
            Assert.Equal(denyAudit ? (afterPublication ? HomePermissionRequestState.Executing : HomePermissionRequestState.Approved)
                : HomePermissionRequestState.Failed, (await permissions.GetAuthorizationAsync(request.RequestId)).State);
            var calls = owner.Calls;
            var foreign = new HomeResourceOperationBroker(new ResourceAuthorizationService(actors, [owner]), permissions);
            Assert.False((await foreign.RetryRejectedBeginAuditAsync(request.RequestId)).Succeeded);
            if (denyAudit) Assert.True((await broker.RetryRejectedBeginAuditAsync(request.RequestId)).Succeeded);
            Assert.Equal(calls, owner.Calls);
            var final = await permissions.GetAuthorizationAsync(request.RequestId);
            Assert.Equal(HomePermissionRequestState.Failed, final.State);
            Assert.Equal("HOME_RESOURCE_BEGIN_REJECTED", final.Code);
            Assert.Null(await broker.BeginExecutionCapabilityAsync(request.RequestId, arguments));
            Assert.False((await broker.RetryRejectedBeginAuditAsync(request.RequestId)).Succeeded);
            Assert.Equal(calls, owner.Calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("controlled", "profile", null, null, "session"));
    }
    private sealed class Owner : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "test-owner";
        public int Calls;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken ct)
        { Calls++; return ValueTask.FromResult(new ResourceAccessDecision(true, "ControlledOwner", actor.ActorId, scope.Revision, null)); }
    }
    private sealed class FaultStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public Exception? Failure; public bool AfterPublication; public bool DenyAudit;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            var json = record.Payload.GetRawText();
            if (DenyAudit && json.Contains("HOME_RESOURCE_BEGIN_REJECTED", StringComparison.Ordinal))
            { DenyAudit = false; throw new UnauthorizedAccessException("Actual audit store boundary denied."); }
            if (Failure is { } failure && json.Contains("HOME_EXECUTION_STARTED", StringComparison.Ordinal))
            {
                Failure = null;
                if (AfterPublication) Assert.True((await inner.WriteAsync(record, expected, ct)).IsSuccess);
                throw failure;
            }
            return await inner.WriteAsync(record, expected, ct);
        }
    }
}
