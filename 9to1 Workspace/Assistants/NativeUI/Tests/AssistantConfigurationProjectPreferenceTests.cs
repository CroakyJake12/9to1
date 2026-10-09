using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

// Local form controls only. Fixture-issued metadata is not project authorization;
// the actual native producer always obtains a fresh choice from the canonical source.
public sealed class AssistantConfigurationProjectPreferenceTests
{
    [Fact]
    public void Current_source_choice_stages_same_project_reference_and_preserves_unsaved_form_until_canonical_save()
    {
        var definition = Definition(); var bindings = Bindings(definition);
        var draft = Assert.IsType<AssistantConfigurationDraft>(bindings.OriginalDraft);
        Assert.True(draft.TrySetText("DraftPurpose", "User's unsaved purpose"));
        var submitted = draft.CaptureSubmission(); var choice = Choice();
        bindings.BeginProjectSelection(false); bindings.ShowWork();
        bindings.ApplyOriginalProjectPreference(submitted, choice); bindings.ReturnToConfiguration();
        Assert.Same(draft, bindings.OriginalDraft); Assert.Equal("User's unsaved purpose", draft.Configuration.Purpose);
        Assert.Equal(choice.Project.Reference, Assert.Single(draft.Configuration.ProjectReferences));
        Assert.True(draft.IsDirty); Assert.Empty(definition.Configuration.ProjectReferences);
        // Only the maintained original canonical Configure/save acknowledgement binds
        // the new preference. Selecting metadata itself has no durable save outcome.
        var save = draft.CaptureSubmission(); var actualSaved = definition with
        { Revision = definition.Revision + 1, Configuration = save.Configuration };
        bindings.ConfigurationSaved(save, actualSaved);
        Assert.False(draft.IsDirty); Assert.Equal(actualSaved.Revision, draft.ExpectedRevision);
        Assert.Equal(choice.Project.Reference, Assert.Single(draft.Configuration.ProjectReferences));
    }

    [Fact]
    public void Delayed_choice_cannot_overwrite_a_newer_or_replaced_configuration_draft()
    {
        var definition = Definition(); var bindings = Bindings(definition);
        var first = Assert.IsType<AssistantConfigurationDraft>(bindings.OriginalDraft); var captured = first.CaptureSubmission();
        Assert.True(first.TrySetText("DraftInstructions", "Newer instructions"));
        bindings.ApplyOriginalProjectPreference(captured, Choice());
        Assert.Empty(first.Configuration.ProjectReferences); Assert.Equal("Newer instructions", first.Configuration.Instructions);
        bindings.EditConfiguration(definition);
        var replacement = Assert.IsType<AssistantConfigurationDraft>(bindings.OriginalDraft);
        bindings.ApplyOriginalProjectPreference(captured, Choice());
        Assert.NotSame(first, replacement); Assert.Empty(replacement.Configuration.ProjectReferences);
    }

    [Fact]
    public void Unsaved_new_identity_cannot_dispatch_resource_selection_and_unknown_creation_blocks_it()
    {
        var definition = Definition(); var bindings = Bindings(definition);
        Assert.True(bindings.IsActionAvailable("assistants.resources.add"));
        bindings.SetProjectSubmission(true, false, "Actual unknown creation");
        Assert.False(bindings.IsActionAvailable("assistants.resources.add"));
        bindings.SetProjectSubmission(false, false, ""); bindings.EditConfiguration(null);
        Assert.False(bindings.IsActionAvailable("assistants.resources.add"));
    }

    private static AssistantsCuiBindings Bindings(AssistantDefinitionSnapshot definition)
    {
        var bindings = new AssistantsCuiBindings((_, _, _) => ValueTask.CompletedTask, body => body(), () => true);
        bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
        { Revision = 1, SelectedAssistant = definition, Assistants = [definition] });
        bindings.EditConfiguration(definition); return bindings;
    }
    private static AssistantDefinitionSnapshot Definition() => new(new("fixture-den", "personal", "fixture-assistant"), 1,
        ConfiguredIdentityKind.Assistant, new() { Name = "Fixture Assistant" }, []);
    private static AssistantOriginalProjectChoice Choice()
    {
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("fixture-project"));
        var project = new DeveloperProject(Guid.NewGuid(), "fixture", "Fixture project", [root.RootId], "C#", null, "dotnet", [], [], [], [], null);
        var workspace = DeveloperWorkspace.Create([root]) with { Projects = [project] };
        var reference = new DeveloperProjectReference(workspace.WorkspaceId, workspace.Revision, project.ProjectId, 1, root.RootId);
        return new(new object(), new object(), new(reference, workspace, project, root, null));
    }
}
