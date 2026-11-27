using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Tests;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using Rig = HavenOS.Browse.Host.Tests.SpacesConversationRouteTests.Rig;

namespace HavenOS.Browse.Host.Tests;

public sealed class SpacesResponsiveSidebarTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [AvaloniaTheory]
    [InlineData(1200)]
    [InlineData(700)]
    public Task Real_viewport_breakpoints_preserve_workspace_manager_draft_and_source_projections(int initialWidth) =>
        WithWorkspaceAsync(initialWidth, async fixture =>
        {
            var page = fixture.Page;
            var chat = Assert.IsType<NewChatPage>(page.SpaceWorkspaceContent);
            var mount = page.SpaceWorkspaceMount;
            Assert.NotNull(mount);
            var input = Composer(chat);
            Assert.True(input.Bounds.Width > 0 && input.Bounds.Height > 0);
            var selection = page.Controller.State.Selection;
            Assert.NotNull(selection);
            var summary = Authored<CuiContainerControl>(page, "space-details");
            var header = Authored<CuiContainerControl>(page, "space-app-header");
            var workspace = Authored<CuiContainerControl>(page, "space-app-content");
            var layout = Authored<CuiContainerControl>(page, "spaces-layout");
            var conversationId = chat.ConversationId;
            var address = fixture.Session.Address.Value;
            Assert.True(page.TryGetValue("spaces.conversations", out var history));
            Assert.True(fixture.Rig.SpaceHistoryReads > 0);
            const string draft = "An unsent draft retained while the Space resizes.";
            chat.SetDraft(draft);
            await chat.JoinAdmittedOriginalWorkAsync().WaitAsync(Bound);
            await PumpAsync(fixture.Window);
            var reads = SourceReads(fixture);

            void AssertRetained()
            {
                Assert.Same(selection, page.Controller.State.Selection);
                Assert.Same(chat, page.SpaceWorkspaceContent);
                Assert.Same(mount, page.SpaceWorkspaceMount);
                Assert.Same(input, Composer(chat));
                Assert.Equal(conversationId, chat.ConversationId);
                Assert.Equal(draft, input.Text);
                Assert.Equal(draft, chat.ComposerInput.Text);
                Assert.Equal(address, fixture.Session.Address.Value);
                Assert.True(page.TryGetValue("spaces.conversations", out var currentHistory));
                Assert.Same(history, currentHistory);
                Assert.Equal(reads, SourceReads(fixture));
            }

            Assert.Equal(initialWidth < 750, page.Controller.State.SidebarCollapsed);
            await ResizeAsync(fixture, 1200, 232);
            Assert.False(page.Controller.State.SidebarCollapsed);
            Assert.Equal(1, Grid.GetColumn(header));
            Assert.Equal(1, Grid.GetColumnSpan(header));
            Assert.Equal(0, Grid.GetRow(workspace));
            Assert.Equal(2, Grid.GetRowSpan(workspace));
            Assert.Equal(0, Grid.GetRow(summary));
            Assert.Equal(2, Grid.GetRowSpan(summary));
            var headerOrigin = header.TranslatePoint(new Point(), layout);
            var workspaceOrigin = workspace.TranslatePoint(new Point(), layout);
            Assert.NotNull(headerOrigin);
            Assert.NotNull(workspaceOrigin);
            Assert.Equal(headerOrigin.Value.Y, workspaceOrigin.Value.Y, precision: 3);

            await ResizeAsync(fixture, 999, 210);
            AssertRetained();
            await ResizeAsync(fixture, 1000, 232);
            await ResizeAsync(fixture, 749, 0);
            Assert.True(page.Controller.State.SidebarCollapsed);
            Assert.False(header.IsEffectivelyVisible);
            var selectedRail = Assert.Single(page.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "open-custom-space" &&
                    AutomationProperties.GetName(button) == $"Open {selection!.Name}");
            Assert.True(selectedRail.IsEffectivelyVisible);
            Assert.Contains(selection!.Name, Assert.IsType<string>(ToolTip.GetTip(selectedRail)));
            AssertRetained();

            // Growing restores only the collapse owned by the responsive presentation.
            await ResizeAsync(fixture, 750, 210);
            Assert.False(page.Controller.State.SidebarCollapsed);
            Assert.True(header.IsEffectivelyVisible);
            await ResizeAsync(fixture, 1200, 232);

            await page.DispatchAsync("spaces.manage", null).AsTask().WaitAsync(Bound);
            await PumpUntilAsync(fixture.Window, () => summary.IsEffectivelyVisible);
            Assert.True(page.Controller.State.IsManaging);
            var summaryOrigin = summary.TranslatePoint(new Point(), layout);
            Assert.NotNull(summaryOrigin);
            Assert.Equal(headerOrigin.Value.Y, summaryOrigin.Value.Y, precision: 3);
            await ResizeAsync(fixture, 700, 0);
            Assert.True(page.Controller.State.IsManaging);
            Assert.Same(summary, Authored<CuiContainerControl>(page, "space-details"));
            AssertRetained();
            await ResizeAsync(fixture, 1200, 232);
            await page.DispatchAsync("spaces.manage", null).AsTask().WaitAsync(Bound);
            await PumpUntilAsync(fixture.Window, () => workspace.IsEffectivelyVisible);
            Assert.False(page.Controller.State.IsManaging);
            AssertRetained();
        });

    [AvaloniaFact]
    public Task Manual_sidebar_choices_survive_resizing_and_take_ownership_from_auto_collapse() =>
        WithWorkspaceAsync(1200, async fixture =>
        {
            var page = fixture.Page;
            var selection = page.Controller.State.Selection;
            var mounted = page.SpaceWorkspaceContent;
            var reads = SourceReads(fixture);

            await page.DispatchAsync("spaces.toggle-sidebar", null).AsTask().WaitAsync(Bound);
            await ResizeAsync(fixture, 700, 0);
            await ResizeAsync(fixture, 1200, 0);
            Assert.True(page.Controller.State.SidebarCollapsed);

            await page.DispatchAsync("spaces.toggle-sidebar", null).AsTask().WaitAsync(Bound);
            await ResizeAsync(fixture, 700, 0);
            Assert.True(page.Controller.State.SidebarCollapsed);
            // A manual expansion at narrow width is not immediately re-collapsed.
            await page.DispatchAsync("spaces.toggle-sidebar", null).AsTask().WaitAsync(Bound);
            await ResizeAsync(fixture, 650, 210);
            Assert.False(page.Controller.State.SidebarCollapsed);
            await ResizeAsync(fixture, 1200, 232);
            Assert.False(page.Controller.State.SidebarCollapsed);

            // Explicit collapse after overriding auto-collapse must survive the next growth.
            await ResizeAsync(fixture, 700, 0);
            await page.DispatchAsync("spaces.toggle-sidebar", null).AsTask().WaitAsync(Bound);
            await page.DispatchAsync("spaces.toggle-sidebar", null).AsTask().WaitAsync(Bound);
            await ResizeAsync(fixture, 1200, 0);
            Assert.True(page.Controller.State.SidebarCollapsed);
            Assert.Same(selection, page.Controller.State.Selection);
            Assert.Same(mounted, page.SpaceWorkspaceContent);
            Assert.Equal(reads, SourceReads(fixture));
        });

    [AvaloniaFact]
    public Task Resize_paused_by_close_preparation_reconciles_after_abort_without_another_resize() =>
        WithWorkspaceAsync(1200, async fixture =>
        {
            var page = fixture.Page;
            var selection = page.Controller.State.Selection;
            var mounted = page.SpaceWorkspaceContent;
            var reads = SourceReads(fixture);
            var preparation = await page.PrepareHomeCloseAsync().WaitAsync(Bound);
            Assert.NotNull(preparation);
            try
            {
                await ResizeAsync(fixture, 700, 232);
                Assert.False(page.Controller.State.SidebarCollapsed);
                Assert.Same(selection, page.Controller.State.Selection);
                Assert.Same(mounted, page.SpaceWorkspaceContent);
            }
            finally { await page.AbortHomeCloseAsync(preparation).WaitAsync(Bound); }

            await PumpUntilAsync(fixture.Window, () => ContextColumn(fixture).ActualWidth == 0);
            Assert.True(page.Controller.State.SidebarCollapsed);
            Assert.Same(selection, page.Controller.State.Selection);
            Assert.Same(mounted, page.SpaceWorkspaceContent);
            await ResizeAsync(fixture, 1200, 232);
            Assert.False(page.Controller.State.SidebarCollapsed);
            Assert.Equal(reads, SourceReads(fixture));
        });

    private sealed record Fixture(Rig Rig, SpacesBrowseAppContentSession Session,
        SpacesHomePage Page, Window Window, Func<int> SidebarReads);

    private static async Task WithWorkspaceAsync(int initialWidth, Func<Fixture, Task> verify)
    {
        var sidebarReads = 0;
        var rig = new Rig(withHome: true, allowConversationWrites: true, getInstalledAsync: token =>
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref sidebarReads);
            return Task.FromResult<IReadOnlyList<InstalledExtensionPackage>>([]);
        });
        SpacesBrowseAppContentSession? session = null;
        Window? window = null;
        var failures = new List<Exception>();
        try
        {
            var space = await rig.Spaces.CreateAsync("Responsive fixture");
            rig.Row = rig.Row! with { SpaceId = space.Id };
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            session = rig.Session($"9to1://spaces/selected/{space.Id:D}/chat/{rig.Row.Id:D}",
                profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var page = Assert.IsType<SpacesHomePage>(root.Content);
            window = new Window { Content = root, Width = initialWidth, Height = 900 };
            window.Show();
            var fixture = new Fixture(rig, session, page, window, () => Volatile.Read(ref sidebarReads));
            await ResizeAsync(fixture, initialWidth, initialWidth < 750 ? 0 : 232);
            await verify(fixture);
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
                if (session is not null) await session.CloseAndDrainAsync().WaitAsync(Bound);
            }
            catch (Exception error) { failures.Add(error); }
            try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
            catch (Exception error) { failures.Add(error); }
            try { window?.Close(); }
            catch (Exception error) { failures.Add(error); }
            try { rig.Dispose(); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("Responsive Spaces assertions and original cleanup failed.", failures);
    }

    private static T Authored<T>(SpacesHomePage page, string id) where T : Control =>
        Assert.IsType<T>(Assert.Single(AuthoredCuiControls.Enumerate(page), control => control.Name == id));

    private static TextBox Composer(NewChatPage page) =>
        Assert.Single(page.GetVisualDescendants().OfType<TextBox>(),
            input => AutomationProperties.GetAutomationId(input) == "composer-input");

    private static ColumnDefinition ContextColumn(Fixture fixture) =>
        Assert.IsType<Grid>(Authored<CuiContainerControl>(fixture.Page, "spaces-layout").Layout).ColumnDefinitions[1];

    private static (int History, int Rows, int Models, int Writes, int Sidebar) SourceReads(Fixture fixture) =>
        (Volatile.Read(ref fixture.Rig.SpaceHistoryReads), Volatile.Read(ref fixture.Rig.RowReads),
         Volatile.Read(ref fixture.Rig.ModelReads), Volatile.Read(ref fixture.Rig.ConversationWrites),
         fixture.SidebarReads());

    private static async Task ResizeAsync(Fixture fixture, double width, double contextWidth)
    {
        fixture.Window.Width = width;
        await PumpUntilAsync(fixture.Window, () => Math.Abs(fixture.Page.Bounds.Width - width) < 0.1 &&
            Math.Abs(ContextColumn(fixture).ActualWidth - contextWidth) < 0.1);
    }

    private static async Task PumpUntilAsync(Window window, Func<bool> ready)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            await PumpAsync(window);
            if (ready()) return;
            await Task.Delay(10);
        } while (elapsed.Elapsed < Bound);
        Assert.True(ready(), "The real native layout did not reach its expected viewport state.");
    }

    private static async Task PumpAsync(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
