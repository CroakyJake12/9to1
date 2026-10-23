using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperWorkspaceEditorSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-editor-session-tests", Guid.NewGuid().ToString("N"));
    private static readonly DeveloperCleanEditorTab First = new("src/main.cs", new string('a', 64), true);
    private static readonly DeveloperCleanEditorTab Second = new("notes.txt", new string('b', 64), false);

    [Fact]
    public async Task CleanSession_RestartsWithOrderSelectionAndPinsWithoutChangingAuthority()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        var saved = await store.SaveEditorSessionAsync(project, profile, 0, [Second, First], First.RelativePath);
        Assert.True(saved.Succeeded, saved.Error?.Message);
        var restarted = new FileDeveloperWorkspaceStore(_root);
        var loaded = await restarted.GetEditorSessionAsync(project, profile);
        var workspace = await restarted.GetAsync(project.Reference.WorkspaceId);
        Assert.True(loaded.Succeeded, loaded.Error?.Message);
        Assert.NotNull(loaded.Value);
        Assert.Equal(1, loaded.Value.Generation);
        Assert.Equal([Second, First], loaded.Value.Tabs);
        Assert.Equal(First.RelativePath, loaded.Value.SelectedRelativePath);
        Assert.Equal(project.Reference, loaded.Value.ProjectReference);
        Assert.Equal(project.Workspace.Revision, workspace.Value!.Revision);
        Assert.Equal(project.Workspace.ModifiedAt, workspace.Value.ModifiedAt);
        Assert.Empty(workspace.Value.OpenEditors); // Session paths never become fabricated Files identities.
        var foreignProfile = await restarted.GetEditorSessionAsync(project, Guid.NewGuid());
        Assert.True(foreignProfile.Succeeded);
        Assert.Null(foreignProfile.Value);
    }

    [Fact]
    public async Task StaleSessionWriterAndInvalidPaths_PreserveExactPreviouslySavedDocument()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        Assert.True((await store.SaveEditorSessionAsync(project, profile, 0, [First], First.RelativePath)).Succeeded);
        var path = Path.Combine(_root, "workspaces", project.Reference.WorkspaceId.ToString("N") + ".json");
        var before = await File.ReadAllBytesAsync(path);
        var stale = await store.SaveEditorSessionAsync(project, profile, 0, [Second], Second.RelativePath);
        var traversal = await store.SaveEditorSessionAsync(project, profile, 1,
            [new("../outside.cs", new string('c', 64), true)], "../outside.cs");
        var duplicate = await store.SaveEditorSessionAsync(project, profile, 1, [First, First], First.RelativePath);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, stale.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, traversal.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, duplicate.Error!.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task OrdinaryWorkspaceSave_PreservesSessionAndRebindsOnlyUnchangedProject()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        Assert.True((await store.SaveEditorSessionAsync(project, profile, 0, [First, Second], Second.RelativePath)).Succeeded);
        var changedSettings = project.Workspace with
        { Settings = project.Workspace.Settings with { DefaultAiReviewMode = "manual" } };
        var updated = await store.SaveAsync(changedSettings, project.Workspace.Revision);
        Assert.True(updated.Succeeded, updated.Error?.Message);
        var rebound = project with { Workspace = updated.Value!, Reference = project.Reference with { WorkspaceRevision = updated.Value!.Revision } };
        var loaded = await store.GetEditorSessionAsync(rebound, profile);
        Assert.True(loaded.Succeeded, loaded.Error?.Message);
        Assert.Equal(rebound.Reference, loaded.Value!.ProjectReference);
        Assert.Equal(1, loaded.Value.Generation);
        Assert.Equal([First, Second], loaded.Value.Tabs);
        var renamed = project.Project with { Name = "Different project definition", Revision = project.Project.Revision + 1 };
        var changedProject = await store.SaveAsync(updated.Value! with { Projects = [renamed] }, updated.Value!.Revision);
        Assert.True(changedProject.Succeeded);
        var newSelection = rebound with { Workspace = changedProject.Value!, Project = renamed,
            Reference = rebound.Reference with { WorkspaceRevision = changedProject.Value!.Revision, ProjectRevision = renamed.Revision } };
        var refused = await store.GetEditorSessionAsync(newSelection, profile);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, refused.Error!.Code);
        // A stale producer cannot clobber the retained session after authority changed.
        var oldWrite = await store.SaveEditorSessionAsync(project, profile, 1, [], null);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, oldWrite.Error!.Code);
    }

    [Fact]
    public async Task ConcurrentSessionStores_AdmitExactlyOneGenerationWriter()
    {
        var (store, project) = await CreateAsync();
        var other = new FileDeveloperWorkspaceStore(_root); var profile = Guid.NewGuid();
        var results = await Task.WhenAll(
            store.SaveEditorSessionAsync(project, profile, 0, [First], First.RelativePath),
            other.SaveEditorSessionAsync(project, profile, 0, [Second], Second.RelativePath));
        Assert.Single(results.Where(value => value.Succeeded));
        Assert.Single(results.Where(value => value.Error?.Code == DeveloperOperationErrorCode.RevisionConflict));
        var winner = results.Single(value => value.Succeeded).Value!;
        var read = await new FileDeveloperWorkspaceStore(_root).GetEditorSessionAsync(project, profile);
        Assert.True(read.Succeeded);
        Assert.Equal(winner.Generation, read.Value!.Generation);
        Assert.Equal(winner.Tabs, read.Value.Tabs);
        Assert.Equal(winner.SelectedRelativePath, read.Value.SelectedRelativePath);
    }

    private async Task<(FileDeveloperWorkspaceStore Store, DeveloperResolvedProject Project)> CreateAsync()
    {
        var store = new FileDeveloperWorkspaceStore(_root);
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath(Path.GetTempPath()));
        var project = new DeveloperProject(Guid.NewGuid(), "console", "Example", [root.RootId], "C#", null,
            "dotnet", ["linux"], [], [], [], null);
        var created = await store.CreateAsync(DeveloperWorkspace.Create([root]) with { Projects = [project] });
        Assert.True(created.Succeeded, created.Error?.Message);
        var workspace = created.Value!;
        return (store, new(new(workspace.WorkspaceId, workspace.Revision, project.ProjectId, project.Revision, root.RootId),
            workspace, project, root, null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
