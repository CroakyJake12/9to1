using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureVectorNodeTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedUiOwners = [];

    [AvaloniaFact]
    public async Task Node_edit_changes_real_pixels_uses_shared_history_and_preserves_original_unknown_fields_after_reopen()
    {
        var vector = Rectangle();
        var content = JsonNode.Parse(vector.Content.GetRawText())!.AsObject();
        var nodeJson = content["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!;
        nodeJson["FutureNode"] = new JsonObject { ["retained"] = 23 };
        content["FutureShape"] = "preserve";
        content["Transform"]!["RotationDegrees"] = 360; // Shared editor normalization must not rewrite this retained field.
        vector = vector with { Content = JsonSerializer.SerializeToElement(content) };
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (var initial = session.AddSharedVector(vector)) AssertRed(initial, 75, 30);
        var original = Retained(session, vector.ObjectId);
        var target = Capture(session, vector.ObjectId, 1);
        using (var edited = session.MoveVectorNode(target, 50, 0)) Assert.Equal(0, Pixel(edited, 75, 30).A);
        var updated = Retained(session, vector.ObjectId);
        var expected = JsonNode.Parse(original.Content.GetRawText())!;
        var expectedNode = expected["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!;
        expectedNode["X"] = 50d;
        expectedNode["Point"]!["X"] = 50d;
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(updated.Content.GetRawText())));
        Assert.Equal(vector.ObjectId, updated.ObjectId);
        Assert.Equal(target.NodeId, Node(updated, 1).Id);
        Assert.Equal(23, updated.Content.GetProperty("Paths")[0].GetProperty("Subpaths")[0].GetProperty("Nodes")[1].GetProperty("FutureNode").GetProperty("retained").GetInt32());
        using (var undone = session.Undo()) AssertRed(undone, 75, 30);
        using (var redone = session.Redo()) Assert.Equal(0, Pixel(redone, 75, 30).A);
        var path = NewPath("node");
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(session.Document.DocumentId, reopened.Document.DocumentId);
        Assert.Equal(target.NodeId, Node(Retained(reopened, vector.ObjectId), 1).Id);
        Assert.Equal(updated.Content.GetRawText(), Retained(reopened, vector.ObjectId).Content.GetRawText());
        using var rendered = PictureCropService.Render(reopened.Document);
        Assert.Equal(0, Pixel(rendered, 75, 30).A);
    }

    [AvaloniaFact]
    public void Cubic_controls_change_curve_pixels_and_preserve_nested_unknown_fields_with_same_outer_undo()
    {
        var vector = Curve();
        var content = JsonNode.Parse(vector.Content.GetRawText())!;
        content["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!["Control1"]!["FutureControl"] = "retain";
        vector = vector with { Content = JsonSerializer.SerializeToElement(content) };
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (var initial = session.AddSharedVector(vector)) AssertRed(initial, 50, 45);
        var unchanged = session.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => session.MoveVectorControlPoint(Capture(session, vector.ObjectId, 0), 1, 0, 0));
        Assert.Equal(unchanged, session.Document.Serialize());
        var before = Retained(session, vector.ObjectId);
        using (var edited = session.MoveVectorControlPoint(Capture(session, vector.ObjectId, 1), 1, 0, 100))
            Assert.Equal(0, Pixel(edited, 50, 45).A);
        var after = Retained(session, vector.ObjectId);
        var expected = JsonNode.Parse(before.Content.GetRawText())!;
        expected["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!["Control1"]!["Y"] = 100d;
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(after.Content.GetRawText())));
        Assert.Equal("retain", after.Content.GetProperty("Paths")[0].GetProperty("Subpaths")[0].GetProperty("Nodes")[1].GetProperty("Control1").GetProperty("FutureControl").GetString());
        using (var second = session.MoveVectorControlPoint(Capture(session, vector.ObjectId, 1), 2, 100, 100))
            Assert.Equal(0, Pixel(second, 50, 60).A);
        using (session.Undo()) { }
        Assert.Equal(0, Node(Retained(session, vector.ObjectId), 1).Control2!.Y);
        using (var restored = session.Undo()) AssertRed(restored, 50, 45);
        Assert.Equal(before.Content.GetRawText(), Retained(session, vector.ObjectId).Content.GetRawText());
    }

    [AvaloniaFact]
    public void Quadratic_control_uses_shared_geometry_and_refuses_a_second_control_without_mutation()
    {
        var vector = Curve();
        var content = JsonNode.Parse(vector.Content.GetRawText())!;
        var node = content["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!;
        node["IncomingSegment"] = (int)DocumentVectorSegmentKind.Quadratic;
        node["Control1"]!["X"] = 50d; node["Control2"] = null;
        vector = vector with { Content = JsonSerializer.SerializeToElement(content) };
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (var original = session.AddSharedVector(vector)) AssertRed(original, 50, 45);
        var before = session.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => session.MoveVectorControlPoint(Capture(session, vector.ObjectId, 1), 2, 100, 100));
        Assert.Equal(before, session.Document.Serialize());
        using (var edited = session.MoveVectorControlPoint(Capture(session, vector.ObjectId, 1), 1, 50, 100))
            Assert.Equal(0, Pixel(edited, 50, 45).A);
        Assert.Null(Node(Retained(session, vector.ObjectId), 1).Control2);
        using var restored = session.Undo();
        AssertRed(restored, 50, 45);
    }

    [AvaloniaFact]
    public void Stale_foreign_locked_nonfinite_and_invalid_control_requests_preserve_same_original_document()
    {
        var vector = Rectangle();
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (session.AddSharedVector(vector)) { }
        var stale = Capture(session, vector.ObjectId, 1);
        var foreignOwner = new PictureEditorSession(session.Document);
        var foreign = Capture(foreignOwner, vector.ObjectId, 1);
        using (session.MoveVectorNode(stale, 50, 0)) { }
        var before = session.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => session.MoveVectorNode(stale, 80, 0));
        Assert.Throws<InvalidOperationException>(() => session.MoveVectorNode(foreign, 80, 0));
        var current = Capture(session, vector.ObjectId, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.MoveVectorNode(current, double.NaN, 0));
        Assert.Throws<InvalidOperationException>(() => session.MoveVectorControlPoint(current, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.MoveVectorControlPoint(current, 3, 0, 0));
        Assert.Equal(before, session.Document.Serialize());
        var page = PictureCompositionAdapter.Read(session.Document).Pages[0];
        var layer = page.Objects.Single(item => item.ObjectId == vector.ObjectId).LayerId;
        using (session.SetLayerLocked(session.CaptureComposition(layer, PictureCompositionTargetKind.Layer), true)) { }
        before = session.Document.Serialize();
        var denial = Assert.Throws<InvalidOperationException>(() => session.MoveVectorNode(Capture(session, vector.ObjectId, 1), 80, 0));
        Assert.Contains("PermissionDenied", denial.Message);
        Assert.Equal(before, session.Document.Serialize());
    }

    [AvaloniaFact]
    public void Imported_reused_node_identity_is_preserved_and_refused_instead_of_editing_a_different_subpath()
    {
        var vector = Rectangle();
        var content = JsonNode.Parse(vector.Content.GetRawText())!;
        var first = content["Paths"]![0]!["Subpaths"]![0]!;
        var second = first.DeepClone();
        second["Id"] = Guid.NewGuid();
        for (var index = 0; index < 4; index++)
            if (index != 1) second["Nodes"]![index]!["Id"] = Guid.NewGuid();
        content["Paths"]![0]!["Subpaths"]!.AsArray().Add(second);
        vector = vector with { Content = JsonSerializer.SerializeToElement(content) };
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (var imported = session.AddSharedVector(vector)) AssertRed(imported, 50, 50);
        var before = session.Document.Serialize();
        var retained = HomeVectorShapeObjectHandler.ReadCanonical(Retained(session, vector.ObjectId).Content, vector.ObjectId);
        var id = retained.Paths[0].Subpaths[0].Nodes[1].Id;
        Assert.Equal(id, retained.Paths[0].Subpaths[1].Nodes[1].Id);
        var candidates = session.CaptureVectorNodes(session.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), retained.Paths[0].Id);
        Assert.Equal(6, candidates.Count);
        Assert.DoesNotContain(candidates, target => target.NodeId == id);
        var failure = Assert.Throws<NotSupportedException>(() => Capture(session, vector.ObjectId, 1));
        Assert.Contains("reused", failure.Message);
        Assert.Equal(before, session.Document.Serialize());
        using var rendered = PictureCropService.Render(session.Document);
        AssertRed(rendered, 50, 50);
    }

    [AvaloniaFact]
    public async Task Actual_CUI_node_and_curve_controls_modify_same_owner_preview_and_save_reopen_the_exact_nodes()
    {
        var vector = Curve();
        var content = JsonNode.Parse(vector.Content.GetRawText())!;
        const double untouchedY = 50.12345678901234;
        content["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!["Y"] = untouchedY;
        content["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!["Point"]!["Y"] = untouchedY;
        vector = vector with { Content = JsonSerializer.SerializeToElement(content) };
        var seed = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (seed.AddSharedVector(vector)) { }
        var path = NewPath("native-nodes");
        await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness());
        Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture vector-node initialization");
            // The native Open path reads a genuine document then installs this
            // same session/preview. This fixture bypasses only the OS picker;
            // it grants no Home/Files access and replaces no CUI action.
            var originalOpen = PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
            var loaded = await originalOpen.ObserveOriginalAsync(window, "Picture vector-node document read");
            var loadedSession = new PictureEditorSession(loaded, path);
            typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [loadedSession, PictureCropService.Render(loaded), null, null]);
            window.Show(); window.UpdateLayout();
            var session = CurrentSession(window);
            await PictureCuiActionFixture.ClickAsync(window, Row(window, "Shape: Curve"));
            await PictureCuiActionFixture.ClickAsync(window, Row(window, "Path 1"));
            await PictureCuiActionFixture.ClickAsync(window, Row(window, "Node 1.2"));
            Assert.True(Find<Button>(window, "ApplyVectorNodeButton").IsEnabled);
            Assert.True(Find<Button>(window, "ApplyVectorControl1Button").IsEnabled);
            Assert.True(Find<Button>(window, "ApplyVectorControl2Button").IsEnabled);
            AssertRed(Preview(window), 50, 45);
            Find<NumericUpDown>(window, "VectorControl1YBox").Value = 100;
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "ZoomInButton"));
            Assert.Equal(100m, Find<NumericUpDown>(window, "VectorControl1YBox").Value);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "ApplyVectorControl1Button"));
            Assert.Equal(0, Pixel(Preview(window), 50, 45).A);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "UndoButton"));
            AssertRed(Preview(window), 50, 45);
            Find<NumericUpDown>(window, "VectorNodeXBox").Value = 50;
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "ApplyVectorNodeButton"));
            Assert.Equal(50, Node(Retained(session, vector.ObjectId), 1).X);
            Assert.Equal(untouchedY, Node(Retained(session, vector.ObjectId), 1).Y);
            await PictureCuiActionFixture.ClickAsync(window, Row(window, "Node 1.1"));
            Assert.False(Find<Button>(window, "ApplyVectorControl1Button").IsEnabled);
            Assert.False(Find<Button>(window, "ApplyVectorControl2Button").IsEnabled);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "SaveButton"));
            var reopened = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
            Assert.Same(session, CurrentSession(window));
            Assert.Equal(session.Document.DocumentId, reopened.DocumentId);
            var retained = PictureCompositionAdapter.ReadVector(PictureCompositionAdapter.Read(reopened).Pages[0].Objects.Single(item => item.ObjectId == vector.ObjectId));
            Assert.Equal(Node(Retained(session, vector.ObjectId), 1).Id, Node(retained, 1).Id);
            Assert.Equal(50, Node(retained, 1).X);
        }
        catch (Exception error) { firstFailure = error; RetainedFailedUiOwners.Add((window, error)); throw; }
        finally
        {
            if (firstFailure is null)
            {
                window.Close(); Assert.NotNull(window.OriginalClose);
                var close = window.OriginalClose!;
                try { await close.ObserveOriginalAsync(window, "Picture vector-node original retirement"); }
                catch (Exception error) { RetainedFailedUiOwners.Add((window, error)); throw; }
                Assert.Same(close, window.OriginalClose); Assert.True(close.IsCompletedSuccessfully);
            }
        }
    }

    private static HomeProductivityObject Rectangle() => Make(false);
    private static HomeProductivityObject Curve() => Make(true);
    private static HomeProductivityObject Make(bool curved)
    {
        var shape = new DocumentVectorShape { Name = curved ? "Curve" : "Rectangle", ViewBox = new() { Width = 100, Height = 100 }, Paths =
            [new() { Fill = new() { Kind = DocumentVectorFillKind.Solid, Color = "#FF0000" }, Stroke = new() { Enabled = false }, Subpaths =
                [new() { Closed = true, Nodes = [new() { X = 0, Y = curved ? 50 : 0 },
                    new() { X = 100, Y = curved ? 50 : 0, IncomingSegment = curved ? DocumentVectorSegmentKind.Cubic : DocumentVectorSegmentKind.Line,
                        Control1 = curved ? new(0, 0) : null, Control2 = curved ? new(100, 0) : null },
                    new() { X = 100, Y = 100 }, new() { X = 0, Y = 100 }] }] }] };
        return new HomeVectorShapeObjectHandler().Project(shape);
    }
    private static HomeProductivityObject Retained(PictureEditorSession session, Guid id) => PictureCompositionAdapter.ReadVector(
        PictureCompositionAdapter.Read(session.Document).Pages[0].Objects.Single(item => item.ObjectId == id));
    private static DocumentVectorNode Node(HomeProductivityObject vector, int index) => HomeVectorShapeObjectHandler.ReadCanonical(vector.Content, vector.ObjectId).Paths[0].Subpaths[0].Nodes[index];
    private static PictureVectorNodeTarget Capture(PictureEditorSession owner, Guid vectorId, int nodeIndex)
    {
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(Retained(owner, vectorId).Content, vectorId);
        return owner.CaptureVectorNode(owner.CaptureComposition(vectorId, PictureCompositionTargetKind.Vector), shape.Paths[0].Id,
            shape.Paths[0].Subpaths[0].Id, shape.Paths[0].Subpaths[0].Nodes[nodeIndex].Id);
    }
    private static string NewPath(string label) => Path.Combine(Path.GetTempPath(), "picture-" + label + "-" + Guid.NewGuid().ToString("N") + ".picture.json");
    private static T Find<T>(MainWindow window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static Button Row(MainWindow window, string label) => window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label));
    private static PictureEditorSession CurrentSession(MainWindow window) => Assert.IsType<PictureEditorSession>(typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private static void AssertRed(Bitmap bitmap, int x, int y) { var p = Pixel(bitmap, x, y); Assert.True(p.R > 220 && p.G < 30 && p.A > 220, $"Expected red pixel, observed {p}."); }
    private static (byte R, byte G, byte B, byte A) Pixel(Bitmap bitmap, int x, int y)
    {
        var bytes = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)]; var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), pinned.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4);
            var offset = checked((y * bitmap.PixelSize.Width + x) * 4); return (bytes[offset + 2], bytes[offset + 1], bytes[offset], bytes[offset + 3]); }
        finally { pinned.Free(); }
    }
}
