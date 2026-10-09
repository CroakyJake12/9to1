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

public sealed class PictureVectorSegmentTests
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
        node["FutureShape"] = "keep"; node["Paths"]![0]!["Subpaths"]![0]!["FutureSubpath"] = 88;
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
    private static string NewPath(string label) => Path.Combine(Directory.CreateTempSubdirectory("picture-segment-" + label + "-").FullName, "editable.picture.json");

    [AvaloniaFact]
    public async Task Actual_segment_creation_uses_shared_controls_and_changes_pixels_with_same_ids_source_undo_and_cold_history()
    {
        var sourcePath = Path.Combine(Directory.CreateTempSubdirectory("picture-segment-source-").FullName, "original.png");
        using (var source = PictureCropService.Render(PictureDocument.Create(100, 100))) using (var bytes = File.Create(sourcePath)) source.Save(bytes);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken); var vector = Line();
        var owner = new PictureEditorSession(new PictureCropService().OpenSource(sourcePath)); using (owner.AddSharedVector(vector)) { }
        using (owner.MoveVector(owner.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), new(0, 0, 100, 100))) { }
        var original = Read(owner, vector.ObjectId); var nodeId = Node(owner, vector.ObjectId).Id;
        using (var rendered = PictureCropService.Render(owner.Document)) Assert.Equal(StraightGolden(), Rgba(rendered));
        using (var quadratic = owner.ConvertVectorSegment(Capture(owner, vector.ObjectId), DocumentVectorSegmentKind.Quadratic)) Assert.Equal(StraightGolden(), Rgba(quadratic));
        Assert.Equal(new DocumentVectorPoint(50, 80), Node(owner, vector.ObjectId).Control1);
        using (var cubic = owner.ConvertVectorSegment(Capture(owner, vector.ObjectId), DocumentVectorSegmentKind.Cubic)) Assert.Equal(StraightGolden(), Rgba(cubic));
        Assert.Equal(new DocumentVectorPoint(50, 80), Node(owner, vector.ObjectId).Control1); Assert.Equal(new DocumentVectorPoint(60, 80), Node(owner, vector.ObjectId).Control2);
        byte[] curved;
        using (var actual = owner.MoveVectorControlPoint(Capture(owner, vector.ObjectId), 1, 40, 20))
        { curved = Rgba(actual); Assert.Equal(new byte[4], Pixel(actual, 50, 80)); Assert.True(Pixel(actual, 50, 57)[3] > 0); }
        using (var line = owner.ConvertVectorSegment(Capture(owner, vector.ObjectId), DocumentVectorSegmentKind.Line)) Assert.Equal(StraightGolden(), Rgba(line));
        Assert.Equal(new DocumentVectorPoint(40, 20), Node(owner, vector.ObjectId).Control1); Assert.Equal(new DocumentVectorPoint(60, 80), Node(owner, vector.ObjectId).Control2);
        using (var restored = owner.Undo()) Assert.Equal(curved, Rgba(restored)); using (var line = owner.Redo()) Assert.Equal(StraightGolden(), Rgba(line));
        var path = NewPath("cold"); await owner.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(owner.Document.DocumentId, cold.Document.DocumentId); Assert.Equal(nodeId, Node(cold, vector.ObjectId).Id);
        using (var undone = cold.Undo()) Assert.Equal(curved, Rgba(undone)); using (var redone = cold.Redo()) Assert.Equal(StraightGolden(), Rgba(redone));
        using (var restored = cold.ConvertVectorSegment(Capture(cold, vector.ObjectId), DocumentVectorSegmentKind.Cubic)) Assert.Equal(curved, Rgba(restored));
        var retained = JsonNode.Parse(Read(cold, vector.ObjectId).Content.GetRawText())!; var expected = JsonNode.Parse(original.Content.GetRawText())!;
        var oldNode = expected["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!;
        oldNode["IncomingSegment"] = (int)DocumentVectorSegmentKind.Cubic; oldNode["Control1"] = JsonSerializer.SerializeToNode(new DocumentVectorPoint(40, 20));
        oldNode["Control2"] = JsonSerializer.SerializeToNode(new DocumentVectorPoint(60, 80)); Assert.True(JsonNode.DeepEquals(expected, retained));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Current_no_op_first_node_stale_foreign_locked_and_invalid_kind_requests_keep_exact_source_history()
    {
        var owner = new PictureEditorSession(PictureDocument.Create(100, 100)); var vector = Line(); using (owner.AddSharedVector(vector)) { }
        var before = owner.Document.Serialize(); var operation = owner.LastOperation;
        using (owner.ConvertVectorSegment(Capture(owner, vector.ObjectId), DocumentVectorSegmentKind.Line)) { }
        Assert.Equal(before, owner.Document.Serialize()); Assert.Same(operation, owner.LastOperation);
        var stale = Capture(owner, vector.ObjectId); var foreign = Capture(new PictureEditorSession(owner.Document), vector.ObjectId);
        Assert.Throws<InvalidOperationException>(() => owner.ConvertVectorSegment(Capture(owner, vector.ObjectId, 0), DocumentVectorSegmentKind.Cubic));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.ConvertVectorSegment(stale, (DocumentVectorSegmentKind)999)); Assert.Equal(before, owner.Document.Serialize());
        using (owner.ConvertVectorSegment(stale, DocumentVectorSegmentKind.Quadratic)) { } before = owner.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => owner.ConvertVectorSegment(stale, DocumentVectorSegmentKind.Cubic));
        Assert.Throws<InvalidOperationException>(() => owner.ConvertVectorSegment(foreign, DocumentVectorSegmentKind.Cubic)); Assert.Equal(before, owner.Document.Serialize());
        var page = PictureCompositionAdapter.Read(owner.Document).Pages[0]; var layer = page.Objects.Single(item => item.ObjectId == vector.ObjectId).LayerId;
        using (owner.SetLayerLocked(owner.CaptureComposition(layer, PictureCompositionTargetKind.Layer), true)) { } before = owner.Document.Serialize();
        Assert.Contains("PermissionDenied", Assert.Throws<InvalidOperationException>(() => owner.ConvertVectorSegment(Capture(owner, vector.ObjectId), DocumentVectorSegmentKind.Cubic)).Message);
        Assert.Equal(before, owner.Document.Serialize());
    }

    [AvaloniaFact]
    public void Exact_retained_control_properties_survive_line_quadratic_cubic_conversion_and_overflow_refuses_before_publication()
    {
        var original = Line(); var content = JsonNode.Parse(original.Content.GetRawText())!;
        var node = content["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!;
        node["IncomingSegment"] = (int)DocumentVectorSegmentKind.Cubic;
        node["Control1"] = new JsonObject { ["X"] = 40.12345678901234, ["Y"] = 20.98765432109876, ["FutureControl"] = "keep1" };
        node["Control2"] = new JsonObject { ["X"] = 60.76543210987654, ["Y"] = 80.12345678901234, ["FutureControl"] = "keep2" };
        original = original with { Content = JsonSerializer.SerializeToElement(content) }; var owner = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (owner.AddSharedVector(original)) { }
        foreach (var kind in new[] { DocumentVectorSegmentKind.Line, DocumentVectorSegmentKind.Quadratic, DocumentVectorSegmentKind.Cubic })
        {
            using (owner.ConvertVectorSegment(Capture(owner, original.ObjectId), kind)) { }
            node["IncomingSegment"] = (int)kind;
            Assert.True(JsonNode.DeepEquals(content, JsonNode.Parse(Read(owner, original.ObjectId).Content.GetRawText())));
        }
        // Actual original adapter refuses arithmetic overflow rather than the
        // shared normalizer silently replacing an infinite new control with0.
        var target = Capture(owner, original.ObjectId); var huge = JsonNode.Parse(Line().Content.GetRawText())!;
        huge["Id"] = original.ObjectId;
        huge["Paths"]![0]!["Id"] = target.PathId; huge["Paths"]![0]!["Subpaths"]![0]!["Id"] = target.SubpathId;
        huge["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!["Id"] = target.NodeId;
        huge["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![0]!["X"] = -double.MaxValue;
        huge["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!["X"] = double.MaxValue;
        var unsupported = original with { Content = JsonSerializer.SerializeToElement(huge) };
        // The arithmetic refusal must exercise a valid SAME canonical graph,
        // rather than failing import identity/schema before reaching the edit.
        var hugeShape = Shape(unsupported);
        Assert.Equal(original.ObjectId, hugeShape.Id); Assert.Equal(DocumentVectorShape.CurrentSchemaVersion, hugeShape.SchemaVersion);
        var hugePath = Assert.Single(hugeShape.Paths); Assert.Equal(target.PathId, hugePath.Id);
        var hugeSubpath = Assert.Single(hugePath.Subpaths); Assert.Equal(target.SubpathId, hugeSubpath.Id);
        Assert.Equal(-double.MaxValue, hugeSubpath.Nodes[0].X); Assert.Equal(double.MaxValue, hugeSubpath.Nodes[1].X);
        Assert.Equal(target.NodeId, hugeSubpath.Nodes[1].Id);
        var method = typeof(PictureEditorSession).Assembly.GetType("HavenOS.Images.PictureVectorSegmentEditor", true)!.GetMethod("Convert",
            BindingFlags.Static | BindingFlags.NonPublic, binder: null,
            types: [typeof(HomeProductivityObject), typeof(PictureVectorNodeTarget), typeof(DocumentVectorSegmentKind)], modifiers: null)!;
        var before = owner.Document.Serialize();
        Assert.Equal("The retained endpoints cannot produce finite curve controls.",
            Assert.Throws<NotSupportedException>(() => InvokeOriginal(() => method.Invoke(null, [unsupported, target, DocumentVectorSegmentKind.Quadratic]))).Message);
        Assert.Equal(before, owner.Document.Serialize());
        var ambiguous = JsonNode.Parse(Read(owner, original.ObjectId).Content.GetRawText())!;
        var paths = ambiguous["Paths"]![0]!["Subpaths"]!.AsArray(); var copy = paths[0]!.DeepClone(); copy["Id"] = Guid.NewGuid(); paths.Add(copy);
        var repeated = original with { Content = JsonSerializer.SerializeToElement(ambiguous) };
        var repeatedShape = Shape(repeated);
        Assert.Equal(2, repeatedShape.Paths[0].Subpaths.Count);
        Assert.NotEqual(repeatedShape.Paths[0].Subpaths[0].Id, repeatedShape.Paths[0].Subpaths[1].Id);
        Assert.Equal(target.NodeId, repeatedShape.Paths[0].Subpaths[0].Nodes[1].Id);
        Assert.Equal(target.NodeId, repeatedShape.Paths[0].Subpaths[1].Nodes[1].Id);
        Assert.Equal("This node ID is reused in another subpath. Its original identity is preserved; node editing is unavailable.",
            Assert.Throws<NotSupportedException>(() => InvokeOriginal(() => method.Invoke(null, [repeated, target, DocumentVectorSegmentKind.Line]))).Message);
        Assert.Equal(before, owner.Document.Serialize());
    }
    private static object? InvokeOriginal(Func<object?> invoke)
    {
        try { return invoke(); }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
    }

    [AvaloniaFact]
    public async Task Genuine_CUI_segment_conversion_creates_actual_controls_keeps_pending_fields_and_saves_same_cold_pixels()
    {
        var vector = Line(); var seed = new PictureEditorSession(PictureDocument.Create(100, 100)); using (seed.AddSharedVector(vector)) { }
        using (seed.MoveVector(seed.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), new(0, 0, 100, 100))) { }
        var path = NewPath("native"); await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Segment initialization");
            var owner = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            Install(window, owner); window.Show(); window.UpdateLayout(); await Row(window, "Shape: Curve"); await Row(window, "Path 1"); await NodeRow(window, "Node 1.1");
            foreach (var id in new[] { "VectorSegmentLineButton", "VectorSegmentQuadraticButton", "VectorSegmentCubicButton" }) Assert.False(Find<Button>(window, id).IsEnabled);
            await NodeRow(window, "Node 1.2"); var pendingX = Find<NumericUpDown>(window, "VectorNodeXBox"); var controlX = Find<NumericUpDown>(window, "VectorControl1XBox");
            pendingX.Value = 81; controlX.Value = 33; Find<NumericUpDown>(window, "VectorRotationBox").Value = 45;
            Assert.False(Find<Button>(window, "VectorSegmentLineButton").IsEnabled); await Click(window, "VectorSegmentQuadraticButton");
            Assert.Equal(DocumentVectorSegmentKind.Quadratic, Node(owner, vector.ObjectId).IncomingSegment); Assert.Equal(new DocumentVectorPoint(50, 80), Node(owner, vector.ObjectId).Control1);
            Assert.Equal(81m, pendingX.Value); Assert.Equal(33m, controlX.Value); Assert.Equal(80m, Find<NumericUpDown>(window, "VectorControl1YBox").Value);
            Assert.True(Find<Button>(window, "ApplyVectorControl1Button").IsEnabled); Assert.False(Find<Button>(window, "ApplyVectorControl2Button").IsEnabled);
            Find<NumericUpDown>(window, "VectorControl1YBox").Value = 20; await Click(window, "ApplyVectorControl1Button");
            Assert.Equal(new DocumentVectorPoint(33, 20), Node(owner, vector.ObjectId).Control1); Assert.Equal(80, Node(owner, vector.ObjectId).X);
            await Click(window, "VectorSegmentCubicButton"); Assert.Equal(new DocumentVectorPoint(60, 80), Node(owner, vector.ObjectId).Control2);
            Assert.Equal(60m, Find<NumericUpDown>(window, "VectorControl2XBox").Value); Assert.True(Find<Button>(window, "ApplyVectorControl2Button").IsEnabled);
            var actualCurve = Rgba(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source));
            await Click(window, "VectorSegmentLineButton"); Assert.False(Find<Button>(window, "ApplyVectorControl1Button").IsEnabled);
            Assert.Equal(StraightGolden(), Rgba(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source)));
            await Click(window, "UndoButton"); Assert.Equal(actualCurve, Rgba(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source)));
            Assert.Equal(81m, pendingX.Value); Assert.Equal(45m, Find<NumericUpDown>(window, "VectorRotationBox").Value);
            await Click(window, "SaveButton"); var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            using (var rendered = PictureCropService.Render(cold.Document)) Assert.Equal(actualCurve, Rgba(rendered));
            Assert.Equal(Read(owner, vector.ObjectId).Content.GetRawText(), Read(cold, vector.ObjectId).Content.GetRawText());
        }
        catch (Exception failure) { firstFailure = failure; RetainedFailures.Add((window, window.OriginalCommand, failure)); throw; }
        finally { await RetireOriginalAsync(window, firstFailure); }
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
            try { await close!.ObserveOriginalAsync(window, "Segment original close"); Assert.True(close.IsCompletedSuccessfully); Assert.Same(close, window.OriginalClose); }
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
            try { await original.ObserveOriginalAsync(window, "Failed segment original child"); }
            catch (Exception failure) { var actual = original.Exception ?? failure; RetainedFailures.Add((window, original, actual)); if (!failures.Any(value => ReferenceEquals(value, actual))) failures.Add(actual); }
        }
        if (failures.Count > 1) throw new AggregateException("Segment body and original child retirement failures.", failures);
    }
}
