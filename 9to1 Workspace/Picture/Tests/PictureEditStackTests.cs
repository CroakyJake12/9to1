using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureEditStackTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedUiOwners = [];

    [AvaloniaFact]
    public async Task Modifying_an_existing_flip_uses_shared_undo_redo_and_reopens_with_actual_source_pixels()
    {
        var (directory, source, bytes) = await CreateSourceAsync();
        var session = new PictureEditorSession(new PictureCropService().OpenSource(source));
        var identity = session.Document.DocumentId;
        var sourceRevision = session.Document.SourceRevision;
        using (var flipped = session.Apply("Flip", document => document.Flip(true))) AssertGreen(flipped);
        using (var modified = session.ReplaceOperation(session.CaptureOperation(0), new FlipOperation(false))) AssertRed(modified);
        using (var undone = session.Undo()) AssertGreen(undone);
        using (var redone = session.Redo()) AssertRed(redone);
        var path = Path.Combine(directory, "modified.picture.json");
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(identity, reopened.Document.DocumentId);
        Assert.Equal(sourceRevision, reopened.Document.SourceRevision);
        Assert.Equal(new FlipOperation(false), Assert.Single(reopened.Document.Operations));
        using (var rendered = PictureCropService.Render(reopened.Document)) AssertRed(rendered);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
        Assert.Null(reopened.Document.FileId); // A local fixture never fabricates Files identity.
    }

    [AvaloniaFact]
    public async Task Reordering_and_removing_edits_changes_real_pixels_and_preserves_shared_history()
    {
        var (_, source, bytes) = await CreateSourceAsync();
        var session = new PictureEditorSession(new PictureCropService().OpenSource(source));
        using (var cropped = session.Apply("Crop", document => document.Crop(0, 0, 1, 1))) AssertRed(cropped);
        using (var flipped = session.Apply("Flip", document => document.Flip(true))) AssertRed(flipped);
        using (var moved = session.MoveOperation(session.CaptureOperation(1), 0)) AssertGreen(moved);
        Assert.IsType<FlipOperation>(session.Document.Operations[0]);
        using (var undone = session.Undo()) AssertRed(undone);
        using (var removed = session.RemoveOperation(session.CaptureOperation(1))) AssertRed(removed);
        Assert.IsType<CropOperation>(Assert.Single(session.Document.Operations));
        Assert.False(session.CanRedo); // A real edit branches the canonical shared history.
        using (var undone = session.Undo()) Assert.Equal(2, session.Document.Operations.Count);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Invalid_dependencies_do_not_mutate_the_session_or_invalidate_its_same_current_selection()
    {
        var session = new PictureEditorSession(PictureDocument.Create(100, 80));
        using (session.Apply("Resize", document => document.Resize(400, 300))) { }
        using (session.Apply("Crop", document => document.Crop(300, 200, 80, 70))) { }
        var resize = session.CaptureOperation(0);
        var crop = session.CaptureOperation(1);
        var before = session.Document.Serialize();
        var action = session.LastAction;
        Assert.Throws<ArgumentOutOfRangeException>(() => session.ReplaceOperation(resize, new ResizeOperation(200, 100)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.MoveOperation(crop, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.RemoveOperation(resize));
        Assert.Equal(before, session.Document.Serialize());
        Assert.Equal(action, session.LastAction);
        Assert.Same(resize.Operation, session.CaptureOperation(0).Operation);
        using var valid = session.ReplaceOperation(resize, new ResizeOperation(500, 400));
        Assert.Equal(new PixelSize(80, 70), valid.PixelSize);
    }

    [AvaloniaFact]
    public void A_foreign_or_stale_selection_cannot_edit_a_same_named_same_identity_document()
    {
        var document = PictureDocument.Create(20, 10).Flip(true);
        var session = new PictureEditorSession(document);
        var other = new PictureEditorSession(document);
        Assert.Equal(session.Document.DocumentId, other.Document.DocumentId);
        var foreign = other.CaptureOperation(0);
        var before = session.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => session.ReplaceOperation(foreign, new FlipOperation(false)));
        Assert.Equal(before, session.Document.Serialize());
        var stale = session.CaptureOperation(0);
        using (session.Apply("Rotate", current => current.Rotate())) { }
        before = session.Document.Serialize();
        Assert.Throws<InvalidOperationException>(() => session.RemoveOperation(stale));
        Assert.Equal(before, session.Document.Serialize());
        using (session.Undo()) { }
        // Undo returns similar content, but it does not restore a stale revision selection.
        Assert.Throws<InvalidOperationException>(() => session.MoveOperation(stale, 0));
    }

    [AvaloniaFact]
    public async Task A_resize_that_becomes_a_no_op_is_retained_after_save_reopen_and_shared_undo()
    {
        var path = Path.Combine(Path.GetTempPath(), "picture-explicit-resize-" + Guid.NewGuid().ToString("N") + ".picture.json");
        var session = new PictureEditorSession(PictureDocument.Create(20, 10));
        using (session.Apply("Resize up", document => document.Resize(40, 20))) { }
        using (session.Apply("Resize back", document => document.Resize(20, 10))) { }
        using (session.RemoveOperation(session.CaptureOperation(0))) { }
        Assert.Equal(new ResizeOperation(20, 10), Assert.Single(session.Document.Operations));
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var reopened = new PictureEditorSession(await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken), path);
        Assert.Equal(new ResizeOperation(20, 10), Assert.Single(reopened.Document.Operations));
        Assert.True(reopened.CanUndo);
        using (var undone = reopened.Undo()) Assert.Equal(new PixelSize(20, 10), undone.PixelSize);
        Assert.Empty(reopened.Document.Operations);
        using (var redone = reopened.Redo()) Assert.Equal(new PixelSize(20, 10), redone.PixelSize);
        Assert.Equal(new ResizeOperation(20, 10), Assert.Single(reopened.Document.Operations));
    }

    [AvaloniaFact]
    public async Task Replacing_equal_values_and_moving_to_the_same_position_does_not_invent_an_edit()
    {
        var path = Path.Combine(Path.GetTempPath(), "picture-unchanged-edit-" + Guid.NewGuid().ToString("N") + ".picture.json");
        var session = new PictureEditorSession(PictureDocument.Create(20, 10).Flip(true));
        await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var before = session.Document.Serialize();
        var action = session.LastAction;
        var target = session.CaptureOperation(0);
        using (session.ReplaceOperation(target, new FlipOperation(true))) { }
        using (session.MoveOperation(target, 0)) { }
        Assert.Equal(before, session.Document.Serialize());
        Assert.Equal(action, session.LastAction);
        Assert.False(session.IsDirty);
    }

    [AvaloniaFact]
    public async Task Actual_CUI_row_selection_modifies_and_removes_the_same_session_and_saves_its_graph()
    {
        var window = new MainWindow(new PictureFixtureReadiness());
        Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture initialization");
            window.Show();
            Find<NumericUpDown>(window, "WidthBox").Value = 40;
            Find<NumericUpDown>(window, "HeightBox").Value = 20;
            await ClickAsync(window, Find<Button>(window, "NewButton"));
            var session = CurrentSession(window);
            var id = session.Document.DocumentId;
            await ClickAsync(window, Find<Button>(window, "RotateRightButton"));
            var row = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "1. Rotate 90°"));
            Assert.True(row.IsEnabled);
            await ClickAsync(window, row); // Actual Repeat row + typed source-issued parameter.
            Find<NumericUpDown>(window, "SelectedRotationTurns").Value = 2;
            await ClickAsync(window, Find<Button>(window, "ReplaceSelectedEditButton"));
            Assert.Same(session, CurrentSession(window));
            Assert.Equal(new RotateOperation(2), Assert.Single(session.Document.Operations));
            Assert.Equal((40, 20), (session.Document.CanvasWidth, session.Document.CanvasHeight));
            row = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "1. Rotate 180°"));
            await ClickAsync(window, row);
            await ClickAsync(window, Find<Button>(window, "RemoveSelectedEditButton"));
            Assert.Empty(session.Document.Operations);
            await ClickAsync(window, Find<Button>(window, "UndoButton"));
            Assert.Equal(new RotateOperation(2), Assert.Single(session.Document.Operations));
            var path = Path.Combine(Path.GetTempPath(), "picture-cui-edit-stack-" + Guid.NewGuid().ToString("N") + ".picture.json");
            await session.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            var reopened = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(id, reopened.DocumentId);
            Assert.Equal(session.Document.Operations, reopened.Operations);
        }
        catch (Exception error)
        {
            firstFailure = error;
            RetainedFailedUiOwners.Add((window, error));
            throw;
        }
        finally
        {
            // A failed body may leave unsaved state. Keep its actual window and
            // first failure instead of entering an unanswerable fixture save modal.
            if (firstFailure is null)
            {
                window.Close();
                Assert.NotNull(window.OriginalClose);
                var originalClose = window.OriginalClose!;
                await originalClose.ObserveOriginalAsync(window, "Picture original window retirement");
                Assert.True(originalClose.IsCompletedSuccessfully);
                Assert.Same(originalClose, window.OriginalClose);
            }
        }
    }

    [AvaloniaFact]
    public Task Entered_resize_survives_unrelated_CUI_actions_and_changes_actual_dimensions_and_pixels() =>
        WithSourceWindowAsync(async (window, session) =>
        {
            var width = Find<NumericUpDown>(window, "WidthBox");
            var height = Find<NumericUpDown>(window, "HeightBox");
            var crop = Find<TextBox>(window, "CropBoundsBox");
            width.Value = 4; // Actual aspect-lock handler produces height2.
            crop.Text = "1, 0, 1, 1";
            await ClickAsync(window, Find<Button>(window, "MetadataRemoveAllButton"));
            await ClickAsync(window, Find<Button>(window, "ZoomInButton"));
            Assert.Equal(4m, width.Value);
            Assert.Equal(2m, height.Value);
            Assert.Equal("1, 0, 1, 1", crop.Text);
            await ClickAsync(window, Find<Button>(window, "ResizeButton"));
            Assert.Same(session, CurrentSession(window));
            Assert.Equal(new ResizeOperation(4, 2), Assert.Single(session.Document.Operations));
            var bitmap = Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
            Assert.Equal(new PixelSize(4, 2), bitmap.PixelSize);
            AssertRed(bitmap);
            var right = ReadPixel(bitmap, 3, 0);
            Assert.True(right.G > right.R, "Resize must retain the green source edge.");
            Assert.Equal("1, 0, 1, 1", crop.Text); // Resize does not consume a separate crop draft.
        });

    [AvaloniaFact]
    public Task Entered_crop_survives_unrelated_CUI_actions_and_consumes_the_intended_source_region() =>
        WithSourceWindowAsync(async (window, session) =>
        {
            var width = Find<NumericUpDown>(window, "WidthBox");
            var height = Find<NumericUpDown>(window, "HeightBox");
            var crop = Find<TextBox>(window, "CropBoundsBox");
            width.Value = 6;
            crop.Text = "1, 0, 1, 1";
            await ClickAsync(window, Find<Button>(window, "MetadataPreserveButton"));
            await ClickAsync(window, Find<Button>(window, "ZoomInButton"));
            Assert.Equal("1, 0, 1, 1", crop.Text);
            Assert.Equal(6m, width.Value);
            Assert.Equal(3m, height.Value);
            await ClickAsync(window, Find<Button>(window, "ApplyCropButton"));
            Assert.Same(session, CurrentSession(window));
            Assert.Equal(new CropOperation(1, 0, 1, 1), Assert.Single(session.Document.Operations));
            var bitmap = Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
            Assert.Equal(new PixelSize(1, 1), bitmap.PixelSize);
            AssertGreen(bitmap);
            Assert.Equal("0, 0, 1, 1", crop.Text); // The successful consumer acknowledges this crop.
            Assert.Equal(6m, width.Value); // Crop does not consume a separate resize draft.
            Assert.Equal(3m, height.Value);
        });

    private static async Task WithSourceWindowAsync(Func<MainWindow, PictureEditorSession, Task> body)
    {
        var (directory, source, bytes) = await CreateSourceAsync();
        var window = new MainWindow(new PictureFixtureReadiness());
        Exception? firstFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture initialization");
            window.Show(); window.UpdateLayout();
            // Explicit local source fixture, never a canonical Files/Home grant.
            typeof(MainWindow).GetMethod("LoadLocalPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [source]);
            var session = CurrentSession(window);
            await body(window, session);
            await session.SaveAsync(Path.Combine(directory, "geometry.picture.json"),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
        }
        catch (Exception error)
        {
            firstFailure = error;
            RetainedFailedUiOwners.Add((window, error));
            throw;
        }
        finally
        {
            if (firstFailure is null)
            {
                window.Close();
                Assert.NotNull(window.OriginalClose);
                var originalClose = window.OriginalClose!;
                try { await originalClose.ObserveOriginalAsync(window, "Picture original window retirement"); }
                catch (Exception error) { RetainedFailedUiOwners.Add((window, error)); throw; }
                Assert.True(originalClose.IsCompletedSuccessfully);
                Assert.Same(originalClose, window.OriginalClose);
            }
        }
    }

    private static Task ClickAsync(MainWindow window, Button button) =>
        PictureCuiActionFixture.ClickAsync(window, button);

    private static T Find<T>(MainWindow window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    private static PictureEditorSession CurrentSession(MainWindow window) => Assert.IsType<PictureEditorSession>(
        typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));

    private static async Task<(string Directory, string Source, byte[] Bytes)> CreateSourceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "picture-edit-stack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.bmp");
        var bytes = new byte[62];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(2).CopyTo(bytes, 18);
        BitConverter.GetBytes(1).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        BitConverter.GetBytes(8).CopyTo(bytes, 34);
        bytes[56] = 255; bytes[58] = 255; // BGR: one red pixel followed by green.
        await File.WriteAllBytesAsync(source, bytes, TestContext.Current.CancellationToken);
        return (directory, source, bytes);
    }

    private static void AssertRed(Bitmap bitmap)
    {
        var pixel = ReadFirstPixel(bitmap);
        Assert.True(pixel.R > pixel.G, $"Expected red source pixel, observed R={pixel.R}, G={pixel.G}.");
    }

    private static void AssertGreen(Bitmap bitmap)
    {
        var pixel = ReadFirstPixel(bitmap);
        Assert.True(pixel.G > pixel.R, $"Expected green source pixel, observed R={pixel.R}, G={pixel.G}.");
    }

    private static (byte R, byte G) ReadFirstPixel(Bitmap bitmap) => ReadPixel(bitmap, 0, 0);

    private static (byte R, byte G) ReadPixel(Bitmap bitmap, int x, int y)
    {
        var bytes = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                pinned.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4);
            var offset = checked((y * bitmap.PixelSize.Width + x) * 4);
            return (bytes[offset + 2], bytes[offset + 1]);
        }
        finally { pinned.Free(); }
    }
}
