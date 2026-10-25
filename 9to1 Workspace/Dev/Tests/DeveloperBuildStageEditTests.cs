using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperBuildStageEditTests
{
    [Theory]
    [InlineData(DeveloperBuildStageKind.Compile)]
    [InlineData(DeveloperBuildStageKind.Test)]
    public void First_stage_review_keeps_exact_command_and_creates_metadata_only(DeveloperBuildStageKind kind)
    {
        var project = Project(); const string command = "  printf 'literal $(unchanged)'  ";
        var result = DeveloperBuildStageEdit.Prepare(project, "user-task", "stage", kind, command, 20);
        Assert.True(result.Succeeded, result.Error?.Message); var reviewed = result.Value!;
        Assert.Equal(command, reviewed.Stage.Command); Assert.Equal(kind, reviewed.Stage.Kind); Assert.Equal(1, reviewed.Task.Revision);
        Assert.Empty(reviewed.Task.DependsOn); Assert.Empty(reviewed.Stage.DependsOn); Assert.Empty(reviewed.Stage.Arguments);
        Assert.True(reviewed.Task.RequiresWorkspaceTrust); Assert.True(DeveloperBuildStageEdit.IsCurrent(project, reviewed));
        Assert.Empty(project.Workspace.Tasks); Assert.Null(reviewed.BeforeTaskSha256); Assert.Null(reviewed.EditedStageId);
    }

    [Fact]
    public void Existing_stage_edit_pins_entire_task_and_preserves_all_other_stages_and_fields()
    {
        var project = Project(); var other = new DeveloperBuildStage("package", DeveloperBuildStageKind.Package, "Keep label", ["compile"], "keep command", ["--keep"], TimeSpan.FromSeconds(5));
        var stages = new List<DeveloperBuildStage> { new("compile", DeveloperBuildStageKind.Compile, "Original label", [], "before", [], null), other };
        var task = new DeveloperTaskDefinition("build", "Keep task label", "user", "local", project.Root.RootId.ToString("D"), [], stages, true);
        project = project with { Workspace = project.Workspace with { Tasks = [task] } };
        var hash = DeveloperTaskWorkspaceService.ComputeSavedTaskDefinitionSha256(task);
        var reviewed = DeveloperBuildStageEdit.Prepare(project, "build", "compile", DeveloperBuildStageKind.Compile, "after", 600, hash, "compile").Value!;
        Assert.Equal(JsonSerializer.Serialize(other), JsonSerializer.Serialize(reviewed.Task.Stages[1])); Assert.Equal("Original label", reviewed.Stage.Label); Assert.Null(reviewed.Stage.Timeout);
        Assert.Equal(task.Label, reviewed.Task.Label); Assert.Equal(task.Source, reviewed.Task.Source); Assert.Equal(2, reviewed.Task.Revision);
        stages[1] = other with { Command = "raced unselected stage" };
        Assert.False(DeveloperBuildStageEdit.IsCurrent(project, reviewed)); Assert.Equal("keep command", reviewed.Task.Stages[1].Command);
        Assert.False(DeveloperBuildStageEdit.Prepare(project, "build", "compile", DeveloperBuildStageKind.Compile, "after", 600, hash, "compile").Succeeded);
    }

    [Fact]
    public void Managed_dependencies_argument_vectors_fractional_timeouts_duplicate_IDs_and_unchanged_edits_refuse()
    {
        var project = Project(); var stage = new DeveloperBuildStage("compile", DeveloperBuildStageKind.Compile, "Compile", [], "before", [], TimeSpan.FromSeconds(20));
        var task = new DeveloperTaskDefinition("build", "Build", "user", "local", project.Root.RootId.ToString("D"), [], [stage], true);
        foreach (var candidate in new[] { task with { Source = "extension-owned" }, task with { DependsOn = ["restore"] },
            task with { Stages = [stage with { DependsOn = ["restore"] }] }, task with { Stages = [stage with { Arguments = ["--test"] }] },
            task with { Stages = [stage with { Timeout = TimeSpan.FromSeconds(1.5) }] } })
        {
            var current = project with { Workspace = project.Workspace with { Tasks = [candidate] } };
            Assert.False(DeveloperBuildStageEdit.Prepare(current, "build", "compile", DeveloperBuildStageKind.Compile, "after", 20,
                DeveloperTaskWorkspaceService.ComputeSavedTaskDefinitionSha256(candidate), "compile").Succeeded);
        }
        project = project with { Workspace = project.Workspace with { Tasks = [task] } };
        Assert.False(DeveloperBuildStageEdit.Prepare(project, "build", "compile", DeveloperBuildStageKind.Compile, "before", 20,
            DeveloperTaskWorkspaceService.ComputeSavedTaskDefinitionSha256(task), "compile").Succeeded);
        Assert.False(DeveloperBuildStageEdit.Prepare(project, "build", "compile", DeveloperBuildStageKind.Compile, "new", 20).Succeeded);
        Assert.False(DeveloperBuildStageEdit.Prepare(project, "other", "stage", DeveloperBuildStageKind.Package, "new", 20).Succeeded);
        var duplicate = project with { Workspace = project.Workspace with { Tasks = [task, task] } };
        Assert.False(DeveloperBuildStageEdit.Prepare(duplicate, "other", "stage", DeveloperBuildStageKind.Test, "new", 20).Succeeded);
    }
    private static DeveloperResolvedProject Project()
    {
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("stage-edit-project"));
        var project = new DeveloperProject(Guid.NewGuid(), "cui-app", "Project", [root.RootId], "CUI", null, "cui", [], [], [], [], null);
        var workspace = DeveloperWorkspace.Create([root]) with { Projects = [project] };
        return new(new(workspace.WorkspaceId, workspace.Revision, project.ProjectId, project.Revision, root.RootId), workspace, project, root, null);
    }
}
