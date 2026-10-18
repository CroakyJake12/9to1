using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiContentRetirementTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private const string Markup = """
        <Cui><Container Type="Vertical Stack">
          <Button id="nested-owner">
            <Container id="retained-content" Type="Vertical Stack">
              <Text text="Canonical nested content" />
              <Input id="retained-draft" Type="Text" Value="Keep this actual draft" />
            </Container>
          </Button>
        </Container></Cui>
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mounted_nested_control_content_retires_without_a_second_visual_parent(bool throughSceneHost)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRetirementTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            CuiControlLoader? loader = null;
            CuiSceneHost? host = null;
            Task? close = null;
            Window? window = null;
            var failures = new List<Exception>();
            try
            {
                Control root;
                if (throughSceneHost)
                {
                    host = new CuiSceneHost();
                    var bindings = new CuiViewModel();
                    await host.ShowAsync(new("template-retirement", "Template retirement", "Home",
                        new CuiRichParser().Parse(Markup), bindings, bindings, new Ready())
                        { IsPublicationCurrent = () => true }).WaitAsync(Bound);
                    root = host;
                }
                else
                {
                    loader = new CuiControlLoader();
                    var loaded = loader.LoadMarkup(Markup);
                    Assert.Empty(loaded.Diagnostics);
                    root = Assert.IsAssignableFrom<Control>(loaded.Root);
                }

                window = new Window { Content = root, Width = 640, Height = 360 };
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var button = Assert.Single(root.GetVisualDescendants().OfType<Button>(), item => item.Name == "nested-owner");
                var originalContent = Assert.IsType<CuiContainerControl>(button.Content);
                var originalDraft = Assert.Single(originalContent.GetVisualDescendants().OfType<TextBox>());
                Assert.Equal("Keep this actual draft", originalDraft.Text);
                Assert.Same(root, window.Content); // The retirement transition occurs while mounted.

                if (host is not null)
                {
                    close = host.CloseOriginalAsync();
                    Assert.Same(close, host.CloseOriginalAsync());
                    await close.WaitAsync(Bound);
                    Assert.Null(host.Content);
                }
                else
                {
                    loader!.Dispose();
                    await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                    Assert.Same(root, window.Content);
                    Assert.Same(originalContent, button.Content);
                }

                // Flush actual deferred layout/template work, rather than detaching
                // the test window before the old template fence is released.
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(Bound);
                Assert.Equal("Keep this actual draft", originalDraft.Text);
            }
            catch (Exception failure) { failures.Add(failure); }
            finally
            {
                try { if (window is not null) { window.Content = null; window.Close(); } }
                catch (Exception failure) { failures.Add(failure); }
                if (host is not null)
                {
                    try { close ??= host.CloseOriginalAsync(); await close.WaitAsync(Bound); }
                    catch (Exception failure) { if (!failures.Contains(failure)) failures.Add(failure); }
                }
                if (loader is not null)
                {
                    try { loader.Dispose(); await loader.WhenActionsIdleAsync().WaitAsync(Bound); }
                    catch (Exception failure) { if (!failures.Contains(failure)) failures.Add(failure); }
                }
            }
            if (failures.Count > 1) throw new AggregateException("Actual template retirement and cleanup failures.", failures);
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            return true;
        }, CancellationToken.None);
    }

    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Actual test scene"));
    }
}

// Exercise the actual competing-template transition without loading a forbidden
// donor theme. The deliberately small test template still re-parents real content
// when CUI releases its projection; removing that competition would weaken this test.
public sealed class CuiRetirementTestApplication : Application
{
    public override void Initialize()
    {
        CuiNativeHost.InitialisePrimitiveTheme(this);
        var replacement = new ControlTheme(typeof(Button));
        replacement.Setters.Add(new Setter(TemplatedControl.TemplateProperty,
            new FuncControlTemplate<Button>((_, names) =>
            {
                var presenter = new ContentPresenter { Name = "PART_ContentPresenter" };
                names.Register(presenter.Name, presenter);
                presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentControl.ContentProperty));
                return presenter;
            })));
        Resources[typeof(Button)] = replacement;
    }

    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(
        AppBuilder.Configure<CuiRetirementTestApplication>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }));
}
