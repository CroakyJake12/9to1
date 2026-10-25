using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperSavedProjectChoicesTests
{
    [Fact]
    public void Every_saved_project_root_and_repository_is_an_explicit_revision_bound_choice()
    {
        var first = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("local-source"));
        var second = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("remote-source"), "container:dev");
        var workspace = DeveloperWorkspace.Create([first, second]);
        var roots = new List<Guid> { first.RootId, second.RootId };
        var project = new DeveloperProject(Guid.NewGuid(), "code", "Shared project", roots, "cui", null, "custom", [], [], [], [], null, 7);
        workspace = workspace with { Projects = [project], Revision = 11, SourceControlBindings = [
            new("origin", second.RootId, "git", "repository:origin"), new("review", second.RootId, "git", "repository:review")] };
        var result = DeveloperSavedProjectChoices.Build([workspace]);
        Assert.True(result.Succeeded, result.Error?.Message); Assert.Equal(3, result.Value!.Count);
        var local = Assert.Single(result.Value, row => row.Reference.RootId == first.RootId);
        Assert.Null(local.Reference.RepositoryBindingId);
        var review = Assert.Single(result.Value, row => row.Reference.RepositoryBindingId == "review");
        Assert.Equal(second.RootId, review.Reference.RootId); Assert.Equal(11, review.Reference.WorkspaceRevision); Assert.Equal(7, review.Reference.ProjectRevision);
        Assert.Contains(second.Location, review.Summary); Assert.Contains("container:dev", review.RootSummary); Assert.Contains("repository:review", review.RootSummary);
        Assert.Equal(3, result.Value.Select(row => row.SelectionKey).Distinct().Count());
        roots.Clear(); Assert.Equal(second.RootId, review.Reference.RootId); // detached metadata choices, never a mutable root-list reference
        Assert.True(Assert.IsAssignableFrom<IList<DeveloperSavedProjectChoice>>(result.Value).IsReadOnly);
    }

    [Fact]
    public void Changed_revisions_withdraw_old_keys_and_ambiguous_bindings_never_appear_as_empty_success()
    {
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("source")); var workspace = DeveloperWorkspace.Create([root]);
        workspace = workspace with { Projects = [new(Guid.NewGuid(), "code", "Sample", [root.RootId], "cui", null, "custom", [], [], [], [], null)] };
        var first = Assert.Single(DeveloperSavedProjectChoices.Build([workspace]).Value!);
        var next = Assert.Single(DeveloperSavedProjectChoices.Build([workspace with { Revision = 2 }]).Value!);
        Assert.NotEqual(first.SelectionKey, next.SelectionKey);
        var binding = new DeveloperSourceControlBinding("same", root.RootId, "git", "canonical:repo");
        var refused = DeveloperSavedProjectChoices.Build([workspace with { SourceControlBindings = [binding, binding] }]);
        Assert.False(refused.Succeeded); Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, refused.Error!.Code); Assert.Null(refused.Value);
        Assert.False(DeveloperSavedProjectChoices.Build([workspace, workspace]).Succeeded);
    }

    [Fact]
    public void Supported_empty_census_is_distinct_from_invalid_metadata_and_cancellation()
    {
        Assert.Empty(DeveloperSavedProjectChoices.Build([]).Value!);
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("source"));
        var invalid = DeveloperWorkspace.Create([root]) with { Projects = [new(Guid.NewGuid(), "code", "Invalid", [Guid.NewGuid()], "cui", null, "custom", [], [], [], [], null)] };
        Assert.False(DeveloperSavedProjectChoices.Build([invalid]).Succeeded);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DeveloperSavedProjectChoices.Build([], cancellation.Token));
    }
}
