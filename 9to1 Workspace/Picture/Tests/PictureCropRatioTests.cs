using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureCropRatioTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedOwners = [];

    [AvaloniaFact]
    public void Required_ratios_fit_inside_entered_bounds_with_explicit_rounding_and_actual_source_pixels()
    {
        using var source = SourcePixels(); var original = Rgba(source);
        var bounds = new CropOperation(1, 1, 11, 7);
        (int Width, int Height, CropOperation Expected, bool Rounded)[] cases =
        [
            (11, 7, bounds, false), // Free: the exact entered bounds.
            (13, 9, new(1, 1, 10, 7), true), // Original source, not current cropped canvas.
            (1, 1, new(3, 1, 7, 7), false),
            (4, 3, new(2, 1, 9, 7), true),
            (3, 2, bounds, true),
            (16, 9, new(1, 1, 11, 6), true),
            (9, 16, new(4, 1, 4, 7), true),
            (5, 3, new(1, 1, 11, 7), true),
        ];
        foreach (var item in cases)
        {
            var fitted = PictureCropRatio.FitInside(bounds, 13, 9, item.Width, item.Height);
            Assert.Equal(item.Expected, fitted.Crop); Assert.Equal(item.Rounded, fitted.IsPixelRounded);
            var crop = fitted.Crop;
            using var rendered = PictureCropService.Render(source, PictureDocument.Create(13, 9).Crop(crop.X, crop.Y, crop.Width, crop.Height));
            AssertCropPixels(original, Rgba(rendered), crop);
            Assert.Equal(new PixelSize(crop.Width, crop.Height), rendered.PixelSize);
        }
        Assert.Equal(original, Rgba(source));
    }

    [Fact]
    public void Exact_ratios_coprime_rounding_centers_and_extreme_or_invalid_bounds_are_truthful()
    {
        var exact = PictureCropRatio.FitInside(new(3, 5, 180, 120), 200, 150, 4, 3);
        Assert.Equal(new CropOperation(13, 5, 160, 120), exact.Crop); Assert.False(exact.IsPixelRounded);
        var coprime = PictureCropRatio.FitInside(new(0, 0, 800, 500), 800, 500, 1279, 719);
        Assert.Equal(new CropOperation(0, 25, 800, 450), coprime.Crop); Assert.True(coprime.IsPixelRounded);
        Assert.Equal(new CropOperation(0, 0, 3, 2), PictureCropRatio.FitInside(new(0, 0, 3, 2), 3, 2, 5, 3).Crop);
        var once = PictureCropRatio.FitInside(new(2, 3, 100, 40), 120, 60, 3, 1);
        Assert.Equal(new CropOperation(2, 6, 100, 33), once.Crop); Assert.True(once.IsPixelRounded);
        Assert.Equal(once, PictureCropRatio.FitInside(once.Crop, 120, 60, 3, 1)); // Draft fit → actual Apply preserves every chosen pixel.
        Assert.Throws<ArgumentOutOfRangeException>(() => PictureCropRatio.FitInside(new(0, 0, 1, 1), 1, 1, 32768, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PictureCropRatio.FitInside(new(0, 0, 1, 1), 1, 1, 1, 32768));
        foreach (var ratio in new[] { 0, -1, 32769, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => PictureCropRatio.FitInside(new(0, 0, 13, 9), 13, 9, ratio, 1));
        CropOperation[] invalidBounds = [new(-1, 0, 2, 2), new(0, 0, 0, 2), new(12, 8, 2, 2), new(int.MaxValue, 0, int.MaxValue, 1)];
        foreach (var invalid in invalidBounds)
            Assert.Throws<ArgumentOutOfRangeException>(() => PictureCropRatio.FitInside(invalid, 13, 9, 1, 1));
    }

    [AvaloniaFact]
    public async Task Ratio_crop_keeps_original_bytes_identity_and_real_shared_undo_saved_redo_and_PNG_pixels()
    {
        var fixture = await SourceAsync(); var session = new PictureEditorSession(new PictureCropService().OpenSource(fixture.Source));
        var id = session.Document.DocumentId; var sourceRevision = session.Document.SourceRevision;
        var crop = PictureCropRatio.FitInside(new(1, 1, 11, 7), 13, 9, 1, 1).Crop;
        using (var rendered = session.Apply("Square crop", document => document.Crop(crop.X, crop.Y, crop.Width, crop.Height)))
            AssertCropPixels(fixture.Pixels, Rgba(rendered), crop);
        using (var undone = session.Undo()) Assert.Equal(fixture.Pixels, Rgba(undone));
        var path = Path.Combine(fixture.Directory, "ratio.picture.json");
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.True(cold.CanRedo); Assert.Equal(id, cold.Document.DocumentId); Assert.Equal(sourceRevision, cold.Document.SourceRevision);
        using (var redone = cold.Redo()) AssertCropPixels(fixture.Pixels, Rgba(redone), crop);
        Assert.Equal(crop, Assert.Single(cold.Document.Operations));
        var png = Path.Combine(fixture.Directory, "crop.png");
        new PictureCropService().ExportPng(cold.Document, png, PictureMetadataExportMode.RemoveAll);
        using (var output = new Bitmap(png)) AssertCropPixels(fixture.Pixels, Rgba(output), crop);
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
        Assert.Null(cold.Document.FileId); // Local fixture paths are no Files authority.
    }

    [AvaloniaFact]
    public async Task Actual_CUI_ratio_custom_and_bounds_drafts_survive_other_actions_and_refuse_invalid_before_mutation()
    {
        var fixture = await SourceAsync(); var window = new MainWindow(new PictureFixtureReadiness());
        await WithWindow(window, fixture, async session =>
        {
            var mode = Find<ComboBox>(window, "CropRatioBox");
            Assert.Equal(new[] { "Free", "Original", "1:1", "4:3", "3:2", "16:9", "9:16", "Custom" }, mode.ItemsSource!.Cast<string>());
            var bounds = Find<TextBox>(window, "CropBoundsBox");
            var width = Find<NumericUpDown>(window, "CropRatioWidthBox"); var height = Find<NumericUpDown>(window, "CropRatioHeightBox");
            bounds.Text = "1, 1, 11, 7"; mode.SelectedIndex = 7; width.Value = 5; height.Value = 3;
            await Click(window, "ZoomInButton"); await Click(window, "MetadataRemoveAllButton");
            Assert.Equal("1, 1, 11, 7", bounds.Text); Assert.Equal(7, mode.SelectedIndex);
            Assert.Equal(5m, width.Value); Assert.Equal(3m, height.Value); Assert.True(width.IsEnabled); Assert.True(height.IsEnabled);
            var before = session.Document.Serialize();
            await Click(window, "FitCropRatioButton"); Assert.Equal(before, session.Document.Serialize());
            Assert.Equal("1, 1, 11, 7", bounds.Text);
            Assert.Contains("rounded", Find<TextBlock>(window, "CropRatioNotice").Text);
            await Click(window, "ApplyCropButton"); var crop = new CropOperation(1, 1, 11, 7);
            Assert.Equal(crop, Assert.Single(session.Document.Operations)); AssertCropPixels(fixture.Pixels, Rgba(Preview(window)), crop);
            Assert.Equal("0, 0, 11, 7", bounds.Text); Assert.Equal(7, mode.SelectedIndex);
            Assert.Equal(5m, width.Value); Assert.Contains("applied", Find<TextBlock>(window, "CropRatioNotice").Text);
            width.Value = 1.5m; height.Value = 1; bounds.Text = "0, 0, 11, 7"; before = session.Document.Serialize();
            await Click(window, "ApplyCropButton"); Assert.Equal(before, session.Document.Serialize());
            Assert.Equal(1.5m, width.Value); Assert.Equal("0, 0, 11, 7", bounds.Text);
            Assert.Contains("whole-number", Find<TextBlock>(window, "StatusText").Text);
            mode.SelectedIndex = -1; await Click(window, "FitCropRatioButton"); Assert.Equal(before, session.Document.Serialize());
            Assert.Equal(-1, mode.SelectedIndex); Assert.Equal("0, 0, 11, 7", bounds.Text);
            mode.SelectedIndex = 0; await Click(window, "UndoButton"); Assert.Equal(fixture.Pixels, Rgba(Preview(window)));
            await Click(window, "RedoButton"); AssertCropPixels(fixture.Pixels, Rgba(Preview(window)), crop);
            await session.SaveAsync(Path.Combine(fixture.Directory, "native-ratio.picture.json"), cancellationToken: TestContext.Current.CancellationToken);
            // Confirmed replacement, unlike unrelated actions, retires every prior ratio draft.
            LoadSource(window, fixture.Source); Assert.Equal(0, mode.SelectedIndex);
            Assert.Equal(1m, width.Value); Assert.Equal(1m, height.Value);
            Assert.Equal("0, 0, 13, 9", bounds.Text);
        });
    }

    [AvaloniaFact]
    public async Task Actual_selected_crop_uses_its_input_canvas_and_Original_ratio_uses_source_after_square_crop()
    {
        var fixture = await SourceAsync(); var window = new MainWindow(new PictureFixtureReadiness());
        await WithWindow(window, fixture, async session =>
        {
            var bounds = Find<TextBox>(window, "CropBoundsBox"); var mode = Find<ComboBox>(window, "CropRatioBox");
            bounds.Text = "0, 0, 5, 9"; await Click(window, "ApplyCropButton");
            var row = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "1. Crop to 5 × 9"));
            await PictureCuiActionFixture.ClickAsync(window, row);
            bounds.Text = "0, 0, 13, 9"; mode.SelectedIndex = 1;
            await Click(window, "FitCropRatioButton"); Assert.Equal("0, 0, 13, 9", bounds.Text);
            await Click(window, "ReplaceSelectedEditButton");
            Assert.Equal(new CropOperation(0, 0, 13, 9), Assert.Single(session.Document.Operations));
            Assert.Equal(fixture.Pixels, Rgba(Preview(window))); Assert.Contains("applied", Find<TextBlock>(window, "CropRatioNotice").Text);
            bounds.Text = "2, 0, 9, 9"; mode.SelectedIndex = 0; await Click(window, "ApplyCropButton");
            Assert.Equal(new PixelSize(9, 9), Preview(window).PixelSize);
            bounds.Text = "0, 0, 9, 9"; mode.SelectedIndex = 1; await Click(window, "FitCropRatioButton");
            Assert.Equal("0, 1, 9, 6", bounds.Text); // Source13:9, not current9:9.
            await Click(window, "ApplyCropButton"); Assert.Equal(new CropOperation(0, 1, 9, 6), session.Document.Operations[^1]);
            await Click(window, "UndoButton"); Assert.Equal(new PixelSize(9, 9), Preview(window).PixelSize);
            await Click(window, "RedoButton"); Assert.Equal(new PixelSize(9, 6), Preview(window).PixelSize);
            var saved = Path.Combine(fixture.Directory, "selected-crop.picture.json");
            await session.SaveAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
            var cold = new PictureEditorSession(await PictureDocument.OpenAsync(saved, TestContext.Current.CancellationToken), saved);
            Assert.Equal(session.Document.DocumentId, cold.Document.DocumentId); Assert.Equal(session.Document.Operations, cold.Document.Operations);
            using var actual = PictureCropService.Render(cold.Document); Assert.Equal(Rgba(Preview(window)), Rgba(actual));
            Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
        });
    }

    private static async Task WithWindow(MainWindow window, SourceFixture fixture, Func<PictureEditorSession, Task> body)
    {
        Exception? bodyFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Crop ratio initialization");
            window.Show(); window.UpdateLayout(); LoadSource(window, fixture.Source);
            var session = Assert.IsType<PictureEditorSession>(typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            await body(session);
        }
        catch (Exception failure) { bodyFailure = failure; RetainedFailedOwners.Add((window, failure)); throw; }
        finally
        {
            if (bodyFailure is null)
            {
                window.Close(); Assert.NotNull(window.OriginalClose); var sameClose = window.OriginalClose!;
                try { await sameClose.ObserveOriginalAsync(window, "Crop ratio original close"); }
                catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
                Assert.True(sameClose.IsCompletedSuccessfully); Assert.Same(sameClose, window.OriginalClose);
            }
        }
    }

    private static void LoadSource(MainWindow window, string path) =>
        typeof(MainWindow).GetMethod("LoadLocalPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [path]);
    private static Task Click(MainWindow window, string id) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, id));
    private static T Find<T>(MainWindow window, string id) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == id);
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private sealed record SourceFixture(string Directory, string Source, byte[] Bytes, byte[] Pixels);
    private static async Task<SourceFixture> SourceAsync()
    {
        var directory = Directory.CreateTempSubdirectory("picture-crop-ratio-").FullName; var path = Path.Combine(directory, "source.png");
        byte[] rgba;
        using (var source = SourcePixels())
        { rgba = Rgba(source); using var output = File.Create(path); source.Save(output); }
        return new(directory, path, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken), rgba);
    }
    private static WriteableBitmap SourcePixels()
    {
        var source = new WriteableBitmap(new(13, 9), new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var framebuffer = source.Lock();
        for (var y = 0; y < 9; y++)
        {
            var row = Enumerable.Range(0, 13).SelectMany(x => new byte[] { (byte)(x * 17), (byte)(y * 23), (byte)(x * 7 + y * 11), 255 }).ToArray();
            Marshal.Copy(row, 0, IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes), row.Length);
        }
        return source;
    }
    private static void AssertCropPixels(byte[] source, byte[] target, CropOperation crop)
    {
        Assert.Equal(crop.Width * crop.Height * 4, target.Length);
        for (var y = 0; y < crop.Height; y++) for (var x = 0; x < crop.Width; x++)
            Assert.Equal(source.AsSpan(((crop.Y + y) * 13 + crop.X + x) * 4, 4).ToArray(), target.AsSpan((y * crop.Width + x) * 4, 4).ToArray());
    }
    private static byte[] Rgba(Bitmap source)
    {
        using var destination = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var framebuffer = destination.Lock(); source.CopyPixels(framebuffer);
        var pixels = new byte[checked(source.PixelSize.Width * source.PixelSize.Height * 4)];
        for (var y = 0; y < source.PixelSize.Height; y++) Marshal.Copy(IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes), pixels, y * source.PixelSize.Width * 4, source.PixelSize.Width * 4);
        return pixels;
    }
}
