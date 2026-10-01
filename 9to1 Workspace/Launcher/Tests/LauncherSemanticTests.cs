using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Cui.AI;
using Xunit;

namespace NineToOne.Launcher.Tests;

public sealed class LauncherSemanticTests
{
    [Fact]
    public async Task ActualHomeApprovalPersistsExactPreparedIdsOnceAndAuditsOwnerCommit()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var plan = await f.Owner.PrepareAsync(initial.AuthorityId, initial.Revision, [new LauncherCreatePageCommand("College")]);
        var proposedId = plan.Proposed.ActivePageId;
        Assert.Equal(initial.Revision, (await f.Store.GetAsync()).Revision);
        var pending = await f.Owner.ApplyAsync(plan); Assert.False(pending.Succeeded);
        var id = pending.Value!.PendingApprovalRequestId!;
        Assert.False((await f.Owner.ApplyAsync(plan, id)).Succeeded);
        Assert.Equal(initial.Revision, (await f.Store.GetAsync()).Revision);
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        var saved = await f.Owner.ApplyAsync(plan, id); Assert.True(saved.Succeeded);
        Assert.Equal(proposedId, saved.Value!.Saved!.Current.ActivePageId);
        Assert.Equal("College", (await f.Store.GetAsync()).Current.ActivePage.Name);
        Assert.Equal(initial.Revision + 1, (await f.Store.GetAsync()).Revision);
        Assert.Equal(HomePermissionRequestState.Succeeded, (await f.Permissions.GetAuthorizationAsync(id)).State);
        Assert.False((await f.Owner.ApplyAsync(plan, id)).Succeeded);
        Assert.Equal(initial.Revision + 1, (await f.Store.GetAsync()).Revision);
    }

    [Fact]
    public async Task ChangedArgumentsAndSessionCannotReuseApprovedPlan()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var plan = await f.Owner.PrepareAsync(initial.AuthorityId, initial.Revision, [new LauncherCreatePageCommand("Approved name")]);
        var pending = await f.Owner.ApplyAsync(plan); var id = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        var changed = plan with { Proposed = LauncherLayoutEdits.RenamePage(plan.Proposed, plan.Proposed.ActivePageId, "Different name") };
        Assert.False((await f.Owner.ApplyAsync(changed, id)).Succeeded);
        Assert.Equal(initial.Revision, (await f.Store.GetAsync()).Revision);
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "new-session" };
        Assert.False((await f.Owner.ApplyAsync(plan, id)).Succeeded);
        Assert.Equal(initial.Revision, (await f.Store.GetAsync()).Revision);
    }

    [Fact]
    public async Task AppIntentCacheIgnoresCopiedGenericTokenAndResumesExactPreparedPlan()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        using var actions = new LauncherAppAiActions(f.Session, f.Owner);
        var context = await new LauncherAppAiContext(f.Session).CaptureAsync(default);
        var request = new AppAiActionRequest(LauncherSemanticFeatureProvider.AppId, LauncherSemanticFeatureProvider.EditAction,
            JsonSerializer.SerializeToElement(new { Commands = new LauncherSemanticCommand[] { new LauncherCreatePageCommand("From Dulche") } }),
            "copied-generic-token-is-not-authority", "correlation", AppAiAccessMode.Write, context.Revision);
        var pending = await actions.ExecuteAsync(request, default); Assert.False(pending.Succeeded);
        var id = actions.PendingReviewRequestId!; Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal(initial.Revision, (await f.Store.GetAsync()).Revision);
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        Assert.True((await actions.ExecuteAsync(request, default)).Succeeded);
        Assert.Equal("From Dulche", (await f.Store.GetAsync()).Current.ActivePage.Name);
        Assert.Equal(initial.Revision + 1, (await f.Store.GetAsync()).Revision);
        Assert.False((await actions.ExecuteAsync(request, default)).Succeeded);
        Assert.Equal(initial.Revision + 1, (await f.Store.GetAsync()).Revision);
    }

    [Fact]
    public async Task ClosingPendingIntentDoesNotExecuteEvenIfHomeLaterApproves()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var actions = new LauncherAppAiActions(f.Session, f.Owner);
        var context = await new LauncherAppAiContext(f.Session).CaptureAsync(default);
        var request = new AppAiActionRequest(LauncherSemanticFeatureProvider.AppId, LauncherSemanticFeatureProvider.EditAction,
            JsonSerializer.SerializeToElement(new { Commands = new LauncherSemanticCommand[] { new LauncherCreatePageCommand("Closed") } }),
            null, "closing", AppAiAccessMode.Write, context.Revision);
        Assert.False((await actions.ExecuteAsync(request, default)).Succeeded);
        var id = actions.PendingReviewRequestId!; actions.Dispose();
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        Assert.False((await actions.ExecuteAsync(request, default)).Succeeded);
        Assert.Equal(initial.Revision, (await f.Store.GetAsync()).Revision);
    }

    [Fact]
    public async Task ActualCoordinatorAndBarUseOnlyOwnerHomeReviewThenRetryWithoutModelRegeneration()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        using var actions = new LauncherAppAiActions(f.Session, f.Owner);
        var context = new LauncherAppAiContext(f.Session); var captured = await context.CaptureAsync(default);
        Assert.True(ActionJsonSchemaValidator.IsSupported(Assert.Single(actions.Actions).InputSchemaJson));
        var generic = new ForbiddenGenericApproval(); var graph = new Graph();
        using var bar = new FloatingAiBarState(new AppAiCoordinator(context, actions, generic, new UnusedDulche(), generic, actionGraph: graph));
        var request = new AppAiActionRequest(LauncherSemanticFeatureProvider.AppId, LauncherSemanticFeatureProvider.EditAction,
            JsonSerializer.SerializeToElement(new { Commands = new LauncherSemanticCommand[] { new LauncherCreatePageCommand("Reviewed through Home") } }),
            "forged-token", "native-host", AppAiAccessMode.Write, captured.Revision);
        Assert.False((await bar.ExecuteActionAsync(request)).Succeeded); Assert.Null(actions.PendingReviewRequestId);
        bar.SetWriteMode(); var pending = await bar.ExecuteActionAsync(request); Assert.Equal("approval-pending", pending.ErrorCode);
        Assert.Equal(AppAiRequestState.WaitingForApproval, bar.RequestState);
        Assert.Contains(graph.Events, item => item.Status == AppAiActionGraphStatus.WaitingForApproval);
        Assert.Equal(initial.Revision, (await f.Store.GetAsync()).Revision);
        var pendingId = actions.PendingReviewRequestId!; var exact = actions.PendingActionRequest!;
        Assert.Null(exact.ApprovalToken);
        Assert.True((await f.Permissions.DecideAsync(pendingId, HomeApprovalChoice.Accept)).Succeeded);
        bar.SetReadOnly(); Assert.False((await bar.ExecuteActionAsync(exact)).Succeeded);
        Assert.Equal(initial.Revision, (await f.Store.GetAsync()).Revision);
        bar.SetWriteMode(); Assert.True((await bar.ExecuteActionAsync(exact)).Succeeded);
        Assert.Equal(initial.Revision + 1, (await f.Store.GetAsync()).Revision);
        Assert.Equal(HomePermissionRequestState.Succeeded, (await f.Permissions.GetAuthorizationAsync(pendingId)).State);
        Assert.Equal(AppAiActionGraphStatus.Completed, graph.Events.Last().Status);
        Assert.Equal(0, generic.Calls);
    }
    private sealed class ForbiddenGenericApproval : IAppAiApprovalVerifier, IAppAiApprovalRequester
    {
        public int Calls;
        public ValueTask<bool> VerifyAsync(string app, string action, string token, CancellationToken ct) { Calls++; throw new InvalidOperationException("A generic token cannot authorize an owner write."); }
        public ValueTask<AppAiApprovalDecision> RequestAsync(AppAiApprovalRequest request, CancellationToken ct) { Calls++; throw new InvalidOperationException("Duplicate review."); }
        public ValueTask CompleteAsync(AppAiActionRequest request, AppAiActionResult result, CancellationToken ct) { Calls++; throw new InvalidOperationException("Duplicate audit."); }
    }
    private sealed class Graph : IAppAiActionGraph
    {
        public List<AppAiActionGraphEvent> Events { get; } = [];
        public ValueTask PublishAsync(AppAiActionGraphEvent value, CancellationToken ct) { Events.Add(value); return ValueTask.CompletedTask; }
    }
    private sealed class UnusedDulche : IDulcheAppClient
    {
        public IAsyncEnumerable<AppAiResponseChunk> StreamAsync(AppAiPrompt prompt, CancellationToken ct) => throw new InvalidOperationException("An exact action retry must not ask a model to regenerate IDs.");
    }

    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class UnavailablePeer : IHomeNativeInstalledPeerVerifier
    {
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken ct) => ValueTask.FromResult<HomeNativeInstalledPeer?>(null);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-launcher-semantic-" + Guid.NewGuid().ToString("N"));
        public Actors Actors { get; } = new(); public HomeLauncherLayoutStore Store { get; }
        public HomeLauncherSession Session { get; } public LauncherSemanticFeatureProvider Owner { get; }
        public HomePermissionTrustService Permissions { get; }
        public Fixture()
        {
            var home = new FileHomeCoreStateStore(Path.Combine(_root, "home.json"));
            var resources = new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(home)]);
            Store = new(home, Actors, resources, new HomeInstalledApplicationRegistry(home, Actors, []));
            Session = new(Store, Actors, new HomeNativeWidgetRegistry(new UnavailablePeer(), Actors, resources));
            Permissions = new(home, new LauncherSemanticActionPolicies().TryGet);
            Owner = new(Session, new HomeResourceOperationBroker(resources, Permissions));
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
