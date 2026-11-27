using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;

namespace Haven.Desktop.Tests;

public sealed class HomeDashboardAttentionTests
{
    [Fact]
    public async Task Attention_reads_only_actual_pending_requests_for_the_exact_actor_without_creating_permissions()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        var expected = await fixture.SubmitAsync("Research", fixture.Actors.Current, token);
        await fixture.SubmitAsync("Another profile", fixture.Actors.Current! with { ProfileId = "other" }, token);
        await fixture.SubmitAsync("Unbound", null, token);
        var writes = fixture.Store.Writes;
        var snapshot = await fixture.Source.ReadAsync(token);
        var observed = Assert.Single(snapshot.Pending);
        Assert.Equal(expected.RequestId, observed.RequestId);
        Assert.Equal("Research", observed.CallerName);
        Assert.Equal("read.source", observed.ActionName);
        Assert.True(snapshot.ObservedAt >= observed.RequestedAt);
        Assert.False(await fixture.Source.ReviewAsync(observed, () => false, token));
        Assert.Empty(fixture.Opened);
        Assert.Equal(writes, fixture.Store.Writes);
        Assert.Empty((await fixture.Permissions.GetSnapshotAsync(cancellationToken: token)).Grants);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("settled")]
    [InlineData("publication")]
    public async Task A_delayed_review_revalidates_actor_pending_request_and_current_publication(string change)
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        await fixture.SubmitAsync("Research", fixture.Actors.Current, token);
        var captured = Assert.Single((await fixture.Source.ReadAsync(token)).Pending);
        var current = true;
        fixture.Actors.HoldNext();
        var review = fixture.Source.ReviewAsync(captured, () => current, token);
        try
        {
            await fixture.Actors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            if (change == "profile") fixture.Actors.Current = fixture.Actors.Current! with { AuthenticationRevision = "changed" };
            if (change == "settled") await fixture.Permissions.DecideAsync(captured.RequestId, HomeApprovalChoice.Decline, cancellationToken: token);
            if (change == "publication") current = false;
            fixture.Actors.Release.TrySetResult();
            if (change == "profile") await Assert.ThrowsAsync<UnauthorizedAccessException>(() => review);
            else Assert.False(await review);
            Assert.Empty(fixture.Opened);
            Assert.Empty((await fixture.Permissions.GetSnapshotAsync(cancellationToken: token)).Grants);
        }
        finally { fixture.Actors.Release.TrySetResult(); }
    }

    [AvaloniaTheory]
    [InlineData("Glow", 1440, 900)]
    [InlineData("Glow", 720, 650)]
    [InlineData("Professional", 1440, 900)]
    [InlineData("Professional", 720, 650)]
    public async Task Actual_pending_attention_precedes_Continue_working_and_reviews_the_exact_request(
        string themeName, int width, int height)
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(marshalReviewToUi: true);
        var pending = await fixture.SubmitAsync("Research sources", fixture.Actors.Current, token);
        await fixture.SubmitAsync("Foreign source", fixture.Actors.Current! with { ProfileId = "other" }, token);
        using var bus = new HavenEventBus();
        using var theme = new CuiThemePreferenceContext(new ThemeSource(themeName));
        using var page = CreatePage(bus, fixture.Source, theme: theme);
        page.ConfigureHomeBriefingLibrary(true);
        var window = new Window { Content = page, Width = width, Height = height };
        try
        {
            window.Show(); await page.ActivateAsync(token); Settle(window);
            var banner = Named<Control>(page, "dashboard-attention");
            var title = Named<TextBlock>(page, "dashboard-attention-title");
            var review = Named<Button>(page, "dashboard-attention-review");
            var continuing = Named<Control>(page, "dashboard-continue-working");
            Assert.True(banner.IsVisible);
            Assert.Equal("Research sources is waiting for your review", title.Text);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Foreign source", StringComparison.Ordinal) == true);
            Assert.True(title.TryFindResource("CuiEffectiveThemeName", out var actualTheme)); Assert.Equal(themeName, actualTheme);
            Assert.True(title.TryFindResource("CuiWorkspaceBrush", out var background));
            window.Background = Assert.IsAssignableFrom<IBrush>(background);
            Assert.NotNull(title.Foreground);
            Settle(window);
            AssertInside(page, banner); AssertInside(page, review);
            Assert.True(review.IsEffectivelyEnabled);
            var bannerPoint = banner.TranslatePoint(default, page); var continuePoint = continuing.TranslatePoint(default, page);
            Assert.NotNull(bannerPoint); Assert.NotNull(continuePoint);
            Assert.True(continuePoint.Value.Y >= bannerPoint.Value.Y + banner.Bounds.Height);
            Assert.InRange(banner.Bounds.Height, 1, 200);
            Assert.Contains("1 pending", Named<TextBlock>(page, "dashboard-attention-freshness").Text);
            if (Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") is { Length: > 0 } captures)
            {
                Directory.CreateDirectory(captures);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                frame.Save(Path.Combine(captures, $"home-dashboard-attention-{themeName}-{width}x{height}.png"));
            }
            var writes = fixture.Store.Writes;
            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Loader(page).WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(pending.RequestId, Assert.Single(fixture.Opened));
            Assert.Equal(writes, fixture.Store.Writes);
            Assert.Equal(HomePermissionRequestState.PendingApproval,
                (await fixture.Permissions.ReadRequestObservationAsync(pending.RequestId, token))!.State);
            Assert.Empty((await fixture.Permissions.GetSnapshotAsync(cancellationToken: token)).Grants);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_or_customised_Dashboard_does_not_invent_attention_or_change_saved_widget_layout(bool briefing)
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(marshalReviewToUi: true);
        using var bus = new HavenEventBus();
        var settings = new Settings();
        var saved = new DashboardWidgetLayoutState(1, new() { ["home"] = [] }, UsesHomeBriefing: briefing);
        await settings.SetAsync("dashboard.widget-layouts.v1", saved, token);
        using var page = CreatePage(bus, fixture.Source, settings);
        var reads = fixture.Store.Reads;
        try
        {
            await page.ActivateAsync(token);
            Assert.Equal(briefing, page.ShowsHomeBriefing);
            Assert.False(Named<Control>(page, "dashboard-attention").IsVisible);
            Assert.False(Named<Control>(page, "dashboard-attention-status").IsVisible);
            Assert.Equal(briefing, (await settings.GetAsync<DashboardWidgetLayoutState>("dashboard.widget-layouts.v1", token))!.UsesHomeBriefing);
            if (briefing) Assert.True(fixture.Store.Reads > reads);
            else Assert.Equal(reads, fixture.Store.Reads);
            Assert.Empty(fixture.Opened);
        }
        finally { await page.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task A_late_attention_read_cannot_republish_a_deactivated_Dashboard()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(marshalReviewToUi: true);
        await fixture.SubmitAsync("Research", fixture.Actors.Current, token);
        using var bus = new HavenEventBus();
        using var page = CreatePage(bus, fixture.Source);
        var window = new Window { Content = page, Width = 720, Height = 650 };
        try
        {
            window.Show(); await page.ActivateAsync(token); Settle(window);
            fixture.Store.HoldNext();
            Named<Button>(page, "dashboard-attention-update").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await fixture.Store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(Named<Control>(page, "dashboard-attention").IsVisible);
            page.Deactivate();
            fixture.Store.Release.TrySetResult();
            await Loader(page).WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(Named<Control>(page, "dashboard-attention").IsVisible);
            Assert.Empty(fixture.Opened);
            await page.ActivateAsync(token); Settle(window);
            Assert.True(Named<Control>(page, "dashboard-attention").IsVisible);
        }
        finally { fixture.Store.Release.TrySetResult(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_held_review_cannot_retarget_the_new_pending_banner_after_refresh()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(marshalReviewToUi: true);
        var first = await fixture.SubmitAsync("First request", fixture.Actors.Current, token);
        var second = await fixture.SubmitAsync("Second request", fixture.Actors.Current, token);
        using var bus = new HavenEventBus();
        using var page = CreatePage(bus, fixture.Source);
        var window = new Window { Content = page, Width = 720, Height = 650 };
        try
        {
            window.Show(); await page.ActivateAsync(token); Settle(window);
            fixture.Actors.HoldNext();
            Named<Button>(page, "dashboard-attention-review").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await fixture.Actors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            await fixture.Permissions.DecideAsync(first.RequestId, HomeApprovalChoice.Decline, cancellationToken: token);
            Named<Button>(page, "dashboard-attention-refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(() => Named<TextBlock>(page, "dashboard-attention-title").Text?.StartsWith("Second request", StringComparison.Ordinal) == true, token);
            fixture.Actors.Release.TrySetResult();
            await Loader(page).WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Empty(fixture.Opened);
            Assert.StartsWith("Second request", Named<TextBlock>(page, "dashboard-attention-title").Text);
            Assert.True(Named<Button>(page, "dashboard-attention-review").IsEffectivelyEnabled);
            Named<Button>(page, "dashboard-attention-review").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Loader(page).WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(second.RequestId, Assert.Single(fixture.Opened));
        }
        finally { fixture.Actors.Release.TrySetResult(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Failed_refresh_keeps_unknown_state_visible_and_withdraws_the_old_review_target()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(marshalReviewToUi: true);
        await fixture.SubmitAsync("Research", fixture.Actors.Current, token);
        using var bus = new HavenEventBus();
        using var page = CreatePage(bus, fixture.Source);
        var window = new Window { Content = page, Width = 720, Height = 650 };
        var failure = new IOException("The canonical permission journal could not be refreshed.");
        try
        {
            window.Show(); await page.ActivateAsync(token); Settle(window);
            fixture.Store.HoldNext(failure);
            Named<Button>(page, "dashboard-attention-update").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await fixture.Store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(Named<Control>(page, "dashboard-attention").IsVisible);
            Assert.Equal("Checking requests…", Named<TextBlock>(page, "dashboard-attention-status-text").Text);
            fixture.Store.Release.TrySetResult();
            await Record.ExceptionAsync(() => Loader(page).WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), token));
            Settle(window);
            Assert.False(Named<Control>(page, "dashboard-attention").IsVisible);
            Assert.False(Named<Button>(page, "dashboard-attention-review").IsEnabled);
            Assert.Equal("Requests are unavailable. Refresh to try again.", Named<TextBlock>(page, "dashboard-attention-status-text").Text);
            AssertInside(page, Named<Button>(page, "dashboard-attention-refresh"));
            Assert.Empty(fixture.Opened);
            var close = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            Assert.NotNull(close); Assert.Contains(Leaves(close!), item => ReferenceEquals(item, failure));
        }
        finally
        {
            fixture.Store.Release.TrySetResult();
            await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            window.Content = null; window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Closing_joins_a_real_held_permission_read_and_retains_its_late_failure()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(marshalReviewToUi: true);
        await fixture.SubmitAsync("Research", fixture.Actors.Current, token);
        using var bus = new HavenEventBus();
        using var page = CreatePage(bus, fixture.Source);
        var failure = new IOException("Original Home permission read failed after close was requested.");
        fixture.Store.HoldNext(failure);
        var activation = page.ActivateAsync(token);
        Task? close = null;
        try
        {
            await fixture.Store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            fixture.Store.Release.TrySetResult();
            await Record.ExceptionAsync(() => activation);
            var observed = await Record.ExceptionAsync(() => close);
            Assert.NotNull(observed);
            Assert.Contains(Leaves(observed!), item => ReferenceEquals(item, failure));
            Assert.Empty(fixture.Opened);
        }
        finally
        {
            fixture.Store.Release.TrySetResult();
            await Record.ExceptionAsync(() => activation);
            await Record.ExceptionAsync(() => close ?? page.CloseAndDrainAsync());
        }
    }

    [AvaloniaFact]
    public async Task Owner_cancelled_permission_read_settles_without_fabricating_pending_requests()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(marshalReviewToUi: true);
        await fixture.SubmitAsync("Research", fixture.Actors.Current, token);
        using var bus = new HavenEventBus();
        using var page = CreatePage(bus, fixture.Source);
        fixture.Store.HoldNext(cooperative: true);
        var activation = page.ActivateAsync(token);
        try
        {
            await fixture.Store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            var close = page.CloseAndDrainAsync();
            await close.WaitAsync(TimeSpan.FromSeconds(10), token);
            await activation;
            Assert.Empty(fixture.Opened);
        }
        finally { fixture.Store.Release.TrySetResult(); await page.CloseAndDrainAsync(); }
    }

    private static NewDashboardPage CreatePage(HavenEventBus bus, HomeDashboardAttentionSource source,
        Settings? settings = null, CuiThemePreferenceContext? theme = null)
    {
        var page = new NewDashboardPage(bus, null!, null!, null!, null!, settings ?? new Settings(), new Dashboard(), new Layout(), [], theme);
        page.EnableHomeBriefing(null, null);
        page.ConfigureHomeBriefingAttention(source);
        return page;
    }

    private static CuiControlLoader Loader(NewDashboardPage page) => (CuiControlLoader)typeof(NewDashboardPage)
        .GetField("_dashboardLoader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
    private static T Named<T>(Control root, string name) where T : Control => Assert.Single(Authored(root).OfType<T>(), control => control.Name == name);
    private static IEnumerable<Control> Authored(Control root)
    {
        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Control>(); pending.Push(root);
        while (pending.TryPop(out var control))
        {
            if (!seen.Add(control)) continue;
            yield return control;
            foreach (var child in control.GetLogicalChildren().OfType<Control>()) pending.Push(child);
            if (control is CuiContainerControl container) foreach (var child in container.AuthoredChildren) pending.Push(child);
        }
    }
    private static void Settle(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void AssertInside(Control viewport, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        var point = control.TranslatePoint(default, viewport); Assert.NotNull(point);
        Assert.InRange(point.Value.X, -1, viewport.Bounds.Width);
        Assert.InRange(point.Value.Y, -1, viewport.Bounds.Height);
        Assert.True(point.Value.X + control.Bounds.Width <= viewport.Bounds.Width + 1);
        Assert.True(point.Value.Y + control.Bounds.Height <= viewport.Bounds.Height + 1);
    }
    private static async Task UntilAsync(Func<bool> condition, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(5, deadline.Token);
    }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException aggregate && aggregate.InnerExceptions.Count > 0
        ? aggregate.InnerExceptions.SelectMany(Leaves) : [error];

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "dashboard-attention-" + Guid.NewGuid().ToString("N"));
        internal readonly Actors Actors = new();
        internal readonly List<string> Opened = [];
        internal readonly HeldStore Store;
        internal readonly HomePermissionTrustService Permissions;
        internal readonly HomeDashboardAttentionSource Source;
        internal Fixture(bool marshalReviewToUi = false)
        {
            Store = new(new FileHomeCoreStateStore(Path.Combine(_root, "home.json")));
            Permissions = new(Store, (_, _) => new(HomePermissionRisk.Elevated, false, false, true));
            Source = new(Permissions, Actors, Actors.Current!, async (id, actor, current, token) =>
            {
                if (!marshalReviewToUi || Dispatcher.UIThread.CheckAccess()) return Open();
                return await Dispatcher.UIThread.InvokeAsync(Open);
                bool Open()
                {
                    token.ThrowIfCancellationRequested();
                    Assert.Equal(Actors.Current, actor);
                    if (!current()) return false;
                    Opened.Add(id); return true;
                }
            });
        }
        internal Task<HomePermissionAuthorization> SubmitAsync(string title, AuthenticatedResourceActor? actor, CancellationToken token)
        {
            var id = Guid.NewGuid().ToString("N");
            return Permissions.AuthorizeAsync(new(id, new("research", title, "test", "v1", true), "session",
                new("research", "read.source", [new("source", id)]), new(["source"], 1, [new("source", id)], false,
                    ResourceBinding: actor is null ? null : new(1, actor, [new("source", id, "1", ResourceAccess.Read)]))), token);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor? Current = new("actor", "profile", null, null, "1");
        private int _hold;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void HoldNext() => Interlocked.Exchange(ref _hold, 1);
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken)
        {
            var captured = Current;
            if (Interlocked.Exchange(ref _hold, 0) == 1)
            { Entered.TrySetResult(); await Release.Task.ConfigureAwait(false); }
            cancellationToken.ThrowIfCancellationRequested(); return captured;
        }
    }
    private sealed class HeldStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        private int _hold;
        private Exception? _failure;
        private bool _cooperative;
        internal int Reads, Writes;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void HoldNext(Exception? failure = null, bool cooperative = false)
        { _failure = failure; _cooperative = cooperative; Interlocked.Exchange(ref _hold, 1); }
        public async Task<HomeStateReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Reads);
            var actual = await inner.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (Interlocked.Exchange(ref _hold, 0) == 1)
            {
                Entered.TrySetResult();
                if (_cooperative) await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                else await Release.Task.ConfigureAwait(false);
                if (_failure is { } failure) throw failure;
            }
            return actual;
        }
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Writes); return inner.WriteAsync(record, expectedRecordRevision, cancellationToken); }
    }
    private sealed class ThemeSource(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "dashboard-attention", true, CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }
    private sealed class Dashboard : IDashboardRepository
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(new DashboardSnapshot(now, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, [], []));
    }
    private sealed class Layout : IDashboardLayoutRepository
    {
        public Task<IReadOnlyList<DashboardTileLayout>> GetAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DashboardTileLayout>>([]);
        public Task SaveAsync(IReadOnlyList<DashboardTileLayout> layout, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Settings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class => Task.FromResult(_values.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
