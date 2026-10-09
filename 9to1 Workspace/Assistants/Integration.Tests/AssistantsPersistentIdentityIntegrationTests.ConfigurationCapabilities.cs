using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace HavenOS.Apps.Assistants.Tests;

// Actual persisted Den + protected initialized SQLite + manual Home import.
// These metadata/configuration controls create no conversation, Task or execution.
public sealed partial class AssistantsPersistentIdentityIntegrationTests
{
    public sealed class LinuxConfigurationCapabilityFactAttribute : FactAttribute
    {
        public LinuxConfigurationCapabilityFactAttribute()
        { if (!OperatingSystem.IsLinux()) Skip = "Requires the actual Linux private kernel store boundary; Windows remains separately unvalidated."; }
    }

    [LinuxConfigurationCapabilityFact]
    public Task Actual_pre_conversation_capability_choice_survives_definition_owner_reopen() =>
        RunOriginalAsync(async rig =>
        {
            await rig.SeedActualCapabilityFixtureAsync(); await rig.ImportActualCapabilityStoreAsync();
            var controller = rig.CreateController();
            var created = await rig.Retain(controller.CreateAsync(new() { Name = "Configured tool helper" }, Guid.NewGuid(), Token));
            var saved = created.SelectedAssistant!; Assert.Empty(created.Conversations); Assert.Null(created.ConversationBinding);
            var catalogue = await rig.Retain(controller.ReadOriginalConfigurationCapabilitiesAsync(Token));
            Assert.Equal(saved.Identity, catalogue.Definition.Identity); Assert.Equal(saved.Revision, catalogue.Definition.Revision);
            Assert.Equal(CapabilityOriginalCatalogueState.Available, catalogue.State);
            var read = Assert.Single(catalogue.Definitions, value => value.Key == "read-file");
            var choice = await rig.Retain(controller.SelectOriginalConfigurationCapabilityAsync(catalogue, read.Id, Token));
            Assert.Equal(read.Id.ToString("D"), choice.PreferenceId); Assert.Same(catalogue, choice.Catalogue);
            await rig.Retain(controller.RevalidateOriginalConfigurationChoiceAsync(choice, Token));
            var configured = await rig.Retain(controller.ConfigureAsync(saved.Identity, saved.Revision,
                saved.Configuration with { ToolIds = [choice.PreferenceId] }, Guid.NewGuid(), Token));
            var revision = configured.SelectedAssistant!.Revision;
            Assert.Empty(configured.Conversations); Assert.Null(configured.ConversationBinding); Assert.Equal(0, rig.ProviderInvocations);
            await rig.ReopenActualOwnersAsync();
            var reopened = rig.CreateController(); var current = await rig.Retain(reopened.OpenAssistantAsync(saved.Identity, Token));
            Assert.Equal(revision, current.SelectedAssistant!.Revision);
            Assert.Equal(choice.PreferenceId, Assert.Single(current.SelectedAssistant.Configuration.ToolIds));
            Assert.Empty(current.Conversations); Assert.Null(current.ConversationBinding); Assert.Equal(0, rig.ProviderInvocations);
            var refreshed = await rig.Retain(reopened.ReadOriginalConfigurationCapabilitiesAsync(Token));
            Assert.Contains(refreshed.Definitions, value => value.Id == read.Id);
        }, withCapabilities: true);

    [LinuxConfigurationCapabilityFact]
    public Task Missing_actual_canonical_import_is_setup_before_capability_metadata_and_can_be_completed() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            await rig.Retain(controller.CreateAsync(new() { Name = "Actual setup helper" }, Guid.NewGuid(), Token));
            var setup = await rig.Retain(controller.ReadOriginalConfigurationCapabilitiesAsync(Token));
            Assert.Equal(CapabilityOriginalCatalogueState.SetupRequired, setup.State); Assert.Empty(setup.Definitions);
            Assert.Empty(controller.Snapshot.Conversations); Assert.Equal(0, rig.ProviderInvocations);
            await rig.SeedActualCapabilityFixtureAsync(); await rig.ImportActualCapabilityStoreAsync();
            var configured = await rig.Retain(controller.ReadOriginalConfigurationCapabilitiesAsync(Token));
            Assert.Equal(CapabilityOriginalCatalogueState.Available, configured.State); Assert.NotEmpty(configured.Definitions);
            Assert.Equal(setup.Definition.Identity, configured.Definition.Identity); Assert.Empty(controller.Snapshot.Conversations);
            await rig.Retain(controller.CloseAndDrainAsync());
        }, withCapabilities: true);

    [LinuxConfigurationCapabilityFact]
    public Task Same_issued_stale_definition_catalogue_is_acknowledged_before_any_selection_dispatch() =>
        RunOriginalAsync(async rig =>
        {
            await rig.SeedActualCapabilityFixtureAsync(); await rig.ImportActualCapabilityStoreAsync();
            var controller = rig.CreateController();
            var created = await rig.Retain(controller.CreateAsync(new() { Name = "Actual stale choice" }, Guid.NewGuid(), Token));
            var stale = await rig.Retain(controller.ReadOriginalConfigurationCapabilitiesAsync(Token));
            var row = Assert.Single(stale.Definitions, value => value.Key == "read-file"); var saved = created.SelectedAssistant!;
            var current = await rig.Retain(controller.ConfigureAsync(saved.Identity, saved.Revision,
                saved.Configuration with { Instructions = "Saved newer revision" }, Guid.NewGuid(), Token));
            var actual = rig.Retain(controller.SelectOriginalConfigurationCapabilityAsync(stale, row.Id, Token));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => actual);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(actual)); rig.Expect(actual, refusal);
            Assert.Equal(current.SelectedAssistant!.Revision, controller.Snapshot.SelectedAssistant!.Revision);
            Assert.Empty(controller.Snapshot.SelectedAssistant.Configuration.ToolIds); Assert.Empty(controller.Snapshot.Conversations);
            var fresh = await rig.Retain(controller.ReadOriginalConfigurationCapabilitiesAsync(Token));
            Assert.Equal(current.SelectedAssistant.Revision, fresh.Definition.Revision);
            Assert.Equal(row.Id, (await rig.Retain(controller.SelectOriginalConfigurationCapabilityAsync(fresh, row.Id, Token))).Selected.Id);
            await rig.Retain(controller.CloseAndDrainAsync()); Assert.Equal(0, rig.ProviderInvocations);
        }, withCapabilities: true);

    [LinuxConfigurationCapabilityFact]
    public Task Changed_actual_capability_row_declines_its_old_choice_and_refreshes_without_saved_config_effect() =>
        RunOriginalAsync(async rig =>
        {
            await rig.SeedActualCapabilityFixtureAsync(); await rig.ImportActualCapabilityStoreAsync();
            var controller = rig.CreateController();
            await rig.Retain(controller.CreateAsync(new() { Name = "Actual removed capability" }, Guid.NewGuid(), Token));
            var original = await rig.Retain(controller.ReadOriginalConfigurationCapabilitiesAsync(Token));
            var row = Assert.Single(original.Definitions, value => value.Key == "read-file");
            await rig.Retain(rig.CapabilityRepository.SetCapabilityEnabledAsync(row.Id, false, Token));
            var actual = rig.Retain(controller.SelectOriginalConfigurationCapabilityAsync(original, row.Id, Token));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => actual);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(actual)); rig.Expect(actual, refusal);
            Assert.Empty(controller.Snapshot.SelectedAssistant!.Configuration.ToolIds); Assert.Empty(controller.Snapshot.Conversations);
            var fresh = await rig.Retain(controller.ReadOriginalConfigurationCapabilitiesAsync(Token));
            Assert.DoesNotContain(fresh.Definitions, value => value.Id == row.Id);
            await rig.Retain(controller.CloseAndDrainAsync()); Assert.Equal(0, rig.ProviderInvocations);
        }, withCapabilities: true);
}
