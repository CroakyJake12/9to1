using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Home;
using HavenOS.Home.Core;
using HavenOS.Home.Discover;
using DiscoverPage = Haven.Desktop.Views.Pages.Home.HomeDiscoverPage;

namespace Haven.Desktop.Tests;

public sealed class HomeModelBookmarkTests
{
    [Fact]
    public async Task Saved_models_use_profile_scoped_durable_CAS_and_preserve_concurrent_edits()
    {
        using var files = new BookmarkFiles();
        var store = new VersionedAtomicSettingsStore(files);
        await store.SetAsync("unrelated.owner", new { Value = "preserve" }, Token);
        var first = new HomeSavedModelPreferences(store, "profile-one", Admit);
        var second = new HomeSavedModelPreferences(new VersionedAtomicSettingsStore(files), "profile-one", Admit);
        var other = new HomeSavedModelPreferences(store, "profile-two", Admit);
        var before = await first.ReadAsync(Token);
        var concurrent = await second.ReadAsync(Token);
        var bookmark = Saved("model-one", "artifact-one");
        Assert.True((await first.SaveAsync(before, new(0, [bookmark]), Token)).Saved);
        var conflict = await second.SaveAsync(concurrent, new(0, [Saved("model-two")]), Token);
        Assert.False(conflict.Saved);
        Assert.Equal(bookmark, Assert.Single(conflict.Current.List.Models));
        Assert.Empty((await other.ReadAsync(Token)).List.Models);
        var reopened = new HomeSavedModelPreferences(new VersionedAtomicSettingsStore(files), "profile-one", Admit);
        var persisted = await reopened.ReadAsync(Token);
        Assert.Equal(bookmark, Assert.Single(persisted.List.Models));
        Assert.True((await reopened.SaveAsync(persisted, new(persisted.List.Revision, []), Token)).Saved);
        Assert.Empty((await first.ReadAsync(Token)).List.Models);
        Assert.Equal("preserve", (await store.GetAsync<JsonDocument>("unrelated.owner", Token))!.RootElement.GetProperty("Value").GetString());
    }

    [Fact]
    public async Task Saved_models_require_current_admission_at_commit_and_leave_other_settings_unchanged()
    {
        using var files = new BookmarkFiles();
        var store = new VersionedAtomicSettingsStore(files);
        var allowed = true;
        Task Current(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!allowed) throw new UnauthorizedAccessException("Original profile retired."); return Task.CompletedTask; }
        var preferences = new HomeSavedModelPreferences(store, "profile", Current);
        var initial = await preferences.ReadAsync(Token);
        var before = await store.ExportAsync(Token);
        allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preferences.SaveAsync(initial, new(0, [Saved("denied")]), Token));
        Assert.Equal(before.Settings, (await store.ExportAsync(Token)).Settings);
    }

    [Fact]
    public async Task Unknown_saved_model_schema_is_preserved_and_cannot_be_silently_replaced()
    {
        using var files = new BookmarkFiles();
        var store = new VersionedAtomicSettingsStore(files);
        var preferences = new HomeSavedModelPreferences(store, "profile", Admit);
        var first = await preferences.ReadAsync(Token);
        Assert.True((await preferences.SaveAsync(first, new(0, [Saved("kept")]), Token)).Saved);
        var export = await store.ExportAsync(Token);
        var key = Assert.Single(export.Settings.Keys, key => key.StartsWith("haven.models.saved.v1.", StringComparison.Ordinal));
        await store.SetAsync(key, new { SchemaVersion = 19, Revision = 1, Models = Array.Empty<HomeSavedModel>() }, Token);
        var before = (await store.ExportAsync(Token)).Settings[key];
        await Assert.ThrowsAsync<InvalidDataException>(() => preferences.ReadAsync(Token));
        Assert.Equal(before, (await store.ExportAsync(Token)).Settings[key]);
    }

    [Fact]
    public async Task Saved_models_reobserve_the_profile_after_a_held_store_read_before_returning_data()
    {
        using var files = new BookmarkFiles();
        var actual = new VersionedAtomicSettingsStore(files);
        var saved = new HomeSavedModelPreferences(actual, "original-profile", Admit);
        var before = await saved.ReadAsync(Token);
        Assert.True((await saved.SaveAsync(before, new(0, [Saved("private-model")]), Token)).Saved);
        var held = new HeldBookmarkStore(actual) { HoldRead = true };
        var allowed = true;
        Task Current(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!allowed) throw new UnauthorizedAccessException("Original profile retired during read."); return Task.CompletedTask; }
        var preferences = new HomeSavedModelPreferences(held, "original-profile", Current);
        var read = preferences.ReadAsync(Token);
        try
        {
            await held.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            allowed = false; held.ReadRelease.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => read);
        }
        finally { held.ReadRelease.TrySetResult(); }
        Assert.Equal("private-model", Assert.Single((await saved.ReadAsync(Token)).List.Models).Identity.ModelId);
    }

    [AvaloniaTheory]
    [InlineData("Glow", 1440, 900)]
    [InlineData("Professional", 1440, 900)]
    [InlineData("Glow", 720, 650)]
    [InlineData("Professional", 720, 650)]
    public async Task Authored_Save_Use_and_saved_shelf_keep_exact_identity_and_truthful_availability(string themeName, int width, int height)
    {
        using var files = new BookmarkFiles();
        var preferences = new HomeSavedModelPreferences(new VersionedAtomicSettingsStore(files), "profile", Admit);
        var source = new Catalogue();
        var target = source.Models[0];
        using var theme = new CuiThemePreferenceContext(new BookmarkTheme(themeName));
        var provider = new Routes(target.Identity);
        var controller = await OpenPickerAsync(provider);
        var selected = new List<HomeDiscoverIdentity>();
        using var page = new DiscoverPage(new HomeDiscoverCatalog([source]), null, null, null, theme: theme,
            useModel: async (identity, token) => { selected.Add(identity); return await HomeDiscoverModelSelection.UseAsync(controller, identity, token); });
        page.BindSavedModels(preferences);
        var window = new Window { Content = page, Width = width, Height = height };
        try
        {
            window.Show(); await page.ActivateAsync(Token); await LayoutAsync(window);
            var details = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => button.Name == "discover-model-details" &&
                AutomationProperties.GetName(button) == "Details for " + target.Name);
            details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await page.TryPrepareToCloseAsync(Token); await LayoutAsync(window);
            Assert.Equal("details", page.CurrentView);
            var save = Find<Button>(page, "discover-save-model");
            var use = Find<Button>(page, "discover-use-model");
            Assert.True(save.IsEffectivelyVisible && save.IsEffectivelyEnabled);
            Assert.True(use.IsEffectivelyVisible && use.IsEffectivelyEnabled);
            Assert.Equal("Use model", use.Content);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await page.TryPrepareToCloseAsync(Token); await LayoutAsync(window);
            Assert.Equal(target.Identity, Assert.Single((await preferences.ReadAsync(Token)).List.Models).Identity);
            Assert.Equal("Remove from saved", save.Content);
            Assert.Empty(selected); Assert.Null(provider.LastEdit); Assert.Equal(0, source.Executions);
            use.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await page.TryPrepareToCloseAsync(Token); await LayoutAsync(window);
            Assert.Equal(target.Identity, Assert.Single(selected)); Assert.NotNull(provider.LastEdit);
            var chosen = Assert.Single(provider.LastEdit.Route.Candidates, candidate => candidate.Enabled);
            Assert.Equal(target.Identity.ProviderId, chosen.ProviderId); Assert.Equal(target.Identity.ModelId, chosen.ModelId);
            Assert.Equal(target.Identity.ArtifactRevision, chosen.ArtifactRevision);
            Assert.Equal(524288, provider.LastEdit.Route.Policy.GetProperty("contextWindow").GetInt32());
            Assert.Equal(0, source.Executions);
            foreach (var button in new[] { save, use })
            {
                var point = button.TranslatePoint(default, window); Assert.NotNull(point);
                Assert.InRange(point.Value.X, 0, width);
                Assert.True(point.Value.X + button.Bounds.Width <= width + 1);
            }
            Capture(window, themeName, "bookmark-details", width, height);
            await page.DispatchAsync("models.mine", null, Token); await LayoutAsync(window);
            var saved = Find<Button>(page, "discover-saved-model-details");
            Assert.True(saved.IsEffectivelyVisible && saved.IsEffectivelyEnabled);
            saved.BringIntoView(); await LayoutAsync(window);
            Capture(window, themeName, "saved-models", width, height);
            source.Models = [];
            await page.ActivateAsync(Token); await LayoutAsync(window);
            saved = Find<Button>(page, "discover-saved-model-details");
            Assert.False(saved.IsEffectivelyEnabled);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "Not in the current catalogue");
            Find<Button>(page, "discover-saved-model-remove").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.TryPrepareToCloseAsync(Token);
            Assert.Empty((await preferences.ReadAsync(Token)).List.Models);
            Assert.Single(selected); Assert.Equal(0, source.Executions);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("models.save")]
    [InlineData("models.use")]
    public async Task Delayed_model_action_cannot_retarget_after_details_change_or_accept_a_duplicate(string command)
    {
        using var files = new BookmarkFiles();
        var preferences = new HomeSavedModelPreferences(new VersionedAtomicSettingsStore(files), "profile", Admit);
        var source = new Catalogue();
        TaskCompletionSource? hold = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Admission(CancellationToken token)
        {
            if (hold is not { } gate) return Admit(token);
            hold = null; entered.TrySetResult(); return gate.Task;
        }
        var uses = 0;
        using var page = new DiscoverPage(new HomeDiscoverCatalog([source]), null, null, null,
            useModel: (_, _) => { uses++; return Task.FromResult(new HomeDiscoverModelUseResult(true, "Saved", "Selected.")); });
        page.BindOriginalAdmission(Admission); page.BindSavedModels(preferences);
        var window = new Window { Content = page, Width = 1000, Height = 800 };
        TaskCompletionSource? release = null;
        try
        {
            window.Show(); await page.ActivateAsync(Token);
            await page.DispatchAsync("models.details", page.VisibleModels[0], Token);
            hold = release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var action = page.DispatchAsync(command, null, Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.False(action.IsCompleted); Assert.False(page.IsActionAvailable(command));
            await page.DispatchAsync(command, null, Token);
            var preparation = page.TryPrepareToCloseAsync(Token); Assert.False(preparation.IsCompleted);
            // Navigation remains available while the command's original Home admission is held.
            await page.DispatchAsync("models.details", page.VisibleModels[1], Token);
            release.TrySetResult(); await action; Assert.True(await preparation);
            Assert.Equal(0, uses); Assert.Empty((await preferences.ReadAsync(Token)).List.Models);
            Assert.Contains("inspected model changed", Find<TextBlock>(page, "discover-status").Text, StringComparison.Ordinal);
        }
        finally { release?.TrySetResult(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [Fact]
    public async Task Discover_Use_preserves_exact_revision_and_the_original_pending_review()
    {
        var identity = new HomeDiscoverIdentity("provider", "chosen", "artifact-exact");
        var provider = new Routes(identity) { RequireReview = true };
        var controller = await OpenPickerAsync(provider);
        var result = await HomeDiscoverModelSelection.UseAsync(controller, identity, Token);
        Assert.False(result.Applied); Assert.Equal("ApprovalPending", result.Code);
        Assert.Equal("review-original", controller.Surface.State.PendingApprovalRequestId);
        Assert.Null(provider.LastEdit);
        var wrongRevision = await HomeDiscoverModelSelection.UseAsync(controller, identity with { ArtifactRevision = "other-artifact" }, Token);
        Assert.False(wrongRevision.Applied); Assert.Equal("CatalogueEntryUnavailable", wrongRevision.Code);
        Assert.Equal("review-original", controller.Surface.State.PendingApprovalRequestId);
        Assert.Null(provider.LastEdit);
    }

    [Fact]
    public async Task Discover_Use_never_drops_an_artifact_pin_to_choose_a_current_alias()
    {
        var identity = new HomeDiscoverIdentity("provider", "chosen", "artifact-exact");
        var provider = new Routes(identity with { ArtifactRevision = null });
        var controller = await OpenPickerAsync(provider);
        var result = await HomeDiscoverModelSelection.UseAsync(controller, identity, Token);
        Assert.False(result.Applied); Assert.Equal("CatalogueEntryUnavailable", result.Code);
        Assert.Null(provider.LastEdit);
        Assert.Equal("previous", Assert.Single(provider.Snapshot.Routes[0].Candidates).ModelId);
    }

    [AvaloniaFact]
    public async Task Saved_model_close_joins_the_actual_held_store_and_keeps_its_late_failure()
    {
        using var files = new BookmarkFiles();
        var actual = new VersionedAtomicSettingsStore(files);
        var held = new HeldBookmarkStore(actual);
        var preferences = new HomeSavedModelPreferences(held, "profile", Admit);
        using var page = new DiscoverPage(new HomeDiscoverCatalog([new Catalogue()]));
        page.BindSavedModels(preferences);
        var window = new Window { Content = page, Width = 1000, Height = 800 };
        try
        {
            window.Show(); await page.ActivateAsync(Token);
            await page.DispatchAsync("models.details", page.VisibleModels[0], Token);
            var action = page.DispatchAsync("models.save", null, Token).AsTask();
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.OriginalClose); Assert.False(close.IsCompleted); Assert.False(action.IsCompleted);
            Assert.True(held.OriginalToken.IsCancellationRequested);
            held.Release.TrySetResult();
            var actionFailure = await Assert.ThrowsAnyAsync<Exception>(() => action);
            Assert.True(ContainsFailure(actionFailure, held.Failure));
            var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(ContainsFailure(closeFailure, held.Failure));
            Assert.DoesNotContain((await actual.ExportAsync(Token)).Settings.Keys,
                key => key.StartsWith("haven.models.saved.v1.", StringComparison.Ordinal));
        }
        finally
        {
            held.Release.TrySetResult();
            var failure = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            if (failure is not null) Assert.True(ContainsFailure(failure, held.Failure));
            window.Content = null; window.Close();
        }
    }

    // Dispatcher pumping may restore another test's expired ambient context. Retain this
    // original test token for its entire fixture, including real action/close settlement.
    private readonly CancellationToken _testToken = TestContext.Current.CancellationToken;
    private CancellationToken Token => _testToken;
    private static bool ContainsFailure(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException aggregate && aggregate.InnerExceptions.Any(error => ContainsFailure(error, expected));
    private static Task Admit(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    private static HomeSavedModel Saved(string model, string? artifact = null) => new(new("provider", model, artifact), model, DateTimeOffset.UtcNow);
    private static T Find<T>(Control root, string name) where T : Control => Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.Name == name);
    private static async Task LayoutAsync(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        { Dispatcher.UIThread.RunJobs(); await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render); }
    }
    private static void Capture(Window window, string theme, string view, int width, int height)
    {
        var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"models-{view}-{theme.ToLowerInvariant()}-{width}x{height}.png"));
    }
    private async Task<HomeModelPickerCuiController> OpenPickerAsync(Routes provider)
    {
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
        var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(provider), surface);
        Assert.True((await controller.OpenAsync("User", "chat", Token)).Succeeded);
        return controller;
    }
    private sealed class Routes(HomeDiscoverIdentity identity) : IHomeModelPickerFeatureProvider
    {
        internal HomeModelPickerSnapshot Snapshot = new(3, "User", "chat", [new("chat.active", 3, "User", "chat", null, null,
            [new("provider", "previous", null, true, 0)], JsonSerializer.SerializeToElement(new { contextWindow = 524288 }))]);
        internal HomeModelRouteEdit? LastEdit;
        internal bool RequireReview;
        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelCataloguePage>(true, "Ready", "Observed catalogue.", new([
                new(identity.ProviderId, identity.ModelId, identity.ArtifactRevision, "Chosen model", "Provider", true,
                    new HashSet<string> { "Text" }, 524288, "Ready", "Local", null)], 0, 20, false, 1)));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Ready", "Current route.", Snapshot));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken cancellationToken = default)
        {
            if (RequireReview) return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "ApprovalPending",
                "Review this model choice in Home.", Snapshot with { PendingApprovalRequestId = "review-original" }));
            if (edit.ExpectedRevision != Snapshot.Revision) return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "Conflict", "Route changed."));
            LastEdit = edit; Snapshot = new(edit.Route.Version, "User", "chat", [edit.Route]);
            return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Saved", "Model route saved.", Snapshot));
        }
        public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Catalogue : IHomeDiscoverCatalogueSource
    {
        public string ProviderId => "provider";
        internal HomeDiscoverModel[] Models = [Model("chosen", "artifact-exact"), Model("another", null)];
        internal int Executions;
        public Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(new HomeDiscoverProviderSnapshot(
            ProviderId, "Provider", HomeDiscoverConnectionState.Connected, HomeDiscoverSourceState.Available, "observed", Models));
        public Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request, CancellationToken cancellationToken)
        { Executions++; throw new InvalidOperationException("Discover cannot install or route a model directly."); }
        private static HomeDiscoverModel Model(string id, string? artifact) => new(new("provider", id, artifact), id == "chosen" ? "Chosen model" : "Another model", null,
            new HashSet<HomeDiscoverCategory> { HomeDiscoverCategory.Chat }, new HashSet<string> { "Text" }, null, null, 524288, null,
            HomeDiscoverLocality.Local, HomeDiscoverInstallState.Installed, HomeDiscoverConnectionState.Connected,
            null, null, null, null, null, null, new HashSet<HomeDiscoverAction>());
    }
    private sealed class BookmarkTheme(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "bookmark-fixture", true, CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }
    private sealed class HeldBookmarkStore(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore, IVersionedSettingsGuardedCompareExchange
    {
        internal bool HoldRead;
        internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReadRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IOException Failure { get; } = new("Original saved-model store failed after caller retirement.");
        internal CancellationToken OriginalToken;
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => actual.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
        public async Task<SettingsExportManifest> ExportAsync(CancellationToken token)
        {
            var value = await actual.ExportAsync(token);
            if (HoldRead) { ReadEntered.TrySetResult(); await ReadRelease.Task; }
            return value;
        }
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => actual.ImportAsync(manifest, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token) =>
            actual.CompareExchangeAsync(key, expected, replacement, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, CancellationToken token) =>
            actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            OriginalToken = token; Entered.TrySetResult();
            await Release.Task; // Deliberately ignore cancellation; ownership must follow this actual task.
            throw Failure;
        }
    }
    private sealed class BookmarkFiles : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-model-bookmarks-" + Guid.NewGuid().ToString("N"));
        internal BookmarkFiles() => Directory.CreateDirectory(DataDirectory);
        public string DatabasePath => Path.Combine(DataDirectory, "state.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
