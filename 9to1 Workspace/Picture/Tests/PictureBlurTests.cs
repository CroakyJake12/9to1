using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Haven.Application;
using Haven.Productivity.NativeUI;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureBlurTests
{
    private static readonly List<(object Owner, Task? Original, Exception Failure)> RetainedFailures = [];
    private static readonly List<(object Owner, Task Original)> RetainedOriginals = [];

    [AvaloniaTheory]
    [InlineData(96)]
    [InlineData(192)]
    public void Every_RGBA_pixel_matches_independent_Gaussian_including_canvas_edges_alpha_and_unchanged_outside(int dpi)
    {
        using var source = SourcePixels(dpi); var original = Rgba(source);
        foreach (var settings in new[] { new RasterBlur(1, 1, 5, 3, 2), new RasterBlur(0, 0, 7, 5, 1), new RasterBlur(0, 0, 7, 5, 32), new RasterBlur(6, 4, 1, 1, 7) })
        {
            using var direct = SharedRasterBlurRenderer.Render(source, settings);
            using var actual = PictureCropService.Render(source, PictureDocument.Create(7, 5).Blur(settings));
            var expected = Golden(original, 7, 5, settings);
            Assert.Equal(source.PixelSize, actual.PixelSize); Assert.Equal(source.Dpi, actual.Dpi);
            Assert.Equal(expected, Rgba(direct)); Assert.Equal(expected, Rgba(actual)); Assert.Equal(original, Rgba(source));
            var directory = Directory.CreateTempSubdirectory("picture-blur-density-"); var png = Path.Combine(directory.FullName, "blurred.png");
            using (var output = File.Create(png)) actual.Save(output);
            Assert.Equal(expected, ReadOriginalPngRgba(png));
        }
        using var tiny = new WriteableBitmap(new(1, 1), new(dpi, dpi), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var locked = tiny.Lock()) Marshal.Copy(new byte[] { 255, 0, 0, 128 }, 0, locked.Address, 4);
        using var blurred = SharedRasterBlurRenderer.Render(tiny, new(0, 0, 1, 1, 32));
        Assert.Equal(Rgba(tiny), Rgba(blurred));
    }

    [Fact]
    public void Saved_geometry_required_settings_legacy_versions_and_invalid_ranges_refuse_without_mutating_original()
    {
        var document = PictureDocument.Create(7, 5); var original = document.Serialize();
        RasterBlur[] invalid = [new(-1, 0, 2, 2, 2), new(0, -1, 2, 2, 2), new(0, 0, 0, 2, 2),
            new(6, 0, 2, 2, 2), new(0, 4, 2, 2, 2), new(int.MaxValue, 0, int.MaxValue, 2, 2), new(0, 0, 2, 2, 0), new(0, 0, 2, 2, 33)];
        foreach (var item in invalid) Assert.Throws<ArgumentOutOfRangeException>(() => document.Blur(item));
        Assert.Equal(original, document.Serialize());
        var settings = new RasterBlur(1, 1, 5, 3, 2); var valid = document.Blur(settings);
        Assert.Equal(new BlurOperation(settings), Assert.Single(PictureDocument.Deserialize(valid.Serialize()).Operations));
        var illegalLegacy = JsonNode.Parse(valid.Serialize())!.AsObject(); illegalLegacy["schemaVersion"] = 6;
        Assert.Throws<InvalidDataException>(() => PictureDocument.Deserialize(JsonSerializer.SerializeToUtf8Bytes(illegalLegacy)));
        var missing = JsonNode.Parse(valid.Serialize())!.AsObject(); missing["operations"]![0]!.AsObject().Remove("settings");
        Assert.Throws<JsonException>(() => PictureDocument.Deserialize(JsonSerializer.SerializeToUtf8Bytes(missing)));
        foreach (var property in new[] { "x", "y", "width", "height", "radius" })
        {
            var absent = JsonNode.Parse(valid.Serialize())!.AsObject(); absent["operations"]![0]!["settings"]!.AsObject().Remove(property);
            Assert.Throws<JsonException>(() => PictureDocument.Deserialize(JsonSerializer.SerializeToUtf8Bytes(absent)));
        }
        for (var schema = 1; schema <= 6; schema++)
        {
            var bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace($"\"schemaVersion\":{PictureDocument.CurrentSchemaVersion}", "\"schemaVersion\":" + schema, StringComparison.Ordinal));
            var compatible = PictureDocument.Deserialize(bytes); Assert.Equal(bytes, compatible.Serialize()); Assert.Equal(schema, compatible.Flip(true).SchemaVersion);
            Assert.Equal(7, compatible.Blur(settings).SchemaVersion);
        }
    }

    [AvaloniaFact]
    public async Task Actual_stack_edit_reorder_refusal_undo_and_schema_six_cold_history_preserve_original_hashes_pixels_and_source()
    {
        var fixture = await SourceAsync(); var initial = new PictureCropService().OpenSource(fixture.Source);
        var legacyBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(initial.Serialize()).Replace($"\"schemaVersion\":{PictureDocument.CurrentSchemaVersion}", "\"schemaVersion\":6", StringComparison.Ordinal));
        var session = new PictureEditorSession(PictureDocument.Deserialize(legacyBytes));
        using (session.Apply("Original schema6 flip", document => document.Flip(true))) { }
        var legacyPath = Path.Combine(fixture.Directory, "legacy-six.picture.json"); await session.SaveAsync(legacyPath, cancellationToken: TestContext.Current.CancellationToken);
        var old = await PictureDocument.OpenAsync(legacyPath, TestContext.Current.CancellationToken); Assert.Equal(6, old.SchemaVersion);
        var frame = Assert.Single(old.SemanticHistory!.Undo); var restored = new PictureEditorSession(old, legacyPath);
        byte[] priorPixels; using (var prior = PictureCropService.Render(restored.Document)) priorPixels = Rgba(prior);
        var settings = new RasterBlur(1, 1, 5, 3, 2); var expected = Golden(priorPixels, 7, 5, settings);
        using (var actual = restored.Apply("Gaussian local blur", document => document.Blur(settings))) Assert.Equal(expected, Rgba(actual));
        using (var undo = restored.Undo()) Assert.Equal(priorPixels, Rgba(undo)); Assert.Equal(7, restored.Document.SchemaVersion);
        var saved = Path.Combine(fixture.Directory, "forward-seven.picture.json"); await restored.SaveAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        var forward = await PictureDocument.OpenAsync(saved, TestContext.Current.CancellationToken);
        Assert.Equal(frame, Assert.Single(forward.SemanticHistory!.Undo)); Assert.Equal(7, forward.SchemaVersion);
        var cold = new PictureEditorSession(forward, saved);
        using (var original = cold.Undo()) Assert.Equal(fixture.Pixels, Rgba(original));
        using (var flipped = cold.Redo()) Assert.Equal(priorPixels, Rgba(flipped));
        using (var blurred = cold.Redo()) Assert.Equal(expected, Rgba(blurred));
        using (var replaced = cold.ReplaceOperation(cold.CaptureOperation(1), new BlurOperation(settings with { Radius = 1 })))
            Assert.Equal(Golden(priorPixels, 7, 5, settings with { Radius = 1 }), Rgba(replaced));
        using (var removed = cold.RemoveOperation(cold.CaptureOperation(1))) Assert.Equal(priorPixels, Rgba(removed));
        using (cold.Apply("Grow actual input canvas", document => document.ResizeCanvas(9, 5, 0, 0))) { }
        using (cold.Apply("Blur transparent added area", document => document.Blur(new(7, 1, 2, 2, 2)))) { }
        var unchanged = cold.Document.Serialize(); var last = cold.LastOperation;
        Assert.Throws<ArgumentOutOfRangeException>(() => cold.MoveOperation(cold.CaptureOperation(2), 1));
        Assert.Equal(unchanged, cold.Document.Serialize()); Assert.Same(last, cold.LastOperation);
        var output = Path.Combine(fixture.Directory, "history.png"); new PictureCropService().ExportPng(cold.Document, output, PictureMetadataExportMode.RemoveAll);
        using var rendered = PictureCropService.Render(cold.Document); Assert.Equal(Rgba(rendered), ReadOriginalPngRgba(output));
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Local_blur_preserves_the_same_shared_vector_graph_identity_and_unknown_payload()
    {
        var session = new PictureEditorSession(PictureDocument.Create(7, 5));
        var vector = new HomeVectorShapeObjectHandler().Project(DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle));
        var payload = JsonNode.Parse(vector.Content.GetRawText())!.AsObject(); payload["FutureBlurFixture"] = new JsonObject { ["retained"] = 81 };
        vector = vector with { Content = JsonSerializer.SerializeToElement(payload) };
        using (session.AddSharedVector(vector)) { }
        var originalGraph = session.Document.CompositionState!; byte[] original;
        using (var before = PictureCropService.Render(session.Document)) original = Rgba(before);
        var settings = new RasterBlur(1, 1, 5, 3, 2);
        using (var result = session.Apply("Blur composed rectangle", document => document.Blur(settings))) Assert.Equal(Golden(original, 7, 5, settings), Rgba(result));
        Assert.Equal(originalGraph, session.Document.CompositionState);
        var retained = PictureCompositionAdapter.ReadVector(PictureCompositionAdapter.Read(session.Document).Pages[0].Objects.Single(item => item.ObjectId == vector.ObjectId));
        Assert.Equal(vector.ObjectId, retained.ObjectId); Assert.Equal(vector.Content.GetRawText(), retained.Content.GetRawText());
        using (var undo = session.Undo()) Assert.Equal(original, Rgba(undo)); Assert.Equal(originalGraph, session.Document.CompositionState);
    }

    [AvaloniaFact]
    public async Task Actual_CUI_radius_region_original_draft_binding_prior_canvas_and_cold_editability_work_together()
    {
        var fixture = await SourceAsync(); var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Blur actual initialization");
            window.Show(); window.UpdateLayout(); LoadSource(window, fixture.Source);
            var session = Assert.IsType<PictureEditorSession>(typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            var bounds = Find<TextBox>(window, "BlurBoundsBox"); var radius = Find<NumericUpDown>(window, "BlurRadiusBox");
            var width = Find<NumericUpDown>(window, "WidthBox"); var crop = Find<TextBox>(window, "CropBoundsBox");
            bounds.Text = "1, 1, 5, 3"; radius.Value = 2; width.Value = 6; crop.Text = "0, 0, 3, 2";
            await Click(window, "ZoomInButton"); await Click(window, "MetadataRemoveAllButton");
            Assert.Equal("1, 1, 5, 3", bounds.Text); Assert.Equal(2m, radius.Value); Assert.Equal(6m, width.Value); Assert.Equal("0, 0, 3, 2", crop.Text);
            var before = session.Document.Serialize(); bounds.Text = "6, 1, 2, 3";
            await Click(window, "ApplyBlurButton"); Assert.Equal(before, session.Document.Serialize()); Assert.Equal("6, 1, 2, 3", bounds.Text);
            bounds.Text = "1, 1, 5, 3"; await Click(window, "ApplyBlurButton"); var settings = new RasterBlur(1, 1, 5, 3, 2);
            Assert.Equal(new BlurOperation(settings), Assert.Single(session.Document.Operations)); Assert.Equal(Golden(fixture.Pixels, 7, 5, settings), Rgba(Preview(window)));
            Assert.Equal(6m, width.Value); Assert.Equal("0, 0, 3, 2", crop.Text);
            var row = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "1. Blur 1, 1 · 5 × 3 · radius 2"));
            await PictureCuiActionFixture.ClickAsync(window, row); Assert.Equal("1, 1, 5, 3", bounds.Text); radius.Value = 1;
            await Click(window, "ReplaceSelectedEditButton"); Assert.Equal(new BlurOperation(settings with { Radius = 1 }), Assert.Single(session.Document.Operations));
            Assert.Equal(Golden(fixture.Pixels, 7, 5, settings with { Radius = 1 }), Rgba(Preview(window)));
            await Click(window, "UndoButton"); Assert.Equal(Golden(fixture.Pixels, 7, 5, settings), Rgba(Preview(window)));
            await Click(window, "RedoButton"); Assert.Equal(Golden(fixture.Pixels, 7, 5, settings with { Radius = 1 }), Rgba(Preview(window)));
            // A selected earlier blur evaluates its entered bounds against the
            // original preceding canvas, even after a later smaller Crop.
            await Click(window, "ApplyCropButton");
            row = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "1. Blur 1, 1 · 5 × 3 · radius 1"));
            await PictureCuiActionFixture.ClickAsync(window, row); Assert.Equal("1, 1, 5, 3", bounds.Text); radius.Value = 2;
            await Click(window, "ReplaceSelectedEditButton");
            var full = Golden(fixture.Pixels, 7, 5, settings); var cropped = new byte[3 * 2 * 4];
            for (var y = 0; y < 2; y++) full.AsSpan(y * 7 * 4, 3 * 4).CopyTo(cropped.AsSpan(y * 3 * 4, 3 * 4));
            Assert.Equal(cropped, Rgba(Preview(window)));
            // A pending radius is not silently retargeted by another accepted edit.
            radius.Value = 3; var oldBounds = bounds.Text; await Click(window, "FlipHorizontalButton");
            Assert.False(Find<Button>(window, "ApplyBlurButton").IsEnabled); Assert.Equal(oldBounds, bounds.Text); Assert.Equal(3m, radius.Value);
            Assert.Contains("previous", Find<TextBlock>(window, "BlurDraftNotice").Text);
            await Click(window, "ResetBlurDraftButton"); Assert.True(Find<Button>(window, "ApplyBlurButton").IsEnabled); Assert.Equal("0, 0, 3, 2", bounds.Text);
            var path = Path.Combine(fixture.Directory, "native-blur.picture.json"); await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            using (var rendered = PictureCropService.Render(cold.Document)) Assert.Equal(Rgba(Preview(window)), Rgba(rendered));
            LoadSource(window, fixture.Source); Assert.Equal("0, 0, 7, 5", bounds.Text); Assert.Equal(2m, radius.Value);
            Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
        }
        catch (Exception failure) { firstFailure = failure; RetainedFailures.Add((window, window.OriginalCommand, failure)); throw; }
        finally { await RetireOriginalWindowAsync(window, firstFailure); }
    }

    private static async Task RetireOriginalWindowAsync(MainWindow window, Exception? firstFailure)
    {
        if (firstFailure is null)
        {
            window.Close(); var close = window.OriginalClose; Assert.NotNull(close); RetainedOriginals.Add((window, close!));
            try { await close!.ObserveOriginalAsync(window, "Blur original close"); Assert.True(close.IsCompletedSuccessfully); Assert.Same(close, window.OriginalClose); }
            catch (Exception failure) { RetainedFailures.Add((window, close, close!.Exception ?? failure)); throw; }
            return;
        }
        var children = new List<Task>(); var failures = new List<Exception> { firstFailure };
        if (window.OriginalCommand is { } command) children.Add(command); if (window.OriginalInitialization is { } initialization) children.Add(initialization);
        try { children.Add(window.SceneHost.CloseOriginalAsync()); }
        catch (Exception failure) { RetainedFailures.Add((window.SceneHost, null, failure)); failures.Add(failure); }
        foreach (var original in children.Distinct<Task>(ReferenceEqualityComparer.Instance))
        {
            RetainedFailures.Add((window, original, firstFailure));
            try { await original.ObserveOriginalAsync(window, "Failed blur child retirement"); }
            catch (Exception failure) { var cause = original.Exception ?? failure; RetainedFailures.Add((window, original, cause)); if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause); }
        }
        if (failures.Count > 1) throw new AggregateException("Blur body and actual child retirement failures.", failures);
    }
    private static void LoadSource(MainWindow window, string path) => typeof(MainWindow).GetMethod("LoadLocalPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [path]);
    private static Task Click(MainWindow window, string id) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, id));
    private static T Find<T>(MainWindow window, string id) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == id);
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private sealed record SourceFixture(string Directory, string Source, byte[] Bytes, byte[] Pixels);
    private static async Task<SourceFixture> SourceAsync()
    {
        var directory = Directory.CreateTempSubdirectory("picture-blur-").FullName; var sourcePath = Path.Combine(directory, "source.png");
        using (var source = SourcePixels(96)) using (var output = File.Create(sourcePath)) source.Save(output);
        using var reopened = new Bitmap(sourcePath);
        return new(directory, sourcePath, await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken), Rgba(reopened));
    }
    private static WriteableBitmap SourcePixels(int dpi)
    {
        var source = new WriteableBitmap(new(7, 5), new(dpi, dpi), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = source.Lock();
        for (var y = 0; y < 5; y++)
        {
            var row = Enumerable.Range(0, 7).SelectMany(x => new byte[] { (byte)(x * 35), (byte)(y * 51), (byte)(x * 19 + y * 13),
                (byte)((x + y) % 4 == 0 ? 0 : (x + y) % 4 == 1 ? 128 : 255) }).ToArray();
            Marshal.Copy(row, 0, IntPtr.Add(buffer.Address, y * buffer.RowBytes), row.Length);
        }
        return source;
    }
    // The file is RGBA8 PNG. Verify its exact unpremultiplied stored samples
    // with the maintained codec, independently of Avalonia display decoding.
    // Premultiplying for display and unpremultiplying can lose a low-alpha byte.
    private static byte[] ReadOriginalPngRgba(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SkiaSharp.SKCodec.Create(stream) ?? throw new InvalidDataException("The original PNG fixture did not decode.");
        Assert.Equal(SkiaSharp.SKEncodedImageFormat.Png, codec.EncodedFormat);
        var info = new SkiaSharp.SKImageInfo(codec.Info.Width, codec.Info.Height,
            SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
        using var decoded = new SkiaSharp.SKBitmap(info);
        Assert.Equal(SkiaSharp.SKCodecResult.Success, codec.GetPixels(info, decoded.GetPixels()));
        var rowLength = checked(info.Width * 4); var result = new byte[checked(rowLength * info.Height)];
        for (var y = 0; y < info.Height; y++)
            Marshal.Copy(IntPtr.Add(decoded.GetPixels(), checked(y * decoded.RowBytes)), result, y * rowLength, rowLength);
        return result;
    }

    private static byte[] Rgba(Bitmap source)
    {
        using var converted = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = converted.Lock(); source.CopyPixels(buffer); var bytes = new byte[checked(source.PixelSize.Width * source.PixelSize.Height * 4)];
        for (var y = 0; y < source.PixelSize.Height; y++) Marshal.Copy(IntPtr.Add(buffer.Address, y * buffer.RowBytes), bytes, y * source.PixelSize.Width * 4, source.PixelSize.Width * 4);
        return bytes;
    }
    // Independent two-dimensional convolution over original bytes. It does not
    // call the shared kernel/accumulator or emulate the product row ring.
    private static byte[] Golden(byte[] source, int width, int height, RasterBlur settings)
    {
        var radius = settings.Radius; var weights = Enumerable.Range(-radius, radius * 2 + 1).Select(offset => Math.Exp(-2d * offset * offset / (radius * radius))).ToArray();
        var sum = weights.Sum(); var quantized = weights.Select(weight => (int)Math.Round(weight / sum * 65536, MidpointRounding.AwayFromZero)).ToArray();
        quantized[radius] += 65536 - quantized.Sum(); var result = source.ToArray();
        for (var y = settings.Y; y < settings.Y + settings.Height; y++) for (var x = settings.X; x < settings.X + settings.Width; x++)
        {
            long alpha = 0, red = 0, green = 0, blue = 0;
            for (var ky = -radius; ky <= radius; ky++) for (var kx = -radius; kx <= radius; kx++)
            {
                var at = (Math.Clamp(y + ky, 0, height - 1) * width + Math.Clamp(x + kx, 0, width - 1)) * 4;
                var weight = (long)quantized[ky + radius] * quantized[kx + radius]; var a = source[at + 3];
                alpha += a * weight; red += source[at] * a * weight; green += source[at + 1] * a * weight; blue += source[at + 2] * a * weight;
            }
            var destination = (y * width + x) * 4; var roundedAlpha = (byte)Math.Round(alpha / 4294967296d, MidpointRounding.AwayFromZero);
            result[destination + 3] = roundedAlpha;
            result[destination] = roundedAlpha == 0 ? (byte)0 : (byte)Math.Round(red / (double)alpha, MidpointRounding.AwayFromZero);
            result[destination + 1] = roundedAlpha == 0 ? (byte)0 : (byte)Math.Round(green / (double)alpha, MidpointRounding.AwayFromZero);
            result[destination + 2] = roundedAlpha == 0 ? (byte)0 : (byte)Math.Round(blue / (double)alpha, MidpointRounding.AwayFromZero);
        }
        return result;
    }
}
