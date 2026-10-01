using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeUnclaimedExecutionAbortTests
{
    [Fact]
    public async Task Issuer_abort_consumes_unclaimed_handle_and_blocks_late_claim_forged_completion_and_foreign_audit()
    {
        using var fixture = new Fixture(); var capability = await fixture.BeginAsync();
        var calls = fixture.Owner.Calls;
        var foreign = new HomeResourceOperationBroker(new ResourceAuthorizationService(new Actors(), [fixture.Owner]), fixture.Permissions);
        Assert.Equal("HOME_EXECUTION_ABORT_NOT_OWNED", (await foreign.AbortUnclaimedExecutionAsync(capability)).Code);
        Assert.True((await fixture.Broker.AbortUnclaimedExecutionAsync(capability)).Succeeded);
        var actual = await fixture.Permissions.GetAuthorizationAsync(capability.RequestId);
        Assert.Equal(HomePermissionRequestState.Failed, actual.State); Assert.Equal("HOME_RESOURCE_EXECUTION_ABORTED", actual.Code);
        Assert.Null(await fixture.Broker.ClaimExecutionAsync(capability, "owner", "owner.edit", fixture.Scopes, fixture.Arguments));
        Assert.False((await fixture.Broker.CompleteExecutionAsync(capability, new(HomePermissionRequestState.Succeeded, "FORGED", "No claim was issued.", []))).Succeeded);
        Assert.False((await fixture.Broker.RetryRejectedClaimAuditAsync(capability)).Succeeded);
        Assert.True((await fixture.Broker.AbortUnclaimedExecutionAsync(capability)).Succeeded);
        Assert.Null(await fixture.Broker.BeginExecutionCapabilityAsync(capability.RequestId, fixture.Arguments));
        Assert.Equal(calls, fixture.Owner.Calls);
    }
    [Fact]
    public async Task Actual_claim_removal_wins_before_async_authority_check_and_abort_cannot_report_no_claim()
    {
        using var fixture = new Fixture(); var capability = await fixture.BeginAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Owner.Wait = async () => { entered.TrySetResult(); await release.Task; };
        var claim = fixture.Broker.ClaimExecutionAsync(capability, "owner", "owner.edit", fixture.Scopes, fixture.Arguments);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("HOME_EXECUTION_ABORT_NOT_OWNED", (await fixture.Broker.AbortUnclaimedExecutionAsync(capability)).Code);
        }
        finally { release.TrySetResult(); }
        Assert.NotNull(await claim);
        Assert.Equal("HOME_EXECUTION_ABORT_NOT_OWNED", (await fixture.Broker.AbortUnclaimedExecutionAsync(capability)).Code);
        Assert.True((await fixture.Broker.CompleteExecutionAsync(capability,
            new(HomePermissionRequestState.Failed, "CONTROLLED_OWNER_FAILURE", "The controlled owner did not mutate.", []))).Succeeded);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Abort_audit_failure_retains_only_same_negative_outcome_before_or_after_actual_publication(bool after, bool denied)
    {
        using var fixture = new Fixture(); var capability = await fixture.BeginAsync();
        Exception expected = denied ? new UnauthorizedAccessException("Audit permission denied.") : new IOException("Audit acknowledgement unavailable.");
        fixture.Store.Failure = expected; fixture.Store.After = after;
        Assert.Same(expected, await Record.ExceptionAsync(() => fixture.Broker.AbortUnclaimedExecutionAsync(capability)));
        Assert.Equal(after ? HomePermissionRequestState.Failed : HomePermissionRequestState.Executing,
            (await fixture.Permissions.GetAuthorizationAsync(capability.RequestId)).State);
        Assert.Null(await fixture.Broker.ClaimExecutionAsync(capability, "owner", "owner.edit", fixture.Scopes, fixture.Arguments));
        Assert.False((await fixture.Broker.RetryRejectedClaimAuditAsync(capability)).Succeeded);
        var calls = fixture.Owner.Calls;
        Assert.True((await fixture.Broker.AbortUnclaimedExecutionAsync(capability)).Succeeded);
        Assert.Equal(calls, fixture.Owner.Calls);
        Assert.Equal("HOME_RESOURCE_EXECUTION_ABORTED", (await fixture.Permissions.GetAuthorizationAsync(capability.RequestId)).Code);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "astra-unclaimed-abort-" + Guid.NewGuid().ToString("N"));
        public Fixture()
        {
            Store = new(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            Permissions = new(Store, (_, _) => new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true));
            Broker = new(new ResourceAuthorizationService(new Actors(), [Owner]), Permissions);
        }
        public Owner Owner { get; } = new(); public FaultStore Store { get; } public HomePermissionTrustService Permissions { get; }
        public HomeResourceOperationBroker Broker { get; }
        public ResourceScope[] Scopes { get; } = [new("test-owner", "existing", "revision", ResourceAccess.Write)];
        public JsonElement Arguments { get; } = JsonSerializer.SerializeToElement(new { operation = "exact edit" });
        public async Task<HomeResourceExecutionCapability> BeginAsync()
        {
            var request = await Broker.AuthorizeAsync("owner", "owner.edit", Scopes, Arguments, "Review exact edit", null, "session");
            Assert.True((await Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await Broker.BeginExecutionCapabilityAsync(request.RequestId, Arguments));
        }
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("controlled", "profile", null, null, "session"));
    }
    private sealed class Owner : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "test-owner"; public int Calls; public Func<Task>? Wait;
        public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken ct)
        { Calls++; if (Wait is not null) await Wait(); return new(true, "ControlledOwner", actor.ActorId, scope.Revision, null); }
    }
    private sealed class FaultStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public Exception? Failure; public bool After;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            if (Failure is { } failure && record.Payload.GetRawText().Contains("HOME_RESOURCE_EXECUTION_ABORTED", StringComparison.Ordinal))
            {
                Failure = null;
                if (After) Assert.True((await inner.WriteAsync(record, expected, ct)).IsSuccess);
                throw failure;
            }
            return await inner.WriteAsync(record, expected, ct);
        }
    }
}
