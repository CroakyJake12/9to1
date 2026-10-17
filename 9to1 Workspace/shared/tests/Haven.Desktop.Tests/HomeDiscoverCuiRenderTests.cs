using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
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
        var previous = CuiThemeScopeApplier.DetectAppearance();
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Professional, appearanceOverride: appearance);
        using var page = new DiscoverPage(new HomeDiscoverCatalog([new Source()]));
        var window = new Window { Content = page, Width = width, Height = height };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            window.UpdateLayout();
            var models = Find<ListBox>(page, "discover-models");
            Assert.Equal(2, models.ItemCount);
            Assert.True(models.Bounds.Width > 0 && models.Bounds.Height > 0);
            foreach (var id in new[] { "discover-search", "discover-category", "discover-locality", "discover-refresh", "discover-manage" })
            {
                var control = Find<Control>(page, id);
                var point = control.TranslatePoint(default, page)!.Value;
                Assert.InRange(point.X, 0, width);
                Assert.True(point.X + control.Bounds.Width <= width + 1, $"{id} overflowed the {width}px view.");
            }
            models.SelectedIndex = 0;
            Assert.Contains("Provider", Find<TextBlock>(page, "discover-details").Text);
            Find<Button>(page, "discover-compare").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("Context: Unknown", Find<TextBlock>(page, "discover-details").Text);
            var opened = 0;
            page.ManageProvidersRequested += (_, _) => opened++;
            Find<Button>(page, "discover-manage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, opened);

            Find<ComboBox>(page, "discover-category").SelectedIndex = 3; // Voice is a primary category.
            await page.ActivateAsync(CancellationToken.None);
            Assert.Equal(1, models.ItemCount);
            Assert.Equal("voice", Assert.IsType<HomeDiscoverModel>(models.Items[0]).Identity.ModelId);
            Find<TextBox>(page, "discover-search").Text = "no such model";
            await page.ActivateAsync(CancellationToken.None);
            Assert.Equal(0, models.ItemCount);
        }
        finally { window.Close(); CuiThemeScopeApplier.ApplyGlobalTheme(previousTheme, appearanceOverride: previous); }
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
            Assert.True(await page.TryPrepareToCloseAsync(CancellationToken.None));
            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.OriginalClose);
            Assert.True(source.Token.IsCancellationRequested);
            Assert.False(close.IsCompleted);
            Assert.NotNull(page.Content);
            source.Release.TrySetResult();
            _ = await Record.ExceptionAsync(() => activation);
            _ = await Record.ExceptionAsync(() => close);
            Assert.True(activation.IsCompleted);
            Assert.Null(page.Content);
        }
        finally { source.Release.TrySetResult(); window.Close(); page.Dispose(); }
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
        Assert.Single(page.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private sealed class Source : IHomeDiscoverCatalogueSource
    {
        public string ProviderId => "Provider";
        public Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HomeDiscoverProviderSnapshot(ProviderId, "Provider", HomeDiscoverConnectionState.Connected,
                HomeDiscoverSourceState.Available, "revision-1", [Model("chat", HomeDiscoverCategory.Chat), Model("voice", HomeDiscoverCategory.Voice)]));
        private static HomeDiscoverModel Model(string id, HomeDiscoverCategory category) =>
            new(new("Provider", id), id, null, new HashSet<HomeDiscoverCategory> { category }, null, null, null, null, null,
                HomeDiscoverLocality.Local, HomeDiscoverInstallState.Unknown, HomeDiscoverConnectionState.Connected,
                null, null, null, null, null, null, new HashSet<HomeDiscoverAction>());
        public Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
