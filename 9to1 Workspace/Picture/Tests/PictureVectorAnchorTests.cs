using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Haven.Core;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureVectorAnchorTests
{
    private static readonly List<(object Owner, Task? Original, Exception Failure)> RetainedFailures = [];
    private static readonly List<object> RetainedOriginals = [];
    private static HomeProductivityObject Line()
    {
        var shape = new DocumentVectorShape { Name = "Curve", ViewBox = new() { Width = 100, Height = 100 }, Paths =
            [new() { Fill = new() { Kind = DocumentVectorFillKind.None }, Stroke = new() { Enabled = true, Color = "#FF0000", Width = 2,
                Cap = DocumentVectorLineCap.Butt }, Subpaths = [new() { Closed = false, Nodes =
                    [new() { X = 20, Y = 80 }, new() { X = 80, Y = 80 }] }] }] };
        var original = new HomeVectorShapeObjectHandler().Project(shape); var node = JsonNode.Parse(original.Content.GetRawText())!;
        node["FutureShape"] = "keep"; node["Paths"]![0]!["Subpaths"]![0]!["FutureSubpath"] = 89;
        node["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!["FutureNode"] = new JsonObject { ["retained"] = true };
        return original with { Content = JsonSerializer.SerializeToElement(node) };
    }
    private static HomeProductivityObject Read(PictureEditorSession owner, Guid vector) =>
        PictureCompositionAdapter.ReadVector(PictureCompositionAdapter.Read(owner.Document).Pages[0].Objects.Single(item => item.ObjectId == vector));
    private static DocumentVectorShape Shape(HomeProductivityObject vector) => HomeVectorShapeObjectHandler.ReadCanonical(vector.Content, vector.ObjectId);
    private static DocumentVectorNode Node(PictureEditorSession owner, Guid vector) => Shape(Read(owner, vector)).Paths[0].Subpaths[0].Nodes[1];
    private static PictureVectorNodeTarget Capture(PictureEditorSession owner, Guid vector, int index = 1)
    {
        var path = Shape(Read(owner, vector)).Paths[0]; var subpath = path.Subpaths[0];
        return owner.CaptureVectorNode(owner.CaptureComposition(vector, PictureCompositionTargetKind.Vector), path.Id, subpath.Id, subpath.Nodes[index].Id);
    }
    private static byte[] Rgba(Bitmap source)
    {
        using var converted = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = converted.Lock(); source.CopyPixels(buffer); var bytes = new byte[source.PixelSize.Width * source.PixelSize.Height * 4];
        for (var y = 0; y < source.PixelSize.Height; y++) Marshal.Copy(IntPtr.Add(buffer.Address, y * buffer.RowBytes), bytes, y * source.PixelSize.Width * 4, source.PixelSize.Width * 4);
        return bytes;
    }
    private static byte[] Pixel(Bitmap bitmap, int x, int y) => Rgba(bitmap).AsSpan((y * bitmap.PixelSize.Width + x) * 4, 4).ToArray();
    private static byte[] StraightGolden()
    {
        var result = new byte[100 * 100 * 4];
        for (var y = 79; y < 81; y++) for (var x = 20; x < 80; x++) { var i = (y * 100 + x) * 4; result[i] = result[i + 3] = 255; }
        return result;
    }
    private static string NewPath(string label) => Path.Combine(Directory.CreateTempSubdirectory("picture-anchor-" + label + "-").FullName, "editable.picture.json");

    [AvaloniaFact]
    public async Task Actual_new_anchor_changes_geometry_with_fresh_identity_preserved_source_full_pixels_and_cold_undo_history()
    {
        var sourcePath = Path.Combine(Directory.CreateTempSubdirectory("picture-anchor-source-").FullName, "original.png");
        using (var source = PictureCropService.Render(PictureDocument.Create(100, 100))) using (var file = File.Create(sourcePath)) source.Save(file);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
        var original = Line(); var owner = new PictureEditorSession(new PictureCropService().OpenSource(sourcePath));
        using (owner.AddSharedVector(original)) { } using (owner.MoveVector(owner.CaptureComposition(original.ObjectId, PictureCompositionTargetKind.Vector), new(0, 0, 100, 100))) { }
        var before = Read(owner, original.ObjectId); var nodes = Shape(before).Paths[0].Subpaths[0].Nodes; var first = nodes[0].Id; var last = nodes[1].Id;
        var sourceRevision = owner.Document.SourceRevision; var documentId = owner.Document.DocumentId;
        using (var rendered = PictureCropService.Render(owner.Document)) Assert.Equal(StraightGolden(), Rgba(rendered));
        PictureVectorNodeTarget added;
        using (var actual = owner.AddVectorAnchor(Capture(owner, original.ObjectId, 0), 50, 80, out added)) Assert.Equal(StraightGolden(), Rgba(actual));
        Assert.NotEqual(first, added.NodeId); Assert.NotEqual(last, added.NodeId);
        nodes = Shape(Read(owner, original.ObjectId)).Paths[0].Subpaths[0].Nodes;
        Assert.Equal(new[] { first, added.NodeId, last }, nodes.Select(node => node.Id)); Assert.Equal(50, nodes[1].X); Assert.Equal(80, nodes[1].Y);
        Assert.Equal(DocumentVectorSegmentKind.Line, nodes[1].IncomingSegment); Assert.Null(nodes[1].Control1); Assert.Null(nodes[1].Control2);
        var afterAdd = JsonNode.Parse(Read(owner, original.ObjectId).Content.GetRawText())!; afterAdd["Paths"]![0]!["Subpaths"]![0]!["Nodes"]!.AsArray().RemoveAt(1);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before.Content.GetRawText()), afterAdd));
        using (var actual = owner.MoveVectorNode(added, 50, 20)) { Assert.Equal(new byte[4], Pixel(actual, 50, 80)); Assert.True(Pixel(actual, 50, 21)[3] > 0); }
        byte[] bent; using (var actual = PictureCropService.Render(owner.Document)) bent = Rgba(actual);
        using (var originalAgain = owner.DeleteVectorAnchor(Capture(owner, original.ObjectId, 1))) Assert.Equal(StraightGolden(), Rgba(originalAgain));
        Assert.Equal(before.Content.GetRawText(), Read(owner, original.ObjectId).Content.GetRawText());
        using (var recovered = owner.Undo()) Assert.Equal(bent, Rgba(recovered)); Assert.Equal(added.NodeId, Shape(Read(owner, original.ObjectId)).Paths[0].Subpaths[0].Nodes[1].Id);
        using (var redone = owner.Redo()) Assert.Equal(StraightGolden(), Rgba(redone));
        var path = NewPath("cold"); await owner.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(documentId, cold.Document.DocumentId); Assert.Equal(sourceRevision, cold.Document.SourceRevision);
        using (var recovered = cold.Undo()) Assert.Equal(bent, Rgba(recovered));
        Assert.Equal(added.NodeId, Shape(Read(cold, original.ObjectId)).Paths[0].Subpaths[0].Nodes[1].Id);
        using (var redone = cold.Redo()) Assert.Equal(StraightGolden(), Rgba(redone));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Nonfinite_stale_foreign_locked_minimum_and_ambiguous_subpath_requests_keep_exact_original_state()
    {
        var original = Line(); var owner = new PictureEditorSession(PictureDocument.Create(100, 100)); using (owner.AddSharedVector(original)) { }
        var current = Capture(owner, original.ObjectId, 0); var foreign = Capture(new PictureEditorSession(owner.Document), original.ObjectId, 0);
        var before = owner.Document.Serialize(); var operation = owner.LastOperation;
        Assert.Equal("A path must keep at least two anchors.", Assert.Throws<InvalidOperationException>(() => owner.DeleteVectorAnchor(current)).Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.AddVectorAnchor(current, double.NaN, 20, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.AddVectorAnchor(current, 20, double.PositiveInfinity, out _));
        Assert.Throws<InvalidOperationException>(() => owner.AddVectorAnchor(foreign, 40, 40, out _));
        Assert.Equal(before, owner.Document.Serialize()); Assert.Same(operation, owner.LastOperation);
        using (owner.AddVectorAnchor(current, 50, 80, out _)) { } before = owner.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => owner.AddVectorAnchor(current, 40, 40, out _));
        Assert.Throws<InvalidOperationException>(() => owner.DeleteVectorAnchor(current)); Assert.Equal(before, owner.Document.Serialize());
        var valid = Capture(owner, original.ObjectId, 0);
        var ambiguous = JsonNode.Parse(Read(owner, original.ObjectId).Content.GetRawText())!; var path = ambiguous["Paths"]![0]!.DeepClone(); path["Id"] = Guid.NewGuid();
        foreach (var node in path["Subpaths"]![0]!["Nodes"]!.AsArray()) node!["Id"] = Guid.NewGuid();
        ambiguous["Paths"]!.AsArray().Add(path);
        var duplicated = original with { Content = JsonSerializer.SerializeToElement(ambiguous) };
        var method = typeof(PictureEditorSession).Assembly.GetType("HavenOS.Images.PictureVectorAnchorEditor", true)!.GetMethod("Add",
            BindingFlags.Static | BindingFlags.NonPublic, binder: null, types: [typeof(HomeProductivityObject), typeof(PictureVectorNodeTarget), typeof(double), typeof(double), typeof(Guid).MakeByRefType()], modifiers: null)!;
        Assert.Contains("subpath ID", Assert.Throws<NotSupportedException>(() => InvokeOriginal(() => method.Invoke(null, [duplicated, valid, 40d, 40d, Guid.Empty]))).Message);
        Assert.Equal(before, owner.Document.Serialize());
        var page = PictureCompositionAdapter.Read(owner.Document).Pages[0]; var layer = page.Objects.Single(item => item.ObjectId == original.ObjectId).LayerId;
        using (owner.SetLayerLocked(owner.CaptureComposition(layer, PictureCompositionTargetKind.Layer), true)) { } before = owner.Document.Serialize();
        Assert.Contains("PermissionDenied", Assert.Throws<InvalidOperationException>(() => owner.AddVectorAnchor(Capture(owner, original.ObjectId, 0), 40, 40, out _)).Message);
        Assert.Contains("PermissionDenied", Assert.Throws<InvalidOperationException>(() => owner.DeleteVectorAnchor(Capture(owner, original.ObjectId, 0))).Message);
        Assert.Equal(before, owner.Document.Serialize());
    }

    [AvaloniaFact]
    public async Task Deleting_first_anchor_preserves_remaining_original_controls_full_precision_unknowns_and_saved_payload()
    {
        var original = Line(); var retained = JsonNode.Parse(original.Content.GetRawText())!; var list = retained["Paths"]![0]!["Subpaths"]![0]!["Nodes"]!.AsArray();
        var middle = list[0]!.DeepClone(); middle["Id"] = Guid.NewGuid(); middle["X"] = 50.12345678901234; middle["Y"] = 20.98765432109876;
        middle["IncomingSegment"] = (int)DocumentVectorSegmentKind.Cubic;
        middle["Control1"] = new JsonObject { ["X"] = 30.12345678901234, ["Y"] = 50.76543210987654, ["FutureControl"] = "keep" };
        middle["Control2"] = new JsonObject { ["X"] = 45.98765432109876, ["Y"] = 30.12345678901234 };
        middle["FutureNode"] = new JsonObject { ["exact"] = true }; list.Insert(1, middle);
        original = original with { Content = JsonSerializer.SerializeToElement(retained) };
        var owner = new PictureEditorSession(PictureDocument.Create(100, 100)); using (owner.AddSharedVector(original)) { }
        var first = Capture(owner, original.ObjectId, 0); using (owner.DeleteVectorAnchor(first)) { }
        list.RemoveAt(0); Assert.True(JsonNode.DeepEquals(retained, JsonNode.Parse(Read(owner, original.ObjectId).Content.GetRawText())));
        var actual = Shape(Read(owner, original.ObjectId)).Paths[0].Subpaths[0].Nodes[0];
        Assert.Equal(DocumentVectorSegmentKind.Cubic, actual.IncomingSegment); Assert.Equal(50.12345678901234, actual.X);
        Assert.Equal(new DocumentVectorPoint(30.12345678901234, 50.76543210987654), actual.Control1);
        var path = NewPath("unknown"); await owner.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(Read(owner, original.ObjectId).Content.GetRawText(), Read(cold, original.ObjectId).Content.GetRawText());
        using (var recovered = cold.Undo()) { } Assert.Equal(3, Shape(Read(cold, original.ObjectId)).Paths[0].Subpaths[0].Nodes.Count);
        using (var redone = cold.Redo()) { } Assert.True(JsonNode.DeepEquals(retained, JsonNode.Parse(Read(cold, original.ObjectId).Content.GetRawText())));
    }

    [AvaloniaFact]
    public async Task Actual_CUI_anchor_draft_stays_bound_add_delete_preserve_other_fields_and_explicit_new_selection_uses_current_receipt()
    {
        var vector = Line(); var seed = new PictureEditorSession(PictureDocument.Create(100, 100)); using (seed.AddSharedVector(vector)) { }
        using (seed.MoveVector(seed.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), new(0, 0, 100, 100))) { }
        var path = NewPath("native"); await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Anchor initialization");
            var owner = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            Install(window, owner); window.Show(); window.UpdateLayout(); await Row(window, "Shape: Curve"); await Row(window, "Path 1"); await NodeRow(window, "Node 1.1");
            var selectedId = Capture(owner, vector.ObjectId, 0).NodeId;
            var x = Find<NumericUpDown>(window, "NewVectorAnchorXBox"); var y = Find<NumericUpDown>(window, "NewVectorAnchorYBox");
            Assert.False(Find<Button>(window, "DeleteVectorAnchorButton").IsEnabled);
            x.Value = 50; y.Value = 20; await Click(window, "ZoomInButton"); Assert.Equal(50m, x.Value); Assert.Equal(20m, y.Value);
            await NodeRow(window, "Node 1.2"); Assert.False(Find<Button>(window, "AddVectorAnchorButton").IsEnabled);
            Assert.Equal(50m, x.Value); Assert.Equal(20m, y.Value); Assert.Contains("previous", Find<TextBlock>(window, "VectorAnchorNotice").Text);
            await Click(window, "ResetVectorAnchorButton"); Assert.Equal(80m, x.Value); Assert.Equal(80m, y.Value);
            await NodeRow(window, "Node 1.1"); x.Value = 50; y.Value = 20;
            var pending = Find<NumericUpDown>(window, "VectorNodeXBox"); pending.Value = 21; Find<NumericUpDown>(window, "VectorRotationBox").Value = 45;
            await Click(window, "AddVectorAnchorButton"); var nodes = Shape(Read(owner, vector.ObjectId)).Paths[0].Subpaths[0].Nodes;
            Assert.Equal(3, nodes.Count); Assert.Equal(50, nodes[1].X); Assert.Equal(20, nodes[1].Y); Assert.Equal(selectedId, nodes[0].Id);
            Assert.Equal(21m, pending.Value); Assert.Equal(45m, Find<NumericUpDown>(window, "VectorRotationBox").Value);
            Assert.Equal(20m, x.Value); Assert.Equal(80m, y.Value); Assert.False(Find<Button>(window, "AddVectorAnchorButton").IsEnabled);
            var sourceIssued = typeof(MainWindow).GetField("_lastAddedVectorAnchor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
            var sameIssued = Assert.IsType<PictureVectorNodeTarget>(sourceIssued);
            var select = typeof(MainWindow).GetMethod("SelectAddedVectorAnchor", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var constructor = typeof(PictureVectorNodeTarget).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, types: [typeof(PictureCompositionTarget), typeof(Guid), typeof(Guid), typeof(Guid)], modifiers: null)!;
            var copied = constructor.Invoke([sameIssued.Vector, sameIssued.PathId, sameIssued.SubpathId, sameIssued.NodeId]);
            Assert.Equal("Select the SAME current newly added anchor again.", Assert.Throws<InvalidOperationException>(() => InvokeOriginal(() => select.Invoke(window, [copied]))).Message);
            var foreignOwner = new PictureEditorSession(owner.Document); var foreignTarget = Capture(foreignOwner, vector.ObjectId, 1); var before = owner.Document.Serialize();
            Assert.Equal("Select the SAME current newly added anchor again.", Assert.Throws<InvalidOperationException>(() => InvokeOriginal(() => select.Invoke(window, [foreignTarget]))).Message);
            Assert.Equal(before, owner.Document.Serialize());
            await Click(window, "SelectAddedVectorAnchorButton"); Assert.Equal(50m, pending.Value); Assert.Equal(20m, Find<NumericUpDown>(window, "VectorNodeYBox").Value);
            Assert.Equal(45m, Find<NumericUpDown>(window, "VectorRotationBox").Value); Assert.True(Find<Button>(window, "DeleteVectorAnchorButton").IsEnabled);
            await Click(window, "DeleteVectorAnchorButton"); Assert.Equal(2, Shape(Read(owner, vector.ObjectId)).Paths[0].Subpaths[0].Nodes.Count);
            Assert.False(Find<Button>(window, "DeleteVectorAnchorButton").IsEnabled); Assert.False(Find<Button>(window, "SelectAddedVectorAnchorButton").IsEnabled);
            Assert.Equal(StraightGolden(), Rgba(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source)));
            await Click(window, "UndoButton"); Assert.Equal(3, Shape(Read(owner, vector.ObjectId)).Paths[0].Subpaths[0].Nodes.Count);
            Assert.Equal(45m, Find<NumericUpDown>(window, "VectorRotationBox").Value);
            var restored = Rgba(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source));
            await Click(window, "SaveButton"); var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            using (var rendered = PictureCropService.Render(cold.Document)) Assert.Equal(restored, Rgba(rendered));
            Assert.Equal(Read(owner, vector.ObjectId).Content.GetRawText(), Read(cold, vector.ObjectId).Content.GetRawText());
            Assert.Equal("Select the SAME current newly added anchor again.", Assert.Throws<InvalidOperationException>(() => InvokeOriginal(() => select.Invoke(window, [sourceIssued]))).Message);

        }
        catch (Exception failure) { firstFailure = failure; RetainedFailures.Add((window, window.OriginalCommand, failure)); throw; }
        finally { await RetireOriginalAsync(window, firstFailure); }
    }

    private static object? InvokeOriginal(Func<object?> invoke)
    {
        try { return invoke(); }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
    }
    private static void Install(MainWindow window, PictureEditorSession owner) => typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [owner, PictureCropService.Render(owner.Document), null, null]);
    private static T Find<T>(MainWindow window, string id) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == id);
    private static Task Row(MainWindow window, string label) => PictureCuiActionFixture.ClickAsync(window, window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label)));
    private static Task NodeRow(MainWindow window, string prefix) => PictureCuiActionFixture.ClickAsync(window,
        window.GetLogicalDescendants().OfType<Button>().Single(button => button.Content is string text && text.StartsWith(prefix, StringComparison.Ordinal)));
    private static Task Click(MainWindow window, string id) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, id));
    private static async Task RetireOriginalAsync(MainWindow window, Exception? firstFailure)
    {
        if (firstFailure is null)
        {
            window.Close(); var close = window.OriginalClose; Assert.NotNull(close); RetainedOriginals.Add((window, close!));
            try { await close!.ObserveOriginalAsync(window, "Anchor original close"); Assert.True(close.IsCompletedSuccessfully); Assert.Same(close, window.OriginalClose); }
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
            try { await original.ObserveOriginalAsync(window, "Failed anchor original child"); }
            catch (Exception failure) { var actual = original.Exception ?? failure; RetainedFailures.Add((window, original, actual)); if (!failures.Any(value => ReferenceEquals(value, actual))) failures.Add(actual); }
        }
        if (failures.Count > 1) throw new AggregateException("Anchor body and original child retirement failures.", failures);
    }
}
