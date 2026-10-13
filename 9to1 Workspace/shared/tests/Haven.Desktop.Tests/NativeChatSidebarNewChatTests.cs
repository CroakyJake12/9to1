using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Shell.NativePresentation;
using Haven.UI;
using System.Text.Json;

namespace Haven.Desktop.Tests;

public sealed class NativeChatSidebarNewChatTests
{
    [AvaloniaFact]
    public async Task Restored_assistants_scope_reopens_owner_instead_of_starting_or_listing_generic_chats()
    {
        using var state = new SidebarStateFixture();
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.AssistantsSpaceId);
        var now = DateTimeOffset.UtcNow;
        var unrelated = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
            "Unowned generic chat", null, null, false, false, now, now, SpaceId: SpaceRegistry.AssistantsSpaceId);
        var opens = 0;
        var starts = 0;
        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(unrelated), new EmptyContainerRepository(),
            _ => throw new InvalidOperationException("A generic chat must not open in the Assistants Space."),
            (_, _) => { starts++; return Task.CompletedTask; }, _ => Task.CompletedTask,
            stateStore: state.Store, spaces: registry,
            openSpace: space => { Assert.Equal(SpaceRegistry.AssistantsSpaceId, space.Id); opens++; return Task.CompletedTask; });
        await sidebar.ReloadSpaceScopeAsync();
        Assert.Empty(sidebar.Scene.ChatRows.Items);
        Assert.Equal("Open Assistants", sidebar.Scene.NewChat.Content);

        var window = new Window { Width = 420, Height = 700, Content = sidebar };
        try
        {
            window.Show();
            window.UpdateLayout();
            var button = sidebar.Scene.NewChat;
            var router = new HavenInputRouter(sidebar.Scene.Root);
            var point = new HavenPoint(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);
            router.PointerPressed(point);
            Assert.True(router.PointerReleased(point));
            await WaitUntilAsync(() => opens == 1);
            await sidebar.SelectSpaceFromShellAsync(SpaceRegistry.AssistantsSpaceId);
            Assert.Equal(2, opens);
            Assert.Equal(0, starts);
        }
        finally { window.Content = null; window.Close(); }
    }

    [Fact]
    public async Task Same_named_builtin_and_user_spaces_keep_distinguishable_picker_labels()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var assistants = (await registry.GetAsync(SpaceRegistry.AssistantsSpaceId, TestContext.Current.CancellationToken))!;
        var custom = assistants with { Id = Guid.NewGuid(), IsBuiltIn = false, Kind = SpaceKind.General };
        SpaceDefinition[] rows = [assistants, custom];

        Assert.Equal("Assistants (built-in)", NativeChatSidebar.SpaceChoiceLabel(assistants, rows));
        Assert.Equal("Assistants (custom)", NativeChatSidebar.SpaceChoiceLabel(custom, rows));
        Assert.Equal("Assistants", custom.Name);
        Assert.Equal("Assistants", assistants.Name);
    }

    [AvaloniaFact]
    public async Task Assistants_picker_uses_owning_space_route_and_never_starts_a_generic_chat()
    {
        using var state = new SidebarStateFixture();
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ChatSpaceId);
        SpaceDefinition? opened = null;
        var starts = 0;
        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(), new EmptyContainerRepository(), _ => Task.CompletedTask,
            (_, _) => { starts++; return Task.CompletedTask; }, _ => Task.CompletedTask,
            stateStore: state.Store, spaces: registry, openSpace: space => { opened = space; return Task.CompletedTask; });

        await sidebar.OpenSpaceFromPickerAsync(SpaceRegistry.AssistantsSpaceId);

        Assert.Equal(SpaceRegistry.AssistantsSpaceId, opened!.Id);
        Assert.Equal(SpaceKind.Assistants, opened.Kind);
        Assert.Equal(0, starts);
        // This controlled callback is only a route observation; it grants no runtime
        // authority and does not acknowledge the native owner opening successfully.
        Assert.Equal(SpaceRegistry.ChatSpaceId, await registry.GetCurrentSpaceIdAsync());
    }

    [AvaloniaFact]
    public async Task Successful_assistants_owner_callback_can_restore_the_live_persisted_scope()
    {
        using var state = new SidebarStateFixture();
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ChatSpaceId);
        NativeChatSidebar? sidebar = null;
        using var ownedSidebar = sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(), new EmptyContainerRepository(), _ => Task.CompletedTask,
            (_, _) => throw new InvalidOperationException("The Assistants route must not create a generic chat."),
            _ => Task.CompletedTask, stateStore: state.Store, spaces: registry,
            openSpace: async space =>
            {
                // Controlled owner acknowledgement: production uses the existing App owner
                // before this persistence/refresh sequence, and is not simulated by this test.
                await registry.SetCurrentSpaceIdAsync(space.Id);
                await sidebar!.ReloadSpaceScopeAsync();
            });

        await sidebar.OpenSpaceFromPickerAsync(SpaceRegistry.AssistantsSpaceId);

        Assert.Equal(SpaceRegistry.AssistantsSpaceId, await registry.GetCurrentSpaceIdAsync());
        Assert.Equal(SpaceRegistry.AssistantsSpaceId, sidebar.CurrentSpaceId);
        Assert.Equal("Assistants", sidebar.Scene.SpacePicker.Content);
        Assert.Equal("Open Assistants", sidebar.Scene.NewChat.Content);
    }

    [AvaloniaFact]
    public async Task Missing_assistants_owner_reports_setup_without_changing_scope_or_creating_chat()
    {
        using var state = new SidebarStateFixture();
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ChatSpaceId);
        var starts = 0;
        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(), new EmptyContainerRepository(), _ => Task.CompletedTask,
            (_, _) => { starts++; return Task.CompletedTask; }, _ => Task.CompletedTask, stateStore: state.Store, spaces: registry);

        await sidebar.OpenSpaceFromPickerAsync(SpaceRegistry.AssistantsSpaceId);

        Assert.Equal(0, starts);
        Assert.Equal(SpaceRegistry.ChatSpaceId, await registry.GetCurrentSpaceIdAsync());
        Assert.Contains("configured Assistants workspace", sidebar.Scene.Status.Content);
    }

    [AvaloniaFact]
    public async Task Canonical_chat_lists_legacy_groups_and_chats_without_exposing_another_spaces_rows()
    {
        using var state = new SidebarStateFixture();
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ChatSpaceId);
        var now = DateTimeOffset.UtcNow;
        var group = new ContainerDefinition(Guid.NewGuid(), HavenMode.Chat, "Saved Group", null, "", "", now, now);
        Conversation Chat(string title, Guid? spaceId, Guid? groupId = null) => new(
            Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, title, groupId, null, false, false, now, now, SpaceId: spaceId);
        var legacy = Chat("Legacy chat", null);
        var grouped = Chat("Group member", null, group.Id);
        var current = Chat("Current chat", SpaceRegistry.ChatSpaceId);
        var elsewhere = Chat("Research chat", SpaceRegistry.ResearchSpaceId);
        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(legacy, grouped, current, elsewhere), new EmptyContainerRepository(group),
            _ => Task.CompletedTask, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, stateStore: state.Store, spaces: registry);

        await sidebar.SelectSpaceFromShellAsync(SpaceRegistry.ChatSpaceId);

        Assert.Equal(HavenVisibility.Visible, sidebar.Scene.GroupRows.GetValue(HavenProperties.Visibility));
        Assert.Single(sidebar.Scene.GroupRows.Items);
        Assert.Equal(2, sidebar.Scene.ChatRows.Items.Count);
        var labels = sidebar.Scene.ChatRows.Items.Select(row => row.GetComponent<Haven.UI.Components.Button>("Open").Content).ToArray();
        Assert.Contains("Legacy chat", labels);
        Assert.Contains("Current chat", labels);
        Assert.DoesNotContain("Research chat", labels);
    }

    [AvaloniaFact]
    public async Task Rapid_new_chat_activation_starts_only_one_conversation_until_creation_finishes()
    {
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startCount = 0;

        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(),
            new EmptyContainerRepository(),
            _ => Task.CompletedTask,
            async (_, _) =>
            {
                var count = Interlocked.Increment(ref startCount);
                if (count == 1)
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task;
                }
            },
            _ => Task.CompletedTask);

        var window = new Window { Width = 420, Height = 700, Content = sidebar };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(sidebar.Scene.Root);
            var button = sidebar.Scene.NewChat;
            var point = new HavenPoint(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);

            router.PointerPressed(point);
            Assert.True(router.PointerReleased(point));
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(button.GetValue(HavenProperties.Enabled));

            router.PointerPressed(point);
            router.PointerReleased(point);
            await Task.Delay(50);
            Assert.Equal(1, Volatile.Read(ref startCount));

            releaseFirst.TrySetResult(true);
            await WaitUntilAsync(() => button.GetValue(HavenProperties.Enabled));

            router.PointerPressed(point);
            Assert.True(router.PointerReleased(point));
            await WaitUntilAsync(() => Volatile.Read(ref startCount) == 2);
            Assert.Equal(2, Volatile.Read(ref startCount));
        }
        finally
        {
            releaseFirst.TrySetResult(true);
            window.Content = null;
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class EmptyConversationRepository(params Conversation[] rows) : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Conversation>>(rows.Where(row => mode is null || row.Mode == mode).Take(limit).ToArray());
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyContainerRepository(params ContainerDefinition[] rows) : IContainerRepository
    {
        public Task<IReadOnlyList<ContainerDefinition>> GetByModeAsync(HavenMode mode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerDefinition>>(rows.Where(row => row.Mode == mode).ToArray());
        public Task<IReadOnlyList<Lesson>> GetLessonsAsync(Guid subjectId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Lesson>>([]);
        public Task UpsertAsync(ContainerDefinition item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Lesson> CreateSubjectAsync(ContainerDefinition item, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAndDetachConversationsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpsertLessonAsync(Lesson item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteLessonAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SidebarStateFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "astra-sidebar-" + Guid.NewGuid().ToString("N"));
        public NativeChatUiStateStore Store { get; }
        public SidebarStateFixture()
        {
            Directory.CreateDirectory(_directory);
            Store = new NativeChatUiStateStore(Path.Combine(_directory, "sidebar.json"));
        }
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, string> _settings = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class =>
            Task.FromResult(_settings.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : null);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class
        { _settings[key] = JsonSerializer.Serialize(value); return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token)
        { _settings.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) =>
            Task.FromResult(new SettingsExportManifest { Settings = new(_settings) });
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }
}
