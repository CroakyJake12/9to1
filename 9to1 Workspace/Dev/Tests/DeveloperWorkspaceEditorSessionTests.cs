using System.Text.Json.Nodes;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperWorkspaceEditorSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-editor-session-tests", Guid.NewGuid().ToString("N"));
    private static readonly DeveloperCleanEditorTab First = new("src/main.cs", new string('a', 64), false, true, "right");
    private static readonly DeveloperCleanEditorTab Second = new("notes.txt", new string('b', 64), true, false, "left");

    [Fact]
    public async Task CleanSession_RestartsWithOrderSelectionPreviewAndPinsWithoutChangingAuthority()
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
        Assert.True(loaded.Value.Tabs[0].IsPinned);
        Assert.Equal("left", loaded.Value.Tabs[0].GroupId);
        Assert.True(loaded.Value.Tabs[1].IsPreview);
        Assert.Equal("right", loaded.Value.Tabs[1].GroupId);
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
        var missingGroup = await store.SaveEditorSessionAsync(project, profile, 1,
            [First with { GroupId = " " }], First.RelativePath);
        var exhaustedGeneration = await store.SaveEditorSessionAsync(project, profile, long.MaxValue, [First], First.RelativePath);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, stale.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, traversal.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, duplicate.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, missingGroup.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, exhaustedGeneration.Error!.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CancelledSessionWrite_PreservesReopenablePriorTabsAndSelection()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        var first = await store.SaveEditorSessionAsync(project, profile, 0, [Second, First], First.RelativePath);
        Assert.True(first.Succeeded, first.Error?.Message);
        var firstSession = first.Value!;
        var path = Path.Combine(_root, "workspaces", project.Reference.WorkspaceId.ToString("N") + ".json");
        var before = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveEditorSessionAsync(project, profile, firstSession.Generation, [First], First.RelativePath, cancellation.Token));

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        var reopened = new FileDeveloperWorkspaceStore(_root);
        var loaded = await reopened.GetEditorSessionAsync(project, profile);
        Assert.True(loaded.Succeeded, loaded.Error?.Message);
        var reopenedSession = loaded.Value!;
        Assert.Equal(firstSession.Generation, reopenedSession.Generation);
        Assert.Equal(firstSession.ProjectReference, reopenedSession.ProjectReference);
        Assert.Equal([Second, First], reopenedSession.Tabs);
        Assert.Equal(First.RelativePath, reopenedSession.SelectedRelativePath);
    }

    [Fact]
    public async Task QueuedSessionSave_CapturesCallerTabsBeforeTheFirstAwait()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        var callerTabs = new List<DeveloperCleanEditorTab> { First };

        var pending = store.SaveEditorSessionAsync(project, profile, 0, callerTabs, First.RelativePath);
        callerTabs[0] = Second;
        callerTabs.Add(Second with { RelativePath = "later.cs" });

        var saved = await pending;

        Assert.True(saved.Succeeded, saved.Error?.Message);
        var session = saved.Value!;
        Assert.Equal([First], session.Tabs);
        Assert.Equal(First.RelativePath, session.SelectedRelativePath);
    }

    [Fact]
    public async Task CleanSessionSave_ValidatesTheExactDetachedTabsItPersists()
    {
        var (store, project) = await CreateAsync();
        var changingTabs = new ChangingCleanTabs([First], [First with { RelativePath = "../escape.cs" }]);

        var saved = await store.SaveEditorSessionAsync(project, Guid.NewGuid(), 0, changingTabs, First.RelativePath);

        Assert.True(saved.Succeeded, saved.Error?.Message);
        Assert.Equal([First], saved.Value!.Tabs);
    }

    private sealed class ChangingCleanTabs(
        IReadOnlyList<DeveloperCleanEditorTab> valid, IReadOnlyList<DeveloperCleanEditorTab> later) :
        IReadOnlyList<DeveloperCleanEditorTab>
    {
        private int _enumerations;
        public int Count => valid.Count;
        public DeveloperCleanEditorTab this[int index] => (_enumerations < 3 ? valid : later)[index];
        public IEnumerator<DeveloperCleanEditorTab> GetEnumerator()
        {
            var observed = Interlocked.Increment(ref _enumerations) <= 3 ? valid : later;
            return observed.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task OlderSessionWithoutPreviewOrGroup_RestoresWithLegacyDefaults()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        Assert.True((await store.SaveEditorSessionAsync(project, profile, 0, [First], First.RelativePath)).Succeeded);
        var path = Path.Combine(_root, "workspaces", project.Reference.WorkspaceId.ToString("N") + ".json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        document["editorSessions"]![0]!["tabs"]![0]!.AsObject().Remove("isPreview");
        document["editorSessions"]![0]!["tabs"]![0]!.AsObject().Remove("groupId");
        await File.WriteAllTextAsync(path, document.ToJsonString());

        var reopened = await new FileDeveloperWorkspaceStore(_root).GetEditorSessionAsync(project, profile);

        Assert.True(reopened.Succeeded, reopened.Error?.Message);
        var session = reopened.Value!;
        Assert.Single(session.Tabs);
        Assert.False(session.Tabs[0].IsPreview);
        Assert.False(session.Tabs[0].IsPinned);
        Assert.Equal("default", session.Tabs[0].GroupId);
    }

    [Fact]
    public async Task PreviewTabs_AreIndependentAcrossEditorGroups()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        var leftPreview = First with { RelativePath = "left.cs", GroupId = "left" };
        var rightPreview = First with { RelativePath = "right.cs", GroupId = "right" };

        var saved = await store.SaveEditorSessionAsync(project, profile, 0,
            [leftPreview, rightPreview], rightPreview.RelativePath);

        Assert.True(saved.Succeeded, saved.Error?.Message);
        var session = saved.Value!;
        Assert.Equal(new[] { "left", "right" }, session.Tabs.Select(tab => tab.GroupId));
        Assert.All(session.Tabs, tab => Assert.True(tab.IsPreview));
    }

    [Fact]
    public async Task PreviewAndPinnedFlags_RoundTripAsIndependentCanonicalMetadata()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        var state = First with { IsPinned = true };

        var saved = await store.SaveEditorSessionAsync(project, profile, 0, [state], state.RelativePath);

        Assert.True(saved.Succeeded, saved.Error?.Message);
        var restored = await new FileDeveloperWorkspaceStore(_root).GetEditorSessionAsync(project, profile);
        Assert.True(restored.Succeeded, restored.Error?.Message);
        var session = restored.Value!;
        Assert.True(session.Tabs[0].IsPreview);
        Assert.True(session.Tabs[0].IsPinned);
    }

    [Fact]
    public async Task CleanTabPathUniqueness_UsesHostFilesystemCaseSemantics()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        var caseVariant = First with { RelativePath = "SRC/MAIN.CS" };

        var saved = await store.SaveEditorSessionAsync(project, profile, 0,
            [First, caseVariant], First.RelativePath);

        if (OperatingSystem.IsWindows())
            Assert.Equal(DeveloperOperationErrorCode.InvalidInput, saved.Error!.Code);
        else
        {
            Assert.True(saved.Succeeded, saved.Error?.Message);
            Assert.Equal(2, saved.Value!.Tabs.Count);
        }

        var selectionProfile = Guid.NewGuid();
        var caseVariantSelection = await store.SaveEditorSessionAsync(project, selectionProfile, 0,
            [First], "SRC/MAIN.CS");
        if (OperatingSystem.IsWindows())
        {
            Assert.True(caseVariantSelection.Succeeded, caseVariantSelection.Error?.Message);
            Assert.Equal(First.RelativePath, caseVariantSelection.Value!.SelectedRelativePath);
        }
        else Assert.Equal(DeveloperOperationErrorCode.InvalidInput, caseVariantSelection.Error!.Code);
    }

    [Fact]
    public async Task InvalidStoredSessionTimestamp_FailsClosedWithoutRewritingCanonicalBytes()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        Assert.True((await store.SaveEditorSessionAsync(project, profile, 0, [First], First.RelativePath)).Succeeded);
        var path = Path.Combine(_root, "workspaces", project.Reference.WorkspaceId.ToString("N") + ".json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        document["editorSessions"]![0]!["savedAt"] = default(DateTimeOffset).ToString("O");
        await File.WriteAllTextAsync(path, document.ToJsonString());
        var corruptBytes = await File.ReadAllBytesAsync(path);

        var loaded = await new FileDeveloperWorkspaceStore(_root).GetEditorSessionAsync(project, profile);

        Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, loaded.Error!.Code);
        Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(path));

        document["editorSessions"]![0]!["savedAt"] = "2026-10-24T12:00:00+02:00";
        await File.WriteAllTextAsync(path, document.ToJsonString());
        corruptBytes = await File.ReadAllBytesAsync(path);
        loaded = await new FileDeveloperWorkspaceStore(_root).GetEditorSessionAsync(project, profile);
        Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, loaded.Error!.Code);
        Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task InvalidStoredEditorGroup_FailsClosedWithoutRewritingCanonicalBytes()
    {
        var (store, project) = await CreateAsync();
        var profile = Guid.NewGuid();
        Assert.True((await store.SaveEditorSessionAsync(project, profile, 0, [First], First.RelativePath)).Succeeded);
        var path = Path.Combine(_root, "workspaces", project.Reference.WorkspaceId.ToString("N") + ".json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        document["editorSessions"]![0]!["tabs"]![0]!["groupId"] = " ";
        await File.WriteAllTextAsync(path, document.ToJsonString());
        var corruptBytes = await File.ReadAllBytesAsync(path);

        var loaded = await new FileDeveloperWorkspaceStore(_root).GetEditorSessionAsync(project, profile);

        Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, loaded.Error!.Code);
        Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task RepositoryRebind_DoesNotRestoreSessionForChangedCanonicalRepository()
    {
        var store = new FileDeveloperWorkspaceStore(_root);
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath(Path.GetTempPath()));
        var project = new DeveloperProject(Guid.NewGuid(), "console", "Example", [root.RootId], "C#", null,
            "dotnet", ["linux"], [], [], [], null);
        var binding = new DeveloperSourceControlBinding("repo-binding", root.RootId, "git", "repo-before");
        var created = await store.CreateAsync(DeveloperWorkspace.Create([root]) with
        { Projects = [project], SourceControlBindings = [binding] });
        Assert.True(created.Succeeded, created.Error?.Message);
        var workspace = created.Value!;
        var reference = new DeveloperProjectReference(workspace.WorkspaceId, workspace.Revision,
            project.ProjectId, project.Revision, root.RootId, binding.BindingId);
        var resolved = new DeveloperResolvedProject(reference, workspace, project, root, binding);
        var profile = Guid.NewGuid();
        Assert.True((await store.SaveEditorSessionAsync(resolved, profile, 0, [First], First.RelativePath)).Succeeded);
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "workspaces", workspace.WorkspaceId.ToString("N") + ".json"));

        var changedBinding = binding with { CanonicalRepositoryId = "repo-after" };
        var changed = await store.SaveAsync(workspace with { SourceControlBindings = [changedBinding] }, workspace.Revision);
        Assert.True(changed.Succeeded, changed.Error?.Message);
        var reboundWorkspace = changed.Value!;
        var rebound = resolved with
        {
            Workspace = reboundWorkspace,
            Reference = reference with { WorkspaceRevision = reboundWorkspace.Revision },
            Repository = changedBinding
        };
        var changedBytes = await File.ReadAllBytesAsync(Path.Combine(_root, "workspaces", workspace.WorkspaceId.ToString("N") + ".json"));

        var refused = await new FileDeveloperWorkspaceStore(_root).GetEditorSessionAsync(rebound, profile);

        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, refused.Error!.Code);
        Assert.NotEqual(before, changedBytes);
        Assert.Equal(changedBytes, await File.ReadAllBytesAsync(Path.Combine(_root, "workspaces", workspace.WorkspaceId.ToString("N") + ".json")));
    }

    [Fact]
    public async Task RepositoryBindingReplacement_StartsNewCleanSessionWithoutOverwritingPriorGeneration()
    {
        var store = new FileDeveloperWorkspaceStore(_root);
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath(Path.GetTempPath()));
        var project = new DeveloperProject(Guid.NewGuid(), "console", "Example", [root.RootId], "C#", null,
            "dotnet", ["linux"], [], [], [], null);
        var firstBinding = new DeveloperSourceControlBinding("repo-one", root.RootId, "git", "repository-one");
        var secondBinding = new DeveloperSourceControlBinding("repo-two", root.RootId, "git", "repository-two");
        var created = await store.CreateAsync(DeveloperWorkspace.Create([root]) with
        { Projects = [project], SourceControlBindings = [firstBinding] });
        Assert.True(created.Succeeded, created.Error?.Message);
        var initialWorkspace = created.Value!;
        var firstProject = new DeveloperResolvedProject(
            new(initialWorkspace.WorkspaceId, initialWorkspace.Revision, project.ProjectId, project.Revision, root.RootId, firstBinding.BindingId),
            initialWorkspace, project, root, firstBinding);
        var profile = Guid.NewGuid();

        Assert.True((await store.SaveEditorSessionAsync(firstProject, profile, 0, [First], First.RelativePath)).Succeeded);
        var updated = await store.SaveAsync(initialWorkspace with { SourceControlBindings = [secondBinding] }, initialWorkspace.Revision);
        Assert.True(updated.Succeeded, updated.Error?.Message);
        var currentWorkspace = updated.Value!;
        var secondProject = new DeveloperResolvedProject(
            new(currentWorkspace.WorkspaceId, currentWorkspace.Revision, project.ProjectId, project.Revision, root.RootId, secondBinding.BindingId),
            currentWorkspace, project, root, secondBinding);
        var secondInitial = await store.GetEditorSessionAsync(secondProject, profile);
        Assert.True(secondInitial.Succeeded, secondInitial.Error?.Message);
        Assert.Null(secondInitial.Value);
        Assert.True((await store.SaveEditorSessionAsync(secondProject, profile, 0, [Second], Second.RelativePath)).Succeeded);

        var reopened = new FileDeveloperWorkspaceStore(_root);
        var currentSession = await reopened.GetEditorSessionAsync(secondProject, profile);
        Assert.Equal(Second.RelativePath, currentSession.Value!.SelectedRelativePath);
        var path = Path.Combine(_root, "workspaces", currentWorkspace.WorkspaceId.ToString("N") + ".json");
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal(2, persisted["editorSessions"]!.AsArray().Count);
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
