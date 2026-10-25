using Xunit;
using System.Text.Json;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperRunConfigurationSelectionTests
{
    [Fact]
    public void Exact_selected_target_snapshot_carries_complete_definition_and_detached_lists()
    {
        var project = Project(); var arguments = new List<string>(); var prerequisites = new List<string>();
        var configuration = Configuration(project) with { Arguments = arguments, PreLaunchTaskIds = prerequisites };
        project = project with { Workspace = project.Workspace with { RunConfigurations = [configuration] } };
        var observed = DeveloperRunConfigurationSelection.Observe(project);
        Assert.True(observed.Succeeded); var selected = Assert.Single(observed.Value!); Assert.Null(selected.UnavailableReason);
        Assert.Equal(project.Reference, selected.Project); Assert.Contains("echo declared target", selected.Summary);
        arguments.Add("later"); prerequisites.Add("later-stage");
        Assert.Empty(selected.Configuration.Arguments); Assert.Empty(selected.Configuration.PreLaunchTaskIds);
        Assert.Equal(selected.DefinitionSha256, DeveloperRunConfigurationSelection.Digest(selected.Configuration));
        Assert.NotEqual(selected.SelectionKey, Assert.Single(DeveloperRunConfigurationSelection.Observe(project).Value!).SelectionKey);
        Assert.True(Assert.IsAssignableFrom<IList<string>>(selected.Configuration.Arguments).IsReadOnly);
    }

    [Fact]
    public void Old_constructor_and_deconstruction_are_preserved_without_silently_inventing_a_launch_command()
    {
        var project = Project(); var old = new DeveloperRunConfiguration("run", "Workspace", project.Project.ProjectId.ToString("D"),
            "console", [], project.Root.RootId.ToString("D"), "local", [], null);
        var (id, provider, target, kind, arguments, root, environment, prelaunch, preview, revision) = old;
        Assert.Equal("run", id); Assert.Equal("Workspace", provider); Assert.Equal("console", kind); Assert.Equal("local", environment);
        Assert.Equal(project.Project.ProjectId.ToString("D"), target); Assert.Equal(project.Root.RootId.ToString("D"), root);
        Assert.Empty(arguments); Assert.Empty(prelaunch); Assert.Null(preview); Assert.Equal(1, revision); Assert.Null(old.Command);
        Assert.Null(old.TimeoutSeconds);
        var wire = JsonSerializer.SerializeToElement(old);
        Assert.False(wire.TryGetProperty("Command", out _)); Assert.False(wire.TryGetProperty("TimeoutSeconds", out _));
        Assert.Contains("without Command", DeveloperRunConfigurationSelection.Unavailable(project, old)!);
    }

    [Fact]
    public void Unsupported_launch_contracts_and_ambiguous_metadata_are_distinct_from_a_supported_empty_target_list()
    {
        var project = Project(); var valid = Configuration(project);
        foreach (var unsupported in new[] { valid with { Arguments = ["--argument"] }, valid with { PreLaunchTaskIds = ["restore"] },
            valid with { PreviewProviderId = "project.web" }, valid with { TargetKind = "website" }, valid with { ProviderId = "remote" },
            valid with { EnvironmentId = "remote" }, valid with { TimeoutSeconds = 901 }, valid with { TargetProjectId = Guid.NewGuid().ToString("D") } })
            Assert.NotNull(DeveloperRunConfigurationSelection.Unavailable(project, unsupported));
        Assert.False(DeveloperRunConfigurationSelection.Observe(project with { Workspace = project.Workspace with { RunConfigurations = [valid, valid] } }).Succeeded);
        var noTargets = project.Project with { RunConfigurationIds = [] };
        var empty = project with { Project = noTargets, Workspace = project.Workspace with { Projects = [noTargets] } };
        Assert.Empty(DeveloperRunConfigurationSelection.Observe(empty).Value!);
    }

    [Fact]
    public void Declaration_review_creates_exact_local_target_and_refuses_unselected_or_changed_existing_identity()
    {
        var project = Project(); var emptyProject = project.Project with { RunConfigurationIds = [] };
        project = project with { Project = emptyProject, Workspace = project.Workspace with { Projects = [emptyProject] } };
        const string command = "printf 'exact reviewed command\n'";
        var reviewed = DeveloperRunConfigurationEdit.Prepare(project, "manual-run", command, 45);
        Assert.True(reviewed.Succeeded, reviewed.Error?.Message);
        Assert.Equal(command, reviewed.Value!.Configuration.Command); Assert.Equal(45, reviewed.Value.Configuration.TimeoutSeconds);
        Assert.Null(reviewed.Value.BeforeDefinitionSha256); Assert.True(DeveloperRunConfigurationEdit.IsCurrent(project, reviewed.Value));
        Assert.Empty(project.Project.RunConfigurationIds); Assert.Empty(project.Workspace.RunConfigurations);
        var configuration = reviewed.Value.Configuration;
        var declaredProject = emptyProject with { RunConfigurationIds = [configuration.Id] };
        var declared = project with { Project = declaredProject, Workspace = project.Workspace with { Projects = [declaredProject], RunConfigurations = [configuration] } };
        Assert.False(DeveloperRunConfigurationEdit.Prepare(declared, configuration.Id, "printf other", 45).Succeeded);
        var edit = DeveloperRunConfigurationEdit.Prepare(declared, configuration.Id, "printf other", 45,
            DeveloperRunConfigurationSelection.Digest(configuration));
        Assert.True(edit.Succeeded); Assert.Equal(configuration.Revision + 1, edit.Value!.Configuration.Revision);
        var altered = declared with { Workspace = declared.Workspace with { RunConfigurations = [configuration with { Command = "changed without revision" }] } };
        Assert.False(DeveloperRunConfigurationEdit.IsCurrent(altered, edit.Value));
    }

    [Fact]
    public void Declaration_edit_cannot_drop_unsupported_provider_arguments_or_dependencies_to_make_a_target_run()
    {
        var project = Project();
        foreach (var configuration in new[] { Configuration(project) with { Arguments = ["--keep-me"] },
            Configuration(project) with { PreLaunchTaskIds = ["keep-stage"] }, Configuration(project) with { ProviderId = "another-owner" } })
        {
            var saved = project with { Workspace = project.Workspace with { RunConfigurations = [configuration] } };
            var reviewed = DeveloperRunConfigurationEdit.Prepare(saved, configuration.Id, "printf replacement", 37,
                DeveloperRunConfigurationSelection.Digest(configuration));
            Assert.False(reviewed.Succeeded); Assert.NotNull(reviewed.Error);
            Assert.Equal(configuration, Assert.Single(saved.Workspace.RunConfigurations));
        }
    }

    private static DeveloperResolvedProject Project()
    {
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("selected-run-root"));
        var project = new DeveloperProject(Guid.NewGuid(), "code", "Declared target", [root.RootId], "cui", null, "custom", [], [], ["run"], [], null);
        var workspace = DeveloperWorkspace.Create([root]) with { Projects = [project] };
        return new(new(workspace.WorkspaceId, 1, project.ProjectId, 1, root.RootId), workspace, project, root, null);
    }
    private static DeveloperRunConfiguration Configuration(DeveloperResolvedProject project) => new("run", "Workspace", project.Project.ProjectId.ToString("D"),
        "console", [], project.Root.RootId.ToString("D"), "local", [], null) { Command = "echo declared target", TimeoutSeconds = 37 };
}
