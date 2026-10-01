using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeStoreImportAuditTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Acknowledged_binding_survives_audit_failure_and_retry_never_rebinds(bool afterAuditCommit, bool unauthorizedAudit)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-home-import-audit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FaultStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), afterAuditCommit, unauthorizedAudit);
            var profiles = new HomeLocalProfileIdentity(store, new Principal());
            var evidence = new Evidence();
            var permissions = new HomePermissionTrustService(store, (_, _) =>
                new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true));
            var ownership = new HomeLocalStoreOwnership(store, profiles, evidence, permissions);
            var request = await ownership.RequestImportAsync("planner", "store", "host");
            Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            store.FailAudit = true;
            var pending = await Assert.ThrowsAsync<HomeStoreImportAuditPendingException>(() => ownership.CompleteImportAsync(request.RequestId));
            Assert.Equal(request.RequestId, pending.RequestId);
            Assert.Equal(request.RequestId, pending.Binding.ImportApprovalId);
            Assert.Equal(pending.Binding, await ownership.GetVerifiedAsync("planner", "store"));
            Assert.Equal(1, store.BindingWrites);
            var before = (await store.ReadAsync()).State!.Records.Single(record => record.RecordType == "home.local-store-ownership");
            Assert.Equal(afterAuditCommit ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Executing,
                (await permissions.GetAuthorizationAsync(request.RequestId)).State);
            evidence.ThrowOnRead = true; // An audit retry must not reacquire, import, or grant owning store access.
            Assert.Equal(pending.Binding, await ownership.RetryImportAuditAsync(request.RequestId));
            Assert.Equal(1, store.BindingWrites);
            var after = (await store.ReadAsync()).State!.Records.Single(record => record.RecordType == "home.local-store-ownership");
            Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.Payload.GetRawText(), after.Payload.GetRawText());
            Assert.Equal(HomePermissionRequestState.Succeeded, (await permissions.GetAuthorizationAsync(request.RequestId)).State);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ownership.CompleteImportAsync(request.RequestId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ownership.RetryImportAuditAsync(request.RequestId));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>("actual-fixture-principal");
    }
    private sealed class Evidence : IHomeLocalStoreEvidenceSource
    {
        public bool ThrowOnRead;
        public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string kind, string id, CancellationToken ct) => ThrowOnRead
            ? throw new InvalidOperationException("Audit retry must not read owning evidence.")
            : ValueTask.FromResult<HomeLocalStoreEvidence?>(new("planner", "store", "actual-captured-revision", false, false, true));
    }
    private sealed class FaultStore(IHomeCoreStateStore inner, bool afterCommit, bool unauthorizedAudit) : IHomeCoreStateStore
    {
        public bool FailAudit; public int BindingWrites;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            if (FailAudit && record.RecordType == "home.permissions-trust" && record.Payload.GetRawText().Contains("HOME_STORE_IMPORTED", StringComparison.Ordinal))
            {
                FailAudit = false;
                if (afterCommit) _ = await inner.WriteAsync(record, expected, ct);
                if (unauthorizedAudit) throw new UnauthorizedAccessException("Injected actual audit-store permission denial.");
                throw new IOException("Injected final audit storage failure.");
            }
            return await inner.WriteAsync(record, expected, ct);
        }
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
        {
            if (record.RecordType == "home.local-store-ownership") BindingWrites++;
            return inner.WriteGuardedAsync(record, expected, actor, guard, ct);
        }
    }
}
