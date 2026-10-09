using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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

public sealed class PictureHistoryTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedUiOwners = [];

    [AvaloniaFact]
    public async Task Mixed_raster_vector_undo_and_redo_survive_save_reopen_with_original_metadata_and_pixels()
    {
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        var vector = Rectangle();
        using (session.AddSharedVector(vector)) { }
        var insert = session.LastOperation;
        using (session.Apply("Crop composition", document => document.Crop(20, 20, 60, 60))) { }
        var crop = session.LastOperation;
        var layerId = Layer(session, vector.ObjectId);
        using (var hidden = session.SetLayerVisibility(session.CaptureComposition(layerId, PictureCompositionTargetKind.Layer), false))
            Assert.Equal(0, Pixel(hidden, 30, 30).A);
        var visibility = session.LastOperation;
        using (var undone = session.Undo()) AssertRed(undone, 30, 30);
        var originalUndo = session.LastOperation;
        var path = NewPath("mixed-history");
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var saved = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { insert, crop }, saved.SemanticHistory!.Undo.Select(frame => frame.Operation));
        Assert.Equal(visibility, Assert.Single(saved.SemanticHistory.Redo).Operation);
        foreach (var frame in saved.SemanticHistory.Undo.Concat(saved.SemanticHistory.Redo))
        {
            using var payload = JsonDocument.Parse(Convert.FromBase64String(frame.PayloadBase64));
            Assert.False(payload.RootElement.TryGetProperty("semanticHistory", out _));
            Assert.Equal(saved.DocumentId, payload.RootElement.GetProperty("documentId").GetGuid());
        }
        var reopened = new PictureEditorSession(saved, path);
        Assert.True(reopened.CanUndo); Assert.True(reopened.CanRedo);
        Assert.Equal(originalUndo, reopened.LastOperation);
        Assert.Equal(session.Document.DocumentId, reopened.Document.DocumentId);
        using (var redone = reopened.Redo()) { Assert.Equal(new PixelSize(60, 60), redone.PixelSize); Assert.Equal(0, Pixel(redone, 30, 30).A); }
        using (var undoVisibility = reopened.Undo()) AssertRed(undoVisibility, 30, 30);
        using (var undoCrop = reopened.Undo()) { Assert.Equal(new PixelSize(100, 100), undoCrop.PixelSize); AssertRed(undoCrop, 50, 50); }
        using (var undoInsert = reopened.Undo()) { Assert.Equal(0, Pixel(undoInsert, 50, 50).A); Assert.Null(reopened.Document.CompositionState); }
        Assert.False(reopened.CanUndo);
        using (reopened.Redo()) { }
        using (reopened.Redo()) { }
        using (var final = reopened.Redo()) Assert.Equal(0, Pixel(final, 30, 30).A);
        Assert.Equal(layerId, Layer(reopened, vector.ObjectId));
        Assert.Equal(new[] { insert, crop, visibility }, reopened.CaptureDocumentForSave().SemanticHistory!.Undo.Select(frame => frame.Operation));
    }

    [AvaloniaFact]
    public async Task Retained_node_redo_uses_same_shared_history_and_requires_a_fresh_recovered_owner_selection()
    {
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        var vector = Rectangle();
        var content = JsonNode.Parse(vector.Content.GetRawText())!;
        content["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![1]!["FutureNode"] = "retained";
        vector = vector with { Content = JsonSerializer.SerializeToElement(content) };
        using (session.AddSharedVector(vector)) { }
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(vector.Content, vector.ObjectId);
        var node = shape.Paths[0].Subpaths[0].Nodes[1];
        var originalTarget = session.CaptureVectorNode(session.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector),
            shape.Paths[0].Id, shape.Paths[0].Subpaths[0].Id, node.Id);
        using (var moved = session.MoveVectorNode(originalTarget, 50, 0)) Assert.Equal(0, Pixel(moved, 75, 30).A);
        var operation = session.LastOperation;
        using (var undone = session.Undo()) AssertRed(undone, 75, 30);
        var path = NewPath("node-history");
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        var before = reopened.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => reopened.MoveVectorNode(originalTarget, 70, 0));
        Assert.Equal(before, reopened.Document.Serialize());
        Assert.Equal(operation, Assert.Single(reopened.CaptureDocumentForSave().SemanticHistory!.Redo).Operation);
        using (var redone = reopened.Redo()) Assert.Equal(0, Pixel(redone, 75, 30).A);
        var retained = Vector(reopened, vector.ObjectId);
        Assert.Equal("retained", retained.Content.GetProperty("Paths")[0].GetProperty("Subpaths")[0].GetProperty("Nodes")[1].GetProperty("FutureNode").GetString());
        var fresh = reopened.CaptureVectorNode(reopened.CaptureComposition(vector.ObjectId, PictureCompositionTargetKind.Vector),
            shape.Paths[0].Id, shape.Paths[0].Subpaths[0].Id, node.Id);
        using var current = reopened.MoveVectorNode(fresh, 100, 0);
        AssertRed(current, 75, 30);
        Assert.Equal(node.Id, HomeVectorShapeObjectHandler.ReadCanonical(Vector(reopened, vector.ObjectId).Content, vector.ObjectId).Paths[0].Subpaths[0].Nodes[1].Id);
    }

    [AvaloniaFact]
    public void Valid_digest_cannot_authorize_foreign_source_nested_history_or_invalid_retained_dependencies()
    {
        var session = new PictureEditorSession(PictureDocument.Create(100, 100));
        using (session.Apply("Crop", document => document.Crop(10, 10, 80, 80))) { }
        var snapshot = session.CaptureDocumentForSave().Serialize();
        void Reject(Action<JsonNode> change)
        {
            var root = JsonNode.Parse(snapshot)!;
            var frame = root["semanticHistory"]!["undo"]![0]!;
            var payload = JsonNode.Parse(Convert.FromBase64String(frame["payloadBase64"]!.GetValue<string>()))!;
            change(payload);
            var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            frame["payloadBase64"] = Convert.ToBase64String(bytes); frame["contentHash"] = Convert.ToHexString(SHA256.HashData(bytes));
            Assert.Throws<InvalidDataException>(() => PictureDocument.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        }
        Reject(payload => payload["fileId"] = "another-files-identity");
        Reject(payload => payload["sourcePath"] = "/never-read-this-foreign-source");
        Reject(payload => payload["documentId"] = Guid.NewGuid());
        Reject(payload => payload["canvasWidth"] = 99); // No operations: expected original100, not99.
        Reject(payload => payload["semanticHistory"] = new JsonObject());
        Assert.Equal(snapshot, session.CaptureDocumentForSave().Serialize());
        Assert.True(session.CanUndo);
        using var original = session.Undo(); Assert.Equal(new PixelSize(100, 100), original.PixelSize);
    }

    [AvaloniaFact]
    public async Task Save_copy_starts_a_new_owner_and_preserves_original_session_asset_and_full_history()
    {
        var session = new PictureEditorSession(PictureDocument.Create(100, 100, fileId: "local-original-fixture"));
        var vector = Rectangle();
        using (session.AddSharedVector(vector)) { }
        using (session.Apply("Crop", document => document.Crop(20, 20, 60, 60))) { }
        var originalPath = NewPath("original-history");
        await session.SaveAsync(originalPath, cancellationToken: TestContext.Current.CancellationToken);
        var originalBytes = await File.ReadAllBytesAsync(originalPath, TestContext.Current.CancellationToken);
        var originalId = session.Document.DocumentId;
        var originalHistory = session.CaptureDocumentForSave().SemanticHistory;
        var copyPath = NewPath("copied-history");
        await session.SaveAsync(copyPath, saveCopy: true, TestContext.Current.CancellationToken);
        var copied = await PictureDocument.OpenAsync(copyPath, TestContext.Current.CancellationToken);
        Assert.NotEqual(originalId, copied.DocumentId); Assert.Equal(0, copied.Revision);
        Assert.Null(copied.FileId); Assert.Null(copied.SemanticHistory);
        Assert.Equal(copied.DocumentId, PictureCompositionAdapter.Read(copied).ArtifactId);
        Assert.Null(PictureCompositionAdapter.Read(copied).SemanticHistory);
        Assert.Equal(originalId, session.Document.DocumentId);
        Assert.Equal(originalHistory!.Undo.Select(frame => frame.Operation), session.CaptureDocumentForSave().SemanticHistory!.Undo.Select(frame => frame.Operation));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(originalPath, TestContext.Current.CancellationToken));
        using var rendered = PictureCropService.Render(copied); AssertRed(rendered, 30, 30);
        var original = new PictureEditorSession(await PictureDocument.OpenAsync(originalPath, TestContext.Current.CancellationToken), originalPath);
        using (original.Undo()) { }
        using (var prior = original.Undo()) Assert.Equal(0, Pixel(prior, 50, 50).A);
    }

    [AvaloniaFact]
    public async Task Bounded_history_reports_discarded_entries_and_retains_the_nearest_128_actual_undo_steps()
    {
        var session = new PictureEditorSession(PictureDocument.Create(4, 4));
        for (var index = 0; index < ProductivitySnapshotHistory.MaximumEntries + 2; index++)
            using (session.Apply("Flip " + index, document => document.Flip(true))) { }
        Assert.Equal(2, session.DiscardedEarlierHistoryEntries);
        var path = NewPath("bounded-history");
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var saved = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(2, saved.SemanticHistory!.DiscardedEarlierEntries);
        Assert.Equal(ProductivitySnapshotHistory.MaximumEntries, saved.SemanticHistory.Undo.Count);
        var restored = new PictureEditorSession(saved, path);
        Assert.Equal(2, restored.DiscardedEarlierHistoryEntries);
        for (var index = 0; index < ProductivitySnapshotHistory.MaximumEntries; index++)
            using (var undone = restored.Undo()) Assert.Equal(new PixelSize(4, 4), undone.PixelSize);
        Assert.False(restored.CanUndo); Assert.True(restored.CanRedo);
        Assert.Equal(2, restored.Document.Operations.Count);
        Assert.Equal(session.Document.DocumentId, restored.Document.DocumentId);
    }

    [AvaloniaFact]
    public async Task Actual_CUI_undo_save_close_reopen_and_redo_restore_same_document_history_and_pixels()
    {
        var seed = new PictureEditorSession(PictureDocument.Create(100, 100));
        var vector = Rectangle();
        using (seed.AddSharedVector(vector)) { }
        using (seed.Apply("Crop", document => document.Crop(20, 20, 60, 60))) { }
        using (seed.SetLayerVisibility(seed.CaptureComposition(Layer(seed, vector.ObjectId), PictureCompositionTargetKind.Layer), false)) { }
        var path = NewPath("native-history");
        await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        MainWindow? window = null;
        try
        {
            window = new MainWindow(new PictureFixtureReadiness());
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture history initialization");
            await LoadDocumentAsync(window, path);
            window.Show(); window.UpdateLayout();
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "UndoButton"));
            AssertRed(Preview(window), 30, 30);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "SaveButton"));
            var firstOwner = CurrentSession(window); var id = firstOwner.Document.DocumentId;
            await Close(window);
            window = new MainWindow(new PictureFixtureReadiness());
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture recovered history initialization");
            await LoadDocumentAsync(window, path);
            window.Show(); window.UpdateLayout();
            var recovered = CurrentSession(window);
            Assert.NotSame(firstOwner, recovered); Assert.Equal(id, recovered.Document.DocumentId);
            Assert.True(Find<Button>(window, "RedoButton").IsEnabled);
            Assert.Contains("Undo Change layer visibility", Find<TextBlock>(window, "HistoryStateText").Text);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "RedoButton"));
            Assert.Equal(0, Pixel(Preview(window), 30, 30).A);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "UndoButton"));
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "UndoButton"));
            Assert.Equal(new PixelSize(100, 100), Preview(window).PixelSize); AssertRed(Preview(window), 50, 50);
            await PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, "SaveButton"));
            await Close(window); window = null;
        }
        catch (Exception error) { if (window is not null) RetainedFailedUiOwners.Add((window, error)); throw; }
    }

    private static async Task Close(MainWindow window)
    {
        window.Close(); Assert.NotNull(window.OriginalClose);
        var original = window.OriginalClose!;
        await original.ObserveOriginalAsync(window, "Picture history original retirement");
        Assert.Same(original, window.OriginalClose); Assert.True(original.IsCompletedSuccessfully);
    }
    private static HomeProductivityObject Rectangle() => new HomeVectorShapeObjectHandler().Project(new DocumentVectorShape
    {
        Name = "Red", ViewBox = new() { Width = 100, Height = 100 }, Paths = [new()
        { Fill = new() { Kind = DocumentVectorFillKind.Solid, Color = "#FF0000" }, Stroke = new() { Enabled = false }, Subpaths =
            [new() { Closed = true, Nodes = [new() { X = 0, Y = 0 }, new() { X = 100, Y = 0 }, new() { X = 100, Y = 100 }, new() { X = 0, Y = 100 }] }] }]
    });
    private static HomeProductivityObject Vector(PictureEditorSession session, Guid id) => PictureCompositionAdapter.ReadVector(PictureCompositionAdapter.Read(session.Document).Pages[0].Objects.Single(item => item.ObjectId == id));
    private static Guid Layer(PictureEditorSession session, Guid id) => PictureCompositionAdapter.Read(session.Document).Pages[0].Objects.Single(item => item.ObjectId == id).LayerId;
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
    private static T Find<T>(MainWindow window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
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
