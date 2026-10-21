using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpacesHomeControllerTests
{
    [Fact]
    public async Task LoadAsync_exposes_built_ins_and_empty_custom_list()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);

        await controller.LoadAsync();

        Assert.Equal(8, controller.State.BuiltIns.Count);
        Assert.Contains(controller.State.BuiltIns, item => item.Name == "Chat");
        Assert.Contains(controller.State.BuiltIns, item => item.Name == "Assistants");
        Assert.Empty(controller.State.CustomSpaces);
        Assert.NotNull(controller.State.Selection);
        Assert.True(controller.State.Selection!.IsBuiltIn);
        Assert.False(controller.State.Selection.CanEdit);
    }

    [Fact]
    public async Task ToggleManage_is_presentation_only_and_never_mutates_the_registry()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();

        Assert.False(controller.State.IsManaging);
        var selectionId = controller.State.Selection?.Id;
        var customCountBefore = controller.State.CustomSpaces.Count;

        controller.ToggleManage();
        Assert.True(controller.State.IsManaging);
        Assert.Equal(customCountBefore, controller.State.CustomSpaces.Count);
        Assert.Equal(selectionId, controller.State.Selection?.Id);

        controller.ToggleManage();
        Assert.False(controller.State.IsManaging);
        Assert.Equal(selectionId, controller.State.Selection?.Id);
    }

    [Fact]
    public async Task Manager_and_sidebar_presentation_survive_reload_and_explicit_creation()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        var selectionId = controller.State.Selection?.Id;

        controller.ToggleManage();
        await controller.LoadAsync();
        Assert.True(controller.State.IsManaging);
        Assert.False(controller.State.SidebarCollapsed);
        Assert.Equal(selectionId, controller.State.Selection?.Id);
        Assert.Empty(controller.State.CustomSpaces);

        controller.ToggleSidebar();
        Assert.True(controller.State.SidebarCollapsed);
        await controller.LoadAsync();
        Assert.True(controller.State.IsManaging);
        Assert.True(controller.State.SidebarCollapsed);
        Assert.Equal(selectionId, controller.State.Selection?.Id);
        Assert.Empty(controller.State.CustomSpaces);

        await controller.CreateCustomSpaceAsync();
        Assert.True(controller.State.IsManaging);
        Assert.True(controller.State.SidebarCollapsed);
        Assert.Equal("New Space", Assert.Single(controller.State.CustomSpaces).Name);
    }

    [Fact]
    public async Task CloseManage_is_idempotent_and_never_opens_the_manager()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        var selectionId = controller.State.Selection?.Id;
        var customCountBefore = controller.State.CustomSpaces.Count;

        // Closed manager: CloseManage must be a no-op that never opens it.
        controller.CloseManage();
        Assert.False(controller.State.IsManaging);
        Assert.Equal(customCountBefore, controller.State.CustomSpaces.Count);
        Assert.Equal(selectionId, controller.State.Selection?.Id);

        // Open, then close idempotently.
        controller.ToggleManage();
        Assert.True(controller.State.IsManaging);
        controller.CloseManage();
        Assert.False(controller.State.IsManaging);
        controller.CloseManage();
        Assert.False(controller.State.IsManaging);
        Assert.Equal(customCountBefore, controller.State.CustomSpaces.Count);
        Assert.Equal(selectionId, controller.State.Selection?.Id);
    }

    [Fact]
    public async Task CreateCustomSpaceAsync_creates_selects_and_queues_builder()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var host = new DetailsActionHost();
        var controller = new SpacesHomeController(registry, host);
        await controller.LoadAsync();

        await controller.CreateCustomSpaceAsync();

        Assert.Single(controller.State.CustomSpaces);
        var custom = controller.State.CustomSpaces[0];
        Assert.Equal("New Space", custom.Name);
        Assert.Equal(controller.State.Selection?.Id, custom.Id);
        Assert.Null(controller.State.PendingRenameSpaceId);
        Assert.False(controller.State.Selection?.IsRenaming ?? false);
        Assert.Single(host.Actions);
        Assert.Equal(SpacesActionKind.EditDetails, host.Actions[0].Kind);
        Assert.Equal(custom.Id, host.Actions[0].Context.Scope.RegisteredSpaceId);
        var canonical = await registry.GetAsync(custom.Id);
        Assert.NotNull(canonical);
        await registry.UpdateAsync(canonical! with { IconKey = "book" }, canonical!.Revision);
        await controller.LoadAsync();
        Assert.Equal("book", Assert.Single(controller.State.CustomSpaces).IconKey);
        Assert.Equal("book", (await registry.GetAsync(custom.Id))!.IconKey);
    }

    [Fact]
    public async Task RenameSpaceAsync_renames_custom_space()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        await controller.CreateCustomSpaceAsync();
        var id = controller.State.CustomSpaces[0].Id;

        await controller.RenameSpaceAsync(id, "Recipes");

        Assert.Equal("Recipes", controller.State.CustomSpaces[0].Name);
        Assert.Equal("Recipes", controller.State.Selection?.Name);
        Assert.Null(controller.State.PendingRenameSpaceId);
    }

    [Fact]
    public async Task RenameSpaceAsync_rejects_built_in()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();

        await controller.RenameSpaceAsync(SpaceRegistry.ChatSpaceId, "Forbidden");

        Assert.Contains("Built-in", controller.State.StatusMessage);
        Assert.Equal("Chat", controller.State.BuiltIns[0].Name);
    }

    [Fact]
    public async Task RenameSpaceAsync_rejects_stale_revision()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        await controller.CreateCustomSpaceAsync();
        var id = controller.State.CustomSpaces[0].Id;

        // Advance the registry revision behind the controller's back.
        await registry.RenameAsync(id, "Changed externally");

        await controller.RenameSpaceAsync(id, "Recipes");

        Assert.Contains("revision", controller.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchiveSpaceAsync_removes_custom_space_from_home()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        await controller.CreateCustomSpaceAsync();
        var id = controller.State.CustomSpaces[0].Id;

        await controller.ArchiveSpaceAsync(id);

        Assert.Empty(controller.State.CustomSpaces);
        Assert.DoesNotContain(id, controller.State.BuiltIns.Select(item => item.Id));
    }

    [Fact]
    public async Task ArchiveSpaceAsync_rejects_built_in()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();

        await controller.ArchiveSpaceAsync(SpaceRegistry.ChatSpaceId);

        Assert.Contains("Built-in", controller.State.StatusMessage);
    }

    [Fact]
    public async Task ForkSpaceAsync_creates_independent_copy()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        await controller.CreateCustomSpaceAsync();
        var originalId = controller.State.CustomSpaces[0].Id;

        await controller.ForkSpaceAsync(originalId);

        Assert.Equal(2, controller.State.CustomSpaces.Count);
        Assert.Contains(controller.State.CustomSpaces, item => item.Name == "New Space copy");
    }

    [Fact]
    public async Task MoveSpaceAsync_nests_custom_space_under_another_custom_space()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        var parent = await registry.CreateAsync("Projects");
        var child = await registry.CreateAsync("Alpha");
        await controller.LoadAsync();
        await controller.SelectSpaceAsync(child.Id);

        var parentIndex = controller.State.MoveParentOptions
            .Select((option, index) => (option, index))
            .First(pair => pair.option.Id == parent.Id).index;
        controller.SelectMoveParentIndex(parentIndex);
        await controller.MoveSpaceAsync(child.Id);

        Assert.Equal(parent.Id, controller.State.Selection?.ParentSpaceId);
    }

    [Fact]
    public async Task MoveSpaceAsync_rejects_stale_revision()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        var parent = await registry.CreateAsync("Projects");
        var child = await registry.CreateAsync("Alpha");
        await controller.LoadAsync();
        await controller.SelectSpaceAsync(child.Id);
        controller.SelectMoveParentIndex(controller.State.MoveParentOptions
            .Select((option, index) => (option, index))
            .First(pair => pair.option.Id == parent.Id).index);

        // Mutate the child behind the controller's back so the captured revision is stale.
        await registry.RenameAsync(child.Id, "Changed externally");

        await controller.MoveSpaceAsync(child.Id);

        Assert.Contains("revision", controller.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MoveSpaceAsync_rejects_built_in()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();

        await controller.MoveSpaceAsync(SpaceRegistry.ChatSpaceId);

        Assert.Contains("Built-in", controller.State.StatusMessage);
    }

    [Fact]
    public async Task Search_filters_custom_spaces_and_preserves_selection_and_hierarchy()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        var alpha = await registry.CreateAsync("Alpha recipes");
        var beta = await registry.CreateAsync("Beta notes");
        await controller.LoadAsync();
        await controller.SelectSpaceAsync(alpha.Id);

        controller.SetQuery("beta");

        Assert.Single(controller.State.CustomSpaces);
        Assert.Equal(beta.Id, controller.State.CustomSpaces[0].Id);
        // Selection is preserved even when the selected Space is filtered out of the list.
        Assert.Equal(alpha.Id, controller.State.Selection?.Id);

        controller.SetQuery(string.Empty);

        Assert.Equal(2, controller.State.CustomSpaces.Count);
        Assert.Equal(alpha.Id, controller.State.Selection?.Id);
    }

    [Fact]
    public async Task Clearing_search_restores_exact_ids_and_order()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        var first = await registry.CreateAsync("First");
        var second = await registry.CreateAsync("Second");
        await controller.LoadAsync();
        var originalIds = controller.State.CustomSpaces.Select(item => item.Id).ToArray();

        controller.SetQuery("none");
        Assert.Empty(controller.State.CustomSpaces);

        controller.SetQuery(string.Empty);

        Assert.Equal(originalIds, controller.State.CustomSpaces.Select(item => item.Id));
    }

    [Fact]
    public async Task Delayed_load_does_not_overwrite_newer_query()
    {
        var release = new TaskCompletionSource();
        var store = new DelayedMemorySettingsStore(release.Task);
        var registry = new SpaceRegistry(store);
        var controller = new SpacesHomeController(registry);

        var loadTask = controller.LoadAsync();
        controller.SetQuery("typed while loading");
        release.SetResult();
        await loadTask;

        Assert.Equal("typed while loading", controller.State.Query);
        // The stale load result was discarded because the query advanced the revision.
        Assert.NotEqual("Spaces ready", controller.State.StatusMessage);
    }

    [Fact]
    public async Task OpenSpaceAsync_delegates_to_action_host()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var host = new RecordingActionHost();
        var controller = new SpacesHomeController(registry, host);
        await controller.LoadAsync();

        await controller.OpenSpaceAsync(SpaceRegistry.StudySpaceId);

        Assert.Single(host.Actions);
        Assert.Equal(SpacesActionKind.Open, host.Actions[0].Kind);
        Assert.Equal(SpaceRegistry.StudySpaceId, host.Actions[0].Context.Scope.RegisteredSpaceId);
    }

    [Fact]
    public async Task AddContextAsync_adds_file_reference_to_registry()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        await controller.CreateCustomSpaceAsync();
        var id = controller.State.CustomSpaces[0].Id;

        await controller.AddContextAsync(id, "/tmp/recipe.md");

        var space = await registry.GetAsync(id);
        Assert.NotNull(space);
        Assert.Contains(space!.Files, file => file.Path.EndsWith("recipe.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EditExtensionsAsync_delegates_to_extension_editor_host_for_built_ins()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var host = new RecordingExtensionEditorHost();
        var controller = new SpacesHomeController(registry, extensionEditorHost: host);
        await controller.LoadAsync();
        await controller.SelectSpaceAsync(SpaceRegistry.StudySpaceId);

        await controller.EditExtensionsAsync(SpaceRegistry.StudySpaceId);

        Assert.Single(host.EventsAndExtensionsCalls);
        Assert.Empty(host.GraphEditorCalls);
    }

    [Fact]
    public async Task EditGraphAsync_delegates_to_extension_editor_host_for_custom_spaces()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var host = new RecordingExtensionEditorHost();
        var controller = new SpacesHomeController(registry, extensionEditorHost: host);
        await controller.LoadAsync();
        await controller.CreateCustomSpaceAsync();
        var id = controller.State.CustomSpaces[0].Id;

        await controller.EditGraphAsync(id);

        Assert.Single(host.GraphEditorCalls);
        Assert.Empty(host.EventsAndExtensionsCalls);
    }

    [Fact]
    public async Task EditExtensionsAsync_reports_unavailable_when_host_absent()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();

        await controller.EditExtensionsAsync(SpaceRegistry.StudySpaceId);

        Assert.Contains("not available", controller.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenSpaceAsync_reports_opened_for_direct_host()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var host = new RecordingActionHost();
        var controller = new SpacesHomeController(registry, host);
        await controller.LoadAsync();

        await controller.OpenSpaceAsync(SpaceRegistry.StudySpaceId);

        Assert.Single(host.Actions);
        Assert.Contains("Opened", controller.State.StatusMessage);
    }

    [Fact]
    public async Task OpenSpaceAsync_reports_opening_for_queued_admission_host()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var host = new QueuedAdmissionHost();
        var controller = new SpacesHomeController(registry, host);
        await controller.LoadAsync();

        await controller.OpenSpaceAsync(SpaceRegistry.StudySpaceId);

        Assert.Single(host.Actions);
        Assert.Contains("Opening", controller.State.StatusMessage);
        Assert.DoesNotContain("Opened", controller.State.StatusMessage);
    }

    [Fact]
    public async Task Rejected_queue_admission_is_observable_through_status()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var host = new RefusingAdmissionHost();
        var controller = new SpacesHomeController(registry, host);
        await controller.LoadAsync();

        await controller.OpenSpaceAsync(SpaceRegistry.StudySpaceId);

        Assert.Contains("refused", controller.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Opened", controller.State.StatusMessage);
    }

    [Fact]
    public async Task Held_load_does_not_overwrite_newer_selection()
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var completedReads = 0;
        var controller = new SpacesHomeController(registry, null, null, null, async cancellationToken =>
        {
            if (Interlocked.Increment(ref completedReads) != 1) return;
            observed.TrySetResult(); // The actual canonical read and its lock have completed.
            await release.Task.WaitAsync(cancellationToken);
        });

        var loadTask = controller.LoadAsync();
        Task selectTask = Task.CompletedTask;
        Exception? failure = null;
        try
        {
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The held snapshot deliberately predates this real mutation. The registry
            // remains usable while delivery of that older response is suspended.
            var target = await registry.CreateAsync("Target").WaitAsync(TimeSpan.FromSeconds(5));
            selectTask = controller.SelectSpaceAsync(target.Id);
            await selectTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(loadTask.IsCompleted, "The earlier load must remain held after the newer selection completes.");
            Assert.Equal(target.Id, controller.State.Selection?.Id);
            Assert.Equal(target.Id, await registry.GetCurrentSpaceIdAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            var selectedRevision = controller.State.Revision;

            release.TrySetResult();
            await loadTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(target.Id, controller.State.Selection?.Id);
            Assert.Equal(selectedRevision, controller.State.Revision);
            Assert.Equal("Spaces ready", controller.State.StatusMessage);
        }
        catch (Exception error) { failure = error; }
        finally
        {
            release.TrySetResult();
            try { await Task.WhenAll(loadTask, selectTask).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception cleanup)
            {
                if (failure is null) failure = cleanup;
                else if (!ReferenceEquals(failure, cleanup))
                    failure = new AggregateException("Held-load assertion and original task cleanup failed.", failure, cleanup);
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public async Task Edit_details_uses_the_selected_canonical_record_without_mutating_it_or_opening_graph()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var target = await registry.CreateAsync("Details target", "Preserved description");
        target = await registry.UpdateAsync(target with { Instructions = "Preserved instructions", ContextPolicy = new(SpaceWebAccessPolicy.Disabled) });
        var host = new DetailsActionHost();
        var editors = new RecordingExtensionEditorHost();
        var controller = new SpacesHomeController(registry, host, editors);
        await controller.SelectSpaceAsync(target.Id);
        Assert.True(controller.State.Selection!.CanEditDetails);
        Assert.True(controller.State.Selection.CanEditGraph);

        await controller.EditDetailsAsync(target.Id);

        var action = Assert.Single(host.Actions);
        Assert.Equal(SpacesActionKind.EditDetails, action.Kind);
        Assert.Equal(SpaceScope.ForCustom(target.Id), action.Context.Scope);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(target), System.Text.Json.JsonSerializer.Serialize(action.Context.RegisteredSpace));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(target), System.Text.Json.JsonSerializer.Serialize(await registry.GetAsync(target.Id)));
        Assert.Empty(editors.GraphEditorCalls);
        await controller.EditGraphAsync(target.Id);
        Assert.Equal(target.Id, Assert.Single(editors.GraphEditorCalls));
        await controller.SelectSpaceAsync(SpaceRegistry.ChatSpaceId);
        Assert.False(controller.State.Selection!.CanEditDetails);
        await controller.EditExtensionsAsync(SpaceRegistry.ChatSpaceId);
        Assert.Equal(SpaceRegistry.ChatSpaceId, Assert.Single(editors.EventsAndExtensionsCalls));
    }

    [Fact]
    public async Task Legacy_host_cannot_advertise_or_dispatch_details()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var target = await registry.CreateAsync("Legacy target");
        var host = new RecordingActionHost();
        var controller = new SpacesHomeController(registry, host);
        await controller.SelectSpaceAsync(target.Id);
        Assert.False(controller.State.Selection!.CanEditDetails);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.EditDetailsAsync(target.Id));
        Assert.Empty(host.Actions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Details_refuses_nonselected_or_archived_target_after_selection(bool archive)
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var target = await registry.CreateAsync("Removed target");
        var host = new DetailsActionHost();
        var controller = new SpacesHomeController(registry, host);
        await controller.SelectSpaceAsync(target.Id);
        if (archive) await registry.SetArchivedAsync(target.Id, true);
        var id = archive ? target.Id : Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.EditDetailsAsync(id));
        Assert.Empty(host.Actions);
    }

    [Fact]
    public async Task Details_refuses_built_in_and_nonselected_targets()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var target = await registry.CreateAsync("Other target");
        var host = new DetailsActionHost();
        var controller = new SpacesHomeController(registry, host);
        await controller.SelectSpaceAsync(SpaceRegistry.ChatSpaceId);
        Assert.False(controller.State.Selection!.CanEditDetails);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.EditDetailsAsync(SpaceRegistry.ChatSpaceId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.EditDetailsAsync(target.Id));
        Assert.Empty(host.Actions);
    }

    [Fact]
    public async Task Held_details_resolution_cannot_dispatch_after_a_new_selection_starts()
    {
        var settings = new HeldDetailsSettings();
        var registry = new SpaceRegistry(settings);
        var first = await registry.CreateAsync("First");
        var second = await registry.CreateAsync("Second");
        var host = new DetailsActionHost();
        var controller = new SpacesHomeController(registry, host);
        await controller.SelectSpaceAsync(first.Id);
        settings.HoldNextRegistryRead = true;
        var edit = controller.EditDetailsAsync(first.Id);
        Task? select = null;
        try
        {
            await settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            select = controller.SelectSpaceAsync(second.Id);
            // The read-only existing-record query does not hold the registry mutation lock:
            // the newer selection must actually complete while the older details read is held.
            await select.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(edit.IsCompleted);
            Assert.Equal(second.Id, controller.State.Selection!.Id);
            settings.Release.TrySetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => edit.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(second.Id, controller.State.Selection!.Id);
            Assert.Empty(host.Actions);
        }
        finally
        {
            settings.Release.TrySetResult();
            _ = await Record.ExceptionAsync(() => edit.WaitAsync(TimeSpan.FromSeconds(5)));
            if (select is not null) await select.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class DetailsActionHost : ISpacesActionHost, ISpacesActionSupport
    {
        public List<SpacesAction> Actions { get; } = [];
        public bool SupportsAction(SpacesActionKind action) => action is SpacesActionKind.Open or SpacesActionKind.EditDetails;
        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default)
        { Actions.Add(action); return Task.CompletedTask; }
    }

    private sealed class HeldDetailsSettings : IVersionedSettingsStore
    {
        private readonly MemorySettingsStore _inner = new();
        public bool HoldNextRegistryRead { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        {
            if (key == "spaces.registry" && HoldNextRegistryRead)
            {
                HoldNextRegistryRead = false;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return await _inner.GetAsync<T>(key, token);
        }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => _inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => _inner.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => _inner.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => _inner.ImportAsync(value, token);
    }

    private sealed class RecordingActionHost : ISpacesActionHost
    {
        public List<SpacesAction> Actions { get; } = [];

        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class QueuedAdmissionHost : ISpacesActionHost, ISpacesActionAdmissionHost
    {
        public List<SpacesAction> Actions { get; } = [];

        public bool IsQueueAdmission => true;

        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class RefusingAdmissionHost : ISpacesActionHost, ISpacesActionAdmissionHost
    {
        public bool IsQueueAdmission => true;

        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The navigation queue refused the Spaces action.");
    }

    private sealed class RecordingExtensionEditorHost : ISpacesExtensionEditorHost
    {
        public List<Guid> EventsAndExtensionsCalls { get; } = [];
        public List<Guid> GraphEditorCalls { get; } = [];

        public Task OpenEventsAndExtensionsAsync(Guid spaceId, CancellationToken cancellationToken = default)
        {
            EventsAndExtensionsCalls.Add(spaceId);
            return Task.CompletedTask;
        }

        public Task OpenSpaceGraphEditorAsync(Guid spaceId, CancellationToken cancellationToken = default)
        {
            GraphEditorCalls.Add(spaceId);
            return Task.CompletedTask;
        }
    }
}
