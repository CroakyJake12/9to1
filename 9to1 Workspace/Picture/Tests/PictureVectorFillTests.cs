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

public sealed class PictureVectorFillTests
{
    private static readonly List<(object Owner, Task? Original, Exception Failure)> RetainedFailures = [];
    private static readonly List<object> RetainedOriginals = [];
    private static readonly ConstructorInfo PathConstructor = typeof(PictureVectorPathTarget).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null, types: [typeof(PictureCompositionTarget), typeof(Guid)], modifiers: null)!;
    private static readonly MethodInfo FillAdapter = typeof(PictureEditorSession).Assembly.GetType("HavenOS.Images.PictureVectorFillEditor", true)!
        .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic, binder: null,
            types: [typeof(HomeProductivityObject), typeof(Guid), typeof(DocumentVectorFill), typeof(DocumentVectorFillRule), typeof(IReadOnlySet<string>)], modifiers: null)!;
    private static object? InvokeOriginal(Func<object?> invoke)
    {
        try { return invoke(); }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
    }
    private static PictureVectorPathTarget PathTarget(PictureCompositionTarget vector, Guid path) =>
        (PictureVectorPathTarget)InvokeOriginal(() => PathConstructor.Invoke([vector, path]))!;
    private static PictureVectorPathTarget Capture(PictureEditorSession owner, Guid vector, int path = 0) =>
        PathTarget(owner.CaptureComposition(vector, PictureCompositionTargetKind.Vector), Shape(Read(owner, vector)).Paths[path].Id);
    private static HomeProductivityObject Read(PictureEditorSession owner, Guid id) =>
        PictureCompositionAdapter.ReadVector(PictureCompositionAdapter.Read(owner.Document).Pages[0].Objects.Single(item => item.ObjectId == id));
    private static DocumentVectorShape Shape(HomeProductivityObject item) => HomeVectorShapeObjectHandler.ReadCanonical(item.Content, item.ObjectId);
    private static HomeProductivityObject Contours(bool twoPaths = false)
    {
        DocumentVectorSubpath Square(double left, double top, double size) => new() { Closed = true, Nodes =
            [new() { X = left, Y = top }, new() { X = left + size, Y = top }, new() { X = left + size, Y = top + size }, new() { X = left, Y = top + size }] };
        DocumentVectorPath First() => new() { Fill = new() { Kind = DocumentVectorFillKind.Solid, Color = "#FF0000" },
            Stroke = new() { Enabled = false }, Subpaths = [Square(2, 2, 12), Square(6, 6, 4)] };
        var shape = new DocumentVectorShape { Name = "Contours", ViewBox = new() { Width = 16, Height = 16 },
            Paths = twoPaths ? [First(), new() { Fill = new() { Kind = DocumentVectorFillKind.None, Color = "#00FF00" },
                Stroke = new() { Enabled = false }, Subpaths = [Square(0, 0, 2)] }] : [First()] };
        var original = new HomeVectorShapeObjectHandler().Project(shape); var node = JsonNode.Parse(original.Content.GetRawText())!;
        node["FutureShape"] = new JsonObject { ["retain"] = 84 }; node["Paths"]![0]!["FuturePath"] = "keep";
        node["Paths"]![0]!["Fill"]!["FutureFill"] = new JsonObject { ["colour"] = "keep" };
        return original with { Content = JsonSerializer.SerializeToElement(node) };
    }
    private static string NewPath(string label) => Path.Combine(Directory.CreateTempSubdirectory("picture-fill-" + label + "-").FullName, "editable.picture.json");
    private static byte[] Rgba(Bitmap source)
    {
        using var converted = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = converted.Lock(); source.CopyPixels(buffer); var result = new byte[source.PixelSize.Width * source.PixelSize.Height * 4];
        for (var y = 0; y < source.PixelSize.Height; y++) Marshal.Copy(IntPtr.Add(buffer.Address, y * buffer.RowBytes), result, y * source.PixelSize.Width * 4, source.PixelSize.Width * 4);
        return result;
    }
    private static byte[] Golden(bool hole, byte[] colour)
    {
        var result = new byte[16 * 16 * 4];
        for (var y = 2; y < 14; y++) for (var x = 2; x < 14; x++)
            if (!hole || x < 6 || x >= 10 || y < 6 || y >= 10) colour.CopyTo(result, (y * 16 + x) * 4);
        return result;
    }

    [AvaloniaFact]
    public async Task Actual_contour_rule_solid_colour_opacity_and_none_change_all_RGBA_preserving_source_and_cold_history()
    {
        var originalPath = Path.Combine(Directory.CreateTempSubdirectory("picture-fill-source-").FullName, "original.png");
        using (var source = PictureCropService.Render(PictureDocument.Create(16, 16))) using (var bytes = File.Create(originalPath)) source.Save(bytes);
        var sourceBytes = await File.ReadAllBytesAsync(originalPath, TestContext.Current.CancellationToken);
        var owner = new PictureEditorSession(new PictureCropService().OpenSource(originalPath)); var vector = Contours();
        using (owner.AddSharedVector(vector)) { } using (owner.MoveVector(owner.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), new(0, 0, 16, 16))) { }
        using (var initial = PictureCropService.Render(owner.Document)) Assert.Equal(Golden(true, [255, 0, 0, 255]), Rgba(initial));
        var before = JsonNode.Parse(Read(owner, vector.ObjectId).Content.GetRawText())!;
        var requested = new DocumentVectorFill { Kind = DocumentVectorFillKind.Solid, Color = "#00FF00", Opacity = .5 };
        byte[] edited;
        using (var actual = owner.SetVectorFillStyle(Capture(owner, vector.ObjectId), requested, DocumentVectorFillRule.NonZero))
        { edited = Rgba(actual); Assert.Equal(Golden(false, [0, 255, 0, 127]), edited); }
        before["Paths"]![0]!["Fill"]!["Kind"] = (int)requested.Kind; before["Paths"]![0]!["Fill"]!["Color"] = requested.Color;
        before["Paths"]![0]!["Fill"]!["Opacity"] = requested.Opacity; before["Paths"]![0]!["FillRule"] = (int)DocumentVectorFillRule.NonZero;
        Assert.True(JsonNode.DeepEquals(before, JsonNode.Parse(Read(owner, vector.ObjectId).Content.GetRawText())));
        using (var undone = owner.Undo()) Assert.Equal(Golden(true, [255, 0, 0, 255]), Rgba(undone));
        using (var redone = owner.Redo()) Assert.Equal(edited, Rgba(redone));
        requested.Kind = DocumentVectorFillKind.None;
        using (var hidden = owner.SetVectorFillStyle(Capture(owner, vector.ObjectId), requested, DocumentVectorFillRule.NonZero)) Assert.Equal(new byte[16 * 16 * 4], Rgba(hidden));
        var path = NewPath("cold"); await owner.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(owner.Document.DocumentId, cold.Document.DocumentId); Assert.Equal(Read(owner, vector.ObjectId).Content.GetRawText(), Read(cold, vector.ObjectId).Content.GetRawText());
        using (var restored = cold.Undo()) Assert.Equal(edited, Rgba(restored)); using (var hidden = cold.Redo()) Assert.Equal(new byte[16 * 16 * 4], Rgba(hidden));
        using var exported = cold.Undo(); var png = Path.Combine(Path.GetDirectoryName(path)!, "export.png"); using (var stream = File.Create(png)) exported.Save(stream);
        using var reopened = new Bitmap(png); Assert.Equal(edited, Rgba(reopened));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(originalPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Selected_field_adapter_preserves_other_fill_fields_full_precision_rule_stroke_and_unknown_JSON()
    {
        var original = Contours(true); var content = JsonNode.Parse(original.Content.GetRawText())!; const double opacity = .765432109876543;
        content["Paths"]![0]!["Fill"]!["Opacity"] = opacity; content["Transform"]!["RotationDegrees"] = 360.1234567890123;
        original = original with { Content = JsonSerializer.SerializeToElement(content) }; var id = Shape(original).Paths[0].Id;
        HomeProductivityObject Edit(IReadOnlySet<string> fields) => (HomeProductivityObject)InvokeOriginal(() => FillAdapter.Invoke(null,
            [original, id, new DocumentVectorFill { Color = "#0000FF", Opacity = 0, Kind = DocumentVectorFillKind.None }, DocumentVectorFillRule.NonZero, fields]))!;
        var updated = Edit(new HashSet<string> { "Color" }); var expected = JsonNode.Parse(original.Content.GetRawText())!;
        expected["Paths"]![0]!["Fill"]!["Color"] = "#0000FF";
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(updated.Content.GetRawText()))); Assert.Equal(opacity, Shape(updated).Paths[0].Fill.Opacity);
        Assert.Equal(JsonSerializer.Serialize(original.Layout), JsonSerializer.Serialize(updated.Layout)); Assert.Equal(original.ObjectId, updated.ObjectId);
        Assert.Throws<ArgumentException>(() => Edit(new HashSet<string>())); Assert.Throws<ArgumentException>(() => Edit(new HashSet<string> { "FutureFill" }));
        Assert.Equal(opacity, Shape(original).Paths[0].Fill.Opacity);
    }

    [AvaloniaFact]
    public void Stale_foreign_missing_path_locked_and_invalid_fill_requests_preserve_actual_document_and_history()
    {
        var owner = new PictureEditorSession(PictureDocument.Create(16, 16)); var vector = Contours(true); using (owner.AddSharedVector(vector)) { }
        var stale = Capture(owner, vector.ObjectId); var foreign = Capture(new PictureEditorSession(owner.Document), vector.ObjectId);
        using (owner.SetVectorFillStyle(stale, new() { Color = "#00FF00" }, DocumentVectorFillRule.NonZero)) { }
        var current = Capture(owner, vector.ObjectId); var before = owner.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => owner.SetVectorFillStyle(stale, new(), DocumentVectorFillRule.EvenOdd));
        Assert.Throws<InvalidOperationException>(() => owner.SetVectorFillStyle(foreign, new(), DocumentVectorFillRule.EvenOdd));
        Assert.Throws<InvalidOperationException>(() => owner.SetVectorFillStyle(PathTarget(current.Vector, Guid.NewGuid()), new(), DocumentVectorFillRule.EvenOdd));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.SetVectorFillStyle(current, new() { Opacity = double.NaN }, DocumentVectorFillRule.EvenOdd));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.SetVectorFillStyle(current, new(), (DocumentVectorFillRule)999)); Assert.Equal(before, owner.Document.Serialize());
        var graph = PictureCompositionAdapter.Read(owner.Document); var layer = graph.Pages[0].Objects.Single(item => item.ObjectId == vector.ObjectId).LayerId;
        using (owner.SetLayerLocked(owner.CaptureComposition(layer, PictureCompositionTargetKind.Layer), true)) { }
        before = owner.Document.Serialize(); Assert.Contains("PermissionDenied", Assert.Throws<InvalidOperationException>(() =>
            owner.SetVectorFillStyle(Capture(owner, vector.ObjectId), new(), DocumentVectorFillRule.EvenOdd)).Message); Assert.Equal(before, owner.Document.Serialize());
    }

    [AvaloniaFact]
    public async Task Genuine_CUI_fill_fields_keep_original_path_draft_and_untouched_fields_through_undo_stroke_save_and_reopen()
    {
        var vector = Contours(true); var seed = new PictureEditorSession(PictureDocument.Create(16, 16)); using (seed.AddSharedVector(vector)) { }
        using (seed.MoveVector(seed.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), new(0, 0, 16, 16))) { }
        var path = NewPath("native"); await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Fill initialization");
            var owner = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            Install(window, owner); window.Show(); window.UpdateLayout(); await Row(window, "Shape: Contours"); await Row(window, "Path 1");
            var colour = Find<TextBox>(window, "VectorFillBox"); var opacity = Find<NumericUpDown>(window, "VectorFillOpacityBox");
            colour.Text = "#00FF00"; opacity.Value = .5m; Find<ComboBox>(window, "VectorFillRuleBox").SelectedItem = DocumentVectorFillRule.NonZero;
            Find<NumericUpDown>(window, "VectorStrokeWidthBox").Value = 7;
            await Click(window, "ZoomInButton"); Assert.Equal("#00FF00", colour.Text); await Row(window, "Path 2");
            Assert.False(Find<Button>(window, "ApplyVectorFillButton").IsEnabled); Assert.Contains("another path", Find<TextBlock>(window, "VectorFillNotice").Text);
            Assert.Equal("#00FF00", colour.Text); Assert.Equal(.5m, opacity.Value); var before = owner.Document.Serialize();
            await window.DispatchAsync("9to1.Picture.ApplyVectorFill", null, TestContext.Current.CancellationToken); Assert.Equal(before, owner.Document.Serialize());
            await Row(window, "Path 1"); await Click(window, "ApplyVectorFillButton");
            Assert.Equal("#00FF00", Shape(Read(owner, vector.ObjectId)).Paths[0].Fill.Color); Assert.Equal(.5, Shape(Read(owner, vector.ObjectId)).Paths[0].Fill.Opacity);
            Assert.Equal(DocumentVectorFillRule.NonZero, Shape(Read(owner, vector.ObjectId)).Paths[0].FillRule);
            Assert.Equal(7m, Find<NumericUpDown>(window, "VectorStrokeWidthBox").Value);
            colour.Text = "#0000FF"; await Click(window, "UndoButton"); Assert.Equal("#0000FF", colour.Text); Assert.Equal(1m, opacity.Value);
            await Click(window, "ApplyVectorFillButton"); Assert.Equal(1, Shape(Read(owner, vector.ObjectId)).Paths[0].Fill.Opacity);
            Assert.Equal(DocumentVectorFillRule.EvenOdd, Shape(Read(owner, vector.ObjectId)).Paths[0].FillRule);
            await Row(window, "Path 2"); colour.Text = "#123456"; Find<ComboBox>(window, "VectorFillKindBox").SelectedItem = DocumentVectorFillKind.Solid;
            await Click(window, "ApplyVectorFillButton"); Assert.Equal(DocumentVectorFillKind.Solid, Shape(Read(owner, vector.ObjectId)).Paths[1].Fill.Kind);
            colour.Text = "#FFFFFF"; await Row(window, "Path 1"); Assert.False(Find<Button>(window, "ApplyVectorFillButton").IsEnabled);
            await Click(window, "ResetVectorFillButton"); Assert.Equal("#0000FF", colour.Text); Assert.False(Find<Button>(window, "ApplyVectorFillButton").IsEnabled);
            await Row(window, "Path 2"); Find<ComboBox>(window, "VectorFillKindBox").SelectedItem = DocumentVectorFillKind.None;
            await Click(window, "ApplyVectorFillButton"); var expected = Rgba(Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source));
            await Click(window, "SaveButton"); var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            using (var rendered = PictureCropService.Render(cold.Document)) Assert.Equal(expected, Rgba(rendered));
            Assert.Equal(Read(owner, vector.ObjectId).Content.GetRawText(), Read(cold, vector.ObjectId).Content.GetRawText());
            colour.Text = "#ABCDEF"; Install(window, new PictureEditorSession(cold.Document, path));
            Assert.False(Find<Button>(window, "ApplyVectorFillButton").IsEnabled); await Row(window, "Shape: Contours"); await Row(window, "Path 1");
            Assert.Equal("#0000FF", colour.Text);
        }
        catch (Exception failure) { firstFailure = failure; RetainedFailures.Add((window, window.OriginalCommand, failure)); throw; }
        finally { await RetireOriginalAsync(window, firstFailure); }
    }
    private static void Install(MainWindow window, PictureEditorSession owner) => typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [owner, PictureCropService.Render(owner.Document), null, null]);
    private static T Find<T>(MainWindow window, string id) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == id);
    private static Task Row(MainWindow window, string label) => PictureCuiActionFixture.ClickAsync(window, window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label)));
    private static Task Click(MainWindow window, string id) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, id));
    private static async Task RetireOriginalAsync(MainWindow window, Exception? firstFailure)
    {
        if (firstFailure is null)
        {
            window.Close(); var close = window.OriginalClose; Assert.NotNull(close); RetainedOriginals.Add((window, close!));
            try { await close!.ObserveOriginalAsync(window, "Fill original close"); Assert.True(close.IsCompletedSuccessfully); Assert.Same(close, window.OriginalClose); }
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
            try { await original.ObserveOriginalAsync(window, "Failed fill original child"); }
            catch (Exception failure) { var actual = original.Exception ?? failure; RetainedFailures.Add((window, original, actual)); if (!failures.Any(value => ReferenceEquals(value, actual))) failures.Add(actual); }
        }
        if (failures.Count > 1) throw new AggregateException("Fill body and original child retirement failures.", failures);
    }
}
