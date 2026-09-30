using System.Text;
using Haven.Desktop.Services;
using HavenOS.Apps.Canvas;

namespace Haven.Desktop.Tests;

public sealed class CanvasSvgRasterizerTests
{
    [Fact]
    public void Actual_native_vector_renderer_returns_premultiplied_pixels_and_rejects_external_resources()
    {
        if (!OperatingSystem.IsLinux()) return;
        var red = CanvasSvgRasterizer.Render(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='#ff0000'/></svg>"), 8, 8);
        Assert.Equal(8, red.Width);
        Assert.Equal(8, red.Height);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, red.PremultipliedBgra[..4]);
        var gradient = CanvasSvgRasterizer.Render(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"), 8, 8);
        Assert.NotEqual(gradient.PremultipliedBgra[..4], gradient.PremultipliedBgra[(7 * 4)..(8 * 4)]);
        Assert.Throws<NotSupportedException>(() => CanvasSvgRasterizer.Render(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><use href='file:///private.svg'/></svg>"), 8, 8));
        Assert.Throws<NotSupportedException>(() => CanvasSvgRasterizer.Render(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect fill='url(https://unowned.invalid/image)'/></svg>"), 8, 8));
        Assert.Throws<NotSupportedException>(() => CanvasSvgRasterizer.Render(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,AA=='/></svg>"), 8, 8));
        Assert.Throws<InvalidDataException>(() => CanvasSvgRasterizer.Render(Encoding.UTF8.GetBytes("<!DOCTYPE svg SYSTEM 'file:///private'><svg xmlns='http://www.w3.org/2000/svg'/>"), 8, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasSvgRasterizer.Render([1], 4097, 8));
    }

    [Fact]
    public void Structured_Rnote_document_reopens_and_its_actual_SVG_frame_renders_identically()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var document = CanvasRnoteDocument.Create("Native viewport journey");
        document.DrawStroke([new(10, 20, 0.2), new(30, 40, 0.8), new(90, 100, 0.6)], document.Snapshot.RevisionId);
        var structured = document.Serialize();
        var frame = document.Render();
        Assert.True(frame.Width > 0 && frame.Height > 0);
        var raster = CanvasSvgRasterizer.Render(frame.Svg, 256, 256);
        Assert.Contains(raster.PremultipliedBgra, value => value != 0);
        using var reopened = CanvasRnoteDocument.Open(structured);
        Assert.Equal(document.Snapshot.ArtifactId, reopened.Snapshot.ArtifactId);
        Assert.Equal(document.Snapshot.RevisionId, reopened.Snapshot.RevisionId);
        var restoredFrame = reopened.Render();
        var restored = CanvasSvgRasterizer.Render(restoredFrame.Svg, 256, 256);
        Assert.Equal(raster.PremultipliedBgra, restored.PremultipliedBgra);
    }
}
