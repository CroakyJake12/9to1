using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureLayerTransferTests
{
    private static readonly List<(object Owner, Task? Original, Exception Failure)> RetainedFailures = [];
    private static readonly List<object> RetainedOriginals = [];
    private static HomeProductivityObject Shape(string name)
    {
        var shape = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle); shape.Name = name;
        shape.Paths[0].Fill.Color = "#FF00FF00";
        var projected = new HomeVectorShapeObjectHandler().Project(shape); var content = JsonNode.Parse(projected.Content.GetRawText())!;
        content["FutureShape"] = new JsonObject { ["retained"] = 93 }; content["Paths"]![0]!["FuturePath"] = "retained";
        return projected with { Content = JsonSerializer.SerializeToElement(content) };
    }
    private static Guid SourceLayer(PictureEditorSession owner, Guid vector) => Item(owner, vector).LayerId;
    private static CanvasObject Item(PictureEditorSession owner, Guid vector) => PictureCompositionAdapter.Read(owner.Document).Pages[0].Objects.Single(item => item.ObjectId == vector);
    private static Guid Layer(PictureEditorSession owner, string name) => PictureCompositionAdapter.Read(owner.Document).Pages[0].Layers.Single(layer => layer.Name == name).LayerId;
    private static PictureCompositionTarget Vector(PictureEditorSession owner, Guid id) => owner.CaptureComposition(id, PictureCompositionTargetKind.Vector);
    private static PictureCompositionTarget Destination(PictureEditorSession owner, Guid id) => owner.CaptureComposition(id, PictureCompositionTargetKind.Layer);
    private static string PathFor(string label) => Path.Combine(Directory.CreateTempSubdirectory("picture-layer-" + label + "-").FullName, "editable.picture.json");
    private static byte[] Rgba(Bitmap bitmap)
    {
        using var converted = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = converted.Lock(); bitmap.CopyPixels(buffer); var bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        for (var y = 0; y < bitmap.PixelSize.Height; y++) Marshal.Copy(IntPtr.Add(buffer.Address, y * buffer.RowBytes), bytes, y * bitmap.PixelSize.Width * 4, bitmap.PixelSize.Width * 4);
        return bytes;
    }
    private static byte[] GreenSquare()
    {
        var expected = new byte[16 * 16 * 4];
        for (var y = 1; y < 5; y++) for (var x = 1; x < 5; x++) { var index = (y * 16 + x) * 4; expected[index + 1] = expected[index + 3] = 255; }
        return expected;
    }

    [AvaloniaFact]
    public async Task Canonical_layer_transfer_changes_real_visibility_preserves_exact_shared_object_and_original_source_with_cold_history()
    {
        var directory = Directory.CreateTempSubdirectory("picture-layer-original-").FullName; var originalPath = Path.Combine(directory, "source.png");
        using (var original = PictureCropService.Render(PictureDocument.Create(16, 16))) using (var bytes = File.Create(originalPath)) original.Save(bytes);
        var originalBytes = await File.ReadAllBytesAsync(originalPath, TestContext.Current.CancellationToken);
        var owner = new PictureEditorSession(new PictureCropService().OpenSource(originalPath)); var vector = Shape("Move me");
        using (owner.AddSharedVector(vector)) { } using (owner.MoveVector(Vector(owner, vector.ObjectId), new(1, 1, 4, 4))) { }
        var sourceLayer = SourceLayer(owner, vector.ObjectId); using (owner.CreateLayer("Hidden destination")) { } var hidden = Layer(owner, "Hidden destination");
        using (owner.SetLayerVisibility(Destination(owner, hidden), false)) { }
        var before = Item(owner, vector.ObjectId);
        using (var actual = PictureCropService.Render(owner.Document)) Assert.Equal(GreenSquare(), Rgba(actual));
        using (var moved = owner.MoveVectorToLayer(Vector(owner, vector.ObjectId), Destination(owner, hidden))) Assert.Equal(new byte[16 * 16 * 4], Rgba(moved));
        var after = Item(owner, vector.ObjectId);
        Assert.Equal(JsonSerializer.Serialize(before with { LayerId = hidden, RevisionId = after.RevisionId }), JsonSerializer.Serialize(after));
        Assert.Equal(before.SharedPayload!.Value.GetRawText(), after.SharedPayload!.Value.GetRawText()); Assert.Equal(hidden, after.LayerId);
        using (var undo = owner.Undo()) Assert.Equal(GreenSquare(), Rgba(undo)); Assert.Equal(sourceLayer, Item(owner, vector.ObjectId).LayerId);
        using (var redo = owner.Redo()) Assert.Equal(new byte[16 * 16 * 4], Rgba(redo)); Assert.Equal(hidden, Item(owner, vector.ObjectId).LayerId);
        var path = PathFor("cold"); await owner.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(owner.Document.DocumentId, cold.Document.DocumentId); Assert.Equal(hidden, Item(cold, vector.ObjectId).LayerId);
        Assert.Equal(before.SharedPayload.Value.GetRawText(), Item(cold, vector.ObjectId).SharedPayload!.Value.GetRawText());
        using (var undo = cold.Undo()) Assert.Equal(GreenSquare(), Rgba(undo)); using (var redo = cold.Redo()) Assert.Equal(new byte[16 * 16 * 4], Rgba(redo));
        using (var back = cold.MoveVectorToLayer(Vector(cold, vector.ObjectId), Destination(cold, sourceLayer))) Assert.Equal(GreenSquare(), Rgba(back));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(originalPath, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Stale_foreign_and_both_locked_layer_requests_refuse_before_graph_or_history_changes()
    {
        var owner = new PictureEditorSession(PictureDocument.Create(16, 16)); var vector = Shape("Guarded"); using (owner.AddSharedVector(vector)) { }
        using (owner.CreateLayer("Destination")) { } var destination = Layer(owner, "Destination"); var source = SourceLayer(owner, vector.ObjectId);
        var oldVector = Vector(owner, vector.ObjectId); var oldLayer = Destination(owner, destination);
        using (owner.SetLayerLocked(Destination(owner, destination), true)) { }
        var accepted = owner.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => { using var refused = owner.MoveVectorToLayer(oldVector, oldLayer); });
        Assert.Throws<InvalidOperationException>(() => { using var refused = owner.MoveVectorToLayer(Vector(owner, vector.ObjectId), Destination(owner, destination)); });
        var foreign = new PictureEditorSession(owner.Document);
        Assert.Throws<InvalidOperationException>(() => { using var refused = owner.MoveVectorToLayer(Vector(foreign, vector.ObjectId), Destination(owner, destination)); });
        Assert.Throws<InvalidOperationException>(() => { using var refused = owner.MoveVectorToLayer(Vector(owner, vector.ObjectId), Destination(foreign, destination)); });
        Assert.Equal(accepted, owner.Document.Serialize()); Assert.Equal(source, Item(owner, vector.ObjectId).LayerId);
        using (owner.Undo()) { } Assert.False(PictureCompositionAdapter.Read(owner.Document).Pages[0].Layers.Single(layer => layer.LayerId == destination).IsLocked);
        using (owner.SetLayerLocked(Destination(owner, source), true)) { }
        accepted = owner.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => { using var refused = owner.MoveVectorToLayer(Vector(owner, vector.ObjectId), Destination(owner, destination)); });
        Assert.Equal(accepted, owner.Document.Serialize()); using (owner.Undo()) { }
        var before = Item(owner, vector.ObjectId); using (owner.MoveVectorToLayer(Vector(owner, vector.ObjectId), Destination(owner, destination))) { }
        var after = Item(owner, vector.ObjectId); Assert.Equal(JsonSerializer.Serialize(before with { LayerId = destination, RevisionId = after.RevisionId }), JsonSerializer.Serialize(after));
    }

    [AvaloniaFact]
    public async Task Actual_CUI_chooser_and_move_keep_other_drafts_and_refuse_copied_or_previous_edit_destinations()
    {
        var vector = Shape("Move me"); var other = Shape("Another shape"); var seed = new PictureEditorSession(PictureDocument.Create(16, 16));
        using (seed.AddSharedVector(vector)) { } using (seed.MoveVector(Vector(seed, vector.ObjectId), new(1, 1, 4, 4))) { }
        using (seed.AddSharedVector(other)) { } using (seed.MoveVector(Vector(seed, other.ObjectId), new(10, 10, 2, 2))) { }
        using (seed.CreateLayer("Hidden destination")) { } var hidden = Layer(seed, "Hidden destination"); using (seed.SetLayerVisibility(Destination(seed, hidden), false)) { }
        var path = PathFor("native"); await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Layer transfer original initialization");
            var owner = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path); Install(window, owner); window.Show(); window.UpdateLayout();
            await Row(window, "Shape: Move me"); await Row(window, "Path 1"); var chooser = Find<ComboBox>(window, "VectorDestinationLayerBox");
            var destination = Assert.Single(chooser.Items.Cast<object>(), item => item.ToString() == "Hidden destination · hidden");
            chooser.SelectedItem = destination; Assert.True(Find<Button>(window, "TransferVectorLayerButton").IsEnabled);
            var cloned = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(destination, null)!;
            chooser.SelectedItem = cloned; Assert.False(Find<Button>(window, "TransferVectorLayerButton").IsEnabled);
            chooser.SelectedItem = destination; var before = owner.Document.Serialize();
            Assert.True(Find<TextBox>(window, "VectorStrokeColorBox").IsEnabled);
            Find<NumericUpDown>(window, "VectorRotationBox").Value = 35; Find<TextBox>(window, "VectorStrokeColorBox").Text = "#123456";
            await Click(window, "ZoomInButton"); Assert.Same(destination, chooser.SelectedItem); Assert.Equal(before, owner.Document.Serialize());
            await Row(window, "Shape: Another shape"); Assert.False(Find<Button>(window, "TransferVectorLayerButton").IsEnabled); Assert.Same(destination, chooser.SelectedItem);
            await Row(window, "Shape: Move me"); Assert.True(Find<Button>(window, "TransferVectorLayerButton").IsEnabled);
            var otherBefore = JsonSerializer.Serialize(Item(owner, other.ObjectId));
            await Click(window, "TransferVectorLayerButton"); Assert.Equal(hidden, Item(owner, vector.ObjectId).LayerId); Assert.Equal(otherBefore, JsonSerializer.Serialize(Item(owner, other.ObjectId)));
            Assert.Equal(35m, Find<NumericUpDown>(window, "VectorRotationBox").Value); Assert.Equal("#123456", Find<TextBox>(window, "VectorStrokeColorBox").Text);
            Assert.False(Find<Button>(window, "TransferVectorLayerButton").IsEnabled);
            await Click(window, "ResetVectorLayerDestinationButton");
            var current = Assert.Single(chooser.Items.Cast<object>(), item => item.ToString() == "Hidden destination · hidden"); chooser.SelectedItem = current;
            Assert.False(Find<Button>(window, "TransferVectorLayerButton").IsEnabled); // Already in that layer.
            await Click(window, "UndoButton"); Assert.False(Find<Button>(window, "TransferVectorLayerButton").IsEnabled); Assert.Same(current, chooser.SelectedItem);
            await Click(window, "ResetVectorLayerDestinationButton"); chooser.SelectedItem = Assert.Single(chooser.Items.Cast<object>(), item => item.ToString() == "Hidden destination · hidden");
            Assert.True(Find<Button>(window, "TransferVectorLayerButton").IsEnabled); await Click(window, "TransferVectorLayerButton");
            var expected = Rgba(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source)); await Click(window, "SaveButton");
            var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            using (var rendered = PictureCropService.Render(cold.Document)) Assert.Equal(expected, Rgba(rendered)); Assert.Equal(hidden, Item(cold, vector.ObjectId).LayerId);
            Assert.Equal(Item(owner, vector.ObjectId).SharedPayload!.Value.GetRawText(), Item(cold, vector.ObjectId).SharedPayload!.Value.GetRawText());
        }
        catch (Exception failure) { firstFailure = failure; RetainedFailures.Add((window, window.OriginalCommand, failure)); throw; }
        finally { await RetireOriginalWindowAsync(window, firstFailure); }
    }
    private static void Install(MainWindow window, PictureEditorSession owner) => typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [owner, PictureCropService.Render(owner.Document), null, null]);
    private static T Find<T>(MainWindow window, string id) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == id);
    private static Task Row(MainWindow window, string label) => PictureCuiActionFixture.ClickAsync(window, window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label)));
    private static Task Click(MainWindow window, string id) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, id));
    private static async Task RetireOriginalWindowAsync(MainWindow window, Exception? firstFailure)
    {
        if (firstFailure is null)
        {
            window.Close(); var close = window.OriginalClose; Assert.NotNull(close);
            RetainedOriginals.Add((window, close!));
            try { await close!.ObserveOriginalAsync(window, "Layer transfer original close"); Assert.True(close.IsCompletedSuccessfully); Assert.Same(close, window.OriginalClose); }
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
            try { await original.ObserveOriginalAsync(window, "Failed layer transfer child retirement"); }
            catch (Exception failure) { var cause = original.Exception ?? failure; RetainedFailures.Add((window, original, cause)); if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause); }
        }
        if (failures.Count > 1) throw new AggregateException("Layer transfer body and actual child retirement failures.", failures);
    }
}
