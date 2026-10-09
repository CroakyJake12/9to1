using System.Reflection;
using System.Runtime.InteropServices;
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

public sealed class PicturePrimitiveTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedUiOwners = [];

    [AvaloniaTheory]
    [InlineData(DocumentVectorPrimitive.Rectangle)]
    [InlineData(DocumentVectorPrimitive.Ellipse)]
    [InlineData(DocumentVectorPrimitive.Polygon)]
    [InlineData(DocumentVectorPrimitive.Star)]
    [InlineData(DocumentVectorPrimitive.Line)]
    [InlineData(DocumentVectorPrimitive.Arrow)]
    public async Task Canonical_primitive_renders_real_pixels_and_retains_editable_identity_and_redo_after_save(DocumentVectorPrimitive primitive)
    {
        var shape = DocumentVectorPrimitives.Create(primitive, 7);
        var vector = new HomeVectorShapeObjectHandler().Project(shape);
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (var inserted = session.AddSharedVector(vector)) AssertBlue(inserted, 50, 50);
        var page = PictureCompositionAdapter.Read(session.Document).Pages[0];
        var placed = page.Objects.Single(item => item.ObjectId == vector.ObjectId);
        var retained = ReadShape(session, vector.ObjectId);
        Assert.Equal(shape.Id, retained.Id);
        Assert.Equal(shape.Paths[0].Subpaths[0].Nodes.Select(node => node.Id), retained.Paths[0].Subpaths[0].Nodes.Select(node => node.Id));
        using (var bitmap = PictureCropService.Render(session.Document))
        {
            if (primitive is DocumentVectorPrimitive.Ellipse or DocumentVectorPrimitive.Line or DocumentVectorPrimitive.Arrow)
                Assert.Equal(0, Pixel(bitmap, 25, 25).A);
            if (primitive == DocumentVectorPrimitive.Rectangle) AssertBlue(bitmap, 25, 25);
            if (primitive == DocumentVectorPrimitive.Line) Assert.Equal(0, Pixel(bitmap, 50, 40).A);
        }
        using (var moved = session.MoveVector(session.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector), new(0, 0, 40, 40)))
        { AssertBlue(moved, 20, 20); Assert.Equal(0, Pixel(moved, 50, 50).A); }
        using (var undone = session.Undo()) AssertBlue(undone, 50, 50);
        var path = NewPath("primitive-" + primitive);
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(session.Document.DocumentId, reopened.Document.DocumentId);
        Assert.True(reopened.CanRedo);
        Assert.Equal(shape.Id, ReadShape(reopened, vector.ObjectId).Id);
        Assert.Equal(placed.LayerId, PictureCompositionAdapter.Read(reopened.Document).Pages[0].Objects.Single(item => item.ObjectId == vector.ObjectId).LayerId);
        using (var redone = reopened.Redo()) { AssertBlue(redone, 20, 20); Assert.Equal(0, Pixel(redone, 50, 50).A); }
        using (var reverted = reopened.Undo()) AssertBlue(reverted, 50, 50);
    }

    [AvaloniaFact]
    public async Task Actual_six_CUI_creation_actions_use_current_layer_point_count_locks_and_same_save_owner()
    {
        var seed = new PictureEditorSession(PictureDocument.Create(100, 100));
        var path = NewPath("native-primitives"); await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture primitive initialization");
            await LoadDocumentAsync(window, path);
            window.Show(); window.UpdateLayout(); var owner = CurrentSession(window);
            Find<TextBox>(window, "LayerNameBox").Text = "Primitives";
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "CreateLayerButton"));
            await PictureCuiActionFixture.ClickAsync(window, Row(window, "Primitives · visible"));
            var graph = PictureCompositionAdapter.Read(owner.Document); var layerId = graph.Pages[0].Layers.Single(layer => layer.Name == "Primitives").LayerId;
            Find<NumericUpDown>(window, "PrimitivePointCountBox").Value = 7;
            foreach (var primitive in Enum.GetValues<DocumentVectorPrimitive>())
            {
                var before = PictureCompositionAdapter.Read(owner.Document).Pages[0].ObjectOrder.ToArray();
                await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "Add" + primitive + "Button"));
                var page = PictureCompositionAdapter.Read(owner.Document).Pages[0];
                var added = page.Objects.Single(item => !before.Contains(item.ObjectId));
                Assert.Equal(layerId, added.LayerId); Assert.Equal(primitive.ToString(), ReadShape(owner, added.ObjectId).Name);
                var nodes = ReadShape(owner, added.ObjectId).Paths[0].Subpaths[0].Nodes;
                if (primitive == DocumentVectorPrimitive.Polygon) Assert.Equal(7, nodes.Count);
                if (primitive == DocumentVectorPrimitive.Star) Assert.Equal(14, nodes.Count);
                AssertBlue(Preview(window), 50, 50);
                await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "UndoButton"));
                Assert.Equal(before, PictureCompositionAdapter.Read(owner.Document).Pages[0].ObjectOrder);
                await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "RedoButton"));
                Assert.Equal(added.ObjectId, PictureCompositionAdapter.Read(owner.Document).Pages[0].ObjectOrder[^1]);
            }
            Assert.Equal(7m, Find<NumericUpDown>(window, "PrimitivePointCountBox").Value);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "ToggleLayerLockButton"));
            foreach (var primitive in Enum.GetValues<DocumentVectorPrimitive>()) Assert.False(Find<Button>(window, "Add" + primitive + "Button").IsEnabled);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "ToggleLayerLockButton"));
            foreach (var primitive in Enum.GetValues<DocumentVectorPrimitive>()) Assert.True(Find<Button>(window, "Add" + primitive + "Button").IsEnabled);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "SaveButton"));
            var reopened = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
            Assert.Same(owner, CurrentSession(window)); Assert.Equal(owner.Document.DocumentId, reopened.DocumentId);
            Assert.Equal(PictureCompositionAdapter.Read(owner.Document).Pages[0].ObjectOrder, PictureCompositionAdapter.Read(reopened).Pages[0].ObjectOrder);
            using var rendered = PictureCropService.Render(reopened); AssertBlue(rendered, 50, 50);
        }
        catch (Exception error) { firstFailure = error; RetainedFailedUiOwners.Add((window, error)); throw; }
        finally
        {
            if (firstFailure is null)
            {
                window.Close(); Assert.NotNull(window.OriginalClose); var close = window.OriginalClose!;
                try { await close.ObserveOriginalAsync(window, "Picture primitive original retirement"); }
                catch (Exception error) { RetainedFailedUiOwners.Add((window, error)); throw; }
                Assert.Same(close, window.OriginalClose); Assert.True(close.IsCompletedSuccessfully);
            }
        }
    }

    private static async Task LoadDocumentAsync(MainWindow window, string path)
    {
        // Explicit picker-free local fixture of the maintained Open branch;
        // actual document service and same SetSession/preview, no Home grant.
        var originalOpen = PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        var document = await originalOpen.ObserveOriginalAsync(window, "Picture fixture document read");
        var owner = new PictureEditorSession(document, path);
        typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [owner, PictureCropService.Render(document), null, null]);
    }
    private static string NewPath(string label) => Path.Combine(Path.GetTempPath(), "picture-" + label + "-" + Guid.NewGuid().ToString("N") + ".picture.json");
    private static DocumentVectorShape ReadShape(PictureEditorSession owner, Guid id)
    {
        var item = PictureCompositionAdapter.Read(owner.Document).Pages[0].Objects.Single(item => item.ObjectId == id);
        return HomeVectorShapeObjectHandler.ReadCanonical(PictureCompositionAdapter.ReadVector(item).Content, id);
    }
    private static T Find<T>(MainWindow window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static Button Row(MainWindow window, string label) => window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label));
    private static PictureEditorSession CurrentSession(MainWindow window) => Assert.IsType<PictureEditorSession>(typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private static void AssertBlue(Bitmap bitmap, int x, int y)
    {
        var p = Pixel(bitmap, x, y); Assert.True(p.R < 20 && p.G is > 90 and < 130 && p.B is > 165 and < 205 && p.A > 220, $"Expected blue pixel, observed {p}.");
    }
    private static (byte R, byte G, byte B, byte A) Pixel(Bitmap bitmap, int x, int y)
    {
        var bytes = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)]; var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), pinned.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4);
            var offset = checked((y * bitmap.PixelSize.Width + x) * 4); return (bytes[offset + 2], bytes[offset + 1], bytes[offset], bytes[offset + 3]);
        }
        finally { pinned.Free(); }
    }
}
