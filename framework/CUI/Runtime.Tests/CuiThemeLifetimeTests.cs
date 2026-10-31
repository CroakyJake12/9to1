using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiThemeLifetimeTests
{
    [Fact]
    public async Task Global_palette_without_an_application_does_not_invoke_an_unpumped_document_dispatcher()
    {
        Assert.Null(Application.Current);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowPump = new ManualResetEventSlim();
        var worker = new Thread(() =>
        {
            try
            {
                using (var loader = new CuiControlLoader())
                {
                    Assert.Empty(loader.LoadMarkup("<Button Content='Offline document'/>").Diagnostics);
                    ready.SetResult();
                    if (!allowPump.Wait(TimeSpan.FromSeconds(15)))
                        throw new TimeoutException("The offline document owner was not released.");
                    // Also release a mistakenly queued callback in the failing
                    // implementation, so regression failure cannot strand the worker.
                    Avalonia.Threading.Dispatcher.CurrentDispatcher.RunJobs();
                }
                finished.SetResult();
            }
            catch (Exception error)
            {
                ready.TrySetException(error);
                finished.TrySetException(error);
            }
        }) { IsBackground = true };
        var previous = CuiSurfacePaletteCatalog.ActiveTheme;
        Task? publication = null;
        var completedWithoutPump = false;
        worker.Start();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            publication = Task.Run(() => CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Retro));
            completedWithoutPump = ReferenceEquals(await Task.WhenAny(publication, Task.Delay(TimeSpan.FromSeconds(5))), publication);
        }
        finally
        {
            allowPump.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (publication is not null) await publication.WaitAsync(TimeSpan.FromSeconds(5));
            CuiSurfacePaletteCatalog.ActiveTheme = previous;
        }
        Assert.True(completedWithoutPump, "Global metadata publication must not wait for an offline document's dispatcher.");
    }

    [Fact]
    public async Task Retired_application_loader_cannot_observe_another_applications_global_palette()
    {
        // Deliberately retain the old loader after its application's lifetime.
        // Hosts can retain a document during teardown; the static publisher must
        // neither visit its old dispatcher nor prevent a new app from refreshing.
        CuiControlLoader? retired = null;
        await using (var first = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication)))
        {
            await first.Dispatch(() =>
            {
                CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Glow, appearanceOverride: CuiAppearance.Dark);
                retired = new CuiControlLoader();
                Assert.Empty(retired.LoadMarkup("<Button Content='Retired document'/>").Diagnostics);
            }, CancellationToken.None);
        }

        await using var second = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await second.Dispatch(() =>
        {
            using var active = new CuiControlLoader();
            active.SetSurface("Boards");
            var result = active.LoadMarkup("""
                <Container Type="Vertical Stack" Background="{Resource CuiBackgroundBrush}">
                  <Input Type="Text" Value="Keep this board draft"/>
                  <Button Content="Document action"/>
                </Container>
                """);
            Assert.Empty(result.Diagnostics);
            var root = Assert.IsType<CuiContainerControl>(result.Root);
            var input = Assert.IsType<TextBox>(root.AuthoredChildren[0]);
            var button = Assert.IsType<Button>(root.AuthoredChildren[1]);
            var window = new Window { Content = root, Width = 600, Height = 220 };
            try
            {
                window.Show();
                window.UpdateLayout();
                input.Focus();
                input.SelectionStart = 5;
                input.SelectionEnd = 9;
                var bounds = input.Bounds;
                Assert.True(bounds.Width > 100 && bounds.Height > 0);
                foreach (var appearance in new[] { CuiAppearance.Bright, CuiAppearance.Dark, CuiAppearance.SuperBright, CuiAppearance.SuperDark })
                {
                    CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Glow, appearanceOverride: appearance);
                    window.UpdateLayout();
                    var palette = CuiSurfacePaletteCatalog.For("Boards", appearance, CuiTheme.Glow);
                    Assert.Equal(palette.TideBase, Assert.IsAssignableFrom<ISolidColorBrush>(root.Background).Color);
                    Assert.Equal("Keep this board draft", input.Text);
                    Assert.Equal(5, input.SelectionStart);
                    Assert.Equal(9, input.SelectionEnd);
                    Assert.True(input.IsKeyboardFocusWithin);
                    Assert.Equal(bounds, input.Bounds);
                    Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
                    Assert.Same(input, root.AuthoredChildren[0]);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        GC.KeepAlive(retired);
    }

    [Theory]
    [InlineData(CuiAppearance.Bright)]
    [InlineData(CuiAppearance.SuperBright)]
    [InlineData(CuiAppearance.Dark)]
    [InlineData(CuiAppearance.SuperDark)]
    public void Quiet_glow_button_roles_publish_final_foreground_aliases(CuiAppearance appearance)
    {
        var resources = new ResourceDictionary();
        var palette = CuiSurfacePaletteCatalog.For("Boards", appearance, CuiTheme.Glow);
        CuiThemeResourceApplier.ApplyToResources(resources, palette);
        foreach (var state in new[] { "", "Hover", "Pressed" })
        {
            var ink = Assert.IsAssignableFrom<ISolidColorBrush>(resources[$"CuiButton{state}ForegroundBrush"]);
            Assert.Same(resources["CuiTextBrush"], ink);
            Assert.Same(ink, resources[$"HavenButton{state}ForegroundBrush"]);
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(resources[$"CuiButton{state}Brush"]);
            Assert.True(CuiContrast.Ratio(ink.Color, fill.Color) >= 4.5);
        }
        Assert.Equal(palette.Panel, Assert.IsAssignableFrom<ISolidColorBrush>(resources["CuiButtonBrush"]).Color);
    }
}
