using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Haven.Application;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PicturePixelCropTests
{
    [AvaloniaTheory]
    [InlineData(96)]
    [InlineData(192)]
    public void Cropping_copies_every_unpremultiplied_sample_and_density_without_a_render_roundtrip(int dpi)
    {
        using var source = new WriteableBitmap(new PixelSize(7, 5), new Vector(dpi, dpi), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var frame = source.Lock())
        {
            for (var y = 0; y < 5; y++) for (var x = 0; x < 7; x++)
            {
                byte[] sample = (x + y) % 4 == 0 ? [0, 0, 0, 0] : [47, (byte)(68 + x), (byte)(43 + y), 191];
                Marshal.Copy(sample, 0, IntPtr.Add(frame.Address, y * frame.RowBytes + x * 4), 4);
            }
        }
        var before = Rgba(source); var expected = Extract(before, 7, 1, 1, 5, 3);
        using var cropped = PictureCropService.Render(source, PictureDocument.Create(7, 5).Crop(1, 1, 5, 3));
        Assert.Equal(new PixelSize(5, 3), cropped.PixelSize); Assert.Equal(source.Dpi, cropped.Dpi);
        Assert.Equal(source.Format, cropped.Format); Assert.Equal(source.AlphaFormat, cropped.AlphaFormat);
        Assert.Equal(expected, Rgba(cropped)); Assert.Equal(before, Rgba(source));
        // The no-operation owned render is also an exact full-canvas copy.
        using var full = PictureCropService.Render(source, PictureDocument.Create(7, 5)); Assert.Equal(before, Rgba(full));
        using var twice = PictureCropService.Render(source, PictureDocument.Create(7, 5).Crop(1, 1, 5, 3).Crop(1, 1, 3, 2));
        Assert.Equal(Extract(before, 7, 2, 2, 3, 2), Rgba(twice)); Assert.Equal(source.Dpi, twice.Dpi);
        var path = Path.Combine(Directory.CreateTempSubdirectory("picture-exact-crop-").FullName, "crop.png");
        using (var output = File.Create(path)) cropped.Save(output);
        Assert.Equal(expected, ReadOriginalPngRgba(path, 5, 3));
    }

    [AvaloniaTheory]
    [InlineData(96)]
    [InlineData(192)]
    public void Real_premultiplied_render_target_crop_preserves_its_original_samples_at_both_densities(int dpi)
    {
        using var source = new RenderTargetBitmap(new PixelSize(4, 2), new Vector(dpi, dpi));
        using (var draw = source.CreateDrawingContext())
        using (var pixels = draw.PushTransform(Matrix.CreateScale(96d / dpi, 96d / dpi)))
        {
            draw.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 2, 1));
            draw.DrawRectangle(Brushes.Blue, null, new Rect(2, 0, 2, 1));
            draw.DrawRectangle(new SolidColorBrush(Color.FromArgb(191, 47, 68, 43)), null, new Rect(1, 1, 2, 1));
        }
        var before = NativeSamples(source);
        using var result = PictureCropService.Render(source, PictureDocument.Create(4, 2).Crop(1, 0, 2, 2));
        Assert.Equal(source.Dpi, result.Dpi); Assert.Equal(source.Format, result.Format); Assert.Equal(source.AlphaFormat, result.AlphaFormat);
        Assert.Equal(Extract(before, 4, 1, 0, 2, 2), NativeSamples(result));
        Assert.Equal(Extract(Rgba(source), 4, 1, 0, 2, 2), Rgba(result)); Assert.Equal(before, NativeSamples(source));
    }

    [AvaloniaFact]
    public async Task Actual_pixelation_then_crop_undo_and_saved_cold_replay_preserve_exact_RGBA_source_and_history()
    {
        var directory = Directory.CreateTempSubdirectory("picture-crop-history-").FullName; var originalPath = Path.Combine(directory, "original.png");
        using (var source = new WriteableBitmap(new PixelSize(7, 5), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul))
        {
            using (var frame = source.Lock())
                for (var y = 0; y < 5; y++) for (var x = 0; x < 7; x++)
                    Marshal.Copy(new byte[] { (byte)(x * 35), (byte)(y * 51), (byte)(x * 19 + y * 13), (byte)((x + y) % 4 == 1 ? 128 : 255) }, 0,
                        IntPtr.Add(frame.Address, y * frame.RowBytes + x * 4), 4);
            using var output = File.Create(originalPath); source.Save(output);
        }
        var originalBytes = await File.ReadAllBytesAsync(originalPath, TestContext.Current.CancellationToken);
        var session = new PictureEditorSession(new PictureCropService().OpenSource(originalPath));
        byte[] pixelated; using (var result = session.Apply("Pixelate", document => document.Pixelate(new RasterPixelation(1, 1, 5, 3, 2)))) pixelated = Rgba(result);
        var expected = Extract(pixelated, 7, 0, 0, 3, 2);
        using (var result = session.Apply("Crop", document => document.Crop(0, 0, 3, 2))) Assert.Equal(expected, Rgba(result));
        var cropOperation = Assert.IsType<DocumentOperationMetadata>(session.LastOperation);
        Assert.Equal("Crop", cropOperation.Name); Assert.Equal(DocumentOperationOrigin.User, cropOperation.Origin); Assert.NotEqual(Guid.Empty, cropOperation.Id);
        var id = session.Document.DocumentId; var revision = session.Document.SourceRevision;
        using (var undo = session.Undo()) Assert.Equal(pixelated, Rgba(undo));
        using (var redo = session.Redo()) Assert.Equal(expected, Rgba(redo));
        var navigation = Assert.IsType<DocumentOperationMetadata>(session.LastOperation);
        Assert.Equal("Redo Crop", navigation.Name); Assert.Equal(DocumentOperationOrigin.System, navigation.Origin);
        Assert.NotEqual(Guid.Empty, navigation.Id); Assert.NotEqual(cropOperation.Id, navigation.Id);
        // Navigation issues its own metadata while the retained undo entry
        // keeps the original user edit identity and complete descriptive data.
        Assert.Equal(cropOperation, Assert.IsType<ProductivitySnapshotHistory>(session.CaptureDocumentForSave().SemanticHistory).Undo.Last().Operation);
        var editable = Path.Combine(directory, "editable.picture.json"); await session.SaveAsync(editable, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(editable, TestContext.Current.CancellationToken), editable);
        Assert.Equal(id, cold.Document.DocumentId); Assert.Equal(revision, cold.Document.SourceRevision);
        using (var actual = PictureCropService.Render(cold.Document)) Assert.Equal(expected, Rgba(actual));
        using (var undo = cold.Undo()) Assert.Equal(pixelated, Rgba(undo));
        using (var redo = cold.Redo()) Assert.Equal(expected, Rgba(redo));
        Assert.Equal(cropOperation, Assert.IsType<ProductivitySnapshotHistory>(cold.CaptureDocumentForSave().SemanticHistory).Undo.Last().Operation);
        var png = Path.Combine(directory, "export.png"); new PictureCropService().ExportPng(cold.Document, png, PictureMetadataExportMode.RemoveAll);
        Assert.Equal(expected, ReadOriginalPngRgba(png, 3, 2));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(originalPath, TestContext.Current.CancellationToken));
    }

    private static byte[] Extract(byte[] original, int stridePixels, int x, int y, int width, int height)
    {
        var result = new byte[checked(width * height * 4)];
        for (var row = 0; row < height; row++) original.AsSpan(((y + row) * stridePixels + x) * 4, width * 4).CopyTo(result.AsSpan(row * width * 4, width * 4));
        return result;
    }
    private static byte[] NativeSamples(Bitmap source)
    {
        Assert.NotNull(source.Format); Assert.Equal(32, source.Format!.Value.BitsPerPixel);
        var bytes = new byte[checked(source.PixelSize.Width * source.PixelSize.Height * 4)]; var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { source.CopyPixels(new PixelRect(source.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, source.PixelSize.Width * 4); }
        finally { handle.Free(); }
        return bytes;
    }
    private static byte[] Rgba(Bitmap source)
    {
        using var converted = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var pixels = converted.Lock(); source.CopyPixels(pixels); var rowBytes = checked(source.PixelSize.Width * 4);
        var result = new byte[checked(rowBytes * source.PixelSize.Height)];
        for (var y = 0; y < source.PixelSize.Height; y++) Marshal.Copy(IntPtr.Add(pixels.Address, y * pixels.RowBytes), result, y * rowBytes, rowBytes);
        return result;
    }
    private static byte[] ReadOriginalPngRgba(string path, int width, int height)
    {
        using var stream = File.OpenRead(path); using var codec = SkiaSharp.SKCodec.Create(stream) ?? throw new InvalidDataException("The original PNG fixture did not decode.");
        Assert.Equal(SkiaSharp.SKEncodedImageFormat.Png, codec.EncodedFormat); Assert.Equal(width, codec.Info.Width); Assert.Equal(height, codec.Info.Height);
        var info = new SkiaSharp.SKImageInfo(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
        using var pixels = new SkiaSharp.SKBitmap(info); Assert.Equal(SkiaSharp.SKCodecResult.Success, codec.GetPixels(info, pixels.GetPixels()));
        var result = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++) Marshal.Copy(IntPtr.Add(pixels.GetPixels(), y * pixels.RowBytes), result, y * width * 4, width * 4);
        return result;
    }
}
