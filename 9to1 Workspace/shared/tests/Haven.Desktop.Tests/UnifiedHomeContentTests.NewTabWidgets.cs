using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaFact]
    public async Task New_Tab_widget_picker_persists_placements_and_timer_options_in_the_current_profile()
    {
        var token = TestContext.Current.CancellationToken;
        var paths = new LayoutPaths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var preferences = new NewTabLayoutPreferences(settings, "original-home-profile", _ => Task.CompletedTask);
        var otherProfile = new NewTabLayoutPreferences(settings, "other-profile", _ => Task.CompletedTask);
        var sources = new NewTabWidgetSources(null, null, null, null, _ => Task.CompletedTask);
        var page = new UnifiedHomePage(Request("browse://newtab"), new Registry(),
            () => throw new InvalidOperationException("New Tab must not construct Dashboard."),
            () => throw new InvalidOperationException("New Tab must not construct Discover."),
            (_, _) => throw new InvalidOperationException("Widget placement must not navigate."),
            (_, _) => throw new InvalidOperationException("Widgets must not prepare an AI draft."),
            newTabLayout: preferences, newTabWidgets: sources);
        Window? window = null;
        try
        {
            await page.InitializeAsync(token);
            window = Show(page);
            SettleFixtureLayout(window);

            async Task ClickWidget(string id, string? accessibleName = null)
            {
                var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(), control =>
                    control.Name == id && control.IsEffectivelyVisible &&
                    (accessibleName is null || AutomationProperties.GetName(control) == accessibleName));
                Assert.True(button.IsEnabled);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
                SettleFixtureLayout(window);
            }

            await ClickWidget("newtab-widget-picker-open");
            var query = SettingsControl<TextBox>(page, "newtab-widget-search");
            query.Text = "focus";
            SettleFixtureLayout(window);
            await ClickWidget("newtab-widget-add", "Add Focus timer");
            var placement = Assert.Single((await preferences.ReadAsync(token)).Layout.Widgets, item => item.Id == "focus-timer");
            Assert.Equal("focus-timer", placement.Id);
            Assert.Equal("timer:25", placement.SourceId);
            Assert.Equal(new[] { "up-next", "pinned-note" },
                (await otherProfile.ReadAsync(token)).Layout.Widgets.Select(item => item.Id));

            await ClickWidget("newtab-widget-picker-close");
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == "25:00");
            await ClickWidget("newtab-widget-timer-toggle");
            await ClickWidget("newtab-widget-timer-reset");
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), control => control.IsEffectivelyVisible &&
                control.Text == "Reset the running timer to its configured duration?");

            var unavailable = await sources.ReadAsync(new("up-next", "planner:calendar"), token);
            Assert.Empty(unavailable.Items);
            Assert.Contains("Planner", unavailable.Status);
        }
        finally
        {
            await page.CloseAndDrainAsync();
            window?.Close();
            Directory.Delete(paths.DataDirectory, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData(1440, 900, 3)]
    [InlineData(720, 520, 2)]
    public async Task New_Tab_widget_picker_centres_preview_cards_and_returns_focus_at_supported_widths(int width, int height, int columns)
    {
        var token = TestContext.Current.CancellationToken;
        var paths = new LayoutPaths();
        var preferences = new NewTabLayoutPreferences(new VersionedAtomicSettingsStore(paths), "original-home-profile", _ => Task.CompletedTask);
        var sources = new NewTabWidgetSources(null, null, null, null, _ => Task.CompletedTask);
        var page = new UnifiedHomePage(Request("browse://newtab"), new Registry(),
            () => throw new InvalidOperationException("New Tab must not construct Dashboard."),
            () => throw new InvalidOperationException("New Tab must not construct Discover."),
            (_, _) => throw new InvalidOperationException("The catalogue must not navigate."),
            (_, _) => throw new InvalidOperationException("Widgets must not prepare an AI draft."),
            newTabLayout: preferences, newTabWidgets: sources);
        Window? window = null;
        try
        {
            await page.InitializeAsync(token);
            window = new Window { Width = width, Height = height, Content = page };
            window.Show();
            SettleFixtureLayout(window);

            var trigger = SettingsControl<Button>(page, "newtab-widget-picker-open");
            trigger.BringIntoView();
            SettleFixtureLayout(window);
            ClickSettingsButton(window, trigger);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            SettleFixtureLayout(window);

            var tray = SettingsControl<CuiContainerControl>(page, "newtab-widget-picker");
            var input = SettingsControl<TextBox>(page, "newtab-widget-search");
            Assert.True(input.IsFocused);
            var trayCentre = tray.TranslatePoint(new Rect(tray.Bounds.Size).Center, page)!.Value;
            Assert.InRange(trayCentre.X, page.Bounds.Width / 2 - 1, page.Bounds.Width / 2 + 1);
            Assert.InRange(trayCentre.Y, page.Bounds.Height / 2 - 1, page.Bounds.Height / 2 + 1);
            var cards = tray.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "newtab-widget-add").ToArray();
            Assert.Equal(6, cards.Length);
            Assert.Equal(columns, cards.GroupBy(button => Math.Round(button.TranslatePoint(default, tray)!.Value.Y)).First().Count());
            Assert.Equal(2, cards.Count(button => !button.IsEnabled));
            var body = SettingsControl<CuiContainerControl>(tray, "newtab-widget-picker-scroll");
            var scroll = Assert.Single(body.GetVisualDescendants().OfType<ScrollViewer>());
            if (columns == 2) Assert.True(scroll.Extent.Height > scroll.Viewport.Height);

            PressSettingsKey(window, PhysicalKey.Escape);
            SettleFixtureLayout(window);
            Assert.True(trigger.IsFocused);
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control.Name == "newtab-widget-picker");
        }
        finally
        {
            await page.CloseAndDrainAsync();
            window?.Close();
            Directory.Delete(paths.DataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task New_Tab_widget_sources_read_and_mutate_only_the_selected_real_owner_records()
    {
        var token = TestContext.Current.CancellationToken;
        var paths = new LayoutPaths();
        try
        {
            var database = new SqliteDatabase(paths);
            await database.InitializeAsync(token);
            var planner = new PlannerRepository(database);
            var conversations = new ConversationRepository(database);
            await planner.EnsureDefaultsAsync(token);
            var sources = new NewTabWidgetSources(planner, conversations, null, null, _ => Task.CompletedTask, libraryAvailable: true);
            var list = $"list:{PlannerDefaults.PersonalCollectionId:D}";
            var tasks = new NewTabWidgetPlacement("my-tasks", list);
            var calendar = new NewTabWidgetPlacement("up-next", "planner:calendar");
            var definition = Assert.Single(NewTabWidgetSources.Catalogue, item => item.Id == "continue-working");
            Assert.Equal(NewTabWidgetSources.LibrarySource, definition.InitialSource);
            var choices = await sources.ChoicesAsync(definition.Id, token);
            Assert.Equal(new[] { NewTabWidgetSources.LibrarySource, NewTabWidgetSources.SpacesRecentSource },
                choices.Select(choice => choice.Id));
            var libraryPlacement = new NewTabWidgetPlacement(definition.Id, definition.InitialSource);
            var library = await sources.ReadAsync(libraryPlacement, token);
            Assert.Contains("Recently opened documents are unavailable", library.Status);
            Assert.Equal("browse://home/library", Assert.Single(library.Items).Address?.Value);
            var absentLibrary = new NewTabWidgetSources(planner, conversations, null, null, _ => Task.CompletedTask);
            Assert.Equal(NewTabWidgetSources.SpacesRecentSource,
                Assert.Single(await absentLibrary.ChoicesAsync(definition.Id, token)).Id);
            Assert.Empty((await absentLibrary.ReadAsync(libraryPlacement, token)).Items);
            var recent = new NewTabWidgetPlacement(definition.Id, NewTabWidgetSources.SpacesRecentSource);
            Assert.Empty((await sources.ReadAsync(tasks, token)).Items);
            Assert.Empty((await sources.ReadAsync(calendar, token)).Items);
            Assert.Empty((await sources.ReadAsync(recent, token)).Items);

            var now = DateTimeOffset.UtcNow;
            var task = new PlannerTask(Guid.NewGuid(), PlannerDefaults.PersonalCollectionId, null, "Prepare notes", "", PlannerPriority.Medium,
                PlannerTaskStatus.Planned, "[]", 30, null, now.AddDays(1), null, null, null, 0, now, now);
            var saved = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Saved discussion",
                null, null, false, false, now, now);
            var temporary = saved with { Id = Guid.NewGuid(), Title = "Temporary discussion", IsTemporary = true };
            await planner.UpsertTaskAsync(task, token);
            await planner.UpsertEventAsync(new PlannerEvent(
                Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Seminar", "", "", now.AddHours(1), now.AddHours(2), false,
                null, null, false, null, null, now, now, null, "UTC"), token);
            await conversations.UpsertConversationAsync(saved, token);
            await conversations.UpsertConversationAsync(temporary, token);

            Assert.Equal("Seminar", Assert.Single((await sources.ReadAsync(calendar, token)).Items).Title);
            Assert.Equal($"9to1://spaces/chat/{saved.Id:D}",
                Assert.Single((await sources.ReadAsync(recent, token)).Items).Address?.Value);
            Assert.Equal(task.Id, Assert.Single((await sources.ReadAsync(tasks, token)).Items).TaskId);
            Assert.NotNull(await sources.CompleteTaskAsync($"list:{Guid.NewGuid():D}", task.Id, token));
            Assert.Equal(PlannerTaskStatus.Planned, (await planner.GetTaskAsync(task.Id, token))?.Status);
            Assert.Null(await sources.CompleteTaskAsync(list, task.Id, token));
            Assert.Equal(PlannerTaskStatus.Completed, (await planner.GetTaskAsync(task.Id, token))?.Status);
            Assert.Empty((await sources.ReadAsync(tasks, token)).Items);
        }
        finally { Directory.Delete(paths.DataDirectory, recursive: true); }
    }
}
