using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Home.Discover;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DiscoverView = Haven.Desktop.Views.Pages.Home.HomeDiscoverPage;

namespace Haven.Desktop.Tests;

public sealed partial class HomeDulcheIntegrationTests
{
    // Drive the actual authored navigation before using the retained production controls.
    // No model-selection helper is allowed to start inference, load weights or install files.
    private static async Task OpenFirstModelAsync(DiscoverView page, CancellationToken token)
    {
        Assert.True(await page.TryPrepareToCloseAsync(token));
        var mine = ModelUiControl<Button>(page, "discover-my-models");
        Assert.True(mine.IsEffectivelyVisible && mine.IsEffectivelyEnabled);
        mine.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await page.TryPrepareToCloseAsync(token));
        Assert.Equal("mine", page.CurrentView);
        await ModelUiWaitAsync(() => page.GetVisualDescendants().OfType<Button>().Any(button =>
            button.Name == "dulche-local-details" && button.IsEffectivelyVisible), token);
        var details = page.GetVisualDescendants().OfType<Button>().First(button => button.Name == "dulche-local-details");
        Assert.True(details.IsEffectivelyEnabled);
        details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await page.TryPrepareToCloseAsync(token));
        Assert.Equal("local-details", page.CurrentView);
        if (TopLevel.GetTopLevel(page) is Window window) ModelUiLayout(window);
    }

    [AvaloniaTheory]
    [InlineData("Glow")]
    [InlineData("Professional")]
    public async Task Atlas_models_use_actual_catalogue_and_runtime_controls_without_loading_on_navigation(string themeName)
    {
        var root = Directory.CreateTempSubdirectory("dulche-atlas-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var handler = new ModelPresentationHandler();
        using var theme = new CuiThemePreferenceContext(new ModelsThemeSource(themeName));
        var catalogue = new ModelsCatalogueSource();
        var page = new DiscoverView(new HomeDiscoverCatalog([catalogue]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler), theme: theme);
        var window = new Window { Width = 1440, Height = 900, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token);
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            foreach (var size in new[] { new Size(1440, 900), new Size(720, 650) })
            {
                window.Width = size.Width; window.Height = size.Height;
                await page.DispatchAsync("models.discover", null, timeout.Token); ModelUiLayout(window);
                Assert.Equal(2, page.VisibleModels.Count);
                Assert.False(ModelUiControl<Control>(page, "discover-dulche-mount").IsEffectivelyVisible);
                AssertModelsFit(page, "discover-search-field", "discover-search", "discover-locality", "discover-filter-all", "discover-more-filters");
                Assert.False(ModelUiControl<Control>(page, "discover-advanced-filters").IsVisible);
                var searchField = ModelUiControl<Control>(page, "discover-search-field");
                // The real scroll viewport and authored padding own the available width.
                // At720px the desktop padding plus scrollbar exceeds the former72px estimate.
                var searchRow = searchField.GetVisualAncestors().OfType<CuiContainerControl>().First();
                var minimumSearchWidth = size.Width < 760 ? searchRow.Bounds.Width - 1 : size.Width - 350;
                Assert.True(searchField.Bounds.Width >= minimumSearchWidth,
                    $"Search {searchField.Bounds.Width}px must fill the available row {searchRow.Bounds.Width}px in the {size.Width}px window (minimum {minimumSearchWidth}px).");
                Assert.Equal(2, page.GetVisualDescendants().OfType<TextBlock>().Count(control => control.Name == "discover-model-monogram"));
                CaptureModelsFrame(window, themeName, "discover", size);
                ModelUiControl<Button>(page, "discover-more-filters").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await page.TryPrepareToCloseAsync(timeout.Token)); ModelUiLayout(window);
                AssertModelsFit(page, "discover-category", "discover-provider", "discover-install-state");
                ModelUiControl<Button>(page, "discover-more-filters").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await page.TryPrepareToCloseAsync(timeout.Token)); ModelUiLayout(window);

                ModelUiControl<Button>(page, "discover-my-models").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await page.TryPrepareToCloseAsync(timeout.Token)); ModelUiLayout(window);
                Assert.Equal("mine", page.CurrentView);
                Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => button.Name == "dulche-local-details");
                AssertNoVisibleNamed<TextBox>(page, "Dulche prompt");
                CaptureModelsFrame(window, themeName, "my-models", size);
                var details = ModelUiControl<Button>(page, "dulche-local-details");
                details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await page.TryPrepareToCloseAsync(timeout.Token)); ModelUiLayout(window);
                Assert.Equal("local-details", page.CurrentView);
                Assert.Equal(ModelPresentationHandler.FullName, ModelUiNamed<TextBlock>(page, "Selected model name").Text);
                AssertModelsFit(page, "dulche-model", "dulche-prompt", "dulche-generate");
                AssertNoVisibleNamed<TextBox>(page, "Local model path");
                ModelUiNamed<TextBox>(page, "Dulche prompt").Text = "Retain this draft across every model section.";
                CaptureModelsFrame(window, themeName, "chat", size);

                await SelectModelSectionAsync(page, "dulche-view-performance", timeout.Token); ModelUiLayout(window);
                var resources = ModelUiNamed<TextBlock>(page, "Observed GPU and memory resources").Text;
                Assert.Contains("RTX fixture", resources); Assert.Contains("V100 fixture", resources);
                Assert.Contains("2,048 / 16,384 MiB", resources); Assert.Contains("41 °C", resources);
                var timings = ModelUiNamed<TextBlock>(page, "Measured inference timings").Text;
                Assert.Contains("Prefill: 76.5 tokens/s", timings); Assert.Contains("Generation: 29.4 tokens/s", timings);
                Assert.Contains("First token: 0.42 s", timings);
                Assert.Contains("524,288", ModelUiNamed<TextBlock>(page, "Observed inference profile").Text);
                AssertPersistentModelField(page, "dulche-profile-device-label", "dulche-profile-device", "Execution device");
                AssertPersistentModelField(page, "dulche-profile-context-label", "dulche-profile-context", "Context length (tokens)");
                Assert.Equal("524288", ModelUiControl<TextBox>(page, "dulche-profile-context").Text);
                CaptureModelsFrame(window, themeName, "performance", size);

                await SelectModelSectionAsync(page, "dulche-view-adapters", timeout.Token); ModelUiLayout(window);
                Assert.Contains("Included in model profile", ModelUiNamed<TextBlock>(page, "Selected adapter identity and scope").Text);
                Assert.Contains("multimodal.gguf", ModelUiNamed<TextBlock>(page, "Selected adapter identity and scope").Text);
                Assert.False(ModelUiControl<Button>(page, "dulche-adapter-detach").IsEnabled);
                AssertPersistentModelField(page, "dulche-adapter-scale-label", "dulche-adapter-scale", "Scale (multiplier)");
                AssertPersistentModelField(page, "dulche-adapter-scope-label", "dulche-adapter-scope", "Attach to");
                Assert.Equal("1", ModelUiControl<TextBox>(page, "dulche-adapter-scale").Text);
                Assert.Equal("Model", ModelUiControl<ComboBox>(page, "dulche-adapter-scope").SelectedItem);
                CaptureModelsFrame(window, themeName, "adapters", size);
                ModelUiControl<Control>(page, "dulche-adapter-attach").BringIntoView(); ModelUiLayout(window);
                foreach (var id in new[] { "dulche-adapter-scale-label", "dulche-adapter-scale", "dulche-adapter-scope-label", "dulche-adapter-scope" })
                {
                    var field = ModelUiControl<Control>(page, id); var point = field.TranslatePoint(default, window);
                    Assert.NotNull(point); Assert.InRange(point.Value.Y, -1, window.Bounds.Height);
                    Assert.True(point.Value.Y + field.Bounds.Height <= window.Bounds.Height + 1, id + " must be reachable in the viewport.");
                }
                CaptureModelsFrame(window, themeName, "adapter-fields", size);
                ModelUiControl<Control>(page, "dulche-manage").BringIntoView(); ModelUiLayout(window);

                await SelectModelSectionAsync(page, "dulche-manage", timeout.Token); ModelUiLayout(window);
                Assert.True(ModelUiControl<ComboBox>(page, "dulche-load-policy").IsEffectivelyVisible);
                AssertNoVisibleNamed<TextBox>(page, "Local model path");
                CaptureModelsFrame(window, themeName, "advanced", size);
                await SelectModelSectionAsync(page, "dulche-view-chat", timeout.Token);
                Assert.Equal("Retain this draft across every model section.", ModelUiNamed<TextBox>(page, "Dulche prompt").Text);
                ModelUiControl<Button>(page, "discover-back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
                ModelUiControl<Button>(page, "discover-browse").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await page.TryPrepareToCloseAsync(timeout.Token)); ModelUiLayout(window);
                Assert.True(ModelUiControl<TextBox>(page, "dulche-import-path").IsEffectivelyVisible);
                AssertNoVisibleNamed<TextBox>(page, "Dulche prompt");
                AssertPersistentModelField(page, "dulche-filter-architecture-label", "dulche-filter-architecture", "Architecture / family");
                AssertPersistentModelField(page, "dulche-filter-size-label", "dulche-filter-size", "Maximum parameters (billions)");
                Assert.Contains("Search the catalogue", ModelUiControl<TextBlock>(page, "dulche-results-status").Text);
                AssertCatalogueActionsDisabled(page, "inspect", "download", "remove", "pause", "resume", "cancel", "import");
                Assert.False(ModelUiControl<ComboBox>(page, "dulche-results").IsEffectivelyVisible);
                Assert.False(ModelUiControl<ComboBox>(page, "dulche-files").IsEffectivelyVisible);
                CaptureModelsFrame(window, themeName, "catalogue", size);
            }
            Assert.False(handler.Commands.Reader.TryRead(out _));
            Assert.Equal(0, catalogue.Executions);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Catalogue_controls_follow_actual_repository_file_ownership_and_transfer_state_without_mutating_them()
    {
        var root = Directory.CreateTempSubdirectory("dulche-catalogue-availability-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var handler = new ModelPresentationHandler
        {
            CatalogueResults = JsonNode.Parse("""[{"repo":"owner/first","name":"First repository"},{"repo":"owner/second","name":"Second repository"}]""")!.AsArray(),
            RepositoryDetails = JsonNode.Parse("""{"revision":"exact-repository-revision","files":[{"path":"weights.gguf"}]}""")!.AsObject(),
            InstalledFiles = JsonNode.Parse("""[{"id":"managed","path":"/managed/model.gguf","ownership":"managed"},{"id":"external","path":"/outside/model.gguf","ownership":"external"},{"id":"unknown","path":"/unknown/model.gguf"}]""")!.AsArray(),
            CatalogueJobs = JsonNode.Parse("""[{"id":"queued","state":"queued"},{"id":"running","state":"running"},{"id":"paused","state":"paused"},{"id":"storage","state":"paused_storage"},{"id":"failed","state":"failed"},{"id":"complete","state":"completed"},{"id":"cancelled","state":"cancelled"}]""")!.AsArray()
        };
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenDulcheCatalogueAsync(page, timeout.Token);
            Assert.Contains("Search the catalogue", ModelUiControl<TextBlock>(page, "dulche-results-status").Text);
            AssertCatalogueActionsDisabled(page, "inspect", "download", "remove", "pause", "resume", "cancel", "import");
            ModelUiControl<TextBox>(page, "dulche-search").Text = "First query";
            await SelectModelSectionAsync(page, "dulche-find", timeout.Token);
            Assert.True(ModelUiControl<Button>(page, "dulche-inspect").IsEnabled);
            AssertCatalogueActionsDisabled(page, "download", "remove");
            await SelectModelSectionAsync(page, "dulche-inspect", timeout.Token);
            var files = ModelUiControl<ComboBox>(page, "dulche-files");
            Assert.Single(files.Items); Assert.Null(files.SelectedItem);
            AssertCatalogueActionsDisabled(page, "download");
            files.SelectedIndex = 0;
            Assert.True(ModelUiControl<Button>(page, "dulche-download").IsEnabled);
            var results = ModelUiControl<ComboBox>(page, "dulche-results");
            results.SelectedIndex = 1;
            Assert.Empty(files.Items); Assert.Null(files.SelectedItem);
            Assert.Same(files, ModelUiControl<ComboBox>(page, "dulche-files"));
            Assert.DoesNotContain(page, files.GetVisualAncestors());
            AssertCatalogueActionsDisabled(page, "download");
            await SelectModelSectionAsync(page, "dulche-inspect", timeout.Token);
            ModelUiLayout(window);
            Assert.Same(files, ModelUiControl<ComboBox>(page, "dulche-files"));
            Assert.Contains(page, files.GetVisualAncestors());
            Assert.True(files.IsEffectivelyVisible && files.Bounds.Width > 0 && files.Bounds.Height > 0);
            files.SelectedIndex = 0;
            Assert.True(ModelUiControl<Button>(page, "dulche-download").IsEnabled);
            handler.RepositoryDetails["revision"] = "";
            await SelectModelSectionAsync(page, "dulche-inspect", timeout.Token);
            files.SelectedIndex = 0;
            AssertCatalogueActionsDisabled(page, "download");
            Assert.Contains("did not report a revision", ModelUiControl<TextBlock>(page, "dulche-files-status").Text);
            handler.RepositoryDetails["revision"] = "new-exact-revision";
            await SelectModelSectionAsync(page, "dulche-inspect", timeout.Token);
            files.SelectedIndex = 0;
            Assert.True(ModelUiControl<Button>(page, "dulche-download").IsEnabled);
            ModelUiControl<TextBox>(page, "dulche-filter-architecture").Text = "Changed architecture";
            ModelUiLayout(window);
            Assert.Empty(results.Items); Assert.Empty(files.Items);
            AssertCatalogueActionsDisabled(page, "inspect", "download", "remove");
            Assert.Contains("Search the catalogue", ModelUiControl<TextBlock>(page, "dulche-results-status").Text);

            await SelectModelSectionAsync(page, "dulche-installed", timeout.Token);
            Assert.True(ModelUiControl<Button>(page, "dulche-remove").IsEnabled);
            AssertCatalogueActionsDisabled(page, "download");
            results.SelectedIndex = 1; ModelUiLayout(window);
            Assert.True(ModelUiControl<Button>(page, "dulche-remove").IsEnabled);
            Assert.Contains("external files stay", ModelUiControl<TextBlock>(page, "dulche-removal-status").Text);
            results.SelectedIndex = 2; ModelUiLayout(window);
            AssertCatalogueActionsDisabled(page, "remove");
            Assert.Contains("ownership has not been reported", ModelUiControl<TextBlock>(page, "dulche-removal-status").Text);
            results.SelectedIndex = -1;
            AssertCatalogueActionsDisabled(page, "inspect", "download", "remove");

            var jobs = ModelUiControl<ComboBox>(page, "dulche-jobs");
            await ModelUiWaitAsync(() => jobs.Items.Count == 7, timeout.Token);
            Assert.Null(jobs.SelectedItem);
            var eligibility = new[] { (true, false, true), (true, false, true), (false, true, true),
                (false, true, true), (false, true, true), (false, false, false), (false, false, false) };
            for (var index = 0; index < eligibility.Length; index++)
            {
                jobs.SelectedIndex = index;
                Assert.Equal(eligibility[index].Item1, ModelUiControl<Button>(page, "dulche-pause").IsEnabled);
                Assert.Equal(eligibility[index].Item2, ModelUiControl<Button>(page, "dulche-resume").IsEnabled);
                Assert.Equal(eligibility[index].Item3, ModelUiControl<Button>(page, "dulche-cancel").IsEnabled);
            }
            jobs.SelectedIndex = 0;
            handler.CatalogueJobs = JsonNode.Parse("""[{"id":"replacement","state":"running"}]""")!.AsArray();
            await page.ActivateAsync(timeout.Token);
            await ModelUiWaitAsync(() => jobs.Items.Count == 1, timeout.Token);
            Assert.Null(jobs.SelectedItem); // No first-row fallback to a different owned transfer.
            AssertCatalogueActionsDisabled(page, "pause", "resume", "cancel");
            handler.CatalogueJobs = [];
            await page.ActivateAsync(timeout.Token);
            await ModelUiWaitAsync(() => jobs.Items.Count == 0, timeout.Token);
            Assert.Contains("No model transfers were reported", ModelUiControl<TextBlock>(page, "dulche-transfers-status").Text);
            handler.CatalogueResults = [];
            await SelectModelSectionAsync(page, "dulche-find", timeout.Token);
            Assert.Contains("No catalogue results match", ModelUiControl<TextBlock>(page, "dulche-results-status").Text);
            AssertCatalogueActionsDisabled(page, "inspect", "download", "remove");
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task A_held_catalogue_read_cannot_publish_an_old_query_or_enable_its_repository_files()
    {
        var root = Directory.CreateTempSubdirectory("dulche-catalogue-query-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ModelPresentationHandler
        {
            CatalogueResults = JsonNode.Parse("""[{"repo":"owner/old","name":"Old query repository"}]""")!.AsArray(),
            BeforeCatalogueSearch = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); }
        };
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenDulcheCatalogueAsync(page, timeout.Token);
            ModelUiControl<TextBox>(page, "dulche-search").Text = "Submitted query";
            ModelUiControl<Button>(page, "dulche-find").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(timeout.Token);
            ModelUiControl<TextBox>(page, "dulche-search").Text = "New query draft";
            var preparation = page.TryPrepareToCloseAsync(timeout.Token);
            Assert.False(preparation.IsCompleted); // The held read still belongs to its original owner.
            release.TrySetResult();
            Assert.True(await preparation);
            Assert.Equal("New query draft", ModelUiControl<TextBox>(page, "dulche-search").Text);
            Assert.Empty(ModelUiControl<ComboBox>(page, "dulche-results").Items);
            Assert.Empty(ModelUiControl<ComboBox>(page, "dulche-files").Items);
            Assert.Contains("Search the catalogue", ModelUiControl<TextBlock>(page, "dulche-results-status").Text);
            AssertCatalogueActionsDisabled(page, "inspect", "download", "remove");
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { release.TrySetResult(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Malformed_or_unsupported_configured_model_does_not_remove_valid_rows_or_admit_inference()
    {
        var root = Directory.CreateTempSubdirectory("dulche-model-isolation-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ModelPresentationHandler();
        var models = handler.Status["models"]!.AsArray();
        models.Add(JsonValue.Create("malformed")); models.Add(new JsonObject { ["name"] = "Missing identity" });
        models.Add(JsonNode.Parse("""{"id":"unsupported","name":"Unsupported imported model","state":"installed","serving_supported":false,"error":"Architecture is not supported by this engine."}"""));
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 900, Height = 700, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            ModelUiControl<Button>(page, "discover-my-models").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token)); ModelUiLayout(window);
            var details = page.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "dulche-local-details").ToArray();
            Assert.Equal(2, details.Length);
            var unsupported = details.Single(button => AutomationProperties.GetName(button) == "Details for Unsupported imported model");
            unsupported.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token)); ModelUiLayout(window);
            Assert.Equal("Unsupported imported model", ModelUiNamed<TextBlock>(page, "Selected model name").Text);
            Assert.False(ModelUiControl<Button>(page, "dulche-generate").IsEnabled);
            Assert.False(ModelUiControl<Button>(page, "dulche-toggle").IsEnabled);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible &&
                text.Text?.Contains("Architecture is not supported", StringComparison.Ordinal) == true);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Reobserving_the_same_completed_optimisation_preserves_the_unsaved_profile_draft()
    {
        var root = Directory.CreateTempSubdirectory("dulche-profile-draft-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using var handler = new ModelPresentationHandler();
        handler.Operation = JsonNode.Parse("""{"id":"measured","model":"one","state":"completed","context_tokens":524288,"rollback_available":true,"result":{"base":{"decode_tok_s":10},"chosen":{"decode_tok_s":10.71},"gain_pct":7.1}}""")!;
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            await SelectModelSectionAsync(page, "dulche-view-performance", timeout.Token);
            ModelUiControl<TextBox>(page, "dulche-profile-context").Text = "16384";
            ModelUiControl<TextBox>(page, "dulche-profile-name").Text = "Unapplied profile draft";
            var profileReads = handler.ProfileReads;
            var capabilityReads = handler.CapabilityReads;
            handler.Status["models"]![0]!["last_error"] = "A refreshed diagnostic observation";
            // Exercise the real twenty-second capability cache expiry and periodic poll.
            // A section switch alone would not re-observe the completed operation.
            await ModelUiWaitAsync(() => handler.CapabilityReads > capabilityReads, timeout.Token);
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.Equal(profileReads, handler.ProfileReads);
            Assert.Equal("16384", ModelUiControl<TextBox>(page, "dulche-profile-context").Text);
            Assert.Equal("Unapplied profile draft", ModelUiControl<TextBox>(page, "dulche-profile-name").Text);
            var comparison = ModelUiNamed<TextBlock>(page, "Measured optimisation comparison").Text;
            Assert.Contains("Baseline: 10 tokens/s", comparison);
            Assert.Contains("Chosen settings: 10.71 tokens/s", comparison);
            Assert.Contains("7.1%", comparison);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Profile_and_loading_policy_changes_use_the_actual_persistence_endpoints_only_after_explicit_apply()
    {
        var root = Directory.CreateTempSubdirectory("dulche-profile-apply-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ModelPresentationHandler();
        var paths = new Paths(root.FullName); var preferences = new UserPreferencesService(paths);
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null, new DulcheLocalClient(preferences, handler));
        var window = new Window { Width = 1000, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            await SelectModelSectionAsync(page, "dulche-view-performance", timeout.Token);
            ModelUiControl<ComboBox>(page, "dulche-profiles").SelectedIndex = 1;
            Assert.False(handler.Commands.Reader.TryRead(out _));
            await SelectModelSectionAsync(page, "dulche-profile-select", timeout.Token);
            var applied = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/profiles/fast/select", applied.Path);
            Assert.Equal("fast", handler.Status["models"]![0]!["profile_id"]!.ToString());
            ModelUiControl<TextBox>(page, "dulche-profile-context").Text = "524288";
            ModelUiControl<TextBox>(page, "dulche-profile-name").Text = "Large context draft";
            await SelectModelSectionAsync(page, "dulche-profile-save", timeout.Token);
            var saved = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/profiles", saved.Path);
            Assert.Equal("fast", saved.Body["source_id"]!.ToString());
            Assert.Equal(524288, saved.Body["settings"]!["context"]!.GetValue<int>());
            Assert.Equal("Large context draft", saved.Body["name"]!.ToString());
            await SelectModelSectionAsync(page, "dulche-manage", timeout.Token);
            ModelUiControl<ComboBox>(page, "dulche-load-policy").SelectedIndex = 1;
            Assert.False(handler.Commands.Reader.TryRead(out _));
            await SelectModelSectionAsync(page, "dulche-load-policy-apply", timeout.Token);
            var policy = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/settings", policy.Path); Assert.Equal("resources", policy.Body["policy"]!.ToString());
            Assert.Equal("resources", preferences.DulchePolicy);
            Assert.Equal("resources", new UserPreferencesService(paths).DulchePolicy);
            await SelectModelSectionAsync(page, "dulche-training", timeout.Token);
            Assert.Contains("No training job is active", ModelUiNamed<TextBlock>(page, "Observed training status").Text);
            Assert.True(ModelUiNamed<TextBlock>(page, "Observed training status").IsEffectivelyVisible);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task A_model_without_its_own_capability_cannot_inherit_another_models_optimiser_or_adapter_support()
    {
        var root = Directory.CreateTempSubdirectory("dulche-capability-scope-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ModelPresentationHandler { OmitSelectedModelCapabilities = true };
        handler.Status["resources"] = new JsonObject();
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            await SelectModelSectionAsync(page, "dulche-view-performance", timeout.Token);
            Assert.False(ModelUiControl<Button>(page, "dulche-optimise").IsEnabled);
            Assert.Contains("not been reported", ModelUiNamed<TextBlock>(page, "Observed GPU and memory resources").Text);
            await SelectModelSectionAsync(page, "dulche-view-adapters", timeout.Token);
            Assert.False(ModelUiControl<TextBox>(page, "dulche-adapter-path").IsEffectivelyEnabled);
            Assert.False(ModelUiControl<Button>(page, "dulche-adapter-attach").IsEnabled);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task A_periodic_status_change_cannot_replace_the_model_owning_an_active_response()
    {
        var root = Directory.CreateTempSubdirectory("dulche-stream-identity-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new InferenceManagementHandler();
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            var select = ModelUiControl<ComboBox>(page, "dulche-model");
            var original = select.SelectedItem;
            ModelUiControl<TextBox>(page, "dulche-prompt").Text = "Keep the exact model for this request.";
            ModelUiControl<Button>(page, "dulche-generate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var submitted = await handler.Completion.Task.WaitAsync(timeout.Token);
            Assert.Equal("one", submitted["model"]!.ToString());
            var observed = handler.StatusReads;
            handler.ReplaceModelOnPoll = true;
            await page.ActivateAsync(timeout.Token);
            await ModelUiWaitAsync(() => handler.StatusReads > observed, timeout.Token);
            ModelUiLayout(window);
            Assert.Same(original, select.SelectedItem);
            Assert.False(select.IsEffectivelyEnabled);
            Assert.Equal("Model One", ModelUiNamed<TextBlock>(page, "Selected model name").Text);
            handler.ReplaceModelOnPoll = false;
            handler.ReleaseCompletion.TrySetResult();
            await ModelUiWaitAsync(() => ModelUiControl<Button>(page, "dulche-generate").IsEnabled, timeout.Token);
            Assert.Equal("Completed answer", ModelUiNamed<TextBlock>(page, "Dulche response").Text);
            Assert.Equal(1, handler.CompletionAttempts);
        }
        finally { handler.ReleaseCompletion.TrySetResult(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    private static async Task OpenDulcheCatalogueAsync(DiscoverView page, CancellationToken token)
    {
        ModelUiControl<Button>(page, "discover-browse").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await page.TryPrepareToCloseAsync(token));
        Assert.Equal("catalogue", page.CurrentView);
        await page.ActivateAsync(token); // Observe service-wide transfers through the existing poll owner.
        Assert.True(await page.TryPrepareToCloseAsync(token));
        if (TopLevel.GetTopLevel(page) is Window window) ModelUiLayout(window);
    }

    private static void AssertCatalogueActionsDisabled(Control page, params string[] commands)
    {
        foreach (var command in commands) Assert.False(ModelUiControl<Button>(page, "dulche-" + command).IsEnabled, command);
    }

    private static void AssertPersistentModelField(Control page, string labelId, string fieldId, string text)
    {
        var label = ModelUiControl<TextBlock>(page, labelId);
        var field = ModelUiControl<Control>(page, fieldId);
        Assert.Equal(text, label.Text);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, label.TextWrapping);
        AssertModelsFit(page, labelId, fieldId);
        var bottom = label.TranslatePoint(new Point(0, label.Bounds.Height), page);
        var top = field.TranslatePoint(default, page);
        Assert.NotNull(bottom); Assert.NotNull(top);
        Assert.True(bottom.Value.Y <= top.Value.Y + 1, labelId + " must remain above its input.");
        Assert.True(label.Bounds.Height > 0, labelId);
    }

    private static async Task SelectModelSectionAsync(DiscoverView page, string controlId, CancellationToken token)
    {
        var button = ModelUiControl<Button>(page, controlId);
        Assert.True(button.IsEffectivelyVisible, controlId); Assert.True(button.IsEffectivelyEnabled, controlId);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await page.TryPrepareToCloseAsync(token));
        if (TopLevel.GetTopLevel(page) is Window window) ModelUiLayout(window);
    }

    private static T ModelUiControl<T>(Control root, string id) where T : Control => Assert.Single(
        AuthoredCuiControls.Enumerate(root).OfType<T>(), control => control.Name == id);
    private static T ModelUiNamed<T>(Control root, string name) where T : Control => Assert.Single(
        root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetName(control) == name);
    private static async Task ModelUiWaitAsync(Func<bool> ready, CancellationToken token)
    { while (!ready()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, token); } }
    private static void ModelUiLayout(Window window)
    { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void AssertModelsFit(Control root, params string[] controls)
    {
        foreach (var id in controls)
        {
            var control = ModelUiControl<Control>(root, id); var point = control.TranslatePoint(default, root);
            Assert.True(control.IsEffectivelyVisible, id); Assert.NotNull(point); Assert.True(control.Bounds.Width > 0, id);
            Assert.InRange(point.Value.X, -1, root.Bounds.Width);
            Assert.True(point.Value.X + control.Bounds.Width <= root.Bounds.Width + 1, id + " overflowed horizontally.");
        }
    }
    private static void CaptureModelsFrame(Window window, string theme, string view, Size size)
    {
        var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"models-{view}-{theme.ToLowerInvariant()}-{size.Width:0}x{size.Height:0}.png"));
    }
    private sealed class ModelsThemeSource(string theme) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(theme, 1, "models-fixture", true, CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }
    private sealed class ModelsCatalogueSource : IHomeDiscoverCatalogueSource
    {
        public int Executions;
        public string ProviderId => "catalogue-fixture";
        public Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken token) => Task.FromResult(new HomeDiscoverProviderSnapshot(
            ProviderId, "Fixture catalogue", HomeDiscoverConnectionState.Connected, HomeDiscoverSourceState.Available, "one",
            [Model("local", HomeDiscoverLocality.Local, HomeDiscoverInstallState.Installed), Model("cloud", HomeDiscoverLocality.Cloud, HomeDiscoverInstallState.Unknown)]));
        private HomeDiscoverModel Model(string id, HomeDiscoverLocality locality, HomeDiscoverInstallState installed) => new(
            new(ProviderId, id), id == "local" ? "Installed catalogue fixture" : "Connected cloud fixture", null,
            new HashSet<HomeDiscoverCategory> { HomeDiscoverCategory.Chat }, null, null, null, null, null,
            locality, installed, HomeDiscoverConnectionState.Connected, null, null, null, null, null, null, new HashSet<HomeDiscoverAction>());
        public Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request, CancellationToken token)
        { Executions++; throw new InvalidOperationException("Presentation must not execute a model catalogue action."); }
    }

    private sealed class ModelPresentationHandler : HttpMessageHandler
    {
        public const string FullName = "Local multimodal model with its complete readable identity and 524,288-token context";
        public JsonObject Status { get; } = JsonNode.Parse("""
            {"phase":"ready","stop_all":false,"policy":"never","models":[{"id":"one","name":"Local multimodal model with its complete readable identity and 524,288-token context","state":"unloaded","can_start":true,"serving_supported":true,"context":524288,"profile_id":"current","engine":"Fixture engine","gpu":"Both configured devices","quantization":"Fixture quantisation","recovery":{"attempts":0,"limit":3,"pending":false,"exhausted":false}}],"agents":[],"resources":{"host_available_mib":65536,"gpus":[{"name":"RTX fixture","used_mib":2048,"total_mib":16384,"temperature_c":41},{"name":"V100 fixture","used_mib":1024,"total_mib":16384,"temperature_c":37}]}}
            """)!.AsObject();
        public JsonNode? Operation { get; set; }
        public JsonArray CatalogueResults { get; set; } = [];
        public JsonArray InstalledFiles { get; set; } = [];
        public JsonObject RepositoryDetails { get; set; } = new();
        public JsonArray CatalogueJobs { get; set; } = [];
        public Func<CancellationToken, Task>? BeforeCatalogueSearch { get; init; }
        public bool OmitSelectedModelCapabilities { get; init; }
        public int ProfileReads { get; private set; }
        public int CapabilityReads { get; private set; }
        public Channel<(string Path, JsonNode Body)> Commands { get; } = Channel.CreateUnbounded<(string, JsonNode)>();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(token))!;
            JsonNode response = new JsonObject();
            if (request.Method == HttpMethod.Post && path != "/v1/frontend/connect")
            {
                await Commands.Writer.WriteAsync((path, body), token);
                if (path == "/v1/settings") Status["policy"] = body["policy"]!.DeepClone();
                else if (path == "/v1/models/one/profiles/fast/select") Status["models"]![0]!["profile_id"] = "fast";
            }
            else if (path == "/v1/status") response = Status.DeepClone();
            else if (path == "/v1/catalog/search")
            {
                if (BeforeCatalogueSearch is { } beforeSearch) await beforeSearch(token);
                response = new JsonObject { ["items"] = CatalogueResults.DeepClone() };
            }
            else if (path == "/v1/catalog/installed") response = new JsonObject { ["items"] = InstalledFiles.DeepClone() };
            else if (path == "/v1/catalog/detail") response = RepositoryDetails.DeepClone();
            else if (path == "/v1/catalog/jobs") response = new JsonObject { ["items"] = CatalogueJobs.DeepClone() };
            else if (path == "/v1/inference/capabilities")
            {
                CapabilityReads++;
                var row = OmitSelectedModelCapabilities ? new JsonObject { ["id"] = "one" } :
                    JsonNode.Parse("""{"id":"one","optimise":{"supported":true},"adapters":{"supported":true}}""")!.AsObject();
                row["latest_operation"] = Operation?.DeepClone(); row["rollback_operation"] = Operation?.DeepClone();
                response = new JsonObject { ["optimise"] = new JsonObject { ["supported"] = true },
                    ["adapters"] = new JsonObject { ["supported"] = true }, ["models"] = new JsonArray(row,
                        JsonNode.Parse("""{"id":"another","optimise":{"supported":true},"adapters":{"supported":true}}""")) };
            }
            else if (path == "/v1/models/one/profiles")
            {
                ProfileReads++;
                response = JsonNode.Parse("""{"items":[{"id":"current","name":"Current profile","protected":true,"settings":{"context":524288,"gpu":"auto"}},{"id":"fast","name":"Saved test profile","settings":{"context":8192,"gpu":"auto"}}]}""")!;
            }
            else if (path == "/v1/models/one/adapters")
                response = JsonNode.Parse("""{"items":[{"binding_id":"fixed-multimodal","path":"/fixtures/multimodal.gguf","scale":1,"state":"selected","active":false,"detachable":false,"base_model":"one"}]}""")!;
            else if (path == "/v1/benchmarks")
                response = JsonNode.Parse("""{"items":[{"model":"one","state":"completed","started_epoch":10,"passed":1,"issued":1,"accepted_output_tps":25,"tickets":[{"first_token_s":0.42,"server_timings":{"prompt_per_second":76.5,"predicted_per_second":29.4}}]}]}""")!;
            else if (path == "/v1/training/status") response = new JsonObject { ["message"] = "No training job is active." };
            else if (path.StartsWith("/v1/inference/operations/", StringComparison.Ordinal)) response = Operation?.DeepClone() ?? new JsonObject();
            return new(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
