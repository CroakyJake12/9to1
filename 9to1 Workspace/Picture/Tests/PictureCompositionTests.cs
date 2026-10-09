using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Productivity.NativeUI;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureCompositionTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedUiOwners = [];

    [AvaloniaFact]
    public async Task Canonical_layers_change_actual_pixels_share_history_and_preserve_graph_identity_after_reopen()
    {
        var session = new PictureEditorSession(PictureDocument.Create(100, 80));
        var vector = Rectangle("Red", "#FF0000");
        using (var inserted = session.AddSharedVector(vector)) AssertRed(inserted, 50, 40);
        var graph = PictureCompositionAdapter.Read(session.Document);
        var page = graph.Pages[0];
        var objectId = vector.ObjectId;
        var layerId = page.Objects.Single(item => item.ObjectId == objectId).LayerId;
        using (var hidden = session.SetLayerVisibility(session.CaptureComposition(layerId, PictureCompositionTargetKind.Layer), false))
            Assert.Equal(0, Pixel(hidden, 50, 40).A);
        using (var undone = session.Undo()) AssertRed(undone, 50, 40);
        using (var redone = session.Redo()) Assert.Equal(0, Pixel(redone, 50, 40).A);
        var path = NewPath("layers");
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        var restored = PictureCompositionAdapter.Read(reopened.Document);
        Assert.Equal(graph.ArtifactId, restored.ArtifactId);
        Assert.Equal(page.PageId, restored.Pages[0].PageId);
        Assert.Equal(page.LayerOrder, restored.Pages[0].LayerOrder);
        Assert.Equal(page.ObjectOrder, restored.Pages[0].ObjectOrder);
        Assert.NotNull(restored.SemanticHistory);
        Assert.False(restored.Pages[0].Layers.Single(layer => layer.LayerId == layerId).IsVisible);
        using (var rendered = PictureCropService.Render(reopened.Document)) Assert.Equal(0, Pixel(rendered, 50, 40).A);
        using (var shown = reopened.SetLayerVisibility(reopened.CaptureComposition(layerId, PictureCompositionTargetKind.Layer), true))
            AssertRed(shown, 50, 40);
        Assert.Equal(objectId, restored.Pages[0].Objects.Single(item => item.ObjectTypeId == "drawing.vector").ObjectId);
    }

    [AvaloniaFact]
    public void Layer_locks_and_same_owner_revision_receipts_prevent_invalid_vector_changes_without_mutation()
    {
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        var vector = Rectangle("Red", "#FF0000");
        using (session.AddSharedVector(vector)) { }
        var page = PictureCompositionAdapter.Read(session.Document).Pages[0];
        var layerId = page.Objects.Single(item => item.ObjectId == vector.ObjectId).LayerId;
        var stale = session.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector);
        using (session.SetLayerLocked(session.CaptureComposition(layerId, PictureCompositionTargetKind.Layer), true)) { }
        var before = session.Document.Serialize();
        var detachedBytes = session.Document.CompositionState!;
        detachedBytes[0] ^= 0xff;
        Assert.Equal(before, session.Document.Serialize()); // Public snapshots cannot alter the same history owner.
        Assert.Throws<InvalidOperationException>(() => session.MoveVector(stale, new(0, 0, 10, 10)));
        var current = session.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector);
        var denied = Assert.Throws<InvalidOperationException>(() => session.SetVectorFill(current, FirstPath(vector), "#00FF00"));
        Assert.Contains("PermissionDenied", denied.Message);
        Assert.Equal(before, session.Document.Serialize());
        var foreign = new PictureEditorSession(session.Document).CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector);
        Assert.Throws<InvalidOperationException>(() => session.MoveVector(foreign, new(0, 0, 10, 10)));
        Assert.Equal(before, session.Document.Serialize());
        using (session.SetLayerLocked(session.CaptureComposition(layerId, PictureCompositionTargetKind.Layer), false)) { }
        using var actual = session.SetVectorFill(session.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), FirstPath(vector), "#00FF00");
        AssertGreen(actual, 50, 50);
    }

    [AvaloniaFact]
    public void Shared_graph_layer_order_changes_overlap_and_global_raster_edits_transform_the_complete_composition()
    {
        var session = new PictureEditorSession(PictureDocument.Create(100, 80));
        var red = Rectangle("Red", "#FF0000");
        var green = Rectangle("Green", "#00FF00");
        using (session.AddSharedVector(red)) { }
        using (session.CreateLayer("Green layer")) { }
        var page = PictureCompositionAdapter.Read(session.Document).Pages[0];
        var greenLayer = page.Layers.Single(layer => layer.Name == "Green layer").LayerId;
        using (var added = session.AddSharedVector(green, session.CaptureComposition(greenLayer, PictureCompositionTargetKind.Layer)))
            AssertGreen(added, 50, 40);
        using (var reordered = session.MoveLayer(session.CaptureComposition(greenLayer, PictureCompositionTargetKind.Layer), 0))
            AssertRed(reordered, 50, 40);
        using (var undone = session.Undo()) AssertGreen(undone, 50, 40);
        var identities = PictureCompositionAdapter.Read(session.Document).Pages[0].ObjectOrder.ToArray();
        using (var cropped = session.Apply("Crop hybrid", document => document.Crop(40, 30, 20, 10)))
        { Assert.Equal(new PixelSize(20, 10), cropped.PixelSize); AssertGreen(cropped, 10, 5); }
        using (var rotated = session.Apply("Rotate hybrid", document => document.Rotate()))
        { Assert.Equal(new PixelSize(10, 20), rotated.PixelSize); AssertGreen(rotated, 5, 10); }
        Assert.Equal(identities, PictureCompositionAdapter.Read(session.Document).Pages[0].ObjectOrder);
        using (var reverted = session.Apply("Revert source", document => document.RevertToOriginal(100, 80)))
        { Assert.Equal(0, Pixel(reverted, 50, 40).A); Assert.Null(session.Document.CompositionState); }
        using var restored = session.Undo();
        AssertGreen(restored, 5, 10);
        Assert.Equal(identities, PictureCompositionAdapter.Read(session.Document).Pages[0].ObjectOrder);
    }

    [AvaloniaFact]
    public async Task Save_copy_branches_artifact_identity_and_retains_shared_vector_payload_without_source_alias()
    {
        var session = new PictureEditorSession(PictureDocument.Create(100, 100, fileId: "explicit-local-identity-fixture"));
        var vector = Rectangle("Red", "#FF0000");
        using (session.AddSharedVector(vector)) { }
        var path = NewPath("composition-copy");
        var originalId = session.Document.DocumentId;
        await session.SaveAsync(path, saveCopy: true, TestContext.Current.CancellationToken);
        var copy = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        Assert.NotEqual(originalId, copy.DocumentId);
        Assert.Null(copy.FileId);
        var graph = PictureCompositionAdapter.Read(copy);
        Assert.Equal(copy.DocumentId, graph.ArtifactId);
        Assert.Equal(vector.ObjectId, graph.Pages[0].Objects.Single(item => item.ObjectTypeId == "drawing.vector").ObjectId);
        Assert.Null(graph.SemanticHistory);
        Assert.Null(PictureCompositionAdapter.SourceObject(graph).SharedPayload!.Value.GetProperty("fileId").GetString());
        using var rendered = PictureCropService.Render(copy);
        AssertRed(rendered, 50, 50);
        Assert.Equal(originalId, session.Document.DocumentId);
    }

    [AvaloniaFact]
    public void Unknown_vector_fields_are_preserved_and_visible_while_unsupported_clipping_is_refused_before_mutation()
    {
        var shared = Rectangle("Retained", "#FF0000");
        var content = JsonNode.Parse(shared.Content.GetRawText())!.AsObject();
        content["FutureField"] = new JsonObject { ["retained"] = 23 };
        shared = shared with { Content = JsonSerializer.SerializeToElement(content) };
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (session.AddSharedVector(shared)) { }
        using (session.SetVectorFill(session.CaptureComposition(shared.ObjectId, PictureCompositionTargetKind.Vector), FirstPath(shared), "#00FF00")) { }
        var graph = PictureCompositionAdapter.Read(session.Document);
        var actual = PictureCompositionAdapter.ReadVector(graph.Pages[0].Objects.Single(item => item.ObjectId == shared.ObjectId));
        Assert.Equal(23, actual.Content.GetProperty("FutureField").GetProperty("retained").GetInt32());
        using var original = PictureCropService.OpenVerifiedSource(session.Document);
        using var composed = SharedVisualCompositionRenderer.Render(graph, graph.Pages[0].PageId, original.PixelSize,
            new Dictionary<Guid, Bitmap> { [PictureCompositionAdapter.SourceObject(graph).ObjectId] = original }, out var retained);
        Assert.Contains(retained, value => value.Contains("FutureField", StringComparison.Ordinal));
        AssertGreen(composed, 50, 50);
        var clipped = Rectangle("Unsupported clip", "#006FB9");
        var clip = JsonNode.Parse(clipped.Content.GetRawText())!.AsObject(); clip["ClippingPathId"] = FirstPath(clipped);
        clipped = clipped with { Content = JsonSerializer.SerializeToElement(clip) };
        var before = session.Document.Serialize();
        Assert.Throws<NotSupportedException>(() => session.AddSharedVector(clipped));
        Assert.Equal(before, session.Document.Serialize());
    }

    [AvaloniaFact]
    public void Typed_shared_object_transactions_use_the_actual_revision_history_lock_and_detached_payload()
    {
        var graph = CanvasArtifact.Create(mode: CanvasDocumentMode.Paged);
        var page = graph.Pages[0];
        var shared = Rectangle("Detached", "#FF0000");
        var item = new CanvasObject { ObjectId = shared.ObjectId, ObjectTypeId = shared.ObjectType, LayerId = page.Layers[0].LayerId,
            Geometry = new(0, 0, 50, 50), SharedPayload = JsonSerializer.SerializeToElement(shared) };
        var owner = new CanvasArtifactSession(graph);
        var request = PictureCompositionAdapter.Request(owner);
        var inserted = owner.InsertSharedObjects(request, page.PageId, [item]);
        Assert.True(inserted.IsSuccess);
        Assert.Equal(inserted.Value, owner.InsertSharedObjects(request, page.PageId, [item]).Value);
        Assert.Single(owner.GetArtifactSnapshot().Pages[0].Objects);
        var before = owner.CurrentRevisionId;
        Assert.Equal(CanvasApiErrorCode.OperationIdConflict,
            owner.InsertSharedObjects(request, page.PageId, [item with { Geometry = new(5, 5, 50, 50) }]).Error!.Code);
        Assert.Equal(before, owner.CurrentRevisionId);
        Assert.True(owner.Undo(PictureCompositionAdapter.Request(owner)).IsSuccess);
        Assert.Empty(owner.GetArtifactSnapshot().Pages[0].Objects);
        Assert.True(owner.Redo(PictureCompositionAdapter.Request(owner)).IsSuccess);
        Assert.True(owner.SetLayerLocked(PictureCompositionAdapter.Request(owner), page.PageId, item.LayerId, true).IsSuccess);
        before = owner.CurrentRevisionId;
        var locked = owner.UpdateSharedObject(PictureCompositionAdapter.Request(owner), page.PageId, item with { Geometry = new(1, 2, 30, 40) });
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, locked.Error!.Code);
        Assert.Equal(before, owner.CurrentRevisionId);
        var preserved = PictureCompositionAdapter.ReadVector(owner.GetArtifactSnapshot().Pages[0].Objects.Single());
        Assert.Equal(shared.Content.GetRawText(), preserved.Content.GetRawText());
    }

    [AvaloniaFact]
    public async Task Actual_CUI_layer_shape_path_and_geometry_controls_change_the_same_document_and_pixels()
    {
        var window = new MainWindow(new PictureFixtureReadiness());
        Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken);
            window.Show(); window.UpdateLayout();
            Find<NumericUpDown>(window, "WidthBox").Value = 100;
            Find<NumericUpDown>(window, "HeightBox").Value = 80;
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "NewButton"));
            var session = CurrentSession(window);
            var id = session.Document.DocumentId;
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "AddSharedVectorButton"));
            var graph = PictureCompositionAdapter.Read(session.Document);
            var vectorId = graph.Pages[0].Objects.Single(item => item.ObjectTypeId == "drawing.vector").ObjectId;
            await PictureCuiActionFixture.ClickAsync(window, Row(window, "Shape: Shape"));
            Find<NumericUpDown>(window, "VectorXBox").Value = 5;
            Find<NumericUpDown>(window, "VectorYBox").Value = 5;
            Find<NumericUpDown>(window, "VectorWidthBox").Value = 20;
            Find<NumericUpDown>(window, "VectorHeightBox").Value = 20;
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "ApplyVectorPlacementButton"));
            await PictureCuiActionFixture.ClickAsync(window, Row(window, "Path 1"));
            Find<TextBox>(window, "VectorFillBox").Text = "#FF0000";
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "ApplyVectorFillButton"));
            AssertRed(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source), 15, 15);
            await PictureCuiActionFixture.ClickAsync(window, Row(window, "Objects · visible"));
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "ToggleLayerLockButton"));
            Assert.False(Find<Button>(window, "ApplyVectorPlacementButton").IsEnabled);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "ToggleLayerLockButton"));
            Assert.True(Find<Button>(window, "ApplyVectorPlacementButton").IsEnabled);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "ToggleLayerVisibilityButton"));
            Assert.Equal(0, Pixel(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source), 15, 15).A);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "UndoButton"));
            AssertRed(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source), 15, 15);
            var path = NewPath("native-composition");
            await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            var reopened = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
            Assert.Same(session, CurrentSession(window));
            Assert.Equal(id, reopened.DocumentId);
            Assert.Equal(vectorId, PictureCompositionAdapter.Read(reopened).Pages[0].Objects.Single(item => item.ObjectTypeId == "drawing.vector").ObjectId);
            using var actual = PictureCropService.Render(reopened);
            AssertRed(actual, 15, 15);
        }
        catch (Exception error) { firstFailure = error; RetainedFailedUiOwners.Add((window, error)); throw; }
        finally
        {
            if (firstFailure is null)
            {
                window.Close(); Assert.NotNull(window.OriginalClose);
                var close = window.OriginalClose!;
                try { await close; }
                catch (Exception error) { RetainedFailedUiOwners.Add((window, error)); throw; }
                Assert.Same(close, window.OriginalClose); Assert.True(close.IsCompletedSuccessfully);
            }
        }
    }

    [AvaloniaFact]
    public async Task Shared_spe_vector_CUI_object_mounts_the_original_binding_and_applies_local_shape_transform_once()
    {
        var original = Rectangle("Native typed vector", "#FF0000");
        var content = JsonNode.Parse(original.Content.GetRawText())!.AsObject();
        content["Transform"]!["TranslateX"] = 10;
        original = original with { Content = JsonSerializer.SerializeToElement(content) };
        var binding = new HomeVectorShapeObjectHandler().Render(original).VectorBindings.Single();
        var registry = new CuiControlRegistry(); SharedVectorObjectControl.Register(registry, [binding]);
        var scene = new CuiSceneHost(registry);
        var window = new Window { Content = scene, Width = 100, Height = 100 };
        try
        {
            var parser = new CuiRichParser();
            var definition = parser.Parse("<Cui><Page><Object Type=\"spe.vector\" id=\"" + binding.ControlId + "\" Width=\"100\" Height=\"100\" /></Page></Cui>");
            var model = new CuiViewModel();
            await scene.ShowAsync(new CuiNativeScene("picture", "Shared vector fixture", "Imagine", definition, model, model,
                new PictureFixtureReadiness()) { IsPublicationCurrent = () => true });
            window.Show(); window.UpdateLayout();
            var actual = Assert.Single(window.GetLogicalDescendants().OfType<SharedVectorObjectControl>());
            Assert.Same(binding, actual.OriginalBinding);
            using var result = new RenderTargetBitmap(new PixelSize(100, 100), new Vector(96, 96));
            using (var context = result.CreateDrawingContext()) new SharedVectorDrawing(binding).Draw(context, new Rect(0, 0, 100, 100));
            Assert.Equal(0, Pixel(result, 5, 50).A); // The canonical +10 translation is applied.
            AssertRed(result, 15, 50); // It is applied once, not twice.
        }
        finally { await scene.CloseOriginalAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public void Shared_native_output_size_maps_the_actual_canonical_page_without_rewriting_its_elements()
    {
        var session = new PictureEditorSession(PictureDocument.Create(80, 60));
        using (session.AddSharedVector(Rectangle("Projected red", "#FF0000"))) { }
        var graph = PictureCompositionAdapter.Read(session.Document);
        var originalBytes = CanvasArtifactCodec.Serialize(graph);
        using var source = PictureCropService.OpenVerifiedSource(session.Document);
        using var output = SharedVisualCompositionRenderer.Render(graph, graph.Pages[0].PageId, new PixelSize(160, 120),
            new Dictionary<Guid, Bitmap> { [PictureCompositionAdapter.SourceObject(graph).ObjectId] = source }, out _);
        Assert.Equal(new PixelSize(160, 120), output.PixelSize);
        AssertRed(output, 80, 60); // Old direct-unit drawing left this point outside the shape.
        Assert.Equal(0, Pixel(output, 10, 10).A);
        Assert.Equal(originalBytes, CanvasArtifactCodec.Serialize(graph));
    }

    private static HomeProductivityObject Rectangle(string name, string color)
    {
        var shape = new DocumentVectorShape { Name = name, ViewBox = new() { Width = 100, Height = 100 }, Paths =
            [new() { Fill = new() { Kind = DocumentVectorFillKind.Solid, Color = color }, Stroke = new() { Enabled = false }, Subpaths =
                [new() { Closed = true, Nodes = [new() { X = 0, Y = 0 }, new() { X = 100, Y = 0 }, new() { X = 100, Y = 100 }, new() { X = 0, Y = 100 }] }] }] };
        return new HomeVectorShapeObjectHandler().Project(shape);
    }
    private static Guid FirstPath(HomeProductivityObject vector) => HomeVectorShapeObjectHandler.ReadCanonical(vector.Content, vector.ObjectId).Paths[0].Id;
    private static string NewPath(string label) => Path.Combine(Path.GetTempPath(), "picture-" + label + "-" + Guid.NewGuid().ToString("N") + ".picture.json");
    private static T Find<T>(MainWindow window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static Button Row(MainWindow window, string label) => window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label));
    private static PictureEditorSession CurrentSession(MainWindow window) => Assert.IsType<PictureEditorSession>(typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
    private static void AssertRed(Bitmap bitmap, int x, int y) { var p = Pixel(bitmap, x, y); Assert.True(p.R > 220 && p.G < 30 && p.A > 220, $"Expected red pixel, observed {p}."); }
    private static void AssertGreen(Bitmap bitmap, int x, int y) { var p = Pixel(bitmap, x, y); Assert.True(p.G > 220 && p.R < 30 && p.A > 220, $"Expected green pixel, observed {p}."); }
    private static (byte R, byte G, byte B, byte A) Pixel(Bitmap bitmap, int x, int y)
    {
        var bytes = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)]; var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), pinned.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4);
            var offset = checked((y * bitmap.PixelSize.Width + x) * 4); return (bytes[offset + 2], bytes[offset + 1], bytes[offset], bytes[offset + 3]); }
        finally { pinned.Free(); }
    }
}
