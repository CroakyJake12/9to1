using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Browse.Host.Tests;

/// <summary>Exercises the SAME production post-Home-admission route step and real
/// Chat factory/page with controlled external I/O. This does not claim installed
/// Home admission, native interaction, or canonical draft acceptance.</summary>
public sealed class SpacesConversationRouteTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_saved_conversation_restores_the_real_same_page_target_and_round_trips_existing_tab_state(bool inSpace)
    {
        using var rig = new Rig();
        if (inSpace)
        {
            var space = await rig.Spaces.CreateAsync("Saved route fixture");
            rig.Row = rig.Row! with { SpaceId = space.Id };
        }
        var row = rig.Row!;
        var legacy = new TabSessionSnapshot(Guid.NewGuid(), "chat", "Saved chat",
            new JsonObject { ["ConversationId"] = row.Id.ToString("D"), ["extension"] = "preserved" }.ToJsonString(),
            "{\"history\":[1]}", 42, null, true, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Assert.True(UnifiedWorkspaceTabState.TryProject(legacy, "profile", out var projected));
        var session = rig.Session(projected!.Address, legacy.Id);
        try
        {
            Assert.True(await session.TryRestoreRequestedConversationAsync(CancellationToken.None).WaitAsync(Bound));
            var page = Chat(session);
            Assert.Equal(row.Id, page.ConversationId);
            Assert.Equal(row.SpaceId, page.CurrentConversation.SpaceId);
            Assert.Equal(0, rig.ConversationWrites); // No Fresh or AssignSpace on restoration.
            var messages = (IEnumerable<ChatMessage>)typeof(NewChatPage).GetField("_messages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            Assert.Contains(messages, message => message.Id == rig.SavedMessage.Id && message.Content == rig.SavedMessage.Content);
            var expectedAddress = inSpace
                ? $"9to1://spaces/selected/{row.SpaceId:D}/chat/{row.Id:D}"
                : $"9to1://spaces/chat/{row.Id:D}";
            Assert.Equal(expectedAddress, session.Address.Value);

            var saved = UnifiedWorkspaceTabState.Merge(legacy, projected with { Address = session.Address.Value });
            Assert.Equal(legacy.Id, saved.Id);
            Assert.Equal(legacy.NavigationJson, saved.NavigationJson);
            Assert.Equal("preserved", JsonNode.Parse(saved.StateJson)!["extension"]!.GetValue<string>());
            Assert.True(UnifiedWorkspaceTabState.TryProject(saved, "other-profile", out var reopened));
            Assert.Equal(legacy.Id, reopened!.Id);
            Assert.Equal("profile", reopened.ProfileId);
            Assert.Equal(expectedAddress, reopened.Address);
        }
        finally { await CloseSettledRouteAsync(session); }
    }

    [AvaloniaTheory]
    [InlineData("missing")]
    [InlineData("temporary")]
    [InlineData("archived")]
    [InlineData("mode")]
    [InlineData("kind")]
    [InlineData("missing-space")]
    [InlineData("wrong-space")]
    public async Task Explicit_unavailable_targets_keep_their_address_and_never_create_a_replacement(string unavailable)
    {
        using var rig = new Rig();
        var id = rig.Row!.Id;
        var address = $"9to1://spaces/chat/{id:D}";
        rig.Row = unavailable switch
        {
            "missing" => null,
            "temporary" => rig.Row with { IsTemporary = true },
            "archived" => rig.Row with { IsArchived = true },
            "mode" => rig.Row with { Mode = HavenMode.Tasks },
            "kind" => rig.Row with { Kind = ConversationKind.Call },
            "missing-space" => rig.Row with { SpaceId = Guid.NewGuid() },
            _ => rig.Row
        };
        if (unavailable == "wrong-space") address = $"9to1://spaces/selected/{Guid.NewGuid():D}/chat/{id:D}";
        var session = rig.Session(address);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.TryRestoreRequestedConversationAsync(CancellationToken.None));
            Assert.Equal(address, session.Address.Value);
            Assert.Equal(0, rig.ModelReads); // Rejected before even acquiring a Chat UI.
            Assert.Equal(0, rig.ConversationWrites);
            Assert.Null(typeof(SpacesBrowseAppContentSession).GetField("_chatPage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session));
        }
        finally { await session.CloseAndDrainAsync().WaitAsync(Bound); }
    }

    [AvaloniaFact]
    public async Task Two_tabs_restore_the_same_canonical_conversation_into_distinct_owned_controls()
    {
        using var rig = new Rig();
        var address = $"9to1://spaces/chat/{rig.Row!.Id:D}";
        var first = rig.Session(address); var second = rig.Session(address);
        try
        {
            await first.TryRestoreRequestedConversationAsync(CancellationToken.None).WaitAsync(Bound);
            await second.TryRestoreRequestedConversationAsync(CancellationToken.None).WaitAsync(Bound);
            Assert.NotSame(Chat(first), Chat(second));
            Assert.NotSame(Chat(first).Scene, Chat(second).Scene);
            Assert.Equal(rig.Row.Id, Chat(first).ConversationId);
            Assert.Equal(rig.Row.Id, Chat(second).ConversationId);
            Assert.Equal(0, rig.ConversationWrites);
        }
        finally
        {
            try { await CloseSettledRouteAsync(first); }
            finally { await CloseSettledRouteAsync(second); }
        }
    }

    [AvaloniaFact]
    public async Task Public_initialization_preserves_the_saved_target_and_publishes_real_conversation_changes()
    {
        using var rig = new Rig(withHome: true);
        var home = rig.Home!;
        SpacesBrowseAppContentSession? session = null;
        try
        {
            // Real process-local Linux Home admission in an isolated directory.
            // This issues no installed-peer receipt and opens no native window.
            await home.StartOriginalAsync().WaitAsync(Bound);
            var actor = await home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            var original = rig.Row!;
            session = rig.Session($"9to1://spaces/chat/{original.Id:D}", profileId: actor.ProfileId);
            var observedAddresses = new List<string>();
            session.StateChanged += (_, _) => observedAddresses.Add(session.Address.Value);
            await session.InitializeAsync().WaitAsync(Bound);
            Assert.True(session.IsPublicationCurrent);
            // The owner's Spaces entry retains the icon-only rail: the tab's native view is the
            // Spaces Home layout with the real conversation mounted inside its authored workspace.
            var spacesHome = Assert.IsType<SpacesHomePage>(Assert.IsType<ContentControl>(session.OriginalNativeView).Content);
            var page = Assert.IsType<NewChatPage>(spacesHome.SpaceWorkspaceContent);
            Assert.Equal(original.Id, page.ConversationId);
            Assert.Contains($"9to1://spaces/chat/{original.Id:D}", observedAddresses);

            var changed = original with { Id = Guid.NewGuid(), Title = "Another saved conversation" };
            rig.Row = changed;
            await page.LoadConversationAsync(changed).WaitAsync(Bound);
            var expected = $"9to1://spaces/chat/{changed.Id:D}";
            Assert.Equal(expected, session.Address.Value);
            Assert.Contains(expected, observedAddresses); // The real page event, not a synthetic notification.
            Assert.Equal(0, rig.ConversationWrites);

            var canonical = new TabSessionSnapshot(Guid.NewGuid(), "spaces", "Saved chat", "{}", "history", 2,
                null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            var projection = new BrowserTabState(canonical.Id, canonical.Title, session.Address.Value,
                BrowserTabPrivacy.Standard, string.Empty, DateTimeOffset.UtcNow) { ProfileId = actor.ProfileId };
            var saved = UnifiedWorkspaceTabState.Merge(canonical, projection);
            Assert.True(UnifiedWorkspaceTabState.TryProject(saved, actor.ProfileId, out var reopened));
            Assert.Equal(canonical.Id, reopened!.Id);
            Assert.Equal(expected, reopened.Address);
        }
        finally
        {
            try { if (session is not null) await CloseSettledRouteAsync(session); }
            finally { await home.CloseAndDrainAsync().WaitAsync(Bound); }
        }
    }

    [AvaloniaTheory]
    [InlineData("9to1://spaces/chat/not-a-conversation")]
    [InlineData("9to1://spaces/chat/00000000-0000-0000-0000-000000000000")]
    [InlineData("9to1://spaces/selected/not-a-space/chat/acacacac-acac-acac-acac-acacacacacac")]
    public async Task Malformed_explicit_chat_target_is_not_silently_reinterpreted_as_a_new_chat(string address)
    {
        using var rig = new Rig(); var session = rig.Session(address);
        try
        {
            await Assert.ThrowsAsync<FormatException>(() => session.TryRestoreRequestedConversationAsync(CancellationToken.None));
            Assert.Equal(address, session.Address.Value);
            Assert.Equal(0, rig.ModelReads);
            Assert.Equal(0, rig.ConversationWrites);
        }
        finally { await session.CloseAndDrainAsync().WaitAsync(Bound); }
    }

    [AvaloniaTheory]
    [InlineData("9to1://spaces")]
    [InlineData("9to1://spaces/chat")]
    [InlineData("9to1://spaces/selected/acacacac-acac-acac-acac-acacacacacac")]
    public async Task Existing_locations_without_a_conversation_keep_their_current_initialization_path(string address)
    {
        using var rig = new Rig(); var session = rig.Session(address);
        try
        {
            Assert.False(await session.TryRestoreRequestedConversationAsync(CancellationToken.None));
            Assert.Equal(address, session.Address.Value);
            Assert.Equal(0, rig.ModelReads);
        }
        finally { await session.CloseAndDrainAsync().WaitAsync(Bound); }
    }

    /// <summary>
    /// The contextual Space sidebar must create and open real conversations inside the selected
    /// Space, mounted beside the retained icon-only rail. This is the owner's chat/history
    /// requirement, so it is proven against the actual session route rather than a mock surface.
    /// </summary>
    [AvaloniaFact]
    public async Task Contextual_sidebar_new_chat_opens_a_real_conversation_in_the_selected_Space()
    {
        using var rig = new Rig(withHome: true, allowConversationWrites: true);
        SpacesBrowseAppContentSession? session = null;
        try
        {
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            session = rig.Session("9to1://spaces", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.DispatchAsync("spaces.open", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var first = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            var firstId = first.ConversationId;

            await spaces.DispatchAsync("spaces.new-chat", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var second = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.NotEqual(firstId, second.ConversationId);
            Assert.Equal(SpaceRegistry.ChatSpaceId, second.CurrentConversation.SpaceId);
            // The rail layout is still the tab's live view: the new conversation is mounted
            // inside it, so the rail and the contextual sidebar remain presented together.
            Assert.Same(spaces, root.Content);

            // A sidebar row restores that exact stored conversation identity rather than
            // creating another one or reusing the currently mounted page.
            await spaces.DispatchAsync("spaces.open-conversation", firstId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var reopened = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.NotEqual(second.ConversationId, reopened.ConversationId);
            Assert.Equal(firstId, reopened.ConversationId);
            Assert.Same(spaces, root.Content);
        }
        finally { await session!.CloseAndDrainAsync().WaitAsync(Bound); }
    }

    /// <summary>
    /// A UI action that faults must leave the session closable. On HEAD 71661cf3 a failed Space
    /// sidebar action left the real app unable to complete an ordinary close for the rest of its
    /// lifetime, so this is proven against the actual session route rather than assumed.
    /// </summary>
    [AvaloniaFact]
    public async Task A_faulted_workspace_command_still_allows_an_ordinary_close()
    {
        using var rig = new Rig(withHome: true, allowConversationWrites: true);
        SpacesBrowseAppContentSession? session = null;
        try
        {
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            session = rig.Session("9to1://spaces", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);

            // Ask the sidebar to open a conversation identity that does not exist. The real route
            // refuses it in the surface that raised it; the refusal must not become retained work
            // that makes an ordinary close impossible for the rest of the tab's life.
            await spaces.DispatchAsync("spaces.open-conversation", Guid.NewGuid()).AsTask().WaitAsync(Bound);
            Assert.Contains("no longer available", spaces.Controller.State.StatusMessage);

            await session.CloseAndDrainAsync().WaitAsync(Bound);
            Assert.True(session.CanRetireOriginalView);
            Assert.Null(root.Content);
        }
        finally
        {
            if (session is not null)
                try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Manager_return_preserves_exact_mounted_page_conversation_and_draft()
    {
        using var rig = new Rig(withHome: true, allowConversationWrites: true);
        SpacesBrowseAppContentSession? session = null;
        try
        {
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            session = rig.Session("9to1://spaces", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);

            // Mount a real chat and leave an unsent draft.
            await spaces.DispatchAsync("spaces.open", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var mounted = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            var mountedId = mounted.ConversationId;
            mounted.SetDraft("unsent preserved draft");
            Assert.Equal("unsent preserved draft", mounted.ComposerInput.Text);

            // Open the manager: presentation-only, nothing created, the same page stays mounted.
            await spaces.DispatchAsync("spaces.manage").AsTask().WaitAsync(Bound);
            Assert.True(spaces.Controller.State.IsManaging);
            Assert.Empty(spaces.Controller.State.CustomSpaces);
            Assert.Same(mounted, spaces.SpaceWorkspaceContent);

            // Return restores the SAME retained physical page/conversation/draft.
            await spaces.DispatchAsync("spaces.manage").AsTask().WaitAsync(Bound);
            Assert.False(spaces.Controller.State.IsManaging);
            Assert.Same(mounted, spaces.SpaceWorkspaceContent);
            Assert.Equal(mountedId, mounted.ConversationId);
            Assert.Equal("unsent preserved draft", mounted.ComposerInput.Text);
            Assert.Same(spaces, root.Content);
        }
        finally
        {
            if (session is not null)
                try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
            try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Real_navigation_closes_manager_and_reveals_the_new_chat()
    {
        using var rig = new Rig(withHome: true, allowConversationWrites: true);
        SpacesBrowseAppContentSession? session = null;
        try
        {
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            session = rig.Session("9to1://spaces", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);

            await spaces.DispatchAsync("spaces.open", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var first = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            var firstId = first.ConversationId;

            // Open the manager, then navigate via a REAL new-chat publication. The manager must
            // exit only after the publication succeeds, revealing the newly mounted chat.
            await spaces.DispatchAsync("spaces.manage").AsTask().WaitAsync(Bound);
            Assert.True(spaces.Controller.State.IsManaging);

            await spaces.DispatchAsync("spaces.new-chat", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);

            Assert.False(spaces.Controller.State.IsManaging);
            var second = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.NotEqual(firstId, second.ConversationId);
            Assert.Equal(SpaceRegistry.ChatSpaceId, second.CurrentConversation.SpaceId);
            Assert.Same(spaces, root.Content);
        }
        finally
        {
            if (session is not null)
                try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
            try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Refused_real_navigation_keeps_manager_and_old_mounted_page()
    {
        using var rig = new Rig(withHome: true, allowConversationWrites: true);
        SpacesBrowseAppContentSession? session = null;
        try
        {
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            session = rig.Session("9to1://spaces", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);

            await spaces.DispatchAsync("spaces.open", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var mounted = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);

            // Open the manager, then attempt a real conversation navigation that genuinely refuses
            // (nonexistent conversation identity). Admission is not publication: the manager and
            // the old mounted page must both remain.
            await spaces.DispatchAsync("spaces.manage").AsTask().WaitAsync(Bound);
            Assert.True(spaces.Controller.State.IsManaging);

            await spaces.DispatchAsync("spaces.open-conversation", Guid.NewGuid()).AsTask().WaitAsync(Bound);
            Assert.Contains("no longer available", spaces.Controller.State.StatusMessage);
            Assert.True(spaces.Controller.State.IsManaging);
            Assert.Same(mounted, spaces.SpaceWorkspaceContent);
            Assert.Same(spaces, root.Content);
        }
        finally
        {
            if (session is not null)
                try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
            try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
        }
    }

    private static NewChatPage Chat(SpacesBrowseAppContentSession session) =>
        (NewChatPage)typeof(SpacesBrowseAppContentSession).GetField("_chatPage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

    [AvaloniaFact]
    public async Task Public_spaces_open_and_draft_can_retire_without_pre_draining_page_originals()
    {
        using var rig = new Rig(withHome: true, allowConversationWrites: true);
        var failures = new List<Exception>();
        SpacesBrowseAppContentSession? session = null;
        try
        {
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            session = rig.Session("9to1://spaces", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.DispatchAsync("spaces.open", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            // The conversation is mounted inside the retained Spaces Home rail layout rather than
            // replacing the tab's root view, which is what keeps the rail and chat sidebar together.
            var chat = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            chat.SetDraft("Unsent close-boundary fixture");
            Assert.True(await session.TryPrepareToCloseAsync().WaitAsync(Bound));

            // The real owner close must join its own admitted work. Do not run the
            // settled-route SystemIdle helper or pre-join Chat originals here.
            var close = session.CloseAndDrainAsync();
            Assert.Same(close, session.OriginalClose);
            await close.WaitAsync(Bound);
            Assert.True(session.CanRetireOriginalView);
            Assert.False(session.IsPublicationCurrent);
            Assert.Null(root.Content);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            try { if (session is not null) await session.CloseAndDrainAsync().WaitAsync(Bound); }
            catch (Exception failure) { failures.Add(failure); }
            try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Spaces close boundary and independent cleanup failed.", failures);
    }

    private static async Task CloseSettledRouteAsync(SpacesBrowseAppContentSession session)
    {
        // These checks concern a settled restored page. Let its genuinely queued
        // UI publication finish before ordinary cleanup; do not waive close faults.
        // Immediate close currently exposes a separate base Chat scroll regression,
        // preserved in the first unique failed run rather than claimed fixed here.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
        await session.CloseAndDrainAsync().WaitAsync(Bound);
    }

    internal sealed class Rig : IDisposable
    {
        internal Conversation? Row = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Saved fixture", null, null,
            false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        // Conversations the session actually persisted during the test, keyed by real identity.
        internal readonly Dictionary<Guid, Conversation> Written = new();
        internal readonly SpaceRegistry Spaces;
        internal Func<Guid, Task<IReadOnlyList<ChatMessage>>>? ReadMessages;
        internal readonly ChatMessage SavedMessage;
        internal int ConversationWrites, ModelReads;
        private readonly ServiceProvider _services;
        internal HomeNativeWindowsComposition? Home { get; }
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "9to1-space-route-" + Guid.NewGuid().ToString("N"));
        internal Rig(bool withHome = false, bool allowConversationWrites = false, IVersionedSettingsStore? settings = null,
            InstalledExtensionPackage[]? extensionPackages = null,
            Func<CancellationToken, Task<IReadOnlyList<InstalledExtensionPackage>>>? getInstalledAsync = null)
        {
            Directory.CreateDirectory(_directory);
            Spaces = new(settings ?? new Settings());
            SavedMessage = new(Guid.NewGuid(), Row!.Id, MessageRole.User, "Persisted history fixture", null, null, null, DateTimeOffset.UnixEpoch);
            var conversations = Proxy<IConversationRepository>((name, args) => name switch
            {
                "GetAsync" => Task.FromResult(Written.TryGetValue((Guid)args![0]!, out var stored) ? stored
                    : Row is { } row && row.Id == (Guid)args![0]! ? row : null),
                // The contextual Space sidebar reads the selected Space's own conversations. The
                // fixture still controls all I/O: a row only appears under the Space it names.
                "GetBySpaceAsync" => Task.FromResult<IReadOnlyList<Conversation>>(
                    Written.Values.Where(row => row.SpaceId == (Guid)args![0]!).Concat(
                        Row is { } scoped && scoped.SpaceId == (Guid)args![0]! && !Written.ContainsKey(scoped.Id)
                            ? [scoped] : Array.Empty<Conversation>()).ToArray()),
                "GetMessagesAsync" => ReadMessages?.Invoke((Guid)args![0]!) ?? Task.FromResult<IReadOnlyList<ChatMessage>>([SavedMessage with { ConversationId = Row!.Id }]),
                "GetContextEntriesAsync" => Task.FromResult<IReadOnlyList<ConversationContextEntry>>([]),
                "UpsertConversationAsync" => allowConversationWrites ? RecordConversationWrite((Conversation)args![0]!) : RecordUnexpectedWrite(),
                _ => throw Unexpected(name)
            });
            var ollama = Proxy<IOllamaClient>((name, _) => name == "GetModelsAsync" ? Models() : throw Unexpected(name));
            var safety = Proxy<IConversationSafetyService>((name, args) => name == "GetSnapshotAsync"
                ? Task.FromResult(new ConversationSafetySnapshot((Guid)args![0]!, 0, ConversationSafetyState.Active, null, 0)) : throw Unexpected(name));
            var paths = Proxy<IAppPaths>((name, _) => name switch
            {
                "get_DataDirectory" => _directory,
                "get_DatabasePath" => Path.Combine(_directory, "haven.db"),
                "get_BrowserProfileDirectory" => Path.Combine(_directory, "browser"),
                "get_AttachmentsDirectory" => Path.Combine(_directory, "attachments"),
                "get_LogsDirectory" => Path.Combine(_directory, "logs"),
                "get_LegacyStatePath" => Path.Combine(_directory, "legacy"),
                _ => throw Unexpected(name)
            });
            var instances = new GenUiInstanceStore();
            var services = new ServiceCollection().AddSingleton(new HavenEventBus()).AddSingleton(conversations)
                .AddSingleton(ollama).AddSingleton(safety).AddSingleton(Spaces)
                .AddSingleton(Proxy<IConversationVersioningService>()).AddSingleton(new UserPreferencesService(paths))
                .AddSingleton(new ChatSessionService(conversations, ollama, new CapabilityPreflightService(), safety,
                    new WorkspaceToolRuntime(Proxy<IWorkspaceToolService>()), new ComputerToolRuntime(Proxy<IComputerToolService>())))
                .AddSingleton(new GenUiLocalActionRegistry()).AddSingleton(instances)
                .AddSingleton(new GenerativeUiEventRouter([], Proxy<IGenUiEventAuditSink>(), instances))
                .AddSingleton(new CapabilityRegistryService(Proxy<ICapabilityRepository>((name, _) => name == "GetCapabilitiesAsync"
                    ? Task.FromResult<IReadOnlyList<CapabilityDefinition>>([]) : throw Unexpected(name))))
                .AddSingleton(Proxy<ICatalogRepository>((name, _) => name switch
                {
                    "GetAgentsAsync" => Task.FromResult<IReadOnlyList<AgentDefinition>>([]),
                    "GetPromptsAsync" => Task.FromResult<IReadOnlyList<PromptDefinition>>([]),
                    _ => throw Unexpected(name)
                })).AddSingleton(Proxy<IModeRegistry>((name, _) => name == "GetModesAsync"
                    ? Task.FromResult<IReadOnlyList<ModeDefinition>>([]) : throw Unexpected(name)));
            foreach (var type in new[] { typeof(CalculatorTemplateRuntime), typeof(StructuredFormTemplateRuntime), typeof(ChoicePromptTemplateRuntime),
                typeof(ChecklistTemplateRuntime), typeof(DataGridTemplateRuntime), typeof(CardDeckTemplateRuntime), typeof(GraphTemplateRuntime),
                typeof(TaskListTemplateRuntime), typeof(DashboardTemplateRuntime), typeof(AssessmentTemplateRuntime), typeof(WorkflowTemplateRuntime), typeof(CustomTemplateRuntime) })
                services.AddSingleton(type);
            if (withHome)
            {
                Home = new(new FileHomeCoreStateStore(Path.Combine(_directory, "Home", "home-core-state.json")),
                    new OperatingSystemPrincipalSource(), paths, new("spaces.route.test." + Guid.NewGuid().ToString("N")));
                services.AddSingleton(Home);
            }
            if (extensionPackages is not null || getInstalledAsync is not null)
            {
                // A real ExtensionManager over a controlled extension repository, so the sidebar
                // projection reads the same manifest data the shipping surface uses.
                var extensionRepository = Proxy<IExtensionRepository>((name, args) => name switch
                {
                    "GetInstalledAsync" => getInstalledAsync is not null
                        ? getInstalledAsync((CancellationToken)args![0]!)
                        : Task.FromResult<IReadOnlyList<InstalledExtensionPackage>>(extensionPackages!),
                    _ => throw Unexpected(name)
                });
                services.AddSingleton(new ExtensionManager(extensionRepository,
                    Proxy<IExtensionSourceTransport>(), new ExtensionManifestValidator(),
                    new NativePluginRuntime(Proxy<ICapabilityRepository>(), Proxy<ICatalogRepository>(),
                        Proxy<INativePluginProcessFactory>(), Proxy<IExecutionEventSink>(), extensionRepository),
                    paths));
            }
            _services = services.BuildServiceProvider();
        }
        private Task RecordUnexpectedWrite() { ConversationWrites++; throw Unexpected("Creating or modifying a saved conversation during restore"); }
        private Task RecordConversationWrite(Conversation conversation)
        {
            ConversationWrites++;
            Written[conversation.Id] = conversation;
            Row = conversation;
            return Task.CompletedTask;
        }
        private Task<IReadOnlyList<ModelDescriptor>> Models() { ModelReads++; return Task.FromResult<IReadOnlyList<ModelDescriptor>>([]); }
        internal SpacesBrowseAppContentSession Session(string address, Guid? tabId = null, string profileId = "profile",
            BrowserTabPrivacy privacy = BrowserTabPrivacy.Standard,
            Func<BrowseNavigationIntent, Task, bool>? queueNavigation = null) => new(
            new BrowseAppContentRequest(tabId ?? Guid.NewGuid(), AppContentAddress.Parse(address), profileId, privacy),
            _services, CancellationToken.None, queueNavigation);
        public void Dispose() { _services.Dispose(); Directory.Delete(_directory, true); }
    }

    private sealed class Settings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class => Task.FromResult(_values.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw Unexpected("Export");
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw Unexpected("Import");
    }
    private static InvalidOperationException Unexpected(string name) => new("Unexpected route fixture I/O: " + name);
    private static T Proxy<T>(Func<string, object?[]?, object?>? invoke = null) where T : class
    {
        var value = DispatchProxy.Create<T, ControlledIo>();
        ((ControlledIo)(object)value).Call = invoke ?? ((name, _) => throw Unexpected(name));
        return value;
    }
    public class ControlledIo : DispatchProxy
    {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }
}
