using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using CakeOS.Cui.Runtime;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureComparisonTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedOwners = [];

    [AvaloniaFact]
    public async Task Composition_only_original_and_side_by_side_use_true_pixels_without_mutating_document_or_history()
    {
        var path = NewPath("composition"); var seed = new PictureEditorSession(PictureDocument.Create(100, 80));
        await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await Initialize(window, path); var owner = CurrentSession(window);
            await Click(window, "AddRectangleButton"); await Click(window, "SaveButton");
            Assert.Empty(owner.Document.Operations); Assert.NotNull(owner.Document.CompositionState);
            Assert.True(Find<CheckBox>(window, "CompareButton").IsEnabled);
            var originalBytes = File.ReadAllBytes(path); var snapshot = owner.Document.Serialize(); var last = owner.LastOperation;
            var canUndo = owner.CanUndo; var canRedo = owner.CanRedo; var dirty = owner.IsDirty;
            AssertBlue(CurrentBitmap(window), 50, 40);
            await Toggle(window, "CompareButton", true);
            Assert.Equal(0, Pixel(Preview(window), 50, 40).A); AssertBlue(CurrentBitmap(window), 50, 40);
            Assert.Equal("Original", Find<TextBlock>(window, "ComparisonLeftLabel").Text);
            await Toggle(window, "SideBySideButton", true); window.UpdateLayout();
            Assert.False(Find<CheckBox>(window, "CompareButton").IsChecked);
            var original = Find<Image>(window, "OriginalComparisonImage"); var current = Find<Image>(window, "PreviewImage");
            Assert.True(original.IsVisible); Assert.Equal(0, Pixel(Assert.IsAssignableFrom<Bitmap>(original.Source), 50, 40).A);
            AssertBlue(Assert.IsAssignableFrom<Bitmap>(current.Source), 50, 40);
            Assert.True(original.Bounds.Width > 0 && current.Bounds.Width > 0 && original.Bounds.Right <= current.Bounds.X);
            Assert.Equal("Current", Find<TextBlock>(window, "ComparisonRightLabel").Text);
            Assert.Same(owner, CurrentSession(window)); Assert.Equal(snapshot, owner.Document.Serialize());
            Assert.Same(last, owner.LastOperation); Assert.Equal(canUndo, owner.CanUndo); Assert.Equal(canRedo, owner.CanRedo); Assert.Equal(dirty, owner.IsDirty);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            await Click(window, "UndoButton"); Assert.True(Find<CheckBox>(window, "SideBySideButton").IsChecked);
            Assert.Equal(0, Pixel(Preview(window), 50, 40).A); Assert.Equal(0, Pixel(Assert.IsAssignableFrom<Bitmap>(original.Source), 50, 40).A);
            await Click(window, "RedoButton"); AssertBlue(Preview(window), 50, 40);
            await Click(window, "SaveButton");
            var reopened = await PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
            using var rendered = PictureCropService.Render(reopened); AssertBlue(rendered, 50, 40);
        }
        catch (Exception cause) { firstFailure = cause; RetainedFailedOwners.Add((window, cause)); throw; }
        finally { if (firstFailure is null) await Close(window); }
    }

    [AvaloniaFact]
    public async Task Original_mode_remains_true_across_real_raster_edit_undo_redo_and_paired_source_keeps_original_bytes()
    {
        var bmp = Path.Combine(Path.GetTempPath(), "picture-comparison-source-" + Guid.NewGuid().ToString("N") + ".bmp"); WriteBmp(bmp);
        var sourceBytes = File.ReadAllBytes(bmp); var path = NewPath("source");
        var seed = new PictureEditorSession(new PictureCropService().OpenSource(bmp));
        using (seed.Apply("Initial flip", document => document.Flip(true))) { }
        await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await Initialize(window, path); var owner = CurrentSession(window);
            AssertGreen(Preview(window), 0, 0);
            await Toggle(window, "CompareButton", true); var retainedOriginal = Preview(window); AssertRed(retainedOriginal, 0, 0);
            await Click(window, "UndoButton"); Assert.True(Find<CheckBox>(window, "CompareButton").IsChecked); Assert.Same(retainedOriginal, Preview(window));
            await Click(window, "RedoButton"); Assert.True(Find<CheckBox>(window, "CompareButton").IsChecked); Assert.Same(retainedOriginal, Preview(window));
            AssertGreen(CurrentBitmap(window), 0, 0);
            Find<NumericUpDown>(window, "WidthBox").Value = 7; var enteredWidth = Find<NumericUpDown>(window, "WidthBox").Value;
            await Toggle(window, "SideBySideButton", true);
            Assert.Same(retainedOriginal, Find<Image>(window, "OriginalComparisonImage").Source);
            AssertRed(retainedOriginal, 0, 0); AssertGreen(Preview(window), 0, 0);
            Assert.Equal(enteredWidth, Find<NumericUpDown>(window, "WidthBox").Value);
            await Click(window, "FlipHorizontalButton"); Assert.True(Find<CheckBox>(window, "SideBySideButton").IsChecked);
            AssertRed(Preview(window), 0, 0); Assert.Same(retainedOriginal, Find<Image>(window, "OriginalComparisonImage").Source);
            Assert.Equal(sourceBytes, File.ReadAllBytes(bmp));
            await Click(window, "SaveButton"); Assert.Same(owner, CurrentSession(window));
            await Toggle(window, "SideBySideButton", false); Assert.False(Find<Image>(window, "OriginalComparisonImage").IsVisible);
            Assert.Same(CurrentBitmap(window), Find<Image>(window, "PreviewImage").Source);
        }
        catch (Exception cause) { firstFailure = cause; RetainedFailedOwners.Add((window, cause)); throw; }
        finally { if (firstFailure is null) await Close(window); }
    }

    [AvaloniaFact]
    public async Task Confirmed_document_replacement_retires_old_comparison_source_and_resets_actual_mode()
    {
        var firstPath = NewPath("first"); var first = new PictureEditorSession(PictureDocument.Create(60, 40));
        await first.SaveAsync(firstPath, cancellationToken: TestContext.Current.CancellationToken);
        var secondPath = NewPath("second"); var second = new PictureEditorSession(PictureDocument.Create(80, 50));
        await second.SaveAsync(secondPath, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness()); Exception? firstFailure = null;
        try
        {
            await Initialize(window, firstPath); await Click(window, "AddRectangleButton"); await Click(window, "SaveButton");
            await Toggle(window, "SideBySideButton", true); Assert.NotNull(Field<Bitmap?>(window, "_originalComparisonBitmap"));
            await Load(window, secondPath);
            Assert.Null(Field<Bitmap?>(window, "_originalComparisonBitmap")); Assert.Null(Find<Image>(window, "OriginalComparisonImage").Source);
            Assert.False(Find<Image>(window, "OriginalComparisonImage").IsVisible);
            Assert.False(Find<CheckBox>(window, "CompareButton").IsChecked); Assert.False(Find<CheckBox>(window, "SideBySideButton").IsChecked);
            Assert.Equal(new PixelSize(80, 50), Preview(window).PixelSize); Assert.Equal("Current", Find<TextBlock>(window, "ComparisonLeftLabel").Text);
        }
        catch (Exception cause) { firstFailure = cause; RetainedFailedOwners.Add((window, cause)); throw; }
        finally { if (firstFailure is null) await Close(window); }
    }

    [AvaloniaFact]
    public async Task Changed_source_is_refused_by_original_hash_check_and_failed_close_retains_current_actual_bitmap()
    {
        var bmp = Path.Combine(Path.GetTempPath(), "picture-comparison-changed-" + Guid.NewGuid().ToString("N") + ".bmp"); WriteBmp(bmp);
        var path = NewPath("changed"); var seed = new PictureEditorSession(new PictureCropService().OpenSource(bmp));
        using (seed.Apply("Flip", document => document.Flip(true))) { }
        await seed.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var window = new MainWindow(new PictureFixtureReadiness());
        try
        {
            await Initialize(window, path); var actualCurrent = CurrentBitmap(window); var before = CurrentSession(window).Document.Serialize();
            var changed = File.ReadAllBytes(bmp); changed[54] ^= 0xFF; File.WriteAllBytes(bmp, changed); // Fixture-owned source only.
            var previous = window.OriginalCommand; Find<CheckBox>(window, "CompareButton").IsChecked = true;
            Assert.NotNull(window.OriginalCommand); var original = window.OriginalCommand!; Assert.NotSame(previous, original);
            var failure = await Assert.ThrowsAsync<InvalidDataException>(() => original.ObserveOriginalAsync(window, "Picture changed original source refusal"));
            RetainedFailedOwners.Add((window, failure));
            Assert.Contains(original, window.OriginalCommands); Assert.False(Find<CheckBox>(window, "CompareButton").IsChecked);
            Assert.Same(actualCurrent, Preview(window)); AssertGreen(actualCurrent, 0, 0); Assert.Equal(before, CurrentSession(window).Document.Serialize());
            window.Close(); Assert.NotNull(window.OriginalClose); var originalClose = window.OriginalClose!;
            await Assert.ThrowsAsync<AggregateException>(() => originalClose.ObserveOriginalAsync(window, "Picture comparison failed original close"));
            window.Close(); Assert.Same(originalClose, window.OriginalClose); Assert.Same(actualCurrent, CurrentBitmap(window)); AssertGreen(actualCurrent, 0, 0);
        }
        catch (Exception cause) { RetainedFailedOwners.Add((window, cause)); throw; }
        // The actual failed close/window/source is intentionally retained as evidence, never forced to clean retirement.
    }

    private static Task Click(MainWindow window, string name) => PictureCuiActionFixture.ClickAsync(window, Find<Button>(window, name));
    private static async Task Toggle(MainWindow window, string name, bool selected)
    {
        var input = Find<CheckBox>(window, name); Assert.True(input.IsEnabled);
        var previous = window.OriginalCommand; input.IsChecked = selected;
        Assert.NotNull(window.OriginalCommand); var original = window.OriginalCommand!; Assert.NotSame(previous, original);
        Assert.Contains(original, window.OriginalCommands);
        await original.ObserveOriginalAsync(window, "Picture actual comparison input: " + name);
        Assert.True(original.IsCompletedSuccessfully); Assert.Same(original, window.OriginalCommand);
    }
    private static async Task Initialize(MainWindow window, string path)
    {
        await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture comparison scene initialization");
        await Load(window, path); window.Show(); window.UpdateLayout();
    }
    private static async Task Load(MainWindow window, string path)
    {
        var original = PictureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        var document = await original.ObserveOriginalAsync(window, "Picture comparison fixture original read");
        var owner = new PictureEditorSession(document, path);
        typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [owner, PictureCropService.Render(document), null, null]);
    }
    private static async Task Close(MainWindow window)
    {
        window.Close(); Assert.NotNull(window.OriginalClose); var original = window.OriginalClose!;
        try { await original.ObserveOriginalAsync(window, "Picture original comparison retirement"); }
        catch (Exception cause) { RetainedFailedOwners.Add((window, cause)); throw; }
        Assert.Same(original, window.OriginalClose); Assert.True(original.IsCompletedSuccessfully);
    }
    private static T Find<T>(MainWindow window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static PictureEditorSession CurrentSession(MainWindow window) => Field<PictureEditorSession>(window, "_session");
    private static Bitmap CurrentBitmap(MainWindow window) => Field<Bitmap>(window, "_bitmap");
    private static Bitmap Preview(MainWindow window) => Assert.IsAssignableFrom<Bitmap>(Find<Image>(window, "PreviewImage").Source);
    private static string NewPath(string label) => Path.Combine(Path.GetTempPath(), "picture-comparison-" + label + "-" + Guid.NewGuid().ToString("N") + ".picture.json");
    private static void WriteBmp(string path)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); using var writer = new BinaryWriter(file);
        writer.Write((ushort)0x4D42); writer.Write(62); writer.Write(0); writer.Write(54); writer.Write(40); writer.Write(2); writer.Write(1);
        writer.Write((ushort)1); writer.Write((ushort)24); writer.Write(0); writer.Write(8); writer.Write(3780); writer.Write(3780); writer.Write(0); writer.Write(0);
        writer.Write(new byte[] { 0, 0, 255, 0, 255, 0, 0, 0 });
    }
    private static (byte R, byte G, byte B, byte A) Pixel(Bitmap bitmap, int x, int y)
    {
        var bytes = new byte[4]; var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(x, y, 1, 1), pinned.AddrOfPinnedObject(), 4, 4); }
        finally { pinned.Free(); }
        var bgra = bitmap.Format == Avalonia.Platform.PixelFormat.Bgra8888;
        return (bytes[bgra ? 2 : 0], bytes[1], bytes[bgra ? 0 : 2], bytes[3]);
    }
    private static void AssertBlue(Bitmap bitmap, int x, int y) { var pixel = Pixel(bitmap, x, y); Assert.True(pixel.B > 100 && pixel.B > pixel.R && pixel.A == 255); }
    private static void AssertRed(Bitmap bitmap, int x, int y) { var pixel = Pixel(bitmap, x, y); Assert.True(pixel.R > 240 && pixel.G < 10 && pixel.A == 255); }
    private static void AssertGreen(Bitmap bitmap, int x, int y) { var pixel = Pixel(bitmap, x, y); Assert.True(pixel.G > 240 && pixel.R < 10 && pixel.A == 255); }
}
