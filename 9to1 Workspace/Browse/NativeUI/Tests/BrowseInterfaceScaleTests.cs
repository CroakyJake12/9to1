using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    [AvaloniaFact]
    public async Task Embedded_interface_scale_reflows_controls_and_retains_the_same_address_draft_and_popup()
    {
        var source = new InterfaceScaleSource(1.25);
        using var theme = new CuiThemePreferenceContext(source);
        var chrome = await BrowseChrome.CreateAsync(new Paths(), internalContentFactory: new TabSurfaceInternalFixtureFactory(),
            homeProfileId: "interface-scale-fixture");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true,
            sharedContext: new SharedControlsFakeContext(new SharedControlsFakeGateway()));
        var scene = BrowseNativeSurface.CreateScene(workspace, new FixtureReadiness(), CuiAppearance.Dark, theme);
        RegisterProductionImageResolver(scene.ControlRegistry!);
        var host = new CuiSceneHost(scene.ControlRegistry);
        var window = new Window { Width = 900, Height = 680, Content = host };
        try
        {
            window.Show();
            var ready = await host.ShowAsync(scene, CancellationToken.None);
            Assert.Equal(CuiSceneAvailabilityState.Ready, ready.State);
            workspace.ObserveReadiness(ready);
            Render();

            var address = Named<TextBox>("BrowseAddressBox");
            var plus = Named<Button>("BrowseNewTabButton");
            var title = host.GetVisualDescendants().OfType<Button>().First(button => button.Name == "BrowseTabButton");
            Assert.Equal(1.25, CuiInterfaceScale.For(host));
            Assert.Equal(16.25, address.FontSize);
            Assert.Equal(40, plus.Width);
            Assert.True(Visible("HavenHostMark"));

            source.SetScale(1);
            Render();
            var titlePadding = title.Padding;
            var owner = chrome.State.SelectedTabId;
            var renderScaling = window.RenderScaling;
            var clientSize = window.ClientSize;
            Assert.True(address.Focus(NavigationMethod.Tab));
            const string draft = "an unfinished address";
            address.Text = draft;
            address.SelectionStart = 3;
            address.SelectionEnd = 12;

            foreach (var scale in new[] { 1.5, 1d, 1.75, 1d })
            {
                source.SetScale(scale);
                Render();
                Assert.Equal(13 * scale, address.FontSize);
                Assert.Equal(32 * scale, plus.Width);
                Assert.Equal(new Thickness(titlePadding.Left * scale, titlePadding.Top * scale,
                    titlePadding.Right * scale, titlePadding.Bottom * scale), title.Padding);
                Assert.Equal(scale == 1, Visible("HavenHostMark"));
                Assert.Same(address, Named<TextBox>("BrowseAddressBox"));
                Assert.True(address.IsFocused);
                Assert.Equal(draft, address.Text);
                Assert.Equal(3, address.SelectionStart);
                Assert.Equal(12, address.SelectionEnd);
                Assert.Equal(owner, chrome.State.SelectedTabId);
                Assert.Equal(renderScaling, window.RenderScaling);
                Assert.Equal(clientSize, window.ClientSize);
            }

            // The real native popup remains actionable at the enlarged scale.
            source.SetScale(1.5);
            Render();
            var menu = Named<MenuItem>("BrowseSearchModeButton");
            var center = menu.TranslatePoint(new Point(menu.Bounds.Width / 2, menu.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Render();
            Assert.True(menu.IsSubMenuOpen);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Render();
            Assert.False(menu.IsSubMenuOpen);
            Assert.True(workspace.TryGetValue("HasAddressDraft", out var hasDraft));
            Assert.Equal(true, hasDraft);
        }
        finally
        {
            await workspace.DisposeAsync();
            await host.CloseOriginalAsync();
            window.Close();
        }

        T Named<T>(string id) where T : Control => host.GetVisualDescendants().OfType<T>().Single(control => control.Name == id);

        bool Visible(string id) => host.GetVisualDescendants().OfType<Control>()
            .Any(control => control.Name == id && control.IsEffectivelyVisible);

        void Render()
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
        }
    }

    [AvaloniaFact]
    public void Authored_interface_columns_and_padding_reapply_without_accumulating_scale()
    {
        var source = new InterfaceScaleSource(1);
        using var theme = new CuiThemePreferenceContext(source);
        using var loader = new CuiControlLoader(new CuiControlRegistry { ScaleAuthoredInterfaceMetrics = true });
        loader.SetThemePreferenceContext(theme);
        var bindings = new CuiViewModel();
        bindings.Set("Columns", "96 1fr");
        bindings.Set("Inset", "4 6 8 10");
        loader.SetBindingContext(bindings);
        var document = new CuiRichParser().Parse("""
            <Page>
              <Container id="responsive-grid" Type="Grid" ColumnSizes="{Binding Columns}">
                <Button id="scaled-button" Padding="{Binding Inset}" Content="Interface size" />
              </Container>
            </Page>
            """);
        var loaded = loader.TryLoad(document);
        Assert.NotNull(loaded.Root);
        Assert.DoesNotContain(loaded.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
        loader.WireBindings(loaded.Root);
        var window = new Window { Width = 720, Height = 520, Content = loaded.Root };
        try
        {
            window.Show();
            var frame = loaded.Root.GetVisualDescendants().OfType<CuiContainerControl>()
                .Single(control => control.Name == "responsive-grid");
            var grid = Assert.IsType<Grid>(frame.Layout);
            var button = loaded.Root.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Name == "scaled-button");
            AssertMetrics(96, new Thickness(4, 6, 8, 10));
            source.SetScale(1.5);
            AssertMetrics(144, new Thickness(6, 9, 12, 15));
            bindings.Set("Columns", "80 1fr");
            bindings.Set("Inset", "8 12");
            AssertMetrics(120, new Thickness(12, 18));
            source.SetScale(1);
            AssertMetrics(80, new Thickness(8, 12));
            source.SetScale(1.75);
            AssertMetrics(140, new Thickness(14, 21));
            source.SetScale(1);
            AssertMetrics(80, new Thickness(8, 12));

            void AssertMetrics(double columnWidth, Thickness padding)
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(columnWidth, grid.ColumnDefinitions[0].Width.Value);
                Assert.Equal(padding, button.Padding);
                Assert.True(grid.ColumnDefinitions[1].Width.IsStar);
            }
        }
        finally { window.Close(); }
    }

    private sealed class InterfaceScaleSource(double initialScale) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } =
            new("Professional", 0, "device:interface-scale-fixture", false, new() { DisplayScale = initialScale });
        public event EventHandler? Changed;

        public void SetScale(double scale)
        {
            Current = Current with
            {
                Revision = Current.Revision + 1,
                Accessibility = Current.Accessibility with { DisplayScale = scale }
            };
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
