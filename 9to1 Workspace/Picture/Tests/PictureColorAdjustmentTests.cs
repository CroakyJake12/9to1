using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Haven.Application;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureColorAdjustmentTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedOwners = [];

    [Fact]
    public void Shared_srgb_primitive_preserves_identity_alpha_and_known_linear_exposure_and_gamma_values()
    {
        byte[] identity = [64, 128, 192, 127, 1, 2, 3, 0]; var original = identity.ToArray();
        new RasterColorAdjustmentProcessor(new()).ApplySrgbRgba8(identity); Assert.Equal(original, identity);
        var exposure = original.ToArray(); new RasterColorAdjustmentProcessor(new(ExposureEv: 1)).ApplySrgbRgba8(exposure);
        Assert.Equal(new byte[] { 90, 176, 255, 127, 1, 2, 3, 0 }, exposure);
        byte[] gamma = [64, 64, 64, 123]; new RasterColorAdjustmentProcessor(new(Gamma: 2)).ApplySrgbRgba8(gamma);
        Assert.Equal(new byte[] { 128, 128, 128, 123 }, gamma);
        byte[] grey = [255, 0, 0, 87]; new RasterColorAdjustmentProcessor(new(Saturation: -1)).ApplySrgbRgba8(grey);
        Assert.Equal(grey[0], grey[1]); Assert.Equal(grey[1], grey[2]); Assert.InRange(grey[0], (byte)126, (byte)128); Assert.Equal(87, grey[3]);
    }

    [Fact]
    public void Shared_settings_reuse_actual_Notes_colour_values_without_mutating_its_other_transform_state()
    {
        var notes = new NotesMediaTransformState { Brightness = .2, Contrast = -.3, Saturation = -.5, Opacity = .4, FlipHorizontal = true };
        Assert.Equal(new RasterColorAdjustment(Brightness: .2, Contrast: -.3, Saturation: -.5), RasterColorAdjustment.FromNotes(notes));
        Assert.Equal(.4, notes.Opacity); Assert.True(notes.FlipHorizontal); Assert.Equal(.2, notes.Brightness);
        notes.Saturation = double.NaN; Assert.Throws<ArgumentOutOfRangeException>(() => RasterColorAdjustment.FromNotes(notes));
    }

    [Fact]
    public void Nonfinite_out_of_range_and_incomplete_pixel_inputs_are_refused_before_pixel_mutation()
    {
        foreach (var invalid in new RasterColorAdjustment[] { new RasterColorAdjustment(ExposureEv: double.NaN), new(ExposureEv: 11),
            new(Brightness: -1.01), new(Contrast: double.PositiveInfinity), new(Saturation: 2), new(Gamma: 0) })
            Assert.Throws<ArgumentOutOfRangeException>(() => new RasterColorAdjustmentProcessor(invalid));
        byte[] incomplete = [64, 128, 192]; var processor = new RasterColorAdjustmentProcessor(new(ExposureEv: 1));
        Assert.Throws<ArgumentException>(() => processor.ApplySrgbRgba8(incomplete)); Assert.Equal(new byte[] { 64, 128, 192 }, incomplete);
    }

    [AvaloniaFact]
    public void Shared_native_framebuffer_adjustment_preserves_actual_partial_alpha_and_source_pixels()
    {
        using var source = Pixels([64, 128, 192, 127, 5, 7, 9, 0]); var before = Rgba(source);
        using var adjusted = Haven.Productivity.NativeUI.SharedRasterColorRenderer.Render(source, new(ExposureEv: 1));
        var after = Rgba(adjusted);
        Assert.Equal(source.PixelSize, adjusted.PixelSize); Assert.Equal(before[3], after[3]); Assert.Equal(before[7], after[7]);
        Assert.InRange(after[0], (byte)88, (byte)92); Assert.InRange(after[1], (byte)174, (byte)178); Assert.Equal(255, after[2]);
        Assert.Equal(before, Rgba(source));
        using var grey = Haven.Productivity.NativeUI.SharedRasterColorRenderer.Render(source, new(Saturation: -1));
        var greyPixels = Rgba(grey); Assert.Equal(greyPixels[0], greyPixels[1]); Assert.Equal(greyPixels[1], greyPixels[2]); Assert.Equal(before[3], greyPixels[3]);
    }

    [AvaloniaFact]
    public async Task Non_destructive_colour_stack_order_modification_and_saved_redo_render_original_full_quality_source()
    {
        var fixture = await SourceAsync(); var session = new PictureEditorSession(new PictureCropService().OpenSource(fixture.Source));
        var id = session.Document.DocumentId;
        using (var exposed = session.Apply("Exposure", document => document.AdjustColor(new(ExposureEv: 1), PictureColorWorkingSpace.Srgb8)))
            Assert.InRange(Rgba(exposed)[0], (byte)88, (byte)92);
        using (session.Apply("Gamma", document => document.AdjustColor(new(Gamma: 2), PictureColorWorkingSpace.Srgb8))) { }
        byte beforeOrder;
        using (var before = PictureCropService.Render(session.Document)) beforeOrder = Rgba(before)[0];
        using (var reordered = session.MoveOperation(session.CaptureOperation(1), 0)) Assert.NotEqual(beforeOrder, Rgba(reordered)[0]);
        using (var changed = session.ReplaceOperation(session.CaptureOperation(1), new ColorAdjustmentOperation(new(ExposureEv: -1), PictureColorWorkingSpace.Srgb8)))
            Assert.True(Rgba(changed)[0] < 128);
        using (session.Undo()) { }
        var path = Path.Combine(fixture.Directory, "colour.picture.json"); await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(id, reopened.Document.DocumentId); Assert.True(reopened.CanRedo);
        using (var redone = reopened.Redo()) Assert.True(Rgba(redone)[0] < 128);
        Assert.Equal(new RasterColorAdjustment(ExposureEv: -1), Assert.IsType<ColorAdjustmentOperation>(reopened.Document.Operations[1]).Settings);
        using (var source = PictureCropService.OpenVerifiedSource(reopened.Document)) Assert.Equal(64, Rgba(source)[0]);
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Unsupported_working_space_and_invalid_saved_colour_operation_cannot_change_current_session()
    {
        var session = new PictureEditorSession(PictureDocument.Create(2, 1)); var before = session.Document.Serialize();
        Assert.Throws<NotSupportedException>(() => session.Apply("Unknown space", document => document.AdjustColor(new(ExposureEv: 1), (PictureColorWorkingSpace)2)));
        Assert.Equal(before, session.Document.Serialize()); Assert.False(session.CanUndo);
        var malformed = new PictureDocument { CanvasWidth = 2, CanvasHeight = 1, InitialCanvasWidth = 2, InitialCanvasHeight = 1,
            Operations = [new ColorAdjustmentOperation(new(Gamma: -1), PictureColorWorkingSpace.Srgb8)] };
        Assert.Throws<InvalidDataException>(() => malformed.Serialize());
    }

    [AvaloniaFact]
    public async Task Actual_CUI_working_space_choice_and_colour_drafts_apply_modify_undo_redo_and_save_same_document()
    {
        var fixture = await SourceAsync(); var window = new MainWindow(new PictureFixtureReadiness()); Exception? failure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Colour initialization");
            window.Show(); window.UpdateLayout();
            typeof(MainWindow).GetMethod("LoadLocalPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [fixture.Source]);
            var session = Assert.IsType<PictureEditorSession>(typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            var id = session.Document.DocumentId; var before = session.Document.Serialize();
            Find<NumericUpDown>(window, "ColorExposureBox").Value = 1;
            await Click(window, "ApplyColorAdjustmentButton"); Assert.Equal(before, session.Document.Serialize());
            Assert.Contains("working colour space", Find<TextBlock>(window, "StatusText").Text);
            Find<ComboBox>(window, "ColorWorkingSpaceBox").SelectedIndex = 1;
            await Click(window, "ZoomInButton"); await Click(window, "MetadataPreserveButton");
            Assert.Equal(1m, Find<NumericUpDown>(window, "ColorExposureBox").Value);
            await Click(window, "ApplyColorAdjustmentButton"); Assert.Equal(id, session.Document.DocumentId);
            Assert.InRange(Rgba(Preview(window))[0], (byte)88, (byte)92); Assert.Equal(0m, Find<NumericUpDown>(window, "ColorExposureBox").Value);
            await Click(window, "UndoButton"); Assert.Equal(64, Rgba(Preview(window))[0]);
            await Click(window, "RedoButton"); Assert.InRange(Rgba(Preview(window))[0], (byte)88, (byte)92);
            var label = "1. Colour · sRGB · exposure +1 EV · brightness 0 · contrast 0 · saturation 0 · gamma 1";
            await PictureCuiActionFixture.ClickAsync(window, window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label)));
            Assert.Equal(1m, Find<NumericUpDown>(window, "ColorExposureBox").Value);
            Find<NumericUpDown>(window, "ColorSaturationBox").Value = -1;
            await Click(window, "ReplaceSelectedEditButton"); var grey = Rgba(Preview(window)); Assert.Equal(grey[0], grey[1]); Assert.Equal(grey[1], grey[2]);
            Assert.Equal(-1, Assert.IsType<ColorAdjustmentOperation>(Assert.Single(session.Document.Operations)).Settings.Saturation);
            var path = Path.Combine(fixture.Directory, "native-colour.picture.json"); await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            using (var rendered = PictureCropService.Render(reopened.Document)) Assert.Equal(grey, Rgba(rendered));
            Assert.Equal(id, reopened.Document.DocumentId); Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
        }
        catch (Exception original) { failure = original; RetainedFailedOwners.Add((window, original)); throw; }
        finally
        {
            if (failure is null)
            {
                window.Close(); Assert.NotNull(window.OriginalClose); var sameClose = window.OriginalClose!;
                try { await sameClose.ObserveOriginalAsync(window, "Colour original close"); }
                catch (Exception original) { RetainedFailedOwners.Add((window, original)); throw; }
                Assert.True(sameClose.IsCompletedSuccessfully); Assert.Same(sameClose, window.OriginalClose);
            }
        }
    }

    [AvaloniaFact]
    public async Task Adjusted_export_preserves_non_colour_metadata_and_privacy_without_copying_stale_source_colour_claims()
    {
        var fixture = await SourceAsync(); var source = Path.Combine(fixture.Directory, "metadata-source.png");
        using (var original = new Bitmap(fixture.Source)) using (var output = File.Create(source)) original.Save(output);
        using (var originalMetadata = TagLib.File.Create(source))
        {
            var png = Assert.IsAssignableFrom<TagLib.Png.PngTag>(originalMetadata.GetTag(TagLib.TagTypes.Png, create: true));
            png.Title = "Retained image title"; png.Comment = "Retained capture comment";
            var image = Assert.IsAssignableFrom<TagLib.Image.File>(originalMetadata);
            image.ImageTag.Latitude = 51.5; image.ImageTag.Longitude = -.12;
            var exif = Assert.Single(image.ImageTag.AllTags.OfType<TagLib.IFD.IFDTag>());
            exif.ExifIFD.SetEntry(0, new TagLib.IFD.Entries.ShortIFDEntry(40961, 2));
            originalMetadata.Save();
        }
        var originalBytes = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
        using (var originalMetadata = TagLib.File.Create(source))
        {
            var image = Assert.IsAssignableFrom<TagLib.Image.File>(originalMetadata);
            Assert.True(Assert.Single(image.ImageTag.AllTags.OfType<TagLib.IFD.IFDTag>()).ExifIFD.ContainsTag(0, (ushort)40961));
        }
        var service = new PictureCropService();
        var document = service.OpenSource(source).AdjustColor(new(ExposureEv: 1), PictureColorWorkingSpace.Srgb8);
        foreach (var mode in Enum.GetValues<PictureMetadataExportMode>())
        {
            var destination = Path.Combine(fixture.Directory, "adjusted-" + mode + ".png"); service.ExportPng(document, destination, mode);
            using (var pixels = new Bitmap(destination)) Assert.InRange(Rgba(pixels)[0], (byte)88, (byte)92);
            using var exportedMetadata = TagLib.File.Create(destination);
            if (mode == PictureMetadataExportMode.RemoveAll)
            { Assert.Null(exportedMetadata.GetTag(TagLib.TagTypes.Png, create: false)); continue; }
            var exportedImage = Assert.IsAssignableFrom<TagLib.Image.File>(exportedMetadata);
            Assert.Equal("Retained image title", exportedImage.ImageTag.Title); Assert.Equal("Retained capture comment", exportedImage.ImageTag.Comment);
            foreach (var exif in exportedImage.ImageTag.AllTags.OfType<TagLib.IFD.IFDTag>())
                Assert.False(exif.ExifIFD.ContainsTag(0, (ushort)40961));
            if (mode == PictureMetadataExportMode.RemoveLocation)
            { Assert.Null(exportedImage.ImageTag.Latitude); Assert.Null(exportedImage.ImageTag.Longitude); }
            else { Assert.Equal(51.5, exportedImage.ImageTag.Latitude); Assert.Equal(-.12, exportedImage.ImageTag.Longitude); }
        }
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
        Assert.Equal(PictureColorWorkingSpace.Srgb8, Assert.IsType<ColorAdjustmentOperation>(Assert.Single(document.Operations)).WorkingSpace);
    }

    private static Task Click(MainWindow window, string name) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, name));
    private static T Find<T>(MainWindow window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private sealed record SourceFixture(string Directory, string Source, byte[] Bytes);
    private static async Task<SourceFixture> SourceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "picture-colour-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.bmp"); var bytes = new byte[62]; bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2); BitConverter.GetBytes(54).CopyTo(bytes, 10); BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(2).CopyTo(bytes, 18); BitConverter.GetBytes(1).CopyTo(bytes, 22); BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28); BitConverter.GetBytes(8).CopyTo(bytes, 34);
        bytes[54] = 192; bytes[55] = 128; bytes[56] = 64; bytes[57] = 96; bytes[58] = 64; bytes[59] = 32;
        await File.WriteAllBytesAsync(source, bytes, TestContext.Current.CancellationToken); return new(directory, source, bytes);
    }
    private static WriteableBitmap Pixels(byte[] sameRgba)
    {
        var bitmap = new WriteableBitmap(new(sameRgba.Length / 4, 1), new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var pixels = bitmap.Lock(); Marshal.Copy(sameRgba, 0, pixels.Address, sameRgba.Length); return bitmap;
    }
    private static byte[] Rgba(Bitmap sameSource)
    {
        using var converted = new WriteableBitmap(sameSource.PixelSize, sameSource.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var pixels = converted.Lock(); sameSource.CopyPixels(pixels);
        var result = new byte[checked(sameSource.PixelSize.Width * sameSource.PixelSize.Height * 4)];
        for (var y = 0; y < sameSource.PixelSize.Height; y++)
            Marshal.Copy(IntPtr.Add(pixels.Address, checked(y * pixels.RowBytes)), result, y * sameSource.PixelSize.Width * 4, sameSource.PixelSize.Width * 4);
        return result;
    }
}
