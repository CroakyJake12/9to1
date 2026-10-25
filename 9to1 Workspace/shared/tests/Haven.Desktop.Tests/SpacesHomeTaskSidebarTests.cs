using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.Desktop.Views.Pages.Tasks;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Tasks;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    private sealed class SidebarNavigationHost : ISpacesActionHost
    { public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private static T SidebarControl<T>(SpacesHomePage page, string id) where T : Control =>
        ObservationAuthoredControls(Assert.IsAssignableFrom<Control>(page.OriginalHost.Content))
            .OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static async Task SidebarClickAsync(SpacesHomePage page, string id)
    {
        SidebarControl<Button>(page, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await page.CaptureTaskSidebarActionsAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [AvaloniaFact]
    public async Task Tasks_contextual_sidebar_uses_persisted_scope_and_real_rich_tiles_then_opens_the_exact_issued_task()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(); using var paths = new ObservationPaths();
        var registry = new SpaceRegistry(new ObservationSettings());
        _ = await registry.GetAllAsync(cancellationToken: token);
        rig.Conversations.Current = rig.Conversation with { SpaceId = SpaceRegistry.TasksSpaceId, Mode = HavenMode.Tasks };
        var local = await rig.Tasks.BeginAuthorizedAsync(rig.Conversation.Id, Guid.NewGuid(), "Compile the current project", TaskExecutionDurability.PersistedPlan, [], token);
        var external = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "External app work", TaskExecutionDurability.PersistedPlan, [], token);
        var source = new SpaceTaskWorkspaceService(registry, rig.Conversations, rig.Tasks);
        var preferences = new UserPreferencesService(paths);
        SpacesHomePage.TaskOpenIntent? opened = null;
        var page = new SpacesHomePage(registry, new SidebarNavigationHost(), SpacesHomePage.ImmediateReadinessInstance,
            workspaceCommandHost: (command, parameter, _, _) =>
            { Assert.Equal("spaces.tasks.open", command); opened = Assert.IsType<SpacesHomePage.TaskOpenIntent>(parameter); return Task.CompletedTask; });
        page.BindOriginalTaskSidebar(source, rig.Tasks, preferences, (commit, _) => { commit(); return Task.CompletedTask; }, () => true);
        var window = new Window { Width = 1200, Height = 900, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token); await page.Controller.SelectSpaceAsync(SpaceRegistry.TasksSpaceId, token);
            await page.RefreshTaskSidebarAsync(token); window.UpdateLayout();
            Assert.Equal(local.TaskId, Assert.Single(page.OriginalSidebarTasks).Snapshot.TaskId);
            var text = string.Join("\n", SidebarControl<Control>(page, "space-task-sidebar").GetVisualDescendants().OfType<TextBlock>().Select(control => control.Text));
            Assert.Contains(local.PromptSummary, text); Assert.Contains("Activity unconfirmed", text);
            Assert.DoesNotContain(local.TaskId.ToString("D"), text); Assert.DoesNotContain("Verified", text, StringComparison.OrdinalIgnoreCase);
            var sidebar = SidebarControl<Control>(page, "space-task-sidebar");
            var menu = Assert.IsType<ContextMenu>(sidebar.ContextMenu);
            var point = sidebar.TranslatePoint(new Point(8, 8), window)!.Value;
            window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); window.UpdateLayout();
            Assert.True(menu.IsOpen);
            var shown = Assert.IsAssignableFrom<MenuItem>(Assert.Single(menu.Items)); Assert.Equal("Shown Tasks", shown.Header);
            shown.IsSubMenuOpen = true;
            shown.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "All Tasks")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await page.CaptureTaskSidebarActionsAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(TaskShownScope.AllTasks, new UserPreferencesService(paths).ShownTasks);
            Assert.Equal(2, page.OriginalSidebarTasks.Count);
            var row = Assert.Single(page.OriginalSidebarTasks, value => value.Snapshot.TaskId == external.TaskId);
            Assert.True(page.TryGetItemValue(row, "openIntent", out var intent));
            await page.DispatchAsync("spaces.tasks.open", intent, token);
            var actualOpened = Assert.IsType<SpacesHomePage.TaskOpenIntent>(opened);
            Assert.Same(intent, actualOpened); Assert.True(source.IsIssuedSelection(actualOpened.TaskSelection));
            Assert.Equal(external.ExecutionId, actualOpened.TaskSelection.ExecutionId); Assert.Null(actualOpened.TaskSelection.OriginSpaceId);
            var copied = (SpacesHomePage.TaskOpenIntent)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(actualOpened, null)!;
            Assert.False(page.IsCurrentTaskOpenIntent(copied));
            await page.RefreshTaskSidebarAsync(token); Assert.False(page.IsCurrentTaskOpenIntent(actualOpened));
            var overflow = SidebarControl<MenuItem>(page, "space-task-options");
            Assert.Equal("Task sidebar options", AutomationProperties.GetName(overflow));
            overflow.IsSubMenuOpen = true; var overflowScope = Assert.IsAssignableFrom<MenuItem>(Assert.Single(overflow.Items));
            overflowScope.IsSubMenuOpen = true;
            overflowScope.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Task Space Only")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await page.CaptureTaskSidebarActionsAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(local.TaskId, Assert.Single(page.OriginalSidebarTasks).Snapshot.TaskId);
            Assert.Equal(TaskShownScope.TaskSpaceOnly, new UserPreferencesService(paths).ShownTasks);
            Assert.Equal(0, rig.Conversations.Writes); Assert.Equal(0, rig.Client.Dispatches);
        }
        finally { page.RequestRetirement(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Tasks_contextual_sidebar_preserves_other_space_history_extensions_and_collapse()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(); using var paths = new ObservationPaths();
        var registry = new SpaceRegistry(new ObservationSettings());
        _ = await registry.GetAllAsync(cancellationToken: token);
        var custom = await registry.CreateAsync("Existing project", cancellationToken: token);
        var conversation = Guid.NewGuid(); var historyReads = 0;
        var page = new SpacesHomePage(registry, new SidebarNavigationHost(), SpacesHomePage.ImmediateReadinessInstance,
            conversationsProvider: (space, _) => { Assert.Equal(custom.Id, space); historyReads++; return Task.FromResult<IReadOnlyList<SpacesHomeConversationRow>>([new(conversation, "Existing project chat", "Saved", false)]); },
            sidebarItemsProvider: (_, _) => Task.FromResult<IReadOnlyList<SpacesHomeSidebarRow>>([new("plugin.stable.destination", "Project destination", "settings", "Page", "https://example.invalid", false, "Controlled source has no navigation") ]));
        page.BindOriginalTaskSidebar(new(registry, rig.Conversations, rig.Tasks), rig.Tasks, new(paths), (commit, _) => { commit(); return Task.CompletedTask; }, () => true);
        var window = new Window { Width = 1200, Height = 900, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token); await page.Controller.SelectSpaceAsync(custom.Id, token);
            await page.RefreshConversationsAsync(token); await page.RefreshSidebarItemsAsync(token);
            Assert.Equal(1, historyReads); Assert.True(page.TryGetValue("spaces.isTasksSpace", out var tasks)); Assert.Equal(false, tasks);
            Assert.True(page.TryGetValue("spaces.conversations", out var rows)); Assert.Equal(conversation, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<SpacesHomeConversationRow>>(rows)).Id);
            Assert.True(page.TryGetValue("spaces.sidebarItems", out var extensions)); Assert.Equal("plugin.stable.destination", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<SpacesHomeSidebarRow>>(extensions)).SidebarItemId);
            page.Controller.ToggleSidebar(); Assert.True(page.Controller.State.SidebarCollapsed);
            await page.Controller.SelectSpaceAsync(SpaceRegistry.TasksSpaceId, token); await page.RefreshTaskSidebarAsync(token);
            Assert.True(page.Controller.State.SidebarCollapsed); Assert.Empty(page.OriginalSidebarTasks);
            await page.Controller.SelectSpaceAsync(custom.Id, token); await page.RefreshConversationsAsync(token);
            Assert.Equal(2, historyReads); Assert.True(page.Controller.State.SidebarCollapsed);
        }
        finally { page.RequestRetirement(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Tasks_contextual_sidebar_permission_popout_uses_the_actual_request_and_deny_ack_without_resuming()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false); using var paths = new ObservationPaths();
        await rig.RunAsync(tools: false);
        var actual = rig.Service.CurrentCanonicalTask!;
        rig.Conversations.Current = rig.Conversation with { SpaceId = SpaceRegistry.TasksSpaceId, Mode = HavenMode.Tasks };
        var registry = new SpaceRegistry(new ObservationSettings()); _ = await registry.GetAllAsync(cancellationToken: token);
        var page = new SpacesHomePage(registry, new SidebarNavigationHost(), SpacesHomePage.ImmediateReadinessInstance);
        page.BindOriginalTaskSidebar(new(registry, rig.Conversations, rig.Tasks), rig.Tasks, new(paths), (commit, _) => { commit(); return Task.CompletedTask; }, () => true);
        var window = new Window { Width = 1200, Height = 900, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token); await page.Controller.SelectSpaceAsync(SpaceRegistry.TasksSpaceId, token);
            await page.RefreshTaskSidebarAsync(token);
            await SidebarClickAsync(page, "space-task-review");
            var request = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
            Assert.Equal(request.Title, SidebarControl<TextBlock>(page, "space-task-review-question").Text);
            Assert.Contains("does not approve", SidebarControl<TextBlock>(page, "space-task-review-explanation").Text!, StringComparison.Ordinal);
            await SidebarClickAsync(page, "space-task-review-deny");
            var reply = Assert.IsType<TaskRunOriginalTaskPermissionResponse>(page.AcknowledgedSidebarPermissionResponse);
            Assert.Equal(request.Id, reply.RequestId);
            Assert.Equal(PermissionDecisionKind.Denied, reply.Decision.Kind);
            Assert.Equal(TaskExecutionLifecycle.Suspended, (await rig.Tasks.GetAsync(actual.TaskId, token))!.State);
            Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(0, rig.Workspace.Effects);
        }
        finally { page.RequestRetirement(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Tasks_browse_factory_opens_the_same_actual_task_and_joins_draft_safe_view_close_under_real_Home()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(); using var paths = new ObservationPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(paths.DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var principal = new OperatingSystemPrincipalSource();
        await using var home = new HomeNativeWindowsComposition(new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json")),
            principal, paths, new("9to1.home.tasks.fixture." + Guid.NewGuid().ToString("N")));
        var registry = new SpaceRegistry(new ObservationSettings());
        var preferences = new UserPreferencesService(paths);
        var services = new ServiceCollection(); services.AddHavenInfrastructure(); services.AddHavenOwnedWindowsHomeDomain(home, paths, principal);
        services.AddSingleton(registry); services.AddSingleton(rig.Tasks); services.AddSingleton<IConversationRepository>(rig.Conversations); services.AddSingleton(preferences);
        await using var provider = services.BuildServiceProvider();
        using var app = new CancellationTokenSource(); using var host = new CancellationTokenSource();
        await home.StartOriginalAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
        var homeActor = (await home.Profiles.GetCurrentAsync(token))!;
        rig.Actors.Current = Assert.IsType<AuthenticatedResourceActor>(await provider.GetRequiredService<HostLocalTaskActorSource>().GetCurrentAsync(token));
        rig.Conversations.Current = rig.Conversation; // Existing external Studio context with no Space origin.
        var task = await rig.Tasks.BeginAuthorizedAsync(rig.Conversation.Id, Guid.NewGuid(), "Continue exact original app work", TaskExecutionDurability.PersistedPlan, [], token);
        var address = AppContentAddress.Parse($"9to1://spaces/task/{task.TaskId:D}/{task.ExecutionId:D}");
        var factory = SpacesBrowseAppContentFactory.BindOriginalSameProcess(provider, home, app.Token, host.Token);
        var session = Assert.IsType<SpacesBrowseAppContentSession>(factory.CreateSession(new(Guid.NewGuid(), address, homeActor.ProfileId, BrowserTabPrivacy.Standard)));
        var window = new Window { Width = 1200, Height = 900 };
        try
        {
            await session.InitializeAsync(token); window.Content = session.OriginalNativeView; window.Show(); window.UpdateLayout();
            var widget = Assert.Single(session.OriginalTaskViews);
            var originalSelection = Assert.IsType<TaskContextSelection>(widget.OriginalTaskSelection);
            Assert.Equal(task.TaskId, originalSelection.TaskId); Assert.Equal(task.ExecutionId, originalSelection.ExecutionId);
            Assert.Null(originalSelection.OriginSpaceId); Assert.Equal(address, session.Address);
            var originalConversation = Assert.IsType<Conversation>(rig.Conversations.Current);
            Assert.Equal(HavenMode.Studio, originalConversation.Mode); Assert.Null(originalConversation.SpaceId);
            Assert.True(widget.TrySetValue("Instruction", "retain this unsent follow-up"));
            Assert.Null(await session.PrepareCloseAsync(token));
            Assert.Null(session.OriginalClose); Assert.Null(widget.OriginalClose);
            Assert.True(widget.TryGetValue("Instruction", out var draft)); Assert.Equal("retain this unsent follow-up", draft);
            Assert.True(widget.TrySetValue("Instruction", ""));
            var preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync(token));
            Assert.True(preparation.IsCurrent); Assert.False(widget.TrySetValue("Instruction", "cannot enter while frozen"));
            await preparation.AbortAsync(); Assert.True(widget.TrySetValue("Instruction", "restored draft editing"));
            Assert.True(widget.TrySetValue("Instruction", ""));
            preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync(token));
            await preparation.BeginClose(); Assert.True(session.CanRetireOriginalView); Assert.True(widget.OriginalClose!.IsCompletedSuccessfully);
            Assert.Equal(task.PersistenceRevision, (await rig.Tasks.GetAsync(task.TaskId, token))!.PersistenceRevision);
            Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(0, rig.Conversations.Writes); Assert.Null(home.OriginalCloseTask);
        }
        finally { await session.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }
}
