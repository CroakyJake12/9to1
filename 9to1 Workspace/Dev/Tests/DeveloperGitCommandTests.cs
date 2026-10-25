using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperGitCommandTests
{
    [Theory]
    [InlineData(DeveloperProjectGitInspectionKind.Status)]
    [InlineData(DeveloperProjectGitInspectionKind.Diff)]
    [InlineData(DeveloperProjectGitInspectionKind.Branches)]
    public void Inspection_uses_literal_current_root_and_disables_optional_locks_and_fsmonitor(DeveloperProjectGitInspectionKind operation)
    {
        var project = Project(); var result = DeveloperGitCommand.Prepare(project, operation);
        Assert.True(result.Succeeded, result.Error?.Message);
        var command = result.Value!;
        Assert.Equal(project.Reference, command.Project); Assert.Contains("'--no-pager'", command.Command);
        Assert.Contains("'--no-optional-locks'", command.Command); Assert.Contains("'core.fsmonitor=false'", command.Command);
        Assert.Contains(OperatingSystem.IsWindows() ? "''" : "'\"'\"'", command.Command);
        if (operation == DeveloperProjectGitInspectionKind.Diff)
        { Assert.Contains("'--no-ext-diff'", command.Command); Assert.Contains("'--no-textconv'", command.Command); }
        Assert.Equal(64, command.DefinitionSha256.Length); Assert.Equal(command.DefinitionSha256, DeveloperGitCommand.Prepare(project, operation).Value!.DefinitionSha256);
    }

    [Fact]
    public void Exact_definition_captures_repository_identity_operation_and_root_without_normalisation()
    {
        var project = Project(); var original = DeveloperGitCommand.Prepare(project, DeveloperProjectGitInspectionKind.Status).Value!;
        var changedRepository = project with { Repository = project.Repository! with { CanonicalRepositoryId = "another-repository" } };
        Assert.NotEqual(original.DefinitionSha256, DeveloperGitCommand.Prepare(changedRepository, DeveloperProjectGitInspectionKind.Status).Value!.DefinitionSha256);
        Assert.NotEqual(original.DefinitionSha256, DeveloperGitCommand.Prepare(project, DeveloperProjectGitInspectionKind.Branches).Value!.DefinitionSha256);
        Assert.NotEqual(original.DefinitionSha256, DeveloperGitCommand.Prepare(project with { Root = project.Root with { Location = project.Root.Location + "-other" } }, DeveloperProjectGitInspectionKind.Status).Value!.DefinitionSha256);
    }

    [Fact]
    public void Missing_foreign_or_stale_repository_binding_and_unknown_operation_are_explicit_refusals()
    {
        var project = Project();
        foreach (var candidate in new[] { project with { Repository = null }, project with { Repository = project.Repository! with { ProviderId = "other" } },
            project with { Reference = project.Reference with { RepositoryBindingId = "foreign" } } })
        { var result = DeveloperGitCommand.Prepare(candidate, DeveloperProjectGitInspectionKind.Status); Assert.False(result.Succeeded); Assert.Null(result.Value); }
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => { _ = DeveloperGitCommand.Prepare(project, (DeveloperProjectGitInspectionKind)99); }));
    }

    [Fact]
    public void Existing_Task_branch_validation_and_literal_quoting_are_preserved()
    {
        foreach (var branch in new[] { "-option", "bad space", "bad..ref", "bad\nref", "bad:ref", "bad\\ref" })
            Assert.Throws<ArgumentException>((Action)(() => { _ = DeveloperGitCommand.Prepare(Project(), DeveloperGitOperation.CreateBranch, branch); }));
        var created = DeveloperGitCommand.Prepare(Project(), DeveloperGitOperation.CreateBranch, "feature/it's-literal");
        Assert.True(created.Succeeded); Assert.Contains("'branch' '--'", created.Value!.Command);
        Assert.DoesNotContain("'--no-optional-locks'", created.Value.Command); // Existing mutation route, never manual inspection.
    }

    private static DeveloperResolvedProject Project()
    {
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("project's literal ; $(not-executed) root"));
        var project = new DeveloperProject(Guid.NewGuid(), "code", "Git project", [root.RootId], "cui", null, "custom", [], [], [], [], null);
        var repository = new DeveloperSourceControlBinding("git-binding", root.RootId, "git", "canonical-existing-repository");
        var workspace = DeveloperWorkspace.Create([root]) with { Projects = [project], SourceControlBindings = [repository] };
        return new(new(workspace.WorkspaceId, 1, project.ProjectId, 1, root.RootId, repository.BindingId), workspace, project, root, repository);
    }
}
