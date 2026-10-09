using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
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

public sealed class PicturePixelationTests
{
    private static readonly List<(object Owner, Task? Original, Exception Failure)> RetainedFailures = [];

    [AvaloniaTheory]
    [InlineData(96)]
    [InlineData(192)]
    public void Entered_rectangle_and_partial_edge_blocks_compare_every_RGBA_pixel_preserve_density_and_export(int dpi)
    {
        using var source = SourcePixels(dpi); var original = Rgba(source);
        var settings = new RasterPixelation(1, 1, 5, 3, 2);
        var document = PictureDocument.Create(7, 5).Pixelate(settings);
        var expected = Golden(original, 7, settings);
        using var actual = PictureCropService.Render(source, document);
        Assert.Equal(source.PixelSize, actual.PixelSize); Assert.Equal(source.Dpi, actual.Dpi);
        Assert.Equal(expected, Rgba(actual)); Assert.Equal(original, Rgba(source));
        var reopened = PictureDocument.Deserialize(document.Serialize());
        Assert.Equal(new PixelationOperation(settings), Assert.Single(reopened.Operations));
        using var replayed = PictureCropService.Render(source, reopened); Assert.Equal(expected, Rgba(replayed));
        var directory = Directory.CreateTempSubdirectory("picture-pixelation-density-");
        var png = Path.Combine(directory.FullName, "pixelated.png");
        using (var output = File.Create(png)) actual.Save(output);
        Assert.Equal(expected, ReadOriginalPngRgba(png));
    }

    [Fact]
    public void Alpha_weighting_transparent_blocks_sample_limit_geometry_and_saved_required_values_are_exact()
    {
        var accumulator = new RasterPixelationAccumulator();
        accumulator.AddRgba8([255, 0, 0, 255, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0]);
        byte[] mean = new byte[4]; accumulator.WriteMeanRgba8(mean);
        Assert.Equal(new byte[] { 255, 0, 0, 128 }, mean);
        accumulator = new(); accumulator.AddRgba8([255, 0, 255, 0, 0, 255, 0, 0]); accumulator.WriteMeanRgba8(mean);
        Assert.Equal(new byte[4], mean);
        // A tiny alpha sum which rounds to zero cannot preserve hidden colour.
        accumulator = new(); accumulator.AddRgba8([255, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0]); accumulator.WriteMeanRgba8(mean);
        Assert.Equal(new byte[4], mean);
        accumulator = new(); var row = new byte[512 * 4];
        for (var y = 0; y < 512; y++) accumulator.AddRgba8(row);
        Assert.Throws<ArgumentOutOfRangeException>(() => accumulator.AddRgba8(new byte[4]));
        var empty = new RasterPixelationAccumulator(); Assert.Throws<ArgumentException>(() => empty.WriteMeanRgba8(mean));
        var document = PictureDocument.Create(7, 5); var original = document.Serialize();
        RasterPixelation[] invalid = [new(-1, 0, 2, 2, 2), new(0, -1, 2, 2, 2), new(0, 0, 0, 2, 2),
            new(6, 0, 2, 2, 2), new(0, 4, 2, 2, 2), new(int.MaxValue, 0, int.MaxValue, 2, 2), new(0, 0, 2, 2, 1), new(0, 0, 2, 2, 513)];
        foreach (var item in invalid) Assert.Throws<ArgumentOutOfRangeException>(() => document.Pixelate(item));
        new RasterPixelation(0, 0, 1, 1, 512).Validate(1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterPixelation(0, 0, 32768, 32768, 2).ValidateGeometry());
        Assert.Equal(original, document.Serialize());
        var valid = document.Pixelate(new(1, 1, 5, 3, 2));
        var schema5 = Encoding.UTF8.GetString(valid.Serialize()).Replace($"\"schemaVersion\":{PictureDocument.CurrentSchemaVersion}", "\"schemaVersion\":5", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => PictureDocument.Deserialize(Encoding.UTF8.GetBytes(schema5)));
        var noSettings = JsonNode.Parse(valid.Serialize())!.AsObject(); noSettings["operations"]![0]!.AsObject().Remove("settings");
        Assert.Throws<JsonException>(() => PictureDocument.Deserialize(JsonSerializer.SerializeToUtf8Bytes(noSettings)));
    }

    [AvaloniaFact]
    public async Task Actual_stack_replace_invalid_reorder_remove_undo_and_cold_saved_redo_keep_original_bytes_and_pixels()
    {
        var fixture = await SourceAsync(); var session = new PictureEditorSession(new PictureCropService().OpenSource(fixture.Source));
        var id = session.Document.DocumentId; var sourceRevision = session.Document.SourceRevision;
        using (session.Apply("Grow canvas", document => document.ResizeCanvas(9, 5, 0, 0))) { }
        var settings = new RasterPixelation(7, 1, 2, 2, 2);
        using (session.Apply("Pixelate transparent padding", document => document.Pixelate(settings))) { }
        var originalGraph = session.Document.Serialize(); var operation = session.LastOperation;
        Assert.Throws<ArgumentOutOfRangeException>(() => session.MoveOperation(session.CaptureOperation(1), 0));
        Assert.Equal(originalGraph, session.Document.Serialize()); Assert.Same(operation, session.LastOperation);
        var changed = new RasterPixelation(1, 1, 5, 3, 3); var beforePixels = new byte[9 * 5 * 4];
        for (var y = 0; y < 5; y++) fixture.Pixels.AsSpan(y * 7 * 4, 7 * 4).CopyTo(beforePixels.AsSpan(y * 9 * 4, 7 * 4));
        var expected = Golden(beforePixels, 9, changed);
        using (var actual = session.ReplaceOperation(session.CaptureOperation(1), new PixelationOperation(changed))) Assert.Equal(expected, Rgba(actual));
        using (var removed = session.RemoveOperation(session.CaptureOperation(1))) Assert.Equal(beforePixels, Rgba(removed));
        using (var recovered = session.Undo()) Assert.Equal(expected, Rgba(recovered));
        using (var undone = session.Undo()) Assert.Equal(beforePixels, Rgba(undone));
        var saved = Path.Combine(fixture.Directory, "stack.picture.json"); await session.SaveAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(saved, TestContext.Current.CancellationToken), saved);
        Assert.True(cold.CanRedo); Assert.Equal(id, cold.Document.DocumentId); Assert.Equal(sourceRevision, cold.Document.SourceRevision);
        using (var redo = cold.Redo()) Assert.Equal(expected, Rgba(redo));
        Assert.Equal(new PixelationOperation(changed), cold.Document.Operations[^1]);
        var exportedPath = Path.Combine(fixture.Directory, "stack.png"); new PictureCropService().ExportPng(cold.Document, exportedPath, PictureMetadataExportMode.RemoveAll);
        Assert.Equal(expected, ReadOriginalPngRgba(exportedPath));
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task Compatible_schema_one_through_five_and_original_schema_five_history_hashes_survive_forward_pixelation_six()
    {
        var fixture = await SourceAsync(); var initial = new PictureCropService().OpenSource(fixture.Source);
        for (var schema = 1; schema <= 5; schema++)
        {
            var json = Encoding.UTF8.GetString(initial.Serialize()).Replace($"\"schemaVersion\":{PictureDocument.CurrentSchemaVersion}", "\"schemaVersion\":" + schema, StringComparison.Ordinal);
            var bytes = Encoding.UTF8.GetBytes(json); var compatible = PictureDocument.Deserialize(bytes);
            Assert.Equal(bytes, compatible.Serialize()); Assert.Equal(schema, compatible.SchemaVersion);
            var pure = compatible.Flip(true); Assert.Equal(schema, pure.SchemaVersion);
            Assert.Equal(6, pure.Pixelate(new(0, 0, 2, 2, 2)).SchemaVersion);
        }
        var legacyBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(initial.Serialize()).Replace($"\"schemaVersion\":{PictureDocument.CurrentSchemaVersion}", "\"schemaVersion\":5", StringComparison.Ordinal));
        var session = new PictureEditorSession(PictureDocument.Deserialize(legacyBytes));
        using (session.Apply("Original schema5 horizontal flip", document => document.Flip(true))) { }
        var path = Path.Combine(fixture.Directory, "legacy-five.picture.json"); await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var legacy = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(5, legacy.SchemaVersion); var originalFrame = Assert.Single(legacy.SemanticHistory!.Undo);
        var restored = new PictureEditorSession(legacy, path); byte[] priorPixels;
        using (var prior = PictureCropService.Render(restored.Document)) priorPixels = Rgba(prior);
        var settings = new RasterPixelation(1, 1, 5, 3, 2); var expected = Golden(priorPixels, 7, settings);
        using (var rendered = restored.Apply("New editable pixelation", document => document.Pixelate(settings))) Assert.Equal(expected, Rgba(rendered));
        using (var undone = restored.Undo()) Assert.Equal(priorPixels, Rgba(undone));
        Assert.Equal(6, restored.Document.SchemaVersion); Assert.True(restored.CanRedo);
        var forward = Path.Combine(fixture.Directory, "forward-six.picture.json"); await restored.SaveAsync(forward, cancellationToken: TestContext.Current.CancellationToken);
        var saved = await PictureDocument.OpenAsync(forward, TestContext.Current.CancellationToken);
        Assert.Equal(originalFrame, Assert.Single(saved.SemanticHistory!.Undo));
        var cold = new PictureEditorSession(saved, forward); Assert.Equal(6, cold.Document.SchemaVersion);
        using (var undone = cold.Undo()) Assert.Equal(fixture.Pixels, Rgba(undone));
        using (var flip = cold.Redo()) Assert.Equal(priorPixels, Rgba(flip));
        using (var redo = cold.Redo()) Assert.Equal(expected, Rgba(redo));
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Pixelation_uses_the_same_hybrid_graph_preserves_canonical_vector_identity_and_unknown_payload()
    {
        var session = new PictureEditorSession(PictureDocument.Create(7, 5));
        var shape = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle);
        var vector = new HomeVectorShapeObjectHandler().Project(shape);
        var payload = JsonNode.Parse(vector.Content.GetRawText())!.AsObject(); payload["FuturePixelationFixture"] = new JsonObject { ["retained"] = 67 };
        vector = vector with { Content = JsonSerializer.SerializeToElement(payload) };
        using (session.AddSharedVector(vector)) { }
        var sameGraph = session.Document.CompositionState!; byte[] priorPixels;
        using (var prior = PictureCropService.Render(session.Document)) priorPixels = Rgba(prior);
        var settings = new RasterPixelation(1, 1, 5, 3, 2);
        using (var result = session.Apply("Pixelate composed region", document => document.Pixelate(settings))) Assert.Equal(Golden(priorPixels, 7, settings), Rgba(result));
        Assert.Equal(sameGraph, session.Document.CompositionState);
        var graph = PictureCompositionAdapter.Read(session.Document);
        var retainedVector = PictureCompositionAdapter.ReadVector(graph.Pages[0].Objects.Single(item => item.ObjectId == vector.ObjectId));
        Assert.Equal(vector.ObjectId, retainedVector.ObjectId); Assert.Equal(vector.Content.GetRawText(), retainedVector.Content.GetRawText());
        using (var recovered = session.Undo()) Assert.Equal(priorPixels, Rgba(recovered));
        Assert.Equal(sameGraph, session.Document.CompositionState);
    }

    [AvaloniaFact]
    public async Task Actual_CUI_pixelation_preserves_unrelated_drafts_acknowledges_its_bounds_and_replaces_the_current_edit()
    {
        var fixture = await SourceAsync(); var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Pixelation actual initialization");
            window.Show(); window.UpdateLayout(); LoadSource(window, fixture.Source);
            var session = Assert.IsType<PictureEditorSession>(typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            var bounds = Find<TextBox>(window, "CropBoundsBox"); var block = Find<NumericUpDown>(window, "PixelationBlockSizeBox");
            var width = Find<NumericUpDown>(window, "WidthBox"); var ratio = Find<ComboBox>(window, "CropRatioBox");
            bounds.Text = "1, 1, 5, 3"; block.Value = 2; width.Value = 6; ratio.SelectedIndex = 7;
            Find<NumericUpDown>(window, "CropRatioWidthBox").Value = 5; Find<NumericUpDown>(window, "CropRatioHeightBox").Value = 3;
            await Click(window, "ZoomInButton"); await Click(window, "MetadataRemoveAllButton");
            Assert.Equal("1, 1, 5, 3", bounds.Text); Assert.Equal(2m, block.Value); Assert.Equal(6m, width.Value); Assert.Equal(7, ratio.SelectedIndex);
            var before = session.Document.Serialize(); bounds.Text = "6, 1, 2, 3";
            await Click(window, "ApplyPixelationButton"); Assert.Equal(before, session.Document.Serialize());
            Assert.Equal("6, 1, 2, 3", bounds.Text); Assert.Contains("inside", Find<TextBlock>(window, "StatusText").Text);
            bounds.Text = "1, 1, 5, 3"; await Click(window, "ApplyPixelationButton");
            var applied = new RasterPixelation(1, 1, 5, 3, 2);
            Assert.Equal(new PixelationOperation(applied), Assert.Single(session.Document.Operations)); Assert.Equal(Golden(fixture.Pixels, 7, applied), Rgba(Preview(window)));
            Assert.Equal("0, 0, 7, 5", bounds.Text); Assert.Equal(8m, block.Value); Assert.Equal(6m, width.Value); Assert.Equal(7, ratio.SelectedIndex);
            Assert.Equal(5m, Find<NumericUpDown>(window, "CropRatioWidthBox").Value);
            var row = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "1. Pixelate 1, 1 · 5 × 3 · 2-pixel blocks"));
            await PictureCuiActionFixture.ClickAsync(window, row); Assert.Equal("1, 1, 5, 3", bounds.Text); Assert.Equal(2m, block.Value);
            block.Value = 3; await Click(window, "ReplaceSelectedEditButton");
            var replaced = applied with { BlockSize = 3 }; Assert.Equal(new PixelationOperation(replaced), Assert.Single(session.Document.Operations));
            Assert.Equal(Golden(fixture.Pixels, 7, replaced), Rgba(Preview(window))); Assert.Equal(6m, width.Value); Assert.Equal(7, ratio.SelectedIndex);
            await Click(window, "UndoButton"); Assert.Equal(Golden(fixture.Pixels, 7, applied), Rgba(Preview(window)));
            await Click(window, "RedoButton"); Assert.Equal(Golden(fixture.Pixels, 7, replaced), Rgba(Preview(window)));
            // Editing an earlier pixelation uses its real preceding canvas,
            // even after a later Crop makes the displayed canvas smaller.
            ratio.SelectedIndex = 0; bounds.Text = "0, 0, 3, 2"; await Click(window, "ApplyCropButton");
            row = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "1. Pixelate 1, 1 · 5 × 3 · 3-pixel blocks"));
            await PictureCuiActionFixture.ClickAsync(window, row);
            Assert.Equal("1, 1, 5, 3", bounds.Text); block.Value = 2; await Click(window, "ReplaceSelectedEditButton");
            Assert.Equal(new PixelationOperation(applied), session.Document.Operations[0]);
            var full = Golden(fixture.Pixels, 7, applied); var cropped = new byte[3 * 2 * 4];
            for (var y = 0; y < 2; y++) full.AsSpan(y * 7 * 4, 3 * 4).CopyTo(cropped.AsSpan(y * 3 * 4, 3 * 4));
            Assert.Equal(cropped, Rgba(Preview(window)));
            var path = Path.Combine(fixture.Directory, "native-pixelation.picture.json"); await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            using (var actual = PictureCropService.Render(cold.Document)) Assert.Equal(Rgba(Preview(window)), Rgba(actual));
            LoadSource(window, fixture.Source); Assert.Equal(8m, block.Value); Assert.Equal("0, 0, 7, 5", bounds.Text);
            Assert.Equal(0, ratio.SelectedIndex); Assert.Equal(7m, width.Value);
            Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
        }
        catch (Exception failure) { firstFailure = failure; RetainedFailures.Add((window, window.OriginalCommand, failure)); throw; }
        finally
        {
            if (firstFailure is null)
            {
                window.Close(); Assert.NotNull(window.OriginalClose); var original = window.OriginalClose!;
                try { await original.ObserveOriginalAsync(window, "Pixelation original close"); Assert.True(original.IsCompletedSuccessfully); Assert.Same(original, window.OriginalClose); }
                catch (Exception failure) { RetainedFailures.Add((window, original, failure)); throw; }
            }
            else
            {
                // Drain SAME accepted command/initialization/scene originals
                // independently; failed dirty UI never authorizes discard or
                // an unanswered Save popup. The actual native owner stays held.
                var children = new List<Task>();
                if (window.OriginalCommand is { } command) children.Add(command);
                if (window.OriginalInitialization is { } initialization) children.Add(initialization);
                try { var sceneClose = window.SceneHost.CloseOriginalAsync(); children.Add(sceneClose); }
                catch (Exception failure) { RetainedFailures.Add((window.SceneHost, null, failure)); }
                foreach (var sameChild in children.Distinct<Task>(ReferenceEqualityComparer.Instance))
                {
                    RetainedFailures.Add((window, sameChild, firstFailure));
                    try { await sameChild.ObserveOriginalAsync(window, "Failed pixelation original child"); }
                    catch (Exception failure) { RetainedFailures.Add((window, sameChild, sameChild.Exception ?? failure)); }
                }
            }
        }
    }

    private static void LoadSource(MainWindow window, string path) => typeof(MainWindow).GetMethod("LoadLocalPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [path]);
    private static Task Click(MainWindow window, string id) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, id));
    private static T Find<T>(MainWindow window, string id) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == id);
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private sealed record SourceFixture(string Directory, string Source, byte[] Bytes, byte[] Pixels);
    private static async Task<SourceFixture> SourceAsync()
    {
        var directory = Directory.CreateTempSubdirectory("picture-pixelation-").FullName; var sourcePath = Path.Combine(directory, "source.png");
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
            var row = Enumerable.Range(0, 7).SelectMany(x => (x + y) % 4 == 0 ? new byte[] { 0, 0, 0, 0 } :
                new byte[] { (byte)(x * 35), (byte)(y * 51), (byte)(x * 19 + y * 13), (byte)((x + y) % 4 == 1 ? 128 : 255) }).ToArray();
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
        using var buffer = converted.Lock(); source.CopyPixels(buffer);
        var bytes = new byte[checked(source.PixelSize.Width * source.PixelSize.Height * 4)];
        for (var y = 0; y < source.PixelSize.Height; y++) Marshal.Copy(IntPtr.Add(buffer.Address, y * buffer.RowBytes), bytes, y * source.PixelSize.Width * 4, source.PixelSize.Width * 4);
        return bytes;
    }
    // Independent whole-raster expected result; it neither calls the shared
    // accumulator nor duplicates an editable engine/decoder in the product.
    private static byte[] Golden(byte[] source, int stridePixels, RasterPixelation region)
    {
        var result = source.ToArray();
        for (var top = region.Y; top < region.Y + region.Height; top += region.BlockSize)
            for (var left = region.X; left < region.X + region.Width; left += region.BlockSize)
            {
                var endY = Math.Min(top + region.BlockSize, region.Y + region.Height); var endX = Math.Min(left + region.BlockSize, region.X + region.Width);
                long alpha = 0, red = 0, green = 0, blue = 0; var count = (endY - top) * (endX - left);
                for (var y = top; y < endY; y++) for (var x = left; x < endX; x++)
                {
                    var at = (y * stridePixels + x) * 4; var a = source[at + 3]; alpha += a;
                    red += source[at] * a; green += source[at + 1] * a; blue += source[at + 2] * a;
                }
                var roundedAlpha = (byte)((alpha + count / 2) / count);
                byte[] mean = roundedAlpha == 0 ? [0, 0, 0, 0] : [(byte)((red + alpha / 2) / alpha), (byte)((green + alpha / 2) / alpha), (byte)((blue + alpha / 2) / alpha), roundedAlpha];
                for (var y = top; y < endY; y++) for (var x = left; x < endX; x++) mean.CopyTo(result, (y * stridePixels + x) * 4);
            }
        return result;
    }
}
