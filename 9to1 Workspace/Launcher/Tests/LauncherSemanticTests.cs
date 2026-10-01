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
    [Fact]
    public async Task CompositeGroupingKeepsPlacementIdentitiesOnFullPageAndCommitsThroughHome()
    {
        using var f = new Fixture(includeApps: true); var initial = await f.Store.GetAsync();
        // Existing references can remain after uninstall; grouping must preserve their canonical IDs.
        var layout = LauncherLayout.Empty(3, 3);
        var appIds = Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in appIds) layout = LauncherLayoutEdits.AddApplication(layout, layout.ActivePageId, id);
        var members = layout.ActivePage.Items.Take(3).Select(item => item.Id).ToArray();
        var grouped = LauncherLayoutEdits.GroupPlacements(layout, layout.ActivePageId, "Study", members);
        Assert.Equal(9, layout.ActivePage.Items.Count);
        var folder = Assert.Single(grouped.Folders);
        Assert.Equal(members, folder.Items.Select(item => item.Id));
        Assert.Equal(appIds.Take(3), folder.Items.Select(item => item.ApplicationId));
        Assert.Equal(7, grouped.ActivePage.Items.Count);
        Assert.Throws<ArgumentException>(() => LauncherLayoutEdits.GroupPlacements(layout, layout.ActivePageId, "Duplicate", [members[0], members[0]]));
        Assert.Throws<InvalidOperationException>(() => LauncherLayoutEdits.GroupPlacements(grouped, grouped.ActivePageId, "Nested", [grouped.ActivePage.Items.Single(item => item.FolderId is not null).Id]));
        // New identities are generated while preparing, not while claiming/retrying approval.
        var ownedIds = initial.Current.ActivePage.Items.Select(item => item.ApplicationId).ToArray();
        var plan = await f.Owner.PrepareAsync(initial.AuthorityId, initial.Revision, [new LauncherCreatePageWithApplicationsCommand("Composite base", ownedIds)]);
        var exactPage = plan.Proposed.ActivePage.Id;
        var exactPlacements = plan.Proposed.ActivePage.Items.Select(item => item.Id).ToArray();
        var pending = await f.Owner.ApplyAsync(plan);
        Assert.True((await f.Permissions.DecideAsync(pending.Value!.PendingApprovalRequestId!, HomeApprovalChoice.Accept)).Succeeded);
        Assert.True((await f.Owner.ApplyAsync(plan, pending.Value.PendingApprovalRequestId)).Succeeded);
        var saved = await f.Store.GetAsync();
        Assert.Equal(initial.Revision + 1, saved.Revision);
        Assert.Equal(exactPage, saved.Current.ActivePage.Id);
        Assert.Equal(exactPlacements, saved.Current.ActivePage.Items.Select(item => item.Id));
        var grouping = await f.Owner.PrepareAsync(saved.AuthorityId, saved.Revision,
            [new LauncherGroupPlacementsCommand(exactPage, "Reviewed folder", exactPlacements)]);
        var exactFolder = Assert.Single(grouping.Proposed.Folders).Id;
        var groupPending = await f.Owner.ApplyAsync(grouping);
        Assert.Empty((await f.Store.GetAsync()).Current.Folders);
        Assert.True((await f.Permissions.DecideAsync(groupPending.Value!.PendingApprovalRequestId!, HomeApprovalChoice.Accept)).Succeeded);
        Assert.True((await f.Owner.ApplyAsync(grouping, groupPending.Value.PendingApprovalRequestId)).Succeeded);
        var groupedSaved = await f.Store.GetAsync();
        Assert.Equal(saved.Revision + 1, groupedSaved.Revision);
        Assert.Equal(exactFolder, Assert.Single(groupedSaved.Current.Folders).Id);
        Assert.Equal(exactPlacements, groupedSaved.Current.Folders[0].Items.Select(item => item.Id));
    }

    [Fact]
    public void CompositePageCreatesAllPlacementsOrLeavesOriginalUnchanged()
    {
        var layout = LauncherLayout.Empty(3, 3);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var page = LauncherLayoutEdits.AddPageWithApplications(layout, "College", ids);
        Assert.Single(layout.Pages); Assert.Empty(layout.ActivePage.Items);
        Assert.Equal(ids, page.ActivePage.Items.Select(item => item.ApplicationId));
        Assert.Throws<InvalidOperationException>(() => LauncherLayoutEdits.AddPageWithApplications(layout, "Too many", Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray()));
        Assert.Single(layout.Pages); Assert.Empty(layout.ActivePage.Items);
    }

    [Fact]
    public async Task CompositeInputIsDetachedBeforeFirstAuthorityAwait()
    {
        using var f = new Fixture(includeApps: true); var initial = await f.Store.GetAsync();
        var ids = initial.Current.ActivePage.Items.Select(item => item.ApplicationId).ToArray();
        var expected = ids.ToArray();
        f.Actors.OnNextRead = () => ids[0] = Guid.NewGuid();
        var plan = await f.Owner.PrepareAsync(initial.AuthorityId, initial.Revision,
            [new LauncherCreatePageWithApplicationsCommand("Frozen input", ids)]);
        Assert.Equal(expected, plan.Proposed.ActivePage.Items.Select(item => item.ApplicationId));
        Assert.Equal(initial.Revision, (await f.Store.GetAsync()).Revision);
    }

    [Fact]
    public async Task ActualCoordinatorReviewsGlobalAppearanceFolderGeometryAndEmptyPageRemovalTogether()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var basePage = initial.Current.ActivePageId;
        var prepared = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.AddPage(
            LauncherLayoutEdits.CreateFolder(layout, basePage, "Original folder"), "Spare"));
        var folder = Assert.Single(prepared.Current.Folders);
        using var actions = new LauncherAppAiActions(f.Session, f.Owner);
        var context = new LauncherAppAiContext(f.Session); var captured = await context.CaptureAsync(default);
        var generic = new ForbiddenGenericApproval();
        using var bar = new FloatingAiBarState(new AppAiCoordinator(context, actions, generic, new UnusedDulche(), generic, actionGraph: new Graph()));
        bar.SetWriteMode();
        var request = new AppAiActionRequest(LauncherSemanticFeatureProvider.AppId, LauncherSemanticFeatureProvider.EditAction,
            JsonSerializer.SerializeToElement(new { Commands = new LauncherSemanticCommand[] {
                new LauncherConfigureFolderCommand(folder.Id, "Tools", 6),
                new LauncherPresentationCommand(72, 18, 12, 10, true, false),
                new LauncherRemovePageCommand(prepared.Current.ActivePageId) } }),
            null, "appearance", AppAiAccessMode.Write, captured.Revision);
        var pending = await bar.ExecuteActionAsync(request);
        Assert.Equal("approval-pending", pending.ErrorCode);
        Assert.Null((await f.Store.GetAsync()).Current.Presentation);
        Assert.Equal(2, (await f.Store.GetAsync()).Current.Pages.Count);
        var id = actions.PendingReviewRequestId!;
        Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        Assert.True((await bar.ExecuteActionAsync(actions.PendingActionRequest!)).Succeeded);
        var saved = await f.Store.GetAsync();
        Assert.Equal(prepared.Revision + 1, saved.Revision);
        Assert.Single(saved.Current.Pages);
        Assert.Equal(new LauncherPresentation(72, 18, 12, 10, true, false), saved.Current.Presentation);
        Assert.Equal(folder.Id, Assert.Single(saved.Current.Folders).Id);
        Assert.Equal("Tools", saved.Current.Folders[0].Name); Assert.Equal(6, saved.Current.Folders[0].Columns);
        Assert.Equal(0, generic.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owner.PrepareAsync(saved.AuthorityId, saved.Revision,
            [new LauncherRemovePageCommand(saved.Current.ActivePageId)]));
        Assert.Equal(saved.Revision, (await f.Store.GetAsync()).Revision);
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
        public Action? OnNextRead;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { var callback = OnNextRead; OnNextRead = null; callback?.Invoke(); return ValueTask.FromResult<AuthenticatedResourceActor?>(Current); }
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class UnavailablePeer : IHomeNativeInstalledPeerVerifier
    {
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken ct) => ValueTask.FromResult<HomeNativeInstalledPeer?>(null);
    }
    private sealed class Provider : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "android-test";
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
            => ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([new("personal", "Personal", false, true,
                [new("first", "first/main", "First app", "1", true), new("second", "second/main", "Second app", "1", true)])]);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-launcher-semantic-" + Guid.NewGuid().ToString("N"));
        public Actors Actors { get; } = new(); public HomeLauncherLayoutStore Store { get; }
        public HomeLauncherSession Session { get; } public LauncherSemanticFeatureProvider Owner { get; }
        public HomePermissionTrustService Permissions { get; }
        public Fixture(bool includeApps = false)
        {
            var home = new FileHomeCoreStateStore(Path.Combine(_root, "home.json"));
            var resources = new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(home)]);
            Store = new(home, Actors, resources, new HomeInstalledApplicationRegistry(home, Actors, includeApps ? [new Provider()] : []));
            Session = new(Store, Actors, new HomeNativeWidgetRegistry(new UnavailablePeer(), Actors, resources));
            Permissions = new(home, new LauncherSemanticActionPolicies().TryGet);
            Owner = new(Session, new HomeResourceOperationBroker(resources, Permissions));
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
