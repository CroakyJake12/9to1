using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

/// <summary>Presentation rules only. Public work metadata grants no Task/project/Dev authority.</summary>
public sealed class AssistantsCuiProjectPresentationTests
{
    [Fact]
    public void Missing_native_route_stays_disabled_even_with_task_control_metadata()
    {
        var bindings = Bindings(false); bindings.ApplySnapshot(WorkSnapshot());
        Assert.False(bindings.IsActionAvailable("assistants.development.open"));
        Assert.True(bindings.TryGetValue("DevelopmentRouteStatus", out var status));
        Assert.Contains("unavailable", Assert.IsType<string>(status));
    }

    [Fact]
    public void Public_task_metadata_without_the_actual_conversation_binding_cannot_enable_dev()
    {
        var bindings = Bindings(true); bindings.ApplySnapshot(WorkSnapshot()); bindings.SetStreaming(true);
        Assert.False(bindings.IsActionAvailable("assistants.development.open"));
        bindings.ApplySnapshot(WorkSnapshot() with { Revision = 2, Work = null });
        Assert.False(bindings.IsActionAvailable("assistants.development.open"));
    }

    [Fact]
    public void An_empty_owner_catalogue_keeps_its_status_and_cannot_be_replaced_by_a_public_project_id()
    {
        var bindings = Bindings(false); bindings.ApplySnapshot(WorkSnapshot());
        bindings.BeginProjectSelection(false);
        bindings.SetProjectCatalogue(new([], false, "The genuine saved project source is unavailable."));
        Assert.False(bindings.IsActionAvailable("assistants.project.choose"));
        Assert.Throws<InvalidOperationException>(() => bindings.DemandOriginalProjectCandidate(Guid.NewGuid()));
        Assert.True(bindings.TryGetValue("ProjectSelectionStatus", out var status));
        Assert.Equal("The genuine saved project source is unavailable.", status);
        bindings.Revoke();
        Assert.True(bindings.TryGetValue("ProjectSelectionStatus", out status)); Assert.Null(status);
        Assert.False(bindings.IsActionAvailable("assistants.project.refresh"));
    }

    private static AssistantsCuiBindings Bindings(bool actualRoutePresent) => new((_, _, _) => ValueTask.CompletedTask,
        action => action(), () => true, actualRoutePresent);
    private static AssistantsWorkspaceSnapshot WorkSnapshot()
    {
        var definition = new AssistantDefinitionSnapshot(new("fixture-den", "personal", "fixture-assistant"), 1,
            ConfiguredIdentityKind.Assistant, new() { Name = "Fixture Assistant" }, []);
        var context = new ProviderExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 1);
        return AssistantsWorkspaceSnapshot.Empty with { Revision = 1, SelectedAssistant = definition,
            Assistants = [definition], Work = new(null, new(context, false, false, false), []) };
    }
}
