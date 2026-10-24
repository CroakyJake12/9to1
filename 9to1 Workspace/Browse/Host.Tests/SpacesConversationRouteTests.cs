using HistoryRow = HavenOS.Apps.Spaces.Home.SpacesHomeConversationRow;
using Avalonia.VisualTree;
using Avalonia.Automation;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
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
using HavenOS.Apps.Spaces;
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

    /// <summary>
    /// The native13 false-empty regression: on initial restored-Chat startup, the selected Space's
    /// REAL recent history must be projected from the canonical repository at the admitted bootstrap
    /// boundary, BEFORE any manager toggle, extra selection call, or explicit refresh. Stored IDs and
    /// the current marker are asserted, and unrelated-Space/archived/temporary entries are excluded.
    /// </summary>
    [AvaloniaFact]
    public async Task Initial_restored_chat_startup_publishes_real_selected_space_history()
    {
        using var rig = new Rig(withHome: true);
        var failures = new List<Exception>();
        SpacesBrowseAppContentSession? session = null;
        try
        {
            // A genuine saved selected-Space/Chat route requires a real, non-archived Space.
            var space = await rig.Spaces.CreateAsync("History fixture");
            var spaceId = space.Id;

            // Seed the controlled canonical repository fixture with stable identities: two live
            // conversations in the selected Space, one archived, one temporary, one in another Space.
            var restored = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
                "Restored chat", null, null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                SpaceId: spaceId);
            var recent = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
                "Recent chat", null, null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                SpaceId: spaceId);
            var archived = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
                "Archived chat", null, null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                IsArchived: true, SpaceId: spaceId);
            var temporary = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
                "Temporary chat", null, null, false, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                SpaceId: spaceId);
            var unrelated = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
                "Other-space chat", null, null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                SpaceId: Guid.NewGuid());

            rig.Row = restored;
            rig.Written[restored.Id] = restored;
            rig.Written[recent.Id] = recent;
            rig.Written[archived.Id] = archived;
            rig.Written[temporary.Id] = temporary;
            rig.Written[unrelated.Id] = unrelated;

            // Real process-local Linux Home admission in an isolated directory, then the public
            // init path over the saved selected-Space/Chat route.
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            session = rig.Session($"9to1://spaces/selected/{spaceId:D}/chat/{restored.Id:D}", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);

            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);

            // Assert the projected history IMMEDIATELY after init, before any manager toggle, extra
            // selection call, or explicit refresh.
            Assert.True(spaces.TryGetValue("spaces.conversations", out var projected));
            var stored = Assert.IsAssignableFrom<IReadOnlyList<SpacesHomeConversationRow>>(projected);
            Assert.True(spaces.TryGetValue("spaces.hasConversations", out var hasHistory));
            Assert.Equal(true, hasHistory);
            Assert.Contains(stored, row => row.Id == restored.Id && row.Title == restored.Title && row.IsCurrent);
            Assert.Contains(stored, row => row.Id == recent.Id && row.Title == recent.Title && !row.IsCurrent);
            Assert.Equal(2, stored.Count); // Only the two live rows of the selected Space.
            Assert.DoesNotContain(stored, row => row.Id == archived.Id);
            Assert.DoesNotContain(stored, row => row.Id == temporary.Id);
            Assert.DoesNotContain(stored, row => row.Id == unrelated.Id);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            try { if (session is not null) await CloseSettledRouteAsync(session); }
            catch (Exception failure) { failures.Add(failure); }
            try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Initial Spaces history and independent cleanup failed.", failures);
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

    // The owner's Space entry is deliberately different from explicit conversation history.
    // Exercise the same dispatcher used by the rail and Open control, including revisiting a Space.
    [AvaloniaTheory]
    [InlineData("spaces.select")]
    [InlineData("spaces.open")]
    public async Task Opening_or_selecting_a_Space_always_starts_a_fresh_chat_and_retains_prior_work(string command)
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
            await spaces.DispatchAsync(command, SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var first = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            var firstId = first.ConversationId;
            var original = rig.Written[firstId];
            first.SetDraft("Retained unsent draft while selecting another new chat");

            await spaces.DispatchAsync(command, SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var second = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.NotSame(first, second);
            Assert.NotEqual(firstId, second.ConversationId);
            Assert.Equal(SpaceRegistry.ChatSpaceId, second.CurrentConversation.SpaceId);
            Assert.Equal(SpaceRegistry.ChatSpaceId, spaces.Controller.State.Selection?.Id);
            Assert.Equal(original, rig.Written[firstId]);
            Assert.Equal(firstId, first.ConversationId);
            Assert.Equal("Retained unsent draft while selecting another new chat", RouteScene(first).Input.Text);
            Assert.Equal(string.Empty, RouteScene(second).Input.Text);
            Assert.Same(spaces, root.Content); // Same authored rail + contextual sidebar owner.

            await spaces.DispatchAsync(command, SpaceRegistry.StudySpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var study = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.Equal(SpaceRegistry.StudySpaceId, study.CurrentConversation.SpaceId);
            await spaces.DispatchAsync(command, SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var returning = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.DoesNotContain(returning.ConversationId, new[] { firstId, second.ConversationId, study.ConversationId });
            Assert.Equal(SpaceRegistry.ChatSpaceId, returning.CurrentConversation.SpaceId);
            Assert.Equal("Retained unsent draft while selecting another new chat", RouteScene(first).Input.Text);

            // History remains an explicit request for the exact saved identity, not another Fresh.
            var writesBeforeRestore = rig.ConversationWrites;
            await spaces.DispatchAsync("spaces.open-conversation", firstId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            Assert.Equal(firstId, Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent).ConversationId);
            Assert.Equal(writesBeforeRestore, rig.ConversationWrites);
            Assert.Same(spaces, root.Content);
            await session.CloseAndDrainAsync().WaitAsync(Bound);
            Assert.True(session.CanRetireOriginalView);
            Assert.Null(root.Content);
        }
        finally
        {
            try { if (session is not null) await session.CloseAndDrainAsync().WaitAsync(Bound); }
            finally { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
        }
    }

    [AvaloniaFact]
    public async Task Open_without_a_parameter_starts_a_fresh_chat_in_the_current_selection_and_keeps_prior_work()
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
            await spaces.DispatchAsync("spaces.select", SpaceRegistry.StudySpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var first = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            var original = rig.Written[first.ConversationId];
            first.SetDraft("Retained Study draft while opening the current selection");
            var previousNavigation = session.OriginalQueuedNavigation;

            await spaces.DispatchAsync("spaces.open", null).AsTask().WaitAsync(Bound);
            Assert.NotSame(previousNavigation, session.OriginalQueuedNavigation);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var next = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.NotSame(first, next);
            Assert.NotEqual(first.ConversationId, next.ConversationId);
            Assert.Equal(SpaceRegistry.StudySpaceId, next.CurrentConversation.SpaceId);
            Assert.Equal(SpaceRegistry.StudySpaceId, spaces.Controller.State.Selection?.Id);
            Assert.Equal(SpaceRegistry.StudySpaceId, await rig.Spaces.GetCurrentSpaceIdAsync());
            Assert.Equal(original, rig.Written[first.ConversationId]);
            Assert.Equal("Retained Study draft while opening the current selection", RouteScene(first).Input.Text);
            Assert.Equal(string.Empty, RouteScene(next).Input.Text);
            Assert.Same(spaces, root.Content);
        }
        finally
        {
            try { if (session is not null) await CloseSettledRouteAsync(session); }
            finally { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
        }
    }

    [AvaloniaFact]
    public async Task Explicit_unavailable_Space_open_never_changes_selection_navigation_or_prior_work()
    {
        using var rig = new Rig(withHome: true, allowConversationWrites: true);
        SpacesBrowseAppContentSession? session = null;
        try
        {
            var archived = await rig.Spaces.CreateAsync("Archived Open target");
            await rig.Spaces.SetArchivedAsync(archived.Id, true);
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            session = rig.Session("9to1://spaces", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.DispatchAsync("spaces.select", SpaceRegistry.StudySpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var mounted = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            mounted.SetDraft("Keep this Study draft when another Space is unavailable");
            var selection = spaces.Controller.State.Selection;
            var previousNavigation = session.OriginalQueuedNavigation;
            var previousAddress = session.Address.Value;
            var previousWrites = rig.ConversationWrites;
            var previousRows = rig.Written.OrderBy(pair => pair.Key).ToArray();

            foreach (var unavailable in new[] { Guid.Empty, Guid.NewGuid(), archived.Id })
            {
                await spaces.DispatchAsync("spaces.open", unavailable).AsTask().WaitAsync(Bound);
                Assert.Same(previousNavigation, session.OriginalQueuedNavigation);
                Assert.Same(selection, spaces.Controller.State.Selection);
                Assert.Equal(SpaceRegistry.StudySpaceId, await rig.Spaces.GetCurrentSpaceIdAsync());
                Assert.Same(mounted, spaces.SpaceWorkspaceContent);
                Assert.Equal(previousAddress, session.Address.Value);
                Assert.Equal(previousWrites, rig.ConversationWrites);
                Assert.Equal(previousRows, rig.Written.OrderBy(pair => pair.Key).ToArray());
                Assert.Equal("Keep this Study draft when another Space is unavailable", RouteScene(mounted).Input.Text);
                Assert.Contains("unavailable", spaces.Controller.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
                Assert.Same(spaces, root.Content);
            }
        }
        finally
        {
            try { if (session is not null) await CloseSettledRouteAsync(session); }
            finally { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
        }
    }

    [AvaloniaFact]
    public async Task Saved_chat_rendered_model_recovery_is_scoped_to_the_current_page_and_keeps_prior_work()
    {
        using var rig = new Rig(withHome: true, allowConversationWrites: true);
        SpacesBrowseAppContentSession? session = null;
        Window? window = null;
        var navigations = new List<(BrowseNavigationIntent Intent, Task Settlement)>();
        try
        {
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            var original = rig.Row!;
            var tabId = Guid.NewGuid();
            session = rig.Session($"9to1://spaces/chat/{original.Id:D}", tabId, actor.ProfileId,
                queueNavigation: (intent, settlement) => { navigations.Add((intent, settlement)); return true; });
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            var chat = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            var scene = RouteScene(chat);
            window = new Window { Content = root, Width = 1280, Height = 900 };
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(ChatModelAvailability.Unavailable, chat.ModelAvailability);
            chat.SetDraft("Saved chat local-model recovery draft");
            var button = scene.RecoveryView.GetVisualDescendants().OfType<Button>()
                .Single(value => AutomationProperties.GetAutomationId(value) == "recovery-manage-button");
            Assert.True(button.IsEffectivelyVisible);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await scene.RecoveryView.CaptureOriginalActionSettlement().WaitAsync(Bound);
            var navigation = Assert.Single(navigations);
            await navigation.Settlement.WaitAsync(Bound);
            Assert.Equal(tabId, navigation.Intent.SourceTabId);
            Assert.Equal("browse://home/models", navigation.Intent.Address.Value);
            Assert.True(navigation.Intent.OpenInNewTab);
            Assert.Same(chat, spaces.SpaceWorkspaceContent);
            Assert.Equal(original.Id, chat.ConversationId);
            Assert.Equal("Saved chat local-model recovery draft", scene.Input.Text);
            Assert.Equal(ChatModelAvailability.Unavailable, chat.ModelAvailability);
            Assert.Equal(0, rig.ConversationWrites);
            Assert.Equal($"9to1://spaces/chat/{original.Id:D}", session.Address.Value);

            // A fresh chat replaces the published route while the prior owned page stays alive.
            // Retaining that old control's text proves live-page preservation only, not a saved
            // draft or restart guarantee; the existing canonical draft suites own those checks.
            await spaces.DispatchAsync("spaces.open", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var current = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.NotSame(chat, current);
            Assert.NotEqual(original.Id, current.ConversationId);
            var writesAfterFreshOpen = rig.ConversationWrites;
            var stale = Assert.Throws<SpacesWorkspaceActionRefusal>(chat.RequestManageLocalModels);
            Assert.Contains("no longer publishes this Chat page", stale.Message, StringComparison.Ordinal);
            Assert.Single(navigations); // The inactive old page must never queue a host action.
            Assert.Equal(writesAfterFreshOpen, rig.ConversationWrites);
            Assert.Same(current, spaces.SpaceWorkspaceContent);
            Assert.Equal(original.Id, chat.ConversationId);
            Assert.Equal("Saved chat local-model recovery draft", scene.Input.Text);

            // The current page still routes the same genuine recovery action after that refusal.
            current.RequestManageLocalModels();
            Assert.Equal(2, navigations.Count);
            await navigations[1].Settlement.WaitAsync(Bound);
            Assert.Equal(tabId, navigations[1].Intent.SourceTabId);
            Assert.Equal("browse://home/models", navigations[1].Intent.Address.Value);
            Assert.True(navigations[1].Intent.OpenInNewTab);
            Assert.Same(current, spaces.SpaceWorkspaceContent);
            Assert.Equal(writesAfterFreshOpen, rig.ConversationWrites);
        }
        finally
        {
            try { if (session is not null) await session.CloseAndDrainAsync().WaitAsync(Bound); }
            finally { window?.Close(); await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
        }
    }

    [AvaloniaFact]
    public async Task Saved_chat_model_recovery_without_a_host_route_reports_refusal_and_preserves_the_saved_target()
    {
        using var rig = new Rig(withHome: true);
        SpacesBrowseAppContentSession? session = null;
        try
        {
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            var original = rig.Row!;
            session = rig.Session($"9to1://spaces/chat/{original.Id:D}", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var spaces = Assert.IsType<SpacesHomePage>(Assert.IsType<ContentControl>(session.OriginalNativeView).Content);
            var chat = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            chat.SetDraft("Unrouted saved-chat recovery draft");
            var refusal = Assert.Throws<SpacesWorkspaceActionRefusal>(chat.RequestManageLocalModels);
            Assert.Contains("No host navigation seam", refusal.Message, StringComparison.Ordinal);
            Assert.Equal(original.Id, chat.ConversationId);
            Assert.Equal("Unrouted saved-chat recovery draft", RouteScene(chat).Input.Text);
            Assert.Same(chat, spaces.SpaceWorkspaceContent);
            Assert.Equal(0, rig.ConversationWrites);
        }
        finally
        {
            try { if (session is not null) await session.CloseAndDrainAsync().WaitAsync(Bound); }
            finally { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
        }
    }

    private static ChatHavenScene RouteScene(NewChatPage page) =>
        (ChatHavenScene)typeof(NewChatPage).GetField("_scene", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

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

    [AvaloniaFact]
    public async Task Manager_return_preserves_exact_mounted_page_conversation_and_draft()
    {
        using var rig = new Rig(withHome: true, allowConversationWrites: true);
        var failures = new List<Exception>();
        SpacesBrowseAppContentSession? session = null;
        Window? window = null;
        try
        {
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            session = rig.Session("9to1://spaces", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            window = new Window { Width = 1100, Height = 800, Content = root };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // Mount the real chat in the actual root before observing its physical input.
            await spaces.DispatchAsync("spaces.open", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            var mounted = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            var mountedId = mounted.ConversationId;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var physicalInput = RenderedComposerTextBox(mounted);
            Assert.True(physicalInput.Bounds.Width > 0 && physicalInput.Bounds.Height > 0);
            var physicalMount = spaces.SpaceWorkspaceMount;
            Assert.NotNull(physicalMount);
            const string draft = "alpha\nbeta";
            mounted.SetDraft(draft);
            Assert.Equal(draft, mounted.ComposerInput.Text);
            Assert.Equal(draft, physicalInput.Text);

            // Open the manager: presentation-only, nothing created, the same page stays mounted.
            await spaces.DispatchAsync("spaces.manage", null).AsTask().WaitAsync(Bound);
            Assert.True(spaces.Controller.State.IsManaging);
            Assert.Empty(spaces.Controller.State.CustomSpaces);
            Assert.Same(mounted, spaces.SpaceWorkspaceContent);

            // Return restores the SAME retained physical page/conversation/draft.
            await spaces.DispatchAsync("spaces.manage", null).AsTask().WaitAsync(Bound);
            Assert.False(spaces.Controller.State.IsManaging);
            Assert.Same(mounted, spaces.SpaceWorkspaceContent);
            Assert.Equal(mountedId, mounted.ConversationId);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(draft, mounted.ComposerInput.Text);
            Assert.Same(physicalInput, RenderedComposerTextBox(mounted));
            Assert.Equal(draft, physicalInput.Text);
            Assert.Same(physicalMount, spaces.SpaceWorkspaceMount);
            Assert.Same(spaces, root.Content);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            try { if (session is not null) await CloseSettledRouteAsync(session); }
            catch (Exception failure) { failures.Add(failure); }
            try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
            catch (Exception failure) { failures.Add(failure); }
            try { window?.Close(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Spaces manager navigation and independent cleanup failed.", failures);
    }

    [AvaloniaFact]
    public async Task Real_navigation_closes_manager_and_reveals_the_new_chat()
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
            var first = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            var firstId = first.ConversationId;
            var visiblePublications = new List<(Guid ConversationId, string Address)>();
            session.StateChanged += (_, _) =>
            {
                if (!spaces.Controller.State.IsManaging && spaces.SpaceWorkspaceContent is NewChatPage visible)
                    visiblePublications.Add((visible.ConversationId, session.Address.Value));
            };

            // Open the manager, then navigate via a REAL new-chat publication. The manager must
            // exit only after the publication succeeds, revealing the newly mounted chat.
            await spaces.DispatchAsync("spaces.manage", null).AsTask().WaitAsync(Bound);
            Assert.True(spaces.Controller.State.IsManaging);

            await spaces.DispatchAsync("spaces.new-chat", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);

            Assert.False(spaces.Controller.State.IsManaging);
            var second = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.NotEqual(firstId, second.ConversationId);
            Assert.Equal(SpaceRegistry.ChatSpaceId, second.CurrentConversation.SpaceId);
            Assert.Same(spaces, root.Content);
            Assert.NotEmpty(visiblePublications);
            Assert.All(visiblePublications, observed => Assert.Equal(
                $"9to1://spaces/selected/{SpaceRegistry.ChatSpaceId:D}/chat/{observed.ConversationId:D}", observed.Address));

            // Ordinary Space Open also creates a fresh canonical chat, then exits management.
            // The previous owned page retains its identity and live draft; that is distinct from
            // repository/history/restart draft persistence, which this controlled rig does not prove.
            var secondId = second.ConversationId;
            second.SetDraft("Prior chat draft retained across manager navigation");
            var writesBeforeFreshOpen = rig.ConversationWrites;
            await spaces.DispatchAsync("spaces.manage", null).AsTask().WaitAsync(Bound);
            Assert.True(spaces.Controller.State.IsManaging);
            await spaces.DispatchAsync("spaces.open", SpaceRegistry.ChatSpaceId).AsTask().WaitAsync(Bound);
            await session.OriginalQueuedNavigation!.WaitAsync(Bound);
            Assert.False(spaces.Controller.State.IsManaging);
            var third = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.NotSame(second, third);
            Assert.NotEqual(firstId, third.ConversationId);
            Assert.NotEqual(secondId, third.ConversationId);
            Assert.Equal(SpaceRegistry.ChatSpaceId, third.CurrentConversation.SpaceId);
            Assert.Equal(secondId, second.ConversationId);
            Assert.Equal("Prior chat draft retained across manager navigation", RouteScene(second).Input.Text);
            Assert.Equal(SpaceRegistry.ChatSpaceId, rig.Written[secondId].SpaceId);
            Assert.Equal(third.ConversationId, rig.Written[third.ConversationId].Id);
            Assert.Same(spaces, root.Content);
            Assert.True(rig.ConversationWrites > writesBeforeFreshOpen);
            Assert.Contains(visiblePublications, observed => observed.ConversationId == third.ConversationId &&
                observed.Address == session.Address.Value);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            try { if (session is not null) await CloseSettledRouteAsync(session); }
            catch (Exception failure) { failures.Add(failure); }
            try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Spaces manager navigation and independent cleanup failed.", failures);
    }

    [AvaloniaFact]
    public async Task Refused_real_navigation_keeps_manager_and_old_mounted_page()
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
            var mounted = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);

            // Open the manager, then attempt a real conversation navigation that genuinely refuses
            // (nonexistent conversation identity). Admission is not publication: the manager and
            // the old mounted page must both remain.
            await spaces.DispatchAsync("spaces.manage", null).AsTask().WaitAsync(Bound);
            Assert.True(spaces.Controller.State.IsManaging);

            await spaces.DispatchAsync("spaces.open-conversation", Guid.NewGuid()).AsTask().WaitAsync(Bound);
            Assert.Contains("no longer available", spaces.Controller.State.StatusMessage);
            Assert.True(spaces.Controller.State.IsManaging);
            Assert.Same(mounted, spaces.SpaceWorkspaceContent);
            Assert.Same(spaces, root.Content);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            try { if (session is not null) await CloseSettledRouteAsync(session); }
            catch (Exception failure) { failures.Add(failure); }
            try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Spaces manager navigation and independent cleanup failed.", failures);
    }

    private static Avalonia.Controls.TextBox RenderedComposerTextBox(NewChatPage page) => page
        .GetVisualDescendants().OfType<Avalonia.Controls.TextBox>()
        .Single(input => AutomationProperties.GetAutomationId(input) == "composer-input");

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
            // The actual factory requires the shared routed source; preserve the real caller token.
            var routed = Proxy<IProviderModelClient>((name, args) => name == "GetModelsAsync"
                ? Models((CancellationToken)args![0]!) : throw Unexpected(name));
            // Keep raw transport distinct: accidental raw discovery/chat is an unexpected I/O call.
            var ollama = Proxy<IOllamaClient>((name, _) => throw Unexpected("Raw Ollama: " + name));
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
                .AddSingleton<IProviderModelClient>(routed).AddSingleton<IOllamaClient>(ollama)
                .AddSingleton(safety).AddSingleton(Spaces)
                .AddSingleton(Proxy<IConversationVersioningService>()).AddSingleton(new UserPreferencesService(paths))
                .AddSingleton(new ChatSessionService(conversations, routed, new CapabilityPreflightService(), safety,
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
        private Task<IReadOnlyList<ModelDescriptor>> Models(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ModelReads++;
            return Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        }
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


/// <summary>Exercises the actual shared CUI history surface with controlled store reads.
/// These fixtures make no Home/provider/native-runtime acceptance claim.</summary>
public sealed class SpacesSavedTitleSearchTests
{
    private static readonly TimeSpan SearchBound = TimeSpan.FromSeconds(10);

    [AvaloniaFact]
    public async Task Explicit_CUI_search_and_clear_keep_the_same_mounted_owner_and_unsent_draft()
    {
        var calls = new List<(Guid Space, string Query)>();
        var match = new HistoryRow(Guid.NewGuid(), "Older saved revision plan", "1 Jan", false);
        await using var rig = await SearchRig.CreateAsync((space, query, _) =>
        {
            calls.Add((space, query));
            return Task.FromResult<IReadOnlyList<HistoryRow>>([match]);
        });
        var owner = new TextBox { Text = "Unsent owner draft" };
        Assert.True(rig.Page.TryMountSpaceWorkspace(owner));
        rig.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var queryInput = rig.Page.GetVisualDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetAutomationId(box) == "space-history-query");
        queryInput.Text = "revision"; // Exercise the actual CUI two-way input binding.
        Dispatcher.UIThread.RunJobs();
        await (rig.Page.LatestOriginalMutation ?? throw new InvalidOperationException("CUI did not issue a typed query update.")).WaitAsync(SearchBound);
        Assert.Equal("revision", Value(rig.Page, "spaces.conversationQuery"));
        Assert.Empty(calls); // Typing does not perform an implicit history request.
        var searchButton = rig.Page.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == "space-history-search-button");
        Assert.Equal("Search saved chat titles in this Space", AutomationProperties.GetName(searchButton));
        await SearchAsync(rig.Page);
        Assert.Equal((rig.SpaceA.Id, "revision"), Assert.Single(calls));
        Assert.Equal(match, Assert.Single(Rows(rig.Page)));
        Assert.Same(owner, rig.Page.SpaceWorkspaceContent);
        Assert.Equal("Unsent owner draft", owner.Text);
        await rig.Page.DispatchAsync("spaces.clear-conversation-search", Intent(rig.Page));
        Assert.Equal(string.Empty, Value(rig.Page, "spaces.conversationQuery"));
        Assert.Equal(rig.Recent, Assert.Single(Rows(rig.Page)));
        Assert.Single(calls);
        Assert.Same(owner, rig.Page.SpaceWorkspaceContent);
        Assert.Equal("Unsent owner draft", owner.Text);
    }

    [AvaloniaFact]
    public async Task Older_query_finishing_last_cannot_replace_newer_query_results()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<HistoryRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = new HistoryRow(Guid.NewGuid(), "Latest title", "2 Jan", false);
        await using var rig = await SearchRig.CreateAsync((_, query, _) =>
        {
            if (query == "old") { entered.TrySetResult(); return release.Task; }
            return Task.FromResult<IReadOnlyList<HistoryRow>>([latest]);
        });
        Task? first = null;
        try
        {
            await SetQueryAsync(rig.Page, "old");
            first = SearchAsync(rig.Page);
            await entered.Task.WaitAsync(SearchBound);
            await SetQueryAsync(rig.Page, "latest");
            await SearchAsync(rig.Page);
            release.SetResult([new HistoryRow(Guid.NewGuid(), "Old title", "1 Jan", false)]);
            await first.WaitAsync(SearchBound);
            Assert.Equal(latest, Assert.Single(Rows(rig.Page)));
            Assert.Equal("latest", Value(rig.Page, "spaces.conversationQuery"));
        }
        finally
        {
            release.TrySetResult([]);
            if (first is not null) await first.WaitAsync(SearchBound);
        }
    }

    [AvaloniaFact]
    public async Task Switching_Spaces_retires_old_title_results_and_resets_only_history_text()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<HistoryRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var rig = await SearchRig.CreateAsync((_, _, _) => { entered.TrySetResult(); return release.Task; });
        var owner = new TextBox { Text = "Keep this draft" };
        Assert.True(rig.Page.TryMountSpaceWorkspace(owner));
        Task? first = null;
        try
        {
            await SetQueryAsync(rig.Page, "private A title");
            first = SearchAsync(rig.Page);
            await entered.Task.WaitAsync(SearchBound);
            await rig.Page.Controller.SelectSpaceAsync(rig.SpaceB.Id);
            await rig.Page.RefreshConversationsAsync();
            release.SetResult([new HistoryRow(Guid.NewGuid(), "A must not leak", "1 Jan", false)]);
            await first.WaitAsync(SearchBound);
            Assert.Equal(string.Empty, Value(rig.Page, "spaces.conversationQuery"));
            Assert.Equal(rig.Recent, Assert.Single(Rows(rig.Page)));
            Assert.Equal(rig.SpaceB.Id, rig.Page.Controller.State.Selection!.Id);
            Assert.Same(owner, rig.Page.SpaceWorkspaceContent);
            Assert.Equal("Keep this draft", owner.Text);
        }
        finally { release.TrySetResult([]); if (first is not null) await first.WaitAsync(SearchBound); }
    }

    [AvaloniaFact]
    public async Task Clear_while_search_is_pending_keeps_recent_rows_when_old_read_settles()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<HistoryRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var rig = await SearchRig.CreateAsync((_, _, _) => { entered.TrySetResult(); return release.Task; });
        Task? first = null;
        try
        {
            await SetQueryAsync(rig.Page, "old"); first = SearchAsync(rig.Page);
            await entered.Task.WaitAsync(SearchBound);
            await rig.Page.DispatchAsync("spaces.clear-conversation-search", Intent(rig.Page));
            release.SetResult([new HistoryRow(Guid.NewGuid(), "Old result", "1 Jan", false)]);
            await first.WaitAsync(SearchBound);
            Assert.Equal(rig.Recent, Assert.Single(Rows(rig.Page)));
            Assert.Equal("Recent chats", Value(rig.Page, "spaces.conversationHistoryHeading"));
        }
        finally { release.TrySetResult([]); if (first is not null) await first.WaitAsync(SearchBound); }
    }

    [AvaloniaFact]
    public async Task Original_cancellation_is_observed_and_is_never_reported_as_no_matches()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<HistoryRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var rig = await SearchRig.CreateAsync((_, _, token) => { entered.TrySetResult(); return release.Task.WaitAsync(token); });
        rig.AllowExpectedReadFailure = true;
        using var cancellation = new CancellationTokenSource();
        await SetQueryAsync(rig.Page, "cancel");
        var read = rig.Page.DispatchAsync("spaces.search-conversations", Intent(rig.Page), cancellation.Token).AsTask();
        await entered.Task.WaitAsync(SearchBound);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(SearchBound));
        Assert.Empty(Rows(rig.Page));
        Assert.Equal("Chat history read was canceled.", Value(rig.Page, "spaces.conversationHistoryNotice"));
        Assert.DoesNotContain("No saved chat", Assert.IsType<string>(Value(rig.Page, "spaces.conversationHistoryEmpty")));
    }

    [AvaloniaFact]
    public async Task Unsupported_capability_is_unavailable_and_stale_issued_intent_cannot_search()
    {
        await using (var unsupported = await SearchRig.CreateAsync(null))
        {
            Assert.Equal(false, Value(unsupported.Page, "spaces.canSearchConversations"));
            Assert.False(unsupported.Page.IsActionAvailable("spaces.search-conversations"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => SearchAsync(unsupported.Page));
            Assert.Contains("unavailable", Assert.IsType<string>(Value(unsupported.Page, "spaces.conversationHistoryNotice")));
        }
        var calls = 0;
        await using var rig = await SearchRig.CreateAsync((_, _, _) => { calls++; return Task.FromResult<IReadOnlyList<HistoryRow>>([]); });
        await SetQueryAsync(rig.Page, "first");
        var stale = Intent(rig.Page);
        await SetQueryAsync(rig.Page, "second");
        rig.AllowExpectedReadFailure = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Page.DispatchAsync("spaces.search-conversations", stale).AsTask());
        Assert.Equal(0, calls);
        Assert.Equal("second", Value(rig.Page, "spaces.conversationQuery"));
    }

    [AvaloniaFact]
    public async Task Store_failure_retains_its_actual_cause_and_does_not_display_an_empty_success()
    {
        var cause = new IOException("Controlled original store failure");
        await using var rig = await SearchRig.CreateAsync((_, _, _) => Task.FromException<IReadOnlyList<HistoryRow>>(cause));
        rig.AllowExpectedReadFailure = true;
        await SetQueryAsync(rig.Page, "title");
        var actual = await Assert.ThrowsAsync<IOException>(() => SearchAsync(rig.Page));
        Assert.Same(cause, actual);
        Assert.Equal("Chat history could not be read. Try again.", Value(rig.Page, "spaces.conversationHistoryNotice"));
        Assert.DoesNotContain("No saved chat", Assert.IsType<string>(Value(rig.Page, "spaces.conversationHistoryEmpty")));
    }

    private static object? Value(SpacesHomePage page, string path)
    { Assert.True(page.TryGetValue(path, out var value)); return value; }
    private static IReadOnlyList<HistoryRow> Rows(SpacesHomePage page) => Assert.IsAssignableFrom<IReadOnlyList<HistoryRow>>(Value(page, "spaces.conversations"));
    private static SpacesHomePage.ConversationSearchIntent Intent(SpacesHomePage page) => Assert.IsType<SpacesHomePage.ConversationSearchIntent>(Value(page, "spaces.conversationSearchIntent"));
    private static Task SetQueryAsync(SpacesHomePage page, string query) => page.DispatchAsync("spaces.set-conversation-query", query).AsTask();
    private static Task SearchAsync(SpacesHomePage page) => page.DispatchAsync("spaces.search-conversations", Intent(page)).AsTask();

    private sealed class SearchRig : IAsyncDisposable
    {
        private SearchRig(SpacesHomePage page, Window window, SpaceDefinition a, SpaceDefinition b, HistoryRow recent)
        { Page = page; Window = window; SpaceA = a; SpaceB = b; Recent = recent; }
        public SpacesHomePage Page { get; }
        public Window Window { get; }
        public SpaceDefinition SpaceA { get; }
        public SpaceDefinition SpaceB { get; }
        public HistoryRow Recent { get; }
        public bool AllowExpectedReadFailure { get; set; }
        public static async Task<SearchRig> CreateAsync(Func<Guid, string, CancellationToken, Task<IReadOnlyList<HistoryRow>>>? search)
        {
            var registry = new SpaceRegistry(new SearchSettings());
            var a = await registry.CreateAsync("Search fixture A");
            var b = await registry.CreateAsync("Search fixture B");
            var recent = new HistoryRow(Guid.NewGuid(), "Recent fixture", "3 Jan", false);
            var page = new SpacesHomePage(registry, new SearchActionHost(), SpacesHomePage.ImmediateReadinessInstance,
                conversationsProvider: (_, _) => Task.FromResult<IReadOnlyList<HistoryRow>>([recent]), conversationSearchProvider: search);
            var window = new Window { Width = 1100, Height = 800, Content = page };
            window.Show();
            await page.ActivateAsync(CancellationToken.None).WaitAsync(SearchBound);
            await page.Controller.SelectSpaceAsync(a.Id);
            await page.RefreshConversationsAsync().WaitAsync(SearchBound);
            return new(page, window, a, b, recent);
        }
        public async ValueTask DisposeAsync()
        {
            var cause = await Record.ExceptionAsync(() => Page.CloseAndDrainAsync().WaitAsync(SearchBound));
            Window.Close();
            if (!AllowExpectedReadFailure) Assert.Null(cause);
        }
    }

    private sealed class SearchActionHost : ISpacesActionHost
    { public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class SearchSettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class => Task.FromResult(_values.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
