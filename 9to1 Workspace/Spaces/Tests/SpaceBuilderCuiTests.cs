using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Spaces.Builder;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpaceBuilderCuiTests
{
    [Fact]
    public async Task Authored_builder_cui_loads_without_diagnostics()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var (tree, diagnostics) = loader.TryLoad(SpaceBuilderCuiDocument.Load());
            Assert.Empty(diagnostics);
            var root = Assert.IsAssignableFrom<Control>(tree);
            loader.WireBindings(root);
            var window = new Window { Content = root, Width = 900, Height = 800 };
            window.Show();
            for (var pass = 0; pass < 3; pass++)
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
            }
            T Find<T>(string id) where T : Control => root.GetVisualDescendants().OfType<T>()
                .Single(control => control.Name == id || AutomationProperties.GetAutomationId(control) == id);
            var name = Find<TextBox>("space-builder-name");
            Assert.NotNull(name);
            var status = Find<TextBlock>("space-builder-status");
            Assert.NotNull(status);
            window.Close();
            loader.Dispose();
            Dispatcher.UIThread.RunJobs();
            return true;
        }, token);
    }
}
