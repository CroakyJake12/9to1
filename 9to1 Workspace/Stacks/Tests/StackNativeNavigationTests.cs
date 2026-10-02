using HavenOS.Apps.Stacks.NativeUI;
using Xunit;

namespace HavenOS.Apps.Stacks.Tests;

/// <summary>Real canonical Stack store/engine; the read-authority callback is an explicit controlled fixture.</summary>
public sealed class StackNativeNavigationTests
{
    [Fact]
    public async Task Native_projects_domains_history_and_activity_keep_canonical_identity_after_reopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-stacks-native-" + Guid.NewGuid().ToString("N"));
        var actor = new StackActor("controlled-maintainer", Enum.GetValues<StackCapability>().ToHashSet());
        try
        {
            var engine = new StackEngine(new JsonFileStackProjectStore(directory));
            var main = await engine.CreateProjectAsync(new("Native fixture", StackStorageMode.Files, directory), actor);
            var branch = await engine.CreateBranchAsync(main.ProjectId, "Feature", actor);
            await engine.ApplyChangeAsync(branch.Id, new(StackMutationKind.Upsert, "src/example.txt", new("canonical source"u8.ToArray())), actor);
            var commit = await engine.CreateCommitAsync(branch.Id, "Actual canonical commit", actor);
            await engine.RenameDomainAsync(branch.Id, "Feature renamed", actor);
            await engine.ApplyChangeAsync(branch.Id, new(StackMutationKind.Upsert, "src/example.txt", new("persisted working change"u8.ToArray())), actor);
            var reopened = new StackEngine(new JsonFileStackProjectStore(directory));
            await reopened.OpenProjectAsync();
            var source = new ControlledSource(new(main.ProjectId, reopened, actor.ActorId, _ => Task.FromResult(true)));
            var controller = new StackCuiController(source);
            await controller.DispatchAsync("RefreshProjects", null);
            Assert.Equal(main.ProjectId.ToString("D"), Assert.Single(controller.Projects)["ProjectID"]);
            await controller.DispatchAsync("SelectProject", main.ProjectId.ToString("D"));
            Assert.Equal(2, controller.Tabs.Count);
            Assert.Equal("Home", controller.Tabs[0]["Name"]);
            var domain = Assert.Single(controller.Domains, row => Equals(row["DomainID"], branch.Id.ToString("D")));
            Assert.Equal("Feature renamed", domain["Name"]);
            await controller.DispatchAsync("SelectDomain", branch.Id.ToString("D"));
            Assert.Equal("src/example.txt", Assert.Single(controller.Files)["Path"]);
            var fileView = Assert.Single(controller.Files);
            Assert.Equal(commit.Id.ToString("D"), fileView["HeadRevisionID"]);
            Assert.Equal(reopened.LoadedRevisionToken, fileView["ManifestRevision"]);
            Assert.Equal((long)"persisted working change".Length, fileView["Size"]);
            Assert.False(fileView.ContainsKey("RevisionID")); // Working bytes are not labeled immutable commit content.
            Assert.True(controller.TrySetValue("DomainID", branch.Id.ToString("D")));
            await controller.DispatchAsync("ShowHistory", null);
            Assert.Equal(commit.Id.ToString("D"), Assert.Single(controller.History)["RevisionID"]);
            await controller.DispatchAsync("ShowActivity", null);
            Assert.Contains(controller.Activity, row => Equals(row["Action"], "DomainRenamed") && Equals(row["TargetID"], branch.Id.ToString("D")));
            Assert.True(controller.IsActionAvailable("Back"));
            await controller.DispatchAsync("Back", null);
            Assert.Single(controller.History);
            await controller.DispatchAsync("Home", null);
            Assert.Empty(controller.History);
            await controller.DispatchAsync("SelectProject", main.ProjectId.ToString("D"));
            Assert.Single(controller.History); // Navigation belongs to its original project tab.
            Assert.False(controller.TrySetValue("RootDirectory", directory));
            Assert.False(controller.IsActionAvailable("ExecuteArbitraryCommand"));
            source.FailListing = true;
            await controller.DispatchAsync("RefreshProjects", null);
            Assert.Empty(controller.Projects);
            Assert.Single(controller.Tabs);
            Assert.Empty(controller.Domains);
            await controller.DispatchAsync("SelectProject", main.ProjectId.ToString("D"));
            Assert.Empty(controller.Domains); // Failed fresh listing cannot retain an old admission.

        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Revoked_read_binding_clears_native_source_and_cannot_open_unlisted_project()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-stacks-revoked-" + Guid.NewGuid().ToString("N"));
        var actor = new StackActor("controlled-reader", Enum.GetValues<StackCapability>().ToHashSet());
        try
        {
            var engine = new StackEngine(new JsonFileStackProjectStore(directory));
            var main = await engine.CreateProjectAsync(new("Revocation fixture", StackStorageMode.Files, directory), actor);
            var current = true;
            var controller = new StackCuiController(new ControlledSource(new(main.ProjectId, engine, actor.ActorId, _ => Task.FromResult(current))));
            await controller.DispatchAsync("RefreshProjects", null);
            await controller.DispatchAsync("SelectProject", main.ProjectId.ToString("D"));
            await controller.DispatchAsync("SelectDomain", main.Id.ToString("D"));
            current = false;
            await controller.DispatchAsync("RefreshProject", null);
            Assert.Empty(controller.Files); Assert.Empty(controller.Domains); Assert.Empty(controller.Activity); Assert.Empty(controller.History);
            await controller.DispatchAsync("SelectProject", Guid.NewGuid().ToString("D"));
            Assert.Empty(controller.Domains);
            Assert.Single((await engine.GetLineageAsync())); // Navigation never created a second source domain.
            var denied = new StackActor("unprivileged", new HashSet<StackCapability>());
            var failure = await Assert.ThrowsAsync<StackFailureException>(() => engine.GetActivityAsync(denied));
            Assert.Equal(StackFailureCode.PermissionDenied, failure.Code);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Queued_native_action_cannot_adopt_selection_edited_during_suspended_owner_read()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-stacks-selection-" + Guid.NewGuid().ToString("N"));
        var actor = new StackActor("controlled-original-reader", Enum.GetValues<StackCapability>().ToHashSet());
        try
        {
            var engine = new StackEngine(new JsonFileStackProjectStore(directory));
            var main = await engine.CreateProjectAsync(new("Selection fixture", StackStorageMode.Files, directory), actor);
            var source = new SuspendedSource(new(main.ProjectId, engine, actor.ActorId, _ => Task.FromResult(true)));
            var controller = new StackCuiController(source);
            await controller.DispatchAsync("RefreshProjects", null);
            source.BlockNext = true;
            var suspended = controller.DispatchAsync("RefreshProjects", null).AsTask();
            await source.Entered.Task;
            Assert.True(controller.TrySetValue("ProjectID", main.ProjectId.ToString("D")));
            var queued = controller.DispatchAsync("OpenProject", null).AsTask();
            Assert.True(controller.TrySetValue("ProjectID", Guid.NewGuid().ToString("D")));
            source.Release.SetResult(true);
            await suspended; await queued;
            Assert.Single(controller.Projects); // A text race does not adopt or replace the authorized list.
            Assert.Single(controller.Tabs);
            Assert.Empty(controller.Domains);
            Assert.True(controller.TryGetValue("Status", out var status));
            Assert.Contains("selection changed", Assert.IsType<string>(status), StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class SuspendedSource(StackNativeProjectContext context) : IStackNativeProjectSource
    {
        public bool BlockNext { get; set; }
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<StackNativeProjectContext>> ListAuthorizedAsync(CancellationToken cancellationToken = default)
        {
            if (BlockNext)
            {
                BlockNext = false; Entered.SetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return [context];
        }
    }

    private sealed class ControlledSource(StackNativeProjectContext context) : IStackNativeProjectSource
    {
        public bool FailListing { get; set; }
        public Task<IReadOnlyList<StackNativeProjectContext>> ListAuthorizedAsync(CancellationToken cancellationToken = default) =>
            FailListing ? Task.FromException<IReadOnlyList<StackNativeProjectContext>>(new IOException("Controlled owning source unavailable.")) :
                Task.FromResult<IReadOnlyList<StackNativeProjectContext>>([context]);
    }
}
