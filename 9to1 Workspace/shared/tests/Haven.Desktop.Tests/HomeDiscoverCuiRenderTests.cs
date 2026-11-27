using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Home.Discover;
using DiscoverPage = Haven.Desktop.Views.Pages.Home.HomeDiscoverPage;

namespace Haven.Desktop.Tests;

public sealed class HomeDiscoverCuiRenderTests
{
    [AvaloniaTheory]
    [InlineData(1280, 900, CuiAppearance.SuperBright)]
    [InlineData(1280, 900, CuiAppearance.Bright)]
    [InlineData(1280, 900, CuiAppearance.Dark)]
    [InlineData(1280, 900, CuiAppearance.SuperDark)]
    [InlineData(390, 844, CuiAppearance.SuperBright)]
    [InlineData(390, 844, CuiAppearance.Bright)]
    [InlineData(390, 844, CuiAppearance.Dark)]
    [InlineData(390, 844, CuiAppearance.SuperDark)]
    public async Task Authored_discover_renders_and_filters_actual_catalogue_metadata(int width, int height, CuiAppearance appearance)
    {
        var token = TestContext.Current.CancellationToken;
        var previous = CuiThemeScopeApplier.DetectAppearance();
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Professional, appearanceOverride: appearance);
        var source = new Source();
        using var page = new DiscoverPage(new HomeDiscoverCatalog([source]));
        var window = new Window { Content = page, Width = width, Height = height };
        try
        {
            window.Show();
            await page.ActivateAsync(token);
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            window.UpdateLayout();
            var models = Find<ListBox>(page, "discover-models");
            Assert.Equal(3, models.ItemCount);
            Assert.Equal("discover", page.CurrentView);
            Assert.Equal(3, page.VisibleModels.Count);
            Assert.False(Find<Control>(page, "discover-comparison").IsVisible);
            Assert.False(VisibleInPage(page, models));
            Assert.Equal(3, page.GetVisualDescendants().OfType<Button>().Count(control => control.Name == "discover-model-details" && control.IsEffectivelyVisible));
            Assert.False(Find<Control>(page, "discover-advanced-filters").IsVisible);
            var searchField = Find<Control>(page, "discover-search-field");
            Assert.True(searchField.Bounds.Width >= width - (width >= 760 ? 350 : 72),
                "Search must use the available row, rather than retain a fixed desktop width.");
            Assert.True(Find<TextBox>(page, "discover-search").Bounds.Width > 200);
            Assert.Equal(3, page.GetVisualDescendants().OfType<TextBlock>().Count(control => control.Name == "discover-model-monogram"));
            var routes = page.GetVisualDescendants().OfType<TextBlock>().Where(control => control.Name == "discover-model-route").ToArray();
            Assert.Equal(3, routes.Length);
            Assert.All(routes, route => Assert.Equal("Provider · On this device · Context unreported", route.Text));
            var warning = Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(), control =>
                control.Name == "discover-model-attention" && VisibleInPage(page, control));
            Assert.Equal("Provider disconnected", warning.Text);
            var longName = Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(), control =>
                control.Name == "discover-model-name" && control.Text == Source.LongName);
            var longCard = longName.GetVisualAncestors().OfType<Control>().First(control => control.Name == "discover-model-card");
            var longDetails = Assert.Single(longCard.GetVisualDescendants().OfType<Button>(), control => control.Name == "discover-model-details");
            foreach (var control in new Control[] { longName, longDetails })
            {
                var origin = control.TranslatePoint(default, longCard);
                Assert.NotNull(origin);
                Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
                Assert.True(origin.Value.X >= 0 && origin.Value.X + control.Bounds.Width <= longCard.Bounds.Width + 1);
            }
            AssertQuickFilterInk(page, "all");
            var catalogueReads = source.Reads;
            foreach (var filter in new[] { ("vision", "chat"), ("code", "code"), ("speech", "voice") })
            {
                Find<Button>(page, "discover-filter-" + filter.Item1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await page.TryPrepareToCloseAsync(token));
                Assert.Equal(filter.Item2, Assert.Single(page.VisibleModels).Identity.ModelId);
                Assert.Equal(catalogueReads, source.Reads);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                AssertQuickFilterInk(page, filter.Item1);
            }
            Find<Button>(page, "discover-filter-image").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(token));
            Assert.Empty(page.VisibleModels); // Reported Vision does not imply image generation.
            Assert.Equal(catalogueReads, source.Reads);
            Find<Button>(page, "discover-filter-all").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(token));
            Assert.Equal(3, page.VisibleModels.Count);
            foreach (var filter in new[] { ("vision", "chat"), ("code", "code") })
            {
                Find<Button>(page, "discover-filter-" + filter.Item1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await page.TryPrepareToCloseAsync(token));
                await page.ActivateAsync(token); // One explicit refresh under the chosen capability.
                Assert.Equal(filter.Item2, Assert.Single(page.VisibleModels).Identity.ModelId);
                var readsAfterRefresh = source.Reads;
                Find<Button>(page, "discover-filter-all").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await page.TryPrepareToCloseAsync(token));
                Assert.Equal(3, page.VisibleModels.Count);
                Assert.Equal(readsAfterRefresh, source.Reads); // All reuses this same observed catalogue.
                Assert.Equal(3, models.ItemCount);
            }
            var category = Find<ComboBox>(page, "discover-category");
            Find<Button>(page, "discover-more-filters").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(token)); window.UpdateLayout();
            Assert.True(Find<Control>(page, "discover-advanced-filters").IsVisible);
            Assert.Same(category, Find<ComboBox>(page, "discover-category"));
            var huggingFaceQuery = Find<TextBox>(page, "hf-query");
            var huggingFaceSearch = Find<Button>(page, "hf-search");
            var huggingFaceToken = Find<TextBox>(page, "hf-token");
            Assert.False(Find<Control>(page, "discover-huggingface-mount").IsVisible);
            Assert.False(VisibleInPage(page, huggingFaceQuery));
            foreach (var id in new[] { "discover-search", "discover-category", "discover-locality", "discover-refresh", "discover-manage" })
            {
                var control = Find<Control>(page, id);
                var point = control.TranslatePoint(default, page)!.Value;
                Assert.InRange(point.X, 0, width);
                Assert.True(point.X + control.Bounds.Width <= width + 1, $"{id} overflowed the {width}px view.");
            }
            Find<Button>(page, "discover-browse").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(token));
            window.UpdateLayout();
            Assert.Equal("catalogue", page.CurrentView);
            Assert.Same(huggingFaceQuery, Find<TextBox>(page, "hf-query"));
            Assert.True(VisibleInPage(page, huggingFaceQuery));
            Assert.Equal('•', huggingFaceToken.PasswordChar);
            var huggingFaceQueryOrigin = huggingFaceQuery.TranslatePoint(default, window);
            var huggingFaceSearchOrigin = huggingFaceSearch.TranslatePoint(default, window);
            Assert.NotNull(huggingFaceQueryOrigin);
            Assert.NotNull(huggingFaceSearchOrigin);
            Assert.InRange(huggingFaceQueryOrigin.Value.Y, 0, height - huggingFaceQuery.Bounds.Height);
            Assert.InRange(huggingFaceSearchOrigin.Value.Y, 0, height - huggingFaceSearch.Bounds.Height);
            Assert.True(huggingFaceQuery.Bounds.Width > 0 && huggingFaceSearch.Bounds.Width > 0,
                "The explicit installation catalogue must lay out its actual masked provider controls.");
            await page.DispatchAsync("models.discover", null, token);
            Find<Button>(page, "discover-show-comparison").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(token));
            window.UpdateLayout();
            Assert.Same(models, Find<ListBox>(page, "discover-models"));
            Assert.True(Find<Control>(page, "discover-comparison").IsVisible);
            Assert.True(VisibleInPage(page, models) && models.Bounds.Width > 0 && models.Bounds.Height > 0);
            models.SelectedIndex = 0;
            Assert.Contains("Provider", Find<TextBlock>(page, "discover-details").Text);
            Find<Button>(page, "discover-compare").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(token));
            Assert.Contains("Context: Unknown", Find<TextBlock>(page, "discover-details").Text);
            var opened = 0;
            page.ManageProvidersRequested += (_, _) => opened++;
            Find<Button>(page, "discover-manage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, opened);

            await page.DispatchAsync("models.discover", null, token);
            var reads = source.Reads;
            Find<ComboBox>(page, "discover-category").SelectedIndex = 3; // Voice is a primary category.
            Assert.Equal("voice", Assert.Single(page.VisibleModels).Identity.ModelId);
            Assert.Equal(reads, source.Reads); // Editing filters uses the observed catalogue only.
            await page.ActivateAsync(token);
            Assert.Equal(3, models.ItemCount); // The comparison owner retains the bounded observed catalogue.
            Assert.Equal("voice", Assert.Single(page.VisibleModels).Identity.ModelId);
            reads = source.Reads;
            Find<TextBox>(page, "discover-search").Text = "no such model";
            // The native TextBox posts TextChanged. Observe that actual UI callback
            // before checking its local filter; this must not trigger a provider read.
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(page.VisibleModels);
            Assert.Equal(reads, source.Reads);
            await page.ActivateAsync(token);
            Assert.Equal(3, models.ItemCount);
            Assert.Empty(page.VisibleModels);
            Find<TextBox>(page, "discover-search").Text = string.Empty;
            Find<Button>(page, "discover-filter-all").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(token)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, page.VisibleModels.Count);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); CuiThemeScopeApplier.ApplyGlobalTheme(previousTheme, appearanceOverride: previous); }
    }

    [AvaloniaFact]
    public async Task Discover_close_cancels_then_joins_the_actual_provider_before_disposing_controls()
    {
        var source = new HeldSource();
        var page = new DiscoverPage(new HomeDiscoverCatalog([source]));
        var window = new Window { Content = page, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            var activation = page.ActivateAsync(CancellationToken.None);
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var preparation = page.TryPrepareToCloseAsync(CancellationToken.None);
            Assert.False(preparation.IsCompleted);
            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.OriginalClose);
            Assert.True(source.Token.IsCancellationRequested);
            Assert.False(close.IsCompleted);
            Assert.NotNull(page.Content);
            source.Release.TrySetResult();
            var preparationFailure = Assert.IsType<AggregateException>(await Record.ExceptionAsync(
                () => preparation.WaitAsync(TimeSpan.FromSeconds(5))));
            var cancellation = Assert.Single(preparationFailure.Flatten().InnerExceptions.OfType<OperationCanceledException>());
            Assert.Equal(source.Token, cancellation.CancellationToken);
            Assert.True(cancellation.CancellationToken.IsCancellationRequested);
            _ = await Record.ExceptionAsync(() => activation);
            var closeFailure = Assert.IsType<AggregateException>(await Record.ExceptionAsync(
                () => close.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Contains(closeFailure.Flatten().InnerExceptions, error => ReferenceEquals(error, cancellation));
            Assert.True(activation.IsCompleted);
            Assert.Null(page.Content);
            Assert.Same(close, page.CloseAndDrainAsync());
        }
        finally
        {
            source.Release.TrySetResult();
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            window.Content = null; window.Close(); page.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task Refresh_button_retirement_joins_its_source_and_refuses_late_event_work()
    {
        var source = new HeldSource();
        var page = new DiscoverPage(new HomeDiscoverCatalog([source]));
        var window = new Window { Content = page, Width = 1000, Height = 800 }; window.Show();
        try
        {
            var refresh = Find<Button>(page, "discover-refresh");
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, source.Reads);
            source.Release.TrySetResult();
            await Record.ExceptionAsync(() => close);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Null(page.Content);
            Assert.Equal(1, source.Reads);
        }
        finally { source.Release.TrySetResult(); window.Close(); page.Dispose(); }
    }

    private sealed class HeldSource : IHomeDiscoverCatalogueSource
    {
        public string ProviderId => "held";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public int Reads;
        public async Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            Reads++; Token = cancellationToken; Entered.TrySetResult(); await Release.Task;
            return new(ProviderId, "Held provider", HomeDiscoverConnectionState.Connected, HomeDiscoverSourceState.Available, "1", []);
        }
        public Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static T Find<T>(Control page, string name) where T : Control =>
        Assert.Single(AuthoredCuiControls.Enumerate(page).OfType<T>(), control => control.Name == name);

    // CUI retains hidden authored sections outside the native tree. A detached
    // descendant's effective flag alone does not prove that it is on this page.
    private static bool VisibleInPage(Control page, Control control) =>
        control.IsEffectivelyVisible && control.GetVisualAncestors().Contains(page);

    private static void AssertQuickFilterInk(Control page, string selected)
    {
        foreach (var key in new[] { "all", "chat", "code", "vision", "speech", "image", "video" })
        {
            var button = Find<Button>(page, "discover-filter-" + key);
            Assert.True(button.TryFindResource(key == selected ? "CuiAccentPrimaryForegroundBrush" : "CuiTextBrush", out var expected));
            var colour = Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color;
            Assert.Equal(colour, Assert.IsAssignableFrom<ISolidColorBrush>(button.Foreground).Color);
            var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>(), text => Equals(text.Text, button.Content));
            Assert.Equal(colour, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
        }
    }

    private sealed class Source : IHomeDiscoverCatalogueSource
    {
        internal const string LongName = "A local assistant with a fully spelled-out model repository and a deliberately long published variant name";
        public int Reads;
        public string ProviderId => "Provider";
        public Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(new HomeDiscoverProviderSnapshot(ProviderId, "Provider", HomeDiscoverConnectionState.Connected,
                HomeDiscoverSourceState.Available, "revision-1", [Model("chat", HomeDiscoverCategory.Chat, "Text", "Vision"),
                    Model("code", HomeDiscoverCategory.Chat, "Text", "Code"), Model("voice", HomeDiscoverCategory.Voice, "AudioOutput")]));
        }
        private static HomeDiscoverModel Model(string id, HomeDiscoverCategory category, params string[] capabilities) =>
            new(new("Provider", id), id, null, new HashSet<HomeDiscoverCategory> { category },
                new HashSet<string>(capabilities, StringComparer.OrdinalIgnoreCase), null, null, null, null,
                HomeDiscoverLocality.Local, HomeDiscoverInstallState.Unknown,
                id == "voice" ? HomeDiscoverConnectionState.Disconnected : HomeDiscoverConnectionState.Connected,
                null, null, null, null, null, null, new HashSet<HomeDiscoverAction>(), id == "chat" ? LongName : null);
        public Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
