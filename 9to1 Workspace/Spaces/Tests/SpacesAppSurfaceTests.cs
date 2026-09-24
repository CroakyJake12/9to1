using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpacesAppSurfaceTests
{
    [Fact]
    public void Built_in_descriptors_have_independent_stable_scopes()
    {
        Assert.Collection(
            SpacesModel.BuiltIns,
            chat =>
            {
                Assert.Equal(SpacesDestinationKind.Chat, chat.Destination);
                Assert.Equal("spaces.chat", chat.Scope.Key);
                Assert.Null(chat.Scope.RegisteredSpaceId);
            },
            study =>
            {
                Assert.Equal(SpacesDestinationKind.Study, study.Destination);
                Assert.Equal(SpaceRegistry.StudySpaceId, study.Scope.RegisteredSpaceId);
            },
            tasks =>
            {
                Assert.Equal(SpacesDestinationKind.Tasks, tasks.Destination);
                Assert.Equal(SpaceRegistry.AgentSpaceId, tasks.Scope.RegisteredSpaceId);
            });

        Assert.Equal(3, SpacesModel.BuiltIns.Select(definition => definition.Scope.Key).Distinct().Count());
    }

    [Fact]
    public async Task Sidebar_includes_built_ins_and_independently_scoped_custom_spaces()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var model = new SpacesModel(registry);

        var custom = await model.CreateCustomSpaceAsync("Recipes", "Keep dinner plans together.");
        var sidebar = await model.GetSidebarDestinationsAsync();

        Assert.Collection(
            sidebar,
            chat => Assert.Equal(SpacesDestinationKind.Chat, chat.Destination),
            study => Assert.Equal(SpacesDestinationKind.Study, study.Destination),
            tasks => Assert.Equal(SpacesDestinationKind.Tasks, tasks.Destination),
            recipes =>
            {
                Assert.Equal(SpacesDestinationKind.Custom, recipes.Destination);
                Assert.Equal("Recipes", recipes.Label);
                Assert.Equal(custom.Scope, recipes.Scope);
                Assert.Equal(custom.Scope.RegisteredSpaceId, recipes.Scope.RegisteredSpaceId);
            });
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
                SpacesDestination.Research
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
        Assert.Null(await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(SpacesDestination.Chat, surface.CurrentDestination);
    }

    [Theory]
    [InlineData(SpacesDestination.Study, "b1000000-0000-0000-0000-000000000001", SpaceKind.Study)]
    [InlineData(SpacesDestination.Tasks, "b1000000-0000-0000-0000-000000000004", SpaceKind.Agent)]
    [InlineData(SpacesDestination.Research, "b1000000-0000-0000-0000-000000000003", SpaceKind.Research)]
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
        Assert.Null(await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(SpacesDestination.Chat, surface.CurrentDestination);
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
