using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Haven.Infrastructure;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using HavenOS.Home.Core;
using HavenOS.Files.NativeHost;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;
using HavenOS.Home.Discover;
using HavenOS.Home;
using HomeDiscoverPage = Haven.Desktop.Views.Pages.Home.HomeDiscoverPage;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    private static BrowseAppContentRequest Request(string address = "browse://home") =>
        new(Guid.NewGuid(), AppContentAddress.Parse(address), "original-home-profile", BrowserTabPrivacy.Standard);

    private static UnifiedHomeContentFactory Factory(Registry source, CancellationToken lifetime,
        Func<BrowseNavigationIntent, Task, bool>? navigation = null,
        Func<BrowseAppContentRequest, CancellationToken, Task>? admission = null, bool aiAvailable = true,
        Func<NewDashboardPage>? dashboard = null, Func<HomeDiscoverPage>? discover = null,
        Func<BrowseAppContentRequest, HomeEventsDestinationPage>? events = null,
        Func<BrowseAppContentRequest, HomeSettingsDestinationPage>? settings = null,
        Func<BrowseAppContentRequest, string, CancellationToken, Task<AppContentAddress>>? prepareAiDraft = null,
        Func<BrowseAppContentRequest, NewTabLayoutPreferences>? newTabLayout = null) => new(source,
            dashboard ?? (() => throw new InvalidOperationException("Apps must not construct an unused dashboard.")),
            discover ?? (() => throw new InvalidOperationException("Apps must not construct an unused model catalogue.")),
            navigation ?? ((_, _) => true),
            prepareAiDraft ?? ((_, _, _) => throw new InvalidOperationException("Search must not implicitly invoke AI.")),
            admission ?? ((_, _) => Task.CompletedTask), () => true, lifetime, aiAvailable, events, settings, newTabLayout: newTabLayout);

    // Merge inside the existing UnifiedHomeContentTests class. This is a component
    // control over the real Home/source-authority seam and real Browse retirement;
    // it is not an installed/native App-host or Windows compatibility acceptance.
    public static bool LinuxHomeAppearanceAuthoritySupported => OperatingSystem.IsLinux();

    [AvaloniaFact(SkipUnless = nameof(LinuxHomeAppearanceAuthoritySupported),
        Skip = "Linux component owner fixture; installed Windows/native acceptance is separate.")]
    public async Task Source_owned_appearance_refresh_outlives_retired_Browse_chrome_while_actual_App_and_window_tokens_remain_live()
    {
        await using var rig = await HomeAppearanceAuthorityRig.CreateAsync();
        BrowseChrome? chrome = null;
        try
        {
            chrome = await BrowseChrome.CreateAsync(rig.Paths,
                internalContentFactory: Factory(new(), rig.AppLifetime.Token,
                    admission: async (request, token) =>
                    {
                        if (request.ProfileId != rig.Actor.ProfileId || !await rig.ObserveAsync(token))
                            throw new UnauthorizedAccessException("The actual Home/source authority refused this fixture tab.");
                    }),
                homeProfileId: rig.Actor.ProfileId, cancellationToken: rig.AppLifetime.Token);
            var before = await File.ReadAllBytesAsync(rig.PreferencePath);
            rig.Principal.HoldNext = true;
            var refresh = rig.Source.RefreshAsync();
            await rig.Principal.Entered.Task.WaitAsync(HomeAppearanceAuthorityRig.Limit);
            var actualPrincipal = Assert.IsAssignableFrom<Task<string?>>(rig.Principal.HeldOriginal);
            Assert.False(actualPrincipal.IsCompleted);
            Assert.False(refresh.IsCompleted);

            var preparation = await chrome.PrepareCloseAsync(rig.AppLifetime.Token);
            Assert.NotNull(preparation);
            var actualChromeClose = preparation!.BeginClose();
            await actualChromeClose.WaitAsync(HomeAppearanceAuthorityRig.Limit);
            Assert.NotNull(chrome.OriginalClose);
            Assert.True(actualChromeClose.IsCompletedSuccessfully);
            Assert.False(rig.AppLifetime.IsCancellationRequested);
            Assert.False(rig.WindowLifetime.IsCancellationRequested);
            Assert.Null(rig.Source.OriginalClose);
            Assert.Null(rig.Home.OriginalCloseTask);
            Assert.False(refresh.IsCompleted); // Closing Chrome cannot manufacture source settlement.

            rig.Principal.Release.TrySetResult();
            await refresh.WaitAsync(HomeAppearanceAuthorityRig.Limit);
            Assert.True(actualPrincipal.IsCompletedSuccessfully);
            Assert.True(refresh.IsCompletedSuccessfully);
            Assert.Equal("Professional", rig.Source.Current.Name);
            Assert.Equal("device:" + rig.Actor.ProfileId, rig.Source.Current.ScopeId);
            Assert.Equal(before, await File.ReadAllBytesAsync(rig.PreferencePath));
            Assert.Empty(rig.Source.AuthorityObservations);
        }
        finally
        {
            rig.Principal.Release.TrySetResult();
            if (chrome is not null)
            {
                if (chrome.OriginalClose is { } accepted)
                    await accepted.WaitAsync(HomeAppearanceAuthorityRig.Limit);
                else
                {
                    var preparation = await chrome.PrepareCloseAsync(rig.AppLifetime.Token);
                    Assert.NotNull(preparation);
                    await preparation!.BeginClose().WaitAsync(HomeAppearanceAuthorityRig.Limit);
                }
            }
        }
    }

    // Implementation regression for source-owned cancellation across actual Home reads.
    // AUTHORED NOT RUN: the source must keep its exact private token through normal close.
    [AvaloniaFact(SkipUnless = nameof(LinuxHomeAppearanceAuthoritySupported),
        Skip = "Linux component owner fixture; installed Windows/native acceptance is separate.")]
    public async Task Source_close_joins_its_held_actual_authority_and_accepts_only_its_same_private_owner_cancellation()
    {
        await using var rig = await HomeAppearanceAuthorityRig.CreateAsync();
        rig.Principal.HoldNext = true;
        var refresh = rig.Source.RefreshAsync();
        await rig.Principal.Entered.Task.WaitAsync(HomeAppearanceAuthorityRig.Limit);
        var rawPrincipal = Assert.IsAssignableFrom<Task<string?>>(rig.Principal.HeldOriginal);
        var close = rig.Source.DisposeAsync().AsTask();
        Assert.Same(close, rig.Source.DisposeAsync().AsTask());
        Assert.False(close.IsCompleted);
        Assert.False(rawPrincipal.IsCompleted);
        rig.Principal.Release.TrySetResult();
        await close.WaitAsync(HomeAppearanceAuthorityRig.Limit);
        Assert.True(rawPrincipal.IsCompleted);
        Assert.True(refresh.IsCanceled);
        var canceled = await Record.ExceptionAsync(() => refresh);
        var observation = Assert.Single(rig.Source.AuthorityObservations);
        Assert.Equal(observation.RequestedToken,
            Assert.IsAssignableFrom<OperationCanceledException>(canceled).CancellationToken);
        Assert.True(Assert.IsAssignableFrom<Task<bool>>(rig.LastAuthority).IsCanceled);
        Assert.True(close.IsCompletedSuccessfully);
        Assert.Null(rig.Home.OriginalCloseTask); // The source never owns the borrowed Home.
    }

    [AvaloniaTheory(SkipUnless = nameof(LinuxHomeAppearanceAuthoritySupported),
        Skip = "Linux component owner fixture; installed Windows/native acceptance is separate.")]
    [InlineData("full-actor")]
    [InlineData("required-service")]
    public async Task Source_owned_appearance_authority_still_refuses_a_foreign_full_actor_or_unready_actual_Home_service(string refusal)
    {
        await using var rig = await HomeAppearanceAuthorityRig.CreateAsync();
        var before = await File.ReadAllBytesAsync(rig.PreferencePath);
        var previous = rig.Source.Current;
        try
        {
            if (refusal == "full-actor")
                rig.ExpectedActor = rig.Actor with { AuthenticationRevision = rig.Actor.AuthenticationRevision + ":foreign" };
            else
                rig.Home.Runtime.ReportServiceState("permissions.trust", HomeServiceLifecycleState.Unavailable, false,
                    "Controlled actual Home service refusal");
            var refresh = rig.Source.RefreshAsync();
            var failure = await Record.ExceptionAsync(async () => await refresh.WaitAsync(HomeAppearanceAuthorityRig.Limit));
            Assert.NotNull(failure);
            rig.ExpectFailedSource(failure!);
            Assert.True(refresh.IsFaulted);
            Assert.True(HomeAppearanceAuthorityRig.ContainsCause<UnauthorizedAccessException>(failure!));
            Assert.Equal(previous, rig.Source.Current);
            Assert.Equal(before, await File.ReadAllBytesAsync(rig.PreferencePath));
            Assert.Null(rig.Home.OriginalCloseTask);
        }
        finally
        {
            rig.ExpectedActor = rig.Actor;
            if (refusal == "required-service")
                rig.Home.Runtime.ReportServiceState("permissions.trust", HomeServiceLifecycleState.Ready, true);
        }
    }

    [AvaloniaFact(SkipUnless = nameof(LinuxHomeAppearanceAuthoritySupported),
        Skip = "Linux component owner fixture; installed Windows/native acceptance is separate.")]
    public async Task Source_owned_appearance_authority_retains_the_actual_faulted_principal_Task_and_foreign_OCE_cause()
    {
        await using var rig = await HomeAppearanceAuthorityRig.CreateAsync();
        var before = await File.ReadAllBytesAsync(rig.PreferencePath);
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var originalCause = new OperationCanceledException("A real principal source FAULTED; this is not native owner retirement.",
            foreign.Token);
        var originalPrincipal = Task.FromException<string?>(originalCause);
        rig.Principal.NextOriginal = originalPrincipal;
        var refresh = rig.Source.RefreshAsync();
        var failure = await Record.ExceptionAsync(async () => await refresh.WaitAsync(HomeAppearanceAuthorityRig.Limit));
        Assert.NotNull(failure);
        rig.ExpectFailedSource(failure!);
        Assert.Same(originalPrincipal, rig.Principal.LastOriginal);
        Assert.True(originalPrincipal.IsFaulted);
        Assert.False(originalPrincipal.IsCanceled);
        Assert.True(refresh.IsFaulted);
        Assert.False(refresh.IsCanceled);
        Assert.True(HomeAppearanceAuthorityRig.HasExactCause(failure!, originalCause));
        var actualAuthority = Assert.IsAssignableFrom<Task<bool>>(rig.LastAuthority);
        Assert.True(actualAuthority.IsFaulted);
        var retained = Assert.Single(rig.Source.AuthorityObservations);
        Assert.Same(actualAuthority, retained.Source);
        Assert.True(HomeAppearanceAuthorityRig.HasExactCause(retained.Cause, originalCause));
        Assert.Equal(foreign.Token, originalCause.CancellationToken);
        Assert.False(rig.AppLifetime.IsCancellationRequested);
        Assert.False(rig.WindowLifetime.IsCancellationRequested);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.PreferencePath));
        Assert.Null(rig.Source.OriginalClose);
    }

    private sealed class HomeAppearanceAuthorityPrincipal : ITrustedHostPrincipalSource
    {
        private readonly OperatingSystemPrincipalSource _actual = new();
        internal bool HoldNext;
        internal Task<string?>? HeldOriginal, LastOriginal, NextOriginal;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            if (NextOriginal is { } next)
            {
                NextOriginal = null;
                return new(LastOriginal = next); // Deliberate fault injection, never an alternate identity grant.
            }
            var original = _actual.GetPrincipalAsync(token).AsTask();
            if (HoldNext)
            {
                HoldNext = false;
                var held = HoldOriginalAsync(original, token);
                LastOriginal = HeldOriginal = held;
                Entered.TrySetResult();
                return new(held);
            }
            return new(LastOriginal = original);
        }

        private async Task<string?> HoldOriginalAsync(Task<string?> original, CancellationToken token)
        {
            var principal = await original.ConfigureAwait(false);
            await Release.Task.ConfigureAwait(false); // Raw source stays held until its actual acknowledgement.
            token.ThrowIfCancellationRequested();
            return principal;
        }
    }

    private sealed class HomeAppearanceAuthorityRig : IAsyncDisposable
    {
        internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
        internal readonly LayoutPaths Paths = new();
        internal readonly HomeAppearanceAuthorityPrincipal Principal = new();
        internal readonly CancellationTokenSource AppLifetime = new(), WindowLifetime = new();
        internal readonly HomeNativeWindowsComposition Home;
        internal readonly Microsoft.Extensions.DependencyInjection.ServiceProvider Provider;
        internal AuthenticatedResourceActor Actor = null!, ExpectedActor = null!;
        internal NativeHomeThemePreferences Source = null!;
        internal Task<bool>? LastAuthority;
        internal string PreferencePath => Path.Combine(Paths.DataDirectory, "preferences.json");
        private readonly HashSet<Exception> _expectedFailures = new(ReferenceEqualityComparer.Instance);

        private HomeAppearanceAuthorityRig(bool includeFiles)
        {
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Paths.DataDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            NativeFilesWorkspaceService? files = null;
            NativeFilesWorkspaceAuthority? authority = null;
            FilesArtifactResourceResolver? resolver = null;
            Home = new(new FileHomeCoreStateStore(Path.Combine(Paths.DataDirectory, "home-appearance-state.json")),
                Principal, Paths, new("9to1.home.appearance.fixture." + Guid.NewGuid().ToString("N")),
                configureOriginalStores: includeFiles ? identity =>
                {
                    files = new(identity.StateStore, identity.Profiles);
                    return new([files], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = files });
                } : null,
                configureOriginalResolvers: includeFiles ? ownership =>
                {
                    authority = new(files!, ownership.Identity.Profiles, ownership.Ownership);
                    resolver = new(authority);
                    return new([resolver], new Dictionary<Type, object>
                    {
                        [typeof(NativeFilesWorkspaceAuthority)] = authority,
                        [typeof(FilesArtifactResourceResolver)] = resolver,
                    });
                } : null);
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            // Preserve the real uninstantiated production descriptor checks, including the
            // Linux secret-store wiring, then bind the SAME protected local Home tuple.
            services.AddHavenInfrastructure();
            services.AddHavenOwnedWindowsHomeDomain(Home, Paths, Principal);
            if (includeFiles)
            {
                services.AddSingleton(files!); services.AddSingleton(authority!); services.AddSingleton(resolver!);
                services.AddFilesNativeHost();
            }
            Provider = services.BuildServiceProvider();
        }

        internal static async Task<HomeAppearanceAuthorityRig> CreateAsync(bool includeFiles = false)
        {
            var rig = new HomeAppearanceAuthorityRig(includeFiles);
            try
            {
                await File.WriteAllTextAsync(rig.PreferencePath,
                    "{\"havenUiThemeName\":\"Professional\",\"defaultModel\":\"preserved-model\",\"futureSetting\":{\"keep\":true}}");
                // This is the genuine isolated Home process startup. It observes package/broker
                // health but has no admitted generation capability and sends no model request.
                await rig.Home.StartOriginalAsync().WaitAsync(Limit);
                rig.Actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None)
                    ?? throw new InvalidOperationException("The original operating-system Home profile is unavailable.");
                rig.ExpectedActor = rig.Actor;
                rig.Source = new(rig.PreferencePath, new(HomeDataScope.DeviceLocal, rig.Actor.ProfileId),
                    UserPreferencesService.ProjectLegacyHomeTheme,
                    token => new ValueTask<bool>(rig.ObserveAsync(token)), callback => Dispatcher.UIThread.Post(callback));
                await rig.Source.RefreshAsync().WaitAsync(Limit);
                Assert.False(rig.Home.InstalledPeerAdmissionConfigured);
                Assert.Null(rig.Actor.AccountId);
                Assert.Null(rig.Actor.OrganisationId);
                return rig;
            }
            catch (Exception primary)
            {
                try { await rig.DisposeAsync(); }
                catch (Exception cleanup) { throw new AggregateException("Actual Home appearance fixture startup and cleanup failed.", primary, cleanup); }
                throw;
            }
        }

        internal Task<bool> ObserveAsync(CancellationToken token)
        {
            var actual = App.ObserveOriginalHomeAppearanceAuthorityAsync(Provider, Home, ExpectedActor,
                AppLifetime.Token, WindowLifetime.Token, body => body(), token);
            Interlocked.Exchange(ref LastAuthority, actual);
            return actual;
        }

        internal void ExpectFailedSource(Exception error)
        {
            foreach (var cause in Leaves(error)) _expectedFailures.Add(cause);
        }

        internal static bool ContainsCause<T>(Exception error) where T : Exception =>
            error is T || error is AggregateException group && group.InnerExceptions.Any(ContainsCause<T>) ||
            error.InnerException is { } inner && ContainsCause<T>(inner);

        internal static bool HasExactCause(Exception error, Exception expected) =>
            ReferenceEquals(error, expected) || error is AggregateException group && group.InnerExceptions.Any(inner => HasExactCause(inner, expected)) ||
            error.InnerException is { } inner && HasExactCause(inner, expected);

        private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group
            ? group.InnerExceptions.SelectMany(Leaves)
            : new[] { error };

        public async ValueTask DisposeAsync()
        {
            Principal.NextOriginal = null;
            Principal.HoldNext = false;
            Principal.Release.TrySetResult();
            var failures = new List<Exception>();
            async Task<bool> Join(Task actual)
            {
                try { await actual.WaitAsync(Limit); }
                catch (Exception caught)
                {
                    foreach (var cause in Leaves(actual.Exception ?? caught))
                        if (!_expectedFailures.Contains(cause)) failures.Add(cause);
                }
                return actual.IsCompleted;
            }
            var sourceSettled = true;
            if (Source is not null)
            {
                try { sourceSettled = await Join(Source.DisposeAsync().AsTask()); }
                catch (Exception error) { failures.Add(error); sourceSettled = false; }
            }
            // A timeout never permits closing the Home/provider dependency while an
            // actual appearance original still borrows it. Leave that custody intact.
            var homeClosed = false;
            if (sourceSettled && failures.Count == 0)
                try
                {
                    var homeClose = Home.CloseAndDrainAsync();
                    await Join(homeClose);
                    homeClosed = homeClose.IsCompletedSuccessfully;
                }
                catch (Exception error) { failures.Add(error); }
            if (homeClosed)
                try { await Join(Provider.DisposeAsync().AsTask()); } catch (Exception error) { failures.Add(error); }
            // Unexpected failures retain their actual fixture files for the owning runner.
            if (failures.Count != 0) throw new AggregateException("Original Home appearance fixture cleanup failed.", failures);
            AppLifetime.Dispose(); WindowLifetime.Dispose();
            Directory.Delete(Paths.DataDirectory, true);
        }
    }

    // Actual canonical CUI/native preference file/factory routes. The explicit admission
    // callbacks in these component cases are controlled; actual Home authority has separate cases.
    [AvaloniaFact]
    public async Task Route_free_Home_session_constructed_off_UI_initializes_its_stable_view_on_UI()
    {
        var testToken = TestContext.Current.CancellationToken;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        var factory = Factory(new(), lifetime.Token); var request = Request();
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(await Task.Run(
            () => factory.CreateSession(request), testToken));
        try
        {
        await session.InitializeAsync(testToken);
        var view = Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView);
        Assert.IsType<UnifiedHomePage>(view.OriginalContent);
        Assert.Same(view, session.OriginalNativeView);
        Assert.True(await session.TryPrepareToCloseAsync(testToken));
        }
        finally { await session.CloseAndDrainAsync(); }
        Assert.True(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Guard_refusal_and_close_veto_preserve_pending_original_tab_choice()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token).CreateSession(Request()));
        await session.InitializeAsync(TestContext.Current.CancellationToken);
        var host = Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView);
        var picker = new TextBlock();
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = host.PresentAsync(picker, token => decision.Task.WaitAsync(token), lifetime.Token);
        using (CloudflareOriginalExecutionGuard.EnterOriginal(session))
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.TryPrepareToCloseAsync(TestContext.Current.CancellationToken));
        Assert.False(pending.IsCompleted); Assert.Same(picker, host.VisibleContent);
        Assert.False(await session.TryPrepareToCloseAsync(TestContext.Current.CancellationToken));
        Assert.False(pending.IsCompleted); Assert.Same(picker, host.VisibleContent);
        decision.SetResult(true); Assert.True(await pending);
        Assert.True(await session.TryPrepareToCloseAsync(TestContext.Current.CancellationToken));
        await session.CloseAndDrainAsync(); Assert.True(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Settings_rail_saves_through_the_same_canonical_appearance_source_and_retains_its_page()
    {
        var testToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "home-settings-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\",\"havenUiAppearance\":2,\"overrideAccentColour\":true,\"accentColourName\":\"Blue\",\"defaultModel\":\"retained-model\"}");
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "original-home-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        using var lifetime = new CancellationTokenSource();
        HomeSettingsDestinationPage? settings = null;
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            settings: _ => settings = new(source, _ => Task.CompletedTask)).CreateSession(Request()));
        Window? window = null;
        try
        {
            await session.InitializeAsync();
            var publishedTitles = new List<string>();
            session.StateChanged += (_, _) => publishedTitles.Add(session.Title);
            Assert.Equal("Home", session.Title);
            var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView).OriginalContent);
            window = Show(session.OriginalNativeView);
            var gear = HomeRailButton(page, "home-settings", "Settings");
            ClickSettingsButton(window, gear);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            Assert.NotNull(settings);
            Assert.Same(settings, Assert.Single(page.GetVisualDescendants().OfType<HomeSettingsDestinationPage>()));
            Assert.Equal("Theme and accent preferences", session.Title);
            Assert.Equal("browse://home", session.Address.Value);
            var theme = SettingsControl<Button>(settings!, "home-settings-theme");
            var appearance = SettingsControl<Button>(settings, "home-settings-appearance");
            var fragment = SettingsControl<CuiContainerControl>(settings, "home-appearance-settings");
            Assert.True(fragment.IsEffectivelyVisible);
            Assert.True(theme.IsFocused);
            Assert.True(SettingsCategorySelected(theme));
            Assert.False(SettingsCategorySelected(appearance));
            var cards = fragment.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "home-theme-option").ToArray();
            Assert.Equal(new[] { 5, 4 }, cards.GroupBy(button => Math.Round(button.TranslatePoint(default, window)!.Value.Y)).Select(row => row.Count()));
            Assert.All(cards, card => Assert.True(card.Bounds.Height >= 136));
            Assert.Contains(cards.SelectMany(card => card.GetVisualDescendants().OfType<TextBlock>()), label => label.Text == "Amber Console");
            CaptureSettingsIfRequested(window, "appearance", source);

            AppearanceOption(settings, "Glow").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            Assert.True(settings.LastWrite!.ConfirmedWrite!.Succeeded);
            Assert.Equal(1, settings.LastWrite.ConfirmedWrite.Value!.Preference.Revision);
            var saved = HomeNativePreferenceFile.Read(file);
            Assert.Equal("retained-model", saved["defaultModel"]!.GetValue<string>());
            Assert.Equal("Glow", HomeNativePreferenceFile.Resolve(saved,
                new(HomeDataScope.DeviceLocal, "original-home-profile"), UserPreferencesService.ProjectLegacyHomeTheme).ThemeName);
            Assert.Single(Directory.EnumerateFiles(root, "*.json"));

            var savedBytes = await File.ReadAllBytesAsync(file, testToken);
            var brightness = SettingsControl<Slider>(settings, "home-brightness");
            var savedBrightness = brightness.Value;
            Assert.True(brightness.Focus());
            PressSettingsKey(window, PhysicalKey.ArrowRight);
            var draftBrightness = brightness.Value;
            Assert.True(draftBrightness > savedBrightness);

            var layout = SettingsControl<Button>(settings, "home-settings-layout");
            var searchSettings = SettingsControl<TextBox>(settings, "home-settings-search");
            Assert.True(searchSettings.Focus());
            searchSettings.Text = "layout";
            SettleFixtureLayout(window);
            Assert.True(layout.IsEffectivelyVisible && appearance.IsEffectivelyVisible);
            Assert.False(theme.IsEffectivelyVisible && theme.IsAttachedToVisualTree());
            PressSettingsKey(window, PhysicalKey.Enter);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            Assert.True(SettingsCategorySelected(layout));
            Assert.Equal("Layout", session.Title);
            Assert.False(SettingsCategorySelected(appearance));
            Assert.True(SettingsControl<Button>(settings, "home-settings-open-dashboard").IsEffectivelyVisible);
            Assert.True(SettingsControl<Button>(settings, "home-settings-reload-appearance").IsEffectivelyVisible);
            Assert.True(layout.IsFocused);
            PressSettingsKey(window, PhysicalKey.Escape);
            SettleFixtureLayout(window);
            Assert.True(searchSettings.IsFocused);
            Assert.True(string.IsNullOrEmpty(searchSettings.Text));
            theme = SettingsControl<Button>(settings, "home-settings-theme");
            Assert.True(theme.IsEffectivelyVisible);
            CaptureSettingsIfRequested(window, "layout", source);

            ClickSettingsButton(window, appearance);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            Assert.True(SettingsCategorySelected(appearance));
            Assert.Equal("Appearance", session.Title);
            Assert.True(SettingsControl<CuiContainerControl>(settings, "home-settings-appearance-overview").IsEffectivelyVisible);
            ClickSettingsButton(window, theme);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            Assert.Same(fragment, SettingsControl<CuiContainerControl>(settings, "home-appearance-settings"));
            Assert.Equal(draftBrightness, brightness.Value);
            Assert.Equal(savedBytes, await File.ReadAllBytesAsync(file, testToken));

            ClickSettingsButton(window, SettingsControl<Button>(settings, "home-settings-back"));
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            Assert.DoesNotContain(page.GetVisualDescendants(), control => ReferenceEquals(control, settings));
            Assert.True(gear.IsFocused);
            Assert.True(SettingsCategorySelected(HomeRailButton(page, "home-apps", "9to1 Apps")));
            Assert.Equal("Home", session.Title);
            Assert.Equal("browse://home", session.Address.Value);
            Assert.Equal(new[] { "Theme and accent preferences", "Layout", "Appearance", "Theme and accent preferences", "Home" },
                publishedTitles);

            PressSettingsKey(window, PhysicalKey.Enter);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            Assert.Same(settings, Assert.Single(page.GetVisualDescendants().OfType<HomeSettingsDestinationPage>()));
            Assert.True(theme.IsFocused);
            Assert.Equal(draftBrightness, brightness.Value);
            Assert.Equal("Theme and accent preferences", session.Title);

            ClickSettingsButton(window, layout);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            ClickSettingsButton(window, SettingsControl<Button>(settings, "home-settings-reload-appearance"));
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            ClickSettingsButton(window, appearance);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            Assert.Equal(savedBrightness, brightness.Value);
            Assert.Equal(savedBytes, await File.ReadAllBytesAsync(file, testToken));

            Assert.True(await session.TryPrepareToCloseAsync());
            await session.CloseAndDrainAsync();
            Assert.True(session.CanRetireOriginalView);
            Assert.True(settings.OriginalClose!.IsCompletedSuccessfully);
            // A tab is only a borrower. Closing it cannot dispose the shared source or its context.
            await source.RefreshAsync();
            Assert.Equal("Glow", source.Context.Current.Theme.Name);
        }
        finally
        {
            try { await session.CloseAndDrainAsync(); }
            finally { try { window?.Close(); } finally { await source.DisposeAsync(); Directory.Delete(root, true); } }
        }
    }

    [AvaloniaFact]
    public async Task Settings_shared_appearance_fragment_scrolls_at_520px_and_keeps_its_actual_last_action_reachable()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-short-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\",\"havenUiAppearance\":2,\"overrideAccentColour\":true,\"accentColourName\":\"Orange\"}");
        var initial = await File.ReadAllBytesAsync(file);
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "original-home-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        using var lifetime = new CancellationTokenSource();
        HomeSettingsDestinationPage? settings = null;
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            settings: _ => settings = new(source, _ => Task.CompletedTask)).CreateSession(Request("browse://home/settings")));
        Window? window = null;
        void Layout() { window!.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        try
        {
            await session.InitializeAsync();
            window = new Window { Width = 520, Height = 520, Content = session.OriginalNativeView };
            window.Show(); Layout(); Assert.NotNull(settings);
            var back = SettingsControl<Button>(settings!, "home-settings-back");
            var backOrigin = back.TranslatePoint(default, window)!.Value;
            var appearance = SettingsControl<Button>(settings, "home-settings-appearance");
            var theme = SettingsControl<Button>(settings, "home-settings-theme");
            var layout = SettingsControl<Button>(settings, "home-settings-layout");
            Assert.True(appearance.Focus());
            CaptureSettingsIfRequested(window, "appearance", source);
            PressSettingsKey(window, PhysicalKey.Tab);
            Assert.True(theme.IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowDown);
            Layout();
            Assert.True(SettingsControl<Button>(settings, "home-settings-theme-options").IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowDown);
            Layout();
            Assert.True(layout.IsFocused);
            var nav = SettingsControl<CuiContainerControl>(settings, "home-settings-nav");
            var categories = SettingsControl<CuiContainerControl>(settings, "home-settings-categories");
            var navScroll = Assert.IsType<ScrollViewer>(categories.Child);
            Assert.InRange(nav.Bounds.Height, 1, 176.5);
            Assert.True(navScroll.Offset.Y > 0);
            var categoriesOrigin = categories.TranslatePoint(default, window)!.Value;
            Assert.True(backOrigin.Y >= 0 && backOrigin.Y + back.Bounds.Height <= categoriesOrigin.Y + .5);
            Assert.False(SettingsCategorySelected(layout));
            PressSettingsKey(window, PhysicalKey.Enter);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Layout();
            Assert.True(SettingsCategorySelected(layout));
            Assert.Equal(backOrigin, back.TranslatePoint(default, window)!.Value);
            CaptureSettingsIfRequested(window, "layout", source);

            var accessibility = SettingsControl<Button>(settings, "home-settings-accessibility");
            PressSettingsKey(window, PhysicalKey.End);
            Assert.True(SettingsControl<Button>(settings, "home-settings-apps").IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowUp);
            Assert.True(SettingsControl<Button>(settings, "home-settings-haven").IsFocused);
            Assert.True(accessibility.Focus());
            PressSettingsKey(window, PhysicalKey.Enter);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Layout();
            Assert.True(SettingsCategorySelected(accessibility));
            Assert.Equal(backOrigin, back.TranslatePoint(default, window)!.Value);
            Assert.True(back.IsEffectivelyVisible && back.IsEnabled);
            var accessibilityPage = SettingsControl<CuiContainerControl>(settings, "home-settings-accessibility-page");
            Assert.True(accessibilityPage.IsEffectivelyVisible);
            Assert.False(SettingsControl<CuiContainerControl>(settings, "home-appearance-settings").IsEffectivelyVisible);
            var scroll = Assert.IsType<ScrollViewer>(accessibilityPage.Child);
            var scale = SettingsControl<ComboBox>(settings, "home-settings-interface-scale");
            Assert.True(scale.IsEffectivelyVisible);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.InRange(scale.TranslatePoint(default, scroll)!.Value.Y, 0, scroll.Extent.Height);

            PressSettingsKey(window, PhysicalKey.Home);
            var global = SettingsControl<Button>(settings, "home-settings-global");
            Assert.True(global.IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowLeft);
            Layout();
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(global));
            Assert.DoesNotContain(settings.GetVisualDescendants(), control => ReferenceEquals(control, theme));
            PressSettingsKey(window, PhysicalKey.ArrowRight);
            Layout();
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(global));
            theme = SettingsControl<Button>(settings, "home-settings-theme");
            Assert.True(theme.IsEffectivelyVisible);
            PressSettingsKey(window, PhysicalKey.ArrowRight);
            Assert.True(SettingsControl<Button>(settings, "home-settings-global-general").IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowDown);
            Assert.True(SettingsControl<Button>(settings, "home-settings-appearance").IsFocused);
            appearance = SettingsControl<Button>(settings, "home-settings-appearance");
            PressSettingsKey(window, PhysicalKey.Enter);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Layout();
            Assert.True(SettingsCategorySelected(appearance));
            PressSettingsKey(window, PhysicalKey.ArrowRight);
            Assert.True(theme.IsFocused);
            PressSettingsKey(window, PhysicalKey.Enter);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Layout();
            Assert.True(SettingsCategorySelected(theme));
            Assert.Equal(global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.Equal(global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.True(scroll.Viewport.Height > 0 && scroll.Viewport.Height <= 520);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height,
                $"The constrained real fragment must exercise overflow: extent={scroll.Extent}, viewport={scroll.Viewport}.");
            scroll.Offset = new Vector(0, scroll.Extent.Height); Layout();
            Assert.True(scroll.Offset.Y > 0);
            var refresh = Assert.Single(settings.GetVisualDescendants().OfType<Button>(),
                control => CuiRuntimeIdentity.GetStableId(control) == "id:home-appearance-refresh");
            var origin = refresh.TranslatePoint(default, scroll)!.Value;
            Assert.True(origin.Y >= -.5 && origin.Y + refresh.Bounds.Height <= scroll.Viewport.Height + .5,
                $"The actual last action is clipped after scrolling: origin={origin}, bounds={refresh.Bounds}, viewport={scroll.Viewport}.");
            Assert.True(refresh.IsEnabled);
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(initial, await File.ReadAllBytesAsync(file)); // Refresh cannot replay a write.
            await session.CloseAndDrainAsync(); Assert.True(session.CanRetireOriginalView);
            await source.RefreshAsync(); // Normal tab close does not retire the shared Home owner.
        }
        finally
        {
            try { await session.CloseAndDrainAsync(); }
            finally { try { window?.Close(); } finally { await source.DisposeAsync(); Directory.Delete(root, true); } }
        }
    }

    [AvaloniaTheory]
    [InlineData(720)]
    [InlineData(1180)]
    public async Task Settings_Haven_group_keeps_source_settings_and_opens_existing_native_Browse_destinations(int width)
    {
        var limit = TimeSpan.FromSeconds(20);
        using var lifetime = new CancellationTokenSource();
        var paths = new LayoutPaths();
        const string profile = "original-home-profile";
        var file = Path.Combine(paths.DataDirectory, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\",\"havenUiAppearance\":2}");
        var initial = await File.ReadAllBytesAsync(file);
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, profile),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        BrowseNativeWorkspace? workspace = null;
        Task? navigation = null;
        BrowseNavigationIntent? intent = null;
        var allow = true;
        var home = Factory(new Registry(), lifetime.Token, navigation: (admitted, settlement) =>
        {
            intent = admitted;
            return workspace!.TryQueueNavigationOriginal(admitted, settlement, out navigation);
        }, settings: _ => new(source, _ => allow ? Task.CompletedTask : Task.FromException(new HomeSettingsAuthorityRefusal("Profile changed."))));
        var chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: home,
            homeProfileId: profile, cancellationToken: lifetime.Token);
        workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new LayoutReadiness());
        window.Width = width;
        window.Height = 720;
        try
        {
            RegisterProductionImageResolver(window);
            window.Show();
            await window.InitializeAsync(lifetime.Token).WaitAsync(limit);
            await chrome.OpenContentAsync(AppContentAddress.Parse("browse://home/settings"), cancellationToken: lifetime.Token).WaitAsync(limit);
            await RenderAsync();
            var settingsTab = chrome.State.SelectedTab;
            var settings = Assert.Single(window.GetVisualDescendants().OfType<HomeSettingsDestinationPage>());
            var search = SettingsControl<TextBox>(settings, "home-settings-search");
            var theme = SettingsControl<Button>(settings, "home-settings-theme");
            var group = SettingsControl<Button>(settings, "home-settings-haven");
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(group));
            Assert.True(theme.Focus());
            PressSettingsKey(window, PhysicalKey.End);
            Assert.True(SettingsControl<Button>(settings, "home-settings-apps").IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowUp);
            Assert.True(group.IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowRight);
            await RenderAsync();
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(group));
            PressSettingsKey(window, PhysicalKey.End);
            PressSettingsKey(window, PhysicalKey.ArrowUp);
            Assert.True(SettingsControl<Button>(settings, "home-settings-haven-storage-cache").IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowLeft);
            Assert.True(SettingsControl<Button>(settings, "home-settings-haven-performance").IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowLeft);
            Assert.True(group.IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowLeft);
            await RenderAsync();
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(group));
            PressSettingsKey(window, PhysicalKey.Home);
            Assert.True(SettingsControl<Button>(settings, "home-settings-global").IsFocused);

            foreach (var (section, label, nativePage, control) in new[]
            {
                ("general", "General", BrowserNativePage.Settings, "BrowseSettingsPage"),
                ("bookmarks", "Bookmarks", BrowserNativePage.Bookmarks, "BrowseBookmarksPage"),
                ("history", "History", BrowserNativePage.History, "BrowseHistoryPage")
            })
            {
                Assert.True(search.Focus());
                var query = nativePage == BrowserNativePage.Settings ? "Browse engine" : label;
                search.Text = query;
                await RenderAsync();
                PressSettingsKey(window, PhysicalKey.Enter);
                await settings.WhenActionsIdleAsync().WaitAsync(limit);
                await RenderAsync();
                Assert.True(SettingsCategorySelected(SettingsControl<Button>(settings, "home-settings-haven-" + section)));
                Assert.Equal(label, SettingsControl<TextBlock>(settings, "home-settings-haven-page-title").Text);
                Assert.Equal(settingsTab.Id, chrome.State.SelectedTabId);
                var open = SettingsControl<Button>(settings, "home-settings-open-browse-" + section);
                if (nativePage == BrowserNativePage.Settings)
                {
                    allow = false;
                    ClickSettingsButton(window, open);
                    await settings.WhenActionsIdleAsync().WaitAsync(limit);
                    Assert.Null(intent);
                    Assert.Equal(settingsTab.Id, chrome.State.SelectedTabId);
                    allow = true;
                }
                CaptureSettingsIfRequested(window, "haven-" + section, source);
                ClickSettingsButton(window, open);
                await settings.WhenActionsIdleAsync().WaitAsync(limit);
                Assert.NotNull(navigation);
                await navigation!.WaitAsync(limit);
                await RenderAsync();
                var address = AppContentAddress.Parse(BrowserNativePages.Address(nativePage).AbsoluteUri);
                Assert.Equal(new BrowseNavigationIntent(settingsTab.Id, address, true), intent);
                Assert.NotEqual(settingsTab.Id, chrome.State.SelectedTabId);
                Assert.Equal(address, chrome.State.SelectedTab.Address);
                Assert.Equal(BrowseEngineState.NativePage, chrome.State.SelectedTab.EngineState);
                Assert.True(SettingsControl<CuiContainerControl>(window, control).IsEffectivelyVisible);
                await chrome.SelectTabAsync(settingsTab.Id, lifetime.Token).WaitAsync(limit);
                await RenderAsync();
                Assert.Same(settings, Assert.Single(window.GetVisualDescendants().OfType<HomeSettingsDestinationPage>()));
                Assert.Equal(query, search.Text);
                Assert.True(SettingsCategorySelected(SettingsControl<Button>(settings, "home-settings-haven-" + section)));
            }
            Assert.Equal(initial, await File.ReadAllBytesAsync(file));
        }
        finally
        {
            try
            {
                window.Close();
                if (window.OriginalClosePreparation is { } preparation) Assert.True(await preparation.WaitAsync(limit));
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(window.OriginalClose);
                await window.OriginalClose!.WaitAsync(limit);
            }
            finally
            {
                await source.DisposeAsync();
                Directory.Delete(paths.DataDirectory, recursive: true);
            }
        }

        async Task RenderAsync()
        {
            await Task.WhenAll(workspace!.OriginalNotifications).WaitAsync(limit);
            SettleFixtureLayout(window);
        }
    }

    private static T SettingsControl<T>(Control root, string id) where T : Control
    {
        var directoryId = id.StartsWith("home-settings-", StringComparison.Ordinal)
            ? HomeSettingsDestinationPage.DirectoryItemId(id["home-settings-".Length..])
            : null;
        return Assert.Single(root.GetVisualDescendants().OfType<T>(), control =>
            CuiRuntimeIdentity.GetStableId(control) is { } stableId && (stableId == "id:" + id || stableId == directoryId));
    }

    private static bool SettingsCategorySelected(Button button) =>
        Assert.IsType<bool>(CuiPropertyPresentation.For(button).Resolve("Selected").Value);

    private static void ClickSettingsButton(Window window, Button button)
    {
        var center = button.TranslatePoint(new Rect(button.Bounds.Size).Center, window);
        Assert.NotNull(center);
        window.MouseMove(center.Value);
        window.MouseDown(center.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
    }

    private static void PressSettingsKey(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    private static void CaptureSettingsIfRequested(Window window, string section, NativeHomeThemePreferences source)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_UNIFIED_HOME_CAPTURES"))) return;
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var current = source.Context.Current;
        var accent = current.EffectiveAccent.ResolveColour("app.home") ?? "Home default";
        CaptureOptionalFrame(window, frame, $"home-settings-{section}-{frame.PixelSize.Width}x{frame.PixelSize.Height}",
            $"UnifiedHome Settings route, isolated fixture theme {current.Theme.Name}, accent {accent}", current.Appearance);
    }

    [AvaloniaFact]
    public async Task Configured_Home_Settings_has_one_strict_route_preserving_New_Tab_nested_and_private_refusals()
    {
        using var lifetime = new CancellationTokenSource();
        var paths = new LayoutPaths();
        var file = Path.Combine(paths.DataDirectory, "preferences.json");
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "original-home-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        BrowseChrome? chrome = null;
        try
        {
            chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: Factory(new(), lifetime.Token,
                settings: _ => new(source, _ => Task.CompletedTask)), homeProfileId: "original-home-profile", cancellationToken: lifetime.Token);
            var actual = await chrome.NavigateAsync("browse://home/settings", lifetime.Token);
            Assert.Equal("browse://home/settings", actual.SelectedTab.Address.Value);
            Assert.Equal(BrowseEngineState.NativeApp, actual.SelectedTab.EngineState);
            Assert.Equal("Theme and accent preferences", actual.SelectedTab.Title);
            var pinnedHome = Assert.Single(actual.Tabs, tab => tab.IsHome);
            Assert.False(pinnedHome.IsCloseable);
            Assert.Equal("browse://home", pinnedHome.Address.Value);
            foreach (var invalid in new[] { "browse://newtab/settings", "browse://home/settings/nested" })
            {
                var refused = await chrome.NavigateAsync(invalid, lifetime.Token);
                Assert.Equal("browse://home/settings", refused.SelectedTab.Address.Value);
                Assert.Contains("not registered", refused.Status, StringComparison.Ordinal);
            }
            Assert.Throws<NotSupportedException>(() => Factory(new(), lifetime.Token,
                settings: _ => new(source, _ => Task.CompletedTask)).CreateSession(
                    Request("browse://home/settings") with { Privacy = BrowserTabPrivacy.Private }));
        }
        finally
        {
            try
            {
                if (chrome is not null)
                {
                    var preparation = await chrome.PrepareCloseAsync(lifetime.Token);
                    Assert.NotNull(preparation); await preparation!.BeginClose();
                }
            }
            finally { await source.DisposeAsync(); Directory.Delete(paths.DataDirectory, true); }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_expected_denial_clears_the_projection_without_losing_a_confirmed_write(bool denyAfterWrite)
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-denial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\"}");
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "original-home-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        var checks = 0; var armed = false;
        Task Admit(CancellationToken _) => armed && ++checks == (denyAfterWrite ? 2 : 1)
            ? Task.FromException(new HomeSettingsAuthorityRefusal("Actual owning predicate returned false")) : Task.CompletedTask;
        var page = new HomeSettingsDestinationPage(source, Admit);
        Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            var initial = await File.ReadAllBytesAsync(file); armed = true;
            AppearanceOption(page, "Glow").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.False(page.IsActionAvailable("ApplyHomeAppearance"));
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), button => button.Name == "home-theme-option");
            if (denyAfterWrite)
            {
                Assert.True(page.LastWrite!.ConfirmedWrite!.Succeeded);
                Assert.Equal(1, page.LastWrite.ConfirmedWrite.Value!.Preference.Revision);
                Assert.NotEqual(initial, await File.ReadAllBytesAsync(file));
            }
            else { Assert.Null(page.LastWrite); Assert.Equal(initial, await File.ReadAllBytesAsync(file)); }
            Assert.Empty(page.AdmissionObservations); // Explicit owner denial is not fabricated I/O failure.
            await page.CloseAndDrainAsync();
            Assert.True(page.OriginalClose!.IsCompletedSuccessfully);
        }
        finally
        {
            try { await page.CloseAndDrainAsync(); }
            finally { try { window?.Close(); } finally { await source.DisposeAsync(); Directory.Delete(root, true); } }
        }
    }

    [AvaloniaFact]
    public async Task Settings_tab_close_joins_the_actual_global_write_without_canceling_or_disposing_its_owner()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-held-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\"}");
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = false; var once = true;
        async ValueTask<bool> Authorize(CancellationToken token)
        {
            if (armed && once) { once = false; entered.TrySetResult(token); await release.Task; }
            token.ThrowIfCancellationRequested(); return true;
        }
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "original-home-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme, Authorize, callback => Dispatcher.UIThread.Post(callback));
        var page = new HomeSettingsDestinationPage(source, _ => Task.CompletedTask);
        Window? window = null; Task? close = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page); armed = true;
            AppearanceOption(page, "Glow").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            close = page.CloseAndDrainAsync(); Assert.Same(close, page.CloseAndDrainAsync());
            Assert.False(close.IsCompleted); Assert.False(token.IsCancellationRequested);
            release.TrySetResult();
            await close.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(page.LastWrite!.ConfirmedWrite!.Succeeded);
            Assert.False(token.IsCancellationRequested);
            await source.RefreshAsync();
            Assert.Equal("Glow", source.Context.Current.Theme.Name);
        }
        finally
        {
            release.TrySetResult();
            try { await (close ?? page.CloseAndDrainAsync()).WaitAsync(TimeSpan.FromSeconds(20)); }
            finally { try { window?.Close(); } finally { await source.DisposeAsync(); Directory.Delete(root, true); } }
        }
    }

    [AvaloniaTheory]
    [InlineData("io")]
    [InlineData("foreign-cancel")]
    [InlineData("faulted-owner-oce")]
    [InlineData("mixed-owner-and-io")]
    [InlineData("mixed-io-and-owner")]
    public async Task Settings_actual_admission_faults_are_not_quiet_owner_denials_or_successful_close(string kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-raw-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\"}");
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "original-home-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        var actualAdmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = false;
        Task Admit(CancellationToken token)
        { if (!armed) return Task.CompletedTask; entered.TrySetResult(token); return actualAdmission.Task; }
        var page = new HomeSettingsDestinationPage(source, Admit);
        Window? window = null; Task? close = null; Exception? expected = null, sibling = null;
        using var foreign = new CancellationTokenSource();
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            var initial = await File.ReadAllBytesAsync(file); armed = true;
            AppearanceOption(page, "Glow").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var ownerToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            close = page.CloseAndDrainAsync(); Assert.False(close.IsCompleted);
            if (kind == "foreign-cancel")
            {
                foreign.Cancel(); actualAdmission.SetCanceled(foreign.Token);
            }
            else if (kind.StartsWith("mixed-", StringComparison.Ordinal))
            {
                expected = new HomeSettingsAuthorityRefusal("One owning predicate denied, but its raw Task also failed.");
                sibling = new IOException("A separate actual authority storage observation failed.");
                actualAdmission.SetException(kind == "mixed-owner-and-io" ? [expected, sibling] : [sibling, expected]);
            }
            else
            {
                expected = kind == "io" ? new UnauthorizedAccessException("Original authority I/O access fault")
                    : new OperationCanceledException("FAULTED raw source carrying the canceled page-owner token", ownerToken);
                actualAdmission.SetException(expected);
            }
            var failure = await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.NotNull(failure); Assert.True(close.IsFaulted);
            var observation = Assert.Single(page.AdmissionObservations);
            Assert.Same(actualAdmission.Task, observation.Source); Assert.Equal(ownerToken, observation.RequestedToken);
            if (expected is not null)
            {
                if (sibling is null) Assert.Same(expected, observation.Cause);
                else
                {
                    Assert.Equal(2, Assert.IsType<AggregateException>(observation.Cause).InnerExceptions.Count);
                    Assert.True(ContainsSameSettingsCause(observation.Cause, expected));
                    Assert.True(ContainsSameSettingsCause(observation.Cause, sibling));
                    Assert.True(ContainsSameSettingsCause(failure!, sibling));
                }
                Assert.True(actualAdmission.Task.IsFaulted); Assert.False(actualAdmission.Task.IsCanceled);
                Assert.True(ContainsSameSettingsCause(failure!, expected));
            }
            else
            {
                Assert.True(actualAdmission.Task.IsCanceled);
                Assert.Equal(foreign.Token, Assert.IsAssignableFrom<OperationCanceledException>(observation.Cause).CancellationToken);
                Assert.True(ContainsSameSettingsCause(failure!, observation.Cause));
            }
            Assert.Null(page.LastWrite);
            Assert.Null(page.Content); // All acknowledged actions/loaders physically closed despite retained failure.
            Assert.Equal(initial, await File.ReadAllBytesAsync(file));
            await source.RefreshAsync(); // The unrelated global owner remains live.
        }
        finally
        {
            actualAdmission.TrySetResult();
            if (close is null) close = page.CloseAndDrainAsync();
            var terminal = await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(20)));
            try
            {
                if (terminal is not null && !(expected is not null && ContainsSameSettingsCause(terminal, expected)) &&
                    !page.AdmissionObservations.Any(observation => ContainsSameSettingsCause(terminal, observation.Cause))) throw terminal;
            }
            finally { try { window?.Close(); } finally { await source.DisposeAsync(); Directory.Delete(root, true); } }
        }
    }

    private static bool ContainsSameSettingsCause(Exception actual, Exception expected) =>
        ReferenceEquals(actual, expected) || actual is AggregateException group && group.InnerExceptions.Any(inner => ContainsSameSettingsCause(inner, expected)) ||
        actual.InnerException is { } cause && ContainsSameSettingsCause(cause, expected);

    private static Button AppearanceOption(HomeSettingsDestinationPage page, string name) =>
        Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
            CuiRuntimeIdentity.GetStableId(button) is { } identity &&
            identity.Contains("id:home-theme-options|key:", StringComparison.Ordinal) &&
            identity.EndsWith("/id:home-theme-option", StringComparison.Ordinal) &&
            string.Equals(Avalonia.Automation.AutomationProperties.GetName(button), name, StringComparison.Ordinal));

    [AvaloniaFact]
    public async Task Only_actual_home_owner_projects_native_theme_into_shared_CUI_and_legacy_resources()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-theme-projection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file,
            "{\"havenUiThemeName\":\"Glow\",\"havenUiAppearance\":1,\"overrideAccentColour\":true,\"accentColourName\":\"Blue\"}");
        var originalPreferenceBytes = await File.ReadAllBytesAsync(file);
        var app = Avalonia.Application.Current!;
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        var previousAppearance = CuiThemeScopeApplier.DetectAppearance();
        var previousVariant = app.RequestedThemeVariant;
        var previousCuiOverride = CuiSurfacePaletteCatalog.OverrideAccent;
        var previousCuiAccent = CuiSurfacePaletteCatalog.AccentOverride;
        var previousHavenTheme = HavenPersonalisation.Theme;
        var previousHavenOverride = HavenPersonalisation.OverrideAccent;
        var previousHavenAccent = HavenPersonalisation.Accent;
        var resources = app.Resources.ToDictionary(pair => pair.Key, pair => pair.Value);
        try
        {
            var projectorRoot = Path.Combine(root, "projector");
            Directory.CreateDirectory(projectorRoot);
            await File.WriteAllBytesAsync(Path.Combine(projectorRoot, "preferences.json"), originalPreferenceBytes);
            var projection = new UserPreferencesService(new ThemeProjectionPaths(projectorRoot));
            CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Professional, appearanceOverride: CuiAppearance.Dark);
            await using (var local = new NativeHomeThemePreferences(file,
                new(HomeDataScope.DeviceLocal, "home-theme-projection-profile"), UserPreferencesService.ProjectLegacyHomeTheme,
                _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback)))
            {
                await local.RefreshAsync();
                await Dispatcher.UIThread.InvokeAsync(() => Dispatcher.UIThread.RunJobs());
                Assert.Equal(CuiTheme.Professional, CuiSurfacePaletteCatalog.ActiveTheme);
                Assert.Equal(CuiAppearance.Dark, CuiThemeScopeApplier.DetectAppearance());
            }

            await using (var owner = new NativeHomeThemePreferences(file,
                new(HomeDataScope.DeviceLocal, "home-theme-projection-profile"), UserPreferencesService.ProjectLegacyHomeTheme,
                _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback),
                projection.ApplyHomeThemePresentation))
            {
                await owner.RefreshAsync();
                await Dispatcher.UIThread.InvokeAsync(() => Dispatcher.UIThread.RunJobs());
                Assert.Equal(CuiTheme.Glow, CuiSurfacePaletteCatalog.ActiveTheme);
                Assert.Equal(CuiAppearance.Bright, CuiThemeScopeApplier.DetectAppearance());
                Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
                Assert.Equal(HavenUiTheme.Glow, HavenPersonalisation.Theme);
                Assert.Equal(HavenUiAppearance.Bright, projection.Appearance);
                Assert.Equal(HavenUiTheme.Glow, projection.Theme);
                Assert.True(HavenPersonalisation.OverrideAccent);
                Assert.Equal(HavenAccentColour.Blue, HavenPersonalisation.Accent);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Bright, CuiTheme.Glow, "#2563EB").TideBase,
                    Assert.IsAssignableFrom<SolidColorBrush>(app.Resources["CuiBackgroundBrush"]).Color);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Bright, CuiTheme.Glow, "#2563EB").TideBase,
                    Assert.IsAssignableFrom<SolidColorBrush>(app.Resources["HavenBackgroundPrimaryBrush"]).Color);
                Assert.Equal(originalPreferenceBytes, await File.ReadAllBytesAsync(file));

                Assert.True((await owner.ApplySettingsAsync(AppearanceTarget(owner, "HomeAppearanceChoices", "Dark"))).Succeeded);
                await owner.RefreshAsync();
                await Dispatcher.UIThread.InvokeAsync(() => Dispatcher.UIThread.RunJobs());
                Assert.Equal(CuiAppearance.Dark, CuiThemeScopeApplier.DetectAppearance());
                Assert.Equal(HavenUiAppearance.Dark, projection.Appearance);
                Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Glow, "#2563EB").TideBase,
                    Assert.IsAssignableFrom<SolidColorBrush>(app.Resources["CuiBackgroundBrush"]).Color);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Glow, "#2563EB").TideBase,
                    Assert.IsAssignableFrom<SolidColorBrush>(app.Resources["HavenBackgroundPrimaryBrush"]).Color);

                projection.SetFontPreference(null, save: false);
                Assert.Equal(CuiTheme.Glow, CuiSurfacePaletteCatalog.ActiveTheme);
                Assert.Equal(CuiAccentColour.Blue, CuiSurfacePaletteCatalog.AccentOverride);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark, CuiTheme.Glow, "#2563EB").TideBase,
                    Assert.IsAssignableFrom<SolidColorBrush>(app.Resources["CuiBackgroundBrush"]).Color);
                projection.ApplyThemeChoice("Professional", save: false);
                Assert.Equal(HavenUiTheme.Glow, projection.Theme); // Legacy projection of CUI-only Professional.
                Assert.Equal(HavenUiAppearance.Dark, projection.Appearance); // Keep the current Home appearance across a theme-only change.
                Assert.Equal(CuiTheme.Professional, CuiSurfacePaletteCatalog.ActiveTheme);
                projection.ApplyAppearance(HavenUiAppearance.SuperBright, save: false);
                Assert.Equal(HavenUiAppearance.SuperBright, projection.Appearance);
                Assert.Equal(CuiTheme.Professional, CuiSurfacePaletteCatalog.ActiveTheme);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Home", CuiAppearance.SuperBright, CuiTheme.Professional).TideBase,
                    Assert.IsAssignableFrom<SolidColorBrush>(app.Resources["CuiBackgroundBrush"]).Color);
            }
        }
        finally
        {
            CuiSurfacePaletteCatalog.OverrideAccent = previousCuiOverride;
            CuiSurfacePaletteCatalog.AccentOverride = previousCuiAccent;
            HavenPersonalisation.Theme = previousHavenTheme;
            HavenPersonalisation.OverrideAccent = previousHavenOverride;
            HavenPersonalisation.Accent = previousHavenAccent;
            foreach (var key in app.Resources.Keys.ToArray())
                if (!resources.ContainsKey(key)) app.Resources.Remove(key);
            foreach (var pair in resources) app.Resources[pair.Key] = pair.Value;
            app.RequestedThemeVariant = previousVariant;
            CuiThemeScopeApplier.ApplyGlobalTheme(previousTheme, appearanceOverride: previousAppearance);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task Home_appearance_targets_save_the_real_native_file_preserve_other_settings_and_reopen_live_contexts()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Glow\",\"havenUiAppearance\":3,\"overrideAccentColour\":true,\"accentColourName\":\"Blue\",\"fontFamilyName\":\"Existing font\",\"defaultModel\":\"retained-model\",\"futureSetting\":{\"keep\":true}}");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "actual-settings-profile");
        var source = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        try
        {
            var initial = await File.ReadAllBytesAsync(file);
            await source.RefreshAsync();
            Assert.Equal(initial, await File.ReadAllBytesAsync(file));
            var stale = ThemeTarget(source, "Glow");
            var target = ThemeTarget(source, "Professional");
            using var secondContext = new CuiThemePreferenceContext(source);
            var receipt = await source.ApplySettingsAsync(target);
            Assert.True(receipt.Succeeded);
            Assert.Equal(scope, receipt.Value!.Preference.Scope);
            Assert.Equal(1, receipt.Value.Preference.Revision);
            await source.RefreshAsync();
            Assert.Equal("Professional", source.Context.Current.Theme.Name);
            Assert.Equal("Professional", secondContext.Current.Theme.Name);
            var saved = HomeNativePreferenceFile.Read(file);
            Assert.Equal("retained-model", saved["defaultModel"]!.GetValue<string>());
            Assert.True(saved["futureSetting"]!["keep"]!.GetValue<bool>());
            var preference = HomeNativePreferenceFile.Resolve(saved, scope, UserPreferencesService.ProjectLegacyHomeTheme);
            Assert.Equal("#4E86FF", preference.Accent!.AllAppsColour);
            Assert.Equal("Existing font", preference.Accessibility.InterfaceFontFamily);
            Assert.Equal("SuperDark", preference.Appearance);
            Assert.True((await source.ApplySettingsAsync(AppearanceTarget(source, "HomeAccessibilityChoices", "Reduce motion"))).Succeeded);
            await source.RefreshAsync();
            Assert.True(source.Current.Accessibility.ReduceMotion);
            Assert.True((await source.ApplySettingsAsync(AppearanceTarget(source, "HomeAccessibilityChoices", "High contrast"))).Succeeded);
            await source.RefreshAsync();
            Assert.True(source.Current.Accessibility.HighContrast);
            Assert.True((await source.ApplySettingsAsync(AppearanceTarget(source, "HomeAppearanceChoices", "Bright"))).Succeeded);
            await source.RefreshAsync();
            Assert.Equal(CuiAppearance.Bright, source.Current.Appearance);
            var committed = await File.ReadAllBytesAsync(file);
            Assert.Equal("SettingsIntentRetired", (await source.ApplySettingsAsync(stale)).Code);
            Assert.Equal(committed, await File.ReadAllBytesAsync(file));
        }
        finally { await source.DisposeAsync(); }
        await using (var reopened = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback)))
        {
            await reopened.RefreshAsync();
            Assert.Equal("Professional", reopened.Current.Name);
            Assert.Equal(4, reopened.Current.Revision);
            Assert.True(reopened.Current.Accessibility.ReduceMotion);
            Assert.True(reopened.Current.Accessibility.HighContrast);
            Assert.Equal(CuiAppearance.Bright, reopened.Current.Appearance);
        }
        Directory.Delete(root, true);
    }

    [AvaloniaFact]
    public async Task Settings_accent_controls_save_global_colour_while_off_and_retain_per_app_choices_after_reopen()
    {
        var directory = Directory.CreateTempSubdirectory("home-accent-");
        var file = Path.Combine(directory.FullName, "preferences.json");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "accent-profile");
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(file, """{"defaultModel":"retained-model"}""", token);
        var existing = HomeThemePreference.Default(scope) with
        {
            Revision = 1, Appearance = "Dark", Display = new(HomeDisplayMode.Dark, 60, 40),
            Accent = new(HomeThemeAccentOverrideMode.None, "#123ABC", new Dictionary<string, string> { ["app.write"] = "#11AA77" })
        };
        Assert.True((await HomeNativePreferenceFile.CompareExchangeAsync(file, scope, 0, existing,
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), token)).Succeeded);
        NativeHomeThemePreferences OpenOwner() => new(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        try
        {
            await using (var source = OpenOwner())
            {
                var settings = new HomeSettingsDestinationPage(source, _ => Task.CompletedTask);
                Window? window = null;
                try
                {
                    await settings.ActivateAsync(token);
                    window = Show(settings);
                    var fragment = SettingsControl<CuiContainerControl>(settings, "home-appearance-settings");
                    var scroll = Assert.IsType<ScrollViewer>(fragment.Child);
                    scroll.ScrollToEnd();
                    SettleFixtureLayout(window);
                    var panel = SettingsControl<CuiContainerControl>(settings, "home-accent-settings");
                    // TestAppBuilder's fast drawing stub has no painted geometry for scrolled swatches.
                    // The same controls always accept keyboard activation; Skia also exercises pointer targeting.
                    var pointerRendering = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR"));
                    Button Option(string name) => panel.GetVisualDescendants().OfType<Button>()
                        .Single(button => AutomationProperties.GetName(button) == name);
                    async Task<HomeThemePreference> Choose(string name, bool keyboard = false)
                    {
                        var button = Option(name);
                        button.BringIntoView();
                        SettleFixtureLayout(window);
                        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
                        if (keyboard || !pointerRendering)
                        {
                            Assert.True(button.Focus());
                            PressSettingsKey(window, PhysicalKey.Enter);
                        }
                        else
                        {
                            var center = button.TranslatePoint(new Rect(button.Bounds.Size).Center, window)!.Value;
                            window.MouseMove(center);
                            var hit = window.InputHitTest(center) as Visual;
                            Assert.True(hit == button || hit?.GetVisualAncestors().Contains(button) == true,
                                $"Accent {name} at {center} hit {hit}; scroll {scroll.Offset}/{scroll.Extent}, viewport {scroll.Viewport}.");
                            ClickSettingsButton(window, button);
                        }
                        await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
                        SettleFixtureLayout(window);
                        Assert.True(SettingsCategorySelected(Option(name)),
                            $"Accent {name} failed to select; last receipt {settings.LastWrite?.ConfirmedWrite?.Code ?? "none"}.");
                        return settings.LastWrite!.ConfirmedWrite!.Value!.Preference;
                    }
                    Assert.True(SettingsCategorySelected(Option("Off")));
                    Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "#123ABC");
                    var off = await Choose("Iris");
                    Assert.Equal(2, off.Revision);
                    Assert.Equal(HomeThemeAccentOverrideMode.None, off.Accent!.Mode);
                    Assert.Equal("#7145FF", off.Accent.AllAppsColour);
                    Assert.Null(off.Accent.ResolveColour("app.write"));
                    var on = await Choose("On", keyboard: true);
                    Assert.Equal("#7145FF", on.Accent!.ResolveColour("app.write"));
                    var individual = await Choose("On per app");
                    Assert.Equal(4, individual.Revision);
                    Assert.Equal("#11AA77", individual.Accent!.ResolveColour("app.write"));
                    Assert.Null(individual.Accent.ResolveColour("app.present"));
                    await source.RefreshAsync(token);
                    SettleFixtureLayout(window);
                    CaptureSettingsIfRequested(window, "accent", source);
                }
                finally { window?.Close(); await settings.CloseAndDrainAsync(); }
            }
            await using var reopened = OpenOwner();
            await reopened.RefreshAsync(token);
            Assert.Equal(CuiAccentOverrideMode.IndividualApps, reopened.Current.EffectiveAccent.Mode);
            Assert.Equal("#7145FF", reopened.Current.EffectiveAccent.AllAppsColour);
            Assert.Equal("#11AA77", reopened.Current.EffectiveAccent.IndividualApps["app.write"]);
            var saved = HomeNativePreferenceFile.Read(file);
            Assert.Equal("retained-model", saved["defaultModel"]!.GetValue<string>());
            Assert.Equal(existing.Display, HomeNativePreferenceFile.Resolve(saved, scope, UserPreferencesService.ProjectLegacyHomeTheme).Display);
        }
        finally { directory.Delete(recursive: true); }
    }

    [AvaloniaFact]
    public async Task Home_display_sliders_commit_once_preserve_legacy_settings_and_reject_a_stale_draft()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-display-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, """{"havenUiThemeName":"Glow","havenUiAppearance":3,"keep":"existing"}""");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "display-test");
        var source = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        try
        {
            var original = await File.ReadAllBytesAsync(file);
            await source.RefreshAsync();
            var model = new CuiViewModel();
            model.PropertyChanged += NativeHomeThemePreferences.UpdateDisplayReadout;
            source.ApplyEditorBindings(model);
            Assert.False((bool)model.Get("HomeCanApplyLevels")!);
            Assert.Equal(50d, model.Get("HomeBrightness"));
            Assert.Equal(50d, model.Get("HomeColourContrast"));
            Assert.True(model.TrySetValue("HomeBrightness", 72d));
            Assert.True(model.TrySetValue("HomeColourContrast", 38d));
            source.ApplyEditorBindings(model);
            Assert.Equal("72%", model.Get("HomeBrightnessText"));
            Assert.True((bool)model.Get("HomeCanApplyLevels")!);
            Assert.Equal(original, await File.ReadAllBytesAsync(file));

            var draft = NativeHomeThemePreferences.CaptureDisplayDraft(model.Get("HomeDisplayTarget"), model);
            var commit = await source.ApplySettingsAsync(draft);
            Assert.True(commit.Succeeded);
            Assert.Equal(new HomeDisplaySettings(HomeDisplayMode.Dark, 72, 38), commit.Value!.Preference.Display);
            Assert.Equal("SuperDark", commit.Value.Preference.Appearance);
            await source.RefreshAsync();
            source.ApplyEditorBindings(model);
            Assert.False((bool)model.Get("HomeCanApplyLevels")!);
            Assert.False((await source.ApplySettingsAsync(draft)).Succeeded);
            Assert.True((await source.ApplySettingsAsync(AppearanceTarget(source, "HomeModeChoices", "Light"))).Succeeded);
            await source.RefreshAsync();
            Assert.Equal(CuiAppearance.Bright, source.Current.Appearance);
            Assert.Equal(72, source.Current.Accessibility.Brightness);
            Assert.Equal(38, source.Current.Accessibility.ColourContrast);
            var highContrast = await source.ApplySettingsAsync(AppearanceTarget(source, "HomeAccessibilityChoices", "High contrast"));
            Assert.True(highContrast.Succeeded);
            await source.RefreshAsync();
            var palette = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Bright, CuiTheme.Glow);
            Assert.Equal(Colors.White, CuiAccessibilityPalette.Resolve(palette, source.Current.Accessibility).TideBase);
            var saved = HomeNativePreferenceFile.Read(file);
            var same = HomeNativePreferenceFile.Resolve(saved, scope, UserPreferencesService.ProjectLegacyHomeTheme);
            Assert.Equal("existing", saved["keep"]!.GetValue<string>());
            Assert.Equal(new HomeDisplaySettings(HomeDisplayMode.Light, 72, 38), same.Display);

            source.ApplyEditorBindings(model);
            Assert.True(model.TrySetValue("HomeBrightness", 80d));
            var other = new CuiViewModel();
            source.ApplyEditorBindings(other);
            Assert.True(other.TrySetValue("HomeBrightness", 60d));
            var external = NativeHomeThemePreferences.CaptureDisplayDraft(other.Get("HomeDisplayTarget"), other);
            Assert.True((await source.ApplySettingsAsync(external)).Succeeded);
            await source.RefreshAsync();
            source.ApplyEditorBindings(model);
            Assert.Equal(80d, model.Get("HomeBrightness"));
            Assert.Equal(true, model.Get("HomeDisplayConflict"));
            Assert.Null(model.Get("HomeDisplayTarget"));
            Assert.Equal(false, model.Get("HomeCanApplyLevels"));
            NativeHomeThemePreferences.ReloadDisplayDraft(model);
            source.ApplyEditorBindings(model);
            Assert.Equal(60d, model.Get("HomeBrightness"));
            Assert.Equal(false, model.Get("HomeDisplayConflict"));
        }
        finally
        {
            await source.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task Home_appearance_changes_preserve_custom_library_per_app_accents_and_additive_saved_fields()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-additive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\",\"defaultModel\":\"unchanged-model\"}");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "additive-settings-profile");
        var seed = HomeThemePreference.Default(scope) with
        {
            Revision = 1, Appearance = "Bright", CustomThemesJson = "[]",
            Accessibility = new(DisplayScale: 1.25, InterfaceFontFamily: "Existing interface font", CodeFontFamily: "Existing code font"),
            Accent = new(HomeThemeAccentOverrideMode.IndividualApps, individualApps: new Dictionary<string, string> { ["app.boards"] = "#123456" })
        };
        Assert.True((await HomeNativePreferenceFile.CompareExchangeAsync(file, scope, 0, seed,
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), CancellationToken.None)).Succeeded);
        var document = HomeNativePreferenceFile.Read(file);
        document[HomeNativePreferenceFile.ThemeKey]!["futureThemeField"] = new JsonObject { ["keep"] = true };
        document[HomeNativePreferenceFile.ThemeKey]!["accessibility"]!["futureAccessibilityField"] = "preserved";
        document[HomeNativePreferenceFile.ThemeKey]!["accent"]!["futureAccentField"] = "preserved";
        await File.WriteAllTextAsync(file, document.ToJsonString());
        await using (var source = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback)))
        {
            await source.RefreshAsync();
            Assert.True((await source.ApplySettingsAsync(ThemeTarget(source, "Glow"))).Succeeded);
            var saved = HomeNativePreferenceFile.Read(file);
            var preference = HomeNativePreferenceFile.Resolve(saved, scope, UserPreferencesService.ProjectLegacyHomeTheme);
            Assert.Equal("[]", preference.CustomThemesJson);
            Assert.Equal(HomeThemeAccentOverrideMode.IndividualApps, preference.Accent!.Mode);
            Assert.Equal("#123456", preference.Accent.IndividualApps["app.boards"]);
            Assert.Equal(seed.Accessibility, preference.Accessibility);
            Assert.Equal("Bright", preference.Appearance);
            Assert.Equal("unchanged-model", saved["defaultModel"]!.GetValue<string>());
            Assert.True(saved[HomeNativePreferenceFile.ThemeKey]!["futureThemeField"]!["keep"]!.GetValue<bool>());
            Assert.Equal("preserved", saved[HomeNativePreferenceFile.ThemeKey]!["accessibility"]!["futureAccessibilityField"]!.GetValue<string>());
            Assert.Equal("preserved", saved[HomeNativePreferenceFile.ThemeKey]!["accent"]!["futureAccentField"]!.GetValue<string>());
        }
        Directory.Delete(root, true);
    }

    [AvaloniaFact]
    public async Task Home_appearance_refuses_foreign_issued_targets_current_actor_denial_and_unknown_saved_schema_without_overwrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-authority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\",\"defaultModel\":\"retained\"}");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "original-settings-profile");
        var allowed = true;
        await using var original = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(allowed), callback => Dispatcher.UIThread.Post(callback));
        await using var other = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        try
        {
            await original.RefreshAsync(); await other.RefreshAsync();
            var target = ThemeTarget(original, "Glow");
            var initial = await File.ReadAllBytesAsync(file);
            Assert.Equal("SettingsIntentRetired", (await other.ApplySettingsAsync(target)).Code);
            allowed = false;
            Assert.Equal("PermissionDenied", (await original.ApplySettingsAsync(target)).Code);
            Assert.Equal(initial, await File.ReadAllBytesAsync(file));
            allowed = true;
            Assert.True((await original.ApplySettingsAsync(target)).Succeeded);
            await original.RefreshAsync();
            var envelope = HomeNativePreferenceFile.Read(file);
            envelope[HomeNativePreferenceFile.ThemeKey]!["schemaVersion"] = 999;
            await File.WriteAllTextAsync(file, envelope.ToJsonString());
            var incompatible = await File.ReadAllBytesAsync(file);
            Assert.False((await original.ApplySettingsAsync(ThemeTarget(original, "Bubble"))).Succeeded);
            Assert.Equal(incompatible, await File.ReadAllBytesAsync(file));
        }
        finally
        {
            await other.DisposeAsync(); await original.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task Home_appearance_close_retains_its_actual_pending_authorization_and_joins_before_service_disposal()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\"}");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "original-settings-close-profile");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = false;
        async ValueTask<bool> Authorize(CancellationToken token)
        {
            if (hold) { entered.TrySetResult(); await release.Task; }
            token.ThrowIfCancellationRequested(); return true;
        }
        var source = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            Authorize, callback => Dispatcher.UIThread.Post(callback));
        await source.RefreshAsync();
        var initial = await File.ReadAllBytesAsync(file);
        hold = true;
        var actual = source.ApplySettingsAsync(ThemeTarget(source, "Glow"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var close = source.DisposeAsync().AsTask();
        Assert.Same(close, source.DisposeAsync().AsTask());
        Assert.False(close.IsCompleted);
        release.SetResult();
        var retired = await actual.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("SettingsRetired", retired.Code);
        Assert.Contains(source.AuthorityObservations, observation => observation.Source?.IsCanceled == true &&
            observation.Cause is OperationCanceledException error && error.CancellationToken == observation.RequestedToken &&
            observation.RequestedToken.IsCancellationRequested);
        await close.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Same(close, source.OriginalClose);
        Assert.Equal(initial, await File.ReadAllBytesAsync(file));
        Directory.Delete(root, true);
    }

    [AvaloniaFact]
    public async Task Home_appearance_foreign_cancellation_remains_a_failed_original_when_the_owner_closes()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-foreign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\"}");
        using var foreign = new CancellationTokenSource(); foreign.Cancel();
        var refuse = false;
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "foreign-token-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme,
            _ => refuse ? ValueTask.FromCanceled<bool>(foreign.Token) : ValueTask.FromResult(true),
            callback => Dispatcher.UIThread.Post(callback));
        await source.RefreshAsync();
        var initial = await File.ReadAllBytesAsync(file);
        var target = ThemeTarget(source, "Glow"); refuse = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ApplySettingsAsync(target));
        await Assert.ThrowsAnyAsync<Exception>(() => source.DisposeAsync().AsTask());
        Assert.Equal(initial, await File.ReadAllBytesAsync(file));
        Directory.Delete(root, true);
    }

    [AvaloniaFact]
    public async Task Home_appearance_does_not_waive_failed_explicit_caller_cancellation_during_later_owner_close()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-caller-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\"}");
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "cancelled-caller-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true),
            callback => Dispatcher.UIThread.Post(callback));
        await source.RefreshAsync();
        var initial = await File.ReadAllBytesAsync(file);
        var target = ThemeTarget(source, "Glow");
        using var caller = new CancellationTokenSource(); caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ApplySettingsAsync(target, caller.Token));
        await Assert.ThrowsAnyAsync<Exception>(() => source.DisposeAsync().AsTask());
        Assert.Equal(initial, await File.ReadAllBytesAsync(file));
        Directory.Delete(root, true);
    }

    [AvaloniaFact]
    public async Task Home_appearance_faulted_authority_source_reusing_owner_token_cancellation_remains_a_real_close_failure()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-faulted-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\"}");
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actualAuthority = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = false;
        ValueTask<bool> Authorize(CancellationToken token)
        {
            if (!hold) return ValueTask.FromResult(true);
            entered.TrySetResult(token); return new(actualAuthority.Task);
        }
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "faulted-owner-source-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme, Authorize, callback => Dispatcher.UIThread.Post(callback));
        await source.RefreshAsync();
        var initial = await File.ReadAllBytesAsync(file);
        var target = ThemeTarget(source, "Glow"); hold = true;
        var operation = source.ApplySettingsAsync(target);
        var issued = await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var close = source.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        Assert.True(issued.IsCancellationRequested);
        var fault = new OperationCanceledException("faulted source, not a cancelled task", issued);
        actualAuthority.SetException(fault);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => operation);
        Assert.Contains(fault, Assert.IsType<AggregateException>(failure).Flatten().InnerExceptions);
        await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.True(actualAuthority.Task.IsFaulted);
        var observation = Assert.Single(source.AuthorityObservations);
        Assert.Same(actualAuthority.Task, observation.Source);
        Assert.Same(fault, observation.Cause);
        Assert.Equal(issued, observation.RequestedToken);
        Assert.Equal(initial, await File.ReadAllBytesAsync(file));
        Directory.Delete(root, true);
    }

    [Fact]
    public void Captured_native_preference_binding_preserves_reader_and_candidate_files_and_refuses_competing_store_writes()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-binding-" + Guid.NewGuid().ToString("N"));
        var reader = Path.Combine(root, "reader"); var artifacts = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(reader); Directory.CreateDirectory(artifacts);
        var canonical = Path.Combine(reader, "preferences.json");
        var competing = Path.Combine(artifacts, "preferences.json");
        File.WriteAllText(canonical, "{\"havenUiThemeName\":\"Glow\",\"defaultModel\":\"existing-model\"}");
        try
        {
            var initial = File.ReadAllBytes(canonical);
            var binding = HomeNativePreferenceFile.CaptureExistingReaderBinding(artifacts, reader);
            Assert.True(binding.CanWrite);
            Assert.Equal(canonical, binding.Path);
            Assert.False(File.Exists(competing));
            Assert.Equal(initial, File.ReadAllBytes(canonical));
            File.WriteAllText(competing, "{\"havenUiThemeName\":\"Retro\",\"otherProfileData\":true}");
            var candidateBytes = File.ReadAllBytes(competing);
            var conflict = HomeNativePreferenceFile.CaptureExistingReaderBinding(artifacts, reader);
            Assert.False(conflict.CanWrite);
            Assert.Equal(canonical, conflict.Path);
            Assert.NotNull(conflict.Diagnostic);
            Assert.Equal(initial, File.ReadAllBytes(canonical));
            Assert.Equal(candidateBytes, File.ReadAllBytes(competing));
            Assert.True(HomeNativePreferenceFile.CaptureExistingReaderBinding(reader, reader).CanWrite);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaTheory]
    [InlineData("actor")]
    [InlineData("presentation")]
    [InlineData("foreign-cancellation")]
    public async Task Confirmed_appearance_write_keeps_its_exact_receipt_after_late_observation_failures_without_replay(string failureKind)
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-postcommit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\",\"defaultModel\":\"preserved-model\"}");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "postcommit-observation-profile");
        var source = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        using var foreign = new CancellationTokenSource();
        try
        {
            await source.RefreshAsync();
            var target = ThemeTarget(source, "Glow");
            var write = source.CreateSettingsWriteOutcome();
            var receipt = await write.ApplyAsync(target);
            Assert.True(receipt.Succeeded);
            Assert.Same(receipt, write.ConfirmedWrite);
            Assert.Equal(1, receipt.Value!.Preference.Revision);
            var committed = await File.ReadAllBytesAsync(file);
            var observerCalls = 0;
            Task? actualObserver = null;
            Exception? originalCause = failureKind == "actor"
                ? new UnauthorizedAccessException("Original current-profile observation failed AFTER the confirmed save")
                : failureKind == "presentation" ? new InvalidOperationException("Original presentation callback failed AFTER the confirmed save") : null;
            await write.ObserveAfterWriteAsync(() =>
            {
                observerCalls++;
                if (failureKind == "presentation") throw originalCause!;
                if (failureKind == "actor") return actualObserver = Task.FromException(originalCause!);
                foreign.Cancel();
                return actualObserver = Task.FromCanceled(foreign.Token);
            });
            Assert.Equal(1, observerCalls);
            var observation = Assert.Single(write.PostCommitObservations);
            Assert.Same(actualObserver, observation.Source);
            if (originalCause is not null) Assert.Same(originalCause, observation.Cause);
            else
            {
                Assert.True(actualObserver!.IsCanceled);
                Assert.Equal(foreign.Token, Assert.IsAssignableFrom<OperationCanceledException>(observation.Cause).CancellationToken);
            }
            Assert.Same(receipt, write.ConfirmedWrite);
            Assert.True(write.ConfirmedWrite!.Succeeded);
            Assert.Equal(committed, await File.ReadAllBytesAsync(file));
            var saved = HomeNativePreferenceFile.Resolve(HomeNativePreferenceFile.Read(file), scope, UserPreferencesService.ProjectLegacyHomeTheme);
            Assert.Equal(1, saved.Revision); Assert.Equal("Glow", saved.ThemeName);
            Assert.Equal("preserved-model", HomeNativePreferenceFile.Read(file)["defaultModel"]!.GetValue<string>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => write.ApplyAsync(target));
            Assert.Equal(committed, await File.ReadAllBytesAsync(file)); // Same outcome never replays Set/CAS.
        }
        finally { await source.DisposeAsync(); Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task Unconfirmed_appearance_write_preserves_original_precommit_failure_and_cannot_waive_observer_errors()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-settings-precommit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\",\"defaultModel\":\"preserved-model\"}");
        var cause = new IOException("Original authority source failed BEFORE any preference commit");
        var actualAuthority = Task.FromException<bool>(cause);
        var fail = false;
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "precommit-failure-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme,
            _ => fail ? new ValueTask<bool>(actualAuthority) : ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        try
        {
            await source.RefreshAsync();
            var initial = await File.ReadAllBytesAsync(file);
            var write = source.CreateSettingsWriteOutcome();
            var target = ThemeTarget(source, "Glow");
            fail = true;
            var error = await Assert.ThrowsAsync<IOException>(() => write.ApplyAsync(target));
            Assert.Same(cause, error); Assert.Null(write.ConfirmedWrite);
            Assert.Empty(write.PostCommitObservations);
            var called = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() => write.ObserveAfterWriteAsync(() =>
            { called = true; return Task.CompletedTask; }));
            Assert.False(called);
            Assert.False(write.RetainPostCommitFailure(null, new InvalidOperationException("No receipt exists")));
            Assert.Equal(initial, await File.ReadAllBytesAsync(file));
            var retained = Assert.Single(source.AuthorityObservations);
            Assert.Same(actualAuthority, retained.Source); Assert.Same(cause, retained.Cause);
            var close = await Assert.ThrowsAnyAsync<Exception>(() => source.DisposeAsync().AsTask());
            Assert.True(ReferenceEquals(close, cause) || close is AggregateException group &&
                group.Flatten().InnerExceptions.Any(retainedCause => ReferenceEquals(retainedCause, cause)));
        }
        finally
        {
            try { await source.DisposeAsync(); }
            catch (Exception close) when (ReferenceEquals(close, cause) || close is AggregateException group &&
                group.Flatten().InnerExceptions.Any(retainedCause => ReferenceEquals(retainedCause, cause))) { }
            Directory.Delete(root, true);
        }
    }

    private static object ThemeTarget(NativeHomeThemePreferences source, string name) =>
        AppearanceTarget(source, "HomeThemeChoices", name);
    private static object AppearanceTarget(NativeHomeThemePreferences source, string collection, string name) =>
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(source.SettingsBindings()[collection]),
            choice => Equals(choice["Name"], name))["Target"]!;

    [AvaloniaFact]
    public async Task Authored_Events_rail_uses_canonical_source_and_retains_its_destination_between_sections()
    {
        var testToken = TestContext.Current.CancellationToken;
        using var lifetime = new CancellationTokenSource();
        var activity = new HomeActivityFixture();
        HomeEventsDestinationPage? destination = null;
        var navigations = 0;
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            navigation: (_, _) => { navigations++; return true; },
            events: _ => destination = new HomeEventsDestinationPage(new HomeEventsService([activity], activity),
                _ => Task.CompletedTask)).CreateSession(Request()));
        Window? window = null;
        try
        {
            await session.InitializeAsync();
            var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView).OriginalContent);
            window = Show(session.OriginalNativeView);
            var rail = page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "home-events");
            rail.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            Assert.NotNull(destination);
            Assert.Contains(page.GetVisualDescendants().OfType<HomeEventsDestinationPage>(), item => ReferenceEquals(item, destination));
            var observed = Assert.Single(destination!.Current!.Items);
            Assert.Equal(activity.Event, observed);
            Assert.True(activity.Reads > 0);
            Assert.Equal(0, navigations);
            var row = Assert.Single(destination.GetVisualDescendants().OfType<Button>(), button =>
                CuiRuntimeIdentity.GetStableId(button) is { } identity &&
                identity.Contains("id:home-events-rows|key:", StringComparison.Ordinal) &&
                identity.EndsWith("/id:home-event-row", StringComparison.Ordinal));
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await destination.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), testToken);
            SettleFixtureLayout(window);
            Assert.Equal(activity.Event.EventId, destination.SelectedEventId);
            var open = destination.GetVisualDescendants().OfType<Button>().Single(button => CuiRuntimeIdentity.GetStableId(button) == "id:events-open-selected");
            Assert.False(open.IsEnabled);
            Assert.DoesNotContain(destination.GetVisualDescendants(), control => control is ListBox);
            await page.DispatchAsync("home.apps", null);
            await page.DispatchAsync("home.events", null);
            SettleFixtureLayout(window);
            Assert.Same(destination, Assert.Single(page.GetVisualDescendants().OfType<HomeEventsDestinationPage>()));
            Assert.Equal(activity.Event.EventId, destination.SelectedEventId);
            Assert.True(await session.TryPrepareToCloseAsync());
        }
        finally { await session.CloseAndDrainAsync(); window?.Close(); }
        Assert.True(session.CanRetireOriginalView);
        Assert.NotNull(destination!.OriginalClose);
        Assert.True(destination.OriginalClose!.IsCompletedSuccessfully);
    }

    [AvaloniaFact]
    public async Task Central_Browse_dispatch_admits_real_Home_Events_but_refuses_newtab_and_nested_event_routes()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var paths = new LayoutPaths();
        var activity = new HomeActivityFixture();
        BrowseChrome? chrome = null;
        HomeEventsDestinationPage? events = null;
        try
        {
            chrome = await BrowseChrome.CreateAsync(paths,
                internalContentFactory: Factory(new(), lifetime.Token,
                    events: _ => events = new HomeEventsDestinationPage(new HomeEventsService([activity], activity),
                        _ => Task.CompletedTask)),
                homeProfileId: "central-events-profile", cancellationToken: lifetime.Token);
            var admitted = await chrome.NavigateAsync("browse://home/events", lifetime.Token);
            Assert.Equal("browse://home/events", admitted.SelectedTab.Address.Value);
            Assert.Equal(BrowseEngineState.NativeApp, admitted.SelectedTab.EngineState);
            Assert.NotNull(events);
            Assert.Equal(activity.Event, Assert.Single(events!.Current!.Items));
            var refusedNewTab = await chrome.NavigateAsync("browse://newtab/events", lifetime.Token);
            Assert.Equal("browse://home/events", refusedNewTab.SelectedTab.Address.Value);
            var refusedNested = await chrome.NavigateAsync("browse://home/events/arbitrary", lifetime.Token);
            Assert.Equal("browse://home/events", refusedNested.SelectedTab.Address.Value);
            Assert.Contains("not registered", refusedNested.Status, StringComparison.Ordinal);
        }
        finally
        {
            if (chrome is not null)
            {
                var preparation = await chrome.PrepareCloseAsync(lifetime.Token);
                Assert.NotNull(preparation);
                await preparation!.BeginClose();
            }
            Directory.Delete(paths.DataDirectory, true);
        }
    }

    [AvaloniaFact]
    public async Task Events_refresh_rechecks_canonical_authorization_and_removes_revoked_rows()
    {
        var testToken = TestContext.Current.CancellationToken;
        var activity = new HomeActivityFixture();
        var page = new HomeEventsDestinationPage(new HomeEventsService([activity], activity), _ => Task.CompletedTask);
        Window? window = null;
        try
        {
            await page.ActivateAsync(testToken);
            window = Show(page);
            Assert.Single(page.Current!.Items);
            activity.Allowed = false;
            await page.DispatchAsync("RefreshEvents", null, testToken);
            SettleFixtureLayout(window);
            Assert.Empty(page.Current!.Items);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), button =>
                CuiRuntimeIdentity.GetStableId(button) is { } identity &&
                identity.Contains("id:home-events-rows|key:", StringComparison.Ordinal) &&
                identity.EndsWith("/id:home-event-row", StringComparison.Ordinal));
            var open = page.GetVisualDescendants().OfType<Button>().Single(button => CuiRuntimeIdentity.GetStableId(button) == "id:events-open-selected");
            Assert.False(open.IsEnabled);
            Assert.False(page.IsActionAvailable("OpenEventObject"));
            Assert.True(await page.TryPrepareToCloseAsync(testToken));
        }
        finally { await page.CloseAndDrainAsync(); window?.Close(); }
    }

    [AvaloniaFact]
    public async Task Home_Events_close_joins_the_actual_pending_source_and_accepts_its_owner_cancellation()
    {
        var source = new HeldHomeActivityFixture();
        var page = new HomeEventsDestinationPage(new HomeEventsService([source], source), _ => Task.CompletedTask);
        var activation = page.ActivateAsync(TestContext.Current.CancellationToken);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var close = page.CloseAndDrainAsync();
        Assert.Same(close, page.CloseAndDrainAsync());
        Assert.False(close.IsCompleted);
        source.Release.SetResult();
        await activation.WaitAsync(TimeSpan.FromSeconds(20));
        await close.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Same(close, page.OriginalClose);
        Assert.True(source.Completed);
        Assert.True(close.IsCompletedSuccessfully);
    }

    private sealed class HeldHomeActivityFixture : IHomeActivitySource, IHomeActivityAuthorization
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Completed;
        public string SourceId => "fixture.held.activity";
        public async Task<IReadOnlyList<HomeActivityEvent>> ReadAsync(HomeEventsQuery query, CancellationToken token)
        {
            Entered.SetResult();
            try { await Release.Task; token.ThrowIfCancellationRequested(); return []; }
            finally { Completed = true; }
        }
        public Task<HomeActivityEvent?> GetByIdAsync(string eventId, CancellationToken token) =>
            throw new InvalidOperationException("This fixture only owns the held read.");
        public Task<bool> CanReadAsync(HomeActivityEvent activity, CancellationToken token) =>
            throw new InvalidOperationException("The held source returns no events.");
    }

    private sealed class HomeActivityFixture : IHomeActivitySource, IHomeActivityAuthorization
    {
        internal bool Allowed = true;
        internal int Reads;
        public string SourceId => "fixture.activity";
        internal HomeActivityEvent Event { get; } = new("durable-event-id", new DateTimeOffset(2026, 10, 24, 1, 0, 0, TimeSpan.Zero),
            "fixture.activity", "ArtifactChanged", "original-actor", "Original actor",
            [new HomeDeepLink(HomeRoute.Library, "files", "canonical-artifact", TargetRouteId: "app.files")], "info", "artifact",
            "An existing canonical artifact changed.");
        public Task<IReadOnlyList<HomeActivityEvent>> ReadAsync(HomeEventsQuery query, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Reads++; return Task.FromResult<IReadOnlyList<HomeActivityEvent>>([Event]); }
        public Task<HomeActivityEvent?> GetByIdAsync(string eventId, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<HomeActivityEvent?>(eventId == Event.EventId ? Event : null); }
        public Task<bool> CanReadAsync(HomeActivityEvent activity, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(Allowed && activity == Event); }
    }

    [AvaloniaFact]
    public async Task Dashboard_rail_opens_a_distinct_native_tab_and_preserves_the_New_Tab_draft()
    {
        using var lifetime = new CancellationTokenSource();
        using var bus = new HavenEventBus();
        var paths = new LayoutPaths();
        var limit = TimeSpan.FromSeconds(20);
        var settings = new EmptySettings();
        BrowseNativeWorkspace? workspace = null;
        Task? originalNavigation = null;
        var home = Factory(new Registry(), lifetime.Token,
            navigation: (intent, settlement) => workspace!.TryQueueNavigationOriginal(intent, settlement, out originalNavigation),
            dashboard: () => new NewDashboardPage(bus, null!, null!, null!, null!, settings,
                new EmptyDashboard(), new EmptyDashboardLayout(), []));
        var chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: home,
            homeProfileId: "dashboard-route-fixture", cancellationToken: lifetime.Token);
        workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new LayoutReadiness());
        try
        {
            RegisterProductionImageResolver(window);
            window.Show();
            await window.InitializeAsync(lifetime.Token).WaitAsync(limit);
            await chrome.NewTabAsync(false, lifetime.Token).WaitAsync(limit);
            await RenderPublishedStateAsync();

            var newTab = chrome.State.SelectedTab;
            Assert.Equal("browse://newtab", newTab.Address.Value);
            Assert.Equal("New Tab", newTab.Title);
            var newTabSession = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(
                chrome.ObserveOriginalAppContent(newTab.Id));
            var newTabPage = Assert.IsType<UnifiedHomePage>(
                Assert.IsType<OriginalTabTransientPresentation>(newTabSession.OriginalNativeView).OriginalContent);
            Assert.True(newTabPage.IsEffectivelyVisible);
            var search = Assert.Single(newTabPage.GetVisualDescendants().OfType<TextBox>(), item => item.Name == "home-search");
            Assert.True(search.IsEffectivelyVisible);
            search.Text = "a search draft";
            Capture("new-tab");

            var dashboard = HomeRailButton(newTabPage, "home-dashboard", "Dashboard");
            var center = dashboard.TranslatePoint(new Rect(dashboard.Bounds.Size).Center, window);
            Assert.NotNull(center);
            window.MouseMove(center.Value);
            window.MouseDown(center.Value, MouseButton.Left);
            window.MouseUp(center.Value, MouseButton.Left);
            await newTabPage.WhenActionsIdleAsync().WaitAsync(limit);
            Assert.NotNull(originalNavigation);
            await originalNavigation!.WaitAsync(limit);
            await RenderPublishedStateAsync();

            var dashboardTab = chrome.State.SelectedTab;
            Assert.Equal("browse://home/dashboard", dashboardTab.Address.Value);
            Assert.Equal("Dashboard", dashboardTab.Title);
            Assert.NotEqual(newTab.Id, dashboardTab.Id);
            Assert.Equal(BrowseEngineState.NativeApp, dashboardTab.EngineState);
            Assert.Same(newTabSession, chrome.ObserveOriginalAppContent(newTab.Id));
            Assert.True(newTabSession.IsPublicationCurrent);
            Assert.Single(window.GetVisualDescendants().OfType<NewDashboardPage>(), page => page.IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(),
                item => item.Name == "home-search" && item.IsEffectivelyVisible);
            Capture("dashboard");

            foreach (var invalid in new[] { "browse://newtab/dashboard", "browse://home/dashboard/nested" })
            {
                var refused = await chrome.NavigateAsync(invalid, lifetime.Token).WaitAsync(limit);
                Assert.Equal(dashboardTab.Id, refused.SelectedTabId);
                Assert.Contains("not registered", refused.Status, StringComparison.Ordinal);
            }
            Assert.Throws<NotSupportedException>(() => home.CreateSession(
                Request("browse://home/dashboard") with { Privacy = BrowserTabPrivacy.Private }));

            await chrome.CloseTabAsync(dashboardTab.Id, lifetime.Token).WaitAsync(limit);
            await RenderPublishedStateAsync();
            Assert.Equal(newTab.Id, chrome.State.SelectedTabId);
            Assert.Same(search, Assert.Single(window.GetVisualDescendants().OfType<TextBox>(),
                item => item.Name == "home-search" && item.IsEffectivelyVisible));
            Assert.Equal("a search draft", search.Text);
            await chrome.ReopenClosedTabAsync(lifetime.Token).WaitAsync(limit);
            await RenderPublishedStateAsync();
            Assert.Equal(dashboardTab.Id, chrome.State.SelectedTabId);
            Assert.Equal(dashboardTab.Address, chrome.State.SelectedTab.Address);
            Assert.Equal("Dashboard", chrome.State.SelectedTab.Title);
            Assert.Same(newTabSession, chrome.ObserveOriginalAppContent(newTab.Id));
            Assert.Single(window.GetVisualDescendants().OfType<NewDashboardPage>(), page => page.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
            if (window.OriginalClosePreparation is { } preparation) Assert.True(await preparation.WaitAsync(limit));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.OriginalClose);
            await window.OriginalClose!.WaitAsync(limit);
            Directory.Delete(paths.DataDirectory, recursive: true);
        }

        async Task RenderPublishedStateAsync()
        {
            await Task.WhenAll(workspace!.OriginalNotifications).WaitAsync(limit);
            SettleFixtureLayout(window);
        }

        void Capture(string route)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_UNIFIED_HOME_CAPTURES"))) return;
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            CaptureOptionalFrame(window, frame, $"dashboard-route-{route}-{frame.PixelSize.Width}x{frame.PixelSize.Height}",
                "Browse-hosted Home route fixture, isolated Dashboard sources");
        }
    }

    [AvaloniaTheory]
    [InlineData(360, 760, 1)]
    [InlineData(430, 760, null)]
    [InlineData(720, 520, 2)]
    [InlineData(1180, 760, 3)]
    public async Task Unavailable_AI_retains_input_does_not_invoke_draft_and_keeps_Search_and_close_usable(
        int width, int height, int? expectedShortcutColumns)
    {
        using var lifetime = new CancellationTokenSource();
        using var bus = new HavenEventBus();
        BrowseNavigationIntent? navigation = null;
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            (intent, _) => { navigation = intent; return true; }, aiAvailable: false,
            dashboard: () => new NewDashboardPage(bus, null!, null!, null!, null!, new EmptySettings(),
                new EmptyDashboard(), new EmptyDashboardLayout(), [])).CreateSession(Request("browse://newtab")));
        await session.InitializeAsync();
        var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView).OriginalContent);
        var window = new Window { Width = width, Height = height, Content = session.OriginalNativeView };
        try
        {
            window.Show();
            Assert.Equal("New Tab", session.Title);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var heading = SettingsControl<TextBlock>(page, "home-newtab-heading");
            Assert.Equal("Home", heading.Text);
            if (width >= 1180)
            {
                var rail = SettingsControl<CuiContainerControl>(page, "home-sidebar");
                Assert.InRange(rail.Bounds.Width, 233, 235);
                Assert.Equal(210, HomeRailButton(page, "home-newtab", "New Tab").Bounds.Width, 1);
            }
            var clock = SettingsControl<TextBlock>(page, "home-newtab-time");
            var date = SettingsControl<TextBlock>(page, "home-newtab-date");
            var now = DateTimeOffset.Now;
            var previousMinute = now.AddMinutes(-1);
            Assert.Contains(clock.Text, new[] { now.ToString("t"), previousMinute.ToString("t") });
            Assert.Contains(date.Text, new[] { now.ToString("D"), previousMinute.ToString("D") });
            var askAi = SettingsControl<Button>(page, "home-newtab-ask-ai");
            var allApps = SettingsControl<Button>(page, "home-newtab-all-apps");
            var help = SettingsControl<TextBlock>(page, "home-newtab-ai-unavailable");
            Assert.False(askAi.IsEnabled);
            Assert.True(help.IsEffectivelyVisible);
            Assert.Equal("Ask AI requires an available Spaces destination.", help.Text);
            Assert.Equal(false, page.IsActionAvailable("home.ask-ai"));
            var search = page.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "home-search");
            var submit = page.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "home-submit");
            Assert.Equal("Go", AutomationProperties.GetName(submit));
            Assert.False(submit.IsEnabled);
            var searchEntry = SettingsControl<CuiContainerControl>(page, "home-search-entry");
            Assert.Equal(68, searchEntry.Bounds.Height, 1);
            Assert.InRange(searchEntry.Bounds.Width, 0, 780.5);
            var searchOrigin = search.TranslatePoint(default, window)!.Value;
            var submitOrigin = submit.TranslatePoint(default, window)!.Value;
            var allAppsOrigin = allApps.TranslatePoint(default, window)!.Value;
            Assert.True(date.TranslatePoint(new Point(0, date.Bounds.Height), window)!.Value.Y <= searchOrigin.Y);
            Assert.True(searchOrigin.X + search.Bounds.Width <= submitOrigin.X + .5);
            Assert.True(searchOrigin.Y + search.Bounds.Height <= allAppsOrigin.Y + .5);
            Assert.True(search.Bounds.Width >= 150);
            var shortcutsHeading = SettingsControl<TextBlock>(page, "home-newtab-shortcuts-heading");
            Assert.Equal("Shortcuts", shortcutsHeading.Text);
            var shortcutsHeadingOrigin = shortcutsHeading.TranslatePoint(default, window)!.Value;
            Assert.True(allAppsOrigin.Y + allApps.Bounds.Height <= shortcutsHeadingOrigin.Y);
            Assert.Equal(heading.TranslatePoint(default, window)!.Value.X, shortcutsHeadingOrigin.X, 1);
            var shortcuts = SettingsControl<CuiContainerControl>(page, "home-newtab-shortcuts");
            var shortcutButtons = shortcuts.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "home-newtab-shortcut").ToArray();
            Assert.Equal(new[] { "Apps", "Library", "Dashboard" }, shortcutButtons.Select(AutomationProperties.GetName));
            var positions = shortcutButtons.Select(button => button.TranslatePoint(default, shortcuts)!.Value).ToArray();
            if (expectedShortcutColumns is { } expected)
                Assert.Equal(expected, positions.GroupBy(position => Math.Round(position.Y)).Max(row => row.Count()));
            Assert.All(shortcutButtons.Zip(positions), pair =>
            {
                Assert.True(pair.First.IsEffectivelyVisible);
                Assert.Equal(width >= 1180 ? 94 : 80, pair.First.Bounds.Height, 1);
                Assert.InRange(pair.Second.X, -.5, shortcuts.Bounds.Width - pair.First.Bounds.Width + .5);
            });
            foreach (var control in new Control[] { search, allApps })
            {
                Assert.True(control.IsEffectivelyVisible && control.Focus());
                var origin = control.TranslatePoint(default, window)!.Value;
                Assert.InRange(origin.X, -.5, width - control.Bounds.Width + .5);
                Assert.InRange(origin.Y, -.5, window.ClientSize.Height - control.Bounds.Height + .5);
            }
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_UNIFIED_HOME_CAPTURES")))
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                CaptureOptionalFrame(window, frame, $"home-newtab-{width}", "UnifiedHome New Tab fixture with unavailable Ask AI");
            }
            search.Text = "charging diagnosis";
            SettleFixtureLayout(window);
            Assert.True(submit.IsEnabled && submit.Focus());
            ClickSettingsButton(window, askAi);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("charging diagnosis", search.Text);
            Assert.Null(navigation);
            ClickSettingsButton(window, submit);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.NotNull(navigation);
            Assert.True(navigation.Address.IsWeb);
            navigation = null;
            Assert.True(search.Focus());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(navigation);
            Assert.True(navigation.Address.IsWeb);

            navigation = null;
            var dashboard = shortcutButtons[^1];
            ScrollViewer? contentScroll = null;
            if (height == 520)
            {
                contentScroll = Assert.IsType<ScrollViewer>(SettingsControl<CuiContainerControl>(page, "home-search-row").Child);
                Assert.True(contentScroll.Extent.Height > contentScroll.Viewport.Height);
                contentScroll.ScrollToEnd();
                SettleFixtureLayout(window);
                Assert.True(contentScroll.Offset.Y > 0);
                var dashboardOrigin = dashboard.TranslatePoint(default, contentScroll)!.Value;
                Assert.InRange(dashboardOrigin.Y, -.5, contentScroll.Viewport.Height - dashboard.Bounds.Height + .5);
            }
            Assert.True(dashboard.Focus());
            PressSettingsKey(window, PhysicalKey.Enter);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.NotNull(navigation);
            Assert.True(navigation.OpenInNewTab);
            Assert.Equal("browse://home/dashboard", navigation.Address.Value);
            Assert.Equal("charging diagnosis", search.Text);

            contentScroll?.ScrollToHome();
            SettleFixtureLayout(window);
            ClickSettingsButton(window, allApps);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            SettleFixtureLayout(window);
            var appsHeading = SettingsControl<TextBlock>(page, "home-heading");
            Assert.True(appsHeading.IsEffectivelyVisible);
            Assert.Equal("Apps", appsHeading.Text);
            Assert.True(SettingsControl<CuiContainerControl>(page, "home-apps-search-row").IsEffectivelyVisible);
            Assert.False(search.IsAttachedToVisualTree() && search.IsEffectivelyVisible);
            await page.DispatchAsync("home.newtab", null);
            SettleFixtureLayout(window);
            Assert.True(search.IsEffectivelyVisible);
            Assert.Equal("charging diagnosis", search.Text);
            Assert.True(await session.TryPrepareToCloseAsync());
        }
        finally { await session.CloseAndDrainAsync(); window.Close(); }
        Assert.True(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task New_Tab_Ask_AI_opens_the_prepared_unsent_Spaces_draft_and_reuses_it_on_Browse_retry()
    {
        using var lifetime = new CancellationTokenSource();
        var limit = TimeSpan.FromSeconds(20);
        var entered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepared = new TaskCompletionSource<AppContentAddress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var route = AppContentAddress.Parse("9to1://spaces/chat/" + Guid.NewGuid().ToString("D"));
        var nextRoute = AppContentAddress.Parse($"9to1://spaces/selected/{Guid.NewGuid():D}/chat/{Guid.NewGuid():D}");
        const string prompt = "  A draft for Spaces  ";
        var navigation = new List<BrowseNavigationIntent>();
        var request = Request("browse://newtab");
        var preparations = 0;
        var canNavigate = false;
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            (intent, _) => { navigation.Add(intent); return canNavigate; },
            prepareAiDraft: (origin, query, token) =>
            {
                Assert.Equal(request, origin);
                preparations++;
                if (query == "Keep this text" && preparations == 3)
                    throw new UnauthorizedAccessException("The test Space ceased to be available.");
                entered.TrySetResult(query);
                return preparations == 1 ? prepared.Task.WaitAsync(token) : Task.FromResult(nextRoute);
            }).CreateSession(request));
        await session.InitializeAsync();
        var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView).OriginalContent);
        var window = Show(session.OriginalNativeView);
        try
        {
            var search = SettingsControl<TextBox>(page, "home-search");
            var askAi = SettingsControl<Button>(page, "home-newtab-ask-ai");
            Assert.False(askAi.IsEnabled);
            search.Text = prompt;
            SettleFixtureLayout(window);
            Assert.True(askAi.IsEnabled);
            Assert.Equal(0, preparations);
            ClickSettingsButton(window, askAi);
            Assert.Equal(prompt, await entered.Task.WaitAsync(limit));
            Assert.False(askAi.IsEnabled);
            await page.DispatchAsync("home.ask-ai", null);
            Assert.Empty(navigation);
            Assert.Equal(1, preparations);
            prepared.TrySetResult(route);
            await page.WhenActionsIdleAsync().WaitAsync(limit);
            Assert.Equal(route, Assert.Single(navigation).Address);
            Assert.Equal(prompt, search.Text);

            canNavigate = true;
            Assert.True(askAi.Focus());
            PressSettingsKey(window, PhysicalKey.Enter);
            await page.WhenActionsIdleAsync().WaitAsync(limit);
            Assert.Equal(1, preparations);
            Assert.Equal(2, navigation.Count);
            Assert.Equal(new BrowseNavigationIntent(request.TabId, route, OpenInNewTab: true), navigation[^1]);
            Assert.Equal(prompt, search.Text);
            ClickSettingsButton(window, askAi);
            await page.WhenActionsIdleAsync().WaitAsync(limit);
            // Admission alone does not retire the source, as with a late Browse mount failure.
            Assert.Equal(1, preparations);
            Assert.Equal(3, navigation.Count);
            Assert.Equal(route, navigation[^1].Address);

            search.Text = "A different draft";
            SettleFixtureLayout(window);
            ClickSettingsButton(window, askAi);
            await page.WhenActionsIdleAsync().WaitAsync(limit);
            Assert.Equal(2, preparations);
            Assert.Equal(4, navigation.Count);
            Assert.Equal(nextRoute, navigation[^1].Address);

            search.Text = "Keep this text";
            SettleFixtureLayout(window);
            ClickSettingsButton(window, askAi);
            await page.WhenActionsIdleAsync().WaitAsync(limit);
            Assert.Equal(4, navigation.Count);
            Assert.Equal("Keep this text", search.Text);
            Assert.Contains("Your text is still here", SettingsControl<TextBlock>(page, "home-status").Text);
            Assert.True(askAi.IsEnabled);
            ClickSettingsButton(window, askAi);
            await page.WhenActionsIdleAsync().WaitAsync(limit);
            Assert.Equal(5, navigation.Count);
            Assert.True(navigation[^1].OpenInNewTab);
        }
        finally
        {
            prepared.TrySetCanceled();
            try { await session.CloseAndDrainAsync(); } finally { window.Close(); }
        }
    }

    [AvaloniaTheory]
    [InlineData("9to1://files")]
    [InlineData("cui://home")]
    [InlineData("browse://history")]
    [InlineData("browse://home/settings")]
    [InlineData("browse://newtab/models")]
    public void Home_factory_does_not_admit_other_namespaces_or_nested_privileged_routes(string route)
    {
        using var lifetime = new CancellationTokenSource();
        Assert.Throws<NotSupportedException>(() => Factory(new(), lifetime.Token).CreateSession(Request(route)));
    }

    [AvaloniaFact]
    public void Home_models_route_is_admitted_only_under_the_home_authority()
    {
        using var lifetime = new CancellationTokenSource();
        // The canonical local-model recovery route (browse://home/models) is admitted, and it
        // resolves to the Home surface.
        var admitted = Factory(new(), lifetime.Token).CreateSession(Request("browse://home/models"));
        Assert.Equal(AppContentNamespace.Browse, admitted.Address.Namespace);
        Assert.Equal("home", admitted.Address.Authority);
        Assert.Equal(new[] { "models" }, admitted.Address.ResourceSegments);
        // The same models segment is NOT admissible under the New Tab authority.
        Assert.Throws<NotSupportedException>(() => Factory(new(), lifetime.Token).CreateSession(Request("browse://newtab/models")));
    }

    [AvaloniaFact]
    public async Task Central_Browse_dispatch_admits_only_browse_home_models_and_refuses_newtab_models_and_arbitrary_segments()
    {
        var testToken = TestContext.Current.CancellationToken;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        var paths = new LayoutPaths();
        var catalogue = new OwnedRecoveryCatalogueFixture();
        BrowseChrome? chrome = null;
        HomeDiscoverPage? discover = null;
        Window? window = null;
        IBrowseNativeAppContentSession? session = null;
        var factory = new RefusalRecordingHomeFactory(Factory(new(), lifetime.Token,
            discover: () => discover = new HomeDiscoverPage(new HomeDiscoverCatalog([catalogue]))));
        try
        {
            // The ACTUAL owning Discover page/catalogue is acquired by the normal Home factory.
            // This fixture reports an explicitly empty local catalogue, not a fabricated model.
            chrome = await BrowseChrome.CreateAsync(paths,
                internalContentFactory: factory,
                homeProfileId: "central-route-profile", cancellationToken: lifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(20), lifetime.Token);

            var admitted = await chrome.NavigateAsync("browse://home/models", lifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(20), lifetime.Token);
            var tab = admitted.SelectedTab;
            Assert.Equal(AppContentNamespace.Browse, tab.Address.Namespace);
            Assert.Equal("home", tab.Address.Authority);
            Assert.Equal(new[] { "models" }, tab.Address.ResourceSegments);
            Assert.Equal(BrowseEngineState.NativeApp, tab.EngineState);
            session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(
                chrome.ObserveOriginalAppContent(admitted.SelectedTabId));
            Assert.NotNull(session);
            Assert.Equal("browse://home/models", session!.Address.Value);
            var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView).OriginalContent);
            window = Show(session.OriginalNativeView);
            Assert.NotNull(discover);
            Assert.Contains(page.GetVisualDescendants().OfType<HomeDiscoverPage>(), item => ReferenceEquals(item, discover));
            Assert.Single(discover!.GetVisualDescendants().OfType<TextBox>(), item => item.Name == "hf-query");
            Assert.Equal('•', Assert.Single(discover.GetVisualDescendants().OfType<TextBox>(), item => item.Name == "hf-token").PasswordChar);
            Assert.True(catalogue.Reads > 0, "The genuine Models page must activate its owned catalogue source.");
            Assert.Equal(0, catalogue.Actions);

            var tabCount = admitted.Tabs.Count;
            var refusedNewTab = await chrome.NavigateAsync("browse://newtab/models", lifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(20), lifetime.Token);
            Assert.Equal("browse://home/models", refusedNewTab.SelectedTab.Address.Value);
            Assert.Equal(tabCount, refusedNewTab.Tabs.Count);
            Assert.Contains("not registered", refusedNewTab.Status, StringComparison.Ordinal);
            // Settings is a recognised Home route, but this composition has no Settings owner.
            // Its genuine factory refusal remains an original failure, rather than an optimistic status.
            var refusal = await Assert.ThrowsAsync<NotSupportedException>(() =>
                chrome.NavigateAsync("browse://home/settings", lifetime.Token)
                    .WaitAsync(TimeSpan.FromSeconds(20), lifetime.Token));
            var refusedNested = chrome.State;
            Assert.Equal("browse://home/models", refusedNested.SelectedTab.Address.Value);
            Assert.Equal(tabCount, refusedNested.Tabs.Count);
            Assert.Same(session, chrome.ObserveOriginalAppContent(refusedNested.SelectedTabId));
            Assert.True(session.IsPublicationCurrent);
            Assert.False(session.CanRetireOriginalView);
            var rejected = Assert.Single(factory.Refusals);
            Assert.Equal("browse://home/settings", rejected.Address.Value);
            Assert.Same(rejected.Failure, refusal);
        }
        finally
        {
            try
            {
                if (chrome is not null)
                {
                    var preparation = await chrome.PrepareCloseAsync(testToken)
                        .WaitAsync(TimeSpan.FromSeconds(20), testToken);
                    Assert.NotNull(preparation);
                    if (factory.Refusals.Count == 0)
                        await preparation!.BeginClose().WaitAsync(TimeSpan.FromSeconds(20), testToken);
                    else
                    {
                        // The real factory refusal is deliberately retained by Browse. Match
                        // exact original causes; any additional cleanup failure still fails.
                        var close = preparation!.BeginClose();
                        var failure = await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(20), testToken));
                        var aggregate = Assert.IsType<AggregateException>(failure);
                        var causes = aggregate.Flatten().InnerExceptions;
                        Assert.NotEmpty(causes);
                        Assert.All(causes, cause => Assert.Contains(factory.Refusals,
                            refusal => ReferenceEquals(refusal.Failure, cause)));
                        Assert.All(factory.Refusals, refusal => Assert.Contains(causes,
                            cause => ReferenceEquals(refusal.Failure, cause)));
                        Assert.True(close.IsFaulted);
                    }
                    if (session is not null) Assert.True(session.CanRetireOriginalView);
                }
            }
            finally { window?.Close(); Directory.Delete(paths.DataDirectory, true); }
        }
    }

    private sealed class OwnedRecoveryCatalogueFixture : IHomeDiscoverCatalogueSource
    {
        public string ProviderId => "fixture-owned-local-catalogue";
        internal int Reads, Actions;
        public Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult(new HomeDiscoverProviderSnapshot(ProviderId, "Fixture-owned empty local catalogue",
                HomeDiscoverConnectionState.Connected, HomeDiscoverSourceState.Available, "fixture-revision-1", []));
        }
        public Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request, CancellationToken cancellationToken)
        {
            Actions++;
            throw new NotSupportedException("This catalogue fixture does not authorize model actions.");
        }
    }

    [AvaloniaFact]
    public async Task Home_uses_current_catalogue_and_retains_exact_initialization_and_retirement()
    {
        using var lifetime = new CancellationTokenSource();
        var source = new Registry(); var admissions = 0;
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(source, lifetime.Token,
            admission: (_, _) => { admissions++; return Task.CompletedTask; }).CreateSession(Request()));
        Assert.Equal(0, source.Reads);
        Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
        var original = session.InitializeAsync();
        Assert.Same(original, session.OriginalInitialization);
        Assert.Same(original, session.InitializeAsync());
        await original;
        var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView).OriginalContent);
        var window = Show(session.OriginalNativeView);
        // The launcher is authored canonical CUI (Repeat Panel="Wrap"), never a donor ListBox, so the
        // same catalogue rule is enforced against the tiles the page actually projects.
        var tiles = page.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Name == "home-app-tile")
            .Select(button => Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()).Text)
            .ToArray();
        Assert.Contains("Files", tiles);
        Assert.Contains("Spaces", tiles);
        Assert.DoesNotContain("Assistants", tiles);
        Assert.DoesNotContain("Dashboard", tiles);
        Assert.Equal(1, source.Reads);
        Assert.Equal(2, admissions);
        Assert.True(await session.TryPrepareToCloseAsync());
        var close = session.CloseAndDrainAsync();
        Assert.Same(close, session.CloseAndDrainAsync());
        Assert.Same(close, session.OriginalClose);
        await close;
        Assert.True(session.CanRetireOriginalView);
        Assert.False(session.IsPublicationCurrent);
        window.Close();
    }

    [AvaloniaTheory(SkipUnless = nameof(LinuxHomeAppearanceAuthoritySupported),
        Skip = "Linux actual local Home/Files component fixture; installed acceptance is separate.")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_Files_tile_double_click_settles_its_Apps_loader_and_opens_the_real_Files_owner(bool fromNewTab)
    {
        await using var rig = await HomeAppearanceAuthorityRig.CreateAsync(includeFiles: true);
        var limit = TimeSpan.FromSeconds(20);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BrowseNativeWorkspace? workspace = null;
        UnifiedHomePage? sourcePage = null;
        Task? navigation = null;
        var accepted = 0; var refused = 0; var actualTileWasPending = false;
        var files = FilesBrowseAppContentFactory.BindOriginalSameProcess(rig.Provider, rig.Home,
            rig.AppLifetime.Token, rig.WindowLifetime.Token);
        var home = Factory(new(), rig.AppLifetime.Token, navigation: (intent, settlement) =>
        {
            actualTileWasPending |= sourcePage?.WhenActionsIdleAsync().IsCompleted == false;
            var admitted = workspace!.TryQueueNavigationOriginal(intent, Task.WhenAll(settlement, release.Task), out var original);
            if (admitted) { accepted++; navigation = original; }
            else refused++;
            return admitted;
        }, admission: async (_, token) => { Assert.True(await rig.ObserveAsync(token)); });
        var chrome = await BrowseChrome.CreateAsync(rig.Paths, appContentFactory: files,
            internalContentFactory: home, homeProfileId: rig.Actor.ProfileId);
        if (fromNewTab) await chrome.NewTabAsync(false);
        var sourceId = chrome.State.SelectedTabId;
        var source = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(chrome.ObserveOriginalAppContent(sourceId));
        workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new LayoutReadiness());
        try
        {
            RegisterProductionImageResolver(window);
            window.Show(); await window.InitializeAsync().WaitAsync(limit);
            sourcePage = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(source.OriginalNativeView).OriginalContent);
            if (fromNewTab)
            {
                HomeRailButton(sourcePage, "home-apps", "9to1 Apps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await sourcePage.WhenActionsIdleAsync().WaitAsync(limit);
            }
            SettleFixtureLayout(window);
            var tile = Assert.Single(sourcePage.GetVisualDescendants().OfType<Button>(),
                item => item.Name == "home-app-tile" && AutomationProperties.GetName(item) == "Open Files");
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await sourcePage.WhenActionsIdleAsync().WaitAsync(limit);
            Assert.Equal(1, accepted); Assert.Equal(1, refused);
            Assert.True(actualTileWasPending, "The sender settlement must include the actual Apps CUI action.");
            Assert.Contains(sourcePage.GetVisualDescendants().OfType<TextBlock>(),
                item => item.Text?.Contains("Finish the current browser action", StringComparison.Ordinal) == true);
            Assert.Equal(sourceId, chrome.State.SelectedTabId);
            Assert.True(source.IsPublicationCurrent);
            release.TrySetResult();
            Assert.NotNull(navigation); await navigation!.WaitAsync(limit);
            await Task.WhenAll(workspace.OriginalNotifications).WaitAsync(limit);
            SettleFixtureLayout(window);
            Assert.Equal("files", chrome.State.SelectedTab.Address.Authority);
            Assert.Equal("Files", chrome.State.SelectedTab.Title);
            Assert.Equal(BrowseEngineState.NativeApp, chrome.State.SelectedTab.EngineState);
            var actualFiles = Assert.IsType<FilesBrowseAppContentSession>(chrome.ObserveOriginalAppContent(chrome.State.SelectedTabId));
            Assert.IsType<Haven.Desktop.Views.Pages.Files.FilesWorkspaceSetupPage>(actualFiles.OriginalNativeView);
            Assert.True(actualFiles.IsPublicationCurrent);
            if (fromNewTab) { Assert.Equal(sourceId, chrome.State.SelectedTabId); Assert.True(source.CanRetireOriginalView); }
            else { Assert.NotEqual(sourceId, chrome.State.SelectedTabId); Assert.True(source.IsPublicationCurrent); }
        }
        finally
        {
            release.TrySetResult();
            if (navigation is not null) await navigation.WaitAsync(limit);
            window.Close();
            if (window.OriginalClosePreparation is { } preparation) Assert.True(await preparation.WaitAsync(limit));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.OriginalClose); await window.OriginalClose!.WaitAsync(limit);
        }
        Assert.Null(rig.Home.OriginalCloseTask);
    }

    [AvaloniaFact]
    public async Task Actual_App_tile_callback_exception_stays_a_retained_failure_instead_of_a_retryable_refusal()
    {
        using var lifetime = new CancellationTokenSource();
        var expected = new IOException("The actual host navigation callback failed.");
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            navigation: (_, _) => throw expected).CreateSession(Request()));
        await session.InitializeAsync();
        var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView).OriginalContent);
        var window = Show(session.OriginalNativeView);
        try
        {
            var tile = Assert.Single(page.GetVisualDescendants().OfType<Button>(),
                item => item.Name == "home-app-tile" && AutomationProperties.GetName(item) == "Open Files");
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var failure = await Record.ExceptionAsync(() => page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.NotNull(failure); Assert.True(HomeAppearanceAuthorityRig.HasExactCause(failure!, expected));
            var close = await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.NotNull(close); Assert.True(HomeAppearanceAuthorityRig.HasExactCause(close!, expected));
            Assert.False(session.CanRetireOriginalView);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Authored_Apps_sidebar_button_dispatches_through_its_actual_CUI_loader()
    {
        using var lifetime = new CancellationTokenSource();
        var source = new Registry();
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(source, lifetime.Token).CreateSession(Request()));
        await session.InitializeAsync();
        var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView).OriginalContent);
        var window = Show(session.OriginalNativeView);
        var button = HomeRailButton(page, "home-apps", "9to1 Apps");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await page.WhenActionsIdleAsync();
        Assert.Equal(2, source.Reads);
        await session.CloseAndDrainAsync();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Close_cancels_original_admission_before_joining_it_and_retains_its_cancellation()
    {
        using var lifetime = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Registry();
        var session = Factory(source, lifetime.Token, admission: async (_, token) =>
        {
            entered.SetResult(); await Task.Delay(Timeout.Infinite, token);
        }).CreateSession(Request());
        var original = session.InitializeAsync();
        await entered.Task;
        var close = session.CloseAndDrainAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(close.IsCompleted);
        Assert.False(close.IsCompletedSuccessfully);
        Assert.True(original.IsCompleted);
        Assert.Equal(0, source.Reads);
        Assert.False(session.IsPublicationCurrent);
        Assert.Same(close, session.CloseAndDrainAsync());
    }

    [AvaloniaFact]
    public async Task Restored_execution_context_cannot_join_the_initialization_callback_owning_it()
    {
        using var lifetime = new CancellationTokenSource();
        var capturedContext = ExecutionContext.Capture()!;
        var session = Factory(new(), lifetime.Token).CreateSession(Request());
        var refused = false;
        session.StateChanged += (_, _) => ExecutionContext.Run(capturedContext, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = session.CloseAndDrainAsync(); }); refused = true;
        }, null);
        await session.InitializeAsync();
        Assert.True(refused);
        await session.CloseAndDrainAsync();
    }

    [AvaloniaTheory]
    [InlineData(360, 844, CuiAppearance.SuperBright)]
    [InlineData(360, 844, CuiAppearance.Bright)]
    [InlineData(360, 844, CuiAppearance.Dark)]
    [InlineData(360, 844, CuiAppearance.SuperDark)]
    [InlineData(390, 844, CuiAppearance.SuperBright)]
    [InlineData(390, 844, CuiAppearance.Bright)]
    [InlineData(390, 844, CuiAppearance.Dark)]
    [InlineData(390, 844, CuiAppearance.SuperDark)]
    [InlineData(1280, 900, CuiAppearance.SuperBright)]
    [InlineData(1280, 900, CuiAppearance.Bright)]
    [InlineData(1280, 900, CuiAppearance.Dark)]
    [InlineData(1280, 900, CuiAppearance.SuperDark)]
    public async Task Shared_Home_renders_canonical_apps_and_keeps_controls_inside_the_window(int width, int height, CuiAppearance appearance)
    {
        var application = global::Avalonia.Application.Current!;
        var previousResources = application.Resources;
        var previousVariant = application.RequestedThemeVariant;
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        using var lifetime = new CancellationTokenSource();
        IBrowseNativeAppContentSession? session = null;
        Window? window = null;
        Task? initialization = null, close = null;
        Task? searchActions = null;
        var failures = new List<Exception>();
        try
        {
            // Use the production primitive snapshot inside a fresh owned dictionary.
            // Never merge mutable original brushes, publish a global palette event, or
            // rewrite the previous custom/accessibility/accent projection.
            var fixtureResources = new ResourceDictionary();
            fixtureResources.MergedDictionaries.Add(CuiSceneVisualResources.Create("Home", appearance));
            application.Resources = fixtureResources;
            application.RequestedThemeVariant = CuiSceneVisualResources.Variant(appearance);
            session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token).CreateSession(Request()));
            initialization = session.InitializeAsync();
            await initialization;
            var page = session.OriginalNativeView;
            window = new Window { Width = width, Height = height, Content = page };
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(width, frame.PixelSize.Width); Assert.Equal(height, frame.PixelSize.Height);
            // Preserve the actual frame/mode before checking the requested appearance.
            CaptureOptionalFrame(window, frame, $"home-apps-{width}-{appearance}", "UnifiedHome Apps layout fixture", appearance);
            Assert.Equal(appearance, CuiThemeScopeApplier.DetectAppearance());
            foreach (var (id, label) in new[] { ("home-apps", "9to1 Apps"), ("home-models", "Models"), ("home-newtab", "New Tab") })
            {
                var button = HomeRailButton(page, id, label);
                Assert.True(button.Bounds.Width >= 44 && button.Bounds.Height >= 24);
                var origin = button.TranslatePoint(default, window);
                Assert.NotNull(origin);
                Assert.InRange(origin.Value.X, -1, width - 43);
                Assert.InRange(origin.Value.Y, -1, height - 23);
                Assert.True(origin.Value.X + button.Bounds.Width <= width + 1);
            }
            if (width is 360 or 390 or 1280)
            {
                // Real canonical Apps controls in the original owning page, including the query-visible Clear button.
                var home = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(page).OriginalContent);
                var searchRow = Assert.Single(page.GetVisualDescendants().OfType<CuiContainerControl>(),
                    control => control.Name == "home-apps-search-row");
                var search = Assert.Single(page.GetVisualDescendants().OfType<TextBox>(),
                    control => control.Name == "home-app-search");
                // Hidden CUI children retain their controls but leave the visual tree.
                // Resolve Clear only after the genuine query makes it visible.
                Button? clear = null;
                var count = Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(),
                    control => control.Name == "home-apps-count");
                Assert.Equal("Search apps", AutomationProperties.GetName(search));
                Assert.Equal("Installed app count", AutomationProperties.GetName(count));
                Assert.Equal("19 apps", count.Text);
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(),
                    control => control.Name == "home-apps-clear");
                AssertSearchGeometry();
                Assert.True(search.Focus(), "The narrow app search must remain keyboard-focusable.");
                search.Text = "Boards";
                // Home's action drain includes the nested Apps loader as well as the rail loader.
                searchActions = home.WhenActionsIdleAsync(); await searchActions;
                SearchLayout();
                Assert.Equal("Boards", search.Text);
                clear = Assert.Single(page.GetVisualDescendants().OfType<Button>(),
                    control => control.Name == "home-apps-clear");
                Assert.True(clear.IsEffectivelyVisible);
                Assert.True(clear.IsEnabled);
                Assert.Equal("Clear app search", AutomationProperties.GetName(clear));
                Assert.True(clear.Focus(), "The query-visible Clear action must remain keyboard-focusable.");
                Assert.Equal("19 apps", count.Text);
                // Search includes descriptions: Canvas mentions whiteboards/study boards.
                // Both genuine catalogue matches must remain, with exactly one Boards tile.
                var filteredTiles = page.GetVisualDescendants().OfType<Button>()
                    .Where(control => control.Name == "home-app-tile" && control.IsEffectivelyVisible).ToArray();
                Assert.Equal(new[] { "Boards", "Canvas" }, filteredTiles.Select(tile =>
                    Assert.Single(tile.GetVisualDescendants().OfType<TextBlock>()).Text)
                    .OrderBy(name => name, StringComparer.Ordinal));
                var filteredTile = Assert.Single(filteredTiles,
                    control => AutomationProperties.GetName(control) == "Open Boards");
                Assert.Equal("Boards", Assert.Single(filteredTile.GetVisualDescendants().OfType<TextBlock>()).Text);
                AssertSearchGeometry();
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                searchActions = home.WhenActionsIdleAsync(); await searchActions;
                SearchLayout();
                Assert.Equal(string.Empty, search.Text);
                Assert.False(clear.IsEffectivelyVisible);
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(),
                    control => control.Name == "home-apps-clear");
                Assert.Equal("19 apps", count.Text);
                Assert.Same(search, Assert.Single(page.GetVisualDescendants().OfType<TextBox>(),
                    control => control.Name == "home-app-search"));
                Assert.True(page.GetVisualDescendants().OfType<Button>()
                    .Count(control => control.Name == "home-app-tile" && control.IsEffectivelyVisible) > 1);
                AssertSearchGeometry();

                void SearchLayout()
                {
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                }
                void AssertSearchGeometry()
                {
                    var visible = clear is not null && clear.IsEffectivelyVisible
                        ? new Control[] { search, clear, count } : new Control[] { search, count };
                    var rowOrigin = searchRow.TranslatePoint(default, window);
                    Assert.NotNull(rowOrigin);
                    Assert.True(rowOrigin.Value.X >= -.5 && rowOrigin.Value.X + searchRow.Bounds.Width <= width + .5);
                    foreach (var control in visible)
                    {
                        Assert.True(control.IsEffectivelyVisible);
                        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
                        var insideRow = control.TranslatePoint(default, searchRow);
                        var insideWindow = control.TranslatePoint(default, window);
                        Assert.NotNull(insideRow); Assert.NotNull(insideWindow);
                        Assert.True(insideRow.Value.X >= -.5 && insideRow.Value.X + control.Bounds.Width <= searchRow.Bounds.Width + .5,
                            $"{control.Name} exceeds the Apps search row: X={insideRow.Value.X}, Width={control.Bounds.Width}, RowWidth={searchRow.Bounds.Width}.");
                        Assert.True(insideRow.Value.Y >= -.5 && insideRow.Value.Y + control.Bounds.Height <= searchRow.Bounds.Height + .5);
                        Assert.True(insideWindow.Value.X >= -.5 && insideWindow.Value.X + control.Bounds.Width <= width + .5);
                        Assert.True(insideWindow.Value.Y >= -.5 && insideWindow.Value.Y + control.Bounds.Height <= height + .5);
                    }
                    Assert.True(search.Bounds.Width >= 160 && search.Bounds.Width <= 480);
                    Assert.True(count.DesiredSize.Width <= count.Bounds.Width + .5, "The full installed-app count must remain readable.");
                    var searchOrigin = search.TranslatePoint(default, searchRow)!.Value;
                    var countOrigin = count.TranslatePoint(default, searchRow)!.Value;
                    Assert.True(searchOrigin.X <= .5, "App search must stay at the left edge on desktop as well as narrow viewports.");
                    Assert.True(searchRow.Bounds.Width <= 520.5);
                    Assert.True(searchOrigin.Y + search.Bounds.Height <= countOrigin.Y + .5,
                        "The full installed-app count must occupy a separate row below search.");
                    if (clear is not null && clear.IsEffectivelyVisible)
                    {
                        var clearOrigin = clear.TranslatePoint(default, searchRow)!.Value;
                        Assert.True(searchOrigin.X + search.Bounds.Width <= clearOrigin.X + .5);
                        Assert.InRange(clearOrigin.X - searchOrigin.X - search.Bounds.Width, -.5, 8.5);
                        Assert.True(clearOrigin.Y + clear.Bounds.Height <= countOrigin.Y + .5);
                    }
                }
            }
        }
        catch (Exception cause) { Retain(cause); RetainTask(initialization); RetainTask(searchActions); }
        finally
        {
            if (session is not null)
            {
                try { close = session.CloseAndDrainAsync(); await close; }
                catch (Exception cause) { Retain(cause); RetainTask(close); }
            }
            if (window is not null && session?.CanRetireOriginalView == true)
            {
                try { window.Content = null; window.Close(); }
                catch (Exception cause) { Retain(cause); }
            }
            // The supported Application.Resources setter restores the SAME dictionary
            // and owner, including key absence, merged providers and mutable brush identities.
            try { application.Resources = previousResources; }
            catch (Exception cause) { Retain(cause); }
            try { application.RequestedThemeVariant = previousVariant; }
            catch (Exception cause) { Retain(cause); }
            try
            {
                Assert.Same(previousResources, application.Resources);
                Assert.Same(application, ((IResourceProvider)previousResources).Owner);
                Assert.Equal(previousVariant, application.RequestedThemeVariant);
                Assert.Equal(previousTheme, CuiSurfacePaletteCatalog.ActiveTheme);
            }
            catch (Exception cause) { Retain(cause); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Home layout assertion and independent original cleanup/theme restoration failed.", failures);
        void Retain(Exception cause)
        {
            if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
        }
        void RetainTask(Task? source)
        {
            if (source?.Exception is { } original)
                foreach (var cause in original.InnerExceptions) Retain(cause);
        }
    }

    [AvaloniaTheory]
    [InlineData(1440, 920)]
    [InlineData(1280, 520)]
    [InlineData(390, 844)]
    public async Task Browse_hosted_Home_sidebar_scrolls_without_spilling_into_main_content(int width, int height)
    {
        using var lifetime = new CancellationTokenSource();
        var paths = new LayoutPaths();
        var chrome = await BrowseChrome.CreateAsync(paths,
            internalContentFactory: Factory(new(), lifetime.Token), homeProfileId: "layout-fixture-profile");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new LayoutReadiness());
        window.Width = width; window.Height = height;
        try
        {
            // The real host registers this SAME resolver on the exact scene before Show.
            // Keep renderer/readiness unchanged; failures still reach the original close/drain.
            RegisterProductionImageResolver(window);
            window.Show(); await window.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(20)); Layout();
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            CaptureOptionalFrame(window, frame, $"browse-home-{width}x{height}", "Browse/Home layout fixture; explicit fixture readiness");
            var page = Assert.Single(window.GetVisualDescendants().OfType<UnifiedHomePage>());
            var sidebar = Assert.IsType<CuiContainerControl>(page.GetVisualDescendants().OfType<Control>()
                .Single(item => item.Name == "home-sidebar"));
            var main = page.GetVisualDescendants().OfType<Control>().Single(item => item.Name == "home-main");
            var sideOrigin = sidebar.TranslatePoint(default, window)!.Value;
            var mainOrigin = main.TranslatePoint(default, window)!.Value;
            // No Assistants owner is composed in this fixture. Existing destinations keep one scroll column.
            var nav = Assert.IsType<CuiContainerControl>(page.GetVisualDescendants().OfType<Control>()
                .Single(item => item.Name == "home-rail-nav"));
            var footer = Assert.IsType<CuiContainerControl>(page.GetVisualDescendants().OfType<Control>()
                .Single(item => item.Name == "home-rail-footer"));
            var scroll = Assert.IsType<ScrollViewer>(nav.Child);
            var buttons = nav.AuthoredChildren.OfType<Button>().Where(button => button.IsEffectivelyVisible && button.IsAttachedToVisualTree()).ToArray();
            Assert.Equal(7, buttons.Length);
            Assert.Equal(new[] { "home-dashboard", "home-newtab", "home-library", "home-events", "home-apps", "home-models", "home-mesh" },
                buttons.Select(button => button.Name));
            Assert.Equal(new[] { "home-account", "home-settings" }, footer.AuthoredChildren.OfType<Button>().Select(button => button.Name));
            var settings = HomeRailButton(page, "home-settings", "Settings");
            var brand = HomeRailButton(page, "home-brand", "Home");
            Assert.NotNull(Assert.Single(brand.GetVisualDescendants().OfType<Image>()).Source);
            var groupHeadings = nav.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Name is "home-rail-explore" or "home-rail-workspace" or "home-rail-discover")
                .Select(text => text.Text);
            if (width >= 840) Assert.Equal(new[] { "Explore", "Workspace", "Discover" }, groupHeadings);
            else Assert.Empty(groupHeadings);
            Assert.Equal(global::Avalonia.Layout.Orientation.Vertical, Assert.IsType<StackPanel>(nav.Layout).Orientation);
            Assert.True(sideOrigin.X + sidebar.Bounds.Width <= mainOrigin.X + .5);
            double previousBottom = -1;
            foreach (var button in buttons)
            {
                var origin = button.TranslatePoint(default, nav.Layout)!.Value;
                Assert.True(origin.Y >= previousBottom, "The icon rail wrapped into a second column.");
                Assert.True(origin.X >= 0 && origin.X + button.Bounds.Width <= nav.Bounds.Width + .5,
                    $"{button.Name}: X={origin.X}, Width={button.Bounds.Width}, NavigationWidth={nav.Bounds.Width}, ViewportWidth={scroll.Viewport.Width}.");
                previousBottom = origin.Y + button.Bounds.Height;
                var label = Assert.IsType<string>(AutomationProperties.GetName(button));
                var visibleLabels = button.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && text.Text == label);
                if (width >= 840)
                    Assert.True(Assert.Single(visibleLabels).Bounds.Width > 0);
                else
                    Assert.Empty(visibleLabels);
                var icon = Assert.Single(button.GetVisualDescendants().OfType<Image>());
                Assert.NotNull(icon.Source);
                Assert.True(icon.Bounds.Width > 0 && icon.Bounds.Height > 0);
            }
            Assert.True(main.Bounds.Height >= height - 300, $"Apps has only {main.Bounds.Height} vertical space.");
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + .5, "Sidebar content spills horizontally.");
            var settingsOrigin = settings.TranslatePoint(default, window)!.Value;
            Assert.True(settingsOrigin.Y >= sideOrigin.Y);
            Assert.True(settingsOrigin.Y + settings.Bounds.Height <= sideOrigin.Y + sidebar.Bounds.Height + .5);
            if (scroll.Extent.Height > scroll.Viewport.Height + .5)
            {
                scroll.Offset = new Vector(0, scroll.Extent.Height); Layout();
                Assert.True(scroll.Offset.Y > 0);
                var last = HomeRailButton(page, "home-mesh", "Mesh");
                var lastOrigin = last.TranslatePoint(default, scroll)!.Value;
                Assert.True(lastOrigin.Y >= -.5);
                Assert.True(lastOrigin.Y + last.Bounds.Height <= scroll.Viewport.Height + .5);
                Assert.Equal(settingsOrigin, settings.TranslatePoint(default, window)!.Value);
            }
            var appRegion = Assert.Single(page.GetVisualDescendants().OfType<Control>(),
                item => item.Name == "home-apps-results");
            Assert.True(appRegion.Bounds.Width >= 150 && appRegion.Bounds.Height >= 180);
            var selectedId = chrome.State.SelectedTabId;
            scroll.Offset = default; Layout();
            var apps = buttons.Single(item => item.Name == "home-apps");
            apps.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(selectedId, chrome.State.SelectedTabId);
            Assert.Same(page, Assert.Single(window.GetVisualDescendants().OfType<UnifiedHomePage>()));
            // Preserve the existing 44px requirement; authored40px targets may still FAIL.
            // A captured failing frame is evidence, never a waived product-quality assertion.
            foreach (var button in buttons.Append(settings))
                Assert.True(button.Bounds.Width >= 44 && button.Bounds.Height >= 24);

        }
        finally
        {
            window.Close();
            if (window.OriginalClosePreparation is { } preparation)
                Assert.True(await preparation.WaitAsync(TimeSpan.FromSeconds(20)));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.OriginalClose);
            await window.OriginalClose!.WaitAsync(TimeSpan.FromSeconds(20));
            Directory.Delete(paths.DataDirectory, true);
        }
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaFact]
    public async Task Shared_Home_rail_scrolls_in_a_short_component_viewport_and_keeps_Settings_pinned()
    {
        // Stress the SAME canonical Home component below Browse's 520px shipping window minimum.
        // This is component overflow coverage, not acceptance of a below-minimum browser window.
        using var lifetime = new CancellationTokenSource();
        IBrowseNativeAppContentSession? session = null;
        Window? window = null;
        var failures = new List<Exception>();
        Task? initialization = null, close = null;
        try
        {
            session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token).CreateSession(Request()));
            initialization = session.InitializeAsync();
            await initialization;
            var page = session.OriginalNativeView;
            // Retain the acquired window before setting properties or attaching the owned view.
            window = new Window(); window.Width = 390; window.Height = 320; window.Content = page;
            window.Show(); Layout();
            var nav = Assert.IsType<CuiContainerControl>(page.GetVisualDescendants().OfType<Control>()
                .Single(item => item.Name == "home-rail-nav"));
            var scroll = Assert.IsType<ScrollViewer>(nav.Child);
            var settings = HomeRailButton(page, "home-settings", "Settings");
            var pinnedOrigin = settings.TranslatePoint(default, window)!.Value;
            Assert.True(scroll.Viewport.Height > 0);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "Short real Home viewport must exercise rail overflow.");
            scroll.Offset = new Vector(0, scroll.Extent.Height); Layout();
            Assert.True(scroll.Offset.Y > 0);
            var last = HomeRailButton(page, "home-mesh", "Mesh");
            var lastOrigin = last.TranslatePoint(default, scroll)!.Value;
            Assert.True(lastOrigin.Y >= -.5);
            Assert.True(lastOrigin.Y + last.Bounds.Height <= scroll.Viewport.Height + .5);
            Assert.Equal(pinnedOrigin, settings.TranslatePoint(default, window)!.Value);
        }
        catch (Exception cause) { Retain(cause); }
        finally
        {
            var drained = false;
            if (session is not null)
            {
                try
                {
                    close = session.CloseAndDrainAsync();
                    await close;
                    Assert.True(session.CanRetireOriginalView);
                    drained = true;
                }
                catch (Exception cause) { Retain(cause); }
            }
            // A failed original drain retains its mounted view; never detach or close around it.
            if (window is not null && drained)
            {
                try { window.Content = null; } catch (Exception cause) { Retain(cause); }
                try { window.Close(); } catch (Exception cause) { Retain(cause); }
            }
            // Await may expose only one fault. Preserve raw direct siblings on the exact tasks.
            RetainTask(initialization); RetainTask(close);
            if (session is not null) { RetainTask(session.OriginalInitialization); RetainTask(session.OriginalClose); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Short Home layout assertion and independent original cleanup failed.", failures);
        void Retain(Exception cause)
        {
            if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
        }
        void RetainTask(Task? source)
        {
            if (source?.Exception is { } original)
                foreach (var cause in original.InnerExceptions) Retain(cause);
        }
        void Layout() { window!.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaTheory]
    [InlineData(CuiAppearance.SuperBright)]
    [InlineData(CuiAppearance.Bright)]
    [InlineData(CuiAppearance.Dark)]
    [InlineData(CuiAppearance.SuperDark)]
    public void Theme_detection_reads_merged_bootstrap_and_preserves_direct_preference_precedence(CuiAppearance appearance)
    {
        var application = global::Avalonia.Application.Current!;
        var resources = application.Resources;
        var hadDirect = resources.TryGetValue("CuiAppearance", out var previousDirect);
        var snapshot = CuiSceneVisualResources.Create("Home", appearance);
        var failures = new List<Exception>();
        try
        {
            resources.Remove("CuiAppearance");
            resources.MergedDictionaries.Add(snapshot);
            // Exact primitive-bootstrap provider shape, without a direct preference shadowing it.
            Assert.Equal(appearance, CuiThemeScopeApplier.DetectAppearance());
            var direct = appearance == CuiAppearance.Bright ? CuiAppearance.SuperDark : CuiAppearance.Bright;
            resources["CuiAppearance"] = direct.ToString();
            Assert.Equal(direct, CuiThemeScopeApplier.DetectAppearance());
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            try { resources.MergedDictionaries.Remove(snapshot); }
            catch (Exception cause) { failures.Add(cause); }
            try
            {
                if (hadDirect) resources["CuiAppearance"] = previousDirect;
                else resources.Remove("CuiAppearance");
            }
            catch (Exception cause) { failures.Add(cause); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Theme lookup assertion and independent resource restoration failed.", failures);
    }

    private static Button HomeRailButton(Control page, string id, string label)
    {
        var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(), item => item.Name == id);
        Assert.Equal(label, AutomationProperties.GetName(button));
        Assert.Equal(label, ToolTip.GetTip(button));
        Assert.True(button.IsEffectivelyVisible);
        return button;
    }

    private static void RegisterProductionImageResolver(BrowseNativeWindow window)
    {
        // Same retained scene seam used by existing BrowseWorkspaceLayoutTests.
        var scene = typeof(BrowseNativeWindow).GetField("_originalScene", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window) as CuiNativeScene;
        Assert.NotNull(scene);
        scene!.ControlRegistry!.RegisterImageSourceResolver(HavenPackagedSvgResolver.Resolve);
    }

    private static readonly object CaptureGate = new();
    private static int CaptureCount;
    private static long CaptureBytes;
    private static void CaptureOptionalFrame(Window window, Bitmap frame, string name, string fixture, CuiAppearance? requestedAppearance = null)
    {
        var directory = Environment.GetEnvironmentVariable("HAVEN_UNIFIED_HOME_CAPTURES");
        if (string.IsNullOrWhiteSpace(directory)) return;
        lock (CaptureGate)
        {
            try
            {
                const int maximumBytes = 1024 * 1024;
                var source = Environment.GetEnvironmentVariable("HAVEN_CAPTURE_SOURCE_REVISION");
                var build = Environment.GetEnvironmentVariable("HAVEN_CAPTURE_BUILD_REVISION");
                if (CaptureCount >= 6 || CaptureBytes >= maximumBytes || !IsRevision(source) || !IsRevision(build))
                    throw new InvalidOperationException("NO_IMAGE: capture allowance or exact runner source/build identity unavailable.");
                // Fixed capacity refuses oversized encoding BEFORE any file write.
                using var encoded = new MemoryStream(new byte[maximumBytes], writable: true);
                encoded.SetLength(0); frame.Save(encoded);
                var png = encoded.ToArray();
                if (CaptureBytes + png.Length > maximumBytes) throw new IOException("NO_IMAGE: PNG run allowance exceeded.");
                var stem = name + "-" + Guid.NewGuid().ToString("N");
                var pngPath = Path.Combine(directory, stem + ".png");
                var metadata = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    captured_utc = DateTimeOffset.UtcNow, source_revision = source, build_revision = build,
                    fixture, renderer = "headless-Skia", installed_native = false,
                    requested_appearance = requestedAppearance?.ToString(),
                    appearance = CuiThemeScopeApplier.DetectAppearance().ToString(),
                    viewport = new { window.ClientSize.Width, window.ClientSize.Height },
                    pixels = new { frame.PixelSize.Width, frame.PixelSize.Height },
                    outcome = "PRE_ASSERTION_FRAME; owning TRX and original cleanup determine result",
                    png = new { path = pngPath, bytes = png.Length, sha256 = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant() },
                    assemblies = new[] { typeof(UnifiedHomeContentTests).Assembly, typeof(UnifiedHomePage).Assembly,
                        typeof(CuiSceneHost).Assembly, typeof(BrowseNativeWorkspace).Assembly }.Distinct().Select(assembly => new
                        {
                            name = assembly.GetName().Name, path = assembly.Location,
                            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant()
                        }).ToArray()
                });
                foreach (var location in new[] { "/workspace", Path.GetTempPath(), directory }.Where(Directory.Exists))
                {
                    // Longest actual mount prefix also checks the separate cloud /tmp volume.
                    var full = Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var drive = DriveInfo.GetDrives().Where(item => full.StartsWith(item.Name, StringComparison.Ordinal))
                        .OrderByDescending(item => item.Name.Length).First();
                    if (drive.AvailableFreeSpace < 268435456L + png.Length + metadata.Length + 8192)
                        throw new IOException("NO_IMAGE: disk floor would be crossed.");
                }
                Directory.CreateDirectory(directory);
                CaptureCount++; CaptureBytes += png.Length;
                using (var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write)) output.Write(png);
                using (var output = new FileStream(Path.Combine(directory, stem + ".json"), FileMode.CreateNew, FileAccess.Write))
                    output.Write(metadata);
                Console.WriteLine("PRE_ASSERTION_CAPTURE: " + pngPath);
            }
            catch (Exception failure)
            {
                // Optional capture cannot suppress/replace genuine assertions or original drain faults.
                Console.WriteLine("OPTIONAL_CAPTURE_FAILED_OR_REFUSED: " + failure);
            }
        }
        static bool IsRevision(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);
    }

    // The New Tab fixture renders the real dashboard with empty, local repositories.
    private sealed class EmptyDashboard : IDashboardRepository
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken token) =>
            Task.FromResult(new DashboardSnapshot(now, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, [], []));
    }
    private sealed class EmptyDashboardLayout : IDashboardLayoutRepository
    {
        public Task<IReadOnlyList<DashboardTileLayout>> GetAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<DashboardTileLayout>>([]);
        public Task SaveAsync(IReadOnlyList<DashboardTileLayout> layout, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class EmptySettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => Task.FromResult(_values.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class LayoutReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "home-layout-fixture", "Explicit headless layout fixture"));
    }
    private sealed class ThemeProjectionPaths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }

    private sealed class LayoutPaths : IAppPaths
    {
        public LayoutPaths() { Directory.CreateDirectory(DataDirectory); }
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "home-layout-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "fixture.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
    }

    private static void SettleFixtureLayout(Window window)
    {
        // Domain/loader action completion does not perform Avalonia's template/layout pass.
        // Keep assertions on the actual mounted visual tree after deferred presenters settle.
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private sealed class RefusalRecordingHomeFactory(IBrowseInternalContentFactory original) : IBrowseInternalContentFactory
    {
        internal List<(AppContentAddress Address, Exception Failure)> Refusals { get; } = [];
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
        {
            try { return original.CreateSession(request); }
            catch (Exception failure)
            {
                Refusals.Add((request.Address, failure));
                throw;
            }
        }
    }

    private static Window Show(Control page)
    {
        var window = new Window { Width = 1280, Height = 900, Content = page };
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); return window;
    }

    private sealed class Registry : IModeRegistry
    {
        internal int Reads;
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Reads++; return Task.FromResult(BuiltInModeSeed.Modes); }
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) => Task.FromResult(BuiltInModeSeed.Modes.SingleOrDefault(mode => mode.Id == id));
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) => Task.FromResult(BuiltInModeSeed.Modes.SingleOrDefault(mode => mode.Key == key));
        public Task UpsertModeAsync(ModeDefinition mode, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) => throw new NotSupportedException();
    }
}
