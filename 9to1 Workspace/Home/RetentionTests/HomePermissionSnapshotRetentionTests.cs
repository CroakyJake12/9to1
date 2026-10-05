using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;
using HomeTrustLevel = HavenOS.Home.PermissionsTrustNotifications.HomeTrustLevel;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomePermissionSnapshotRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-home-permission-retention-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _stateStore;
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));

    public HomePermissionSnapshotRetentionTests()
    {
        Directory.CreateDirectory(_root);
        _stateStore = new FileHomeCoreStateStore(Path.Combine(_root, "home-state.json"));
    }

    [Fact]
    public async Task Snapshot_without_expired_grants_preserves_actual_bytes_and_record_revisions_after_reopen()
    {
        var path = Path.Combine(_root, "home-state.json");
        var service = CreateService();
        var empty = await service.GetSnapshotAsync();
        Assert.Empty(empty.Grants);
        Assert.False(File.Exists(path));
        var emptyState = await _stateStore.ReadAsync();
        Assert.True(emptyState.IsSuccess);
        Assert.Equal(0, emptyState.State!.Revision);
        Assert.Empty(emptyState.State.Records);

        var request = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var before = await _stateStore.ReadAsync();
        Assert.True(before.IsSuccess);
        var originalRecord = Assert.Single(before.State!.Records);
        var originalBytes = await File.ReadAllBytesAsync(path);
        var first = await service.GetSnapshotAsync();
        Assert.False(Assert.Single(first.Grants).IsRevoked);
        var reopened = CreateService();
        var second = await reopened.GetSnapshotAsync();
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(Assert.Single(first.Grants)),
            System.Text.Json.JsonSerializer.Serialize(Assert.Single(second.Grants)));
        var after = await _stateStore.ReadAsync();
        Assert.True(after.IsSuccess);
        Assert.Equal(before.State.Revision, after.State!.Revision);
        var retainedRecord = Assert.Single(after.State.Records);
        Assert.Equal(originalRecord.Revision, retainedRecord.Revision);
        Assert.Equal(originalRecord.Payload.GetRawText(), retainedRecord.Payload.GetRawText());
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Snapshot_expiration_persists_once_and_reopened_snapshot_preserves_same_expired_audit()
    {
        var path = Path.Combine(_root, "home-state.json");
        var service = CreateService();
        var request = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var before = await _stateStore.ReadAsync();
        Assert.True(before.IsSuccess);
        var originalRecord = Assert.Single(before.State!.Records);
        _clock.Advance(TimeSpan.FromDays(30).Add(TimeSpan.FromSeconds(1)));
        var expired = await service.GetSnapshotAsync();
        Assert.True(Assert.Single(expired.Grants).IsRevoked);
        var originalAudit = Assert.Single(expired.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.TrustExpired);
        var afterExpiry = await _stateStore.ReadAsync();
        Assert.True(afterExpiry.IsSuccess);
        Assert.Equal(before.State.Revision + 1, afterExpiry.State!.Revision);
        Assert.Equal(originalRecord.Revision + 1, Assert.Single(afterExpiry.State.Records).Revision);
        var expiredBytes = await File.ReadAllBytesAsync(path);
        var reopened = CreateService();
        var retained = await reopened.GetSnapshotAsync();
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(Assert.Single(expired.Grants)),
            System.Text.Json.JsonSerializer.Serialize(Assert.Single(retained.Grants)));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(originalAudit),
            System.Text.Json.JsonSerializer.Serialize(Assert.Single(retained.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.TrustExpired)));
        var afterReopen = await _stateStore.ReadAsync();
        Assert.True(afterReopen.IsSuccess);
        Assert.Equal(afterExpiry.State.Revision, afterReopen.State!.Revision);
        Assert.Equal(Assert.Single(afterExpiry.State.Records).Revision, Assert.Single(afterReopen.State.Records).Revision);
        Assert.Equal(expiredBytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await reopened.AuthorizeAsync(Request("boards.read", sessionId: "session-after-expiry"))).State);
    }

    private HomePermissionTrustService CreateService(string[]? knownActions = null)
    {
        var actions = (knownActions ?? ["boards.read", "boards.edit", "boards.delete"])
            .ToHashSet(StringComparer.Ordinal);
        return new HomePermissionTrustService(_stateStore, (target, action) =>
            target == "app.boards" && actions.Contains(action)
                ? new HomePermissionActionPolicy(
                    action == "boards.delete" ? HomePermissionRisk.High : HomePermissionRisk.Routine,
                    IsReversible: action != "boards.delete",
                    HasExternalSideEffects: false)
                : null, _clock);
    }

    private static HomePermissionRequestSubmission Request(
        string action,
        string sessionId = "session-1",
        string objectId = "board-42") => new(
        null,
        new HomePermissionCallerIdentity("script:sha256:test", "Board assistant", "local project", "hash-v1", false),
        sessionId,
        new HomePermissionScope("app.boards", action, [new HomeObjectReference("board", objectId)]),
        new HomePermissionImpactPreview(["board"], 1, [new HomeObjectReference("board", objectId)], false));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan amount) => Now += amount;
    }
}
