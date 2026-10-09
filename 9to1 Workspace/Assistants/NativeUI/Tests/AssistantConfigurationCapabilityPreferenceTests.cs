using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

// Form/metadata correlation controls only. Fixture-issued catalogue rows do not
// qualify the protected Home READ owner or real Task capability execution.
public sealed class AssistantConfigurationCapabilityPreferenceTests
{
    [Fact]
    public void One_observed_connected_capability_saves_only_its_exact_preference_and_preserves_other_draft_fields()
    {
        var definition = Definition(); var draft = new AssistantConfigurationDraft(definition);
        Assert.True(draft.TrySetText("DraftPurpose", "Unsaved user purpose"));
        var submitted = draft.CaptureSubmission(); var state = new AssistantConfigurationCapabilityPreferences();
        var first = CapabilityRegistryCatalog.BuiltIns.Single(row => row.Key == "web-search") with
        { Id = Guid.NewGuid(), Key = "connection.first", IsBuiltIn = false, ProviderId = "haven.connections" };
        var second = first with { Id = Guid.NewGuid(), Key = "connection.second" };
        var catalogue = Catalogue(definition, [first, second]); state.Begin(submitted);
        Assert.True(state.PublishCatalogue(draft, submitted, catalogue));
        var row = state.Rows[0]; Assert.Same(first, state.FindCurrentRow(draft, row)!.Original);
        var choice = new AssistantOriginalConfigurationCapabilityChoice(new object(), new object(), catalogue, first);
        Assert.True(state.ApplyChoice(draft, submitted, choice));
        Assert.Equal(first.Id.ToString("D"), Assert.Single(draft.Configuration.ConnectedAppIds));
        Assert.DoesNotContain("haven.connections", draft.Configuration.ConnectedAppIds);
        Assert.DoesNotContain(second.Id.ToString("D"), draft.Configuration.ConnectedAppIds);
        Assert.Empty(draft.Configuration.ToolIds); Assert.Equal("Unsaved user purpose", draft.Configuration.Purpose);
        Assert.True(draft.IsDirty); Assert.Empty(definition.Configuration.ConnectedAppIds);
        var save = draft.CaptureSubmission(); draft.Acknowledge(save, definition with
        { Revision = definition.Revision + 1, Configuration = save.Configuration });
        Assert.False(draft.IsDirty); Assert.Equal(first.Id.ToString("D"), Assert.Single(draft.Configuration.ConnectedAppIds));
    }

    [Fact]
    public void Delayed_catalogue_or_choice_cannot_apply_to_a_newer_or_replaced_form_or_another_catalogue()
    {
        var definition = Definition(); var draft = new AssistantConfigurationDraft(definition);
        var submitted = draft.CaptureSubmission(); var state = new AssistantConfigurationCapabilityPreferences();
        var tool = CapabilityRegistryCatalog.BuiltIns.Single(row => row.Key == "read-file");
        var catalogue = Catalogue(definition, [tool]); state.Begin(submitted);
        Assert.True(state.PublishCatalogue(draft, submitted, catalogue));
        var oldRow = state.Rows[0];
        var foreignChoice = new AssistantOriginalConfigurationCapabilityChoice(new object(), new object(), Catalogue(definition, [tool]), tool);
        Assert.False(state.ApplyChoice(draft, submitted, foreignChoice)); Assert.Empty(draft.Configuration.ToolIds);
        Assert.True(draft.TrySetText("DraftInstructions", "Newer instructions"));
        Assert.False(state.PublishCatalogue(draft, submitted, catalogue)); Assert.Null(state.FindCurrentRow(draft, oldRow));
        var actualChoice = new AssistantOriginalConfigurationCapabilityChoice(new object(), new object(), catalogue, tool);
        Assert.False(state.ApplyChoice(draft, submitted, actualChoice));
        Assert.Empty(draft.Configuration.ToolIds); Assert.Equal("Newer instructions", draft.Configuration.Instructions);
        var replacement = new AssistantConfigurationDraft(definition);
        Assert.False(state.PublishCatalogue(replacement, submitted, catalogue));
        Assert.False(state.ApplyChoice(replacement, submitted, actualChoice)); Assert.Empty(replacement.Configuration.ToolIds);
    }

    [Fact]
    public void Setup_only_catalogue_and_unsupported_rows_cannot_be_selected_as_ready_preferences()
    {
        var definition = Definition(); var draft = new AssistantConfigurationDraft(definition);
        var submitted = draft.CaptureSubmission(); var state = new AssistantConfigurationCapabilityPreferences();
        state.Begin(submitted);
        Assert.True(state.PublishCatalogue(draft, submitted, Catalogue(definition, [], CapabilityOriginalCatalogueState.SetupRequired)));
        Assert.Empty(state.Rows); Assert.False(state.CanSelect(draft));
        var unsupported = CapabilityRegistryCatalog.BuiltIns[0] with { Availability = CapabilityAvailability.Unsupported };
        state.Begin(submitted); Assert.True(state.PublishCatalogue(draft, submitted, Catalogue(definition, [unsupported])));
        var row = Assert.Single(state.Rows); Assert.False(row.CanChoose); Assert.Null(state.FindCurrentRow(draft, row));
        Assert.False(state.CanSelect(draft)); Assert.False(draft.IsDirty);
    }

    private static AssistantDefinitionSnapshot Definition() => new(new("fixture-den", "personal", "fixture-assistant"),
        1, ConfiguredIdentityKind.Assistant, new() { Name = "Fixture Assistant" }, []);
    private static AssistantOriginalConfigurationCapabilityCatalogue Catalogue(AssistantDefinitionSnapshot definition,
        IReadOnlyList<CapabilityDefinition> rows, CapabilityOriginalCatalogueState state = CapabilityOriginalCatalogueState.Available) =>
        new(new object(), new object(), definition, new("fixture-actor", "fixture-profile", null, null, "fixture-auth"), state,
            state == CapabilityOriginalCatalogueState.Available ? "Observed fixture metadata" : "Setup required", rows);
}
