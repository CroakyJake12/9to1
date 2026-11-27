using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Home;

namespace HavenOS.Browse.Host.Tests;

public sealed class HomeSpecialistsTests
{
    private static readonly AgentDefinition Research = Definition(1, "Research", "Evidence and current sources",
        "research|verify|evidence", "{\"webSearch\":true,\"knowledgeResources\":[\"collection:research\"]}");
    private static readonly AgentDefinition Review = Definition(2, "Review", "Review code and tests", "code|tests",
        "{\"capabilities\":[\"code-review\"]}") with { IsEnabled = false, PreferredModel = "dulche-local:review", FallbackModel = "dulche-local:fallback" };
    private static readonly AgentDefinition Writing = Definition(3, "Writing", "Clear writing and editing", "prose|writing", "{}");

    [Fact]
    public void Canonical_projection_preserves_identity_disabled_definitions_and_searchable_configured_scope()
    {
        AgentDefinition[] actual = [Research, Review, Writing];
        var all = HomeSpecialistCatalogueProjection.Project(actual, string.Empty, HomeSpecialistFilter.All, 3);
        Assert.Equal(actual.Select(item => item.Id).Order(), all.Select(item => item.Definition.Id).Order());
        Assert.Equal("Disabled", Assert.Single(all, item => item.Definition.Id == Review.Id).Status);
        Assert.Equal(Research.Id, Assert.Single(HomeSpecialistCatalogueProjection.Project(actual, "collection:research", HomeSpecialistFilter.All, 2)).Definition.Id);
        Assert.Equal(Review.Id, Assert.Single(HomeSpecialistCatalogueProjection.Project(actual, "code-review", HomeSpecialistFilter.Disabled, 1)).Definition.Id);
        Assert.Empty(HomeSpecialistCatalogueProjection.Project(actual, "code-review", HomeSpecialistFilter.Enabled, 1));
        var renamed = Review with { Name = "Research" };
        Assert.Equal(Review.Id, HomeSpecialistCatalogueProjection.Inspect(renamed).Target.AgentId);
        Assert.NotEqual(HomeSpecialistCatalogueProjection.Fingerprint(Review), HomeSpecialistCatalogueProjection.Fingerprint(renamed));
        Assert.Equal("Not specified", HomeSpecialistCatalogueProjection.Inspect(Research).ModelPolicy);
    }

    [Fact]
    public void Scope_inspection_is_explicit_about_malformed_policy_and_does_not_expose_unknown_values_or_uri_credentials()
    {
        var scope = HomeSpecialistCatalogueProjection.ReadScope("{\"capabilities\":[\"code-review\",\"code-review\"],\"browserUse\":true,\"computerUse\":false,\"knowledgeResources\":[\"https://name:secret@example.test/source?token=hidden#fragment\"],\"accessToken\":\"private-value\"}");
        Assert.True(scope.IsValid);
        Assert.Equal(new[] { "browser-use", "code-review" }, scope.Capabilities);
        var source = Assert.Single(scope.Sources);
        Assert.StartsWith("https://example.test/source", source);
        Assert.DoesNotContain("secret", source); Assert.DoesNotContain("hidden", source.Replace("query parameters hidden", string.Empty));
        Assert.Contains("Additional policy fields", scope.Note);
        Assert.True(scope.HasUninspectedFields);
        Assert.DoesNotContain("No tools", scope.Summary);
        Assert.DoesNotContain("private-value", scope.Note);
        Assert.False(HomeSpecialistCatalogueProjection.ReadScope("{").IsValid);
        Assert.False(HomeSpecialistCatalogueProjection.ReadScope("[]").IsValid);
        var malformed = HomeSpecialistCatalogueProjection.ReadScope("{\"capabilities\":42}");
        Assert.False(malformed.IsValid); Assert.Contains("needs attention", malformed.Summary);
        Assert.DoesNotContain("No tools", malformed.Summary);
    }

    [Fact]
    public void Duplicate_or_missing_catalogue_identity_is_rejected_before_selection()
    {
        Assert.Throws<InvalidDataException>(() => HomeSpecialistCatalogueProjection.Validate([Research, Research with { Name = "Lookalike" }]));
        Assert.Throws<InvalidDataException>(() => HomeSpecialistCatalogueProjection.Validate([Research with { Id = Guid.Empty }]));
    }

    [AvaloniaFact]
    public async Task Authored_catalogue_and_details_read_actual_definitions_without_starting_or_mutating_work()
    {
        var repository = new Catalogue([Research, Review]);
        var page = new HomeSpecialistsDestinationPage(repository, _ => Task.CompletedTask);
        var window = new Window { Content = page, Width = 1200, Height = 900 };
        try
        {
            window.Show(); await page.ActivateAsync(TestContext.Current.CancellationToken); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, page.GetVisualDescendants().OfType<Control>().Count(control => control.Name == "specialist-card"));
            var disabled = Assert.Single(page.VisibleRows, item => item.Definition.Id == Review.Id);
            var inspect = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "specialist-details" && AutomationProperties.GetName(button) == disabled.DetailsLabel);
            Assert.True(inspect.IsEnabled);
            inspect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Review.Id, page.SelectedSpecialistId);
            Assert.Equal(Review.Instructions, Find<TextBlock>(page, "specialist-instructions").Text);
            Assert.Equal(Review.PreferredModel, Find<TextBlock>(page, "specialist-preferred-model").Text);
            Assert.Equal(Review.FallbackModel, Find<TextBlock>(page, "specialist-fallback-model").Text);
            Assert.Equal(Review.DetectionRules, Find<TextBlock>(page, "specialist-inputs").Text);
            Assert.Equal(Review.Id.ToString("D"), Find<TextBlock>(page, "specialist-identity").Text);
            Assert.False(string.IsNullOrWhiteSpace(Find<TextBlock>(page, "specialist-updated").Text));
            Assert.False(Find<Button>(page, "specialists-create").IsEnabled);
            Assert.False(Find<Button>(page, "specialist-configure").IsEnabled);
            Assert.All(page.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "specialist-invite"), button => Assert.False(button.IsEnabled));
            Assert.Contains("unavailable", Assert.IsType<string>(ToolTip.GetTip(Find<Button>(page, "specialist-configure"))));
            Assert.Equal(0, repository.Mutations); Assert.Equal(0, repository.EnabledOnlyReads);
            Assert.Equal(2, repository.Reads);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Search_and_status_filters_preserve_an_inspected_identity_without_dispatching_work()
    {
        var repository = new Catalogue([Research, Review, Writing]);
        var page = new HomeSpecialistsDestinationPage(repository, _ => Task.CompletedTask);
        var window = new Window { Content = page, Width = 1200, Height = 900 };
        try
        {
            window.Show(); await page.ActivateAsync(TestContext.Current.CancellationToken);
            await page.DispatchAsync("specialists.select", HomeSpecialistCatalogueProjection.Inspect(Review).Target);
            var reads = repository.Reads;
            Assert.True(page.TrySetValue("specialists.query", "evidence"));
            Assert.Equal(Research.Id, Assert.Single(page.VisibleRows).Definition.Id);
            Assert.Equal(Review.Id, page.SelectedSpecialistId);
            Assert.Equal(reads, repository.Reads);
            await page.DispatchAsync("specialists.filter", "enabled");
            Assert.DoesNotContain(page.VisibleRows, row => row.Definition.Id == Review.Id);
            await page.DispatchAsync("specialists.clear-query", null);
            Assert.Equal(2, page.VisibleRows.Count);
            await page.DispatchAsync("specialists.filter", "disabled");
            Assert.Equal(Review.Id, Assert.Single(page.VisibleRows).Definition.Id);
            Assert.Equal(Review.Id, page.SelectedSpecialistId);
            Assert.Equal(0, repository.Mutations);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Older_catalogue_completion_cannot_replace_a_new_activation()
    {
        var old = new TaskCompletionSource<IReadOnlyList<AgentDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Catalogue([Review]) { Read = (count, _) => count == 1 ? old.Task : Task.FromResult<IReadOnlyList<AgentDefinition>>([Review]) };
        var page = new HomeSpecialistsDestinationPage(repository, _ => Task.CompletedTask);
        var window = new Window { Content = page, Width = 900, Height = 700 };
        try
        {
            window.Show(); var first = page.ActivateAsync(TestContext.Current.CancellationToken);
            Assert.False(first.IsCompleted); page.Deactivate();
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            Assert.Equal(Review.Id, Assert.Single(page.VisibleRows).Definition.Id);
            old.SetResult([Research]); await first;
            Assert.Equal(Review.Id, Assert.Single(page.VisibleRows).Definition.Id);
            Assert.Null(page.SelectedSpecialistId);
        }
        finally { old.TrySetResult([Research]); await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Older_inspection_cannot_replace_a_newer_selected_definition()
    {
        var old = new TaskCompletionSource<IReadOnlyList<AgentDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Catalogue([Research, Review]);
        var page = new HomeSpecialistsDestinationPage(repository, _ => Task.CompletedTask);
        var window = new Window { Content = page, Width = 900, Height = 700 };
        try
        {
            window.Show(); await page.ActivateAsync(TestContext.Current.CancellationToken);
            repository.Read = (count, _) => count == 2 ? old.Task : Task.FromResult<IReadOnlyList<AgentDefinition>>([Research, Review]);
            var first = page.DispatchAsync("specialists.select", HomeSpecialistCatalogueProjection.Inspect(Research).Target).AsTask();
            Assert.False(first.IsCompleted);
            await page.DispatchAsync("specialists.select", HomeSpecialistCatalogueProjection.Inspect(Review).Target);
            old.SetResult([Research, Review]); await first;
            Assert.Equal(Review.Id, page.SelectedSpecialistId);
            Assert.Equal(Review.Instructions, Find<TextBlock>(page, "specialist-instructions").Text);
        }
        finally { old.TrySetResult([Research, Review]); await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Close_joins_the_original_catalogue_read_and_never_publishes_its_late_rows()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<AgentDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Catalogue([]) { Read = (_, _) => pending.Task };
        var page = new HomeSpecialistsDestinationPage(repository, _ => Task.CompletedTask);
        var window = new Window { Content = page, Width = 900, Height = 700 };
        window.Show();
        var load = page.ActivateAsync(TestContext.Current.CancellationToken);
        var close = page.CloseAndDrainAsync();
        try
        {
            Assert.False(close.IsCompleted);
            pending.SetResult([Research]);
            await close;
            Assert.True(load.IsCompleted);
            Assert.Empty(page.VisibleRows); Assert.Null(page.SelectedSpecialistId); Assert.Null(page.Content);
        }
        finally
        {
            pending.TrySetResult([Research]);
            await close; window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Preflight_joins_the_original_read_and_cancelling_its_waiter_does_not_cancel_or_retire_the_page()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<AgentDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken readToken = default;
        var repository = new Catalogue([]) { Read = (_, token) => { readToken = token; return pending.Task; } };
        var page = new HomeSpecialistsDestinationPage(repository, _ => Task.CompletedTask);
        var window = new Window { Content = page, Width = 900, Height = 700 };
        using var waiter = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        try
        {
            window.Show();
            var load = page.ActivateAsync(TestContext.Current.CancellationToken);
            var first = page.TryPrepareToCloseAsync(waiter.Token);
            Assert.False(first.IsCompleted);
            waiter.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.True(page.IsAlive); Assert.False(readToken.IsCancellationRequested); Assert.False(load.IsCompleted);
            var second = page.TryPrepareToCloseAsync(TestContext.Current.CancellationToken);
            Assert.False(second.IsCompleted);
            pending.SetResult([Research]);
            await load;
            Assert.True(await second);
            Assert.Equal(Research.Id, Assert.Single(page.VisibleRows).Definition.Id);
            Assert.True(page.IsAlive); Assert.NotNull(page.Content);
        }
        finally
        {
            pending.TrySetResult([Research]);
            await page.CloseAndDrainAsync(); window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Revoked_admission_clears_previously_inspected_data_and_preserves_original_failure()
    {
        var allowed = true;
        var repository = new Catalogue([Research]);
        var page = new HomeSpecialistsDestinationPage(repository, _ => allowed ? Task.CompletedTask :
            Task.FromException(new UnauthorizedAccessException("Profile was revoked.")));
        var window = new Window { Content = page, Width = 900, Height = 700 };
        try
        {
            window.Show(); await page.ActivateAsync(TestContext.Current.CancellationToken);
            await page.DispatchAsync("specialists.select", HomeSpecialistCatalogueProjection.Inspect(Research).Target);
            allowed = false;
            await Assert.ThrowsAnyAsync<Exception>(() => page.DispatchAsync("specialists.refresh", null).AsTask());
            Assert.Empty(page.VisibleRows); Assert.Null(page.SelectedSpecialistId);
            Assert.False(Find<CuiContainerControl>(page, "specialist-inspector").IsVisible);
            Assert.False(page.TrySetValue("specialists.query", "research"));
            Assert.Equal(0, repository.Mutations);
            await Assert.ThrowsAnyAsync<Exception>(() => page.TryPrepareToCloseAsync(TestContext.Current.CancellationToken));
            Assert.True(page.IsAlive); Assert.NotNull(page.Content);
        }
        finally
        {
            await Assert.ThrowsAnyAsync<Exception>(() => page.CloseAndDrainAsync());
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Reentrant_deactivation_cannot_republish_private_inspection_fields()
    {
        var page = new HomeSpecialistsDestinationPage(new Catalogue([Research]), _ => Task.CompletedTask);
        var window = new Window { Content = page, Width = 900, Height = 700 };
        var deactivated = false;
        PropertyChangedEventHandler changed = (_, _) =>
        {
            if (!deactivated && page.SelectedSpecialistId == Research.Id)
            { deactivated = true; page.Deactivate(); }
        };
        try
        {
            window.Show(); await page.ActivateAsync(TestContext.Current.CancellationToken);
            ((INotifyPropertyChanged)page).PropertyChanged += changed;
            await page.DispatchAsync("specialists.select", HomeSpecialistCatalogueProjection.Inspect(Research).Target);
            Assert.True(deactivated); Assert.Empty(page.VisibleRows); Assert.Null(page.SelectedSpecialistId);
            Assert.True(page.TryGetValue("specialist.identity", out var identity)); Assert.Equal(string.Empty, identity);
            Assert.DoesNotContain(Research.Instructions, Find<TextBlock>(page, "specialist-instructions").Text ?? string.Empty);
            Assert.False(Find<CuiContainerControl>(page, "specialist-inspector").IsVisible);
        }
        finally
        {
            ((INotifyPropertyChanged)page).PropertyChanged -= changed;
            await page.CloseAndDrainAsync(); window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Configuration_uses_the_inspected_identity_and_refuses_a_changed_snapshot_before_queueing()
    {
        var repository = new Catalogue([Research]);
        var queued = new List<(HomeSpecialistConfigurationRequest Request, Task Settlement)>();
        var page = new HomeSpecialistsDestinationPage(repository, _ => Task.CompletedTask, configure: (request, settlement) =>
        { queued.Add((request, settlement)); Assert.False(settlement.IsCompleted); return true; });
        var window = new Window { Content = page, Width = 900, Height = 700 };
        try
        {
            window.Show(); await page.ActivateAsync(TestContext.Current.CancellationToken);
            await page.DispatchAsync("specialists.select", HomeSpecialistCatalogueProjection.Inspect(Research).Target);
            repository.Definitions = [Research with { Instructions = "Updated exact instructions", UpdatedAt = Research.UpdatedAt.AddMinutes(1) }];
            await page.DispatchAsync("specialists.configure", null);
            Assert.Empty(queued);
            Assert.Equal("Updated exact instructions", Find<TextBlock>(page, "specialist-instructions").Text);
            await page.DispatchAsync("specialists.configure", null);
            var target = Assert.Single(queued);
            await target.Settlement;
            Assert.Equal(Research.Id, target.Request.AgentId);
            Assert.Equal(repository.Definitions[0].UpdatedAt, target.Request.ExpectedUpdatedAt);
            Assert.Equal(HomeSpecialistCatalogueProjection.Fingerprint(repository.Definitions[0]), target.Request.ExpectedSnapshot);
            Assert.Equal(0, repository.Mutations);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Glow")]
    [InlineData("Professional")]
    public async Task Actual_specialist_catalogue_renders_at_desktop_and_narrow_widths(string name)
    {
        using var theme = new CuiThemePreferenceContext(new ThemeSource(name));
        var catalogue = new Catalogue([Research, Review, Writing]);
        var page = new HomeSpecialistsDestinationPage(catalogue, _ => Task.CompletedTask, theme);
        var window = new Window { Content = page, Width = 1440, Height = 900 };
        try
        {
            window.Show(); await page.ActivateAsync(TestContext.Current.CancellationToken);
            var reads = catalogue.Reads;
            Find<Button>(page, "specialists-refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync();
            Assert.Equal(reads + 1, catalogue.Reads);
            Assert.Equal(0, catalogue.Mutations);
            foreach (var size in new[] { new Size(1440, 900), new Size(720, 650), new Size(560, 700) })
            {
                window.Width = size.Width; window.Height = size.Height;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var body = Find<CuiContainerControl>(page, "specialists-body");
                var cards = page.GetVisualDescendants().OfType<CuiContainerControl>().Where(control => control.Name == "specialist-card").ToArray();
                Assert.Equal(3, cards.Length);
                Assert.True(body.Bounds.Width > 0);
                foreach (var card in cards)
                {
                    var point = card.TranslatePoint(default, body);
                    Assert.NotNull(point);
                    Assert.True(point.Value.X >= -1 && point.Value.X + card.Bounds.Width <= body.Bounds.Width + 1);
                    Assert.True(card.Bounds.Width >= 100);
                }
                Assert.True(page.TryGetValue("specialists.columns", out var columns));
                Assert.Equal(size.Width <= 650 ? "1fr" : size.Width <= 1000 ? "1fr 1fr" : "1fr 1fr 1fr", columns);
                var refresh = Find<Button>(page, "specialists-refresh");
                Assert.Equal("Refresh Specialists", AutomationProperties.GetName(refresh));
                var icon = Assert.Single(refresh.GetVisualDescendants().OfType<Image>());
                Assert.True(icon.IsEffectivelyVisible);
                Assert.Equal(new Size(16, 16), icon.Bounds.Size);
                var image = Assert.IsType<DrawingImage>(icon.Source);
                var drawing = Assert.IsType<GeometryDrawing>(image.Drawing);
                Assert.NotNull(drawing.Geometry);
                Assert.NotNull(drawing.Pen);
                Assert.InRange(drawing.Geometry.Bounds.Width, 16.4, 16.6);
                Assert.InRange(drawing.Geometry.Bounds.Height, 15.9, 16.1);
                Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(refresh.Foreground).Color,
                    Assert.IsAssignableFrom<ISolidColorBrush>(drawing.Pen.Brush).Color);
                if (Environment.GetEnvironmentVariable("CUI_EVIDENCE_DIRECTORY") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    using var capture = window.CaptureRenderedFrame(); Assert.NotNull(capture);
                    capture.Save(Path.Combine(directory, $"home-specialists-{name}-{size.Width:0}x{size.Height:0}.png"));
                }
            }
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    private static AgentDefinition Definition(int number, string name, string description, string detection, string permissions) =>
        new(Guid.Parse("d0000000-0000-0000-0000-" + number.ToString("D12")), name, description,
            "Inspect sources and show reasoning for " + name + ".", "agent-research", string.Empty, null,
            detection, permissions, false, true, new DateTimeOffset(2026, 11, 26, 20, 0, 0, TimeSpan.Zero));

    private static T Find<T>(Control root, string id) where T : Control =>
        Assert.Single(global::Haven.Desktop.Tests.AuthoredCuiControls.Enumerate(root).OfType<T>(), control => control.Name == id);

    private sealed class ThemeSource(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "specialist-render-test", true,
            CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class Catalogue(IReadOnlyList<AgentDefinition> definitions) : ICatalogRepository
    {
        internal IReadOnlyList<AgentDefinition> Definitions = definitions;
        internal Func<int, CancellationToken, Task<IReadOnlyList<AgentDefinition>>>? Read;
        internal int Reads, EnabledOnlyReads, Mutations;
        public Task<IReadOnlyList<AgentDefinition>> GetAllAgentsAsync(CancellationToken token)
        { var count = ++Reads; return Read?.Invoke(count, token) ?? Task.FromResult(Definitions); }
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken token)
        { ++EnabledOnlyReads; throw new InvalidOperationException("Home must retain disabled definitions."); }
        public Task<IReadOnlyList<PromptDefinition>> GetPromptsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task UpsertAgentAsync(AgentDefinition agent, CancellationToken token) => Mutate();
        public Task UpsertPromptAsync(PromptDefinition prompt, CancellationToken token) => Mutate();
        public Task SetAgentEnabledAsync(Guid id, bool enabled, CancellationToken token) => Mutate();
        public Task SetPromptEnabledAsync(Guid id, bool enabled, CancellationToken token) => Mutate();
        public Task DeleteCustomAgentAsync(Guid id, CancellationToken token) => Mutate();
        public Task DeleteCustomPromptAsync(Guid id, CancellationToken token) => Mutate();
        private Task Mutate() { ++Mutations; throw new InvalidOperationException("Browsing must not write the catalogue."); }
    }
}
