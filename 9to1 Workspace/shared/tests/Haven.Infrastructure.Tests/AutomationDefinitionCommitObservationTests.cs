using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class AutomationDefinitionCommitObservationTests
{
    [Theory]
    [InlineData("cancel")]
    [InlineData("unexpected-fault")]
    [InlineData("direct-cancel")]
    [InlineData("direct-unexpected-fault")]
    public async Task Actual_SQL_confirmed_receipt_survives_audit_fault_then_supersession_without_claim_or_write_replay(string fault)
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-automation-confirmed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var durableHome = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var home = new CompletionFaultStore(durableHome);
            var services = new ServiceCollection().AddHavenInfrastructure().AddHavenAutomationDefinitionOwnership();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(home);
            await using var graph = services.BuildServiceProvider();
            // Resolve the real read/resource/broker graph first: a deferred owner must not create a constructor cycle.
            var broker = graph.GetRequiredService<HomeResourceOperationBroker>();
            var database = graph.GetRequiredService<SqliteDatabase>();
            var definitions = graph.GetRequiredService<AutomationRepository>();
            var tasks = graph.GetRequiredService<WorkspaceStateRepository>();
            Assert.Same(database, graph.GetRequiredService<ISqliteConnectionFactory>());
            Assert.Same(definitions, graph.GetRequiredService<IAutomationRepository>());
            Assert.Same(definitions, graph.GetRequiredService<IAutomationOwnerRepository>());
            Assert.Same(tasks, graph.GetRequiredService<IWorkspaceStateRepository>());
            Assert.Same(tasks, graph.GetRequiredService<IReusableTaskOwnerRepository>());
            await database.InitializeAsync(token); // The real global migration28, never hand-made test columns.
            var identity = await database.GetStoreIdentityAsync(token);
            var actors = graph.GetRequiredService<IAuthenticatedResourceActorSource>();
            var actor = (await actors.GetCurrentAsync(token))!;
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync(actor,
                "automations", identity.StoreId.ToString("D"), token);
            var permissions = graph.GetRequiredService<HomePermissionTrustService>();
            async Task<HomeResourceExecutionCapability> ApproveAsync(AutomationDefinitionChange intent)
            {
                var pending = await broker.AuthorizeForActorAsync(intent.OriginalActor, AutomationDefinitionChange.TargetAppID,
                    intent.ActionID, intent.Scopes, intent.Arguments, "Change this exact canonical definition", null,
                    "actual-automation-owner-test", token);
                Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
                Assert.True((await permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
                return Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, token));
            }
            var now = DateTimeOffset.UtcNow;
            var proposal = new AutomationDefinition(Guid.NewGuid(), "Original canonical definition", default,
                "Retain actual instruction", AutomationScheduleKind.Daily, "{}", null, null, false, now, now);
            var intent = AutomationDefinitionChange.Capture(identity.StoreId, actor, proposal, 0, AutomationDefinitionChangeKind.Create);
            var capability = await ApproveAsync(intent);
            // This decorator introduces ONLY lost return/observation transport. All SQL writes and receipts are real.
            var direct = fault.StartsWith("direct-", StringComparison.Ordinal);
            using var cancelledAudit = CancellationTokenSource.CreateLinkedTokenSource(token);
            var transport = new ReturnAndObservationFault(definitions, direct);
            if (direct) home.Arm(capability.RequestId, fault, cancelledAudit);
            var operation = new AutomationHomeDefinitionOperation(transport, tasks,
                graph.GetRequiredService<AutomationLocalStoreAuthority>(), broker);
            var uncertain = await operation.ExecuteAsync(intent, capability, token);
            if (direct)
            {
                Assert.True(uncertain.Committed);
                Assert.Equal("DefinitionCommittedAuditPending", uncertain.Code);
                Assert.NotNull(uncertain.AuditRecovery);
            }
            else { Assert.Null(uncertain.Committed); Assert.Equal("DefinitionOutcomeUnconfirmed", uncertain.Code); }
            var recovery = direct ? operation.PrepareObservation(intent, capability) : uncertain.ObservationRecovery!;
            Assert.NotNull(recovery);
            var actualFirst = await definitions.ObserveCommitAsync(proposal.Id, intent.OperationID, intent.PayloadSHA256, 0, token);
            Assert.Equal(AutomationCommitReceiptObservation.CurrentCommitted, actualFirst.Observation);
            Assert.Equal(1, actualFirst.Receipt!.CommittedRevision);
            if (!direct)
            {
                home.Arm(capability.RequestId, fault, cancelledAudit);
                if (fault == "cancel")
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recovery.ObserveAsync(cancelledAudit.Token));
                else await Assert.ThrowsAsync<NotSupportedException>(() => recovery.ObserveAsync(cancelledAudit.Token));
            }
            Assert.Equal(1, home.InjectedFailures);
            Assert.Equal(direct ? 0 : 2, transport.ObservationCalls);
            Assert.Equal(HomePermissionRequestState.Executing,
                (await broker.GetExecutionDecisionAsync(capability, token))!.State);
            var owned = (await definitions.GetOwnedAsync(proposal.Id, token))!;
            Assert.NotNull(owned); Assert.False(owned.RequiresRecovery);
            var supersedingIntent = AutomationDefinitionChange.Capture(identity.StoreId, actor,
                owned.Value with { Name = "Actually superseding canonical revision" }, owned.Value.Revision, AutomationDefinitionChangeKind.Update);
            var supersedingCapability = await ApproveAsync(supersedingIntent);
            var superseding = await graph.GetRequiredService<AutomationHomeDefinitionOperation>()
                .ExecuteAsync(supersedingIntent, supersedingCapability, token);
            Assert.True(superseding.Committed);
            Assert.Equal(2, superseding.Commit!.Revision);
            Assert.Equal(AutomationCommitReceiptObservation.OutcomeUnconfirmed,
                (await definitions.ObserveCommitAsync(proposal.Id, intent.OperationID, intent.PayloadSHA256, 0, token)).Observation);
            var beforeRecovery = await ReadCanonicalRowAsync(database, proposal.Id, token);
            var confirmed = await recovery.ObserveAsync(token);
            Assert.True(confirmed.Committed);
            Assert.Equal("DefinitionCommitted", confirmed.Code);
            Assert.Equal(intent.OperationID, confirmed.Commit!.OperationID);
            Assert.Equal(1, confirmed.Commit.Revision);
            Assert.Null(confirmed.AuditRecovery);
            Assert.Equal(direct ? 0 : 2, transport.ObservationCalls); // A known outcome never reobserves the superseded receipt.
            Assert.Equal(1, transport.CommitCalls); // Nor does it issue a second owner write/claim.
            Assert.Equal(beforeRecovery, await ReadCanonicalRowAsync(database, proposal.Id, token));
            Assert.Equal(HomePermissionRequestState.Succeeded,
                (await broker.GetExecutionDecisionAsync(capability, token))!.State);
            Assert.Single((await permissions.GetSnapshotAsync(cancellationToken: token)).RecentAuditEvents,
                item => item.RequestId == capability.RequestId && item.Kind == HomePermissionAuditKind.ExecutionCompleted);
            var again = await recovery.ObserveAsync(token);
            Assert.True(again.Committed); Assert.Equal(intent.OperationID, again.Commit!.OperationID);
            Assert.Equal(beforeRecovery, await ReadCanonicalRowAsync(database, proposal.Id, token));
            // The original operation's uncertain issued result cannot cause execution replay.
            if (direct) Assert.True((await operation.ExecuteAsync(intent, capability, token)).Committed);
            else Assert.Null((await operation.ExecuteAsync(intent, capability, token)).Committed);
            Assert.Equal(1, transport.CommitCalls);
            Assert.Equal(direct ? 0 : 2, transport.ObservationCalls);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Actual_canonical_read_paths_never_invoke_deferred_owner_issuer_accessor()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-automation-deferred-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = new SqliteDatabase(new Paths(root));
            await database.InitializeAsync(token);
            var calls = 0;
            AutomationLocalStoreAuthority Unavailable() { calls++; throw new InvalidOperationException("Read paths must not resolve owner execution."); }
            var definitions = new AutomationRepository(database, ownerAuthorityAccessor: Unavailable);
            var tasks = new WorkspaceStateRepository(database, ownerAuthorityAccessor: Unavailable);
            await definitions.GetStoreIdentityAsync(token);
            Assert.Null(await definitions.GetOwnedAsync(Guid.NewGuid(), token));
            await definitions.ListOwnedAsync(new(), token);
            await definitions.GetAllAsync(token);
            Assert.Null(await tasks.GetOwnedTaskAsync(Guid.NewGuid(), token));
            await tasks.ListOwnedTasksAsync(new(), token);
            await tasks.GetReusableTasksAsync(null, token);
            Assert.Equal(0, calls);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task<string> ReadCanonicalRowAsync(SqliteDatabase database, Guid id, CancellationToken token)
    {
        await using var connection = await database.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM automations WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var row = await command.ExecuteReaderAsync(token);
        Assert.True(await row.ReadAsync(token));
        var values = new SortedDictionary<string, string?>();
        for (var i = 0; i < row.FieldCount; i++) values.Add(row.GetName(i), row.IsDBNull(i) ? null : Convert.ToString(row.GetValue(i), System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(await row.ReadAsync(token));
        return JsonSerializer.Serialize(values);
    }

    private sealed class CompletionFaultStore(IHomeCoreStateStore actual) : IHomeCoreStateStore
    {
        private string? _requestID, _mode;
        private CancellationTokenSource? _auditCancellation;
        public int InjectedFailures { get; private set; }
        public void Arm(string requestID, string mode, CancellationTokenSource cancellation)
        { _requestID = requestID; _mode = mode; _auditCancellation = cancellation; }
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => actual.ReadAsync(ct);
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expectedRevision,
            AuthenticatedResourceActor expectedActor, IHomeStateCommitActorGuard guard, CancellationToken ct = default) =>
            actual.WriteGuardedAsync(record, expectedRevision, expectedActor, guard, ct);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRevision, CancellationToken ct = default)
        {
            var payload = record.Payload.GetRawText();
            if (_requestID is not null && payload.Contains(_requestID, StringComparison.Ordinal)
                && payload.Contains("DefinitionCommitted", StringComparison.Ordinal))
            {
                var mode = _mode; _requestID = null; InjectedFailures++;
                if (mode == "direct-cancel") { _auditCancellation!.Cancel(); throw new OperationCanceledException("Controlled direct-known audit cancellation.", _auditCancellation.Token); }
                if (mode == "cancel") { _auditCancellation!.Cancel(); ct.ThrowIfCancellationRequested(); }
                throw new NotSupportedException("Controlled durable Home audit transport fault after a real SQL receipt.");
            }
            return actual.WriteAsync(record, expectedRevision, ct);
        }
    }

    private sealed class ReturnAndObservationFault(IAutomationOwnerRepository actual, bool direct = false) : IAutomationOwnerRepository
    {
        public int CommitCalls { get; private set; }
        public int ObservationCalls { get; private set; }
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken ct) => actual.GetStoreIdentityAsync(ct);
        public Task<AutomationOwnerRead<AutomationDefinition>?> GetOwnedAsync(Guid id, CancellationToken ct) => actual.GetOwnedAsync(id, ct);
        public Task<AutomationLibraryPage<AutomationDefinition>> ListOwnedAsync(AutomationLibraryQuery query, CancellationToken ct) => actual.ListOwnedAsync(query, ct);
        public async Task<AutomationDefinitionCommitResult> CompareExchangeOwnedAsync(AutomationDefinitionChange change,
            IAutomationDefinitionCommitAdmission admission, CancellationToken ct)
        {
            CommitCalls++;
            var committed = await actual.CompareExchangeOwnedAsync(change, admission, ct);
            Assert.True(committed.Committed);
            if (direct) return committed;
            throw new IOException("Controlled lost return after actual SQL commit.");
        }
        public Task<AutomationCommitReceiptRead> ObserveCommitAsync(Guid id, Guid operation, string hash, long revision, CancellationToken ct)
        {
            ObservationCalls++;
            if (!direct && ObservationCalls == 1) throw new IOException("Controlled first receipt read transport loss.");
            return actual.ObserveCommitAsync(id, operation, hash, revision, ct);
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "app.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
