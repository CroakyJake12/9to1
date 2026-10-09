using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Actual persistent Files/Home/broker/fence and issued browser selections.
/// These controls do not certify installed bootstrap, production rendering or cloud connectivity.</summary>
public sealed class FilesNativeBrowserMutationTests
{
    [Fact]
    public async Task Create_and_rename_require_original_Home_review_and_commit_each_operation_once()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var prepared = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Friday work", () => true, "files-control", token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, prepared.Approval.State);
        var before = await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token);
        var waiting = await h.Browser.ApplyNativeMutationAsync(prepared, token);
        Assert.True(waiting.AwaitingHomeReview);
        Assert.Null(waiting.Operation);
        Assert.Equal(before, await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token));
        Assert.True((await h.Permissions.DecideAsync(prepared.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var actual = h.Browser.ApplyNativeMutationAsync(prepared, token);
        Assert.Same(actual, h.Browser.ApplyNativeMutationAsync(prepared, token));
        var result = await actual;
        Assert.True(result.Operation!.IsSuccess);
        Assert.True(result.AuditRecorded);
        var created = result.Operation.Value!.ItemId;
        Assert.Equal(FilesOperationState.Committed, result.Operation.Value.State);
        Assert.Equal(HomePermissionRequestState.Succeeded,
            (await h.Permissions.GetAuthorizationAsync(prepared.Approval.RequestId, token)).State);
        Assert.Same(actual, h.Browser.ApplyNativeMutationAsync(prepared, token));
        var refreshed = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var row = Assert.Single(refreshed.Items);
        Assert.Equal(created, row.Id);
        Assert.Equal("Friday work", row.Name);
        var rename = await h.Browser.PrepareNativeMutationAsync(refreshed, row, h.Workspace.Actor,
            "Rename", "Footage", () => true, "files-control", token);
        Assert.True((await h.Permissions.DecideAsync(rename.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var renamed = await h.Browser.ApplyNativeMutationAsync(rename, token);
        Assert.True(renamed.Operation!.IsSuccess);
        Assert.True(renamed.AuditRecorded);
        var final = Assert.Single((await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items);
        Assert.Equal(created, final.Id);
        Assert.Equal("Footage", final.Name);
        Assert.NotEqual(row.CurrentRevisionId, final.CurrentRevisionId);
        var changes = await h.Workspace.Provider.GetChangesAsync(null, 100, token);
        Assert.Equal(2, changes.Items.Count(change => change.ItemId == created));
        var after = await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token);
        Assert.True((await h.Browser.RetryNativeMutationAuditAsync(rename, token)).Succeeded);
        Assert.Equal(after, await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token));
    }

    [Fact]
    public async Task Copied_page_configured_App_root_and_retired_review_cannot_admit_an_effect()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var root = await h.Browser.ListAsync(h.Workspace.Actor, token: token);
        var appRoot = root.Items.Single(item => item.Id == h.Parent);
        Assert.False(h.Browser.CanRenameNativeSelection(root, appRoot));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.PrepareNativeMutationAsync(root,
            appRoot, h.Workspace.Actor, "Rename", "Wrong root", () => true, "files-control", token));
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => h.Browser.PrepareNativeMutationAsync(page with { },
            null, h.Workspace.Actor, "CreateFolder", "Copied page", () => true, "files-control", token));
        var prepared = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Cancelled review", () => true, "files-control", token);
        var copied = (FilesNativeBrowserService.PreparedMutation)typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(prepared, null)!;
        Assert.Throws<UnauthorizedAccessException>(() => { _ = h.Browser.ApplyNativeMutationAsync(copied, token); });
        Assert.True((await h.Permissions.DecideAsync(prepared.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        h.Browser.RetireNativeMutationReview(prepared);
        Assert.Throws<InvalidOperationException>(() => { _ = h.Browser.ApplyNativeMutationAsync(prepared, token); });
        Assert.Empty((await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items);
    }

    [Fact]
    public async Task Changed_store_or_retired_original_view_refuses_after_approval_without_committing_prepared_item()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var live = true;
        var retired = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Late original", () => live, "files-control", token);
        Assert.True((await h.Permissions.DecideAsync(retired.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        live = false;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Browser.ApplyNativeMutationAsync(retired, token));
        var stale = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Stale original", () => true, "files-control", token);
        Assert.True((await h.Permissions.DecideAsync(stale.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var concurrent = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Actual concurrent folder", () => true, "files-control", token);
        Assert.True((await h.Permissions.DecideAsync(concurrent.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        Assert.True((await h.Browser.ApplyNativeMutationAsync(concurrent, token)).Operation!.IsSuccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Browser.ApplyNativeMutationAsync(stale, token));
        Assert.Equal("Actual concurrent folder", Assert.Single(
            (await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token)).Items).Name);
    }

    [Fact]
    public async Task Saved_Files_ACK_survives_terminal_Home_audit_fault_and_retry_never_repeats_the_effect()
    {
        var token = TestContext.Current.CancellationToken;
        var observer = new PersistedObserver();
        await using var h = await Harness.CreateAsync(token, observer);
        var page = await h.Browser.ListAsync(h.Workspace.Actor, h.Parent.Value, token: token);
        var prepared = await h.Browser.PrepareNativeMutationAsync(page, null, h.Workspace.Actor,
            "CreateFolder", "Saved once", () => true, "files-audit-fault-control", token);
        Assert.True((await h.Permissions.DecideAsync(prepared.Approval.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: token)).Succeeded);
        var first = new IOException("Actual terminal Home publication ACK lost.");
        var sibling = new InvalidOperationException("Original terminal observer companion fault.");
        var faultCount = 0;
        observer.BeforeAcknowledgement = async (id, revision, ct) =>
        {
            if (id != "home.permissions-trust") return;
            // Observe only actual already-persisted Home bytes after its locks release;
            // do not reenter permission methods whose original operation may still own its gate.
            var read = await h.Home.ReadAsync(ct);
            Assert.True(read.IsSuccess);
            var record = read.State!.Records.Single(row => row.RecordId == id);
            Assert.Equal(revision, record.Revision);
            var request = record.Payload.GetProperty("Requests").EnumerateArray()
                .Single(row => row.GetProperty("RequestId").GetString() == prepared.Approval.RequestId);
            if (request.GetProperty("State").GetInt32() == (int)HomePermissionRequestState.Succeeded
                && Interlocked.Increment(ref faultCount) == 1)
            {
                Assert.Equal(FilesOperationState.Committed, prepared.ObservedOperation!.Value!.State);
                throw new AggregateException(first, sibling);
            }
        };
        var actual = h.Browser.ApplyNativeMutationAsync(prepared, token);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(failure.Flatten().InnerExceptions, error => ReferenceEquals(error, first));
        Assert.Contains(failure.Flatten().InnerExceptions, error => ReferenceEquals(error, sibling));
        var ack = prepared.ObservedOperation!;
        Assert.True(ack.IsSuccess); Assert.Equal(FilesOperationState.Committed, ack.Value!.State);
        Assert.True(prepared.HasObservedOutcome);
        Assert.Same(actual, h.Browser.ApplyNativeMutationAsync(prepared, token));
        Assert.Same(failure, await Assert.ThrowsAsync<AggregateException>(() => actual));
        var evidence = await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token);
        var change = Assert.Single((await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items,
            row => row.ItemId == ack.Value.ItemId);
        Assert.Equal(ack.Value.Id, change.OperationId);
        observer.BeforeAcknowledgement = null;
        Assert.True((await h.Browser.RetryNativeMutationAuditAsync(prepared, token)).Succeeded);
        Assert.Same(ack, prepared.ObservedOperation);
        Assert.Equal(evidence, await h.Workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token));
        Assert.Single((await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items, row => row.ItemId == ack.Value.ItemId);
        Assert.Same(actual, h.Browser.ApplyNativeMutationAsync(prepared, token));
        Assert.Equal(1, faultCount);
    }

    [AvaloniaFact]
    public async Task Native_controls_stage_Home_review_then_publish_observed_folder_and_preserve_protected_roots()
    {
        var token = TestContext.Current.CancellationToken;
        await using var h = await Harness.CreateAsync(token);
        await using var view = new FilesNativeBrowserSurface(h.Browser, null, h.Workspace.Actor,
            new Readiness(), CancellationToken.None);
        var window = new Window { Content = view, Width = 720, Height = 640 };
        window.Show();
        try
        {
            await view.InitializeAsync(token); window.UpdateLayout();
            var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == h.Parent);
            var name = view.GetVisualDescendants().OfType<TextBox>().Single(input => input.PlaceholderText == "Folder or item name");
            name.Text = "Protected root";
            Assert.False(view.IsActionAvailable("9to1.Files.Rename"));
            await view.DispatchAsync("9to1.Files.Open", null, token);
            name.Text = "Native folder";
            Assert.True(view.IsActionAvailable("9to1.Files.CreateFolder"));
            await view.DispatchAsync("9to1.Files.CreateFolder", null, token);
            Assert.Empty(list.Items);
            Assert.True(view.IsActionAvailable("9to1.Files.Apply"));
            Assert.False(view.IsActionAvailable("9to1.Files.Refresh"));
            var pending = Assert.Single((await h.Permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.Equal("files", pending.Scope.TargetAppId);
            Assert.Equal("9to1.Files.CreateFolder", pending.Scope.ActionName);
            Assert.True((await h.Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: token)).Succeeded);
            await view.DispatchAsync("9to1.Files.Apply", null, token);
            Assert.Equal("Native folder", Assert.Single(list.Items.Cast<HostedItemMetadata>()).Name);
            Assert.True(view.IsActionAvailable("9to1.Files.Refresh"));
            Assert.False(view.IsActionAvailable("9to1.Files.Apply"));
        }
        finally { await view.CloseAndDrainAsync(); window.Close(); }
    }

    private sealed class PersistedObserver : IHomePersistedWriteObserver
    {
        internal Func<string, long, CancellationToken, Task>? BeforeAcknowledgement;
        public Task OnPersistedWriteAsync(string id, long revision, CancellationToken token) =>
            BeforeAcknowledgement?.Invoke(id, revision, token) ?? Task.CompletedTask;
    }

    private sealed class Readiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "CONTROL", "Source test host")); }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required string Root { get; init; }
        public required ServiceProvider Graph { get; init; }
        public required HomePermissionTrustService Permissions { get; init; }
        public required FileHomeCoreStateStore Home { get; init; }
        public required NativeFilesWorkspace Workspace { get; init; }
        public required FilesNativeBrowserService Browser { get; init; }
        public HostedItemId Parent => Workspace.Configuration.AppFolders["write"];
        public static async Task<Harness> CreateAsync(CancellationToken token, PersistedObserver? observer = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-files-browser-mutation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var home = observer is null ? new FileHomeCoreStateStore(Path.Combine(root, "home.json"))
                    : new FileHomeCoreStateStore(Path.Combine(root, "home.json"), observer);
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var policy = new FilesNativeBrowserActionPolicySource();
                var permissions = new HomePermissionTrustService(home, policy.TryGet);
                var services = new ServiceCollection();
                services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles);
                services.AddSingleton<IAuthenticatedResourceActorSource>(profiles); services.AddSingleton(permissions);
                services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
                services.AddSingleton<HomeLocalStoreOwnership>();
                services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
                services.AddSingleton<ResourceAuthorizationService>(); services.AddSingleton<HomeResourceOperationBroker>();
                services.AddFilesNativeHost();
                var graph = services.BuildServiceProvider();
                try
                {
                    var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
                    await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                        graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
                    var workspace = await graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token)
                        ?? throw new InvalidOperationException("The actual Files setup did not produce its owned workspace.");
                    return new() { Root = root, Graph = graph, Permissions = permissions, Home = home, Workspace = workspace,
                        Browser = graph.GetRequiredService<FilesNativeBrowserService>() };
                }
                catch { await graph.DisposeAsync(); throw; }
            }
            catch { Directory.Delete(root, true); throw; }
        }
        public async ValueTask DisposeAsync() { await Graph.DisposeAsync(); Directory.Delete(Root, true); }
    }
}
