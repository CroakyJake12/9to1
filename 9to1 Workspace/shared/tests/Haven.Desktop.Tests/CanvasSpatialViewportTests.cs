using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Controls;
using HavenOS.Apps.Canvas;

namespace Haven.Desktop.Tests;

public sealed class CanvasSpatialViewportTests
{
    [AvaloniaFact]
    public async Task Native_viewport_renders_structured_document_maps_letterboxing_and_clears_pixels_on_revocation()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var document = CanvasRnoteDocument.Create("Actual native viewport");
        document.DrawStroke([new(10, 20, 0.2), new(80, 100, 0.8)], document.Snapshot.RevisionId);
        var readiness = new Readiness();
        using var viewport = new CanvasSpatialViewport(document, readiness);
        var window = new Window { Content = viewport, Width = 800, Height = 600 };
        window.Show();
        try
        {
            Assert.True(await viewport.RefreshAsync(TestContext.Current.CancellationToken));
            window.UpdateLayout();
            Assert.IsType<WriteableBitmap>(Assert.Single(viewport.GetVisualDescendants().OfType<Image>()).Source);
            var bounds = document.Render();
            var center = viewport.ToDocumentPoint(new(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2));
            Assert.NotNull(center);
            Assert.InRange(center.Value.X, bounds.X + bounds.Width / 2 - 0.01, bounds.X + bounds.Width / 2 + 0.01);
            Assert.InRange(center.Value.Y, bounds.Y + bounds.Height / 2 - 0.01, bounds.Y + bounds.Height / 2 + 0.01);
            Assert.Null(viewport.ToDocumentPoint(new Point(-1, -1)));
            readiness.Allowed = false;
            Assert.False(await viewport.RefreshAsync(TestContext.Current.CancellationToken));
            Assert.Null(Assert.Single(viewport.GetVisualDescendants().OfType<Image>()).Source);
            Assert.Null(viewport.ToDocumentPoint(new Point(400, 300)));
            Assert.Contains(viewport.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Current Files access was revoked.");
            viewport.Dispose();
            Assert.NotNull(document.Snapshot); // Owning route's structured document is borrowed, never destroyed by the view.
        }
        finally { window.Close(); }
    }

    private sealed class Readiness : ICuiSceneReadiness
    {
        public bool Allowed { get; set; } = true;
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) => ValueTask.FromResult(
            new CuiSceneAvailability(Allowed ? CuiSceneAvailabilityState.Ready : CuiSceneAvailabilityState.Unavailable,
                Allowed ? "FixtureReady" : "FilesAccessRevoked", Allowed ? "Controlled native rendering fixture." : "Current Files access was revoked."));
    }
}
