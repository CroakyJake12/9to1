using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Cui.AI;
using NineToOne.Os.Shell;
using Xunit;

namespace NineToOne.Os.Shell.Tests;

public sealed class ShellSemanticTests
{
    [Fact]
    public async Task ExactHomeReviewStagesOnlyVolatilePreviewAndKeepMakesOneDurableRevision()
    {
        using var f = new Fixture(); var before = await f.Configuration.GetAsync();
        var plan = await f.Owner.PrepareAsync(before, [new ShellDuplicateSpaceCommand("Study"), new ShellAddLayerCommand("Tools"), new ShellAddPageCommand("Coursework")]);
        var pageId = plan.Proposed.GlobalDesktopSurface!.ActivePageId;
        var bytes = await f.ShellRecordAsync();
        var pending = await f.Owner.ApplyAsync(plan);
        Assert.Equal("ApprovalRequired", pending.Code); Assert.Null((await f.Configuration.GetAsync()).Preview);
        Assert.Equal(bytes, await f.ShellRecordAsync());
        var id = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        var applied = await f.Owner.ApplyAsync(plan, id);
        Assert.True(applied.Succeeded); Assert.Equal("PreviewPrepared", applied.Code);
        Assert.Equal(bytes, await f.ShellRecordAsync());
        var preview = await f.Configuration.GetAsync();
        Assert.Equal(pageId, preview.Effective.GlobalDesktopSurface!.ActivePageId);
        Assert.Equal(before.Stored.Revision, preview.Stored.Revision);
        var saved = await f.Configuration.KeepAsync(applied.Value!.PreviewId!.Value);
        Assert.Equal(before.Stored.Revision + 1, saved.Stored.Revision); Assert.Equal("Study", saved.Stored.Current.ActiveSpace.Name);
        await Assert.ThrowsAsync<ShellConfigurationConflictException>(() => f.Owner.ApplyAsync(plan, id));
    }
    [Fact]
    public async Task ActualSharedBarDelegatesOneHomeReviewAndKeepsTruthfulPreviewOutcome()
    {
        using var f = new Fixture(); using var actions = new ShellAppAiActions(f.Configuration, f.Owner);
        var context = new ShellAppAiContext(f.Configuration); var captured = await context.CaptureAsync(default);
        var generic = new ForbiddenGenericApproval();
        using var bar = new FloatingAiBarState(new AppAiCoordinator(context, actions, generic, new UnusedDulche(), generic, actionGraph: new Graph()));
        var request = new AppAiActionRequest(ShellSemanticFeatureProvider.AppId, ShellSemanticFeatureProvider.PreviewAction,
            JsonSerializer.SerializeToElement(new { Commands = new ShellSemanticCommand[] { new ShellAddLayerCommand("Coding"), new ShellLayerPresentationCommand(64, 10, 12, 8, .9) } }),
            "forged-token", "shell", AppAiAccessMode.Write, captured.Revision);
        Assert.False((await bar.ExecuteActionAsync(request)).Succeeded);
        bar.SetWriteMode(); var pending = await bar.ExecuteActionAsync(request);
        Assert.Equal("approval-pending", pending.ErrorCode); Assert.Null((await f.Configuration.GetAsync()).Preview);
        Assert.Null(actions.PendingActionRequest!.ApprovalToken);
        Assert.True((await f.Permissions.DecideAsync(actions.PendingReviewRequestId!, HomeApprovalChoice.Accept)).Succeeded);
        Assert.True((await bar.ExecuteActionAsync(actions.PendingActionRequest!)).Succeeded);
        var shown = await f.Configuration.GetAsync(); Assert.NotNull(shown.Preview); Assert.Equal(1, shown.Stored.Revision);
        Assert.Equal(64, shown.Effective.ActiveSpace.Taskbar.Layers.Single(l => l.Id == shown.Effective.ActiveSpace.Taskbar.ActiveLayerId).Presentation.Thickness);
        Assert.Equal(0, generic.Calls);
        await f.Configuration.RevertAsync(shown.Preview!.Id); Assert.Equal(1, (await f.Configuration.GetAsync()).Stored.Revision);
    }
    [Fact]
    public async Task ReplacedOrExpiredManualPreviewInvalidatesEarlierApprovedSemanticIntent()
    {
        using var f = new Fixture(); var before = await f.Configuration.GetAsync();
        var plan = await f.Owner.PrepareAsync(before, [new ShellRenameSpaceCommand("Old intent")]);
        var pending = await f.Owner.ApplyAsync(plan); var id = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        var manual = await f.Configuration.PreviewAsync(before.Stored, ShellEdits.AddLayer(before.Effective, "Manual"), TimeSpan.FromSeconds(5));
        f.Clock.Utc += TimeSpan.FromSeconds(6);
        Assert.Null((await f.Configuration.GetAsync()).Preview);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owner.ApplyAsync(plan, id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Configuration.KeepAsync(manual.Preview!.Id));
        Assert.Equal(before.Stored.Revision, (await f.Configuration.GetAsync()).Stored.Revision);
    }
    [Fact]
    public async Task ApprovedOldActorIntentCannotStageOrSaveAfterSameProfileSessionChange()
    {
        using var f = new Fixture(); var before = await f.Configuration.GetAsync();
        var plan = await f.Owner.PrepareAsync(before, [new ShellRenameSpaceCommand("Old actor")]);
        var pending = await f.Owner.ApplyAsync(plan); var id = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        var bytes = await File.ReadAllBytesAsync(f.StatePath);
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "changed" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Owner.ApplyAsync(plan, id));
        Assert.Null((await f.Configuration.GetAsync()).Preview); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task ActualAuditPermissionFailureRetainsPreviewAndRetriesOnlyExactAudit()
    {
        using var f = new Fixture(); var before = await f.Configuration.GetAsync();
        var plan = await f.Owner.PrepareAsync(before, [new ShellAddLayerCommand("Audit recovery")]);
        var pending = await f.Owner.ApplyAsync(plan); var id = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        f.Fault.FailAudit = true;
        var applied = await f.Owner.ApplyAsync(plan, id);
        Assert.True(applied.Succeeded); Assert.Equal("PreviewPreparedAuditPending", applied.Code);
        Assert.NotNull(applied.Value!.AuditReceiptId);
        var shown = await f.Configuration.GetAsync(); var shell = await f.ShellRecordAsync();
        Assert.Equal(applied.Value.PreviewId, shown.Preview!.Id);
        Assert.True(await f.Owner.RetryAuditAsync(applied.Value.AuditReceiptId!));
        Assert.False(await f.Owner.RetryAuditAsync(applied.Value.AuditReceiptId!));
        Assert.Equal(shell, await f.ShellRecordAsync());
        Assert.Equal(shown.Preview.Id, (await f.Configuration.GetAsync()).Preview!.Id);
        Assert.Contains("SHELL_PREVIEW_PREPARED", await File.ReadAllTextAsync(f.StatePath));
    }
    [Fact]
    public async Task CopiedApprovalCannotAuthorizeIdenticalCandidateInANewerVolatileIntentGeneration()
    {
        using var f = new Fixture(); var before = await f.Configuration.GetAsync();
        var original = await f.Owner.PrepareAsync(before, [new ShellRenameSpaceCommand("Same proposed name")]);
        var pending = await f.Owner.ApplyAsync(original); var id = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        var manual = await f.Configuration.PreviewAsync(before.Stored, ShellEdits.RenameSpace(before.Effective, "Manual preview"), TimeSpan.FromSeconds(30));
        await f.Configuration.RevertAsync(manual.Preview!.Id);
        var current = await f.Configuration.GetAsync();
        var fresh = await f.Owner.PrepareAsync(current, [new ShellRenameSpaceCommand("Same proposed name")]);
        Assert.Equal(JsonSerializer.Serialize(original.Proposed), JsonSerializer.Serialize(fresh.Proposed));
        var rejected = await f.Owner.ApplyAsync(fresh, id);
        Assert.False(rejected.Succeeded); Assert.Equal("PermissionDenied", rejected.Code);
        Assert.Null((await f.Configuration.GetAsync()).Preview); Assert.Equal(before.Stored.Revision, (await f.Configuration.GetAsync()).Stored.Revision);
        var newReview = await f.Owner.ApplyAsync(fresh); Assert.NotEqual(id, newReview.Value!.PendingApprovalRequestId);
        Assert.True((await f.Permissions.DecideAsync(newReview.Value.PendingApprovalRequestId!, HomeApprovalChoice.Accept)).Succeeded);
        Assert.True((await f.Owner.ApplyAsync(fresh, newReview.Value.PendingApprovalRequestId)).Succeeded);
    }
    [Fact]
    public async Task ApprovalIsBoundToOriginalPreparedIntentEvenForIdenticalSameGenerationCandidate()
    {
        using var f = new Fixture(); var before = await f.Configuration.GetAsync();
        var original = await f.Owner.PrepareAsync(before, [new ShellRenameSpaceCommand("Identical")]);
        var fresh = await f.Owner.PrepareAsync(before, [new ShellRenameSpaceCommand("Identical")]);
        Assert.NotEqual(original.IntentId, fresh.IntentId);
        Assert.Equal(JsonSerializer.Serialize(original.Proposed), JsonSerializer.Serialize(fresh.Proposed));
        var pending = await f.Owner.ApplyAsync(original); var id = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        Assert.False((await f.Owner.ApplyAsync(fresh, id)).Succeeded);
        Assert.Null((await f.Configuration.GetAsync()).Preview); Assert.Equal(before.Stored.Revision, (await f.Configuration.GetAsync()).Stored.Revision);
    }
    [Fact]
    public async Task ReopenedCurrentProfileCanDiscoverAndFinishRetainedAuditWithoutAnotherPreview()
    {
        using var f = new Fixture(); var before = await f.Configuration.GetAsync();
        var plan = await f.Owner.PrepareAsync(before, [new ShellAddLayerCommand("Reopen recovery")]);
        var pending = await f.Owner.ApplyAsync(plan); var id = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        f.Fault.FailAudit = true; var applied = await f.Owner.ApplyAsync(plan, id);
        Assert.Equal(applied.Value!.AuditReceiptId, Assert.Single(await f.Owner.ReadPendingAuditIdsAsync(before.Stored)));
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "new-session" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Owner.ReadPendingAuditIdsAsync(before.Stored));
        var reopened = await f.Configuration.GetAsync(); Assert.Null(reopened.Preview);
        var retained = Assert.Single(await f.Owner.ReadPendingAuditIdsAsync(reopened.Stored));
        Assert.True(await f.Owner.RetryAuditAsync(retained));
        Assert.Empty(await f.Owner.ReadPendingAuditIdsAsync(reopened.Stored));
        var after = await f.Configuration.GetAsync(); Assert.Null(after.Preview); Assert.Equal(before.Stored.Revision, after.Stored.Revision);
    }
    private sealed class AuditFaultStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public bool FailAudit;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            if (FailAudit && record.RecordType == "home.permissions-trust" && record.Payload.GetRawText().Contains("SHELL_PREVIEW_PREPARED", StringComparison.Ordinal))
            { FailAudit = false; throw new UnauthorizedAccessException("Injected real audit-store permission denial after preview."); }
            return inner.WriteAsync(record, expected, ct);
        }
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
            => inner.WriteGuardedAsync(record, expected, actor, guard, ct);
    }
    private sealed class ForbiddenGenericApproval : IAppAiApprovalVerifier, IAppAiApprovalRequester
    {
        public int Calls;
        public ValueTask<bool> VerifyAsync(string app, string action, string token, CancellationToken ct) { Calls++; throw new InvalidOperationException("No generic grant."); }
        public ValueTask<AppAiApprovalDecision> RequestAsync(AppAiApprovalRequest request, CancellationToken ct) { Calls++; throw new InvalidOperationException("No duplicate review."); }
        public ValueTask CompleteAsync(AppAiActionRequest request, AppAiActionResult result, CancellationToken ct) { Calls++; throw new InvalidOperationException("No duplicate audit."); }
    }
    private sealed class Graph : IAppAiActionGraph
    { public ValueTask PublishAsync(AppAiActionGraphEvent value, CancellationToken ct) => ValueTask.CompletedTask; }
    private sealed class UnusedDulche : IDulcheAppClient
    { public IAsyncEnumerable<AppAiResponseChunk> StreamAsync(AppAiPrompt prompt, CancellationToken ct) => throw new InvalidOperationException("No model regeneration on exact retry."); }
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Utc = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Utc; }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-shell-semantic-" + Guid.NewGuid().ToString("N"));
        public string StatePath => Path.Combine(_root, "home.json");
        public FileHomeCoreStateStore Home { get; }
        public AuditFaultStore Fault { get; }
        public async Task<string> ShellRecordAsync() => JsonSerializer.Serialize((await Home.ReadAsync()).State!.Records.Single(record => record.RecordType == HomeShellConfigurationStore.RecordType));
        public Actors Actors { get; } = new(); public Clock Clock { get; } = new();
        public ShellConfigurationService Configuration { get; } public ShellSemanticFeatureProvider Owner { get; }
        public HomePermissionTrustService Permissions { get; }
        public Fixture()
        {
            Home = new FileHomeCoreStateStore(StatePath);
            var home = Fault = new AuditFaultStore(Home);
            var resources = new ResourceAuthorizationService(Actors, [new ShellConfigurationResourceResolver(home)]);
            Configuration = new(new HomeShellConfigurationStore(home, Actors, resources), Clock);
            Permissions = new(home, new ShellSemanticActionPolicies().TryGet);
            Owner = new(Configuration, new HomeResourceOperationBroker(resources, Permissions));
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
