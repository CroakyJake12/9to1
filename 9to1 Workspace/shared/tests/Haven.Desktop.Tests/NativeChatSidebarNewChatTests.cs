using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Haven.Desktop.Views.Shell.NativePresentation;
using System.Text.Json;

namespace Haven.Desktop.Tests;

public sealed class NativeChatSidebarNewChatTests
{
    [AvaloniaFact]
    public async Task Retirement_joins_actual_pending_destination_and_retains_its_late_owner_failure()
    {
        using var state = new SidebarStateFixture();
        var now = DateTimeOffset.UtcNow;
        var chat = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Held owner", null, null, false, false, now, now);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalFailure = new IOException("Controlled destination failed after retirement began.");
        using var sidebar = new NativeChatSidebar(new EmptyConversationRepository(chat), new EmptyContainerRepository(),
            async _ => { entered.TrySetResult(); await completion.Task; }, (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask, stateStore: state.Store);
        await sidebar.RefreshAsync();
        var opening = sidebar.HandleConversationActionAsync(new(chat.Id, ChatSidebarConversationAction.Open));
        await entered.Task;
        var close = sidebar.CloseAndDrainAsync();
        Assert.False(close.IsCompleted);
        Assert.Same(close, sidebar.CloseAndDrainAsync());
        Assert.Throws<ObjectDisposedException>(() => { _ = sidebar.RefreshAsync(); });
        completion.SetException(originalFailure);
        Assert.Same(originalFailure, await Assert.ThrowsAsync<IOException>(() => opening));
        Assert.Same(originalFailure, await Assert.ThrowsAsync<IOException>(() => close));
        Assert.Null(sidebar.Content);
    }

    [AvaloniaFact]
    public async Task Destination_owner_empty_aggregate_remains_a_failure_alone_and_with_a_sibling()
    {
        using var state = new SidebarStateFixture();
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var empty = new AggregateException();
        var sibling = new IOException("Independent destination failure");
        Exception failure = empty;
        using var sidebar = new NativeChatSidebar(new EmptyConversationRepository(), new EmptyContainerRepository(),
            _ => Task.CompletedTask, (_, _) => Task.FromException(failure), _ => Task.CompletedTask,
            stateStore: state.Store, spaces: registry, startChatInSpace: (_, _, _) => Task.FromException(failure));
        await sidebar.ReloadSpaceScopeAsync();
        Assert.Same(empty, await Assert.ThrowsAsync<AggregateException>(() => sidebar.SelectSpaceFromShellAsync(null)));
        failure = new AggregateException(empty, sibling);
        var compound = await Assert.ThrowsAsync<AggregateException>(() => sidebar.SelectSpaceFromShellAsync(null));
        Assert.Equal(2, compound.InnerExceptions.Count);
        Assert.Contains(compound.InnerExceptions, item => ReferenceEquals(item, empty));
        Assert.Contains(compound.InnerExceptions, item => ReferenceEquals(item, sibling));
        await AssertCloseFailuresAsync(sidebar, empty, sibling);
    }

    [AvaloniaFact]
    public async Task Held_then_refused_conversation_open_preserves_selection_and_unread_state()
    {
        using var state = new SidebarStateFixture();
        var now = DateTimeOffset.UtcNow;
        var priorGroup = Guid.NewGuid(); var targetGroup = Guid.NewGuid();
        var prior = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Prior", priorGroup, null, false, false, now, now);
        var target = prior with { Id = Guid.NewGuid(), Title = "Target", ContainerId = targetGroup };
        var competing = target with { Id = Guid.NewGuid(), Title = "Competing target" };
        await state.Store.MarkUnreadAsync(target.Id);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerCalls = 0;
        using var sidebar = new NativeChatSidebar(new EmptyConversationRepository(prior, target, competing), new EmptyContainerRepository(),
            async _ => { ownerCalls++; entered.TrySetResult(); await finish.Task; }, (_, _) => Task.CompletedTask, _ => Task.CompletedTask,
            stateStore: state.Store);
        await sidebar.RefreshAsync(); sidebar.SetActiveConversation(prior.Id, priorGroup);
        var opening = sidebar.HandleConversationActionAsync(new(target.Id, ChatSidebarConversationAction.Open));
        await entered.Task;
        var busy = await Assert.ThrowsAsync<InvalidOperationException>(() => sidebar.HandleConversationActionAsync(new(competing.Id, ChatSidebarConversationAction.Open)));
        Assert.Equal(1, ownerCalls);
        Assert.Equal(priorGroup, sidebar.ActiveGroupId);
        Assert.True((await state.Store.GetAllAsync())[target.Id].ForceUnread);
        var refused = new InvalidOperationException("Controlled destination owner refused");
        finish.SetException(refused);
        Assert.Same(refused, await Assert.ThrowsAsync<InvalidOperationException>(() => opening));
        Assert.Equal(priorGroup, sidebar.ActiveGroupId);
        Assert.True((await state.Store.GetAllAsync())[target.Id].ForceUnread);
        Assert.Contains("Controlled destination owner refused", (string)Value(sidebar, "sidebar.status")!);
        await AssertCloseFailuresAsync(sidebar, busy, refused);
    }

    [AvaloniaFact]
    public async Task Refused_existing_space_destination_preserves_persisted_scope_and_sidebar_selection()
    {
        using var state = new SidebarStateFixture();
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ChatSpaceId);
        var now = DateTimeOffset.UtcNow; var oldGroup = Guid.NewGuid();
        var target = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
            "Research target", Guid.NewGuid(), null, false, false, now, now, SpaceId: SpaceRegistry.ResearchSpaceId);
        await state.Store.MarkUnreadAsync(target.Id);
        using var sidebar = new NativeChatSidebar(new EmptyConversationRepository(target), new EmptyContainerRepository(),
            _ => Task.FromException(new InvalidOperationException("Controlled pending temporary save refusal")),
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask, stateStore: state.Store, spaces: registry);
        await sidebar.ReloadSpaceScopeAsync(); sidebar.SetActiveConversation(Guid.NewGuid(), oldGroup);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => sidebar.SelectSpaceFromShellAsync(SpaceRegistry.ResearchSpaceId));
        Assert.Equal(SpaceRegistry.ChatSpaceId, sidebar.CurrentSpaceId);
        Assert.Equal(SpaceRegistry.ChatSpaceId, await registry.GetCurrentSpaceIdAsync(TestContext.Current.CancellationToken));
        Assert.Equal(oldGroup, sidebar.ActiveGroupId);
        Assert.True((await state.Store.GetAllAsync())[target.Id].ForceUnread);
        await AssertCloseFailuresAsync(sidebar, refused);
    }

    [AvaloniaFact]
    public async Task New_space_passes_explicit_target_without_publishing_scope_before_owner_acknowledgement()
    {
        using var state = new SidebarStateFixture();
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ChatSpaceId);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var legacyStarts = 0; Guid? requested = null;
        using var sidebar = new NativeChatSidebar(new EmptyConversationRepository(), new EmptyContainerRepository(),
            _ => Task.CompletedTask, (_, _) => { legacyStarts++; return Task.CompletedTask; }, _ => Task.CompletedTask,
            stateStore: state.Store, spaces: registry,
            startChatInSpace: async (_, _, scope) => { requested = scope; entered.TrySetResult(); await finish.Task; });
        await sidebar.ReloadSpaceScopeAsync();
        var opening = sidebar.SelectSpaceFromShellAsync(SpaceRegistry.ResearchSpaceId);
        await entered.Task;
        Assert.Equal(SpaceRegistry.ResearchSpaceId, requested);
        var busy = await Assert.ThrowsAsync<InvalidOperationException>(() => sidebar.SelectSpaceFromShellAsync(SpaceRegistry.TranslateSpaceId));
        Assert.Equal(SpaceRegistry.ChatSpaceId, sidebar.CurrentSpaceId);
        Assert.Equal(SpaceRegistry.ChatSpaceId, await registry.GetCurrentSpaceIdAsync(TestContext.Current.CancellationToken));
        finish.SetResult(); await opening;
        Assert.Equal(SpaceRegistry.ResearchSpaceId, sidebar.CurrentSpaceId);
        Assert.Equal(SpaceRegistry.ResearchSpaceId, await registry.GetCurrentSpaceIdAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, legacyStarts);
        await AssertCloseFailuresAsync(sidebar, busy);
    }

    [AvaloniaFact]
    public async Task Held_existing_space_selection_rejects_another_selection_before_either_scope_is_published()
    {
        using var state = new SidebarStateFixture();
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ChatSpaceId);
        var now = DateTimeOffset.UtcNow;
        var research = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
            "Research target", null, null, false, false, now, now, SpaceId: SpaceRegistry.ResearchSpaceId);
        var translate = research with { Id = Guid.NewGuid(), Title = "Translate target", SpaceId = SpaceRegistry.TranslateSpaceId };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var sidebar = new NativeChatSidebar(new EmptyConversationRepository(research, translate), new EmptyContainerRepository(),
            async _ => { calls++; entered.TrySetResult(); await finish.Task; }, (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask, stateStore: state.Store, spaces: registry);
        await sidebar.ReloadSpaceScopeAsync();
        var first = sidebar.SelectSpaceFromShellAsync(SpaceRegistry.ResearchSpaceId);
        await entered.Task;
        var busy = await Assert.ThrowsAsync<InvalidOperationException>(() => sidebar.SelectSpaceFromShellAsync(SpaceRegistry.TranslateSpaceId));
        Assert.Equal(1, calls);
        Assert.Equal(SpaceRegistry.ChatSpaceId, sidebar.CurrentSpaceId);
        Assert.Equal(SpaceRegistry.ChatSpaceId, await registry.GetCurrentSpaceIdAsync(TestContext.Current.CancellationToken));
        finish.SetResult(); await first;
        Assert.Equal(SpaceRegistry.ResearchSpaceId, sidebar.CurrentSpaceId);
        Assert.Equal(SpaceRegistry.ResearchSpaceId, await registry.GetCurrentSpaceIdAsync(TestContext.Current.CancellationToken));
        await AssertCloseFailuresAsync(sidebar, busy);
    }

    [AvaloniaFact]
    public async Task Picker_retains_exact_scope_callback_and_refuses_duplicate_mismatched_or_retired_lease()
    {
        using var state = new SidebarStateFixture();
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.SetCurrentSpaceIdAsync(SpaceRegistry.ChatSpaceId);
        var now = DateTimeOffset.UtcNow;
        var research = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
            "Research target", null, null, false, false, now, now, SpaceId: SpaceRegistry.ResearchSpaceId);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        NativeChatSidebar? sidebar = null;
        NativeChatSidebar.DestinationTransition? captured = null;
        Task? originalScope = null;
        using var owner = sidebar = new NativeChatSidebar(new EmptyConversationRepository(research), new EmptyContainerRepository(),
            async _ => { entered.TrySetResult(); await finish.Task; }, (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask, stateStore: state.Store, spaces: registry,
            openSpaceInTransition: (space, transition) =>
            {
                captured = transition;
                Assert.Throws<InvalidOperationException>(() => { _ = sidebar!.SelectSpaceUnderTransitionAsync(
                    SpaceRegistry.TranslateSpaceId, HavenMode.Chat, transition); });
                originalScope = sidebar!.SelectSpaceUnderTransitionAsync(space.Id, HavenMode.Chat, transition);
                // Deliberately return before the real scope callback; the coordinator still owns its Task.
                return Task.CompletedTask;
            });
        await sidebar.ReloadSpaceScopeAsync();
        var picker = sidebar.OpenSpaceFromPickerAsync(SpaceRegistry.ResearchSpaceId);
        await entered.Task;
        Assert.False(picker.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => { _ = sidebar.SelectSpaceUnderTransitionAsync(
            SpaceRegistry.ResearchSpaceId, HavenMode.Chat, captured!); });
        Assert.Equal(SpaceRegistry.ChatSpaceId, sidebar.CurrentSpaceId);
        finish.SetResult(); await picker;
        Assert.True(originalScope!.IsCompletedSuccessfully);
        Assert.Equal(SpaceRegistry.ResearchSpaceId, sidebar.CurrentSpaceId);
        Assert.Throws<InvalidOperationException>(() => { _ = sidebar.SelectSpaceUnderTransitionAsync(
            SpaceRegistry.ResearchSpaceId, HavenMode.Chat, captured!); });
        await sidebar.CloseAndDrainAsync();
    }

    [AvaloniaFact]
    public async Task Refused_group_open_does_not_mark_it_read_or_replace_the_active_group()
    {
        using var state = new SidebarStateFixture();
        var now = DateTimeOffset.UtcNow; var oldGroup = Guid.NewGuid();
        var target = new ContainerDefinition(Guid.NewGuid(), HavenMode.Chat, "Target group", null, "", "", now, now);
        await state.Store.MarkUnreadAsync(target.Id);
        using var sidebar = new NativeChatSidebar(new EmptyConversationRepository(), new EmptyContainerRepository(target),
            _ => Task.CompletedTask, (_, _) => Task.CompletedTask,
            _ => Task.FromException(new InvalidOperationException("Controlled group owner refused")), stateStore: state.Store);
        await sidebar.RefreshAsync(); sidebar.SetActiveConversation(Guid.NewGuid(), oldGroup);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => sidebar.HandleGroupActionAsync(new(target.Id, ChatSidebarGroupAction.Open)));
        Assert.Equal(oldGroup, sidebar.ActiveGroupId);
        Assert.True((await state.Store.GetAllAsync())[target.Id].ForceUnread);
        Assert.Contains("Controlled group owner refused", (string)Value(sidebar, "sidebar.status")!);
        await AssertCloseFailuresAsync(sidebar, refused);
    }

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
        Assert.Empty(SectionRows(sidebar, "recent"));
        Assert.Equal("Open Assistants", Value(sidebar, "sidebar.newChatLabel"));

        var window = new Window { Width = 420, Height = 700, Content = sidebar };
        try
        {
            window.Show();
            window.UpdateLayout();
            var button = FindCui<Button>(sidebar, "new-sidebar-chat");
            Click(window, button);
            await WaitUntilAsync(() => opens == 1);
            await sidebar.SelectSpaceFromShellAsync(SpaceRegistry.AssistantsSpaceId);
            Assert.Equal(2, opens);
            Assert.Equal(0, starts);
        }
        finally { window.Content = null; window.Close(); }
        await sidebar.Navigation.WhenActionsIdleAsync();
        await sidebar.CloseAndDrainAsync();
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
        await sidebar.CloseAndDrainAsync();
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
        Assert.Equal("Assistants", Value(sidebar, "sidebar.name"));
        Assert.Equal("Open Assistants", Value(sidebar, "sidebar.newChatLabel"));
        await sidebar.CloseAndDrainAsync();
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

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => sidebar.OpenSpaceFromPickerAsync(SpaceRegistry.AssistantsSpaceId));

        Assert.Equal(0, starts);
        Assert.Equal(SpaceRegistry.ChatSpaceId, await registry.GetCurrentSpaceIdAsync());
        Assert.Contains("configured Assistants workspace", (string)Value(sidebar, "sidebar.status")!);
        await AssertCloseFailuresAsync(sidebar, refused);
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

        Assert.Equal(true, Value(sidebar, "sidebar.groupsVisible"));
        Assert.Single(SectionRows(sidebar, "groups"));
        Assert.Equal(2, SectionRows(sidebar, "recent").Length);
        var labels = SectionRows(sidebar, "recent").Select(row => row["title"] as string).ToArray();
        Assert.Contains("Legacy chat", labels);
        Assert.Contains("Current chat", labels);
        Assert.DoesNotContain("Research chat", labels);
        await sidebar.CloseAndDrainAsync();
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
            var button = FindCui<Button>(sidebar, "new-sidebar-chat");
            Click(window, button);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(button.IsEffectivelyEnabled);

            Click(window, button);
            await Task.Delay(50);
            Assert.Equal(1, Volatile.Read(ref startCount));

            releaseFirst.TrySetResult(true);
            await WaitUntilAsync(() => button.IsEffectivelyEnabled);

            Click(window, button);
            await WaitUntilAsync(() => Volatile.Read(ref startCount) == 2);
            Assert.Equal(2, Volatile.Read(ref startCount));
        }
        finally
        {
            releaseFirst.TrySetResult(true);
            window.Content = null;
            window.Close();
        }
        await sidebar.Navigation.WhenActionsIdleAsync();
        await sidebar.CloseAndDrainAsync();
    }

    private static async Task AssertCloseFailuresAsync(NativeChatSidebar sidebar, params Exception[] expected)
    {
        var close = sidebar.CloseAndDrainAsync();
        var actual = await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.Same(close, sidebar.CloseAndDrainAsync());
        var leaves = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        void Visit(Exception error)
        {
            if (error is AggregateException { InnerExceptions.Count: > 0 } group)
                foreach (var cause in group.InnerExceptions) Visit(cause);
            else leaves.Add(error);
        }
        Visit(actual);
        Assert.Equal(expected.Length, leaves.Count);
        foreach (var cause in expected) Assert.Contains(leaves, actualCause => ReferenceEquals(actualCause, cause));
    }

    private static T FindCui<T>(NativeChatSidebar sidebar, string name) where T : Control =>
        sidebar.Navigation.Root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static object? Value(NativeChatSidebar sidebar, string path)
    { Assert.True(sidebar.Navigation.TryGetValue(path, out var value)); return value; }
    private static IReadOnlyDictionary<string, object?>[] SectionRows(NativeChatSidebar sidebar, string section)
    {
        var sections = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(Value(sidebar, "sidebar.sections"));
        return ((IEnumerable<IReadOnlyDictionary<string, object?>>)sections.Single(row => row["key"] as string == section)["rows"]!).ToArray();
    }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
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
