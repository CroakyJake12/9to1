using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureCanvasResizeTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedOwners = [];

    [AvaloniaFact]
    public async Task Canvas_growth_adds_transparent_pixels_without_resampling_or_changing_source()
    {
        var fixture = await SourceAsync(); var session = new PictureEditorSession(new PictureCropService().OpenSource(fixture.Source));
        var id = session.Document.DocumentId; var revision = session.Document.SourceRevision;
        using (var grown = session.Apply("Resize canvas", document => document.ResizeCanvas(5, 3, 1, 1)))
        {
            Assert.Equal(new PixelSize(5, 3), grown.PixelSize);
            Assert.Equal(0, Pixel(grown, 0, 0).A); Assert.Equal(0, Pixel(grown, 4, 2).A);
            AssertRed(grown, 1, 1); AssertGreen(grown, 2, 1); Assert.Equal(0, Pixel(grown, 3, 1).A);
        }
        Assert.Equal(id, session.Document.DocumentId); Assert.Equal(revision, session.Document.SourceRevision);
        Assert.Equal(new CanvasResizeOperation(5, 3, 1, 1), Assert.Single(session.Document.Operations));
        var exportedPath = Path.Combine(fixture.Directory, "grown.png");
        new PictureCropService().ExportPng(session.Document, exportedPath, PictureMetadataExportMode.RemoveAll);
        using (var exported = new Bitmap(exportedPath))
        { Assert.Equal(new PixelSize(5, 3), exported.PixelSize); AssertGreen(exported, 2, 1); Assert.Equal(0, Pixel(exported, 4, 2).A); }
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
        var before = session.Document.Serialize(); var action = session.LastOperation;
        using (session.Apply("Unchanged canvas", document => document.ResizeCanvas(5, 3))) { }
        Assert.Equal(before, session.Document.Serialize()); Assert.Same(action, session.LastOperation);
    }

    [AvaloniaFact]
    public async Task Negative_canvas_placement_clips_real_pixels_and_saved_shared_redo_recovers_the_exact_edit()
    {
        var fixture = await SourceAsync(); var session = new PictureEditorSession(new PictureCropService().OpenSource(fixture.Source));
        using (var clipped = session.Apply("Trim canvas", document => document.ResizeCanvas(1, 1, -1, 0))) AssertGreen(clipped, 0, 0);
        using (var undone = session.Undo()) { Assert.Equal(new PixelSize(2, 1), undone.PixelSize); AssertRed(undone, 0, 0); AssertGreen(undone, 1, 0); }
        var path = Path.Combine(fixture.Directory, "canvas.picture.json");
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(session.Document.DocumentId, reopened.Document.DocumentId); Assert.True(reopened.CanRedo);
        using (var redone = reopened.Redo()) { Assert.Equal(new PixelSize(1, 1), redone.PixelSize); AssertGreen(redone, 0, 0); }
        Assert.Equal(new CanvasResizeOperation(1, 1, -1, 0), Assert.Single(reopened.Document.Operations));
        using (var original = reopened.Undo()) AssertRed(original, 0, 0);
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task Canvas_stack_modification_and_reordering_render_the_actual_order_and_keep_clipped_source_recoverable()
    {
        var fixture = await SourceAsync(); var session = new PictureEditorSession(new PictureCropService().OpenSource(fixture.Source));
        using (session.Apply("Canvas", document => document.ResizeCanvas(4, 1))) { }
        using (var flipped = session.Apply("Flip", document => document.Flip(true)))
        { AssertRed(flipped, 3, 0); AssertGreen(flipped, 2, 0); Assert.Equal(0, Pixel(flipped, 0, 0).A); }
        using (var reordered = session.MoveOperation(session.CaptureOperation(1), 0))
        { AssertGreen(reordered, 0, 0); AssertRed(reordered, 1, 0); Assert.Equal(0, Pixel(reordered, 3, 0).A); }
        using (var changed = session.ReplaceOperation(session.CaptureOperation(1), new CanvasResizeOperation(4, 1, 1, 0)))
        { AssertGreen(changed, 1, 0); AssertRed(changed, 2, 0); Assert.Equal(0, Pixel(changed, 0, 0).A); }
        using (var removed = session.RemoveOperation(session.CaptureOperation(1)))
        { Assert.Equal(new PixelSize(2, 1), removed.PixelSize); AssertGreen(removed, 0, 0); AssertRed(removed, 1, 0); }
        Assert.IsType<FlipOperation>(Assert.Single(session.Document.Operations));
        using (var undone = session.Undo()) AssertRed(undone, 2, 0);
    }

    [AvaloniaFact]
    public void Invalid_canvas_bounds_placement_and_legacy_operation_schema_preserve_the_current_document()
    {
        var session = new PictureEditorSession(PictureDocument.Create(2, 1)); var before = session.Document.Serialize();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Apply("Invalid", document => document.ResizeCanvas(0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Apply("Invalid", document => document.ResizeCanvas(32768, 32768)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Apply("Invalid", document => document.ResizeCanvas(2, 1, -32769, 0)));
        Assert.Equal(before, session.Document.Serialize()); Assert.False(session.CanUndo);
        var withCanvas = PictureDocument.Create(2, 1).ResizeCanvas(4, 1, 1, 0);
        var legacy = Encoding.UTF8.GetString(withCanvas.Serialize()).Replace($"\"schemaVersion\":{PictureDocument.CurrentSchemaVersion}", "\"schemaVersion\":3", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => PictureDocument.Deserialize(Encoding.UTF8.GetBytes(legacy)));
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Earlier_picture_schemas_keep_identity_source_dimensions_and_upgrade_only_through_actual_canvas_edit(int schema)
    {
        var original = PictureDocument.Create(2, 1); var bytes = Encoding.UTF8.GetString(original.Serialize())
            .Replace($"\"schemaVersion\":{PictureDocument.CurrentSchemaVersion}", "\"schemaVersion\":" + schema, StringComparison.Ordinal);
        var prior = PictureDocument.Deserialize(Encoding.UTF8.GetBytes(bytes));
        Assert.Equal(schema, prior.SchemaVersion); Assert.Equal(original.DocumentId, prior.DocumentId);
        var edited = prior.ResizeCanvas(5, 3, 1, 1);
        Assert.Equal(4, edited.SchemaVersion); Assert.Equal(original.DocumentId, edited.DocumentId);
        Assert.Equal(2, edited.InitialCanvasWidth); Assert.Equal(1, edited.InitialCanvasHeight);
        Assert.Equal(new CanvasResizeOperation(5, 3, 1, 1), Assert.Single(PictureDocument.Deserialize(edited.Serialize()).Operations));
    }

    [AvaloniaFact]
    public async Task Actual_CUI_canvas_draft_survives_unrelated_actions_and_edits_same_pixels_history_and_saved_source()
    {
        await WithWindowAsync(async (window, session, fixture) =>
        {
            SetFields(window, 5, 3, 1, 1);
            await Click(window, "ZoomInButton"); await Click(window, "MetadataRemoveAllButton");
            AssertFields(window, 5, 3, 1, 1);
            await Click(window, "ResizeCanvasButton"); AssertRed(Preview(window), 1, 1); AssertGreen(Preview(window), 2, 1);
            AssertFields(window, 5, 3, 0, 0);
            await Click(window, "UndoButton"); Assert.Equal(new PixelSize(2, 1), Preview(window).PixelSize);
            await Click(window, "RedoButton"); AssertGreen(Preview(window), 2, 1);
            await PictureCuiActionFixture.ClickAsync(window, Row(window, "1. Canvas 5 × 3 · place at 1, 1"));
            AssertFields(window, 5, 3, 1, 1);
            Find<NumericUpDown>(window, "CanvasOffsetXBox").Value = 2;
            await Click(window, "ReplaceSelectedEditButton"); AssertRed(Preview(window), 2, 1); AssertGreen(Preview(window), 3, 1);
            Assert.Equal(new CanvasResizeOperation(5, 3, 2, 1), Assert.Single(session.Document.Operations));
            var id = session.Document.DocumentId; var path = Path.Combine(fixture.Directory, "native-canvas.picture.json");
            await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            Assert.Equal(id, reopened.Document.DocumentId); Assert.Equal(session.Document.Operations, reopened.Document.Operations);
            using var rendered = PictureCropService.Render(reopened.Document); AssertGreen(rendered, 3, 1);
        });
    }

    [AvaloniaFact]
    public async Task Actual_CUI_centering_uses_input_of_selected_canvas_edit_and_requires_explicit_apply()
    {
        await WithWindowAsync(async (window, session, fixture) =>
        {
            SetFields(window, 6, 5, 0, 0); var before = session.Document.Serialize();
            await Click(window, "CenterCanvasContentButton"); AssertFields(window, 6, 5, 2, 2);
            Assert.Equal(before, session.Document.Serialize());
            await Click(window, "ResizeCanvasButton"); AssertRed(Preview(window), 2, 2);
            await PictureCuiActionFixture.ClickAsync(window, Row(window, "1. Canvas 6 × 5 · place at 2, 2"));
            SetFields(window, 4, 3, 0, 0);
            await Click(window, "CenterCanvasContentButton"); AssertFields(window, 4, 3, 1, 1);
            await Click(window, "ReplaceSelectedEditButton"); AssertRed(Preview(window), 1, 1); AssertGreen(Preview(window), 2, 1);
            Assert.Equal(new CanvasResizeOperation(4, 3, 1, 1), Assert.Single(session.Document.Operations));
            var unchanged = session.Document.Serialize();
            Find<NumericUpDown>(window, "CanvasWidthBox").Value = 4.5m;
            await Click(window, "ResizeCanvasButton");
            Assert.Equal(unchanged, session.Document.Serialize());
            Assert.Equal(4.5m, Find<NumericUpDown>(window, "CanvasWidthBox").Value);
            Assert.Contains("whole pixel", Find<TextBlock>(window, "StatusText").Text);
        });
    }

    [AvaloniaFact]
    public void Canvas_geometry_transforms_shared_hybrid_output_without_flattening_vector_identity()
    {
        var shape = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle);
        var vector = new HomeVectorShapeObjectHandler().Project(shape);
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (session.AddSharedVector(vector)) { }
        var sameGraph = session.Document.CompositionState!;
        using (var grown = session.Apply("Canvas", document => document.ResizeCanvas(120, 120, 10, 10)))
        { var color = Pixel(grown, 60, 60); Assert.True(color.B > color.R && color.A > 220); Assert.Equal(0, Pixel(grown, 0, 0).A); }
        Assert.Equal(sameGraph, session.Document.CompositionState);
        Assert.Contains(PictureCompositionAdapter.Read(session.Document).Pages[0].Objects, item => item.ObjectId == vector.ObjectId);
        using (session.Undo()) { }
        Assert.Equal(sameGraph, session.Document.CompositionState); Assert.Empty(session.Document.Operations);
    }

    [AvaloniaFact]
    public void Non_96_DPI_canvas_preserves_every_actual_source_RGBA_pixel_offset_padding_and_output_density()
    {
        using var source = new RenderTargetBitmap(new PixelSize(4, 2), new Vector(192, 192));
        using (var original = source.CreateDrawingContext())
        {
            original.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 1, .5));
            original.DrawRectangle(Brushes.Green, null, new Rect(1, 0, 1, .5));
            original.DrawRectangle(Brushes.Blue, null, new Rect(0, .5, 1, .5));
            original.DrawRectangle(new SolidColorBrush(Color.FromArgb(127, 240, 160, 20)), null, new Rect(1, .5, 1, .5));
        }
        var before = ExactRgba(source);
        using var grown = PictureCropService.Render(source, PictureDocument.Create(4, 2).ResizeCanvas(7, 5, 2, 1));
        Assert.Equal(new PixelSize(7, 5), grown.PixelSize); Assert.Equal(source.Dpi, grown.Dpi);
        var after = ExactRgba(grown);
        for (var y = 0; y < 5; y++)
            for (var x = 0; x < 7; x++)
            {
                var pixel = after.AsSpan((y * 7 + x) * 4, 4).ToArray();
                if (x is >= 2 and < 6 && y is >= 1 and < 3)
                    Assert.Equal(before.AsSpan(((y - 1) * 4 + x - 2) * 4, 4).ToArray(), pixel);
                else Assert.Equal(new byte[4], pixel);
            }
        using (var encoded = new MemoryStream())
        {
            grown.Save(encoded); encoded.Position = 0;
            using var reopenedExport = new Bitmap(encoded);
            Assert.Equal(grown.PixelSize, reopenedExport.PixelSize); Assert.Equal(after, ExactRgba(reopenedExport));
        }
        using var trimmed = PictureCropService.Render(source, PictureDocument.Create(4, 2).ResizeCanvas(2, 1, -2, -1));
        Assert.Equal(source.Dpi, trimmed.Dpi);
        Assert.Equal(before.AsSpan((1 * 4 + 2) * 4, 8).ToArray(), ExactRgba(trimmed));
        Assert.Equal(before, ExactRgba(source));
    }
    private static byte[] ExactRgba(Bitmap original)
    {
        using var converted = new WriteableBitmap(original.PixelSize, original.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var frame = converted.Lock(); original.CopyPixels(frame);
        var result = new byte[checked(original.PixelSize.Width * original.PixelSize.Height * 4)];
        for (var y = 0; y < original.PixelSize.Height; y++)
            Marshal.Copy(IntPtr.Add(frame.Address, checked(y * frame.RowBytes)), result, y * original.PixelSize.Width * 4, original.PixelSize.Width * 4);
        return result;
    }

    private static async Task WithWindowAsync(Func<MainWindow, PictureEditorSession, SourceFixture, Task> body)
    {
        var fixture = await SourceAsync(); var window = new MainWindow(new PictureFixtureReadiness()); Exception? failure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Canvas resize initialization");
            window.Show(); window.UpdateLayout();
            typeof(MainWindow).GetMethod("LoadLocalPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [fixture.Source]);
            var session = Assert.IsType<PictureEditorSession>(typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            await body(window, session, fixture);
            await session.SaveAsync(Path.Combine(fixture.Directory, "final.picture.json"), cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
        }
        catch (Exception original) { failure = original; RetainedFailedOwners.Add((window, original)); throw; }
        finally
        {
            if (failure is null)
            {
                window.Close(); Assert.NotNull(window.OriginalClose); var sameClose = window.OriginalClose!;
                try { await sameClose.ObserveOriginalAsync(window, "Canvas resize original close"); }
                catch (Exception original) { RetainedFailedOwners.Add((window, original)); throw; }
                Assert.True(sameClose.IsCompletedSuccessfully); Assert.Same(sameClose, window.OriginalClose);
            }
        }
    }
    private static Task Click(MainWindow window, string name) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, name));
    private static T Find<T>(MainWindow window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static Button Row(MainWindow window, string label) => window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label));
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private static void SetFields(MainWindow window, int width, int height, int x, int y)
    {
        Find<NumericUpDown>(window, "CanvasWidthBox").Value = width; Find<NumericUpDown>(window, "CanvasHeightBox").Value = height;
        Find<NumericUpDown>(window, "CanvasOffsetXBox").Value = x; Find<NumericUpDown>(window, "CanvasOffsetYBox").Value = y;
    }
    private static void AssertFields(MainWindow window, int width, int height, int x, int y)
    {
        Assert.Equal((decimal)width, Find<NumericUpDown>(window, "CanvasWidthBox").Value); Assert.Equal((decimal)height, Find<NumericUpDown>(window, "CanvasHeightBox").Value);
        Assert.Equal((decimal)x, Find<NumericUpDown>(window, "CanvasOffsetXBox").Value); Assert.Equal((decimal)y, Find<NumericUpDown>(window, "CanvasOffsetYBox").Value);
    }
    private sealed record SourceFixture(string Directory, string Source, byte[] Bytes);
    private static async Task<SourceFixture> SourceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "picture-canvas-resize-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.bmp"); var bytes = new byte[62]; bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2); BitConverter.GetBytes(54).CopyTo(bytes, 10); BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(2).CopyTo(bytes, 18); BitConverter.GetBytes(1).CopyTo(bytes, 22); BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28); BitConverter.GetBytes(8).CopyTo(bytes, 34); bytes[56] = 255; bytes[58] = 255;
        await File.WriteAllBytesAsync(source, bytes, TestContext.Current.CancellationToken); return new(directory, source, bytes);
    }
    private static void AssertRed(Bitmap bitmap, int x, int y) { var p = Pixel(bitmap, x, y); Assert.True(p.R > 240 && p.G < 15 && p.B < 15 && p.A > 240, $"Expected unchanged red source pixel, observed {p}."); }
    private static void AssertGreen(Bitmap bitmap, int x, int y) { var p = Pixel(bitmap, x, y); Assert.True(p.G > 240 && p.R < 15 && p.B < 15 && p.A > 240, $"Expected unchanged green source pixel, observed {p}."); }
    private static (byte R, byte G, byte B, byte A) Pixel(Bitmap bitmap, int x, int y)
    {
        var bytes = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)]; var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), pinned.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4);
            var offset = checked((y * bitmap.PixelSize.Width + x) * 4); return (bytes[offset + 2], bytes[offset + 1], bytes[offset], bytes[offset + 3]);
        }
        finally { pinned.Free(); }
    }
}
