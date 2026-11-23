using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaFact]
    public async Task Saved_interface_scale_reopens_real_Home_and_New_Tab_in_two_isolated_profiles_without_writing()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = lifetime.Token;
        var profiles = new[]
        {
            (Paths: new LayoutPaths(), Scope: new HomeThemeScope(HomeDataScope.DeviceLocal, "home-scale-default"), Scale: 1d),
            (Paths: new LayoutPaths(), Scope: new HomeThemeScope(HomeDataScope.DeviceLocal, "home-scale-large"), Scale: 1.5d)
        };
        byte[][] savedBytes = new byte[profiles.Length][];
        try
        {
            for (var index = 0; index < profiles.Length; index++)
            {
                var (paths, scope, scale) = profiles[index];
                var path = Path.Combine(paths.DataDirectory, "preferences.json");
                var saved = HomeThemePreference.Default(scope) with
                {
                    Revision = 1,
                    Appearance = "Dark",
                    Accessibility = new(DisplayScale: scale, ReduceMotion: true)
                };
                var receipt = await HomeNativePreferenceFile.CompareExchangeAsync(path, scope, 0, saved,
                    UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), token);
                Assert.True(receipt.Succeeded);
                savedBytes[index] = await File.ReadAllBytesAsync(path, token);
            }

            foreach (var (paths, scope, scale) in profiles)
            {
                var path = Path.Combine(paths.DataDirectory, "preferences.json");
                await using var preferences = new NativeHomeThemePreferences(path, scope,
                    UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true),
                    action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
                await preferences.RefreshAsync(token);
                Assert.Equal(scale, preferences.Context.Current.Accessibility.DisplayScale);

                var factory = new UnifiedHomeContentFactory(new Registry(),
                    () => throw new InvalidOperationException("Restoring Home must not open Dashboard."),
                    () => throw new InvalidOperationException("Restoring Home must not open Discover."),
                    (_, _) => throw new InvalidOperationException("Measuring the shell must not navigate."),
                    (_, _, _) => throw new InvalidOperationException("Measuring the shell must not prepare an AI draft."),
                    (request, _) => request.ProfileId == scope.OwnerId ? Task.CompletedTask :
                        throw new UnauthorizedAccessException("The fixture route belongs to another profile."),
                    () => true, token, interfaceTheme: preferences.Context);
                var chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: factory,
                    homeProfileId: scope.OwnerId, cancellationToken: token);
                var workspace = new BrowseNativeWorkspace(chrome, _ => true);
                var scene = BrowseNativeSurface.CreateScene(workspace, new LayoutReadiness(), CuiAppearance.Dark, preferences.Context);
                scene.ControlRegistry!.RegisterImageSourceResolver(HavenPackagedSvgResolver.Resolve);
                var host = new CuiSceneHost(scene.ControlRegistry);
                var window = new Window { Width = 980, Height = 750, Content = host };
                try
                {
                    window.Show();
                    var ready = await host.ShowAsync(scene, token);
                    Assert.Equal(CuiSceneAvailabilityState.Ready, ready.State);
                    workspace.ObserveReadiness(ready);
                    var home = await SettlePageAsync();
                    Assert.Equal("browse://home", chrome.State.SelectedTab.Address.Value);
                    AssertRail(home);

                    await chrome.NewTabAsync(false, token);
                    var newTab = await SettlePageAsync();
                    Assert.Equal("browse://newtab", chrome.State.SelectedTab.Address.Value);
                    Assert.NotSame(home, newTab);
                    AssertRail(newTab);
                    var search = SettingsControl<TextBox>(newTab, "home-search");
                    var go = SettingsControl<Button>(newTab, "home-submit");
                    Assert.Equal(48 * scale, search.Height);
                    Assert.Equal(44 * scale, go.Bounds.Width, 1);
                    Assert.Equal(20 * scale, go.FontSize);
                    Assert.Equal(980, window.ClientSize.Width, 1);
                    Assert.Equal(1, preferences.Current.Revision);

                    void AssertRail(UnifiedHomePage page)
                    {
                        var brand = SettingsControl<Button>(page, "home-brand");
                        Assert.Equal(scale, CuiInterfaceScale.For(brand));
                        Assert.Equal((scale == 1 ? 122 : 44) * scale, brand.Bounds.Width, 1);
                    }

                    async Task<UnifiedHomePage> SettlePageAsync()
                    {
                        var selected = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(
                            chrome.ObserveOriginalAppContent(chrome.State.SelectedTabId));
                        var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(
                            selected.OriginalNativeView).OriginalContent);
                        for (var attempt = 0; attempt < 20; attempt++)
                        {
                            SettleFixtureLayout(window);
                            if (page.IsAttachedToVisualTree() && page.Bounds.Width > 0) return page;
                            await Task.Delay(10, token);
                        }
                        throw new InvalidOperationException("The selected Home view did not attach and lay out.");
                    }
                }
                finally
                {
                    await workspace.DisposeAsync();
                    await host.CloseOriginalAsync();
                    window.Close();
                }
            }

            for (var index = 0; index < profiles.Length; index++)
                Assert.Equal(savedBytes[index], await File.ReadAllBytesAsync(
                    Path.Combine(profiles[index].Paths.DataDirectory, "preferences.json"), token));
        }
        finally
        {
            foreach (var (paths, _, _) in profiles) Directory.Delete(paths.DataDirectory, recursive: true);
        }
    }
}
