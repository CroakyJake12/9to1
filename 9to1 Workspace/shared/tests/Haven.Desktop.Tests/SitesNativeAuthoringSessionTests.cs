using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.NativeUI;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Actual persistent Home/Files/Sites owners and their real review/commit flow.
/// These controls do not certify native rendering, installed bootstrap or cloud deployment.</summary>
public sealed class SitesNativeAuthoringSessionTests
{
    [Fact]
    public async Task Original_Home_review_creates_edits_previews_and_reopens_the_same_persisted_site()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var original = await h.Session.ReadAsync(h.Actor, token);
        Assert.Empty(original.Projects);
        var create = await h.Session.PrepareAsync(original, null, null, null, h.Actor, "create",
            "Friday site", "friday-site", "", () => true, "sites-workflow", token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, create.Approval.State);
        var waiting = await h.Session.ApplyAsync(create, token);
        Assert.True(waiting.AwaitingHomeReview); Assert.Null(waiting.Result);
        Assert.Empty((await h.Session.ReadAsync(h.Actor, token)).Projects);
        await h.ApproveAsync(create, token);
        var actual = h.Session.ApplyAsync(create, token);
        Assert.Same(actual, h.Session.ApplyAsync(create, token));
        var created = await actual;
        Assert.True(created.AuditRecorded); Assert.True(created.Result!.IsSuccess);
        var first = created.Result.Value!;
        Assert.Equal("9to1-native", first.Source.FrameworkId);
        Assert.Equal("friday-site", first.Source.RelativeProjectPath);
        Assert.Equal(1, first.Revision);
        Assert.Same(actual, h.Session.ApplyAsync(create, token));

        var observed = await h.Session.ReadAsync(h.Actor, token);
        var project = Assert.Single(observed.Projects);
        var pageEdit = await h.Session.PrepareAsync(observed, project, null, null, h.Actor, "page",
            "Home", "/", "", () => true, "sites-workflow", token);
        await h.ApproveAsync(pageEdit, token);
        Assert.True((await h.Session.ApplyAsync(pageEdit, token)).Result!.IsSuccess);
        observed = await h.Session.ReadAsync(h.Actor, token); project = Assert.Single(observed.Projects);
        var page = Assert.Single(project.Pages);
        Assert.Equal(2, project.Revision); Assert.Equal("/", Assert.Single(project.Routes).Pattern);

        var headingEdit = await h.Session.PrepareAsync(observed, project, page, null, h.Actor, "heading",
            "", "", "Initial heading", () => true, "sites-workflow", token);
        await h.ApproveAsync(headingEdit, token);
        Assert.True((await h.Session.ApplyAsync(headingEdit, token)).Result!.IsSuccess);
        observed = await h.Session.ReadAsync(h.Actor, token); project = Assert.Single(observed.Projects);
        page = Assert.Single(project.Pages); var component = Assert.Single(project.Components);
        var componentId = component.ComponentId; var componentRevision = component.Revision;
        Assert.Equal(3, project.Revision); Assert.Equal("heading", component.ComponentType);
        Assert.Equal(2, component.Properties["level"].GetInt32());

        var textEdit = await h.Session.PrepareAsync(observed, project, page, component, h.Actor, "save-text",
            "", "", "Saved <Friday> & footage", () => true, "sites-workflow", token);
        await h.ApproveAsync(textEdit, token);
        Assert.True((await h.Session.ApplyAsync(textEdit, token)).Result!.IsSuccess);
        observed = await h.Session.ReadAsync(h.Actor, token); project = Assert.Single(observed.Projects);
        page = Assert.Single(project.Pages); component = Assert.Single(project.Components);
        Assert.Equal(4, project.Revision); Assert.Equal(componentId, component.ComponentId);
        Assert.Equal(componentRevision + 1, component.Revision);
        Assert.Equal(2, component.Properties["level"].GetInt32());
        var preview = await h.Session.PreviewAsync(observed, project, page, h.Actor, token);
        Assert.DoesNotContain(preview.Diagnostics, item => item.IsError);
        Assert.Contains("<h2", preview.Document);
        Assert.Contains("Saved &lt;Friday&gt; &amp; footage", preview.Document);
        Assert.DoesNotContain("Saved <Friday>", preview.Document);

        // A new native session rereads the maintained store; no view cache supplies this result.
        var reopened = h.NewSession();
        var sameSaved = Assert.Single((await reopened.ReadAsync(h.Actor, token)).Projects);
        Assert.Equal(first.SiteId, sameSaved.SiteId); Assert.Equal(first.ProjectId, sameSaved.ProjectId);
        Assert.Equal(page.PageId, Assert.Single(sameSaved.Pages).PageId);
        Assert.Equal(componentId, Assert.Single(sameSaved.Components).ComponentId);
        Assert.Equal("Saved <Friday> & footage", sameSaved.Components[0].Properties["text"].GetString());
        Assert.Equal(JsonSerializer.Serialize(project), JsonSerializer.Serialize(sameSaved));
        foreach (var edit in new[] { create, pageEdit, headingEdit, textEdit })
            Assert.Equal(HomePermissionRequestState.Succeeded,
                (await h.Permissions.GetAuthorizationAsync(edit.Approval.RequestId, token)).State);
    }

    [Fact]
    public async Task Copied_observations_edits_and_retired_original_views_cannot_write_a_project()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var page = await h.Session.ReadAsync(h.Actor, token);
        var copiedPageFailure = await Assert.ThrowsAnyAsync<Exception>(() => h.Session.PrepareAsync(page with { },
            null, null, null, h.Actor, "create", "Copied", "copied", "", () => true, "sites-refusal", token));
        Assert.Contains(Causes(copiedPageFailure), cause => cause is UnauthorizedAccessException);
        var live = true;
        var edit = await h.Session.PrepareAsync(page, null, null, null, h.Actor, "create",
            "Retired original", "retired", "", () => live, "sites-refusal", token);
        var copy = (SiteNativeAuthoringSession.PreparedEdit)typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(edit, null)!;
        Assert.Throws<UnauthorizedAccessException>(() => { _ = h.Session.ApplyAsync(copy, token); });
        await h.ApproveAsync(edit, token); live = false;
        var retiredFailure = await Assert.ThrowsAnyAsync<Exception>(() => h.Session.ApplyAsync(edit, token));
        Assert.Contains(Causes(retiredFailure), cause => cause is ObjectDisposedException);
        Assert.Empty((await h.Session.ReadAsync(h.Actor, token)).Projects);
        var cancellation = await h.Session.PrepareAsync(page, null, null, null, h.Actor, "create",
            "Cancelled", "cancelled", "", () => true, "sites-refusal", token);
        await h.ApproveAsync(cancellation, token); h.Session.RetireReview(cancellation);
        Assert.Throws<InvalidOperationException>(() => { _ = h.Session.ApplyAsync(cancellation, token); });
        Assert.Empty((await h.Session.ReadAsync(h.Actor, token)).Projects);
    }

    [Fact]
    public async Task A_changed_saved_revision_refuses_the_stale_Home_review_without_another_effect()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var empty = await h.Session.ReadAsync(h.Actor, token);
        var create = await h.Session.PrepareAsync(empty, null, null, null, h.Actor, "create",
            "Revision control", "revision-control", "", () => true, "sites-revisions", token);
        await h.ApproveAsync(create, token); Assert.True((await h.Session.ApplyAsync(create, token)).Result!.IsSuccess);
        var page = await h.Session.ReadAsync(h.Actor, token); var project = Assert.Single(page.Projects);
        var stale = await h.Session.PrepareAsync(page, project, null, null, h.Actor, "page",
            "Stale page", "/stale", "", () => true, "sites-revisions", token);
        var concurrent = await h.Session.PrepareAsync(page, project, null, null, h.Actor, "page",
            "Actual page", "/actual", "", () => true, "sites-revisions", token);
        await h.ApproveAsync(stale, token); await h.ApproveAsync(concurrent, token);
        var accepted = h.Session.ApplyAsync(concurrent, token);
        Assert.True((await accepted).Result!.IsSuccess);
        var rejected = h.Session.ApplyAsync(stale, token);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => rejected);
        Assert.Contains(Causes(failure), cause => cause is InvalidOperationException);
        Assert.Same(rejected, h.Session.ApplyAsync(stale, token));
        Assert.Same(accepted, h.Session.ApplyAsync(concurrent, token));
        var final = Assert.Single((await h.Session.ReadAsync(h.Actor, token)).Projects);
        Assert.Equal(project.Revision + 1, final.Revision);
        Assert.Equal("Actual page", Assert.Single(final.Pages).Name);
        Assert.Equal("/actual", Assert.Single(final.Routes).Pattern);
    }

    [AvaloniaFact]
    public async Task Native_source_cannot_join_itself_and_external_close_waits_for_the_actual_held_source()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var empty = await h.Session.ReadAsync(h.Actor, token);
        var create = await h.Session.PrepareAsync(empty, null, null, null, h.Actor, "create",
            "Native lifecycle", "native-lifecycle", "", () => true, "sites-native-lifetime", token);
        await h.ApproveAsync(create, token);
        Assert.True((await h.Session.ApplyAsync(create, token)).Result!.IsSuccess);
        var saved = JsonSerializer.Serialize(Assert.Single((await h.NewSession().ReadAsync(h.Actor, token)).Projects));
        using var host = new CancellationTokenSource();
        var readiness = new NativeUnitReadiness(h);
        var surface = new SitesNativeAuthoringSurface(h.Session, h.Actor, readiness, host.Token);
        var outside = ExecutionContext.Capture()!;
        var originalCallbacks = 0;
        surface.PropertyChanged += (_, _) => ExecutionContext.Run(outside.CreateCopy(), _ =>
        {
            originalCallbacks++;
            Assert.Throws<InvalidOperationException>((Action)(() => { _ = surface.CloseAndDrainAsync(); }));
        }, null);
        var window = new Window { Content = surface, Width = 800, Height = 600 };
        Task? refresh = null; Task? close = null;
        window.Show();
        try
        {
            await surface.InitializeAsync(token);
            Assert.True(originalCallbacks > 0);
            readiness.HoldNext = true;
            refresh = surface.DispatchAsync("9to1.Sites.Refresh", null, token).AsTask();
            await readiness.Entered.Task.WaitAsync(token);
            var actualSource = readiness.ActualHeldSource!;
            Assert.False(actualSource.IsCompleted);
            close = surface.CloseAndDrainAsync();
            Assert.Same(close, surface.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.False(refresh.IsCompleted);
            readiness.Release.TrySetResult();
            var sourceFailure = await Assert.ThrowsAnyAsync<Exception>(() => refresh);
            var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(actualSource.IsCompleted);
            Assert.Contains(Causes(sourceFailure), cause => cause is OperationCanceledException);
            Assert.Contains(Causes(closeFailure), cause => cause is OperationCanceledException);
            Assert.Same(close, surface.CloseAndDrainAsync());
            Assert.Equal(saved, JsonSerializer.Serialize(Assert.Single((await h.NewSession().ReadAsync(h.Actor, token)).Projects)));
        }
        finally
        {
            readiness.Release.TrySetResult();
            if (refresh is not null) try { await refresh; } catch { }
            close ??= surface.CloseAndDrainAsync();
            try { await close; } catch { }
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Native_initialize_and_cleanup_faults_keep_the_original_causes_and_one_terminal_close()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        using var host = new CancellationTokenSource();
        var surface = new SitesNativeAuthoringSurface(h.Session, h.Actor, new NativeUnitReadiness(h), host.Token);
        var sourceFailure = new InvalidOperationException("Original Sites initialization callback failed.");
        var cleanupFailure = new IOException("Original Sites native cleanup callback failed.");
        var cleanup = false;
        surface.PropertyChanged += (_, _) => { if (cleanup) throw cleanupFailure; throw sourceFailure; };
        var window = new Window { Content = surface, Width = 800, Height = 600 };
        Task? close = null;
        window.Show();
        try
        {
            var initialize = surface.InitializeAsync(token);
            var failedInitialize = await Assert.ThrowsAnyAsync<Exception>(() => initialize);
            Assert.Contains(Causes(failedInitialize), cause => ReferenceEquals(cause, sourceFailure));
            cleanup = true;
            close = surface.CloseAndDrainAsync();
            Assert.Same(close, surface.CloseAndDrainAsync());
            var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.Contains(Causes(terminal), cause => ReferenceEquals(cause, sourceFailure));
            Assert.Contains(Causes(terminal), cause => ReferenceEquals(cause, cleanupFailure));
            Assert.Same(close, surface.CloseAndDrainAsync());
            Assert.Throws<ObjectDisposedException>((Action)(() => { _ = surface.InitializeAsync(token); }));
            Assert.Empty((await h.NewSession().ReadAsync(h.Actor, token)).Projects);
        }
        finally
        {
            cleanup = true;
            close ??= surface.CloseAndDrainAsync();
            try { await close; } catch { }
            window.Close();
        }
    }

    // This unit-only scene observation uses the real persistent session/actor.
    // It is not a Windows Home bootstrap observation or an authorization grant.
    private sealed class NativeUnitReadiness(Harness original) : ICuiSceneReadiness
    {
        internal bool HoldNext { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<CuiSceneAvailability>? ActualHeldSource { get; private set; }
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            var actual = ObserveAsync(token);
            if (HoldNext) ActualHeldSource = actual;
            return new(actual);
        }
        private async Task<CuiSceneAvailability> ObserveAsync(CancellationToken token)
        {
            var page = await original.Session.ReadAsync(original.Actor, token);
            await original.Session.RevalidateAsync(page, original.Actor, token);
            if (HoldNext)
            {
                Entered.TrySetResult();
                await Release.Task; // The real acquired task settles before close can finish.
            }
            token.ThrowIfCancellationRequested();
            return new(CuiSceneAvailabilityState.Ready, "sites-unit-source", "Persistent Sites fixture observed.");
        }
    }

    private static IEnumerable<Exception> Causes(Exception error) =>
        error is AggregateException group ? group.Flatten().InnerExceptions : [error];

    private sealed class Harness : IAsyncDisposable
    {
        public required string Root { get; init; }
        public required ServiceProvider Graph { get; init; }
        public required HomePermissionTrustService Permissions { get; init; }
        public required AuthenticatedResourceActor Actor { get; init; }
        public required SiteNativeAuthoringSession Session { get; init; }
        public SiteNativeAuthoringSession NewSession() => new(Graph.GetRequiredService<ISiteNativeWorkspaceAuthority>(),
            Graph.GetRequiredService<IAuthenticatedResourceActorSource>(), Graph.GetRequiredService<ResourceAuthorizationService>(),
            Graph.GetRequiredService<HomeResourceOperationBroker>());
        public async Task ApproveAsync(SiteNativeAuthoringSession.PreparedEdit sameOriginal, CancellationToken token) =>
            Assert.True((await Permissions.DecideAsync(sameOriginal.Approval.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: token)).Succeeded);
        public static async Task<Harness> CreateAsync(CancellationToken token)
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-sites-native-authoring-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var policy = new SiteNativeActionPolicies();
                var permissions = new HomePermissionTrustService(home, policy.TryGet);
                var services = new ServiceCollection();
                services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles);
                services.AddSingleton<IAuthenticatedResourceActorSource>(profiles); services.AddSingleton(permissions);
                services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
                services.AddSingleton<HomeLocalStoreOwnership>();
                services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
                services.AddFilesNativeHost();
                services.AddSingleton<ISiteNativeWorkspaceAuthority>(provider => new SitesNativeWorkspaceAuthority(
                    provider.GetRequiredService<NativeFilesWorkspaceAuthority>()));
                services.AddSingleton<ICanonicalResourceAccessResolver>(provider => new SiteNativeProjectAccessResolver(
                    provider.GetRequiredService<ISiteNativeWorkspaceAuthority>()));
                services.AddSingleton<ResourceAuthorizationService>(); services.AddSingleton<HomeResourceOperationBroker>();
                var graph = services.BuildServiceProvider();
                try
                {
                    var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
                    await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                        graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
                    var workspace = await graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token)
                        ?? throw new InvalidOperationException("The actual Home/Files setup has no original workspace.");
                    var session = new SiteNativeAuthoringSession(graph.GetRequiredService<ISiteNativeWorkspaceAuthority>(),
                        profiles, graph.GetRequiredService<ResourceAuthorizationService>(), graph.GetRequiredService<HomeResourceOperationBroker>());
                    return new() { Root = root, Graph = graph, Permissions = permissions, Actor = workspace.Actor, Session = session };
                }
                catch { await graph.DisposeAsync(); throw; }
            }
            catch { Directory.Delete(root, true); throw; }
        }
        public async ValueTask DisposeAsync() { await Graph.DisposeAsync(); Directory.Delete(Root, true); }
    }
}
