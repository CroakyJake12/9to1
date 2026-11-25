using System.Reflection;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.Desktop.Views.Shell;
using Haven.Desktop.Views.Shell.NativePresentation;
using Haven.Infrastructure;
using Haven.UI;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [AvaloniaFact]
    public async Task Spaces_production_New_Chat_does_not_publish_history_or_a_restore_address_before_storage()
    {
        var token = TestContext.Current.CancellationToken;
        var timeout = TimeSpan.FromSeconds(15);
        await using var rig = new Rig(temporary: false);
        using var paths = new ObservationPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(paths.DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var principal = new OperatingSystemPrincipalSource();
        await using var home = new HomeNativeWindowsComposition(
            new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json")), principal, paths,
            new("9to1.home.spaces.fresh." + Guid.NewGuid().ToString("N")));
        var registry = new SpaceRegistry(new ObservationSettings());
        var space = await registry.CreateAsync("Scoped fresh Chat", cancellationToken: token);
        var settings = new UserPreferencesService(paths);
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        services.AddHavenOwnedWindowsHomeDomain(home, paths, principal);
        services.AddSingleton(registry);
        services.AddSingleton<HavenEventBus>();
        services.AddSingleton(rig.Service);
        services.AddSingleton<IConversationSafetyService>(new Safety());
        services.AddSingleton<IConversationRepository>(rig.Conversations);
        var drafts = new NativeComposerDraftStore();
        services.AddSingleton(drafts.Source);
        services.AddSingleton(settings);
        services.AddSingleton(NativeComposerProxy<IProviderModelClient>((name, _) => name switch
        {
            "GetModelsAsync" => Task.FromResult<IReadOnlyList<ModelDescriptor>>([]),
            "IsAvailableAsync" => Task.FromResult(true),
            _ => throw new InvalidOperationException("Unexpected controlled model call: " + name)
        }));
        await using var provider = services.BuildServiceProvider();
        using var app = new CancellationTokenSource();
        using var host = new CancellationTokenSource();
        await provider.GetRequiredService<IAppDatabase>().InitializeAsync(token);
        await home.StartOriginalAsync().WaitAsync(timeout, token);
        var actor = (await home.Profiles.GetCurrentAsync(token))!;
        var expectedLocation = AppContentAddress.Parse($"9to1://spaces/selected/{space.Id:D}");
        var factory = SpacesBrowseAppContentFactory.BindOriginalSameProcess(provider, home, app.Token, host.Token);
        var session = Assert.IsType<SpacesBrowseAppContentSession>(factory.CreateSession(
            new(Guid.NewGuid(), expectedLocation, actor.ProfileId, BrowserTabPrivacy.Standard)));
        var window = new Window { Width = 1200, Height = 900 };
        try
        {
            await session.InitializeAsync(token).WaitAsync(timeout, token);
            var root = session.OriginalNativeView;
            window.Content = root;
            window.Show();
            window.UpdateLayout();
            var spaces = Assert.Single(root.GetVisualDescendants().OfType<SpacesHomePage>());
            async Task<NewChatPage> ClickNewChatAsync()
            {
                window.UpdateLayout();
                var newChat = Assert.Single(root.GetVisualDescendants().OfType<Avalonia.Controls.Button>(),
                    button => AutomationProperties.GetAutomationId(button) == "space-new-chat");
                var center = newChat.TranslatePoint(new Point(newChat.Bounds.Width / 2, newChat.Bounds.Height / 2), window)!.Value;
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
                await spaces.CaptureTaskSidebarActionsAsync().WaitAsync(timeout, token);
                return Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            }

            var startingWrites = rig.Conversations.Writes;
            var page = await ClickNewChatAsync();
            Assert.Equal(space.Id, page.CurrentConversation.SpaceId);
            Assert.Equal(startingWrites, rig.Conversations.Writes);
            Assert.Equal(expectedLocation, session.Address);
            Assert.Null(await rig.Conversations.GetAsync(page.ConversationId, token));
            var toggle = Assert.Single(page.Scene.Root!.DescendantsAndSelf().OfType<HavenButton>(),
                item => item.Name == "TemporaryToggle");
            var id = page.ConversationId;
            InvokeButton(toggle);
            await page.JoinAdmittedOriginalWorkAsync().WaitAsync(timeout, token);
            Assert.True(page.IsTemporary);
            Assert.Equal("Temporary: on", toggle.Content);
            Assert.Equal(id, page.ConversationId);
            Assert.Equal(expectedLocation, session.Address);
            Assert.Equal(startingWrites, rig.Conversations.Writes);

            page.ComposerInput.Text = "Unsent text must not leave Temporary Chat";
            var ordinary = await ClickNewChatAsync();
            Assert.NotEqual(id, ordinary.ConversationId);
            Assert.False(ordinary.IsTemporary);
            Assert.Null(await rig.Conversations.GetAsync(id, token));
            Assert.Equal(startingWrites, rig.Conversations.Writes);
            Assert.Null(drafts.Last);

            var ordinaryId = ordinary.ConversationId;
            ordinary.ComposerInput.Text = "Preserve this distinct ordinary draft";
            var next = await ClickNewChatAsync();
            Assert.NotEqual(ordinaryId, next.ConversationId);
            Assert.Empty(next.ComposerInput.Text ?? string.Empty);
            Assert.Equal(expectedLocation, session.Address);
            Assert.Null(await rig.Conversations.GetAsync(next.ConversationId, token));
            var saved = Assert.IsType<Conversation>(await rig.Conversations.GetAsync(ordinaryId, token));
            Assert.Equal(space.Id, saved.SpaceId);
            Assert.Equal(startingWrites + 1, rig.Conversations.Writes);
            Assert.Equal(ordinaryId, Assert.IsType<ConversationDraft>(drafts.Last).ConversationId);
            Assert.Equal("Preserve this distinct ordinary draft", drafts.Last!.Content);
        }
        finally
        {
            await session.CloseAndDrainAsync();
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Temporary_chat_current_native_controls_cancel_and_fail_without_saving_then_use_exact_local_receipt()
    {
        var token = TestContext.Current.CancellationToken;
        var timeout = TimeSpan.FromSeconds(15);
        await using var rig = new Rig(temporary: false);
        var model = rig.Provider.Model.Model with
        {
            Name = rig.Provider.Model.Key,
            Capabilities = new HashSet<ToolCapability> { ToolCapability.Text }
        };
        rig.Client.AvailableModels = [model];
        rig.Client.OrdinaryProviderBody = (_, _) => Task.FromResult(false);
        var directory = Path.Combine(Path.GetTempPath(), "native-temporary-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new TemporarySaveStore();
        var drafts = new NativeComposerDraftStore();
        var page = CreateNativeComposerPage(rig, directory, drafts, conversationWrites: store,
            enableFreshChatTemporaryLifecycle: true);
        var injectedRollback = new IOException("Simulated local rollback.");
        Exception? retainedOnClose = null;
        try
        {
            await page.OriginalInitialization.WaitAsync(timeout, token);
            await page.RefreshModelsAsync();
            page.SelectModel(model);
            var previousId = page.ConversationId;
            await page.StartFreshConversationAsync();
            var id = page.ConversationId;
            Assert.NotEqual(previousId, id);
            const string prompt = "An original Temporary Chat message";
            page.ComposerInput.Text = prompt;
            Assert.Null(drafts.Last);
            await page.SetTemporaryAsync(true);
            Assert.True(page.IsTemporary);
            await page.SetTemporaryAsync(false);
            Assert.False(page.IsTemporary);
            await page.SetTemporaryAsync(true);
            Assert.Equal(id, page.ConversationId);
            Assert.Equal(prompt, page.ComposerInput.Text);
            Assert.Equal(0, store.Saves);
            var header = Assert.Single(page.Scene.Root!.DescendantsAndSelf().OfType<HavenButton>(),
                item => item.Name == "TemporaryToggle");
            Assert.Equal("Temporary: on", header.Content);
            Assert.Contains("Temporary Chat, on", header.Accessibility.AccessibleName);

            page.RequestComposerSend();
            var send = Assert.IsAssignableFrom<Task>(typeof(NewChatPage)
                .GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            await send.WaitAsync(timeout, token);
            Assert.True(page.HasStarted);
            Assert.DoesNotContain(rig.Conversations.Messages, message => message.ConversationId == id);
            const string draft = "A distinct unsent draft";
            page.SetDraft(draft);

            var temporary = page.CurrentConversation;
            await page.TogglePinAsync();
            await page.ArchiveAsync();
            page.ShowRenameFlyout();
            await page.BranchLatestAsync();
            Assert.Equal(temporary, page.CurrentConversation);
            Assert.Equal(draft, page.ComposerInput.Text);
            Assert.Equal(0, store.Saves);

            InvokeButton(header);
            var cancel = await FindChoiceAsync(page, "Cancel", token);
            Assert.True(page.IsTemporary);
            InvokeButton(cancel);
            Assert.Equal(0, store.Saves);

            var failed = WaitForStatus(page, "Local save could not be completed.");
            await page.ToggleTemporaryAsync();
            InvokeChoice(page, "Save locally");
            var first = await store.NextAsync(token).AsTask().WaitAsync(timeout, token);
            Assert.True(page.IsTemporary);
            first.Reply.SetException(injectedRollback);
            await failed.WaitAsync(timeout, token);
            var saving = typeof(NewChatPage).GetField("_temporarySaveInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var settle = CancellationTokenSource.CreateLinkedTokenSource(token);
            settle.CancelAfter(timeout);
            while (Equals(saving.GetValue(page), 1)) await Task.Delay(1, settle.Token);
            Assert.True(page.IsTemporary);
            Assert.Equal(id, page.ConversationId);
            Assert.Equal(draft, page.ComposerInput.Text);

            var saved = WaitForStatus(page, "This chat is saved locally.");
            await page.ToggleTemporaryAsync();
            InvokeChoice(page, "Save locally");
            var second = await store.NextAsync(token).AsTask().WaitAsync(timeout, token);
            Assert.True(page.IsTemporary);
            Assert.Equal(id, second.Request.Original.Id);
            Assert.Equal(draft, second.Request.DraftText);
            Assert.Equal(prompt, Assert.Single(second.Request.History, msg => msg.Role == MessageRole.User).Content);
            var expected = second.Request.Original with { IsTemporary = false, UpdatedAt = DateTimeOffset.UtcNow };
            second.Reply.SetResult(new(expected, Guid.NewGuid()));
            await saved.WaitAsync(timeout, token);
            Assert.Equal(expected, page.CurrentConversation);
            Assert.Equal("Temporary: off", header.Content);
            Assert.Equal(draft, page.ComposerInput.Text);
            Assert.Equal(2, store.Saves);
            await page.SetTemporaryAsync(true);
            Assert.False(page.IsTemporary);
        }
        finally
        {
            page.RequestRetirement();
            retainedOnClose = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            Directory.Delete(directory, true);
        }

        // Recovery keeps the original rollback observable at page retirement; do not
        // mistake a successfully handled UI failure for an unobserved failure-free owner.
        Assert.Same(injectedRollback, retainedOnClose);
    }

    [AvaloniaFact]
    public async Task Fresh_ordinary_Chat_keeps_its_latest_unsent_draft_on_departure()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var directory = Path.Combine(Path.GetTempPath(), "native-ordinary-draft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var drafts = new NativeComposerDraftStore();
        var page = CreateNativeComposerPage(rig, directory, drafts, enableFreshChatTemporaryLifecycle: true);
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(15), token);
            var outgoing = page.ConversationId;
            page.ComposerInput.Text = "First unsent text";
            page.ComposerInput.Text = "Latest ordinary draft";
            Assert.Null(drafts.Last);
            Assert.Null(await rig.Conversations.GetAsync(outgoing, token));

            await page.StartFreshConversationAsync();
            Assert.NotEqual(outgoing, page.ConversationId);
            Assert.False(page.IsTemporary);
            var stored = await rig.Conversations.GetAsync(outgoing, token);
            Assert.NotNull(stored);
            Assert.False(stored.IsTemporary);
            Assert.NotNull(drafts.Last);
            Assert.Equal(outgoing, drafts.Last.ConversationId);
            Assert.Equal("Latest ordinary draft", drafts.Last.Content);
        }
        finally
        {
            page.RequestRetirement();
            await page.CloseAndDrainAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Native_sidebar_New_Chat_preserves_the_selected_Space_without_saving_before_the_first_send()
    {
        var token = TestContext.Current.CancellationToken;
        var timeout = TimeSpan.FromSeconds(15);
        await using var rig = new Rig(temporary: false);
        var directory = Path.Combine(Path.GetTempPath(), "native-scoped-new-chat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var drafts = new NativeComposerDraftStore();
        var page = CreateNativeComposerPage(rig, directory, drafts, enableFreshChatTemporaryLifecycle: true);
        try
        {
            await page.OriginalInitialization.WaitAsync(timeout, token);
            var paths = NativeComposerProxy<IAppPaths>((name, _) => name == "get_DataDirectory" ? directory : Path.Combine(directory, name));
            var spaces = new SpaceRegistry(new VersionedAtomicSettingsStore(paths));
            var space = await spaces.CreateAsync("Genuine sidebar Space", cancellationToken: token);
            await spaces.SetCurrentSpaceIdAsync(space.Id, token);
            var containers = NativeComposerProxy<IContainerRepository>((name, _) => name == "GetByModeAsync"
                ? Task.FromResult<IReadOnlyList<ContainerDefinition>>([])
                : throw new InvalidOperationException("Unexpected container operation: " + name));

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            NativeChatSidebar? sidebar = null;
            using var actualSidebar = new NativeChatSidebar(rig.Conversations, containers,
                _ => throw new InvalidOperationException("New Chat must not load saved conversation history."),
                async (mode, group) =>
                {
                    try
                    {
                        await MainView.StartNewChatWithScopeAsync(page, mode, group, null, sidebar!.CurrentSpaceId);
                        started.TrySetResult();
                    }
                    catch (Exception cause) { started.TrySetException(cause); }
                }, _ => Task.CompletedTask, spaces: spaces);
            sidebar = actualSidebar;
            await sidebar.ReloadSpaceScopeAsync();
            Assert.Equal(space.Id, sidebar.CurrentSpaceId);
            var previous = page.ConversationId;
            var writes = rig.Conversations.Writes;

            InvokeButton(sidebar.Scene.NewChat);
            await started.Task.WaitAsync(timeout, token);
            var fresh = page.ConversationId;
            Assert.NotEqual(previous, fresh);
            Assert.Equal(space.Id, page.CurrentConversation.SpaceId);
            Assert.Equal(writes, rig.Conversations.Writes);
            Assert.Null(await rig.Conversations.GetAsync(fresh, token));

            page.ComposerInput.Text = "Unsent scoped draft";
            await page.SetTemporaryAsync(true);
            Assert.True(page.IsTemporary);
            Assert.Equal(fresh, page.ConversationId);
            Assert.Equal(space.Id, page.CurrentConversation.SpaceId);
            Assert.Equal("Unsent scoped draft", page.ComposerInput.Text);
            Assert.Equal(writes, rig.Conversations.Writes);
            await page.SetTemporaryAsync(false);
            Assert.False(page.IsTemporary);

            await MainView.StartNewChatWithScopeAsync(page, HavenMode.Chat, null, null, space.Id);
            Assert.NotEqual(fresh, page.ConversationId);
            Assert.Null(await rig.Conversations.GetAsync(page.ConversationId, token));
            var stored = await rig.Conversations.GetAsync(fresh, token);
            Assert.NotNull(stored);
            Assert.Equal(space.Id, stored.SpaceId);
            Assert.NotNull(drafts.Last);
            Assert.Equal(fresh, drafts.Last.ConversationId);
            Assert.Equal("Unsent scoped draft", drafts.Last.Content);
        }
        finally
        {
            page.RequestRetirement();
            await page.CloseAndDrainAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Native_mode_restore_uses_the_selected_Space_and_never_falls_back_to_global_history()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var directory = Path.Combine(Path.GetTempPath(), "native-scoped-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var page = CreateNativeComposerPage(rig, directory, new NativeComposerDraftStore(),
            enableFreshChatTemporaryLifecycle: true);
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(15), token);
            var paths = NativeComposerProxy<IAppPaths>((name, _) => name == "get_DataDirectory" ? directory : Path.Combine(directory, name));
            var spaces = new SpaceRegistry(new VersionedAtomicSettingsStore(paths));
            var space = await spaces.CreateAsync("Current Space", cancellationToken: token);
            var other = await spaces.CreateAsync("Unrelated Space", cancellationToken: token);
            await spaces.SetCurrentSpaceIdAsync(space.Id, token);
            var containers = NativeComposerProxy<IContainerRepository>((name, _) => name == "GetByModeAsync"
                ? Task.FromResult<IReadOnlyList<ContainerDefinition>>([])
                : throw new InvalidOperationException("Unexpected container operation: " + name));
            using var sidebar = new NativeChatSidebar(rig.Conversations, containers, _ => Task.CompletedTask,
                (_, _) => Task.CompletedTask, _ => Task.CompletedTask, spaces: spaces);
            await sidebar.ReloadSpaceScopeAsync();
            sidebar.SetMode(HavenMode.Tasks);
            sidebar.SetMode(HavenMode.Chat);
            Assert.Equal(space.Id, sidebar.CurrentSpaceId);

            var now = DateTimeOffset.UtcNow;
            var allowed = page.CurrentConversation with { Id = Guid.NewGuid(), SpaceId = space.Id, UpdatedAt = now };
            var global = allowed with { Id = Guid.NewGuid(), SpaceId = null, UpdatedAt = now.AddMinutes(4) };
            var foreign = allowed with { Id = Guid.NewGuid(), SpaceId = other.Id, UpdatedAt = now.AddMinutes(3) };
            var temporary = allowed with { Id = Guid.NewGuid(), IsTemporary = true, UpdatedAt = now.AddMinutes(2) };
            var archived = allowed with { Id = Guid.NewGuid(), IsArchived = true, UpdatedAt = now.AddMinutes(1) };
            var wrongMode = allowed with { Id = Guid.NewGuid(), Mode = HavenMode.Tasks, Kind = ConversationKind.Task };
            IReadOnlyList<Conversation> scopedRows = [global, foreign, temporary, archived, wrongMode, allowed];
            var current = allowed;
            var globalReads = 0;
            var scopedReads = 0;
            var stable = true;
            var repository = NativeComposerProxy<IConversationRepository>((name, arguments) =>
            {
                switch (name)
                {
                    case "GetBySpaceAsync":
                        Assert.Equal(space.Id, Assert.IsType<Guid>(arguments![0]));
                        scopedReads++;
                        return Task.FromResult(scopedRows);
                    case "GetAsync":
                        Assert.Equal(allowed.Id, Assert.IsType<Guid>(arguments![0]));
                        return Task.FromResult<Conversation?>(current);
                    case "GetRecentAsync":
                        globalReads++;
                        return Task.FromResult<IReadOnlyList<Conversation>>([global]);
                    default: throw new InvalidOperationException("Unexpected restore operation: " + name);
                }
            });
            bool IsCurrent() => stable && sidebar.CurrentMode == HavenMode.Chat && sidebar.CurrentSpaceId == space.Id;
            var originalWrites = rig.Conversations.Writes;

            Assert.True(await MainView.RestoreNativeModeConversationAsync(page, repository, spaces,
                HavenMode.Chat, sidebar.CurrentSpaceId, IsCurrent));
            Assert.Equal(allowed.Id, page.ConversationId);
            Assert.Equal(0, globalReads);

            // The exact row was moved after the selected Space's recent list was read.
            current = allowed with { SpaceId = other.Id };
            Assert.True(await MainView.RestoreNativeModeConversationAsync(page, repository, spaces,
                HavenMode.Chat, sidebar.CurrentSpaceId, IsCurrent));
            var fresh = page.ConversationId;
            Assert.NotEqual(allowed.Id, fresh);
            Assert.Equal(space.Id, page.CurrentConversation.SpaceId);
            Assert.Equal(originalWrites, rig.Conversations.Writes);
            Assert.Equal(0, globalReads);
            Assert.Equal(2, scopedReads);

            stable = false;
            Assert.False(await MainView.RestoreNativeModeConversationAsync(page, repository, spaces,
                HavenMode.Chat, sidebar.CurrentSpaceId, IsCurrent));
            Assert.Equal(fresh, page.ConversationId);
            Assert.Equal(2, scopedReads);

            Assert.True(await MainView.RestoreNativeModeConversationAsync(page, repository, spaces,
                HavenMode.Chat, null, () => true));
            Assert.Equal(global.Id, page.ConversationId);
            Assert.Equal(1, globalReads);
        }
        finally
        {
            page.RequestRetirement();
            await page.CloseAndDrainAsync();
            Directory.Delete(directory, true);
        }
    }

    private static Task WaitForStatus(NewChatPage page, string prefix)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(object? sender, ChatProjectionStateChangedEventArgs evt)
        {
            if (evt.State.StatusText?.StartsWith(prefix, StringComparison.Ordinal) != true) return;
            page.ProjectionStateChanged -= Observe;
            result.TrySetResult();
        }
        page.ProjectionStateChanged += Observe;
        return result.Task;
    }

    private static void InvokeChoice(NewChatPage page, string label)
    {
        var button = Assert.Single(page.Scene.Root!.DescendantsAndSelf().OfType<HavenButton>(), item => item.Content == label);
        InvokeButton(button);
    }

    private static void InvokeButton(HavenButton button) =>
        typeof(HavenElement).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);

    private static async Task<HavenButton> FindChoiceAsync(NewChatPage page, string label, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            var candidate = page.Scene.Root!.DescendantsAndSelf().OfType<HavenButton>().SingleOrDefault(item => item.Content == label);
            if (candidate is not null) return candidate;
            await Task.Delay(1, limit.Token);
        }
    }

    private sealed class TemporarySaveStore : IConversationRepository, ITemporaryChatSaveRepository
    {
        private readonly Channel<SaveObservation> _attempts = Channel.CreateUnbounded<SaveObservation>();
        internal int Saves { get; private set; }
        internal ValueTask<SaveObservation> NextAsync(CancellationToken token) => _attempts.Reader.ReadAsync(token);
        public Task<TemporaryChatSaveReceipt> SaveTemporaryChatAsync(TemporaryChatSaveRequest request, CancellationToken token)
        {
            Saves++;
            var reply = new TaskCompletionSource<TemporaryChatSaveReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(_attempts.Writer.TryWrite(new(request, reply)));
            return reply.Task.WaitAsync(token);
        }

        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token) =>
            throw new InvalidOperationException("Temporary conversion must not upsert directly.");
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) =>
            throw new InvalidOperationException("Temporary conversion cannot replay individual messages.");
        public Task DeleteConversationAsync(Guid id, CancellationToken token) =>
            throw new InvalidOperationException("No deletion is expected.");
    }
    private sealed record SaveObservation(TemporaryChatSaveRequest Request, TaskCompletionSource<TemporaryChatSaveReceipt> Reply);
}
