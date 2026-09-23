using HavenOS.Images;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class ImageJourneyTests
{
    [Theory]
    [InlineData("sample.png")]
    [InlineData("sample.JPG")]
    [InlineData("sample.jpeg")]
    [InlineData("sample.bmp")]
    [InlineData("sample.GIF")]
    [InlineData("sample.webp")]
    public void PickerPolicyAcceptsSupportedRasterExtensions(string path)
    {
        Assert.True(ImageFilePolicy.IsSupportedPath(path));
    }

    [Theory]
    [InlineData("sample.svg")]
    [InlineData("sample.tiff")]
    [InlineData("sample.txt")]
    [InlineData("")]
    public void PickerPolicyRejectsUnlistedExtensions(string path)
    {
        Assert.False(ImageFilePolicy.IsSupportedPath(path));
    }

    [Fact]
    public void NavigationUsesOnlySupportedSiblingImagesInFileNameOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"haven-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var first = Path.Combine(directory, "a.webp");
            var selected = Path.Combine(directory, "b.JPG");
            var last = Path.Combine(directory, "z.png");
            var ignored = Path.Combine(directory, "notes.txt");

            File.WriteAllBytes(first, []);
            File.WriteAllBytes(selected, []);
            File.WriteAllBytes(last, []);
            File.WriteAllText(ignored, "not an image");

            var session = ImageNavigationSession.FromSelection(selected);

            Assert.Equal(selected, session.CurrentPath);
            Assert.True(session.CanMovePrevious);
            Assert.True(session.CanMoveNext);
            Assert.Equal(first, session.MovePrevious());
            Assert.Null(session.MovePrevious());
            Assert.Equal(selected, session.MoveNext());
            Assert.Equal(last, session.MoveNext());
            Assert.Null(session.MoveNext());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ViewportZoomKeepsTheImagePointUnderThePointerFixed()
    {
        var viewport = new ImageViewportState();
        const double width = 800;
        const double height = 600;
        const double pointerX = 300;
        const double pointerY = 200;

        var imageXBeforeZoom = width / 2 + (pointerX - width / 2 - viewport.OffsetX) / viewport.Scale;
        var imageYBeforeZoom = height / 2 + (pointerY - height / 2 - viewport.OffsetY) / viewport.Scale;

        Assert.True(viewport.ZoomAt(2, pointerX, pointerY, width, height));

        var imageXAfterZoom = width / 2 + (pointerX - width / 2 - viewport.OffsetX) / viewport.Scale;
        var imageYAfterZoom = height / 2 + (pointerY - height / 2 - viewport.OffsetY) / viewport.Scale;
        Assert.Equal(imageXBeforeZoom, imageXAfterZoom, precision: 10);
        Assert.Equal(imageYBeforeZoom, imageYAfterZoom, precision: 10);
        Assert.Equal(2, viewport.Scale);
    }

    [Fact]
    public void ViewportPanAndResetUpdateZoomAvailability()
    {
        var viewport = new ImageViewportState();

        Assert.True(viewport.CanZoomOut);
        Assert.True(viewport.CanZoomIn);
        Assert.True(viewport.ZoomAt(0.01, 400, 300, 800, 600));
        Assert.Equal(ImageViewportState.MinimumScale, viewport.Scale);
        Assert.False(viewport.CanZoomOut);
        Assert.True(viewport.Reset());
        Assert.True(viewport.ZoomAt(16, 400, 300, 800, 600));
        Assert.Equal(ImageViewportState.MaximumScale, viewport.Scale);
        Assert.False(viewport.CanZoomIn);
        Assert.True(viewport.PanBy(24, -12));
        Assert.Equal(24, viewport.OffsetX);
        Assert.Equal(-12, viewport.OffsetY);
        Assert.True(viewport.Reset());
        Assert.True(viewport.IsDefault);
        Assert.True(viewport.CanZoomOut);
    }
}
