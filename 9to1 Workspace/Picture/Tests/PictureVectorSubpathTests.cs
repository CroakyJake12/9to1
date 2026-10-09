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

public sealed class PictureVectorSubpathTests
{
    private static readonly List<(object Owner, Task? Original, Exception Failure)> RetainedFailures = [];
    private static readonly List<object> RetainedOriginals = [];
    private static HomeProductivityObject OpenBox()
    {
        var shape = new DocumentVectorShape { Name = "Open box", ViewBox = new() { Width = 100, Height = 100 }, Paths =
            [new() { Fill = new() { Kind = DocumentVectorFillKind.None }, Stroke = new() { Enabled = true, Color = "#FF0000", Width = 2,
                Cap = DocumentVectorLineCap.Butt, Join = DocumentVectorLineJoin.Miter }, Subpaths = [new() { Closed = false, Nodes =
                    [new() { X = 20, Y = 20 }, new() { X = 80, Y = 20 }, new() { X = 80, Y = 80 }, new() { X = 20, Y = 80 }] }] }] };
        var original = new HomeVectorShapeObjectHandler().Project(shape); var node = JsonNode.Parse(original.Content.GetRawText())!;
        node["FutureShape"] = "keep"; node["Paths"]![0]!["Subpaths"]![0]!["FutureSubpath"] = 92;
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
    private static byte[] ClosedBoxGolden()
    {
        var result = new byte[100 * 100 * 4];
        for (var y = 19; y < 81; y++) for (var x = 19; x < 81; x++)
            if (x < 21 || x >= 79 || y < 21 || y >= 79) { var i = (y * 100 + x) * 4; result[i] = result[i + 3] = 255; }
        return result;
    }
    private static bool Closed(PictureEditorSession owner, Guid vector) => Shape(Read(owner, vector)).Paths[0].Subpaths[0].Closed;
    private static string NewPath(string label) => Path.Combine(Directory.CreateTempSubdirectory("picture-subpath-" + label + "-").FullName, "editable.picture.json");

    [AvaloniaFact]
    public async Task Actual_closed_stroke_keeps_same_graph_source_full_pixels_undo_and_cold_history()
    {
        var sourcePath = Path.Combine(Directory.CreateTempSubdirectory("picture-subpath-source-").FullName, "original.png");
        using (var source = PictureCropService.Render(PictureDocument.Create(100, 100))) using (var file = File.Create(sourcePath)) source.Save(file);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken); var vector = OpenBox();
        var owner = new PictureEditorSession(new PictureCropService().OpenSource(sourcePath)); using (owner.AddSharedVector(vector)) { }
        using (owner.MoveVector(owner.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), new(0, 0, 100, 100))) { }
        var original = Read(owner, vector.ObjectId); var id = owner.Document.DocumentId; var revision = owner.Document.SourceRevision;
        byte[] opened; using (var actual = PictureCropService.Render(owner.Document)) { opened = Rgba(actual); Assert.Equal(new byte[4], Pixel(actual, 20, 50)); }
        using (var actual = owner.SetVectorSubpathClosed(Capture(owner, vector.ObjectId, 0), true)) Assert.Equal(ClosedBoxGolden(), Rgba(actual));
        Assert.True(Closed(owner, vector.ObjectId)); var expected = JsonNode.Parse(original.Content.GetRawText())!; expected["Paths"]![0]!["Subpaths"]![0]!["Closed"] = true;
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(Read(owner, vector.ObjectId).Content.GetRawText())));
        using (var actual = owner.Undo()) Assert.Equal(opened, Rgba(actual)); using (var actual = owner.Redo()) Assert.Equal(ClosedBoxGolden(), Rgba(actual));
        var path = NewPath("cold"); await owner.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(id, cold.Document.DocumentId); Assert.Equal(revision, cold.Document.SourceRevision);
        using (var actual = cold.Undo()) Assert.Equal(opened, Rgba(actual)); Assert.False(Closed(cold, vector.ObjectId));
        using (var actual = cold.Redo()) Assert.Equal(ClosedBoxGolden(), Rgba(actual)); Assert.True(Closed(cold, vector.ObjectId));
        Assert.Equal(Read(owner, vector.ObjectId).Content.GetRawText(), Read(cold, vector.ObjectId).Content.GetRawText());
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Original_controls_precision_unknowns_open_fill_semantics_and_no_op_current_lock_guards_remain_exact()
    {
        var vector = OpenBox(); var content = JsonNode.Parse(vector.Content.GetRawText())!;
        content["Paths"]![0]!["Stroke"]!["Enabled"] = false; content["Paths"]![0]!["Fill"]!["Kind"] = (int)DocumentVectorFillKind.Solid;
        content["Paths"]![0]!["Fill"]!["Color"] = "#00FF00";
        var node = content["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![0]!;
        node["IncomingSegment"] = (int)DocumentVectorSegmentKind.Cubic;
        node["Control1"] = new JsonObject { ["X"] = 30.12345678901234, ["Y"] = 50.98765432109876, ["FutureControl"] = "keep" };
        node["Control2"] = new JsonObject { ["X"] = 10.98765432109876, ["Y"] = 20.12345678901234 };
        vector = vector with { Content = JsonSerializer.SerializeToElement(content) }; var owner = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (owner.AddSharedVector(vector)) { } using (owner.MoveVector(owner.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), new(0, 0, 100, 100))) { }
        var before = owner.Document.Serialize(); var metadata = owner.LastOperation; var target = Capture(owner, vector.ObjectId, 0);
        using (owner.SetVectorSubpathClosed(target, false)) { } Assert.Equal(before, owner.Document.Serialize()); Assert.Same(metadata, owner.LastOperation);
        byte[] filled; using (var actual = PictureCropService.Render(owner.Document)) { filled = Rgba(actual); Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(actual, 50, 50)); }
        var foreign = Capture(new PictureEditorSession(owner.Document), vector.ObjectId, 0);
        using (var actual = owner.SetVectorSubpathClosed(target, true)) Assert.Equal(filled, Rgba(actual));
        content["Paths"]![0]!["Subpaths"]![0]!["Closed"] = true; Assert.True(JsonNode.DeepEquals(content, JsonNode.Parse(Read(owner, vector.ObjectId).Content.GetRawText())));
        before = owner.Document.Serialize(); Assert.Throws<InvalidOperationException>(() => owner.SetVectorSubpathClosed(target, false));
        Assert.Throws<InvalidOperationException>(() => owner.SetVectorSubpathClosed(foreign, false)); Assert.Equal(before, owner.Document.Serialize());
        var layer = PictureCompositionAdapter.Read(owner.Document).Pages[0].Objects.Single(item => item.ObjectId == vector.ObjectId).LayerId;
        using (owner.SetLayerLocked(owner.CaptureComposition(layer, PictureCompositionTargetKind.Layer), true)) { } before = owner.Document.Serialize();
        Assert.Contains("PermissionDenied", Assert.Throws<InvalidOperationException>(() => owner.SetVectorSubpathClosed(Capture(owner, vector.ObjectId, 0), true)).Message);
        Assert.Contains("PermissionDenied", Assert.Throws<InvalidOperationException>(() => owner.SetVectorSubpathClosed(Capture(owner, vector.ObjectId, 0), false)).Message);
        Assert.Equal(before, owner.Document.Serialize());
    }

    [AvaloniaFact]
    public void Reused_original_node_id_refuses_closure_before_publishing_any_payload_change()
    {
        var vector = OpenBox(); var owner = new PictureEditorSession(PictureDocument.Create(100, 100)); using (owner.AddSharedVector(vector)) { }
        var target = Capture(owner, vector.ObjectId); var original = Read(owner, vector.ObjectId); var content = JsonNode.Parse(original.Content.GetRawText())!;
        var subpath = content["Paths"]![0]!["Subpaths"]![0]!.DeepClone(); subpath["Id"] = Guid.NewGuid(); content["Paths"]![0]!["Subpaths"]!.AsArray().Add(subpath);
        var ambiguous = original with { Content = JsonSerializer.SerializeToElement(content) }; var before = owner.Document.Serialize();
        var method = typeof(PictureEditorSession).Assembly.GetType("HavenOS.Images.PictureVectorSubpathEditor", true)!.GetMethod("SetClosed",
            BindingFlags.Static | BindingFlags.NonPublic, binder: null, types: [typeof(HomeProductivityObject), typeof(PictureVectorNodeTarget), typeof(bool)], modifiers: null)!;
        Assert.Contains("node ID", Assert.Throws<NotSupportedException>(() => InvokeOriginal(() => method.Invoke(null, [ambiguous, target, true]))).Message);
        Assert.Equal(before, owner.Document.Serialize());
    }

    [AvaloniaFact]
    public async Task Genuine_CUI_close_open_uses_current_private_subpath_and_preserves_other_inspector_drafts()
    {
        var vector = OpenBox(); var seed = new PictureEditorSession(PictureDocument.Create(100, 100)); using (seed.AddSharedVector(vector)) { }
        using (seed.MoveVector(seed.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), new(0, 0, 100, 100))) { }
        var path = NewPath("native"); await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Subpath initialization");
            var owner = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            Install(window, owner); window.Show(); window.UpdateLayout(); await Row(window, "Shape: Open box"); await Row(window, "Path 1"); await NodeRow(window, "Node 1.1");
            var receipt = Assert.IsType<PictureVectorNodeTarget>(typeof(MainWindow).GetField("_originalVectorSubpathTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            var change = typeof(MainWindow).GetMethod("SetSelectedVectorSubpathClosed", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var constructor = typeof(PictureVectorNodeTarget).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, types: [typeof(PictureCompositionTarget), typeof(Guid), typeof(Guid), typeof(Guid)], modifiers: null)!;
            var copied = constructor.Invoke([receipt.Vector, receipt.PathId, receipt.SubpathId, receipt.NodeId]); var before = owner.Document.Serialize();
            Assert.Equal("Select the SAME current path and subpath again.", Assert.Throws<InvalidOperationException>(() => InvokeOriginal(() => change.Invoke(window, [copied, true]))).Message);
            var foreign = Capture(new PictureEditorSession(owner.Document), vector.ObjectId, 0);
            Assert.Equal("Select the SAME current path and subpath again.", Assert.Throws<InvalidOperationException>(() => InvokeOriginal(() => change.Invoke(window, [foreign, true]))).Message); Assert.Equal(before, owner.Document.Serialize());
            var pending = Find<NumericUpDown>(window, "VectorNodeXBox"); pending.Value = 21; Find<NumericUpDown>(window, "VectorRotationBox").Value = 45;
            var anchorX = Find<NumericUpDown>(window, "NewVectorAnchorXBox"); anchorX.Value = 30; Find<NumericUpDown>(window, "NewVectorAnchorYBox").Value = 40;
            var originalPixels = Rgba(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source));
            Assert.True(Find<Button>(window, "CloseVectorSubpathButton").IsEnabled); Assert.False(Find<Button>(window, "OpenVectorSubpathButton").IsEnabled);
            await Click(window, "ZoomInButton"); await Click(window, "CloseVectorSubpathButton"); Assert.True(Closed(owner, vector.ObjectId));
            Assert.Equal(ClosedBoxGolden(), Rgba(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source)));
            Assert.Equal(21m, pending.Value); Assert.Equal(45m, Find<NumericUpDown>(window, "VectorRotationBox").Value);
            Assert.Equal(30m, anchorX.Value); Assert.Equal(40m, Find<NumericUpDown>(window, "NewVectorAnchorYBox").Value);
            Assert.False(Find<Button>(window, "AddVectorAnchorButton").IsEnabled); Assert.Contains("previous", Find<TextBlock>(window, "VectorAnchorNotice").Text);
            Assert.False(Find<Button>(window, "CloseVectorSubpathButton").IsEnabled); Assert.True(Find<Button>(window, "OpenVectorSubpathButton").IsEnabled);
            Assert.Equal("Select the SAME current path and subpath again.", Assert.Throws<InvalidOperationException>(() => InvokeOriginal(() => change.Invoke(window, [receipt, false]))).Message);
            await Click(window, "OpenVectorSubpathButton"); Assert.False(Closed(owner, vector.ObjectId));
            Assert.Equal(originalPixels, Rgba(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source)));
            await Click(window, "UndoButton"); Assert.True(Closed(owner, vector.ObjectId));
            Assert.Equal(21m, pending.Value); Assert.Equal(45m, Find<NumericUpDown>(window, "VectorRotationBox").Value); Assert.Equal(30m, anchorX.Value);
            await Click(window, "SaveButton"); var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            using (var rendered = PictureCropService.Render(cold.Document)) Assert.Equal(ClosedBoxGolden(), Rgba(rendered));
            Assert.Equal(Read(owner, vector.ObjectId).Content.GetRawText(), Read(cold, vector.ObjectId).Content.GetRawText());
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
            try { await close!.ObserveOriginalAsync(window, "Subpath original close"); Assert.True(close.IsCompletedSuccessfully); Assert.Same(close, window.OriginalClose); }
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
            try { await original.ObserveOriginalAsync(window, "Failed subpath original child"); }
            catch (Exception failure) { var actual = original.Exception ?? failure; RetainedFailures.Add((window, original, actual)); if (!failures.Any(value => ReferenceEquals(value, actual))) failures.Add(actual); }
        }
        if (failures.Count > 1) throw new AggregateException("Subpath body and original child retirement failures.", failures);
    }
}
