using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureStraightenTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedOwners = [];

    [AvaloniaTheory]
    [InlineData(96)]
    [InlineData(192)]
    public void Fit_quarter_angle_maps_all_real_RGBA_pixels_preserves_DPI_and_roundtrips_typed_document_and_PNG(int dpi)
    {
        byte[] pixels = [255,0,0,255, 0,255,0,128, 0,0,255,255, 255,255,255,255,
            255,255,0,255, 0,255,255,255, 255,0,255,64, 0,0,0,0];
        using var source = Pixels(4, 2, dpi, pixels); var original = Rgba(source);
        var document = PictureDocument.Create(4, 2).Straighten(90, expandCanvas: true);
        Assert.Equal(2, document.CanvasWidth); Assert.Equal(4, document.CanvasHeight);
        Assert.Equal(new StraightenOperation(90, true), Assert.Single(document.Operations));
        using var rotated = PictureCropService.Render(source, document);
        Assert.Equal(new PixelSize(2, 4), rotated.PixelSize); Assert.Equal(source.Dpi, rotated.Dpi);
        var actual = Rgba(rotated);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 2; x++)
        {
            var prior = ((1 - x) * 4 + y) * 4;
            var target = (y * 2 + x) * 4;
            // Hidden RGB under zero alpha is not visible colour; actual nonzero
            // partial alpha and every visible channel still compare exactly.
            if (original[prior + 3] != 0)
                Assert.Equal(original.AsSpan(prior, 4).ToArray(), actual.AsSpan(target, 4).ToArray());
            else Assert.Equal(0, actual[target + 3]);
        }
        var reopened = PictureDocument.Deserialize(document.Serialize());
        Assert.Equal(document.DocumentId, reopened.DocumentId); Assert.Equal(document.Operations, reopened.Operations);
        using var rerendered = PictureCropService.Render(source, reopened); Assert.Equal(actual, Rgba(rerendered));
        var directory = Directory.CreateTempSubdirectory("picture-angle-density-");
        var exportedPath = Path.Combine(directory.FullName, "angle.png");
        using (var output = File.Create(exportedPath)) rotated.Save(output);
        using var exported = new Bitmap(exportedPath); Assert.Equal(actual, Rgba(exported));
        Assert.Equal(original, Rgba(source));
    }

    [AvaloniaFact]
    public void Arbitrary_angle_at_both_densities_has_identical_alpha_geometry_and_explicit_fit_vs_keep_canvas()
    {
        var pixels = Enumerable.Range(0, 15).SelectMany(_ => new byte[] { 255, 0, 0, 255 }).ToArray();
        using var source96 = Pixels(5, 3, 96, pixels); using var source192 = Pixels(5, 3, 192, pixels);
        var fit = PictureDocument.Create(5, 3).Straighten(30, true);
        Assert.Equal(6, fit.CanvasWidth); Assert.Equal(6, fit.CanvasHeight);
        using var actual96 = PictureCropService.Render(source96, fit); using var actual192 = PictureCropService.Render(source192, fit);
        Assert.Equal(Rgba(actual96), Rgba(actual192)); Assert.Equal(new Vector(192, 192), actual192.Dpi);
        var expanded = Rgba(actual96); Assert.Equal(0, expanded[3]); Assert.Equal(0, expanded[^1]);
        Assert.Equal(255, expanded[(2 * 6 + 2) * 4 + 3]);
        var keep = PictureDocument.Create(5, 3).Straighten(30, false);
        Assert.Equal(5, keep.CanvasWidth); Assert.Equal(3, keep.CanvasHeight);
        using var clipped96 = PictureCropService.Render(source96, keep); using var clipped192 = PictureCropService.Render(source192, keep);
        Assert.Equal(new PixelSize(5, 3), clipped96.PixelSize); Assert.Equal(Rgba(clipped96), Rgba(clipped192));
        var clipped = Rgba(clipped96); Assert.InRange(clipped[3], (byte)1, (byte)254);
        Assert.Equal(255, clipped[(1 * 5 + 2) * 4 + 3]);
        Assert.Equal(pixels, Rgba(source96)); Assert.Equal(pixels, Rgba(source192));
    }

    [AvaloniaFact]
    public async Task Keep_canvas_clips_only_rendered_edges_and_shared_undo_and_saved_redo_recover_full_pixels()
    {
        var fixture = await SourceAsync(); var session = new PictureEditorSession(new PictureCropService().OpenSource(fixture.Source));
        var id = session.Document.DocumentId; var sourceRevision = session.Document.SourceRevision;
        using (var kept = session.Apply("Keep canvas angle", document => document.Straighten(90, false)))
        { Assert.Equal(new PixelSize(4, 2), kept.PixelSize); Assert.Equal(0, Rgba(kept)[3]); }
        using (var fitted = session.ReplaceOperation(session.CaptureOperation(0), new StraightenOperation(90, true)))
        { Assert.Equal(new PixelSize(2, 4), fitted.PixelSize); AssertQuarter(fixture.Pixels, Rgba(fitted)); }
        using (var undone = session.Undo()) Assert.Equal(new PixelSize(4, 2), undone.PixelSize);
        var path = Path.Combine(fixture.Directory, "angle.picture.json");
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.True(reopened.CanRedo); Assert.Equal(id, reopened.Document.DocumentId); Assert.Equal(sourceRevision, reopened.Document.SourceRevision);
        using (var redone = reopened.Redo()) { AssertQuarter(fixture.Pixels, Rgba(redone)); Assert.Equal(new PixelSize(2, 4), redone.PixelSize); }
        var export = Path.Combine(fixture.Directory, "rotated.png");
        new PictureCropService().ExportPng(reopened.Document, export, PictureMetadataExportMode.RemoveAll);
        using (var exported = new Bitmap(export)) AssertQuarter(fixture.Pixels, Rgba(exported));
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Nonfinite_range_expanded_canvas_and_legacy_discriminator_refusals_preserve_original_document()
    {
        var original = PictureDocument.Create(4, 2); var unchanged = original.Serialize();
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -180.1, 180.1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => original.Straighten(invalid));
        Assert.Equal(unchanged, original.Serialize()); Assert.Same(original, original.Straighten(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PictureDocument.Create(8000, 10000).Straighten(45, true));
        var bounded = PictureDocument.Create(8000, 10000).Straighten(45, false);
        Assert.Equal(8000, bounded.CanvasWidth); Assert.Equal(10000, bounded.CanvasHeight);
        var angle = original.Straighten(-30, true);
        var schema4 = Encoding.UTF8.GetString(angle.Serialize()).Replace($"\"schemaVersion\":{PictureDocument.CurrentSchemaVersion}", "\"schemaVersion\":4", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => PictureDocument.Deserialize(Encoding.UTF8.GetBytes(schema4)));
        var missingChoice = Encoding.UTF8.GetString(angle.Serialize()).Replace(",\"expandCanvas\":true", "", StringComparison.Ordinal);
        Assert.Throws<System.Text.Json.JsonException>(() => PictureDocument.Deserialize(Encoding.UTF8.GetBytes(missingChoice)));
    }

    [AvaloniaFact]
    public async Task Invalid_angle_reorder_preserves_actual_crop_graph_history_pixels_and_source_identity()
    {
        var fixture = await SourceAsync(); var session = new PictureEditorSession(new PictureCropService().OpenSource(fixture.Source));
        using (session.Apply("Crop", document => document.Crop(2, 0, 2, 2))) { }
        using (session.Apply("Angle", document => document.Straighten(90, true))) { }
        var before = session.Document.Serialize(); var operation = session.LastOperation;
        using var rendered = PictureCropService.Render(session.Document); var pixels = Rgba(rendered);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.MoveOperation(session.CaptureOperation(1), 0));
        Assert.Equal(before, session.Document.Serialize()); Assert.Same(operation, session.LastOperation);
        using var after = PictureCropService.Render(session.Document); Assert.Equal(pixels, Rgba(after));
        using (var removed = session.RemoveOperation(session.CaptureOperation(1))) Assert.Equal(new PixelSize(2, 2), removed.PixelSize);
        using (var recovered = session.Undo()) Assert.Equal(pixels, Rgba(recovered));
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
    }

    [AvaloniaTheory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Original_saved_legacy_history_checksums_and_operation_metadata_survive_read_and_forward_angle_upgrade(int schema)
    {
        var fixture = await SourceAsync();
        var sourceDocument = new PictureCropService().OpenSource(fixture.Source);
        var legacyJson = Encoding.UTF8.GetString(sourceDocument.Serialize())
            .Replace($"\"schemaVersion\":{PictureDocument.CurrentSchemaVersion}", "\"schemaVersion\":" + schema, StringComparison.Ordinal);
        var legacyDocument = PictureDocument.Deserialize(Encoding.UTF8.GetBytes(legacyJson));
        var actualHistory = new DocumentMutationHistory<LegacyState>(new(legacyDocument),
            state => new(PictureDocument.Deserialize(state.Document.Serialize())));
        actualHistory.Apply("Original legacy geometry", DocumentOperationOrigin.User,
            state => state.Document = schema == 3 ? state.Document.Crop(1, 0, 3, 2) : state.Document.ResizeCanvas(6, 2, 1, 0));
        var firstMetadata = actualHistory.LastOperation!;
        actualHistory.Apply("Original legacy flip", DocumentOperationOrigin.User, state => state.Document = state.Document.Flip(true));
        var secondMetadata = actualHistory.LastOperation!;
        var nextRevision = checked(actualHistory.Current.Document.Revision + 1);
        Assert.True(actualHistory.Undo());
        actualHistory.Current.Document = LegacyCopy(actualHistory.Current.Document, nextRevision);
        var current = actualHistory.Current.Document;
        var envelope = actualHistory.CaptureSnapshotHistory("9to1.Picture.Document", current.DocumentId,
            current.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), current.Serialize(),
            state => (state.Document.Serialize(), state.Document.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var retainedOld = LegacyCopy(current, current.Revision, envelope);
        Assert.Equal(schema, retainedOld.SchemaVersion);
        var path = Path.Combine(fixture.Directory, "actual-legacy-" + schema + ".picture.json");
        await retainedOld.SaveAsync(path, TestContext.Current.CancellationToken);
        var savedBytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var loaded = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(savedBytes, loaded.Serialize());
        var restored = new PictureEditorSession(loaded, path);
        Assert.Equal(schema, restored.Document.SchemaVersion); Assert.True(restored.CanUndo); Assert.True(restored.CanRedo);
        Assert.Equal(envelope.LastOperation, restored.LastOperation);
        Assert.Equal(firstMetadata, Assert.Single(loaded.SemanticHistory!.Undo).Operation);
        Assert.Equal(secondMetadata, Assert.Single(loaded.SemanticHistory.Redo).Operation);
        Assert.Equal(savedBytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        byte[] currentPixels;
        using (var currentFrame = PictureCropService.Render(restored.Document)) currentPixels = Rgba(currentFrame);
        using (var before = restored.Undo()) Assert.Equal(fixture.Pixels, Rgba(before));
        using (var repeated = restored.Redo()) Assert.Equal(currentPixels, Rgba(repeated));
        Assert.Equal(schema, restored.Document.SchemaVersion);
        using (restored.Apply("New retained angle", document => document.Straighten(90, true))) { }
        Assert.Equal(5, restored.Document.SchemaVersion);
        using (var olderFrame = restored.Undo()) Assert.Equal(currentPixels, Rgba(olderFrame));
        // Undo preserves the original schema3/4 history frame bytes, while the
        // live schema5 owner keeps its forward format and retained angle redo.
        Assert.Equal(5, restored.Document.SchemaVersion); Assert.True(restored.CanRedo);
        var upgradedPath = Path.Combine(fixture.Directory, "angle-forward-" + schema + ".picture.json");
        await restored.SaveAsync(upgradedPath, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(upgradedPath, TestContext.Current.CancellationToken), upgradedPath);
        Assert.Equal(5, cold.Document.SchemaVersion); Assert.Equal(current.DocumentId, cold.Document.DocumentId); Assert.True(cold.CanRedo);
        using (var redone = cold.Redo()) Assert.Equal(new PixelSize(current.CanvasHeight, current.CanvasWidth), redone.PixelSize);
        Assert.Equal(new StraightenOperation(90, true), cold.Document.Operations[^1]);
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
    }
    private sealed class LegacyState(PictureDocument document) { internal PictureDocument Document { get; set; } = document; }
    private static PictureDocument LegacyCopy(PictureDocument original, long revision, ProductivitySnapshotHistory? history = null) => new()
    {
        DocumentId = original.DocumentId, SchemaVersion = original.SchemaVersion, DisplayName = original.DisplayName,
        FileId = original.FileId, SourcePath = original.SourcePath, SourceRevision = original.SourceRevision,
        InitialCanvasWidth = original.InitialCanvasWidth, InitialCanvasHeight = original.InitialCanvasHeight,
        CanvasWidth = original.CanvasWidth, CanvasHeight = original.CanvasHeight, CompositionState = original.CompositionState,
        Operations = original.Operations, Revision = revision, SemanticHistory = history
    };

    [AvaloniaFact]
    public void Angle_transforms_same_shared_hybrid_graph_without_flattening_source_or_vector_identity()
    {
        var shape = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle);
        var vector = new HomeVectorShapeObjectHandler().Project(shape);
        var content = System.Text.Json.Nodes.JsonNode.Parse(vector.Content.GetRawText())!.AsObject();
        content["OpaqueFixtureInfo"] = new System.Text.Json.Nodes.JsonObject { ["note"] = "Retained unknown shared content", ["value"] = 42 };
        vector = vector with { Content = System.Text.Json.JsonSerializer.SerializeToElement(content) };
        var session = new PictureEditorSession(PictureDocument.Create(100, 60));
        using (session.AddSharedVector(vector)) { }
        var before = session.Document.CompositionState!;
        Assert.Contains("OpaqueFixtureInfo", Encoding.UTF8.GetString(before));
        using (var fitted = session.Apply("Angle", document => document.Straighten(90, true)))
            Assert.Equal(new PixelSize(60, 100), fitted.PixelSize);
        Assert.Equal(before, session.Document.CompositionState);
        Assert.Contains(PictureCompositionAdapter.Read(session.Document).Pages[0].Objects, item => item.ObjectId == vector.ObjectId);
        using (session.Undo()) { }
        Assert.Equal(before, session.Document.CompositionState); Assert.Empty(session.Document.Operations);
    }

    [AvaloniaFact]
    public async Task Actual_CUI_angle_and_fit_drafts_survive_unrelated_actions_modify_same_edit_and_save_cold_pixels()
    {
        var fixture = await SourceAsync(); var window = new MainWindow(new PictureFixtureReadiness()); Exception? bodyFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Angle initialization");
            window.Show(); window.UpdateLayout();
            typeof(MainWindow).GetMethod("LoadLocalPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [fixture.Source]);
            var session = Assert.IsType<PictureEditorSession>(typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            var id = session.Document.DocumentId;
            var angle = Find<NumericUpDown>(window, "StraightenAngleBox"); var mode = Find<ComboBox>(window, "StraightenCanvasModeBox");
            Assert.Equal(new[] { "Fit entire image", "Keep canvas · clip rotated edges" }, mode.ItemsSource!.Cast<string>());
            angle.Value = 90; mode.SelectedIndex = 1;
            Find<TextBox>(window, "CropBoundsBox").Text = "0, 0, 1, 1";
            await Click(window, "ZoomInButton"); await Click(window, "MetadataRemoveAllButton");
            Assert.Equal(90m, angle.Value); Assert.Equal(1, mode.SelectedIndex);
            await Click(window, "ApplyStraightenButton");
            Assert.Equal(new StraightenOperation(90, false), Assert.Single(session.Document.Operations));
            Assert.Equal(new PixelSize(4, 2), Preview(window).PixelSize);
            Assert.Equal("0, 0, 1, 1", Find<TextBox>(window, "CropBoundsBox").Text);
            Assert.Equal(0m, angle.Value); Assert.Equal(0, mode.SelectedIndex);
            var row = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "1. Straighten 90° · keep canvas"));
            await PictureCuiActionFixture.ClickAsync(window, row);
            Assert.Equal(90m, angle.Value); Assert.Equal(1, mode.SelectedIndex);
            mode.SelectedIndex = 0; await Click(window, "ReplaceSelectedEditButton");
            Assert.Equal(new StraightenOperation(90, true), Assert.Single(session.Document.Operations));
            AssertQuarter(fixture.Pixels, Rgba(Preview(window)));
            await Click(window, "UndoButton"); Assert.Equal(new StraightenOperation(90, false), Assert.Single(session.Document.Operations));
            await Click(window, "RedoButton"); AssertQuarter(fixture.Pixels, Rgba(Preview(window)));
            angle.Value = 30; mode.SelectedIndex = -1; var beforeInvalid = session.Document.Serialize();
            await Click(window, "ApplyStraightenButton"); Assert.Equal(beforeInvalid, session.Document.Serialize());
            Assert.Equal(30m, angle.Value); Assert.Equal(-1, mode.SelectedIndex);
            Assert.Contains("angle", Find<TextBlock>(window, "StatusText").Text);
            mode.SelectedIndex = 0; await Click(window, "FitButton"); Assert.Equal(30m, angle.Value);
            var path = Path.Combine(fixture.Directory, "native-angle.picture.json");
            await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            Assert.Equal(id, cold.Document.DocumentId); Assert.Equal(session.Document.Operations, cold.Document.Operations);
            using var rendered = PictureCropService.Render(cold.Document); AssertQuarter(fixture.Pixels, Rgba(rendered));
            Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.Current.CancellationToken));
        }
        catch (Exception failure) { bodyFailure = failure; RetainedFailedOwners.Add((window, failure)); throw; }
        finally
        {
            if (bodyFailure is null)
            {
                window.Close(); Assert.NotNull(window.OriginalClose); var sameClose = window.OriginalClose!;
                try { await sameClose.ObserveOriginalAsync(window, "Angle original close"); }
                catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
                Assert.True(sameClose.IsCompletedSuccessfully); Assert.Same(sameClose, window.OriginalClose);
            }
        }
    }

    private static Task Click(MainWindow window, string id) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, id));
    private static T Find<T>(MainWindow window, string id) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == id);
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private sealed record SourceFixture(string Directory, string Source, byte[] Bytes, byte[] Pixels);
    private static async Task<SourceFixture> SourceAsync()
    {
        var directory = Directory.CreateTempSubdirectory("picture-angle-").FullName;
        var path = Path.Combine(directory, "source.png");
        byte[] pixels = [255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,255,255,
            255,255,0,255, 0,255,255,255, 255,0,255,255, 0,0,0,255];
        using (var source = Pixels(4, 2, 96, pixels)) using (var output = File.Create(path)) source.Save(output);
        return new(directory, path, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken), pixels);
    }
    private static void AssertQuarter(byte[] source, byte[] target)
    {
        for (var y = 0; y < 4; y++) for (var x = 0; x < 2; x++)
            Assert.Equal(source.AsSpan(((1 - x) * 4 + y) * 4, 4).ToArray(), target.AsSpan((y * 2 + x) * 4, 4).ToArray());
    }
    private static WriteableBitmap Pixels(int width, int height, int dpi, byte[] rgba)
    {
        var source = new WriteableBitmap(new(width, height), new(dpi, dpi), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var framebuffer = source.Lock();
        for (var y = 0; y < height; y++) Marshal.Copy(rgba, y * width * 4, IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes), width * 4);
        return source;
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
