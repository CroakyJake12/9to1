using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

// Local metadata/draft controls only. This test constructor cannot issue the
// canonical bridge's private registry proof or authorize an actual provider.
public sealed class AssistantsConfigurationModelPreferenceTests
{
    [Fact]
    public async Task Only_current_original_model_row_can_dispatch_and_edit_the_preference()
    {
        var calls = new List<object?>();
        var bindings = new AssistantsCuiBindings((_, value, _) => { calls.Add(value); return ValueTask.CompletedTask; }, action => action(), () => true);
        var (submitted, catalogue, row) = Publish(bindings);
        var clone = new AssistantsCuiBindings.ConfigurationModelRow(catalogue, catalogue.Models[0], row.Id);
        Assert.True(bindings.TryGetItemValue(row, "Label", out var label)); Assert.Equal(row.Label, label);
        Assert.False(bindings.TryGetItemValue(clone, "Label", out _));
        await bindings.DispatchAsync("assistants.configuration.model.choose", clone, CancellationToken.None);
        Assert.Empty(calls);
        await bindings.DispatchAsync("assistants.configuration.model.choose", row, CancellationToken.None);
        Assert.Same(row, Assert.Single(calls));
        bindings.ApplyConfigurationModel(submitted, row);
        var draft = bindings.Draft!;
        Assert.True(draft.IsDirty); Assert.Equal("fixture-provider", draft.Configuration.Model.ProviderId);
        Assert.Equal("fixture-provider:observed", draft.Configuration.Model.ModelId);
        Assert.Equal("Preserved instructions", draft.Configuration.Instructions);
        Assert.False(draft.Configuration.Model.AllowCloud);
        Assert.False(bindings.TryGetItemValue(row, "Label", out _));
        Assert.Equal(draft.Configuration.Model, draft.CaptureSubmission().Configuration.Model);
    }

    [Fact]
    public async Task Draft_edit_or_revocation_invalidates_previous_catalogue_rows_before_dispatch()
    {
        var calls = 0;
        var bindings = new AssistantsCuiBindings((_, _, _) => { calls++; return ValueTask.CompletedTask; }, action => action(), () => true);
        var (submitted, _, row) = Publish(bindings);
        Assert.True(bindings.TrySetValue("DraftName", "Newer form"));
        Assert.False(bindings.TryGetItemValue(row, "Label", out _));
        await bindings.DispatchAsync("assistants.configuration.model.choose", row, CancellationToken.None);
        bindings.ApplyConfigurationModel(submitted, row);
        Assert.Equal(0, calls); Assert.Null(bindings.Draft!.Configuration.Model.ModelId);
        bindings.Revoke();
        Assert.False(bindings.TryGetItemValue(row, "Id", out _));
        await bindings.DispatchAsync("assistants.configuration.model.choose", row, CancellationToken.None);
        Assert.Equal(0, calls);
    }

    private static (AssistantConfigurationDraft.Submission, AssistantOriginalConfigurationModelCatalogue,
        AssistantsCuiBindings.ConfigurationModelRow) Publish(AssistantsCuiBindings bindings)
    {
        var definition = new AssistantDefinitionSnapshot(new("fixture-den", "personal", "definition"), 1,
            ConfiguredIdentityKind.Assistant, new() { Name = "Fixture", Instructions = "Preserved instructions" }, []);
        bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with { Revision = 1, SelectedAssistant = definition, Assistants = [definition] });
        bindings.EditConfiguration(definition); bindings.SetConfigurationModelSource(true);
        var submitted = bindings.Draft!.CaptureSubmission();
        var model = new AssistantModelChoice(new("fixture-provider:observed", 1, "fixture", "fixture", "fixture",
            new HashSet<ToolCapability>(), DateTimeOffset.UnixEpoch), "fixture-provider");
        var catalogue = new AssistantOriginalConfigurationModelCatalogue(new object(), definition,
            new AuthenticatedResourceActor("fixture-actor", "fixture-profile", null, null, "fixture-revision"),
            Array.AsReadOnly(new[] { model }), "Metadata-only native binding fixture.");
        bindings.BeginConfigurationModelRead(submitted); bindings.PublishConfigurationModels(submitted, catalogue);
        Assert.True(bindings.TryGetValue("ConfigurationModels", out var rows));
        return (submitted, catalogue, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AssistantsCuiBindings.ConfigurationModelRow>>(rows)));
    }
}
