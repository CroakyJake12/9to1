using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
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
                if (id == "newtab-widget-add") ClickSettingsButton(window, button);
                else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

            Assert.True(query.IsFocused);
            PressSettingsKey(window, PhysicalKey.Escape);
            SettleFixtureLayout(window);
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control.Name == "newtab-widget-picker");
            var pickerTrigger = SettingsControl<Button>(page, "newtab-widget-picker-open");
            Assert.True(pickerTrigger.IsFocused);
            Button DragHandle(string title) => Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "newtab-widget-drag" && AutomationProperties.GetName(button) == "Reorder " + title);
            var sourceHandle = DragHandle("Up next");
            var targetHandle = DragHandle("Pinned note");
            sourceHandle.BringIntoView();
            SettleFixtureLayout(window);
            var sourcePoint = sourceHandle.TranslatePoint(new Rect(sourceHandle.Bounds.Size).Center, window)!.Value;
            var targetPoint = targetHandle.TranslatePoint(new Rect(targetHandle.Bounds.Size).Center, window)!.Value;
            window.MouseMove(sourcePoint);
            window.MouseDown(sourcePoint, MouseButton.Left);
            window.MouseMove(targetPoint, RawInputModifiers.LeftMouseButton);
            window.MouseUp(targetPoint, MouseButton.Left);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            Assert.Equal(new[] { "pinned-note", "up-next", "focus-timer" },
                (await preferences.ReadAsync(token)).Layout.Widgets.Select(item => item.Id));
            SettleFixtureLayout(window);
            Assert.True(DragHandle("Up next").IsFocused);

            async Task MoveWidget(PhysicalKey key, params string[] expected)
            {
                Assert.True(DragHandle("Up next").Focus());
                window.KeyPressQwerty(key, RawInputModifiers.Alt);
                window.KeyReleaseQwerty(key, RawInputModifiers.Alt);
                await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
                SettleFixtureLayout(window);
                Assert.Equal(expected, (await preferences.ReadAsync(token)).Layout.Widgets.Select(item => item.Id));
                Assert.True(DragHandle("Up next").IsFocused);
            }

            await MoveWidget(PhysicalKey.ArrowLeft, "up-next", "pinned-note", "focus-timer");
            await MoveWidget(PhysicalKey.ArrowUp, "up-next", "pinned-note", "focus-timer");
            await MoveWidget(PhysicalKey.ArrowRight, "pinned-note", "up-next", "focus-timer");
            await MoveWidget(PhysicalKey.ArrowDown, "pinned-note", "focus-timer", "up-next");
            await MoveWidget(PhysicalKey.ArrowRight, "pinned-note", "focus-timer", "up-next");

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
    [InlineData("widget-availability")]
    [InlineData("candidate-preview")]
    public async Task New_Tab_widget_reentrant_publication_refuses_the_superseded_move_before_any_save(string boundary)
    {
        var token = TestContext.Current.CancellationToken;
        var paths = new LayoutPaths();
        var actual = new VersionedAtomicSettingsStore(paths);
        var settings = new WidgetPublicationSettings(actual);
        var preferences = new NewTabLayoutPreferences(settings, "original-home-profile", _ => Task.CompletedTask);
        var page = CreateWidgetPublicationPage(preferences);
        Window? window = null; CuiViewModel? bindings = null; Task? reentry = null;
        var reentered = false;
        var originalOrder = new[] { "up-next", "pinned-note" };
        var attemptedOrder = new[] { "pinned-note", "up-next" };
        void Reenter(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (reentered || bindings is null) return;
            var atBoundary = boundary == "widget-availability"
                ? args.PropertyName == "WidgetCanOpenEditor" && bindings.Get("WidgetCanOpenEditor") is false
                : args.PropertyName == "WidgetCards" && PublishedWidgetOrder(bindings).SequenceEqual(attemptedOrder);
            if (!atBoundary) return;
            reentered = true;
            // A real publication callback changes the selection through the normal owner. No
            // private handler, field mutation, synthetic generation or replacement source is used.
            reentry = page.DispatchAsync("home.newtab", null, token).AsTask();
        }
        try
        {
            await page.InitializeAsync(token);
            window = Show(page); SettleFixtureLayout(window);
            var picker = SettingsControl<Button>(page, "newtab-widget-picker-open");
            bindings = Assert.IsType<CuiViewModel>(picker.DataContext);
            Assert.Equal(originalOrder, PublishedWidgetOrder(bindings));
            bindings.PropertyChanged += Reenter;
            var handle = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "newtab-widget-drag" && AutomationProperties.GetName(button) == "Reorder Up next");
            Assert.True(handle.Focus());
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
            Assert.True(reentered, "The intended actual binding publication was reached synchronously.");
            Assert.NotNull(reentry);
            await reentry.WaitAsync(TimeSpan.FromSeconds(20), token);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            SettleFixtureLayout(window);
            Assert.Equal(0, settings.CommitCalls);
            Assert.Equal(originalOrder, (await preferences.ReadAsync(token)).Layout.Widgets.Select(widget => widget.Id));
            Assert.Equal(originalOrder, PublishedWidgetOrder(bindings)); // The unstarted preview was withdrawn too.
            Assert.True(page.IsAlive);
            Assert.True(SettingsControl<Button>(page, "newtab-widget-picker-open").IsEffectivelyEnabled);
            Assert.Empty((await actual.ExportAsync(token)).Settings);
            await page.CloseAndDrainAsync(); // A settled pre-effect refusal does not become a close failure.
        }
        finally
        {
            if (bindings is not null) bindings.PropertyChanged -= Reenter;
            if (reentry is not null) await reentry;
            await page.CloseAndDrainAsync(); window?.Close();
            Directory.Delete(paths.DataDirectory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task New_Tab_widget_close_still_joins_an_acquired_save_and_retains_its_late_failure()
    {
        var token = TestContext.Current.CancellationToken;
        var paths = new LayoutPaths();
        var actual = new VersionedAtomicSettingsStore(paths);
        var failure = new IOException("The original widget preference commit failed after close began.");
        var settings = new WidgetPublicationSettings(actual) { HoldCommit = true, CommitFailure = failure };
        var preferences = new NewTabLayoutPreferences(settings, "original-home-profile", _ => Task.CompletedTask);
        var page = CreateWidgetPublicationPage(preferences);
        Window? window = null; Task? close = null; Task? actions = null;
        var closeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closePublished = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Establish the external owner on the UI context before headless input enters the
        // page action. The source's CommitEntered signal is not an external-close context.
        var externalCloseOwner = JoinExternalCloseAsync();
        async Task JoinExternalCloseAsync()
        {
            await closeRequested.Task;
            try
            {
                var original = page.CloseAndDrainAsync();
                closePublished.TrySetResult(original);
                await original;
            }
            catch (Exception error) { closePublished.TrySetException(error); throw; }
        }
        try
        {
            await page.InitializeAsync(token);
            window = Show(page); SettleFixtureLayout(window);
            var handle = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "newtab-widget-drag" && AutomationProperties.GetName(button) == "Reorder Up next");
            Assert.True(handle.Focus());
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
            await settings.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            actions = page.WhenActionsIdleAsync();
            closeRequested.TrySetResult();
            close = await closePublished.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            Assert.False(actions.IsCompleted); Assert.False(close.IsCompleted);
            Assert.Equal(1, settings.CommitCalls);
            settings.CommitRelease.TrySetResult();
            await Record.ExceptionAsync(() => actions);
            var error = await Record.ExceptionAsync(() => close);
            Assert.NotNull(error);
            Assert.Contains(WidgetPublicationCauses(error), cause => ReferenceEquals(cause, failure));
            var externalError = await Record.ExceptionAsync(() => externalCloseOwner);
            Assert.NotNull(externalError);
            Assert.Contains(WidgetPublicationCauses(externalError), cause => ReferenceEquals(cause, failure));
            Assert.Equal(1, settings.CommitCalls); // No retry or replacement of the acquired write.
            Assert.Empty((await actual.ExportAsync(token)).Settings);
        }
        finally
        {
            settings.CommitRelease.TrySetResult();
            if (actions is not null) await Record.ExceptionAsync(() => actions);
            closeRequested.TrySetResult();
            close ??= await closePublished.Task;
            await Record.ExceptionAsync(() => close);
            await Record.ExceptionAsync(() => externalCloseOwner);
            window?.Close(); Directory.Delete(paths.DataDirectory, recursive: true);
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
    private static UnifiedHomePage CreateWidgetPublicationPage(NewTabLayoutPreferences preferences) => new(
        Request("browse://newtab"), new Registry(),
        () => throw new InvalidOperationException("Widget ordering must not create Dashboard."),
        () => throw new InvalidOperationException("Widget ordering must not create Discover."),
        (_, _) => throw new InvalidOperationException("Widget ordering must not navigate to another workspace."),
        (_, _) => throw new InvalidOperationException("Widget ordering must not prepare an AI draft."),
        newTabLayout: preferences,
        newTabWidgets: new NewTabWidgetSources(null, null, null, null, _ => Task.CompletedTask));

    private static string[] PublishedWidgetOrder(CuiViewModel bindings)
    {
        Assert.True(bindings.TryGetValue("WidgetCards", out var value));
        // The actual CUI projection is a private record type with public Id members. Observe that
        // emitted projection without replacing the binding context or injecting widget authority.
        return Assert.IsAssignableFrom<IEnumerable<object>>(value).Select(item =>
        {
            var property = item.GetType().GetProperty("Id"); Assert.NotNull(property);
            return Assert.IsType<string>(property.GetValue(item));
        }).ToArray();
    }

    private static IEnumerable<Exception> WidgetPublicationCauses(Exception error)
    {
        yield return error;
        if (error is AggregateException group)
            foreach (var child in group.InnerExceptions) foreach (var cause in WidgetPublicationCauses(child)) yield return cause;
        else if (error.InnerException is { } child)
            foreach (var cause in WidgetPublicationCauses(child)) yield return cause;
    }

    private sealed class WidgetPublicationSettings(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore, IVersionedSettingsGuardedCompareExchange
    {
        internal int CommitCalls;
        internal bool HoldCommit;
        internal IOException? CommitFailure;
        internal TaskCompletionSource CommitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CommitRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => actual.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => actual.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => actual.ImportAsync(value, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token) =>
            actual.CompareExchangeAsync(key, expected, replacement, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
            IReadOnlyDictionary<string, string?> guards, CancellationToken token) => actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
            IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            CommitCalls++; CommitEntered.TrySetResult();
            if (HoldCommit) await CommitRelease.Task; // Deliberately retain an actual write that ignores cancellation.
            if (CommitFailure is { } failure) throw failure;
            return await actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, admission, token);
        }
    }
}
