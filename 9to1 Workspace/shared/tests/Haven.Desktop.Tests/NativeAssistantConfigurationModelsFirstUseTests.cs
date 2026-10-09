#if !ANDROID
using System.Runtime.ExceptionServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task Saved_Assistant_discovers_actual_empty_model_registry_without_creating_a_conversation_then_reopens_preferences() =>
        RunActualConfigurationModelRigAsync(async rig =>
    {
        AssistantIdentity? identity = null; long savedRevision = 0;
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            window.Width = 1320; window.Height = 960;
            await ClickCanonicalCaptureControl(window, surface, "assistant-create", () => surface.Bindings.Draft is not null);
            Assert.True(surface.Bindings.TryGetValue("ConfigurationModelStatus", out var setup));
            Assert.Contains("Save your Assistant first", Assert.IsType<string>(setup));
            Assert.False(surface.Bindings.IsActionAvailable("assistants.configuration.models.read"));
            var name = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), value =>
                AutomationProperties.GetName(value) == "Assistant name");
            name.Text = "Fictional saved model setup";
            await ClickCanonicalCaptureControl(window, surface, "assistant-save", () =>
                surface.Bindings.Draft?.IsDirty == false && surface.Bindings.IsActionAvailable("assistants.configuration.models.read") == true);
            var saved = controller.Snapshot.SelectedAssistant!;
            identity = saved.Identity; savedRevision = saved.Revision;
            Assert.Null(controller.Snapshot.ConversationBinding); Assert.Empty(controller.Snapshot.Conversations);
            Assert.True(surface.Bindings.TryGetValue("ConfigurationModels", out var rows));
            Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(rows));
            Assert.True(surface.Bindings.TryGetValue("ConfigurationModelStatus", out var status));
            Assert.Contains("No models are currently available", Assert.IsType<string>(status));
            var before = controller.Snapshot.Revision;
            await ClickCanonicalCaptureControl(window, surface, "configuration-refresh-models", () =>
                controller.Snapshot.Revision > before && surface.Bindings.IsActionAvailable("assistants.configuration.models.read") == true);
            Assert.Equal(savedRevision, controller.Snapshot.SelectedAssistant!.Revision);
            var actual = await controller.ReadOriginalConfigurationModelsAsync(Token);
            Assert.True(((IAssistantOriginalConfigurationModelCatalogueOwner)rig.Bridge).IsIssuedOriginalConfigurationModels(actual));
            Assert.Equal(identity, actual.Definition.Identity); Assert.Equal(savedRevision, actual.Definition.Revision);
            Assert.Equal(await rig.Profiles.GetCurrentAsync(Token), actual.Actor); Assert.Empty(actual.Models);
            Assert.Null(controller.Snapshot.ConversationBinding); Assert.Empty(controller.Snapshot.Conversations);
            Assert.Null(controller.Snapshot.Work);
            Assert.True(surface.Bindings.TrySetValue("DraftAllowCloud", true));
            Assert.False(surface.Bindings.IsActionAvailable("assistants.configuration.models.read"));
            Assert.True(surface.Bindings.TryGetValue("ConfigurationModelStatus", out status));
            Assert.Contains("Save your changes", Assert.IsType<string>(status));
            await ClickCanonicalCaptureControl(window, surface, "assistant-save", () =>
                surface.Bindings.Draft?.IsDirty == false && surface.Bindings.IsActionAvailable("assistants.configuration.models.read") == true);
            var updated = controller.Snapshot.SelectedAssistant!;
            Assert.Equal(identity, updated.Identity);
            Assert.True(updated.Configuration.Model.AllowCloud);
            Assert.Null(controller.Snapshot.ConversationBinding); Assert.Empty(controller.Snapshot.Conversations);
        });
        await rig.ReopenAsync();
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            window.Width = 1320; window.Height = 960;
            Assert.True(surface.Bindings.TryGetValue("Assistants", out var rows));
            var row = Assert.Single(Assert.IsType<HavenOS.Apps.Assistants.NativeUI.AssistantsCuiBindings.AssistantRow[]>(rows), value => value.Identity == identity);
            await surface.Bindings.DispatchAsync("assistants.open", row, Token);
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token);
            await FlushNativeMemoryUi(window);
            Assert.Equal(identity, surface.Bindings.Draft!.Identity);
            Assert.True(surface.Bindings.Draft.Configuration.Model.AllowCloud);
            Assert.Null(controller.Snapshot.ConversationBinding); Assert.Empty(controller.Snapshot.Conversations);
            Assert.True(surface.Bindings.TryGetValue("ConfigurationModelStatus", out var status));
            Assert.Contains("No models are currently available", Assert.IsType<string>(status));
        });
    });

    [Fact]
    public Task Saved_definition_revision_change_refuses_old_model_discovery_without_creating_a_conversation() =>
        RunActualConfigurationModelRigAsync(async rig =>
    {
        var definition = await rig.Bridge.CreateAsync(ConfiguredIdentityKind.Assistant,
            new AssistantConfiguration { Name = "Fictional revisioned model setup" }, Guid.NewGuid(), Token);
        var owner = (IAssistantOriginalConfigurationModelCatalogueOwner)rig.Bridge;
        var observed = await owner.ReadOriginalConfigurationModelsWithinSourceAsync(definition.Identity, definition.Revision, Scope, rig.Retain, Token);
        Assert.True(owner.IsIssuedOriginalConfigurationModels(observed)); Assert.Empty(observed.Models);
        var updated = await rig.Bridge.UpdateAsync(definition.Identity, definition.Revision,
            definition.Configuration with { Description = "Newer saved revision" }, Guid.NewGuid(), Token);
        var stale = owner.ReadOriginalConfigurationModelsWithinSourceAsync(definition.Identity, definition.Revision, Scope, rig.Retain, Token);
        rig.Retain(stale);
        await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => stale);
        var current = await owner.ReadOriginalConfigurationModelsWithinSourceAsync(updated.Identity, updated.Revision, Scope, rig.Retain, Token);
        Assert.Equal(updated.Revision, current.Definition.Revision);
        Assert.Equal("Newer saved revision", current.Definition.Configuration.Description);
        Assert.Empty(await rig.Bridge.ReadConversationsAsync(updated.Identity, token: Token));
        Assert.Empty(current.Models);
    });

    private static async Task RunActualConfigurationModelRigAsync(Func<Rig, Task> body)
    {
        Rig? fixture = null;
        var registries = new List<ModelProviderRegistry>();
        fixture = new Rig(configuredModelFactory: profiles =>
        {
            // The actual maintained registry has no providers. This exercises real
            // query/policy/actor custody without inventing a model or executing one.
            var paths = new Paths(Path.Combine(fixture!.Root, "model-preferences"));
            var privacy = new PrivacyPreferenceStore(paths);
            var registry = new ModelProviderRegistry(Array.Empty<IModelProvider>());
            registries.Add(registry); // Each reopened fixture owns this SAME configured cohort.
            var permissions = new ModelPermissionEvaluator(new VersionedModelPermissionStore(new VersionedAtomicSettingsStore(paths)));
            var routing = new ProviderRoutingModelClient(new NoModelCalls(), registry, privacy);
            return new AssistantOriginalModelSelectionOwner(registry, privacy, permissions, profiles, routing.ToCompatibilityDescriptor);
        });
        var failures = new List<Exception>(); var originals = new List<Task>();
        try { await fixture.InitializeAsync(true); await body(fixture); }
        catch (Exception cause) { failures.Add(cause); }
        Task? actualClose = null;
        try { actualClose = fixture.CloseAsync(); originals.Add(actualClose); await actualClose; }
        catch (Exception cause) { failures.Add(actualClose?.Exception ?? cause); }
        // Per-view bridges borrowed the registry. After their actual process
        // closes, independently join each SAME global source/observer cohort.
        foreach (var registry in registries)
        {
            Task? actualRegistryClose = null;
            try
            {
                actualRegistryClose = registry.CloseOriginalCataloguesAndDrainAsync();
                originals.Add(actualRegistryClose); await actualRegistryClose;
            }
            catch (Exception cause) { failures.Add(actualRegistryClose?.Exception ?? cause); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual configured-model fixture preserved at " + fixture.Root, failures);
    }
}
#endif
