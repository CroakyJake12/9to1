using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiSceneThemePreferenceTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private const string Markup = """
        <Container Type="Vertical Stack">
          <Input ID="draft" Type="Text" Value="Keep this actual draft" />
          <Theme Name="Professional"><Button ID="fixed" Content="Fixed scope" /></Theme>
          <Container Type="Menu"><Button Type="Menu Item" ID="file" Content="File">
            <Button Type="Menu Item" Content="Open" />
          </Button></Container>
        </Container>
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scene_and_popup_follow_home_preference_preserving_draft_focus_and_explicit_scope(bool guarded)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var source = new Source("Glow", "profile-a");
            using var context = new CuiThemePreferenceContext(source);
            using var otherContext = new CuiThemePreferenceContext(new Source("Bubble", "profile-b"));
            using var host = new CuiSceneHost();
            using var otherHost = new CuiSceneHost();
            Window? window = null;
            Window? otherWindow = null;
            try
            {
                await host.ShowAsync(Scene(context, guarded)).WaitAsync(Bound);
                await otherHost.ShowAsync(Scene(otherContext, guarded)).WaitAsync(Bound);
                window = new Window { Content = host, Width = 600, Height = 320 };
                otherWindow = new Window { Content = otherHost, Width = 600, Height = 320 };
                otherWindow.Show(); window.Show(); window.UpdateLayout(); otherWindow.UpdateLayout();
                var input = host.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "draft");
                var fixedScope = host.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "fixed");
                Assert.Equal("Glow", Resource<string>(host, "CuiEffectiveThemeName"));
                Assert.Equal("Glow", Resource<string>(input, "CuiEffectiveThemeName"));
                Assert.Equal("Professional", Resource<string>(fixedScope, "CuiEffectiveThemeName"));
                Assert.Equal("Bubble", Resource<string>(otherHost, "CuiEffectiveThemeName"));
                input.Text = "A preserved unsaved edit";
                Assert.True(input.Focus()); input.SelectionStart = 2; input.SelectionEnd = 11;
                var currentRoot = host.Content;
                source.Change("Professional", CuiAppearance.Bright);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Same(currentRoot, host.Content);
                Assert.Equal("Professional", Resource<string>(host, "CuiEffectiveThemeName"));
                Assert.Equal("Professional", Resource<string>(input, "CuiEffectiveThemeName"));
                Assert.Equal("Bright", Resource<string>(host, "CuiAppearance"));
                Assert.Equal("Bubble", Resource<string>(otherHost, "CuiEffectiveThemeName"));
                Assert.Equal("A preserved unsaved edit", input.Text);
                Assert.Equal("preserved", input.SelectedText);
                Assert.True(input.IsKeyboardFocusWithin);
                var palette = CuiSurfacePaletteCatalog.For("Spaces", CuiAppearance.Bright, CuiTheme.Professional);
                Assert.Equal(palette.TideBase, Assert.IsAssignableFrom<ISolidColorBrush>(host.Background).Color);
                var menu = host.GetVisualDescendants().OfType<CuiMenuItem>().Single(item => item.Name == "file");
                menu.IsSubMenuOpen = true; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var item = Assert.IsType<CuiMenuItem>(Assert.Single(menu.Items));
                Assert.Equal("Professional", Resource<string>(item, "CuiEffectiveThemeName"));
                Assert.Equal(palette.Text, Assert.IsAssignableFrom<ISolidColorBrush>(item.Foreground).Color);
                Assert.True(item.Bounds.Width > 0 && item.Bounds.Height > 0);
                menu.IsSubMenuOpen = false;
                return true;
            }
            finally
            {
                window?.Close(); otherWindow?.Close();
                if (guarded)
                {
                    await host.CloseOriginalAsync().WaitAsync(Bound);
                    await otherHost.CloseOriginalAsync().WaitAsync(Bound);
                }
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_custom_theme_is_explicit_CUI_fallback_and_retirement_releases_only_scene_subscription(bool guarded)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var source = new Source("Missing custom theme", "offline-profile");
            using var context = new CuiThemePreferenceContext(source);
            var host = new CuiSceneHost();
            try
            {
                await host.ShowAsync(Scene(context, guarded)).WaitAsync(Bound);
                var resources = Assert.Single(host.Resources.MergedDictionaries);
                Assert.Equal("Professional", Resource<string>(resources, "CuiEffectiveThemeName"));
                Assert.Contains("Missing custom theme", Resource<string>(resources, "CuiThemeDiagnostic"));
                if (guarded) await host.CloseOriginalAsync().WaitAsync(Bound);
                else host.Dispose();
                source.Change("Glow", CuiAppearance.Dark);
                Assert.Equal("Glow", context.Current.Theme.Name); // Borrowed canonical owner remains alive.
                Assert.Equal("Professional", Resource<string>(resources, "CuiEffectiveThemeName"));
                return true;
            }
            finally
            {
                if (guarded) await host.CloseOriginalAsync().WaitAsync(Bound);
                else host.Dispose();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Scene_custom_theme_and_accessibility_use_same_canonical_resolution_as_controls()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var custom = CuiCustomThemeContract.CreateDefault("Owner theme");
            var source = new Source(custom.Name, "custom-profile");
            source.Current = source.Current with
            {
                CustomThemesJson = CuiCustomThemeContract.SerializeLibrary([custom]),
                Accessibility = new CuiAccessibilitySettings { HighContrast = true, ReduceMotion = true }
            };
            using var context = new CuiThemePreferenceContext(source);
            using var host = new CuiSceneHost();
            await host.ShowAsync(Scene(context, false)).WaitAsync(Bound);
            Assert.Equal(custom.Name, Resource<string>(host, "CuiEffectiveThemeName"));
            Assert.Equal(0d, Resource<double>(host, "CuiMotionDurationScale"));
            var current = Assert.Single(host.Resources.MergedDictionaries);
            source.Change("Glow", CuiAppearance.Bright);
            Assert.Same(current, Assert.Single(host.Resources.MergedDictionaries));
            Assert.Equal("Glow", Resource<string>(host, "CuiEffectiveThemeName"));
            Assert.Equal(0d, Resource<double>(host, "CuiMotionDurationScale"));
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failed_replacement_keeps_retained_scene_theme_live(bool guarded, bool failReadiness)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var source = new Source("Glow", "retained-profile");
            using var context = new CuiThemePreferenceContext(source);
            using var host = new CuiSceneHost();
            Exception? replacementFailure = null;
            try
            {
                await host.ShowAsync(Scene(context, guarded)).WaitAsync(Bound);
                var originalRoot = host.Content;
                var resources = Assert.Single(host.Resources.MergedDictionaries);
                var invalid = Scene(context, guarded) with
                {
                    Document = new CuiRichParser().Parse("<Object Type=\"UnregisteredOwner\" />"),
                    Readiness = failReadiness ? new Unavailable() : new Ready()
                };
                replacementFailure = await Assert.ThrowsAnyAsync<Exception>(() => host.ShowAsync(invalid).WaitAsync(Bound));
                Assert.Same(originalRoot, host.Content);
                source.Change("Professional", CuiAppearance.Bright);
                Assert.Same(resources, Assert.Single(host.Resources.MergedDictionaries));
                Assert.Equal("Professional", Resource<string>(host, "CuiEffectiveThemeName"));
                var input = Assert.IsType<CuiContainerControl>(originalRoot).AuthoredChildren.OfType<TextBox>().Single();
                Assert.Equal("Professional", Resource<string>(input, "CuiEffectiveThemeName"));
                Assert.Equal("Keep this actual draft", input.Text);
                return true;
            }
            finally
            {
                if (guarded)
                {
                    if (replacementFailure is null) await host.CloseOriginalAsync().WaitAsync(Bound);
                    else
                    {
                        var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => host.CloseOriginalAsync().WaitAsync(Bound));
                        Assert.Same(replacementFailure, closeFailure);
                    }
                }
            }
        }, CancellationToken.None);
    }

    private sealed class Unavailable : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<CuiSceneAvailability>(new InvalidOperationException("Replacement readiness failed"));
    }

    private static CuiNativeScene Scene(CuiThemePreferenceContext context, bool guarded)
    {
        var bindings = new CuiViewModel();
        return new("app.spaces", "Theme scene", "Spaces", new CuiRichParser().Parse(Markup), bindings, bindings, new Ready())
        { ThemePreference = context, IsPublicationCurrent = guarded ? () => true : null };
    }

    private static T Resource<T>(IResourceNode node, string key)
    {
        Assert.True(node.TryGetResource(key, null, out var value));
        return Assert.IsAssignableFrom<T>(value);
    }

    private static T Resource<T>(Control control, string key)
    {
        Assert.True(control.TryFindResource(key, out var value));
        return Assert.IsAssignableFrom<T>(value);
    }

    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Actual theme scene"));
    }

    private sealed class Source(string theme, string scope) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; set; } =
            new(theme, 1, scope, true, CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed;
        public void Change(string theme, CuiAppearance appearance)
        {
            Current = Current with { Name = theme, Appearance = appearance, Revision = Current.Revision + 1 };
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
