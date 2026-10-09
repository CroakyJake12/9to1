using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

public sealed class AssistantsCuiModelSelectionTests
{
    [Fact]
    public void Missing_configured_primary_does_not_silently_select_another_provider_model()
    {
        var bindings = Bindings();
        bindings.ApplySnapshot(Snapshot(1, new("configured", "missing", AllowFallback: false),
            Model("other", "available")));
        Assert.Null(bindings.SelectedModel);
        Assert.True(bindings.TryGetValue("SelectedModelLabel", out var label));
        Assert.Contains("Configured model unavailable", Assert.IsType<string>(label));
    }

    [Fact]
    public void Bare_saved_name_resolves_to_the_actual_provider_qualified_choice()
    {
        var expected = Model("provider", "provider:model");
        var bindings = Bindings();
        bindings.ApplySnapshot(Snapshot(1, new("provider", "model"), Model("other", "model"), expected));
        Assert.Same(expected, bindings.SelectedModel);
    }

    [Fact]
    public void Metadata_refresh_keeps_explicit_selection_and_uses_the_fresh_owner_descriptor()
    {
        var first = Model("provider", "provider:other", DateTimeOffset.UnixEpoch);
        var fresh = Model("provider", "provider:other", DateTimeOffset.UnixEpoch.AddDays(1));
        var primary = Model("provider", "provider:primary");
        var bindings = Bindings();
        bindings.ApplySnapshot(Snapshot(1, new("provider", "primary"), primary, first));
        bindings.SelectModel(first);
        bindings.ApplySnapshot(Snapshot(2, new("provider", "primary"), primary, fresh));
        Assert.Same(fresh, bindings.SelectedModel);
        Assert.NotSame(first.Model, bindings.SelectedModel!.Model);
    }

    [Fact]
    public void A_changed_saved_preference_replaces_the_previous_presentation_selection()
    {
        var old = Model("provider", "provider:old");
        var current = Model("provider", "provider:current");
        var bindings = Bindings();
        bindings.ApplySnapshot(Snapshot(1, new("provider", "old"), old, current));
        bindings.ApplySnapshot(Snapshot(2, new("provider", "current"), old, current));
        Assert.Same(current, bindings.SelectedModel);
    }

    [Fact]
    public void Default_selection_is_allowed_only_without_a_saved_model_preference()
    {
        var first = Model("provider", "provider:first");
        var bindings = Bindings();
        bindings.ApplySnapshot(Snapshot(1, new(), first));
        Assert.Same(first, bindings.SelectedModel);
        bindings.ApplySnapshot(Snapshot(2, new("unavailable-provider"), first));
        Assert.Null(bindings.SelectedModel);
    }

    [Fact]
    public void Revocation_hides_private_model_and_saved_configuration_values()
    {
        var bindings = Bindings();
        bindings.ApplySnapshot(Snapshot(1, new(), Model("provider", "model")));
        bindings.Revoke();
        Assert.Null(bindings.SelectedModel);
        Assert.Same(AssistantsWorkspaceSnapshot.Empty, bindings.Snapshot);
        Assert.True(bindings.TryGetValue("Models", out var models));
        Assert.Null(models);
        Assert.False(bindings.TrySetValue("Prompt", "private input"));
        Assert.False(bindings.IsActionAvailable("assistants.model.choose"));
    }

    private static AssistantsCuiBindings Bindings() => new((_, _, _) => ValueTask.CompletedTask,
        action => action(), () => true);
    private static AssistantModelChoice Model(string provider, string name, DateTimeOffset? modified = null) =>
        new(new ModelDescriptor(name, 1, "family", "parameters", "quantization", new HashSet<ToolCapability>(),
            modified ?? DateTimeOffset.UnixEpoch), provider);
    private static AssistantsWorkspaceSnapshot Snapshot(long revision, AssistantModelPreferences preference,
        params AssistantModelChoice[] models)
    {
        var definition = new AssistantDefinitionSnapshot(new("den", "personal", "assistant"), revision,
            ConfiguredIdentityKind.Assistant, new AssistantConfiguration { Name = "Assistant", Model = preference }, []);
        return AssistantsWorkspaceSnapshot.Empty with
        { Revision = revision, Assistants = [definition], SelectedAssistant = definition, Models = models };
    }
}
