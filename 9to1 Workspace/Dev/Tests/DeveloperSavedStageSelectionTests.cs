using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperSavedStageSelectionTests
{
    [Fact]
    public void Independent_stage_inspection_keeps_exact_saved_command_and_detaches_caller_lists()
    {
        var project = Project(); var original = project.Workspace.Tasks[0];
        var arguments = new List<string>(); var dependencies = new List<string>();
        var stages = new List<DeveloperBuildStage> { original.Stages[0] with { Arguments = arguments, DependsOn = dependencies } };
        var task = original with { Stages = stages };
        project = project with { Workspace = project.Workspace with { Tasks = [task] } };
        var result = DeveloperSavedStageSelection.Observe(project); Assert.True(result.Succeeded, result.Error?.Message);
        var selected = Assert.Single(result.Value!); Assert.Null(selected.UnavailableReason);
        Assert.Equal("  printf 'saved; $(literal)'  ", selected.Stage.Command); Assert.Equal(2, selected.TimeoutSeconds);
        arguments.Add("changed"); dependencies.Add("prerequisite"); stages.Clear();
        Assert.Empty(selected.Stage.Arguments); Assert.Empty(selected.Stage.DependsOn); Assert.Single(selected.Task.Stages);
        Assert.Equal(selected.DefinitionSha256, DeveloperTaskWorkspaceService.ComputeSavedTaskDefinitionSha256(selected.Task));
    }

    [Fact]
    public void Exact_stage_selection_refuses_changed_command_even_without_task_revision_increment()
    {
        var project = Project(); var selected = Assert.Single(DeveloperSavedStageSelection.Observe(project).Value!);
        Assert.True(DeveloperSavedStageSelection.Select(project, selected.Task.TaskId, selected.Task.Revision,
            selected.Stage.StageId, selected.DefinitionSha256).Succeeded);
        var changed = selected.Task with { Stages = [selected.Stage with { Command = "replacement" }] };
        var result = DeveloperSavedStageSelection.Select(project with { Workspace = project.Workspace with { Tasks = [changed] } },
            selected.Task.TaskId, selected.Task.Revision, selected.Stage.StageId, selected.DefinitionSha256);
        Assert.False(result.Succeeded); Assert.Null(result.Value); Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, result.Error!.Code);
    }

    [Fact]
    public void Dependencies_argument_vectors_remote_and_invalid_timeouts_remain_visible_but_unavailable()
    {
        var project = Project(); var task = project.Workspace.Tasks[0]; var stage = task.Stages[0];
        var candidates = new[] { task with { DependsOn = ["restore"] }, task with { Stages = [stage with { DependsOn = ["restore"] }] },
            task with { Stages = [stage with { Arguments = ["--test"] }] }, task with { Stages = [stage with { Timeout = TimeSpan.Zero }] },
            task with { Stages = [stage with { Timeout = TimeSpan.FromSeconds(901) }] } };
        foreach (var candidate in candidates)
        {
            var current = project with { Workspace = project.Workspace with { Tasks = [candidate] } };
            var choice = Assert.Single(DeveloperSavedStageSelection.Observe(current).Value!); Assert.NotNull(choice.UnavailableReason);
            Assert.False(DeveloperSavedStageSelection.Select(current, choice.Task.TaskId, choice.Task.Revision, choice.Stage.StageId, choice.DefinitionSha256).Succeeded);
        }
        var remote = project with { Root = project.Root with { EnvironmentId = "remote" },
            Workspace = project.Workspace with { Tasks = [task with { EnvironmentId = "remote" }] } };
        Assert.Contains("Remote", Assert.Single(DeveloperSavedStageSelection.Observe(remote).Value!).UnavailableReason!);
    }

    [Fact]
    public void Duplicate_stage_or_task_ids_are_refused_and_other_roots_are_not_offered()
    {
        var project = Project(); var task = project.Workspace.Tasks[0];
        foreach (var tasks in new IReadOnlyList<DeveloperTaskDefinition>[] { [task, task], [task with { Stages = [task.Stages[0], task.Stages[0]] }] })
        {
            var result = DeveloperSavedStageSelection.Observe(project with { Workspace = project.Workspace with { Tasks = tasks } });
            Assert.False(result.Succeeded); Assert.Null(result.Value); Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, result.Error!.Code);
        }
        Assert.Empty(DeveloperSavedStageSelection.Observe(project with { Workspace = project.Workspace with
            { Tasks = [task with { WorkingDirectoryRootId = Guid.NewGuid().ToString("D") }] } }).Value!);
    }

    private static DeveloperResolvedProject Project()
    {
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("saved-stage-root"));
        var project = new DeveloperProject(Guid.NewGuid(), "code", "Saved stage project", [root.RootId], "cui", null, "custom", [], [], [], [], null);
        var task = new DeveloperTaskDefinition("build", "Build", "saved", "local", root.RootId.ToString("D"), [],
            [new("compile", DeveloperBuildStageKind.Compile, "Compile", [], "  printf 'saved; $(literal)'  ", [], TimeSpan.FromSeconds(1.25))], true);
        var workspace = DeveloperWorkspace.Create([root]) with { Projects = [project], Tasks = [task] };
        return new(new(workspace.WorkspaceId, 1, project.ProjectId, 1, root.RootId, null), workspace, project, root, null);
    }
}
