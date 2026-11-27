using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>The real Spaces/Home/NewChat/session graph with controlled canonical repository reads.
/// These are native headless route/presentation proofs, not installed-app or inference qualification.</summary>
public sealed class SpacesOrganisationRouteTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    [AvaloniaFact]
    public Task Authored_destinations_preserve_the_exact_chat_response_draft_and_return_owner() => WithRigAsync(async rig =>
    {
        var chat = rig.Chat!; chat.SetDraft("Keep this exact unsent draft.");
        var response = chat.GetLastAssistantResponse();
        foreach (var kind in new[] { "groups", "specialists" })
        {
            var button = Find<Button>(rig.Home, kind == "groups" ? "space-chat-groups" : "space-specialists");
            Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await rig.WaitForDestinationAsync(kind);
            var page = Assert.IsType<SpacesOrganisationPage>(rig.Home.SpaceWorkspaceContent);
            Assert.Equal($"9to1://spaces/selected/{SpaceRegistry.ChatSpaceId:D}/{kind}", rig.Session.Address.Value);
            Assert.Equal("Keep this exact unsent draft.", chat.ComposerInput.Text);
            Assert.Equal(response, chat.GetLastAssistantResponse());
            await page.DispatchAsync("organisation.return", null, rig.Token);
            Assert.Same(chat, rig.Home.SpaceWorkspaceContent);
            Assert.Equal("Keep this exact unsent draft.", chat.ComposerInput.Text);
            Assert.Equal(response, chat.GetLastAssistantResponse());
        }
        Assert.Equal(0, rig.Sources.Mutations); Assert.Equal(0, rig.Canonical.ConversationWrites);
    });

    [AvaloniaFact]
    public Task Group_members_are_scoped_and_open_the_exact_saved_route_after_original_settlement() => WithRigAsync(async rig =>
    {
        var page = await rig.OpenAsync("groups");
        await page.DispatchAsync("organisation.inspect-group", page.Groups[0].Target, rig.Token);
        Assert.Equal(rig.Sources.GroupA.Id, Assert.Single(page.Members).Group.Id);
        var member = Assert.Single(page.Members);
        Assert.Equal(rig.Sources.MemberA.Id, member.Conversation.Id);
        Assert.DoesNotContain(page.Members, item => item.Conversation.Id == rig.Sources.HiddenMember.Id);
        Assert.Contains("Only reviewed project material", Find<TextBlock>(page, "organisation-context").Text, StringComparison.Ordinal);
        Assert.Equal("Source reference", Find<TextBlock>(page, "organisation-source-name").Text);
        await page.DispatchAsync("organisation.open-member", member, rig.Token);
        var queued = Assert.Single(rig.Queued);
        await queued.Settlement.WaitAsync(Bound, rig.Token);
        Assert.True(queued.Intent.OpenInNewTab); Assert.True(queued.Settlement.IsCompletedSuccessfully);
        Assert.Equal($"9to1://spaces/selected/{SpaceRegistry.ChatSpaceId:D}/chat/{member.Conversation.Id:D}", queued.Intent.Address.Value);
        var destination = rig.Canonical.Session(queued.Intent.Address.Value, profileId: rig.Actor.ProfileId);
        rig.AdditionalSessions.Add(destination);
        await destination.InitializeAsync(rig.Token).WaitAsync(Bound, rig.Token);
        var home = Assert.IsType<SpacesHomePage>(Assert.IsType<ContentControl>(destination.OriginalNativeView).Content);
        Assert.Equal(member.Conversation.Id, Assert.IsType<NewChatPage>(home.SpaceWorkspaceContent).ConversationId);
        Assert.Same(page, rig.Home.SpaceWorkspaceContent);
        Assert.Equal(0, rig.Sources.Mutations); Assert.Equal(0, rig.Canonical.ConversationWrites);
    });

    [AvaloniaFact]
    public Task Changed_group_or_membership_refuses_open_without_navigation_or_replacing_the_chat() => WithRigAsync(async rig =>
    {
        var page = await rig.OpenAsync("groups");
        await page.DispatchAsync("organisation.inspect-group", page.Groups[0].Target, rig.Token);
        var member = Assert.Single(page.Members);
        rig.Sources.Members = rig.Sources.Members.Select(row => row.Id == member.Conversation.Id
            ? row with { ContainerId = rig.Sources.GroupB.Id } : row).ToArray();
        await page.DispatchAsync("organisation.open-member", member, rig.Token);
        Assert.Empty(rig.Queued);
        Assert.Contains("changed", Find<TextBlock>(page, "organisation-status").Text, StringComparison.Ordinal);
        await page.DispatchAsync("organisation.return", null, rig.Token);
        Assert.Same(rig.Chat, rig.Home.SpaceWorkspaceContent);
    });

    [AvaloniaFact]
    public Task Healthy_Home_revisions_allow_fresh_reads_and_exact_chat_return_but_fence_a_mixed_read() => WithRigAsync(async rig =>
    {
        rig.Chat!.SetDraft("Retain this draft through a healthy Home update.");
        var page = await rig.OpenAsync("groups");
        var runtime = rig.Canonical.Home!.Runtime;
        var before = runtime.Current.Revision;
        runtime.ReportServiceState("home.core", HomeServiceLifecycleState.Ready, true, "Healthy observation after destination opened");
        Assert.True(runtime.Current.Revision > before);
        rig.Sources.GroupA = rig.Sources.GroupA with { Context = "Fresh canonical group context" };
        await page.DispatchAsync("organisation.refresh", null, rig.Token);
        Assert.Equal("Fresh canonical group context", page.Groups[0].Definition.Context);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<ContainerDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Sources.ReadGroups = (_, _) => { entered.TrySetResult(); return release.Task; };
        var read = page.DispatchAsync("organisation.refresh", null, rig.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(Bound, rig.Token);
            runtime.ReportServiceState("home.core", HomeServiceLifecycleState.Ready, true, "Another healthy observation during the read");
            release.TrySetResult([rig.Sources.GroupA, rig.Sources.GroupB]);
            await read.WaitAsync(Bound, rig.Token);
            Assert.Empty(page.Groups);
            Assert.Contains("Reopen", Find<TextBlock>(page, "organisation-status").Text, StringComparison.Ordinal);
            Assert.True(page.TryGetValue("organisation.loading", out var loading)); Assert.Equal(false, loading);
        }
        finally { release.TrySetResult([rig.Sources.GroupA, rig.Sources.GroupB]); await read.WaitAsync(Bound, rig.Token); }
        rig.Sources.ReadGroups = null;
        await page.DispatchAsync("organisation.refresh", null, rig.Token);
        Assert.Equal(2, page.Groups.Count);
        await page.DispatchAsync("organisation.return", null, rig.Token);
        Assert.Same(rig.Chat, rig.Home.SpaceWorkspaceContent);
        Assert.Equal("Retain this draft through a healthy Home update.", rig.Chat.ComposerInput.Text);
        Assert.Equal(0, rig.Sources.Mutations); Assert.Equal(0, rig.Canonical.ConversationWrites);
    });

    [AvaloniaFact]
    public Task Required_Home_authority_and_changed_Space_policy_withdraw_actionable_rows_with_an_explicit_refusal() => WithRigAsync(async rig =>
    {
        var page = await rig.OpenAsync("groups");
        var runtime = rig.Canonical.Home!.Runtime;
        try
        {
            runtime.ReportServiceState("permissions.trust", HomeServiceLifecycleState.Unavailable, false, "Controlled permission authority revocation");
            await page.DispatchAsync("organisation.refresh", null, rig.Token);
            Assert.Empty(page.Groups); Assert.False(page.IsActionAvailable("organisation.open-member"));
            Assert.Contains("Reopen", Find<TextBlock>(page, "organisation-status").Text, StringComparison.Ordinal);
            Assert.True(page.TryGetValue("organisation.loading", out var loading)); Assert.Equal(false, loading);
        }
        finally { runtime.ReportServiceState("permissions.trust", HomeServiceLifecycleState.Ready, true); }
        await page.DispatchAsync("organisation.refresh", null, rig.Token);
        Assert.Equal(2, page.Groups.Count);
        var space = await rig.Canonical.Spaces.ReadExistingAsync(SpaceRegistry.ChatSpaceId, rig.Token);
        Assert.NotNull(space);
        var updated = await rig.Canonical.Spaces.UpdateAsync(space with { ModelName = "dulche-local:changed-space-policy" }, space.Revision, rig.Token);
        Assert.True(updated.Revision > space.Revision);
        await page.DispatchAsync("organisation.refresh", null, rig.Token);
        Assert.Empty(page.Groups); Assert.Empty(page.Members);
        Assert.False(page.IsActionAvailable("organisation.open-member"));
        Assert.Contains("Reopen", Find<TextBlock>(page, "organisation-status").Text, StringComparison.Ordinal);
        await page.DispatchAsync("organisation.return", null, rig.Token);
        Assert.Same(page, rig.Home.SpaceWorkspaceContent); Assert.Empty(rig.Queued);
    });

    [AvaloniaFact]
    public Task Specialist_inspection_then_attach_uses_the_actual_retained_Chat_identity_without_inference() => WithRigAsync(async rig =>
    {
        rig.Chat!.SetDraft("Draft remains after inspection and attachment.");
        var page = await rig.OpenAsync("specialists");
        var row = Assert.Single(page.Specialists, item => item.Definition.Id == rig.Sources.Agent.Id);
        Assert.Equal("No Agent (Default)", rig.Chat.ActiveAgentName);
        await page.DispatchAsync("organisation.inspect-specialist", row.Target, rig.Token);
        Assert.Equal(rig.Sources.Agent.Instructions, Find<TextBlock>(page, "organisation-specialist-instructions").Text);
        Assert.Contains("code-review", Find<TextBlock>(page, "organisation-specialist-tools").Text, StringComparison.Ordinal);
        Assert.Equal("No Agent (Default)", rig.Chat.ActiveAgentName);
        Assert.True(Find<Button>(page, "organisation-specialist-attach").IsEffectivelyEnabled);
        await page.DispatchAsync("organisation.attach", null, rig.Token);
        Assert.Equal(rig.Sources.Agent.Name, rig.Chat.ActiveAgentName);
        await page.DispatchAsync("organisation.return", null, rig.Token);
        Assert.Same(rig.Chat, rig.Home.SpaceWorkspaceContent);
        var target = rig.Session.CaptureSelectedChatModelTarget(); Assert.NotNull(target);
        var observed = await rig.Session.ReadConversationPickerAsync(target, rig.Actor, () => true, null, rig.Token);
        Assert.True(observed.Succeeded); Assert.Equal(rig.Sources.Agent.Id, observed.Value!.SelectedSpecialistId);
        Assert.Equal("Draft remains after inspection and attachment.", rig.Chat.ComposerInput.Text);
        Assert.Equal(0, rig.Sources.Mutations); Assert.Equal(0, rig.Canonical.ConversationWrites);
    });

    [AvaloniaFact]
    public Task Changed_or_disabled_specialist_cannot_attach_an_uninspected_definition() => WithRigAsync(async rig =>
    {
        var page = await rig.OpenAsync("specialists");
        await page.DispatchAsync("organisation.inspect-specialist", page.Specialists[0].Target, rig.Token);
        rig.Sources.Agents = [rig.Sources.Agent with { Instructions = "Changed instructions", IsEnabled = false }];
        await page.DispatchAsync("organisation.attach", null, rig.Token);
        Assert.Equal("No Agent (Default)", rig.Chat!.ActiveAgentName);
        Assert.Contains("changed", Find<TextBlock>(page, "organisation-status").Text, StringComparison.Ordinal);
        Assert.Equal(0, rig.Sources.Mutations);
    });

    [AvaloniaFact]
    public Task Superseded_Specialists_destination_cannot_attach_to_a_newly_published_chat() => WithRigAsync(async rig =>
    {
        rig.Chat!.SetDraft("Retain the originating conversation draft.");
        var page = await rig.OpenAsync("specialists");
        await page.DispatchAsync("organisation.inspect-specialist", page.Specialists[0].Target, rig.Token);
        await rig.Home.DispatchAsync("spaces.new-chat", SpaceRegistry.ChatSpaceId, rig.Token);
        await rig.Session.OriginalQueuedNavigation!.WaitAsync(Bound, rig.Token);
        var currentChat = Assert.IsType<NewChatPage>(rig.Home.SpaceWorkspaceContent);
        Assert.NotSame(rig.Chat, currentChat);
        var writes = rig.Canonical.ConversationWrites; var modelReads = rig.Canonical.ModelReads;
        await page.DispatchAsync("organisation.attach", null, rig.Token);
        Assert.Equal("No Agent (Default)", currentChat.ActiveAgentName);
        Assert.Equal("No Agent (Default)", rig.Chat.ActiveAgentName);
        Assert.Equal("Retain the originating conversation draft.", rig.Chat.ComposerInput.Text);
        Assert.Equal(writes, rig.Canonical.ConversationWrites); Assert.Equal(modelReads, rig.Canonical.ModelReads);
        Assert.Equal(0, rig.Sources.Mutations);
    });

    [AvaloniaFact]
    public Task Saved_specialists_route_without_a_chat_shows_inspection_but_cannot_create_or_attach() => WithRigAsync(async rig =>
    {
        var page = Assert.IsType<SpacesOrganisationPage>(rig.Home.SpaceWorkspaceContent);
        await page.DispatchAsync("organisation.inspect-specialist", page.Specialists[0].Target, rig.Token);
        Assert.False(Find<Button>(page, "organisation-specialist-attach").IsEffectivelyEnabled);
        Assert.Contains("Open a conversation", Find<TextBlock>(page, "organisation-specialist-attach-reason").Text, StringComparison.Ordinal);
        Assert.Equal("Back to Space", Find<Button>(page, "organisation-return").Content);
        Assert.Equal(0, rig.Canonical.ConversationWrites); Assert.Equal(0, rig.Canonical.ModelReads);
    }, initialChat: false);

    [AvaloniaFact]
    public Task Late_group_read_cannot_replace_a_newer_inspected_group() => WithRigAsync(async rig =>
    {
        var page = await rig.OpenAsync("groups");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<ContainerResource>>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Sources.ReadResources = (id, _) => id == rig.Sources.GroupA.Id ? Hold() : Task.FromResult<IReadOnlyList<ContainerResource>>([]);
        Task<IReadOnlyList<ContainerResource>> Hold() { entered.TrySetResult(); return release.Task; }
        var first = page.DispatchAsync("organisation.inspect-group", page.Groups[0].Target, rig.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(Bound, rig.Token);
            await page.DispatchAsync("organisation.inspect-group", page.Groups[1].Target, rig.Token);
            await rig.SettleAsync();
            var heading = Find<TextBlock>(page, "organisation-selected-group");
            Assert.Equal(rig.Sources.GroupB.Name, heading.Text);
            AssertVisibleInOrganisationViewport(heading, page, rig.Window);
            var offset = OrganisationScroller(page).Offset;
            release.TrySetResult([]); await first.WaitAsync(Bound, rig.Token);
            await rig.SettleAsync();
            Assert.Equal(rig.Sources.GroupB.Name, Find<TextBlock>(page, "organisation-selected-group").Text);
            Assert.Equal(rig.Sources.GroupB.Id, Assert.Single(page.Members).Group.Id);
            AssertVisibleInOrganisationViewport(heading, page, rig.Window);
            Assert.Equal(offset, OrganisationScroller(page).Offset);
        }
        finally { release.TrySetResult([]); await first.WaitAsync(Bound, rig.Token); }
    });

    [AvaloniaFact]
    public Task Close_joins_a_held_catalogue_read_and_keeps_its_genuine_late_failure() => WithRigAsync(async rig =>
    {
        var page = await rig.OpenAsync("groups");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<ContainerDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new IOException("Held canonical group source failed after retirement.");
        rig.Sources.ReadGroups = (_, _) => { entered.TrySetResult(); return release.Task; };
        var read = page.DispatchAsync("organisation.refresh", null, rig.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(Bound, rig.Token);
            var preparation = rig.Session.PrepareCloseAsync(rig.Token);
            Assert.False(preparation.IsCompleted);
            var close = rig.Session.CloseAndDrainAsync();
            Assert.False(close.IsCompleted); Assert.False(read.IsCompleted);
            release.TrySetException(expected);
            var readFailure = await Assert.ThrowsAnyAsync<Exception>(() => read.WaitAsync(Bound, rig.Token));
            Assert.True(Contains(readFailure, expected));
            var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(Bound, rig.Token));
            await Assert.ThrowsAnyAsync<Exception>(() => preparation.WaitAsync(Bound, rig.Token));
            Assert.True(Contains(closeFailure, expected)); Assert.False(rig.Session.CanRetireOriginalView);
            Assert.Same(close, rig.Session.CloseAndDrainAsync()); rig.VerifiedCloseFailure = closeFailure;
        }
        finally { release.TrySetException(expected); }
    });

    [AvaloniaTheory]
    [InlineData("groups", "Glow", 720, 650)]
    [InlineData("groups", "Glow", 1440, 900)]
    [InlineData("groups", "Professional", 720, 650)]
    [InlineData("groups", "Professional", 1440, 900)]
    [InlineData("specialists", "Glow", 720, 650)]
    [InlineData("specialists", "Glow", 1440, 900)]
    [InlineData("specialists", "Professional", 720, 650)]
    [InlineData("specialists", "Professional", 1440, 900)]
    public Task Actual_Spaces_rail_and_organisation_destinations_render_with_reachable_return(
        string destination, string theme, int width, int height) => WithRigAsync(async rig =>
    {
        var page = await rig.OpenAsync(destination);
        await rig.SettleAsync();
        var back = Find<Button>(page, "organisation-return");
        var search = Find<TextBox>(page, "organisation-search");
        AssertInside(back, rig.Window); AssertInside(search, rig.Window);
        Assert.True(Find<Button>(rig.Home, "space-chat-groups").IsEffectivelyVisible);
        Assert.True(Find<Button>(rig.Home, "space-specialists").IsEffectivelyVisible);
        Assert.True(page.Bounds.Width >= 200);
        var notice = Find<TextBlock>(rig.Home, "space-history-notice");
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, notice.TextWrapping);
        Assert.True(notice.TextLayout.TextLines.Count > 1, "the real unavailable-history notice must wrap rather than clip");
        AssertInside(notice, rig.Window);
        var sidebar = Find<Control>(rig.Home, "space-context-sidebar");
        var noticePoint = notice.TranslatePoint(default, sidebar); Assert.NotNull(noticePoint);
        Assert.True(noticePoint.Value.X >= 0 && noticePoint.Value.X + notice.Bounds.Width <= sidebar.Bounds.Width);
        if (destination == "groups")
        {
            Assert.Equal(2, page.Groups.Count);
            await page.DispatchAsync("organisation.inspect-group", page.Groups[0].Target, rig.Token);
        }
        else
        {
            Assert.Equal(2, page.Specialists.Count);
            await page.DispatchAsync("organisation.inspect-specialist", page.Specialists[0].Target, rig.Token);
        }
        await rig.SettleAsync(); AssertInside(back, rig.Window);
        var selectedHeading = Find<TextBlock>(page, destination == "groups"
            ? "organisation-selected-group" : "organisation-selected-specialist");
        AssertVisibleInOrganisationViewport(selectedHeading, page, rig.Window);
        var selectedAction = Find<Button>(page, destination == "groups"
            ? "organisation-member-open" : "organisation-specialist-attach");
        Assert.True(selectedAction.IsEffectivelyEnabled);
        // Inspect first reveals the scope, then the same native scroll owner must make
        // its actual Open/Attach action reachable without replacing the selected content.
        selectedAction.BringIntoView();
        await rig.SettleAsync();
        AssertVisibleInOrganisationViewport(selectedAction, page, rig.Window);
        var actionPoint = selectedAction.TranslatePoint(default, rig.Window); Assert.NotNull(actionPoint);
        var hit = rig.Window.InputHitTest(actionPoint.Value + new Vector(selectedAction.Bounds.Width / 2, selectedAction.Bounds.Height / 2));
        Assert.True(ReferenceEquals(hit, selectedAction) || hit is Visual visual && visual.GetVisualAncestors().Contains(selectedAction),
            "the selected action must be hittable inside the real scroll viewport");
        AssertInside(back, rig.Window);
        Assert.Equal(0, rig.Sources.Mutations);
        if (Environment.GetEnvironmentVariable("CUI_EVIDENCE_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory); using var capture = rig.Window.CaptureRenderedFrame(); Assert.NotNull(capture);
            capture.Save(Path.Combine(directory, $"spaces-{destination}-{theme}-{width}x{height}.png"));
        }
    }, theme, width, height);

    private static T Find<T>(Control root, string id) where T : Control =>
        Assert.Single(global::Haven.Desktop.Tests.AuthoredCuiControls.Enumerate(root).OfType<T>(), control => control.Name == id);
    private static ScrollViewer OrganisationScroller(SpacesOrganisationPage page) =>
        Assert.Single(Find<Control>(page, "organisation-scroll").GetVisualDescendants().OfType<ScrollViewer>());
    private static void AssertVisibleInOrganisationViewport(Control control, SpacesOrganisationPage page, Window window)
    {
        AssertInside(control, window);
        var scroller = OrganisationScroller(page);
        Assert.True(scroller.Viewport.Width > 0 && scroller.Viewport.Height > 0);
        var viewport = Assert.Single(scroller.GetVisualDescendants().OfType<ScrollContentPresenter>());
        var point = control.TranslatePoint(default, viewport); Assert.NotNull(point);
        Assert.True(point.Value.X >= -1 && point.Value.Y >= -1 &&
            point.Value.X + control.Bounds.Width <= viewport.Bounds.Width + 1 &&
            point.Value.Y + control.Bounds.Height <= viewport.Bounds.Height + 1,
            $"{control.Name}: {point} {control.Bounds}; actual viewport {viewport.Bounds}; offset {scroller.Offset}");
    }
    private static void AssertInside(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0);
        var point = control.TranslatePoint(default, window); Assert.NotNull(point);
        Assert.True(point.Value.X >= -1 && point.Value.Y >= -1 && point.Value.X + control.Bounds.Width <= window.Bounds.Width + 1 &&
            point.Value.Y + control.Bounds.Height <= window.Bounds.Height + 1, $"{control.Name}: {point} {control.Bounds}; window {window.Bounds}");
    }
    private static bool Contains(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(error => Contains(error, expected));

    private static async Task WithRigAsync(Func<OrganisationRig, Task> body, string theme = "Professional", int width = 1200,
        int height = 800, bool initialChat = true)
    {
        var token = TestContext.Current.CancellationToken;
        var rig = new OrganisationRig(token, theme, width, height);
        var failures = new List<Exception>();
        try { await rig.InitializeAsync(initialChat); await body(rig); }
        catch (Exception failure) { failures.Add(failure); }
        try { await rig.DisposeAsync(); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Spaces organisation assertions and original cleanup failed.", failures);
    }

    private sealed class OrganisationRig : IAsyncDisposable
    {
        internal readonly CancellationToken Token;
        internal readonly Sources Sources = new();
        internal readonly SpacesConversationRouteTests.Rig Canonical;
        internal readonly Window Window;
        internal readonly CuiThemePreferenceContext Theme;
        internal readonly List<(BrowseNavigationIntent Intent, Task Settlement)> Queued = [];
        internal readonly List<SpacesBrowseAppContentSession> AdditionalSessions = [];
        internal SpacesBrowseAppContentSession Session = null!;
        internal SpacesHomePage Home = null!;
        internal NewChatPage? Chat;
        internal AuthenticatedResourceActor Actor = null!;
        internal Exception? VerifiedCloseFailure;
        internal OrganisationRig(CancellationToken token, string theme, int width, int height)
        {
            Token = token; Theme = new(new ThemeSource(theme));
            Canonical = new(withHome: true, allowConversationWrites: true, configureServices: Sources.Configure);
            Canonical.Row = Canonical.Row! with { SpaceId = SpaceRegistry.ChatSpaceId };
            Canonical.ReadConversation = (id, _) => Task.FromResult(Sources.Members.FirstOrDefault(row => row.Id == id) ??
                (Canonical.Written.TryGetValue(id, out var written) ? written : Canonical.Row?.Id == id ? Canonical.Row : null));
            Canonical.ReadMessages = id => Task.FromResult<IReadOnlyList<ChatMessage>>([
                Canonical.SavedMessage with { ConversationId = id },
                Canonical.SavedMessage with { Id = Guid.NewGuid(), ConversationId = id, Role = MessageRole.Assistant, Content = "Existing retained response." }]);
            Window = new() { Width = width, Height = height };
        }
        internal async Task InitializeAsync(bool initialChat)
        {
            await Canonical.Home!.StartOriginalAsync().WaitAsync(Bound, Token);
            Actor = (await Canonical.Home.Profiles.GetCurrentAsync(Token))!; Assert.NotNull(Actor);
            var address = initialChat ? $"9to1://spaces/selected/{SpaceRegistry.ChatSpaceId:D}/chat/{Canonical.Row!.Id:D}" :
                $"9to1://spaces/selected/{SpaceRegistry.ChatSpaceId:D}/specialists";
            Session = Canonical.Session(address, profileId: Actor.ProfileId, queueNavigation: (intent, settlement) =>
            { Assert.False(settlement.IsCompleted); Queued.Add((intent, settlement)); return true; }, interfaceTheme: Theme);
            await Session.InitializeAsync(Token).WaitAsync(Bound, Token);
            Home = Assert.IsType<SpacesHomePage>(Assert.IsType<ContentControl>(Session.OriginalNativeView).Content);
            Chat = initialChat ? Assert.IsType<NewChatPage>(Home.SpaceWorkspaceContent) : null;
            Window.Content = Session.OriginalNativeView; Window.Show(); await SettleAsync();
        }
        internal async Task<SpacesOrganisationPage> OpenAsync(string kind)
        {
            await Home.DispatchAsync("spaces.open-" + kind, SpaceRegistry.ChatSpaceId, Token).AsTask().WaitAsync(Bound, Token);
            if (Session.OriginalQueuedNavigation is { } original) await original.WaitAsync(Bound, Token);
            await SettleAsync(); return Assert.IsType<SpacesOrganisationPage>(Home.SpaceWorkspaceContent);
        }
        internal async Task WaitForDestinationAsync(string kind)
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                await SettleAsync();
                if (Home.SpaceWorkspaceContent is SpacesOrganisationPage page && page.Kind ==
                    (kind == "groups" ? SpacesOrganisationKind.Groups : SpacesOrganisationKind.Specialists) &&
                    page.OriginalInitialization is { IsCompletedSuccessfully: true })
                { await Home.LatestOriginalMutation!.WaitAsync(Bound, Token); return; }
                await Task.Delay(10, Token);
            }
            throw new TimeoutException("The authored sidebar did not publish its real organisation destination.");
        }
        internal async Task SettleAsync()
        {
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(Bound, Token);
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        }
        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            foreach (var additional in AdditionalSessions)
                try { await additional.CloseAndDrainAsync().WaitAsync(Bound, Token); } catch (Exception failure) { failures.Add(failure); }
            if (Session is not null)
                try { await Session.CloseAndDrainAsync().WaitAsync(Bound, Token); }
                catch (Exception failure) when (ReferenceEquals(failure, VerifiedCloseFailure)) { }
                catch (Exception failure) { failures.Add(failure); }
            if (failures.Count == 0) { Window.Content = null; Window.Close(); }
            try { Canonical.Dispose(); } catch (Exception failure) { failures.Add(failure); }
            Theme.Dispose();
            if (failures.Count != 0) throw new AggregateException("The exact Spaces test owners did not retire cleanly.", failures);
        }
    }
    private sealed class ThemeSource(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "spaces-organisation-test", true,
            CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class Sources
    {
        internal ContainerDefinition GroupA = new(Guid.NewGuid(), HavenMode.Chat, "A research group", null,
            "Only reviewed project material", "Use the linked evidence.", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        internal ContainerDefinition GroupB = new(Guid.NewGuid(), HavenMode.Chat, "B planning group", null,
            "Planning context", "Check the scope before editing.", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        internal AgentDefinition Agent = new(Guid.NewGuid(), "Code reviewer", "Review changes and their tests", "Inspect the exact diff.",
            "agent-default", string.Empty, null, "code|review", "{\"capabilities\":[\"code-review\"]}", false, true, DateTimeOffset.UnixEpoch);
        internal IReadOnlyList<AgentDefinition> Agents;
        internal IReadOnlyList<Conversation> Members;
        internal Conversation MemberA, HiddenMember;
        internal Func<int, CancellationToken, Task<IReadOnlyList<ContainerDefinition>>>? ReadGroups;
        internal Func<Guid, CancellationToken, Task<IReadOnlyList<ContainerResource>>>? ReadResources;
        internal int Mutations, GroupReads;
        internal Sources()
        {
            Agents = [Agent, Agent with { Id = Guid.NewGuid(), Name = "Disabled reviewer", IsEnabled = false }];
            MemberA = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Reviewed conversation", GroupA.Id, null,
                false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, SpaceId: SpaceRegistry.ChatSpaceId);
            HiddenMember = MemberA with { Id = Guid.NewGuid(), Title = "Other Space conversation", SpaceId = Guid.NewGuid() };
            Members = [MemberA, HiddenMember, MemberA with { Id = Guid.NewGuid(), ContainerId = GroupB.Id, Title = "Planning conversation" },
                MemberA with { Id = Guid.NewGuid(), IsTemporary = true, Title = "Temporary conversation" }];
        }
        internal void Configure(IServiceCollection services)
        {
            var original = (IConversationRepository)services.Last(item => item.ServiceType == typeof(IConversationRepository)).ImplementationInstance!;
            services.AddSingleton(Proxy<IConversationRepository>((method, args) => method.Name == "GetRecentInScopeAsync"
                ? Task.FromResult<IReadOnlyList<Conversation>>(Members.Where(row => row.ContainerId == ((ConversationScope)args![0]!).ContainerId).ToArray())
                : Forward(method, original, args)));
            services.AddSingleton(Proxy<IContainerRepository>((method, args) => method.Name == "GetByModeAsync"
                ? ReadGroups?.Invoke(++GroupReads, (CancellationToken)args![1]!) ?? Task.FromResult<IReadOnlyList<ContainerDefinition>>([GroupA, GroupB])
                : RefuseMutation(method.Name)));
            services.AddSingleton(Proxy<IContainerResourceRepository>((method, args) => method.Name == "GetByContainerAsync"
                ? ReadResources?.Invoke((Guid)args![0]!, (CancellationToken)args[1]!) ?? Task.FromResult<IReadOnlyList<ContainerResource>>([
                    new(Guid.NewGuid(), (Guid)args![0]!, "Source reference", "private-storage-anchor", "text/plain", ContainerResourceKind.Document,
                        24, "source-hash", DateTimeOffset.UnixEpoch)])
                : RefuseMutation(method.Name)));
            services.AddSingleton(Proxy<ICatalogRepository>((method, _) => method.Name switch
            {
                "GetAllAgentsAsync" => Task.FromResult(Agents),
                "GetAgentsAsync" => Task.FromResult<IReadOnlyList<AgentDefinition>>(Agents.Where(item => item.IsEnabled).ToArray()),
                "GetPromptsAsync" => Task.FromResult<IReadOnlyList<PromptDefinition>>([]),
                _ => RefuseMutation(method.Name)
            }));
        }
        private object RefuseMutation(string name) { Mutations++; throw new InvalidOperationException("Unexpected organisation mutation: " + name); }
    }
    private static object? Forward(MethodInfo method, object original, object?[]? args)
    {
        try { return method.Invoke(original, args); }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        { ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> callback) where T : class
    {
        var proxy = DispatchProxy.Create<T, ReadProxy>(); ((ReadProxy)(object)proxy).Callback = callback; return proxy;
    }
    public class ReadProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Callback { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Callback(targetMethod!, args);
    }
}
