using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Spaces.Builder;

namespace HavenOS.Browse.Host.Tests;

/// <summary>Independent mounted actual Builder/session actions. Canonical identities,
/// decisions and owner settlement; not native, design, ACL or complete Builder acceptance.</summary>
public sealed class SpacesBuilderRouteTests
{
    private static readonly TimeSpan Bound = SpaceBuilderPageTests.Bound;

    [AvaloniaFact]
    public async Task Actual_Home_New_and_Edit_reuse_the_same_canonical_Builder_and_tab_without_creating_Chat()
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true);
        var actor = await Start(rig); var tab = Guid.NewGuid();
        var session = rig.Session("9to1://spaces", tab, actor);
        Window? window = null; Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = SpaceBuilderPageTests.Mount(root);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            await Open(home, session, "create-space");
            var page = Assert.IsType<SpaceBuilderPage>(root.Content); var id = page.State.SpaceId;
            Assert.NotEqual(Guid.Empty, id);
            Assert.Equal($"9to1://spaces/builder/{id:D}", session.Address.Value);
            Assert.Null(home.Controller.State.PendingRenameSpaceId);
            SpaceBuilderPageTests.Input(page.Host, "space-builder-name").Text = "Saved through real Home Builder";
            SpaceBuilderPageTests.Click(page.Host, "space-builder-save");
            await page.Host.CaptureOriginalActionSettlement(page.Show!).WaitAsync(Bound);
            Assert.Equal("Saved through real Home Builder", (await rig.Spaces.ReadExistingAsync(id))!.Name);
            await Back(page, session); Assert.Same(home, root.Content);
            await Select(home, "Saved through real Home Builder");
            await Open(home, session, "edit-space-details");
            Assert.Same(page, root.Content); Assert.Same(root, session.OriginalNativeView);
            Assert.Equal(id, page.State.SpaceId);
            Assert.Equal($"9to1://spaces/builder/{id:D}", session.Address.Value);
            Assert.Equal(0, rig.ConversationWrites); Assert.Equal(0, rig.ModelReads);
            var original = new TabSessionSnapshot(tab, "spaces", "Saved", "{\"extension\":\"keep\"}", "history", 3,
                null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            var saved = UnifiedWorkspaceTabState.Merge(original,
                new BrowserTabState(tab, session.Title, session.Address.Value, BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow) { ProfileId = actor });
            Assert.Equal(tab, saved.Id);
            Assert.Equal("keep", System.Text.Json.Nodes.JsonNode.Parse(saved.StateJson)!["extension"]!.GetValue<string>());
            Assert.True(UnifiedWorkspaceTabState.TryProject(saved, actor, out var restored));
            Assert.Equal(session.Address.Value, restored!.Address);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(session, rig, window, primary); }
    }

    [AvaloniaTheory]
    [InlineData("save")]
    [InlineData("discard")]
    [InlineData("cancel")]
    public async Task Dirty_actual_Builder_Back_requires_the_visible_three_choice_decision_before_returning(string choice)
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true);
        var row = await rig.Spaces.CreateAsync("Original Builder return"); var actor = await Start(rig);
        var session = rig.Session($"9to1://spaces/builder/{row.Id:D}", profileId: actor);
        Window? window = null; Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = SpaceBuilderPageTests.Mount(root);
            var page = Assert.IsType<SpaceBuilderPage>(root.Content);
            SpaceBuilderPageTests.Input(page.Host, "space-builder-name").Text = "Unsent Builder return edit";
            Assert.True(page.State.HasChanges);
            var prior = session.OriginalQueuedNavigation; SpaceBuilderPageTests.Click(page.Host, "space-builder-back");
            var driver = await Navigation(session, prior);
            var decision = await DecisionBeforeDriverCompletes(page, driver);
            Assert.Same(page, root.Content); Assert.False(driver.IsCompleted);
            SpaceBuilderPageTests.Click(decision, "space-details-close-" + choice);
            await driver.WaitAsync(Bound);
            Assert.Null(page.OriginalClose); Assert.Null(session.OriginalClose);
            if (choice == "cancel")
            {
                Assert.Same(page, root.Content); Assert.True(page.State.HasChanges);
                Assert.Equal("Unsent Builder return edit", page.State.Draft.Name);
                Assert.Equal($"9to1://spaces/builder/{row.Id:D}", session.Address.Value);
            }
            else
            {
                Assert.IsType<SpacesHomePage>(root.Content); Assert.False(page.State.HasChanges);
            }
            Assert.Equal(choice == "save" ? "Unsent Builder return edit" : "Original Builder return",
                (await rig.Spaces.ReadExistingAsync(row.Id))!.Name);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(session, rig, window, primary); }
    }

    [AvaloniaFact]
    public async Task Hidden_Builder_sibling_Cancel_restores_the_same_visible_Chat_conversation_scope_and_draft()
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true);
        var first = await rig.Spaces.CreateAsync("First retained Builder"); var second = await rig.Spaces.CreateAsync("Second retained Builder");
        var actor = await Start(rig); var session = rig.Session($"9to1://spaces/builder/{first.Id:D}", profileId: actor);
        Window? window = null; Exception? primary = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView); window = SpaceBuilderPageTests.Mount(root);
            var firstPage = Assert.IsType<SpaceBuilderPage>(root.Content);
            await Back(firstPage, session);
            var home = Assert.IsType<SpacesHomePage>(root.Content);
            await Select(home, second.Name); await Open(home, session, "edit-space-details");
            var secondPage = Assert.IsType<SpaceBuilderPage>(root.Content);
            await Back(secondPage, session); Assert.Same(home, root.Content);
            await Select(home, "Chat"); await Open(home, session, "open-selected-space");
            var chat = Assert.IsType<NewChatPage>(root.Content);
            var conversation = chat.ConversationId; var savedConversation = chat.CurrentConversation; var address = session.Address.Value;
            chat.SetDraft("Unsent draft across real hidden Builder decisions");
            Assert.True(firstPage.TryEdit(SpaceBuilderField.Name, "Hidden first edit"));
            Assert.True(secondPage.TryEdit(SpaceBuilderField.Name, "Hidden second edit"));
            var prepare = session.PrepareCloseAsync();
            var firstDecision = await SpaceBuilderPageTests.Decision(firstPage);
            Assert.Same(firstPage, root.Content); Assert.False(prepare.IsCompleted);
            SpaceBuilderPageTests.Click(firstDecision, "space-details-close-discard");
            var secondDecision = await SpaceBuilderPageTests.Decision(secondPage);
            Assert.Same(secondPage, root.Content); Assert.False(prepare.IsCompleted);
            SpaceBuilderPageTests.Click(secondDecision, "space-details-close-cancel");
            Assert.Null(await prepare.WaitAsync(Bound));
            Assert.Same(chat, root.Content); Assert.Equal(conversation, chat.ConversationId);
            Assert.Equal(savedConversation.SpaceId, chat.CurrentConversation.SpaceId);
            Assert.Equal(savedConversation.ParentConversationId, chat.CurrentConversation.ParentConversationId);
            Assert.Equal(address, session.Address.Value);
            var scene = (ChatHavenScene)typeof(NewChatPage).GetField("_scene", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(chat)!;
            Assert.Equal("Unsent draft across real hidden Builder decisions", scene.Instruction.Text);
            Assert.Equal("First retained Builder", (await rig.Spaces.ReadExistingAsync(first.Id))!.Name);
            Assert.Equal("Second retained Builder", (await rig.Spaces.ReadExistingAsync(second.Id))!.Name);
            Assert.True(firstPage.TryEdit(SpaceBuilderField.Description, "First lease actually rolled back"));
            Assert.True(secondPage.TryEdit(SpaceBuilderField.Description, "Second decision actually canceled"));
            Assert.Null(firstPage.OriginalClose); Assert.Null(secondPage.OriginalClose); Assert.Null(session.OriginalClose);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(session, rig, window, primary); }
    }

    private static async Task<string> Start(SpacesConversationRouteTests.Rig rig)
    { await rig.Home!.StartOriginalAsync().WaitAsync(Bound); return (await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None))!.ProfileId; }
    private static async Task Open(SpacesHomePage page, SpacesBrowseAppContentSession session, string button)
    { var prior = session.OriginalQueuedNavigation; SpaceBuilderPageTests.Click(page.OriginalHost, button); await (await Navigation(session, prior)).WaitAsync(Bound); }
    private static async Task Back(SpaceBuilderPage page, SpacesBrowseAppContentSession session)
    { var prior = session.OriginalQueuedNavigation; SpaceBuilderPageTests.Click(page.Host, "space-builder-back"); await (await Navigation(session, prior)).WaitAsync(Bound); }
    private static async Task<Task> Navigation(SpacesBrowseAppContentSession session, Task? prior)
    {
        using var timeout = new CancellationTokenSource(Bound);
        while (session.OriginalQueuedNavigation is not { } actual || ReferenceEquals(prior, actual))
        { timeout.Token.ThrowIfCancellationRequested(); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(timeout.Token); }
        return session.OriginalQueuedNavigation!;
    }
    private static async Task<CuiSceneHost> DecisionBeforeDriverCompletes(SpaceBuilderPage page, Task driver)
    {
        using var timeout = new CancellationTokenSource(Bound);
        while (page.Content is not CuiSceneHost host || ReferenceEquals(host, page.Host) || host.Availability?.State != CuiSceneAvailabilityState.Ready)
        {
            if (driver.IsCompleted)
            {
                await driver; Assert.Fail("Actual dirty Builder Back completed before presenting its Save/Discard/Cancel decision.");
            }
            timeout.Token.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(timeout.Token);
        }
        return (CuiSceneHost)page.Content!;
    }
    private static async Task Select(SpacesHomePage home, string name)
    {
        var host = home.OriginalHost; var window = Assert.IsType<Window>(TopLevel.GetTopLevel(host));
        for (var pass = 0; pass < 3; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
        var body = Assert.IsAssignableFrom<Control>(host.Content);
        var button = Assert.Single(body.GetVisualDescendants().Prepend(body).OfType<Button>(), item => AutomationProperties.GetName(item) == "Open " + name);
        Assert.True(button.IsEnabled); var prior = home.LatestOriginalMutation;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        using var timeout = new CancellationTokenSource(Bound);
        while (home.LatestOriginalMutation is not { } actual || ReferenceEquals(prior, actual))
        { timeout.Token.ThrowIfCancellationRequested(); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(timeout.Token); }
        await home.LatestOriginalMutation!.WaitAsync(Bound);
        Assert.Equal(name, home.Controller.State.Selection!.Name);
    }
    private static async Task Cleanup(SpacesBrowseAppContentSession session, SpacesConversationRouteTests.Rig rig, Window? window, Exception? primary)
    {
        List<Exception> failures = [];
        try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch (Exception error) { failures.Add(error); }
        if (window is not null)
        {
            try { window.Content = null; } catch (Exception error) { failures.Add(error); }
            try { window.Close(); Dispatcher.UIThread.RunJobs(); } catch (Exception error) { failures.Add(error); }
        }
        try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); } catch (Exception error) { failures.Add(error); }
        if (failures.Count == 0) return;
        if (primary is not null) failures.Insert(0, primary);
        throw new AggregateException("Actual Builder/session body and all original owner cleanup failures are retained.", failures);
    }
}
