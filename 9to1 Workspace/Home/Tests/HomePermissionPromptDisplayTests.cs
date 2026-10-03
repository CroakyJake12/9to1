using System.Security.Cryptography;
using System.Text.Json;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
using HomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;

namespace HavenOS.Home.Tests;

/// <summary>Real broker/flow logic with a revision-checked detached in-memory store.
/// These tests do not claim native display; the native surface cohort exercises actual visibility.</summary>
public sealed class HomePermissionPromptDisplayTests
{
    [Fact]
    public async Task Creation_is_request_received_only_and_exact_display_ack_is_idempotent_without_approval()
    {
        var service = Service();
        var request = await service.AuthorizeAsync(Submission());
        var original = (await service.ReadRequestObservationAsync(request.RequestId))!;
        var before = await service.GetSnapshotAsync();
        Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
        Assert.Single(before.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.RequestReceived);
        Assert.DoesNotContain(before.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.ApprovalPromptShown);
        var wrong = await service.AcknowledgePromptDisplayedAsync(request.RequestId, Digest(original with { Impact = original.Impact with { ChangePreview = "different" } }));
        Assert.False(wrong.Succeeded); Assert.Equal("HOME_PROMPT_REQUEST_CHANGED", wrong.Code);
        Assert.DoesNotContain((await service.GetSnapshotAsync()).RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.ApprovalPromptShown);
        Assert.True((await service.AcknowledgePromptDisplayedAsync(request.RequestId, Digest(original))).Succeeded);
        Assert.True((await service.AcknowledgePromptDisplayedAsync(request.RequestId, Digest(original))).Succeeded);
        var after = await service.GetSnapshotAsync();
        var audit = Assert.Single(after.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.ApprovalPromptShown);
        Assert.Equal("HOME_PROMPT_DISPLAY_ACKNOWLEDGED", audit.ResultCode);
        Assert.Equal("write", audit.TargetAppId); Assert.Equal("write.file.save", audit.ActionName);
        Assert.Equal("file-42", Assert.Single(audit.AffectedObjects).ObjectId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, (await service.GetAuthorizationAsync(request.RequestId)).State);
        Assert.False((await service.BeginExecutionAsync(request.RequestId)).IsAllowed);
        Assert.Empty(after.Grants);
        Assert.DoesNotContain(after.RecentAuditEvents, item => item.Kind is HomePermissionAuditKind.DecisionMade or HomePermissionAuditKind.ExecutionStarted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_terminal_request_never_records_display(bool terminal)
    {
        var service = Service(); var request = await service.AuthorizeAsync(Submission());
        var original = (await service.ReadRequestObservationAsync(request.RequestId))!;
        if (terminal) Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.Decline)).Succeeded);
        var observed = await service.AcknowledgePromptDisplayedAsync(terminal ? request.RequestId : "missing", Digest(original));
        Assert.False(observed.Succeeded); Assert.Equal("HOME_REQUEST_NOT_PENDING", observed.Code);
        Assert.DoesNotContain((await service.GetSnapshotAsync()).RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.ApprovalPromptShown);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("false")]
    [InlineData("fault")]
    [InlineData("closed")]
    public async Task Unavailable_or_closed_presenter_keeps_actual_request_pending_without_execution(string mode)
    {
        var service = Service(); var request = await service.AuthorizeAsync(Submission());
        var presenter = mode == "missing" ? null : new Presenter((_, _) => mode switch
        {
            "fault" => throw new IOException("controlled native host unavailable"),
            "false" => ValueTask.FromResult(false),
            _ => ValueTask.FromResult(true)
        });
        var result = await new HomeApprovalPromptFlow(service, presenter).ReviewPendingAsync(request.RequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, result.State); Assert.False(result.IsAllowed);
        Assert.Equal("HOME_PERMISSION_REQUIRED", result.Code); Assert.Null(result.TrustLevel);
        Assert.Equal(HomePermissionRequestState.PendingApproval, (await service.GetAuthorizationAsync(request.RequestId)).State);
        var snapshot = await service.GetSnapshotAsync();
        Assert.Empty(snapshot.Grants);
        Assert.DoesNotContain(snapshot.RecentAuditEvents, item => item.Kind is HomePermissionAuditKind.ApprovalPromptShown or HomePermissionAuditKind.DecisionMade or HomePermissionAuditKind.ExecutionStarted);
    }

    [Fact]
    public async Task Display_only_observation_cannot_grant_or_dispatch_the_action()
    {
        var service = Service(); var request = await service.AuthorizeAsync(Submission());
        var presenter = new Presenter(async (id, ct) =>
        {
            var observed = (await service.ReadRequestObservationAsync(id, ct))!;
            Assert.True((await service.AcknowledgePromptDisplayedAsync(id, Digest(observed), ct)).Succeeded);
            return true;
        });
        var result = await new HomeApprovalPromptFlow(service, presenter).ReviewPendingAsync(request.RequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, result.State); Assert.False(result.IsAllowed);
        Assert.Equal(1, presenter.Calls);
        Assert.False((await service.BeginExecutionAsync(request.RequestId)).IsAllowed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_actual_broker_decision_returns_allowed_or_denied_and_flow_does_not_begin_execution(bool accept)
    {
        var service = Service(); var request = await service.AuthorizeAsync(Submission());
        var presenter = new Presenter(async (id, ct) =>
        {
            Assert.True((await service.DecideAsync(id, accept ? HomeApprovalChoice.Accept : HomeApprovalChoice.Decline,
                cancellationToken: ct)).Succeeded); // Controlled trusted-user decision, not native-display acceptance.
            return true;
        });
        var result = await new HomeApprovalPromptFlow(service, presenter).ReviewPendingAsync(request.RequestId);
        Assert.Equal(accept, result.IsAllowed);
        Assert.Equal(accept ? HomePermissionRequestState.Approved : HomePermissionRequestState.Denied, result.State);
        Assert.DoesNotContain((await service.GetSnapshotAsync()).RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.ExecutionStarted);
    }

    [Fact]
    public async Task Presenter_ignoring_cancellation_cannot_return_a_successful_decision()
    {
        var service = Service(); var request = await service.AuthorizeAsync(Submission());
        using var lifetime = new CancellationTokenSource();
        var presenter = new Presenter((_, _) => { lifetime.Cancel(); return ValueTask.FromResult(true); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new HomeApprovalPromptFlow(service, presenter).ReviewPendingAsync(request.RequestId, lifetime.Token).AsTask());
        Assert.Equal(HomePermissionRequestState.PendingApproval, (await service.GetAuthorizationAsync(request.RequestId)).State);
    }

    [Fact]
    public async Task Unknown_request_never_opens_a_prompt_or_creates_a_request()
    {
        var service = Service(); var presenter = new Presenter((_, _) => throw new InvalidOperationException("must not run"));
        var result = await new HomeApprovalPromptFlow(service, presenter).ReviewPendingAsync("missing");
        Assert.False(result.IsAllowed); Assert.Equal("HOME_PERMISSION_REQUEST_NOT_FOUND", result.Code);
        Assert.Equal(0, presenter.Calls); Assert.Empty((await service.GetSnapshotAsync()).RecentAuditEvents);
    }

    private static string Digest(HomePermissionRequest request) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
    private static HomePermissionTrustService Service() => new(new Store(), (app, action) =>
        app == "write" && action == "write.file.save" ? new(HomePermissionRisk.Elevated, true, false, true) : null);
    private static HomePermissionRequestSubmission Submission() => new(null,
        new("controlled-caller", "Controlled caller", "controlled-origin", "controlled-session", true), "controlled-session",
        new("write", "write.file.save", [new("files.item", "file-42")]),
        new(["files.item"], 1, [new("files.item", "file-42")], false, "Save the exact reviewed file"));

    private sealed class Presenter(Func<string, CancellationToken, ValueTask<bool>> present) : IHomeApprovalPromptPresenter
    {
        public int Calls;
        public ValueTask<bool> ShowPendingRequestAsync(string id, CancellationToken ct) { Calls++; return present(id, ct); }
    }

    private sealed class Store : IHomeCoreStateStore
    {
        private HomeCoreStoredState _state = new(1, 0, []);
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(HomeStateReadResult.Success(Detached())); }
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var previous = _state.Records.SingleOrDefault(item => item.RecordId == record.RecordId);
            if ((previous?.Revision ?? 0) != expectedRecordRevision)
                return Task.FromResult(HomeStateWriteResult.Failed(new(HomeCoreErrorCode.HomeStateConflict,
                    "Controlled revision conflict", record.RecordId, true)));
            var saved = record with { Revision = expectedRecordRevision + 1, Payload = record.Payload.Clone() };
            _state = _state with { Revision = _state.Revision + 1, Records = _state.Records.Where(item => item.RecordId != record.RecordId).Append(saved).ToArray() };
            return Task.FromResult(HomeStateWriteResult.Success(Detached()));
        }
        private HomeCoreStoredState Detached() => _state with { Records = _state.Records.Select(record => record with { Payload = record.Payload.Clone() }).ToArray() };
    }
}
