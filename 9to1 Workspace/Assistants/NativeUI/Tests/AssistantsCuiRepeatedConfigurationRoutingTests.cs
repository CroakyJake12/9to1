using System.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

// Actual authored CUI/loader/bindings projection and routing controls. Fixtures
// below issue display metadata only; no installed/Home/project/Task grant is tested.
[Collection("Assistants native UI")]
public sealed class AssistantsCuiRepeatedConfigurationRoutingTests
{
    [Fact]
    public async Task Authored_configuration_renders_typed_tool_connected_project_and_effort_rows_and_routes_the_same_second_row()
    {
        using var lifetime = new CancellationTokenSource();
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var calls = new List<(string Command, object? Parameter)>();
            var bindings = Bindings(calls); var definition = Definition();
            bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
            { Revision = 1, SelectedAssistant = definition, Assistants = [definition] });
            bindings.EditConfiguration(definition);
            var submitted = bindings.OriginalDraft!.CaptureSubmission();
            bindings.BeginConfigurationCapabilityReview(submitted);
            var tool = CapabilityRegistryCatalog.BuiltIns.Single(row => row.Key == "read-file");
            var connection = tool with { Id = Guid.NewGuid(), Key = "fixture.connection", Name = "Observed connected capability", IsBuiltIn = false };
            var catalogue = new AssistantOriginalConfigurationCapabilityCatalogue(new object(), new object(), definition,
                new("fixture-actor", "fixture-profile", null, null, "fixture-auth"), CapabilityOriginalCatalogueState.Available,
                "Observed fixture metadata", [tool, connection]);
            Assert.True(bindings.PublishConfigurationCapabilityCatalogue(submitted, catalogue));
            using var loader = new CuiControlLoader(AssistantsNativeCuiSurface.CreateAvatarRegistry()); loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var window = new Window { Width = 1000, Height = 800 };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Configuration));
                Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
                window.Content = root; window.Show(); window.UpdateLayout();
                var controls = root.GetVisualDescendants().OfType<Control>().ToArray();
                Assert.Contains(controls.OfType<TextBlock>(), text => text.Text == tool.Name);
                Assert.Contains(controls.OfType<TextBlock>(), text => text.Text == connection.Name);
                Assert.Contains(controls.OfType<TextBlock>(), text => text.Text == "Saved development project 1");
                var choices = controls.OfType<Button>().Where(button => button.Tag as string == "ChooseCapabilityPreference").ToArray();
                Assert.Equal(2, choices.Length);
                Assert.Equal(choices.Length, choices.Select(CuiRuntimeIdentity.GetStableId).Distinct().Count());
                Assert.All(choices, button => Assert.False(string.IsNullOrWhiteSpace(CuiRuntimeIdentity.GetStableId(button))));
                var rows = Rows(bindings, "ConfigurationCapabilityRows");
                Assert.Equal(2, rows.Length); Assert.True(choices[1].IsEnabled);
                choices[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                var selected = Assert.Single(calls);
                Assert.Equal("assistants.configuration.capability.choose", selected.Command); Assert.Same(rows[1], selected.Parameter);
                Assert.Same(rows[1], bindings.FindConfigurationCapabilityRow(selected.Parameter));

                var projects = Rows(bindings, "ConfiguredDevelopmentProjects");
                var review = Assert.Single(controls.OfType<Button>(), button => button.Tag as string == "ReviewConfiguredProject");
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Equal("assistants.configuration.project.review", calls[1].Command); Assert.Same(Assert.Single(projects), calls[1].Parameter);
                var efforts = controls.OfType<Button>().Where(button => button.Tag as string == "ChooseEffort").ToArray();
                Assert.NotEmpty(efforts); Assert.All(efforts, button => Assert.False(string.IsNullOrWhiteSpace(button.Content as string)));
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, lifetime.Token);
        Assert.True(completed);
    }

    [Fact]
    public async Task Authored_project_chooser_reuses_display_key_but_updated_button_routes_only_the_same_current_row()
    {
        using var lifetime = new CancellationTokenSource();
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var calls = new List<(string Command, object? Parameter)>(); var bindings = Bindings(calls);
            var definition = Definition(); bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
            { Revision = 1, SelectedAssistant = definition, Assistants = [definition] });
            bindings.BeginProjectSelection(false); bindings.SetProjectCatalogue(Page("First observed project"));
            var old = Assert.Single(Rows(bindings, "ProjectChoices"));
            var registry = AssistantsNativeCuiSurface.CreateAvatarRegistry(); registry.RegisterObjectRenderer("AssistantConversation", _ => new Panel());
            using var loader = new CuiControlLoader(registry); loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var window = new Window { Width = 1000, Height = 800 };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Work));
                Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
                window.Content = root; window.Show(); window.UpdateLayout();
                var firstButton = Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Tag as string == "ChooseOriginalProject");
                var originalId = CuiRuntimeIdentity.GetStableId(firstButton); Assert.Equal("First observed project", firstButton.Content);
                var currentPage = Page("Fresh current project"); bindings.SetProjectCatalogue(currentPage);
                var currentRow = Assert.Single(Rows(bindings, "ProjectChoices")); Assert.NotSame(old, currentRow);
                var currentButton = Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Tag as string == "ChooseOriginalProject");
                Assert.Same(firstButton, currentButton); Assert.Equal(originalId, CuiRuntimeIdentity.GetStableId(currentButton));
                Assert.Equal("Fresh current project", currentButton.Content);
                currentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                var actual = Assert.Single(calls); Assert.Equal("assistants.project.choose", actual.Command); Assert.Same(currentRow, actual.Parameter);
                Assert.Same(currentPage.Candidates[0], bindings.DemandOriginalProjectCandidate(actual.Parameter));
                Assert.False(bindings.TryGetItemValue(old, "Title", out _));
                await bindings.DispatchAsync("assistants.project.choose", old, lifetime.Token);
                Assert.Single(calls); // The stale object is refused before acquiring a product command.
                bindings.Revoke(); Assert.False(bindings.TryGetItemValue(currentRow, "Id", out _));
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, lifetime.Token);
        Assert.True(completed);
    }

    [Fact]
    public void Typed_row_port_rejects_equal_foreign_rows_nested_source_fields_writes_and_newer_form_rows()
    {
        var bindings = Bindings([]); var definition = Definition(); bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
        { Revision = 1, SelectedAssistant = definition, Assistants = [definition] }); bindings.EditConfiguration(definition);
        var row = Assert.IsType<AssistantsCuiBindings.AssistantRow>(Assert.Single(Rows(bindings, "Assistants")));
        Assert.True(bindings.TryGetItemValue(row, "Name", out var name)); Assert.Equal(definition.Configuration.Name, name);
        Assert.False(bindings.TryGetItemValue(row with { }, "Name", out _));
        Assert.False(bindings.TryGetItemValue(row, "Identity.DefinitionId", out _));
        Assert.False(bindings.TrySetItemValue(row, "Name", "Injected"));
        var submitted = bindings.OriginalDraft!.CaptureSubmission(); bindings.BeginConfigurationCapabilityReview(submitted);
        var capability = CapabilityRegistryCatalog.BuiltIns[0]; var catalogue = new AssistantOriginalConfigurationCapabilityCatalogue(
            new object(), new object(), definition, new("fixture-actor", "fixture-profile", null, null, "fixture-auth"),
            CapabilityOriginalCatalogueState.Available, "Fixture", [capability]);
        Assert.True(bindings.PublishConfigurationCapabilityCatalogue(submitted, catalogue));
        var old = Assert.Single(Rows(bindings, "ConfigurationCapabilityRows")); Assert.True(bindings.TryGetItemValue(old, "Id", out _));
        Assert.True(bindings.TrySetValue("DraftMemoryEnabled", true));
        Assert.Empty(Rows(bindings, "ConfigurationCapabilityRows")); Assert.False(bindings.TryGetItemValue(old, "Id", out _));
    }

    private static AssistantsCuiBindings Bindings(List<(string Command, object? Parameter)> calls) =>
        new((command, parameter, _) => { calls.Add((command, parameter)); return ValueTask.CompletedTask; }, body => body(), () => true);
    private static object[] Rows(AssistantsCuiBindings bindings, string key)
    { Assert.True(bindings.TryGetValue(key, out var actual)); return Assert.IsAssignableFrom<IEnumerable>(actual).Cast<object>().ToArray(); }
    private static AssistantDefinitionSnapshot Definition() => new(new("fixture-den", "personal", "fixture-assistant"),
        1, ConfiguredIdentityKind.Assistant, new() { Name = "Rendered fixture", ProjectReferences =
            [new DeveloperProjectReference(Guid.NewGuid(), 1, Guid.NewGuid(), 1, Guid.NewGuid())] }, []);
    private static AssistantOriginalProjectCatalogue Page(string title) => new(
        [new AssistantOriginalProjectCandidate(new object(), new object(), title,
            new DeveloperProjectReference(Guid.NewGuid(), 1, Guid.NewGuid(), 1, Guid.NewGuid()))], false, "Fixture source rows");
}
