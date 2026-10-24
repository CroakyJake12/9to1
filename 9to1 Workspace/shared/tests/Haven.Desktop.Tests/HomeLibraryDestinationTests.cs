using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;
using HavenOS.Home;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

/// <summary>Focused component smoke cases with explicit controlled index/permission inputs.
/// These verify real canonical CUI and domain routing, not production Notes/Home/native acceptance.</summary>
public sealed class HomeLibraryDestinationTests
{
    [AvaloniaFact]
    public async Task Canonical_Library_row_opens_the_same_authorised_identity_through_the_actual_queue_callback()
    {
        var index = new Index(1); var authorization = new Authorization();
        HomeDeepLink? queued = null; Task? settlement = null;
        var page = Page(index, authorization, (link, original) => { queued = link; settlement = original; return true; });
        Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            var row = Assert.Single(page.GetVisualDescendants().OfType<Button>(), item => item.Name == "home-library-item");
            Assert.Equal("Notebook 00", global::Avalonia.Automation.AutomationProperties.GetName(row));
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(index.Rows[0].OpenLink, queued); Assert.NotNull(settlement);
            await settlement!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(index.Rows[0].ArtifactId, queued!.ObjectId);
            Assert.Equal("boards", queued.OwnerApp); Assert.Equal("boards.notebook", queued.TargetRouteId);
            Assert.Equal(1, index.Rows.Count); // Home navigation did not create a second artifact.
            Assert.Equal(1, authorization.Opens);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }
    [AvaloniaFact]
    public async Task Library_rechecks_permission_before_open_and_removes_the_now_unavailable_projection()
    {
        var index = new Index(1); var authorization = new Authorization(); var queued = false;
        var page = Page(index, authorization, (_, _) => queued = true); Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            var row = Assert.Single(page.GetVisualDescendants().OfType<Button>(), item => item.Name == "home-library-item");
            authorization.Allowed = false;
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(queued); Assert.Null(page.Current);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), item => item.Name == "home-library-item");
            await page.CloseAndDrainAsync(); Assert.True(page.OriginalClose!.IsCompletedSuccessfully);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }
    [AvaloniaFact]
    public async Task Library_search_and_page_buttons_use_shared_domain_paging_and_actual_input()
    {
        var index = new Index(31); var page = Page(index, new(), (_, _) => true); Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            Assert.Equal(25, page.Current!.Items.Count); Assert.True(page.Current.HasMore);
            await page.DispatchAsync("NextLibraryPage", null);
            Assert.Equal(6, page.Current!.Items.Count); Assert.False(page.Current.HasMore);
            await page.DispatchAsync("PreviousLibraryPage", null);
            Assert.Equal(25, page.Current!.Items.Count);
            Assert.Single(page.GetVisualDescendants().OfType<TextBox>(), item => item.Name == "library-search").Text = "Notebook 30";
            await page.DispatchAsync("SearchLibrary", null);
            Assert.Equal("Notebook 30", Assert.Single(page.Current!.Items).DisplayName);
            Assert.Equal("Notebook 30", index.LastQuery!.Text); Assert.Equal(0, index.LastQuery.Offset);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }
    [AvaloniaFact]
    public async Task Library_close_waits_for_its_actual_delayed_query_and_does_not_publish_retired_rows()
    {
        var index = new Index(1) { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var page = Page(index, new(), (_, _) => throw new InvalidOperationException("No navigation was requested."));
        var initialization = page.ActivateAsync(CancellationToken.None);
        await index.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close = page.CloseAndDrainAsync(); Assert.False(close.IsCompleted);
        index.Held.TrySetResult(index.Rows);
        var canceled = await Record.ExceptionAsync(() => initialization);
        Assert.IsAssignableFrom<OperationCanceledException>(canceled);
        await close.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(close.IsCompletedSuccessfully); Assert.Null(page.Current); Assert.Null(page.Content);
    }
    [AvaloniaFact]
    public async Task Library_route_is_composition_gated_and_preserves_private_NewTab_and_nested_refusals()
    {
        using var lifetime = new CancellationTokenSource();
        UnifiedHomeContentFactory Factory(bool composed) => new(new Registry(),
            () => throw new InvalidOperationException("Library cannot create Dashboard."),
            () => throw new InvalidOperationException("Library cannot create Models."), (_, _) => true,
            (_, _) => throw new InvalidOperationException("Library cannot create an AI draft."),
            (_, _) => Task.CompletedTask, () => true, lifetime.Token, false,
            library: composed ? _ => Page(new Index(1), new(), (_, _) => true) : null);
        BrowseAppContentRequest Request(string address) => new(Guid.NewGuid(), AppContentAddress.Parse(address), "fixture-profile", BrowserTabPrivacy.Standard);
        Assert.Throws<NotSupportedException>(() => Factory(false).CreateSession(Request("browse://home/library")));
        Assert.Throws<NotSupportedException>(() => Factory(true).CreateSession(Request("browse://newtab/library")));
        Assert.Throws<NotSupportedException>(() => Factory(true).CreateSession(Request("browse://home/library/nested")));
        Assert.Throws<NotSupportedException>(() => Factory(true).CreateSession(Request("browse://home/library") with { Privacy = BrowserTabPrivacy.Private }));
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(true).CreateSession(Request("browse://home/library")));
        Window? window = null;
        try
        {
            await session.InitializeAsync(); window = Show(session.OriginalNativeView);
            Assert.Single(session.OriginalNativeView.GetVisualDescendants().OfType<HomeLibraryDestinationPage>());
        }
        finally { try { await session.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }
    private static HomeLibraryDestinationPage Page(Index index, Authorization authorization, Func<HomeDeepLink, Task, bool> queue) =>
        new(router => new(index, authorization, router, new Preferences()), _ => Task.CompletedTask, queue);
    private static Window Show(Control page)
    { var window = new Window { Width = 800, Height = 620, Content = page }; window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); return window; }
    private sealed class Registry : IModeRegistry
    {
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModeDefinition>>([]);
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) => Task.FromResult<ModeDefinition?>(null);
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) => Task.FromResult<ModeDefinition?>(null);
        public Task UpsertModeAsync(ModeDefinition mode, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Index : IHomeArtifactSearchIndex
    {
        internal readonly IReadOnlyList<HomeArtifactReference> Rows;
        internal HomeLibraryQuery? LastQuery;
        internal TaskCompletionSource<IReadOnlyList<HomeArtifactReference>>? Held;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Index(int count) => Rows = Enumerable.Range(0, count).Select(number =>
        {
            var id = Guid.NewGuid().ToString("D");
            return new HomeArtifactReference("boards", id, $"Notebook {number:D2}", "Boards notebook", "Fixture library", "fixture-profile", null,
                DateTimeOffset.UnixEpoch.AddMinutes(-number), false, false, false, false, [], new(HomeRoute.Library, "boards", id, "open", "boards.notebook"));
        }).ToArray();
        public async Task<IReadOnlyList<HomeArtifactReference>> SearchAsync(HomeLibraryQuery query, CancellationToken token)
        {
            LastQuery = query; Entered.TrySetResult();
            var rows = Held is { } held ? await held.Task : Rows;
            return rows.Where(row => query.Text is null || row.DisplayName.Contains(query.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        public Task<HomeArtifactReference?> GetByIdAsync(string app, string id, CancellationToken token) =>
            Task.FromResult(Rows.SingleOrDefault(row => row.OwnerApp == app && row.ArtifactId == id));
    }
    private sealed class Authorization : IHomeArtifactAuthorization
    {
        internal bool Allowed = true; internal int Opens;
        public Task<bool> CanReadAsync(string app, string id, CancellationToken token) => Task.FromResult(Allowed);
        public Task<bool> CanOpenAsync(HomeDeepLink link, CancellationToken token) { Opens++; return Task.FromResult(Allowed); }
    }
    private sealed class Preferences : IHomeLibraryPreferences
    {
        public Task<bool> IsPinnedAsync(string app, string id, CancellationToken token) => Task.FromResult(false);
        public Task SetPinnedAsync(string app, string id, bool pinned, CancellationToken token) => throw new InvalidOperationException("This read/open slice cannot mutate pin state.");
    }
}
