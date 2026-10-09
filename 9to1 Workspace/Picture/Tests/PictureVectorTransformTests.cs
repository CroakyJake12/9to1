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

public sealed class PictureVectorTransformTests
{
    private static readonly List<(object Owner, Task? Original, Exception Failure)> RetainedFailures = [];

    [AvaloniaFact]
    public async Task Asymmetrical_shape_rotation_and_mirroring_change_actual_RGBA_and_keep_same_graph_history_after_cold_reopen()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory("picture-vector-source-").FullName;
        var sourcePath = Path.Combine(sourceDirectory, "original.png");
        using (var source = PictureCropService.Render(PictureDocument.Create(100, 100)))
            using (var originalStream = File.Create(sourcePath)) source.Save(originalStream);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
        var vector = Asymmetrical("Asymmetric"); var session = new PictureEditorSession(new PictureCropService().OpenSource(sourcePath));
        using (session.AddSharedVector(vector)) { }
        using (session.MoveVector(Capture(session, vector.ObjectId), new(0, 0, 100, 100))) { }
        using var initial = PictureCropService.Render(session.Document); var originalPixels = Rgba(initial);
        AssertRed(initial, 10, 60); AssertTransparent(initial, 80, 60);
        var originalSource = session.Document.SourceRevision; var originalSourceObject = PictureCompositionAdapter.SourceObject(PictureCompositionAdapter.Read(session.Document));
        var originalPayload = Read(session, vector.ObjectId);
        using (var rotated = session.RotateVector(Capture(session, vector.ObjectId), 90))
        {
            Assert.Equal(QuarterTurn(originalPixels, 100), Rgba(rotated)); AssertRed(rotated, 40, 10); AssertTransparent(rotated, 10, 60);
        }
        var rotatedPayload = Read(session, vector.ObjectId);
        AssertOnlyField(originalPayload, rotatedPayload, "RotationDegrees", 90);
        using (var mirrored = session.MirrorVector(Capture(session, vector.ObjectId), true))
        {
            // Mirrors act in canonical shape coordinates before rotation.
            Assert.Equal(QuarterTurn(Mirror(originalPixels, 100, true), 100), Rgba(mirrored)); AssertRed(mirrored, 80, 80);
        }
        var finalPayload = Read(session, vector.ObjectId);
        AssertOnlyField(rotatedPayload, finalPayload, "ScaleX", -1);
        using (var undone = session.Undo()) Assert.Equal(QuarterTurn(originalPixels, 100), Rgba(undone));
        using (var redone = session.Redo()) Assert.Equal(QuarterTurn(Mirror(originalPixels, 100, true), 100), Rgba(redone));
        Assert.Equal(originalSource, session.Document.SourceRevision);
        Assert.Equal(JsonSerializer.Serialize(originalSourceObject), JsonSerializer.Serialize(PictureCompositionAdapter.SourceObject(PictureCompositionAdapter.Read(session.Document))));
        var path = NewPath("vector-transform"); await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(session.Document.DocumentId, cold.Document.DocumentId); Assert.Equal(finalPayload.Content.GetRawText(), Read(cold, vector.ObjectId).Content.GetRawText());
        using (var reopened = PictureCropService.Render(cold.Document)) Assert.Equal(QuarterTurn(Mirror(originalPixels, 100, true), 100), Rgba(reopened));
        using (var coldUndo = cold.Undo()) Assert.Equal(QuarterTurn(originalPixels, 100), Rgba(coldUndo));
        using (var coldRedo = cold.Redo()) Assert.Equal(QuarterTurn(Mirror(originalPixels, 100, true), 100), Rgba(coldRedo));
        using var output = cold.MirrorVector(Capture(cold, vector.ObjectId), false);
        Assert.Equal(QuarterTurn(Mirror(Mirror(originalPixels, 100, true), 100, false), 100), Rgba(output)); AssertRed(output, 10, 90);
        var png = Path.Combine(Path.GetDirectoryName(path)!, "vector-export.png"); using (var stream = File.Create(png)) output.Save(stream);
        using var exported = new Bitmap(png); Assert.Equal(Rgba(output), Rgba(exported));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Shared_transform_adapter_changes_only_requested_field_and_retains_pivots_translation_magnitude_and_unknown_JSON()
    {
        var original = Asymmetrical("Retained"); var content = JsonNode.Parse(original.Content.GetRawText())!;
        content["Transform"]!["TranslateX"] = 8.12345678901234; content["Transform"]!["TranslateY"] = -9.3210987654321;
        content["Transform"]!["OriginX"] = .12345678901234; content["Transform"]!["OriginY"] = 1.65432109876543;
        content["Transform"]!["ScaleX"] = 2.25; content["Transform"]!["ScaleY"] = -.375;
        content["Transform"]!["RotationDegrees"] = 360.1234567890123;
        original = original with { Content = JsonSerializer.SerializeToElement(content) };
        var rotated = OriginalTransform.Rotate(original, -180); AssertOnlyField(original, rotated, "RotationDegrees", 180);
        var horizontal = OriginalTransform.Mirror(original, true); AssertOnlyField(original, horizontal, "ScaleX", -2.25);
        var vertical = OriginalTransform.Mirror(original, false); AssertOnlyField(original, vertical, "ScaleY", .375);
        AssertOnlyField(horizontal, OriginalTransform.Mirror(horizontal, true), "ScaleX", 2.25);
        // Large imported values remain untouched by rotation; mirror refuses
        // the shared editor’s size-changing clamp before any owner mutation.
        content["Transform"]!["ScaleX"] = 1001; content["Transform"]!["OriginX"] = 1001;
        original = original with { Content = JsonSerializer.SerializeToElement(content) };
        AssertOnlyField(original, OriginalTransform.Rotate(original, 45), "RotationDegrees", 45);
        Assert.Throws<NotSupportedException>(() => OriginalTransform.Mirror(original, true));
        foreach (var angle in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -180.001, 180.001 })
            Assert.Throws<ArgumentOutOfRangeException>(() => OriginalTransform.Rotate(original, angle));
    }

    [AvaloniaFact]
    public void Stale_foreign_wrong_kind_and_locked_original_targets_refuse_without_changing_document_or_unknown_other_graphs()
    {
        var vector = Asymmetrical("Chosen"); var other = Asymmetrical("Other");
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (session.AddSharedVector(vector)) { } using (session.AddSharedVector(other)) { }
        var otherPayload = Read(session, other.ObjectId).Content.GetRawText();
        var stale = Capture(session, vector.ObjectId);
        var foreign = Capture(new PictureEditorSession(session.Document), vector.ObjectId);
        using (session.RotateVector(stale, 90)) { }
        var before = session.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => session.RotateVector(stale, 45));
        Assert.Throws<InvalidOperationException>(() => session.MirrorVector(foreign, true));
        var page = PictureCompositionAdapter.Read(session.Document).Pages[0];
        var layerId = page.Objects.Single(item => item.ObjectId == vector.ObjectId).LayerId;
        Assert.Throws<InvalidOperationException>(() => session.RotateVector(session.CaptureComposition(layerId, PictureCompositionTargetKind.Layer), 45));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.RotateVector(Capture(session, vector.ObjectId), double.NaN));
        Assert.Equal(before, session.Document.Serialize());
        using (session.SetLayerLocked(session.CaptureComposition(layerId, PictureCompositionTargetKind.Layer), true)) { }
        before = session.Document.Serialize();
        var locked = Capture(session, vector.ObjectId);
        Assert.Contains("PermissionDenied", Assert.Throws<InvalidOperationException>(() => session.RotateVector(locked, 45)).Message);
        Assert.Contains("PermissionDenied", Assert.Throws<InvalidOperationException>(() => session.MirrorVector(locked, false)).Message);
        Assert.Equal(before, session.Document.Serialize()); Assert.Equal(otherPayload, Read(session, other.ObjectId).Content.GetRawText());
    }

    [AvaloniaFact]
    public async Task Genuine_CUI_angle_draft_stays_bound_to_original_shape_through_selection_mirror_undo_save_and_confirmed_replacement()
    {
        var first = Asymmetrical("First" ); var second = Asymmetrical("Second");
        var seed = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (seed.AddSharedVector(first)) { } using (seed.MoveVector(Capture(seed, first.ObjectId), new(0, 0, 100, 100))) { }
        using (seed.AddSharedVector(second)) { }
        var path = NewPath("native-vector-transform"); await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Vector transform initialization");
            var opened = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
            var session = new PictureEditorSession(opened, path); Install(window, session); window.Show(); window.UpdateLayout();
            await Choose(window, "First");
            var angle = Find<NumericUpDown>(window, "VectorRotationBox"); angle.Value = 90;
            Find<NumericUpDown>(window, "VectorNodeXBox").Value = 123;
            await Click(window, "ZoomInButton"); Assert.Equal(90m, angle.Value);
            await Choose(window, "Second"); Assert.Equal(90m, angle.Value);
            Assert.False(Find<Button>(window, "ApplyVectorRotationButton").IsEnabled);
            Assert.True(Find<Button>(window, "ResetVectorRotationButton").IsEnabled);
            Assert.Contains("another shape", Find<TextBlock>(window, "VectorTransformNotice").Text);
            // The disabled positive button remains disabled; this negative API
            // request verifies the same product draft guard before any edit.
            var unchanged = session.Document.Serialize();
            await window.DispatchAsync("9to1.Picture.RotateVector", null, TestContext.Current.CancellationToken);
            Assert.Equal(unchanged, session.Document.Serialize()); Assert.Equal(0, Shape(Read(session, second.ObjectId)).Transform.RotationDegrees);
            await Click(window, "MirrorVectorHorizontalButton"); Assert.Equal(-1, Shape(Read(session, second.ObjectId)).Transform.ScaleX);
            Assert.Equal(90m, angle.Value); Assert.False(Find<Button>(window, "ApplyVectorRotationButton").IsEnabled);
            await Choose(window, "First"); Assert.True(Find<Button>(window, "ApplyVectorRotationButton").IsEnabled);
            await Click(window, "ApplyVectorRotationButton"); Assert.Equal(90, Shape(Read(session, first.ObjectId)).Transform.RotationDegrees);
            Assert.Equal(123m, Find<NumericUpDown>(window, "VectorNodeXBox").Value);
            angle.Value = 45; await Click(window, "MirrorVectorVerticalButton");
            Assert.Equal(45m, angle.Value); Assert.Equal(-1, Shape(Read(session, first.ObjectId)).Transform.ScaleY);
            await Click(window, "UndoButton"); Assert.Equal(45m, angle.Value); Assert.Equal(1, Shape(Read(session, first.ObjectId)).Transform.ScaleY);
            await Choose(window, "Second"); await Click(window, "ResetVectorRotationButton"); Assert.Equal(0m, angle.Value);
            angle.Value = -90; await Click(window, "ApplyVectorRotationButton");
            Assert.Equal(-90, Shape(Read(session, second.ObjectId)).Transform.RotationDegrees);
            Assert.Equal(90, Shape(Read(session, first.ObjectId)).Transform.RotationDegrees);
            var expected = Rgba(Preview(window)); await Click(window, "SaveButton");
            var cold = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
            using (var rendered = PictureCropService.Render(cold.Document)) Assert.Equal(expected, Rgba(rendered));
            Assert.Equal(Read(session, first.ObjectId).Content.GetRawText(), Read(cold, first.ObjectId).Content.GetRawText());
            angle.Value = 77; Install(window, new PictureEditorSession(cold.Document, path));
            Assert.Equal(0m, angle.Value); Assert.False(Find<Button>(window, "ApplyVectorRotationButton").IsEnabled);
            await Choose(window, "First"); Assert.Equal(90m, angle.Value);
        }
        catch (Exception failure) { firstFailure = failure; RetainedFailures.Add((window, window.OriginalCommand, failure)); throw; }
        finally
        {
            if (firstFailure is null)
            {
                window.Close(); Assert.NotNull(window.OriginalClose); var close = window.OriginalClose!;
                try { await close.ObserveOriginalAsync(window, "Vector transform original retirement"); Assert.True(close.IsCompletedSuccessfully); Assert.Same(close, window.OriginalClose); }
                catch (Exception failure) { RetainedFailures.Add((window, close, close.Exception ?? failure)); throw; }
            }
            else
            {
                // A failed dirty UI stays strongly held. Independently settle
                // accepted children without opening a Save confirmation popup.
                var children = new List<Task>(); var failures = new List<Exception> { firstFailure };
                if (window.OriginalCommand is { } command) children.Add(command);
                if (window.OriginalInitialization is { } initialization) children.Add(initialization);
                try { children.Add(window.SceneHost.CloseOriginalAsync()); }
                catch (Exception failure) { RetainedFailures.Add((window.SceneHost, null, failure)); failures.Add(failure); }
                foreach (var sameChild in children.Distinct<Task>(ReferenceEqualityComparer.Instance))
                {
                    RetainedFailures.Add((window, sameChild, firstFailure));
                    try { await sameChild.ObserveOriginalAsync(window, "Failed vector transform original child"); }
                    catch (Exception failure)
                    {
                        var actual = sameChild.Exception ?? failure; RetainedFailures.Add((window, sameChild, actual));
                        if (!failures.Any(value => ReferenceEquals(value, actual))) failures.Add(actual);
                    }
                }
                if (failures.Count > 1) throw new AggregateException("Vector transform body and original retirement failures.", failures);
            }
        }
    }

    private static class OriginalTransform
    {
        private static readonly Type Adapter = typeof(PictureEditorSession).Assembly
            .GetType("HavenOS.Images.PictureVectorTransformEditor", throwOnError: true)!;
        private static readonly MethodInfo RotateMethod = Adapter.GetMethod("Rotate", BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: [typeof(HomeProductivityObject), typeof(double)], modifiers: null)!;
        private static readonly MethodInfo MirrorMethod = Adapter.GetMethod("Mirror", BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: [typeof(HomeProductivityObject), typeof(bool)], modifiers: null)!;
        internal static HomeProductivityObject Rotate(HomeProductivityObject original, double angle) =>
            (HomeProductivityObject)InvokeOriginal(() => RotateMethod.Invoke(null, [original, angle]))!;
        internal static HomeProductivityObject Mirror(HomeProductivityObject original, bool horizontal) =>
            (HomeProductivityObject)InvokeOriginal(() => MirrorMethod.Invoke(null, [original, horizontal]))!;
    }
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

    private static HomeProductivityObject Asymmetrical(string name)
    {
        var shape = new DocumentVectorShape { Name = name, ViewBox = new() { Width = 100, Height = 100 }, Paths =
            [new() { Fill = new() { Kind = DocumentVectorFillKind.Solid, Color = "#FF0000" }, Stroke = new() { Enabled = false }, Subpaths =
                [new() { Closed = true, Nodes = [new() { X = 0, Y = 0 }, new() { X = 40, Y = 0 }, new() { X = 40, Y = 80 }, new() { X = 0, Y = 80 }] }] }] };
        var original = new HomeVectorShapeObjectHandler().Project(shape);
        var content = JsonNode.Parse(original.Content.GetRawText())!;
        content["FutureShape"] = new JsonObject { ["retained"] = 47 }; content["Transform"]!["FuturePivot"] = "retain";
        content["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!["FutureNode"] = "preserve";
        return original with { Content = JsonSerializer.SerializeToElement(content) };
    }
    private static void AssertOnlyField(HomeProductivityObject original, HomeProductivityObject updated, string field, double value)
    {
        var expected = JsonNode.Parse(original.Content.GetRawText())!; expected["Transform"]![field] = value;
        Assert.Equal(original.ObjectId, updated.ObjectId); Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(updated.Content.GetRawText())));
        Assert.Equal(JsonSerializer.Serialize(original.Layout), JsonSerializer.Serialize(updated.Layout));
    }
    private static DocumentVectorShape Shape(HomeProductivityObject original) => HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
    private static HomeProductivityObject Read(PictureEditorSession owner, Guid id) => PictureCompositionAdapter.ReadVector(PictureCompositionAdapter.Read(owner.Document).Pages[0].Objects.Single(item => item.ObjectId == id));
    private static PictureCompositionTarget Capture(PictureEditorSession owner, Guid id) => owner.CaptureComposition(id, PictureCompositionTargetKind.Vector);
    private static string NewPath(string name) => Path.Combine(Directory.CreateTempSubdirectory("picture-" + name + "-").FullName, "editable.picture.json");
    private static void Install(MainWindow window, PictureEditorSession session) => typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [session, PictureCropService.Render(session.Document), null, null]);
    private static Task Choose(MainWindow window, string shape) => PictureCuiActionFixture.ClickAsync(window,
        window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Shape: " + shape)));
    private static Task Click(MainWindow window, string id) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, id));
    private static T Find<T>(MainWindow window, string id) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == id);
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private static void AssertRed(Bitmap bitmap, int x, int y) => Assert.Equal(new byte[] { 255, 0, 0, 255 }, Rgba(bitmap).AsSpan((y * bitmap.PixelSize.Width + x) * 4, 4).ToArray());
    private static void AssertTransparent(Bitmap bitmap, int x, int y) => Assert.Equal(new byte[4], Rgba(bitmap).AsSpan((y * bitmap.PixelSize.Width + x) * 4, 4).ToArray());
    private static byte[] Rgba(Bitmap source)
    {
        using var converted = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = converted.Lock(); source.CopyPixels(buffer); var bytes = new byte[source.PixelSize.Width * source.PixelSize.Height * 4];
        for (var y = 0; y < source.PixelSize.Height; y++) Marshal.Copy(IntPtr.Add(buffer.Address, y * buffer.RowBytes), bytes, y * source.PixelSize.Width * 4, source.PixelSize.Width * 4);
        return bytes;
    }
    private static byte[] QuarterTurn(byte[] pixels, int size)
    {
        var result = new byte[pixels.Length]; for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            pixels.AsSpan((y * size + x) * 4, 4).CopyTo(result.AsSpan((x * size + size - 1 - y) * 4, 4));
        return result;
    }
    private static byte[] Mirror(byte[] pixels, int size, bool horizontal)
    {
        var result = new byte[pixels.Length]; for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            pixels.AsSpan((y * size + x) * 4, 4).CopyTo(result.AsSpan(((horizontal ? y : size - 1 - y) * size + (horizontal ? size - 1 - x : x)) * 4, 4));
        return result;
    }
}
