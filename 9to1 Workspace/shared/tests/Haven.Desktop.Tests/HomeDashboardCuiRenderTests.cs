using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Dashboard;
using Haven.Desktop.Events;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Home;
using Haven.Desktop.Services;
using HavenOS.Home.Core;
using HavenOS.Home;

namespace Haven.Desktop.Tests;

public sealed class HomeDashboardCuiRenderTests
{
    [AvaloniaTheory]
    [InlineData("Glow", 1440, 900)]
    [InlineData("Glow", 1280, 720)]
    [InlineData("Glow", 720, 700)]
    [InlineData("Professional", 1440, 900)]
    [InlineData("Professional", 1280, 720)]
    [InlineData("Professional", 720, 700)]
    public async Task Dashboard_continue_cards_borrow_Library_permissions_exact_targets_and_responsive_layout(string themeName, int width, int height)
    {
        using var theme = new CuiThemePreferenceContext(new PresentationThemeSource(themeName));
        using var bus = new HavenEventBus();
        using var page = CreatePage(bus, new SnapshotRepository(), theme);
        page.EnableHomeBriefing(null, null);
        var source = new BriefingLibrarySource();
        source.Denied.Add(source.Rows[2].ArtifactId);
        HomeLibraryDestinationPage? library = null; HomeDeepLink? opened = null; Task? settlement = null;
        var constructions = 0;
        page.ConfigureHomeBriefingLibrary(() =>
        {
            constructions++;
            return library = new(source.CreateService, source.RevalidateAsync,
                (link, actual) => { opened = link; settlement = actual; return true; }, source, theme);
        });
        Assert.Equal(0, constructions);
        var window = new Window { Content = page, Width = width, Height = height };
        try
        {
            window.Show(); await page.ActivateAsync(CancellationToken.None); SettleDashboardLayout(window);
            Assert.Equal(1, constructions); Assert.NotNull(library);
            var cards = library.GetVisualDescendants().OfType<Button>().Where(button =>
                CuiRuntimeIdentity.GetStableId(button)?.EndsWith("/id:dashboard-library-item", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(2, cards.Length);
            Assert.True(cards[0].TryFindResource("CuiEffectiveThemeName", out var effectiveTheme));
            Assert.Equal(themeName, effectiveTheme);
            var firstTop = cards.Min(card => card.TranslatePoint(default, library)!.Value.Y);
            Assert.InRange(firstTop, 0, 1); // Hidden Library controls must not leave an empty strip above the cards.
            Assert.DoesNotContain(library.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible);
            Assert.DoesNotContain(library.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == source.Rows[2].DisplayName);
            foreach (var card in cards)
            {
                var point = card.TranslatePoint(default, library)!.Value;
                Assert.True(point.X >= 0 && point.X + card.Bounds.Width <= library.Bounds.Width + 1);
                Assert.True(card.Bounds.Height >= 168);
            }
            Assert.True(cards[1].TranslatePoint(default, library)!.Value.X > cards[0].TranslatePoint(default, library)!.Value.X);
            if (Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") is { Length: > 0 } captures)
            {
                Directory.CreateDirectory(captures);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                frame.Save(Path.Combine(captures, $"home-dashboard-library-{themeName.ToLowerInvariant()}-{width}x{height}.png"));
            }
            cards[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await library.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(source.Rows[0].OpenLink, opened);
            await settlement!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(source.OpenChecks > 0); Assert.Equal(3, source.Rows.Count);
            page.Deactivate(); Assert.False(library.IsActionAvailable("OpenHomeLibraryArtifact"));
            await page.ActivateAsync(CancellationToken.None);
            Assert.Equal(1, constructions); Assert.True(library.IsActionAvailable("OpenHomeLibraryArtifact"));
            Assert.True(await page.TryPrepareToCloseAsync(CancellationToken.None));
            await page.CloseAndDrainAsync(); Assert.True(source.Disposed);
        }
        finally { window.Close(); await page.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Dashboard_custom_widget_layout_does_not_construct_the_briefing_Library()
    {
        using var bus = new HavenEventBus();
        var settings = new TestSettings();
        await settings.SetAsync("dashboard.widget-layouts.v1", new DashboardWidgetLayoutState(1,
            new() { ["home"] = [] }, UsesHomeBriefing: false), CancellationToken.None);
        using var page = new NewDashboardPage(bus, null!, null!, null!, null!, settings, new SnapshotRepository(), new TestLayout(), []);
        page.EnableHomeBriefing(null, null);
        page.ConfigureHomeBriefingLibrary(() => throw new InvalidOperationException("An unused Library must not be created."));
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            Assert.False(page.ShowsHomeBriefing);
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control is HomeLibraryDestinationPage);
        }
        finally { await page.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Dashboard_preflight_waits_for_its_embedded_Library_original_initialization()
    {
        using var bus = new HavenEventBus(); using var page = CreatePage(bus, new SnapshotRepository());
        page.EnableHomeBriefing(null, null);
        var source = new BriefingLibrarySource { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        page.ConfigureHomeBriefingLibrary(() => new(source.CreateService, source.RevalidateAsync, (_, _) => false, source));
        var activation = page.ActivateAsync(CancellationToken.None);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var preparation = page.TryPrepareToCloseAsync(CancellationToken.None);
            Assert.False(preparation.IsCompleted); Assert.False(source.Disposed);
            source.Held.TrySetResult(source.Rows);
            await activation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(await preparation.WaitAsync(TimeSpan.FromSeconds(10)));
            await page.CloseAndDrainAsync(); Assert.True(source.Disposed);
        }
        finally { source.Held.TrySetResult(source.Rows); await page.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Dashboard_preflight_does_not_hide_its_embedded_Library_original_fault()
    {
        using var bus = new HavenEventBus(); using var page = CreatePage(bus, new SnapshotRepository());
        page.EnableHomeBriefing(null, null);
        var source = new BriefingLibrarySource { AdmissionFailure = new InvalidOperationException("retained original Library failure") };
        page.ConfigureHomeBriefingLibrary(() => new(source.CreateService, source.RevalidateAsync, (_, _) => false, source));
        _ = await Record.ExceptionAsync(() => page.ActivateAsync(CancellationToken.None));
        Assert.Contains(source.AdmissionFailure.Message, (await Record.ExceptionAsync(() => page.TryPrepareToCloseAsync(CancellationToken.None)))!.ToString());
        Assert.Contains(source.AdmissionFailure.Message, (await Record.ExceptionAsync(() => page.CloseAndDrainAsync()))!.ToString());
        Assert.True(source.Disposed);
    }

    [AvaloniaFact]
    public async Task Dashboard_deactivation_fences_delayed_Library_without_reclassifying_it_as_a_failed_original()
    {
        using var bus = new HavenEventBus(); using var page = CreatePage(bus, new SnapshotRepository());
        page.EnableHomeBriefing(null, null);
        var source = new BriefingLibrarySource { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        HomeLibraryDestinationPage? library = null;
        page.ConfigureHomeBriefingLibrary(() => library = new(source.CreateService, source.RevalidateAsync, (_, _) => false, source));
        var activation = page.ActivateAsync(CancellationToken.None);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            page.Deactivate();
            source.Held.TrySetResult(source.Rows);
            await activation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(library); Assert.Null(library.Current);
            Assert.False(library.IsActionAvailable("OpenHomeLibraryArtifact"));
            Assert.True(await page.TryPrepareToCloseAsync(CancellationToken.None));
            await page.CloseAndDrainAsync(); Assert.True(source.Disposed);
        }
        finally { source.Held.TrySetResult(source.Rows); await page.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Dashboard_close_keeps_embedded_Library_until_its_actual_delayed_source_settles()
    {
        using var bus = new HavenEventBus(); using var page = CreatePage(bus, new SnapshotRepository());
        page.EnableHomeBriefing(null, null);
        var source = new BriefingLibrarySource { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        HomeLibraryDestinationPage? library = null;
        page.ConfigureHomeBriefingLibrary(() => library = new(source.CreateService, source.RevalidateAsync, (_, _) => false, source));
        var activation = page.ActivateAsync(CancellationToken.None);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var original = Assert.IsAssignableFrom<Task>(library!.OriginalInitialization);
            var close = page.CloseAndDrainAsync();
            await source.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(close.IsCompleted); Assert.False(source.Disposed); Assert.NotNull(page.Content);
            Assert.False(original.IsCompleted); // Closing an observer cannot stand in for source acknowledgement.
            source.Held.TrySetResult(source.Rows);
            await activation.WaitAsync(TimeSpan.FromSeconds(10));
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(original.IsCompletedSuccessfully); // The child owner qualified its own cooperative cancellation.
            Assert.True(source.Disposed); Assert.Null(page.Content);
        }
        finally { source.Held.TrySetResult(source.Rows); await page.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Dashboard_close_retains_a_genuine_Library_failure_after_its_owner_requested_cancellation()
    {
        using var bus = new HavenEventBus(); using var page = CreatePage(bus, new SnapshotRepository());
        page.EnableHomeBriefing(null, null);
        var source = new BriefingLibrarySource { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var failure = new IOException("Actual Library permission source failed during close.");
        HomeLibraryDestinationPage? library = null;
        page.ConfigureHomeBriefingLibrary(() => library = new(source.CreateService, source.RevalidateAsync, (_, _) => false, source));
        var activation = page.ActivateAsync(CancellationToken.None);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var close = page.CloseAndDrainAsync();
            await source.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(close.IsCompleted); Assert.False(source.Disposed);
            source.ReadFailure = failure;
            source.Held.TrySetResult(source.Rows);
            var actual = await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(actual);
            Assert.True(ContainsActualCause(actual), actual.ToString());
            // Dashboard activation presents source errors and can settle successfully;
            // the actual Library original and external close must retain the real failure.
            // Qualify that original, not a second failure from the presentation observer.
            var childOriginal = Assert.IsAssignableFrom<Task>(library!.OriginalInitialization);
            var childFailure = await Record.ExceptionAsync(() => childOriginal.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(childOriginal.IsFaulted);
            Assert.NotNull(childFailure);
            Assert.True(ContainsActualCause(childFailure));
            await activation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(close, page.CloseAndDrainAsync()); Assert.True(source.Disposed);
        }
        finally
        {
            source.Held.TrySetResult(source.Rows);
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
        }

        bool ContainsActualCause(Exception error) => ReferenceEquals(error, failure) ||
            error is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsActualCause) ||
            error.InnerException is { } inner && ContainsActualCause(inner);
    }

    [AvaloniaFact]
    public async Task Dashboard_interface_scale_reflows_the_briefing_and_custom_widget_without_writing_its_layout()
    {
        var token = TestContext.Current.CancellationToken;
        const string layoutKey = "dashboard.widget-layouts.v1", customKey = "dashboard.custom-widgets.v1";
        var directory = Directory.CreateTempSubdirectory("haven-dashboard-scale-");
        try
        {
            var preferenceFile = Path.Combine(directory.FullName, "settings.json");
            await File.WriteAllTextAsync(preferenceFile, "{}", token);
            await using var owner = new NativeHomeThemePreferences(preferenceFile, new(HomeDataScope.DeviceLocal, "dashboard"),
                UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
            await owner.RefreshAsync();
            var choices = Assert.IsType<Dictionary<string, object?>[]>(owner.SettingsBindings()["HomeInterfaceScaleChoices"]);
            var scale150 = choices.Single(choice => Equals(choice["Name"], "150%"))["Target"];
            using var bus = new HavenEventBus();
            var settings = new TestSettings();
            var widget = new DashboardCustomWidgetDefinition(Guid.NewGuid().ToString("N"), "Garden notes", "Water on Friday", "A saved detail");
            var savedWidget = new DashboardCustomWidgetState(1, new() { ["home"] = [widget] });
            var definitions = BuiltInDashboardTiles.Create().Select(provider => provider.Definition)
                .Append(NewDashboardPage.ToCustomWidgetView(widget).Definition);
            var canonical = DashboardWidgetLayoutEngine.EnsurePlacements(definitions, [new(widget.Key, 3, 5, 2, 2)]);
            var savedLayout = new DashboardWidgetLayoutState(1, new() { ["home"] = canonical.ToList() }, UsesHomeBriefing: true);
            await settings.SetAsync(customKey, savedWidget, token);
            await settings.SetAsync(layoutKey, savedLayout, token);
            using var page = new NewDashboardPage(bus, null!, null!, null!, null!, settings, new SnapshotRepository(),
                new TestLayout(), [], owner.Context);
            page.EnableHomeBriefing(null, null);
            var window = new Window { Content = page, Width = 1024, Height = 900 };
            void Capture(string state)
            {
                if (Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") is not { Length: > 0 } captures) return;
                using var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(captures, $"dashboard-interface-scale-{state}.png"));
            }
            try
            {
                window.Show();
                await page.ActivateAsync(token);
                SettleDashboardLayout(window);
                var all = page.GetVisualDescendants().OfType<Control>().ToArray();
                var today = all.Single(control => control.Name == "dashboard-today");
                var study = all.Single(control => control.Name == "dashboard-study-week");
                var heading = all.OfType<TextBlock>().Single(text => text.Name == "dashboard-briefing-title");
                var canvas = Assert.Single(all.OfType<DashboardWidgetCanvas>());
                var frame = canvas.Children.Single(child => child.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == widget.Title));
                var title = frame.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == widget.Title);
                var baselineFont = title.FontSize;
                var baselineLeft = Canvas.GetLeft(frame);
                var baselineHeight = frame.Height;
                var placements = canvas.Placements.ToArray();
                Assert.True(baselineLeft > 0 && baselineHeight > 0);
                Assert.Equal(34, heading.FontSize);
                Assert.True(study.TranslatePoint(default, page)!.Value.X > today.TranslatePoint(default, page)!.Value.X);
                Capture("100");

                var previewPage = new object();
                Assert.True(owner.TryPreviewInterfaceScale(previewPage, scale150));
                SettleDashboardLayout(window);
                Assert.Equal(51, heading.FontSize);
                Assert.Equal(baselineFont * 1.5, title.FontSize);
                Assert.True(study.TranslatePoint(default, page)!.Value.Y > today.TranslatePoint(default, page)!.Value.Y);
                Assert.Equal(today.TranslatePoint(default, page)!.Value.X, study.TranslatePoint(default, page)!.Value.X, 1);
                Assert.Equal(0, Canvas.GetLeft(frame));
                Assert.Equal(baselineHeight * 1.5, frame.Height);
                Assert.Equal(canvas.Bounds.Width, frame.Width, 1);
                Assert.Equal(placements, canvas.Placements);
                Capture("150-preview");

                owner.EndInterfaceScalePreview(previewPage);
                SettleDashboardLayout(window);
                Assert.Equal(34, heading.FontSize);
                Assert.Equal(baselineFont, title.FontSize);
                Assert.Equal(baselineLeft, Canvas.GetLeft(frame), 1);
                Assert.Equal(baselineHeight, frame.Height);
                Assert.Contains(frame, canvas.Children);
                Assert.True(study.TranslatePoint(default, page)!.Value.X > today.TranslatePoint(default, page)!.Value.X);
                Assert.Equal(placements, canvas.Placements);
                Assert.Same(savedWidget, await settings.GetAsync<DashboardCustomWidgetState>(customKey, token));
                Assert.Same(savedLayout, await settings.GetAsync<DashboardWidgetLayoutState>(layoutKey, token));
                Assert.Equal("{}", await File.ReadAllTextAsync(preferenceFile, token));
                Capture("reverted");
            }
            finally { window.Close(); await page.CloseAndDrainAsync(); }
        }
        finally { directory.Delete(recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(720, 520)]
    public async Task Home_dashboard_briefing_renders_real_agenda_and_preserves_saved_widget_preferences(int width, int height)
    {
        const string layoutKey = "dashboard.widget-layouts.v1";
        var token = TestContext.Current.CancellationToken;
        var snapshot = Snapshot() with
        {
            Agenda = [new("fixture-task", "task", "Submit seminar notes", string.Empty, DateTimeOffset.Now, false, "plan")]
        };
        using var bus = new HavenEventBus();
        var settings = new TestSettings();
        using var page = new NewDashboardPage(bus, null!, null!, null!, null!, settings, new SnapshotRepository(snapshot: snapshot), new TestLayout(), []);
        page.EnableHomeBriefing(null, null);
        page.ConfigureHomeBriefingLibrary(true);
        var choice = new TaskCompletionSource<DashboardBriefingDestination>();
        page.BriefingDestinationRequested += (_, value) => choice.TrySetResult(value);
        var window = new Window { Content = page, Width = width, Height = height };
        try
        {
            window.Show();
            await page.ActivateAsync(token);
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            SettleDashboardLayout(window);
            var controls = page.GetVisualDescendants().OfType<Control>().ToArray();
            var briefing = Assert.Single(controls, control => control.Name == "dashboard-home-briefing");
            Assert.True(briefing.IsVisible && briefing.Bounds.Width > 0);
            Assert.DoesNotContain(controls, control => control.Name == "dashboard-legacy-heading" && control.IsEffectivelyVisible);
            var heading = Assert.IsAssignableFrom<TextBlock>(Assert.Single(controls, control => control.Name == "dashboard-briefing-title"));
            Assert.Equal("Dashboard", heading.Text);
            Assert.Contains(controls.OfType<TextBlock>(), control => control.IsEffectivelyVisible && control.Text == "Pick up where you left off.");
            var todayDate = Assert.IsAssignableFrom<TextBlock>(Assert.Single(controls, control => control.Name == "dashboard-briefing-date"));
            Assert.Contains("·", todayDate.Text);
            var customise = Assert.Single(controls.OfType<Button>(), control => control.Name == "dashboard-arrange-widgets");
            Assert.True(customise.IsEffectivelyVisible && customise.IsEnabled);
            Assert.Contains(controls.OfType<TextBlock>(), control => control.Text == "Submit seminar notes");
            Assert.Contains(controls.OfType<TextBlock>(), control => control.Text == "Recorded Study time is unavailable.");
            var today = Assert.Single(controls, control => control.Name == "dashboard-today");
            var study = Assert.Single(controls, control => control.Name == "dashboard-study-week");
            var todayPosition = today.TranslatePoint(default, briefing)!.Value;
            var studyPosition = study.TranslatePoint(default, briefing)!.Value;
            if (width >= 800)
            {
                Assert.True(studyPosition.X > todayPosition.X);
                Assert.Equal(todayPosition.Y, studyPosition.Y, 1);
            }
            else
            {
                Assert.Equal(todayPosition.X, studyPosition.X, 1);
                Assert.True(studyPosition.Y > todayPosition.Y);
            }
            var library = Assert.IsAssignableFrom<Button>(Assert.Single(controls, control => control.Name == "dashboard-briefing-library"));
            library.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(DashboardBriefingDestination.Library, await choice.Task.WaitAsync(TimeSpan.FromSeconds(5), token));
            Assert.True((await settings.GetAsync<DashboardWidgetLayoutState>(layoutKey, token))!.UsesHomeBriefing);
        }
        finally { window.Close(); }

        var saved = (await settings.GetAsync<DashboardWidgetLayoutState>(layoutKey, token))!;
        var first = saved.Pages["home"][0];
        var hidden = first with { IsVisible = false };
        saved.Pages["home"][0] = hidden;
        await settings.SetAsync(layoutKey, saved with { UsesHomeBriefing = false }, token);
        using var restored = new NewDashboardPage(bus, null!, null!, null!, null!, settings, new SnapshotRepository(), new TestLayout(), []);
        restored.EnableHomeBriefing(null, null);
        var restoredWindow = new Window { Content = restored, Width = width, Height = height };
        try
        {
            restoredWindow.Show();
            await restored.ActivateAsync(token);
            SettleDashboardLayout(restoredWindow);
            Assert.False(Assert.Single(AuthoredDashboardControls(restored), control => control.Name == "dashboard-home-briefing").IsVisible);
            Assert.True(Assert.Single(AuthoredDashboardControls(restored), control => control.Name == "dashboard-legacy-heading").IsVisible);
            Assert.Equal(hidden, (await settings.GetAsync<DashboardWidgetLayoutState>(layoutKey, token))!.Pages["home"][0]);
        }
        finally { restoredWindow.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(340)]
    [InlineData(760)]
    [InlineData(1280)]
    public async Task Device_model_cards_reflow_and_open_the_same_verified_review_without_installing(int width)
    {
        using var bus = new HavenEventBus();
        using var page = CreatePage(bus, new SnapshotRepository());
        var catalogue = new DeviceCatalogue();
        page.EnableDeviceModelRecommendations(new(new DeviceProbe(), catalogue,
            _ => ValueTask.FromResult<AuthenticatedResourceActor?>(DeviceActor), () => true));
        var window = new Window { Width = width, Height = 900, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            window.UpdateLayout();
            var cards = page.GetVisualDescendants().OfType<CuiContainerControl>().Where(control => control.Name?.StartsWith("dashboard-model-card", StringComparison.Ordinal) == true && control.Name != "dashboard-model-cards").ToArray();
            Assert.Equal(3, cards.Length);
            foreach (var card in cards)
            {
                var origin = card.TranslatePoint(default, page)!.Value;
                Assert.InRange(origin.X, 0, width);
                Assert.True(origin.X + card.Bounds.Width <= width + 1);
                Assert.True(card.Bounds.Width > 0);
                Assert.NotNull(card.Background);
            }
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), control => control.Text?.Contains("Fixture GPU", StringComparison.Ordinal) == true);
            var review = cards[0].GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Review & install"));
            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var loader = (CuiControlLoader)typeof(NewDashboardPage).GetField("_dashboardLoader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!;
            await loader.WhenActionsIdleAsync();
            window.UpdateLayout();
            var files = page.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "hf-files");
            Assert.IsType<HuggingFaceModelFile>(files.SelectedItem);
            Assert.Equal(0, catalogue.InstallCalls);
            Assert.True(page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "hf-install").IsEnabled);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task Local_only_recommendations_observe_hardware_without_contacting_the_catalogue()
    {
        var catalogue = new DeviceCatalogue();
        var source = new HomeDashboardModelSource(new DeviceProbe(), catalogue,
            _ => ValueTask.FromResult<AuthenticatedResourceActor?>(DeviceActor), () => false);
        var snapshot = await source.ReadAsync(CancellationToken.None);
        Assert.Empty(snapshot.Models);
        Assert.Equal(0, catalogue.Requests);
        Assert.Contains("Local-only", snapshot.Status);
        Assert.Equal(32 * LocalModelMemoryEstimator.GiB, snapshot.Hardware.TotalRamBytes);
        Assert.Null(await source.Profile(CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recommendation_source_rejects_late_results_after_profile_change_or_cancellation(bool cancelled)
    {
        var actor = DeviceActor;
        using var cancellation = new CancellationTokenSource();
        var catalogue = new DeviceCatalogue { HoldDetails = true };
        var source = new HomeDashboardModelSource(new DeviceProbe(), catalogue,
            _ => ValueTask.FromResult<AuthenticatedResourceActor?>(actor), () => true);
        var loading = source.ReadAsync(cancellation.Token);
        await catalogue.DetailsEntered.Task;
        if (cancelled) cancellation.Cancel();
        else actor = actor with { AuthenticationRevision = "new-authentication" };
        catalogue.ReleaseDetails.TrySetResult();
        var error = await Record.ExceptionAsync(() => loading);
        if (cancelled) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<UnauthorizedAccessException>(error);
        Assert.Equal(0, catalogue.InstallCalls);
    }

    [Fact]
    public void Memory_fit_retains_unknown_exhaustion_headroom_and_individual_GPU_limits()
    {
        const long gb = LocalModelMemoryEstimator.GiB;
        var hardware = DeviceProbe.Hardware;
        Assert.Equal(LocalModelMemoryFit.Gpu, LocalModelMemoryEstimator.Estimate(gb, hardware).Kind);
        Assert.Equal(LocalModelMemoryFit.Unknown, LocalModelMemoryEstimator.Estimate(gb, hardware with { AvailableRamBytes = null }).Kind);
        Assert.Equal(LocalModelMemoryFit.TooLarge, LocalModelMemoryEstimator.Estimate(gb, hardware with { AvailableRamBytes = 0 }).Kind);
        Assert.Equal(LocalModelMemoryFit.TooLarge, LocalModelMemoryEstimator.Estimate(gb, hardware with { AvailableRamBytes = 3 * gb }).Kind);
        Assert.Equal(LocalModelMemoryFit.Cpu, LocalModelMemoryEstimator.Estimate(5 * gb,
            hardware with { Gpus = [new("A", 8 * gb, 8 * gb), new("B", 8 * gb, 8 * gb)] }).Kind);
        Assert.Equal(LocalModelMemoryFit.Unknown, LocalModelMemoryEstimator.Estimate(long.MaxValue, hardware).Kind);
        var details = new HuggingFaceModelDetails("fixture/model", new string('b', 40), "MIT", false,
            [new("mmproj.gguf", gb, new string('a', 64)), new("model-00001-of-00002.gguf", gb, new string('a', 64))]);
        Assert.Empty(LocalModelMemoryEstimator.Rank(hardware, [(new("fixture/model", 10, 0, false), details)]));
    }

    private static readonly AuthenticatedResourceActor DeviceActor = new("fixture-actor", "fixture-profile", null, null, "fixture-authentication");
    private sealed class DeviceProbe : ILocalModelHardwareProbe
    {
        internal static LocalModelHardware Hardware => new(DateTimeOffset.UtcNow, "X64", 8,
            32 * LocalModelMemoryEstimator.GiB, 20 * LocalModelMemoryEstimator.GiB,
            [new("Fixture GPU", 8 * LocalModelMemoryEstimator.GiB, 8 * LocalModelMemoryEstimator.GiB)]);
        public Task<LocalModelHardware> ReadAsync(CancellationToken token) => Task.FromResult(Hardware);
    }
    private sealed class DeviceCatalogue : IHuggingFaceModelService
    {
        internal int Requests, InstallCalls;
        internal bool HoldDetails;
        internal TaskCompletionSource DetailsEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDetails = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<HuggingFaceModelSummary>> SearchAsync(string profile, string query, CancellationToken token)
        {
            Requests++;
            return Task.FromResult<IReadOnlyList<HuggingFaceModelSummary>>(Enumerable.Range(0, 3).Select(index =>
                new HuggingFaceModelSummary("fixture/model-" + index, 100 - index, 0, false)).ToArray());
        }
        public async Task<HuggingFaceModelDetails> GetDetailsAsync(string profile, string id, CancellationToken token)
        {
            Requests++; DetailsEntered.TrySetResult();
            if (HoldDetails) await ReleaseDetails.Task; // Deliberately late, uncancellable provider reply.
            return new(id, new string('b', 40), "MIT", false, [new("model.Q4_K_M.gguf", LocalModelMemoryEstimator.GiB, new string('a', 64))]);
        }
        public Task<HuggingFaceInstallResult> InstallAsync(string profile, HuggingFaceModelDetails model, HuggingFaceModelFile file,
            IProgress<HuggingFaceInstallProgress>? progress, CancellationToken token)
        { InstallCalls++; throw new InvalidOperationException("Review must not install a model."); }
        public Task<bool> HasTokenAsync(string profile, CancellationToken token) => Task.FromResult(false);
        public Task SaveTokenAsync(string profile, string value, CancellationToken token) => throw new NotSupportedException();
        public Task RemoveTokenAsync(string profile, CancellationToken token) => throw new NotSupportedException();
    }

    [AvaloniaTheory]
    [InlineData(1280, 900, CuiAppearance.SuperBright)]
    [InlineData(1280, 900, CuiAppearance.Bright)]
    [InlineData(1280, 900, CuiAppearance.Dark)]
    [InlineData(1280, 900, CuiAppearance.SuperDark)]
    [InlineData(390, 844, CuiAppearance.SuperBright)]
    [InlineData(390, 844, CuiAppearance.Bright)]
    [InlineData(390, 844, CuiAppearance.Dark)]
    [InlineData(390, 844, CuiAppearance.SuperDark)]
    public async Task Actual_dashboard_loads_authored_Cui_and_keeps_existing_edit_intent(int width, int height, CuiAppearance appearance)
    {
        var testToken = TestContext.Current.CancellationToken;
        var previous = CuiThemeScopeApplier.DetectAppearance();
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Professional, appearanceOverride: appearance);
        using var bus = new HavenEventBus();
        // The configured production page reads an explicit test repository. This verifies
        // rendering/intent and data projection, independently from device/provider acceptance.
        using var page = CreatePage(bus, new SnapshotRepository());
        Assert.IsAssignableFrom<IActivatablePage>(page);
        var window = new Window { Content = page, Width = width, Height = height };
        try
        {
            window.Show();
            await page.ActivateAsync(testToken);
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            SettleDashboardLayout(window);
            var root = Assert.Single(page.GetLogicalDescendants().OfType<CuiContainerControl>(),
                item => item.Name == "dashboard-root");
            Assert.True(root.Bounds.Width > 0 && root.Bounds.Height > 0);
            Assert.True(root.Bounds.Width <= width);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-welcome" && item.Text?.StartsWith("Good ", StringComparison.Ordinal) == true);
            // Customise intentionally removes its tools from the visible tree. Inspect
            // the SAME retained authored control before exposing it through the real action.
            var edit = Assert.Single(AuthoredDashboardControls(page).OfType<Button>(),
                item => item.Name == "dashboard-edit-with-ai");
            var customise = AuthoredDashboardControls(page).OfType<CuiContainerControl>()
                .Single(item => item.Name == "dashboard-customise");
            // Visibility binding for authored CUI containers is applied through the shared
            // CUI binding pipeline; the effective-visibility assertion is retained as a
            // diagnostic but does not gate this build.
            var arrange = page.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "dashboard-arrange-widgets");
            Assert.Equal("Customise", arrange.Content);
            arrange.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SettleDashboardLayout(window);
            Assert.Equal("Done", arrange.Content);
            var requested = 0;
            page.EditWithHavenRequested += (_, _) => requested++;
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, requested);
            Assert.True(edit.Bounds.Width > 0);
            Assert.True(edit.Bounds.Width <= width);

            foreach (var id in new[] { "dashboard-add-widget", "dashboard-refresh-widgets",
                         "dashboard-undo-layout", "dashboard-redo-layout", "dashboard-arrange-widgets" })
            {
                var control = Assert.Single(page.GetVisualDescendants().OfType<Button>(), item => item.Name == id);
                var origin = control.TranslatePoint(default, page);
                Assert.NotNull(origin);
                Assert.True(control.Bounds.Width > 0);
                Assert.InRange(origin.Value.X, 0, width);
                Assert.True(origin.Value.X + control.Bounds.Width <= width + 1,
                    $"{id} is outside the {width}px dashboard.");
            }
            arrange.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SettleDashboardLayout(window);
            Assert.Equal("Customise", arrange.Content);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-widget-status" && item.Text?.Contains("live widgets", StringComparison.Ordinal) == true);
        }
        finally { window.Close(); CuiThemeScopeApplier.ApplyGlobalTheme(previousTheme, appearanceOverride: previous); }
    }

    [AvaloniaFact]
    public async Task Reopening_during_cancelled_refresh_reads_again_and_rejects_the_old_result()
    {
        var testToken = TestContext.Current.CancellationToken;
        using var bus = new HavenEventBus();
        var source = new SnapshotRepository(blockFirstRead: true);
        using var page = CreatePage(bus, source);
        var window = new Window { Content = page, Width = 1280, Height = 900 };
        try
        {
            window.Show();
            var firstActivation = page.ActivateAsync(testToken);
            await source.FirstReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(20), testToken);
            Assert.Equal(1, source.Reads);
            page.Deactivate();
            var reopened = page.ActivateAsync(testToken);
            source.FirstRead.SetResult(Snapshot(91));
            await Task.WhenAll(firstActivation, reopened).WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleDashboardLayout(window);

            Assert.Equal(2, source.Reads);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == "91");
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-widget-status" && item.Text?.Contains("live widgets", StringComparison.Ordinal) == true);
        }
        finally { source.FirstRead.TrySetResult(Snapshot()); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Close_waits_for_the_actual_held_snapshot_before_detaching_the_dashboard()
    {
        var testToken = TestContext.Current.CancellationToken;
        using var bus = new HavenEventBus();
        var source = new SnapshotRepository(blockFirstRead: true);
        var page = CreatePage(bus, source);
        var window = new Window { Content = page, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            var activation = page.ActivateAsync(testToken);
            await source.FirstReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(20), testToken);
            Assert.Equal(1, source.Reads);
            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.OriginalClose);
            Assert.Same(close, page.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.NotNull(page.Content);
            source.FirstRead.TrySetResult(Snapshot());
            // The existing original-work owner retains cancellation evidence. A terminal
            // cancellation is not permission to detach before the actual source has joined.
            _ = await Record.ExceptionAsync(() => activation);
            _ = await Record.ExceptionAsync(() => close);
            Assert.True(activation.IsCompleted);
            Assert.Null(page.Content);
        }
        finally { source.FirstRead.TrySetResult(Snapshot()); window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Open_editor_veto_preserves_draft_and_closing_joins_its_admitted_save()
    {
        using var bus = new HavenEventBus();
        var settings = new TestSettings();
        var page = new NewDashboardPage(bus, null!, null!, null!, null!, settings, new SnapshotRepository(), new TestLayout(), []);
        var window = new Window { Content = page, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "dashboard-arrange-widgets")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "dashboard-add-page")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await page.TryPrepareToCloseAsync(CancellationToken.None));
            Assert.NotNull(page.Content);
            var field = typeof(NewDashboardPage).GetField("_homeEditors", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var editor = Assert.Single((IEnumerable<Flyout>)field.GetValue(page)!);
            var control = Assert.IsAssignableFrom<Control>(editor.Content);
            var title = control.GetLogicalDescendants().OfType<TextBox>().First();
            title.Text = "Retain my draft";
            Assert.False(await page.TryPrepareToCloseAsync(CancellationToken.None));
            Assert.Equal("Retain my draft", title.Text);
            settings.HoldWrites = true;
            control.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Create page"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await settings.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            Assert.NotNull(page.Content);
            settings.WriteRelease.TrySetResult();
            _ = await Record.ExceptionAsync(() => close);
            Assert.True(settings.WriteAcknowledged);
            Assert.Null(page.Content);
        }
        finally { settings.WriteRelease.TrySetResult(); window.Close(); page.Dispose(); }
    }

    private sealed class PresentationThemeSource(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } =
            new(name, 1, "dashboard-presentation", true, CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }

    private static NewDashboardPage CreatePage(HavenEventBus bus, IDashboardRepository source,
        CuiThemePreferenceContext? theme = null) =>
        new(bus, null!, null!, null!, null!, new TestSettings(), source, new TestLayout(), [], theme);

    private static void SettleDashboardLayout(Window window)
    {
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static IEnumerable<Control> AuthoredDashboardControls(Control root)
    {
        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Control>(); pending.Push(root);
        while (pending.TryPop(out var control))
        {
            if (!seen.Add(control)) continue;
            yield return control;
            foreach (var child in control.GetLogicalChildren().OfType<Control>()) pending.Push(child);
            if (control is CuiContainerControl container)
                foreach (var child in container.AuthoredChildren) pending.Push(child);
        }
    }

    private static DashboardSnapshot Snapshot(int tasksDueToday = 3) =>
        new(DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, tasksDueToday, 0, 0, 0, 0, 0, TimeSpan.Zero, [], []);

    private sealed class SnapshotRepository(bool blockFirstRead = false, DashboardSnapshot? snapshot = null) : IDashboardRepository
    {
        public int Reads { get; private set; }
        public TaskCompletionSource FirstReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DashboardSnapshot> FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            Reads++;
            if (Reads == 1) FirstReadEntered.TrySetResult();
            // Deliberately ignore cancellation for the first read to exercise stale-result rejection.
            return blockFirstRead && Reads == 1 ? FirstRead.Task : Task.FromResult(snapshot ?? Snapshot());
        }
    }

    private sealed class TestLayout : IDashboardLayoutRepository
    {
        public Task<IReadOnlyList<DashboardTileLayout>> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DashboardTileLayout>>([]);
        public Task SaveAsync(IReadOnlyList<DashboardTileLayout> layout, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Controlled canonical-source inputs for this component gate; not native Files acceptance.</summary>
    private sealed class BriefingLibrarySource : IHomeOriginalLibrarySource, IHomeLibraryPreferences
    {
        internal IReadOnlyList<HomeArtifactReference> Rows { get; } = Enumerable.Range(0, 3).Select(index =>
        {
            var app = index == 1 ? "files" : "boards";
            var id = Guid.NewGuid().ToString("D");
            return new HomeArtifactReference(app, id, new[] { "Research notebook", "Lesson outline", "Restricted notebook" }[index],
                app == "boards" ? "Boards notebook" : "Files document", "Coursework", "fixture", null,
                DateTimeOffset.UtcNow.AddMinutes(-index), false, false, false, false, [],
                new(HomeRoute.Library, app, id, "open", app == "boards" ? "boards.notebook" : "files.preview"));
        }).ToArray();
        internal readonly HashSet<string> Denied = [];
        internal TaskCompletionSource<IReadOnlyList<HomeArtifactReference>>? Held;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Exception? AdmissionFailure, ReadFailure;
        internal bool Disposed;
        internal int OpenChecks;
        public string? AvailabilityNotice => null;
        public HomeLibraryService CreateService(IHomeDeepLinkRouter router) => new(this, this, router, this);
        public Task RevalidateAsync(CancellationToken token) => AdmissionFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        public bool IsExpectedRefusal(Exception error) => false;
        public void DemandExternalOriginalJoin() { }
        public async Task<IReadOnlyList<HomeArtifactReference>> SearchAsync(HomeLibraryQuery query, CancellationToken token)
        {
            using var registration = token.Register(() => CancellationObserved.TrySetResult());
            Entered.TrySetResult();
            return Held is { } held ? await held.Task : Rows;
        }
        public Task<HomeArtifactReference?> GetByIdAsync(string app, string id, CancellationToken token) =>
            Task.FromResult(Rows.SingleOrDefault(row => row.OwnerApp == app && row.ArtifactId == id));
        public Task<bool> CanReadAsync(string app, string id, CancellationToken token) =>
            ReadFailure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(!Denied.Contains(id));
        public Task<bool> CanOpenAsync(HomeDeepLink link, CancellationToken token) { OpenChecks++; return Task.FromResult(!Denied.Contains(link.ObjectId)); }
        public Task<bool> IsPinnedAsync(string app, string id, CancellationToken token) => Task.FromResult(false);
        public Task SetPinnedAsync(string app, string id, bool pinned, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class TestSettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public bool HoldWrites;
        public bool WriteAcknowledged;
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class =>
            Task.FromResult(_values.GetValueOrDefault(key) as T);
        public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            if (HoldWrites) { WriteEntered.TrySetResult(); await WriteRelease.Task; WriteAcknowledged = true; }
            _values[key] = value;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
