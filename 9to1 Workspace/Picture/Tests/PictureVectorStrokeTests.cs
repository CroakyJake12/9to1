using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
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

public sealed class PictureVectorStrokeTests
{
    private static readonly List<(object Owner, Task? Original, Exception Failure)> RetainedFailures = [];

    [AvaloniaFact]
    public async Task Actual_stroke_width_colour_opacity_caps_and_visibility_change_RGBA_with_same_source_and_cold_history()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("picture-stroke-source-").FullName;
        var sourcePath = Path.Combine(sourceDirectory, "original.png");
        using (var source = PictureCropService.Render(PictureDocument.Create(100, 100)))
            using (var stream = File.Create(sourcePath)) source.Save(stream);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
        var vector = Line(false); var session = new PictureEditorSession(new PictureCropService().OpenSource(sourcePath));
        using (session.AddSharedVector(vector)) { } using (session.MoveVector(Capture(session, vector.ObjectId).Vector, new(0, 0, 100, 100))) { }
        using (var initial = PictureCropService.Render(session.Document)) Assert.Equal(RectanglePixels(20, 49, 60, 2, [255, 0, 0, 255]), Rgba(initial));
        var requested = new DocumentVectorStroke { Enabled = true, Color = "#00FF00", Width = 10, Opacity = .5,
            Cap = DocumentVectorLineCap.Butt, Join = DocumentVectorLineJoin.Bevel };
        var before = Read(session, vector.ObjectId);
        using (var butt = session.SetVectorStroke(Capture(session, vector.ObjectId), requested))
            Assert.Equal(RectanglePixels(20, 45, 60, 10, [0, 255, 0, 127]), Rgba(butt));
        AssertOnlyStrokeFields(before, Read(session, vector.ObjectId), before.Content.GetProperty("Paths")[0].GetProperty("Id").GetGuid(), requested);
        requested.Cap = DocumentVectorLineCap.Square;
        using (var square = session.SetVectorStroke(Capture(session, vector.ObjectId), requested))
            Assert.Equal(RectanglePixels(15, 45, 70, 10, [0, 255, 0, 127]), Rgba(square));
        requested.Cap = DocumentVectorLineCap.Round;
        byte[] roundPixels;
        using (var round = session.SetVectorStroke(Capture(session, vector.ObjectId), requested))
        {
            Assert.Equal(new byte[] { 0, 255, 0, 127 }, Pixel(round, 16, 50)); Assert.Equal(new byte[4], Pixel(round, 15, 45)); roundPixels = Rgba(round);
        }
        using (var undone = session.Undo()) Assert.Equal(RectanglePixels(15, 45, 70, 10, [0, 255, 0, 127]), Rgba(undone));
        using (var redone = session.Redo()) Assert.Equal(roundPixels, Rgba(redone));
        requested.Enabled = false;
        using (var hidden = session.SetVectorStroke(Capture(session, vector.ObjectId), requested)) Assert.Equal(new byte[100 * 100 * 4], Rgba(hidden));
        var path = NewPath("stroke-cold"); await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(session.Document.DocumentId, cold.Document.DocumentId);
        Assert.Equal(Read(session, vector.ObjectId).Content.GetRawText(), Read(cold, vector.ObjectId).Content.GetRawText());
        using (var undo = cold.Undo()) Assert.Equal(roundPixels, Rgba(undo));
        using (var redo = cold.Redo()) Assert.Equal(new byte[100 * 100 * 4], Rgba(redo));
        using var final = cold.Undo(); var png = Path.Combine(Path.GetDirectoryName(path)!, "export.png");
        using (var output = File.Create(png)) final.Save(output);
        using var exported = new Bitmap(png); Assert.Equal(roundPixels, Rgba(exported));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Actual_miter_and_bevel_join_pixels_use_existing_shared_renderer_without_changing_local_nodes()
    {
        var shape = new DocumentVectorShape { ViewBox = new() { Width = 100, Height = 100 }, Paths =
            [new() { Fill = new() { Kind = DocumentVectorFillKind.None }, Stroke = new() { Enabled = true, Color = "#FF0000", Width = 10,
                Join = DocumentVectorLineJoin.Miter, Cap = DocumentVectorLineCap.Butt }, Subpaths =
                [new() { Closed = false, Nodes = [new() { X = 20, Y = 80 }, new() { X = 50, Y = 20 }, new() { X = 80, Y = 80 }] }] }] };
        var vector = new HomeVectorShapeObjectHandler().Project(shape); var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (session.AddSharedVector(vector)) { } using (session.MoveVector(Capture(session, vector.ObjectId).Vector, new(0, 0, 100, 100))) { }
        var nodes = Read(session, vector.ObjectId).Content.GetProperty("Paths")[0].GetProperty("Subpaths").GetRawText();
        using (var miter = PictureCropService.Render(session.Document)) Assert.True(Pixel(miter, 50, 14)[3] > 220);
        using (var bevel = session.SetVectorStroke(Capture(session, vector.ObjectId), new() { Enabled = true, Color = "#FF0000", Width = 10,
            Join = DocumentVectorLineJoin.Bevel, Cap = DocumentVectorLineCap.Butt })) Assert.Equal(0, Pixel(bevel, 50, 14)[3]);
        Assert.Equal(nodes, Read(session, vector.ObjectId).Content.GetProperty("Paths")[0].GetProperty("Subpaths").GetRawText());
        using var restored = session.Undo(); Assert.True(Pixel(restored, 50, 14)[3] > 220);
    }

    [Fact]
    public void Selected_field_adapter_retains_unknown_path_stroke_geometry_transform_and_untouched_double_precision()
    {
        var original = Line(true); var content = JsonNode.Parse(original.Content.GetRawText())!;
        content["Transform"]!["RotationDegrees"] = 360; content["Transform"]!["FuturePivot"] = 93;
        const double width = 2.123456789012345; const double opacity = .765432109876543;
        content["Paths"]![0]!["Stroke"]!["Width"] = width; content["Paths"]![0]!["Stroke"]!["Opacity"] = opacity;
        original = original with { Content = JsonSerializer.SerializeToElement(content) };
        var id = Shape(original).Paths[0].Id;
        var desired = new DocumentVectorStroke { Color = "#0000FF", Width = 999, Opacity = 0, Enabled = false,
            Cap = DocumentVectorLineCap.Square, Join = DocumentVectorLineJoin.Round };
        var updated = ApplyOriginalStroke(original, id, desired, new HashSet<string> { "Color" });
        var expected = JsonNode.Parse(original.Content.GetRawText())!; expected["Paths"]![0]!["Stroke"]!["Color"] = "#0000FF";
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(updated.Content.GetRawText())));
        Assert.Equal(width, Shape(updated).Paths[0].Stroke.Width); Assert.Equal(opacity, Shape(updated).Paths[0].Stroke.Opacity);
        Assert.Equal(original.ObjectId, updated.ObjectId); Assert.Equal(JsonSerializer.Serialize(original.Layout), JsonSerializer.Serialize(updated.Layout));
        Assert.Throws<ArgumentException>(() => ApplyOriginalStroke(original, id, desired, new HashSet<string> { "FutureStroke" }));
        Assert.Throws<ArgumentException>(() => ApplyOriginalStroke(original, id, desired, new HashSet<string>()));
        Assert.Throws<InvalidDataException>(() => ApplyOriginalStroke(original, id, new() { Color = "not-a-colour" }));
        Assert.Equal(width, Shape(original).Paths[0].Stroke.Width);
    }

    [AvaloniaFact]
    public void Stale_foreign_locked_and_missing_path_requests_leave_original_state_and_other_paths_unchanged()
    {
        var vector = Line(true); var session = new PictureEditorSession(PictureDocument.Create(100, 100)); using (session.AddSharedVector(vector)) { }
        var stale = Capture(session, vector.ObjectId); var foreign = Capture(new PictureEditorSession(session.Document), vector.ObjectId);
        using (session.SetVectorStroke(stale, new() { Color = "#0000FF", Width = 4 })) { }
        var before = session.Document.Serialize(); var current = Capture(session, vector.ObjectId);
        Assert.Throws<InvalidOperationException>(() => session.SetVectorStroke(stale, new()));
        Assert.Throws<InvalidOperationException>(() => session.SetVectorStroke(foreign, new()));
        Assert.Throws<InvalidOperationException>(() => session.SetVectorStroke(CreateOriginalPathTarget(current.Vector, Guid.NewGuid()), new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetVectorStroke(current, new() { Width = double.NaN }));
        Assert.Throws<InvalidDataException>(() => session.SetVectorStroke(current, new() { Color = "invalid" }));
        Assert.Equal(before, session.Document.Serialize());
        var page = PictureCompositionAdapter.Read(session.Document).Pages[0]; var layerId = page.Objects.Single(item => item.ObjectId == vector.ObjectId).LayerId;
        using (session.SetLayerLocked(session.CaptureComposition(layerId, PictureCompositionTargetKind.Layer), true)) { }
        before = session.Document.Serialize(); Assert.Contains("PermissionDenied", Assert.Throws<InvalidOperationException>(() => session.SetVectorStroke(Capture(session, vector.ObjectId), new())).Message);
        Assert.Equal(before, session.Document.Serialize());
    }

    [AvaloniaFact]
    public async Task Genuine_CUI_stroke_draft_binds_original_path_untouched_fields_follow_undo_and_angles_remain_pending()
    {
        var vector = Line(true); var seed = new PictureEditorSession(PictureDocument.Create(100, 100)); using (seed.AddSharedVector(vector)) { }
        using (seed.MoveVector(Capture(seed, vector.ObjectId).Vector, new(0, 0, 100, 100))) { }
        var path = NewPath("native-strokes"); await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Vector stroke initialization");
            var session = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            Install(window, session); window.Show(); window.UpdateLayout(); await Row(window, "Shape: Stroke"); await Row(window, "Path 1");
            var width = Find<NumericUpDown>(window, "VectorStrokeWidthBox"); var colour = Find<TextBox>(window, "VectorStrokeColorBox");
            var opacity = Find<NumericUpDown>(window, "VectorStrokeOpacityBox"); var angle = Find<NumericUpDown>(window, "VectorRotationBox");
            width.Value = 8; colour.Text = "#00FF00"; opacity.Value = .5m; Find<ComboBox>(window, "VectorStrokeCapBox").SelectedItem = DocumentVectorLineCap.Square;
            Find<ComboBox>(window, "VectorStrokeJoinBox").SelectedItem = DocumentVectorLineJoin.Round; angle.Value = 45;
            await Click(window, "ZoomInButton"); Assert.Equal(8m, width.Value); Assert.Equal("#00FF00", colour.Text);
            await Row(window, "Path 2"); Assert.False(Find<Button>(window, "ApplyVectorStrokeButton").IsEnabled);
            Assert.Contains("another path", Find<TextBlock>(window, "VectorStrokeNotice").Text);
            var unchanged = session.Document.Serialize(); await window.DispatchAsync("9to1.Picture.ApplyVectorStroke", null, TestContext.Current.CancellationToken);
            Assert.Equal(unchanged, session.Document.Serialize()); Assert.Equal(8m, width.Value);
            await Click(window, "ResetVectorStrokeButton"); Assert.Equal(2m, width.Value); Assert.Equal("#FF0000", colour.Text); Assert.Equal(1m, opacity.Value);
            Assert.Equal(45m, angle.Value); width.Value = 5; await Row(window, "Path 1"); Assert.Equal(5m, width.Value);
            Assert.False(Find<Button>(window, "ApplyVectorStrokeButton").IsEnabled); await Row(window, "Path 2");
            await Click(window, "ApplyVectorStrokeButton"); Assert.Equal(5, Shape(Read(session, vector.ObjectId)).Paths[1].Stroke.Width);
            width.Value = 7; await Click(window, "ApplyVectorStrokeButton"); colour.Text = "#0000FF";
            await Click(window, "UndoButton"); Assert.Equal(5m, width.Value); Assert.Equal("#0000FF", colour.Text);
            await Click(window, "ApplyVectorStrokeButton"); Assert.Equal(5, Shape(Read(session, vector.ObjectId)).Paths[1].Stroke.Width);
            Assert.Equal("#0000FF", Shape(Read(session, vector.ObjectId)).Paths[1].Stroke.Color);
            await Row(window, "Path 1"); colour.Text = "#00FF00"; await Click(window, "MirrorVectorHorizontalButton");
            Assert.Equal("#00FF00", colour.Text); Assert.Equal(45m, angle.Value); await Click(window, "ApplyVectorStrokeButton");
            Assert.Equal(2, Shape(Read(session, vector.ObjectId)).Paths[0].Stroke.Width); Assert.Equal(1, Shape(Read(session, vector.ObjectId)).Paths[0].Stroke.Opacity);
            Assert.Equal(-1, Shape(Read(session, vector.ObjectId)).Transform.ScaleX); Assert.Equal(45m, angle.Value);
            var expected = Rgba(Preview(window)); await Click(window, "SaveButton");
            var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            using (var rendered = PictureCropService.Render(cold.Document)) Assert.Equal(expected, Rgba(rendered));
            Assert.Equal(Read(session, vector.ObjectId).Content.GetRawText(), Read(cold, vector.ObjectId).Content.GetRawText());
            colour.Text = "#123456"; Install(window, new PictureEditorSession(cold.Document, path));
            Assert.Equal(0m, angle.Value); Assert.False(Find<Button>(window, "ApplyVectorStrokeButton").IsEnabled);
            await Row(window, "Shape: Stroke"); await Row(window, "Path 1"); Assert.Equal("#00FF00", colour.Text);
        }
        catch (Exception failure) { firstFailure = failure; RetainedFailures.Add((window, window.OriginalCommand, failure)); throw; }
        finally
        {
            if (firstFailure is null)
            {
                window.Close(); Assert.NotNull(window.OriginalClose); var close = window.OriginalClose!;
                try { await close.ObserveOriginalAsync(window, "Vector stroke original retirement"); Assert.True(close.IsCompletedSuccessfully); Assert.Same(close, window.OriginalClose); }
                catch (Exception failure) { RetainedFailures.Add((window, close, close.Exception ?? failure)); throw; }
            }
            else
            {
                var children = new List<Task>(); var failures = new List<Exception> { firstFailure };
                if (window.OriginalCommand is { } command) children.Add(command);
                if (window.OriginalInitialization is { } initialization) children.Add(initialization);
                try { children.Add(window.SceneHost.CloseOriginalAsync()); }
                catch (Exception failure) { RetainedFailures.Add((window.SceneHost, null, failure)); failures.Add(failure); }
                foreach (var sameChild in children.Distinct<Task>(ReferenceEqualityComparer.Instance))
                {
                    RetainedFailures.Add((window, sameChild, firstFailure));
                    try { await sameChild.ObserveOriginalAsync(window, "Failed vector stroke original child"); }
                    catch (Exception failure)
                    {
                        var actual = sameChild.Exception ?? failure; RetainedFailures.Add((window, sameChild, actual));
                        if (!failures.Any(value => ReferenceEquals(value, actual))) failures.Add(actual);
                    }
                }
                if (failures.Count > 1) throw new AggregateException("Vector stroke body and original retirement failures.", failures);
            }
        }
    }

    private static readonly MethodInfo OriginalStrokeMethod = typeof(PictureEditorSession).Assembly
        .GetType("HavenOS.Images.PictureVectorStrokeEditor", throwOnError: true)!
        .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic, binder: null,
            types: [typeof(HomeProductivityObject), typeof(Guid), typeof(DocumentVectorStroke), typeof(IReadOnlySet<string>)], modifiers: null)!;
    private static readonly ConstructorInfo OriginalPathConstructor = typeof(PictureVectorPathTarget)
        .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
            types: [typeof(PictureCompositionTarget), typeof(Guid)], modifiers: null)!;
    private static HomeProductivityObject ApplyOriginalStroke(HomeProductivityObject original, Guid pathId,
        DocumentVectorStroke requested, IReadOnlySet<string>? fields = null) =>
        (HomeProductivityObject)InvokeOriginal(() => OriginalStrokeMethod.Invoke(null, [original, pathId, requested, fields]))!;
    private static PictureVectorPathTarget CreateOriginalPathTarget(PictureCompositionTarget vector, Guid pathId) =>
        (PictureVectorPathTarget)InvokeOriginal(() => OriginalPathConstructor.Invoke([vector, pathId]))!;
    // Invoke the exact original internal adapter; rethrow its actual cause so
    // existing refusal assertions observe the original exception and stack.
    private static object? InvokeOriginal(Func<object?> invoke)
    {
        try { return invoke(); }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException).Throw();
            throw;
        }
    }

    private static HomeProductivityObject Line(bool twoPaths)
    {
        DocumentVectorPath Make(double y) => new() { Fill = new() { Kind = DocumentVectorFillKind.None },
            Stroke = new() { Enabled = true, Color = "#FF0000", Width = 2, Cap = DocumentVectorLineCap.Butt, Join = DocumentVectorLineJoin.Miter },
            Subpaths = [new() { Closed = false, Nodes = [new() { X = 20, Y = y }, new() { X = 80, Y = y }] }] };
        var shape = new DocumentVectorShape { Name = "Stroke", ViewBox = new() { Width = 100, Height = 100 }, Paths = twoPaths ? [Make(30), Make(70)] : [Make(50)] };
        var original = new HomeVectorShapeObjectHandler().Project(shape); var content = JsonNode.Parse(original.Content.GetRawText())!;
        content["FutureShape"] = "retain"; content["Paths"]![0]!["FuturePath"] = 51; content["Paths"]![0]!["Stroke"]!["FutureStroke"] = new JsonObject { ["preserved"] = true };
        return original with { Content = JsonSerializer.SerializeToElement(content) };
    }
    private static void AssertOnlyStrokeFields(HomeProductivityObject original, HomeProductivityObject updated, Guid pathId, DocumentVectorStroke style)
    {
        var expected = JsonNode.Parse(original.Content.GetRawText())!; var path = expected["Paths"]!.AsArray().Single(value => value!["Id"]!.GetValue<Guid>() == pathId)!;
        var fields = JsonSerializer.SerializeToNode(style)!.AsObject(); foreach (var pair in fields) path["Stroke"]![pair.Key] = pair.Value!.DeepClone();
        Assert.Equal(original.ObjectId, updated.ObjectId); Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(updated.Content.GetRawText())));
    }
    private static DocumentVectorShape Shape(HomeProductivityObject original) => HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
    private static HomeProductivityObject Read(PictureEditorSession owner, Guid id) => PictureCompositionAdapter.ReadVector(PictureCompositionAdapter.Read(owner.Document).Pages[0].Objects.Single(item => item.ObjectId == id));
    private static PictureVectorPathTarget Capture(PictureEditorSession owner, Guid id) => CreateOriginalPathTarget(owner.CaptureComposition(id, PictureCompositionTargetKind.Vector), Shape(Read(owner, id)).Paths[0].Id);
    private static string NewPath(string label) => Path.Combine(Directory.CreateTempSubdirectory("picture-" + label + "-").FullName, "editable.picture.json");
    private static void Install(MainWindow window, PictureEditorSession session) => typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [session, PictureCropService.Render(session.Document), null, null]);
    private static Task Row(MainWindow window, string label) => PictureCuiActionFixture.ClickAsync(window,
        window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label)));
    private static Task Click(MainWindow window, string id) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, id));
    private static T Find<T>(MainWindow window, string id) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == id);
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private static byte[] Rgba(Bitmap source)
    {
        using var converted = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = converted.Lock(); source.CopyPixels(buffer); var bytes = new byte[source.PixelSize.Width * source.PixelSize.Height * 4];
        for (var y = 0; y < source.PixelSize.Height; y++) Marshal.Copy(IntPtr.Add(buffer.Address, y * buffer.RowBytes), bytes, y * source.PixelSize.Width * 4, source.PixelSize.Width * 4);
        return bytes;
    }
    private static byte[] Pixel(Bitmap bitmap, int x, int y) => Rgba(bitmap).AsSpan((y * bitmap.PixelSize.Width + x) * 4, 4).ToArray();
    private static byte[] RectanglePixels(int left, int top, int width, int height, byte[] pixel)
    {
        var result = new byte[100 * 100 * 4];
        for (var y = top; y < top + height; y++) for (var x = left; x < left + width; x++) pixel.CopyTo(result, (y * 100 + x) * 4);
        return result;
    }
}
