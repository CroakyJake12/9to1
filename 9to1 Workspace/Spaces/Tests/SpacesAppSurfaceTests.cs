using Haven.Application;
using Haven.Core;
using CakeOS.Cui;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpacesAppSurfaceTests
{
    [Fact]
    public void Built_in_descriptors_have_independent_stable_scopes()
    {
        // "Assistants" replaces the older "Agent" destination label per the 13 October 2026
        // Spaces companion (SPUI-154-159, OWNER-CONFIRMED), which makes Assistants a Space
        // with a permanent Main conversation per Assistant, Teams and owned Specialist
        // launch. The registered SpaceID and SpaceKind deliberately stay Agent-nominated:
        // SpaceID must survive a rename and SpaceKind numeric values must never be
        // renumbered. This expectation was corrected to the effective source; the previous
        // literal "Agent" label asserted superseded placement guidance.
        Assert.Equal(
            ["Chat", "Study", "Tasks", "Assistants", "Shopping", "Research", "Translate", "Experiences"],
            SpacesModel.BuiltIns.Select(definition => definition.Label));
        Assert.Equal(8, SpacesModel.BuiltIns.Select(definition => definition.Scope.Key).Distinct().Count());
        Assert.Equal(SpaceRegistry.ChatSpaceId, SpacesModel.BuiltIns[0].Scope.RegisteredSpaceId);
        Assert.Equal(SpaceRegistry.TasksSpaceId, SpacesModel.BuiltIns[2].Scope.RegisteredSpaceId);
        Assert.NotEqual(SpaceRegistry.AgentSpaceId, SpaceRegistry.TasksSpaceId);
    }

    [Fact]
    public async Task Sidebar_includes_built_ins_and_independently_scoped_custom_spaces()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var model = new SpacesModel(registry);

        var custom = await model.CreateCustomSpaceAsync("Recipes", "Keep dinner plans together.");
        var sidebar = await model.GetSidebarDestinationsAsync();

        Assert.Equal(
            ["Chat", "Study", "Tasks", "Assistants", "Shopping", "Research", "Translate", "Experiences", "Recipes"],
            sidebar.Select(item => item.Label));
        var recipes = Assert.Single(sidebar, item => item.Label == "Recipes");
        Assert.Equal(SpacesDestinationKind.Custom, recipes.Destination);
        Assert.Equal(custom.Scope, recipes.Scope);
        Assert.Equal(custom.Scope.RegisteredSpaceId, recipes.Scope.RegisteredSpaceId);
    }

    [Fact]
    public void Spaces_home_surface_exposes_accessible_built_in_and_custom_space_actions()
    {
        var document = SpacesHomeCuiDocument.Load();
        var components = document.Components.SelectMany(component => component.DescendantsAndSelf()).ToArray();
        var actions = components
            .Where(component => component.TryGetLiteralAttribute("action", out _))
            .Select(component =>
            {
                component.TryGetLiteralAttribute("action", out var action);
                // Resolve canonical CUI action references; buttons may use a named action
                // whose command and selected-ID parameter belong to document.Actions.
                return action is not null && document.Actions.TryGetValue(action, out var definition)
                    ? definition.Command : action;
            })
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("spaces.open", actions);
        Assert.Contains("spaces.create", actions);
        Assert.Contains("spaces.rename", actions);
        Assert.Contains("spaces.move", actions);
        Assert.Contains("spaces.fork", actions);
        Assert.Contains("spaces.archive", actions);
        Assert.Contains("spaces.edit-details", actions);
        Assert.Contains("spaces.edit-graph", actions);
        Assert.Contains("spaces.edit-extensions", actions);
        Assert.Contains(components, component => component.Name == "custom-spaces");
        Assert.Contains(components, component => component.Name == "space-context-items");
    }

    [Fact]
    public async Task Sidebar_exposes_custom_hierarchy_and_stable_sibling_positions()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var model = new SpacesModel(registry);
        var parent = await model.CreateCustomSpaceAsync("Projects");
        var first = await model.CreateCustomSpaceAsync("Alpha", null, parent.Scope.RegisteredSpaceId);
        var second = await model.CreateCustomSpaceAsync("Beta", null, parent.Scope.RegisteredSpaceId);
        await registry.MoveAsync(second.Scope.RegisteredSpaceId!.Value, null);

        var sidebar = await model.GetSidebarDestinationsAsync();
        var alpha = Assert.Single(sidebar, item => item.Label == "Alpha");
        var beta = Assert.Single(sidebar, item => item.Label == "Beta");
        Assert.Equal(parent.Scope.RegisteredSpaceId, alpha.ParentSpaceId);
        Assert.Equal(0, alpha.Position);
        Assert.Null(beta.ParentSpaceId);
        Assert.Equal(1, beta.Position);
        Assert.Equal(1, first.Revision);
    }

    [Fact]
    public async Task Typed_open_actions_delegate_to_existing_app_surfaces()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var model = new SpacesModel(registry);
        var host = new RecordingHost();
        var actions = new SpacesNavigationActionHost(host);

        await actions.ExecuteAsync(await model.CreateOpenActionAsync(SpaceScope.Chat));
        Assert.Equal(HavenMode.Chat, host.Mode);
        Assert.Null(host.Space);

        await actions.ExecuteAsync(await model.CreateOpenActionAsync(SpaceScope.Study));
        Assert.Equal(SpaceRegistry.StudySpaceId, host.Space!.Id);

        var custom = await model.CreateCustomSpaceAsync("Writing");
        await actions.ExecuteAsync(await model.CreateOpenActionAsync(custom.Scope));
        Assert.Equal(custom.Scope.RegisteredSpaceId, host.Space!.Id);
    }

    [Fact]
    public async Task Open_actions_reject_a_scope_that_does_not_match_its_registered_space()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var model = new SpacesModel(registry);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => model.CreateOpenActionAsync(SpaceScope.ForCustom(SpaceRegistry.StudySpaceId)));

        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_open_action_respects_cancellation_before_returning_an_action()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var model = new SpacesModel(registry);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => model.CreateOpenActionAsync(SpaceScope.Chat, cancellation.Token));
    }

    [Fact]
    public void Navigation_exposes_the_bounded_Home_Chat_Study_Tasks_Research_order()
    {
        Assert.Equal(
            [
                SpacesDestination.Home,
                SpacesDestination.Chat,
                SpacesDestination.Study,
                SpacesDestination.Tasks,
                SpacesDestination.Agent,
                SpacesDestination.Shopping,
                SpacesDestination.Research,
                SpacesDestination.Translate,
                SpacesDestination.Experiences
            ],
            SpacesAppSurface.Navigation.Select(item => item.Destination));
    }

    [Fact]
    public async Task Home_navigation_stays_in_the_app_and_preserves_existing_space_scope()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ResearchSpaceId);
        var host = new RecordingHost();
        var surface = new SpacesAppSurface(registry, host);

        await surface.NavigateAsync(SpacesDestination.Home);

        Assert.True(host.HomeOpened);
        Assert.Null(host.Mode);
        Assert.Null(host.Space);
        Assert.Equal(SpaceRegistry.ResearchSpaceId, await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(SpacesDestination.Home, surface.CurrentDestination);
    }

    [Fact]
    public async Task Chat_navigation_opens_existing_chat_mode_and_clears_space_scope()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.StudySpaceId);
        var host = new RecordingHost();
        var surface = new SpacesAppSurface(registry, host);

        await surface.NavigateAsync(SpacesDestination.Chat);

        Assert.Equal(HavenMode.Chat, host.Mode);
        Assert.Null(host.Space);
        Assert.Equal(SpaceRegistry.ChatSpaceId, await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(SpacesDestination.Chat, surface.CurrentDestination);
    }

    [Theory]
    [InlineData(SpacesDestination.Study, "b1000000-0000-0000-0000-000000000001", SpaceKind.Study)]
    [InlineData(SpacesDestination.Tasks, "b1000000-0000-0000-0000-000000000006", SpaceKind.Tasks)]
    [InlineData(SpacesDestination.Agent, "b1000000-0000-0000-0000-000000000004", SpaceKind.Agent)]
    [InlineData(SpacesDestination.Shopping, "b1000000-0000-0000-0000-000000000002", SpaceKind.Shopping)]
    [InlineData(SpacesDestination.Research, "b1000000-0000-0000-0000-000000000003", SpaceKind.Research)]
    [InlineData(SpacesDestination.Translate, "b1000000-0000-0000-0000-000000000007", SpaceKind.Translate)]
    [InlineData(SpacesDestination.Experiences, "b1000000-0000-0000-0000-000000000008", SpaceKind.Experiences)]
    public async Task Built_in_destinations_open_existing_space_records(
        SpacesDestination destination,
        string expectedSpaceId,
        SpaceKind expectedKind)
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var host = new RecordingHost();
        var surface = new SpacesAppSurface(registry, host);

        await surface.NavigateAsync(destination);

        var expectedId = Guid.Parse(expectedSpaceId);
        Assert.NotNull(host.Space);
        Assert.Equal(expectedId, host.Space!.Id);
        Assert.Equal(expectedKind, host.Space.Kind);
        Assert.Equal(expectedId, await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(destination, surface.CurrentDestination);
    }

    [Fact]
    public async Task Failed_launch_restores_previous_scope_and_does_not_select_failed_destination()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ResearchSpaceId);
        var host = new RecordingHost { Failure = new InvalidOperationException("launch failed") };
        var surface = new SpacesAppSurface(registry, host);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => surface.NavigateAsync(SpacesDestination.Study));

        Assert.Equal("launch failed", error.Message);
        Assert.Equal(SpaceRegistry.ResearchSpaceId, await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(SpacesDestination.Home, surface.CurrentDestination);
    }

    [Fact]
    public async Task Concurrent_navigation_is_serialized_so_destination_matches_final_scope()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var host = new BlockingRecordingHost();
        var surface = new SpacesAppSurface(registry, host);

        var studyNavigation = surface.NavigateAsync(SpacesDestination.Study);
        await host.SpaceLaunchStarted.Task;

        var chatNavigation = surface.NavigateAsync(SpacesDestination.Chat);
        await Assert.ThrowsAsync<TimeoutException>(
            () => host.ModeLaunchStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(100)));

        host.ReleaseSpaceLaunch.TrySetResult();
        await Task.WhenAll(studyNavigation, chatNavigation);

        Assert.True(host.ModeLaunchStarted.Task.IsCompletedSuccessfully);
        Assert.Equal(HavenMode.Chat, host.Mode);
        Assert.Equal(SpaceRegistry.StudySpaceId, host.Space!.Id);
        Assert.Equal(SpaceRegistry.ChatSpaceId, await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(SpacesDestination.Chat, surface.CurrentDestination);
    }

    [Fact]
    public async Task Typed_details_action_preserves_the_canonical_id_policies_and_document()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var target = await registry.CreateAsync("Details model", "Description");
        target = await registry.UpdateAsync(target with { Instructions = "Original instructions", ModelPolicy = new("retained-model"), ContextPolicy = new(SpaceWebAccessPolicy.Disabled),
            LayoutDocument = new([new(Guid.NewGuid(), "user", "Preserved graph") { Metadata = new Dictionary<string, string> { ["custom"] = "retained" } }], []) });
        var before = await registry.GetAllAsync(includeArchived: true);
        var action = await new SpacesModel(registry).CreateEditDetailsActionAsync(SpaceScope.ForCustom(target.Id));
        Assert.Equal(0, (int)SpacesActionKind.Open);
        Assert.Equal(SpacesActionKind.EditDetails, action.Kind);
        Assert.Equal(SpaceScope.ForCustom(target.Id), action.Context.Scope);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(target), System.Text.Json.JsonSerializer.Serialize(action.Context.RegisteredSpace));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(await registry.GetAllAsync(includeArchived: true)));
    }

    [Fact]
    public async Task Typed_details_action_rejects_builtin_archived_missing_and_canceled_resolution()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var model = new SpacesModel(registry);
        Assert.True((await registry.GetAsync(SpaceRegistry.ChatSpaceId))!.IsBuiltIn);
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.CreateEditDetailsActionAsync(SpaceScope.Chat));
        var target = await registry.CreateAsync("Archived");
        await registry.SetArchivedAsync(target.Id, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.CreateEditDetailsActionAsync(SpaceScope.ForCustom(target.Id)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => model.CreateEditDetailsActionAsync(SpaceScope.ForCustom(Guid.NewGuid())));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.CreateEditDetailsActionAsync(SpaceScope.ForCustom(target.Id), canceled.Token));
    }

    [Fact]
    public async Task Missing_existing_details_does_not_seed_or_write_registry_state()
    {
        var settings = new MemorySettingsStore();
        var model = new SpacesModel(new SpaceRegistry(settings));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => model.CreateEditDetailsActionAsync(SpaceScope.ForCustom(Guid.NewGuid())));
        Assert.Null(await settings.GetAsync<object>("spaces.registry", CancellationToken.None));
    }

    [Fact]
    public async Task Legacy_navigation_explicitly_refuses_details_without_opening_chat_or_a_space()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var target = await registry.CreateAsync("No details route");
        var action = await new SpacesModel(registry).CreateEditDetailsActionAsync(SpaceScope.ForCustom(target.Id));
        var navigation = new RecordingHost();
        var host = new SpacesNavigationActionHost(navigation);
        Assert.True(host.SupportsAction(SpacesActionKind.Open));
        Assert.False(host.SupportsAction(SpacesActionKind.EditDetails));
        await Assert.ThrowsAsync<NotSupportedException>(() => host.ExecuteAsync(action));
        Assert.Null(navigation.Mode);
        Assert.Null(navigation.Space);
        Assert.False(navigation.HomeOpened);
    }

    private sealed class BlockingRecordingHost : ISpacesNavigationHost
    {
        public TaskCompletionSource SpaceLaunchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSpaceLaunch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ModeLaunchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SpaceDefinition? Space { get; private set; }
        public HavenMode? Mode { get; private set; }

        public Task OpenHomeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task OpenModeAsync(HavenMode mode, CancellationToken cancellationToken = default)
        {
            Mode = mode;
            ModeLaunchStarted.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task OpenSpaceAsync(SpaceDefinition space, CancellationToken cancellationToken = default)
        {
            Space = space;
            SpaceLaunchStarted.TrySetResult();
            await ReleaseSpaceLaunch.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class RecordingHost : ISpacesNavigationHost
    {
        public bool HomeOpened { get; private set; }
        public HavenMode? Mode { get; private set; }
        public SpaceDefinition? Space { get; private set; }
        public Exception? Failure { get; init; }

        public Task OpenHomeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HomeOpened = true;
            return CompleteOrFail();
        }

        public Task OpenModeAsync(HavenMode mode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Mode = mode;
            return CompleteOrFail();
        }

        public Task OpenSpaceAsync(SpaceDefinition space, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Space = space;
            return CompleteOrFail();
        }

        private Task CompleteOrFail() =>
            Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : null);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SettingsImportResult> ImportAsync(
            SettingsExportManifest manifest,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
