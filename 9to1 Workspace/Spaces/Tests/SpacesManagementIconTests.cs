using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpacesManagementIconTests
{
    [Fact]
    public async Task Shared_management_icon_resolves_to_canonical_settings_instead_of_unknown_placeholder()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader(SpacesHomeCuiDocument.CreateControlRegistry());
            var (root, diagnostics) = loader.LoadMarkup("""
                <Cui><Page><Container Type="Horizontal Stack">
                  <Image id="manage" Source="icon:manage-spaces" Width="24" Height="24" />
                  <Image id="settings" Source="icon:settings" Width="24" Height="24" />
                  <Image id="unknown" Source="icon:unknown" Width="24" Height="24" />
                </Container></Page></Cui>
                """);
            Assert.Empty(diagnostics);
            var control = Assert.IsAssignableFrom<Control>(root);
            var window = new Window { Content = control, Width = 200, Height = 100 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var manage = Drawing("manage");
                var settings = Drawing("settings");
                var unknown = Drawing("unknown");
                var manageGeometry = Assert.IsAssignableFrom<Geometry>(manage.Geometry);
                Assert.Equal(Assert.IsAssignableFrom<Geometry>(settings.Geometry).Bounds, manageGeometry.Bounds);
                Assert.NotEqual(Assert.IsAssignableFrom<Geometry>(unknown.Geometry).Bounds, manageGeometry.Bounds);
                var settingsPen = Assert.IsAssignableFrom<IPen>(settings.Pen);
                var managePen = Assert.IsAssignableFrom<IPen>(manage.Pen);
                Assert.Equal(settingsPen.Thickness, managePen.Thickness);
                Assert.Equal(settingsPen.Brush, managePen.Brush);
            }
            finally { window.Close(); }
            return true;

            GeometryDrawing Drawing(string id) => Assert.IsType<GeometryDrawing>(
                Assert.IsType<DrawingImage>(control.GetVisualDescendants().OfType<Image>().Single(image => image.Name == id).Source).Drawing);
        }, CancellationToken.None);
    }
}
