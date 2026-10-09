using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>9T-FW-001: shared dispatcher and actual Home markup with controlled owners.
/// This is source-built headless rendering, not installed Home/auth/provider acceptance.</summary>
public sealed class CuiViewModelDispatchRegressionTests
{
    [Fact]
    public void Missing_sync_command_is_not_reported_as_success()
    {
        var model = new CuiViewModel();
        model.SetActionAvailability("missing", true); // Metadata is not a command registration.
        Assert.Throws<InvalidOperationException>(() => model.Dispatch("missing"));
        Assert.False(model.HasAction("missing"));
    }

    [Fact]
    public async Task Missing_async_command_is_not_reported_as_success()
    {
        var model = new CuiViewModel();
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.DispatchAsync("missing", null).AsTask());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Already_cancelled_dispatch_preserves_token_and_prevents_effects(bool registered)
    {
        var model = new CuiViewModel();
        var effects = 0;
        if (registered) model.On("run", _ => effects++);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Record.ExceptionAsync(() => model.DispatchAsync("run", null, cancellation.Token).AsTask());
        Assert.Equal(0, effects);
        Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
    }

    [Fact]
    public async Task Registered_command_keeps_case_insensitive_dispatch_and_parameter_identity()
    {
        var model = new CuiViewModel();
        var parameter = new object();
        var calls = 0;
        model.On("Save", value => { Assert.Same(parameter, value); calls++; });
        model.Dispatch("save", parameter);
        await model.DispatchAsync("SAVE", parameter);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Unavailable_route_keeps_the_owners_truthful_explanation_callback()
    {
        var model = new CuiViewModel();
        model.On("NavigateSpaces", _ => model.Set("NavigationStatus", "Spaces navigation is not connected in this host."));
        model.SetActionAvailability("NavigateSpaces", false);
        await model.DispatchAsync("NavigateSpaces", null);
        Assert.Equal("Spaces navigation is not connected in this host.", model.Get("NavigationStatus"));
        Assert.Equal(false, model.IsActionAvailable("NavigateSpaces"));
    }

    [Fact]
    public async Task Registered_handler_failure_keeps_its_original_identity()
    {
        var model = new CuiViewModel();
        var original = new IOException("Controlled original handler failure.");
        model.On("run", _ => throw original);
        Assert.Same(original, Record.Exception(() => model.Dispatch("run")));
        Assert.Same(original, await Record.ExceptionAsync(() => model.DispatchAsync("run", null).AsTask()));
    }

    [Theory]
    [InlineData(CuiAppearance.SuperBright)]
    [InlineData(CuiAppearance.Dark)]
    [InlineData(CuiAppearance.Bright)]
    [InlineData(CuiAppearance.SuperDark)]
    public async Task Actual_Home_keyboard_pointer_bindings_and_missing_route_preserve_the_live_tree(CuiAppearance appearance)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiW2NativeTestApplication));
        await session.Dispatch(async () =>
        {
            var errors = new List<Exception>();
            Exception? expectedFailure = null;
            var model = new CuiViewModel();
            model.Set("IsNativeHomeShell", true);
            model.Set("IsDashboard", true);
            model.Set("IsSettings", false);
            model.Set("NavigationStatus", "Ready");
            var settingsCalls = 0;
            model.On("NavigateSettings", _ =>
            {
                settingsCalls++;
                model.Set("IsDashboard", false);
                model.Set("IsSettings", true);
                model.Set("NavigationStatus", "Settings opened");
            });
            model.On("NavigateSpaces", _ => model.Set("NavigationStatus", "Spaces navigation is not connected in this host."));
            model.SetActionAvailability("NavigateSpaces", false);
            var loader = new CuiControlLoader();
            var window = new Window { Width = 1200, Height = 900 };
            try
            {
                loader.SetSurface("Home");
                loader.SetAppearance(appearance);
                loader.SetBindingContext(model);
                loader.SetActionDispatcher(model);
                using var stream = typeof(CuiViewModelDispatchRegressionTests).Assembly.GetManifestResourceStream("9T.W2.Home.cui")
                    ?? throw new InvalidDataException("The actual canonical Home.cui resource is required.");
                using var reader = new StreamReader(stream);
                var loaded = loader.LoadMarkup(reader.ReadToEnd(), "9to1 Workspace/Home/UI/Home.cui");
                Assert.DoesNotContain(loaded.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
                var root = Assert.IsAssignableFrom<Control>(loaded.Root);
                loader.WireBindings(root);
                window.Resources.MergedDictionaries.Add(CuiSceneVisualResources.Create("Home", appearance));
                window.RequestedThemeVariant = CuiSceneVisualResources.Variant(appearance);
                window.Content = root;
                window.Show();
                window.UpdateLayout();
                Assert.True(Controls(root).Count() > 40); // Full Home, not a two-control substitute.

                var search = Find<TextBox>(root, "global-search");
                Assert.Equal("Search 9-1", AutomationProperties.GetName(search));
                Assert.True(search.Focus());
                window.KeyTextInput("draft retained across navigation");
                Assert.Equal("draft retained across navigation", search.Text);

                var settings = Find<Button>(root, "nav-settings");
                Assert.True(settings.Focus());
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
                await loader.WhenActionsIdleAsync();
                Assert.Equal(1, settingsCalls);
                Assert.NotNull(Find<StackPanel>(root, "home-settings-page"));
                Assert.DoesNotContain(Controls(root), control => control.Name == "dashboard-page");
                Assert.Equal("draft retained across navigation", search.Text);

                Click(window, Find<Button>(root, "nav-spaces"));
                await loader.WhenActionsIdleAsync();
                Assert.Equal("Spaces navigation is not connected in this host.", model.Get("NavigationStatus"));
                Assert.Contains(Controls(root).OfType<TextBlock>(), text => text.Text == (string?)model.Get("NavigationStatus"));

                CuiActionFailure? failure = null;
                loader.ActionFailed += (_, value) => failure = value;
                Click(window, Find<Button>(root, "open-studio")); // Intentionally no registered owner.
                var pipeline = loader.WhenActionsIdleAsync();
                var error = await Record.ExceptionAsync(() => pipeline);
                expectedFailure = Assert.IsType<InvalidOperationException>(error);
                Assert.True(pipeline.IsFaulted);
                Assert.Equal("CUIA_FAILED", Assert.IsType<CuiActionFailure>(failure).Code);
                Assert.DoesNotContain("OpenStudio", failure!.Message); // No private exception text in safe UI status.
                Assert.Same(root, window.Content);
                Assert.Equal("draft retained across navigation", search.Text);
                Assert.Equal(1, settingsCalls);

                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.True(frame.PixelSize.Width >= 1200 && frame.PixelSize.Height >= 900);
                if (Environment.GetEnvironmentVariable("CUI_W2_EVIDENCE_DIR") is { Length: > 0 } evidence)
                {
                    Directory.CreateDirectory(evidence);
                    frame.Save(Path.Combine(evidence, $"actual-home-{appearance}.png"));
                }
            }
            catch (Exception error) { errors.Add(error); }
            finally
            {
                try { window.Close(); } catch (Exception error) { errors.Add(error); }
                try { loader.Dispose(); } catch (Exception error) { errors.Add(error); }
                var terminal = loader.WhenActionsIdleAsync();
                var closeError = await Record.ExceptionAsync(() => terminal);
                if (expectedFailure is not null)
                {
                    if (!terminal.IsFaulted || !ReferenceEquals(expectedFailure, closeError))
                        errors.Add(new InvalidOperationException("Original dispatch failure was not retained through loader retirement.", closeError));
                }
                else if (closeError is not null) errors.Add(closeError);
            }
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Count > 1) throw new AggregateException("Home regression and independent cleanup failed.", errors);
            return 0;
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string id) where T : Control =>
        Assert.IsType<T>(Assert.Single(Controls(root).Where(control => control.Name == id)));

    private static IEnumerable<Control> Controls(Control root)
    {
        yield return root;
        IEnumerable<Control> children = root switch
        {
            Panel panel => panel.Children,
            Decorator { Child: Control child } => [child],
            ContentControl { Content: Control child } => [child],
            ItemsControl items => items.Items.OfType<Control>(),
            _ => []
        };
        foreach (var child in children)
        foreach (var descendant in Controls(child)) yield return descendant;
    }

    private static void Click(Window window, Button button)
    {
        window.UpdateLayout();
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The authored button is not attached to the native Home window.");
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
}

public sealed class CuiW2NativeTestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        CuiNativeHost.ConfigureFonts(AppBuilder.Configure<CuiW2NativeTestApplication>())
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
}
