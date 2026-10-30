using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsAttachmentPickerTests
{
    [Theory]
    [InlineData("accept", false)]
    [InlineData("cancel", false)]
    [InlineData("detach", false)]
    [InlineData("detach-and-back", false)]
    [InlineData("lifetime", false)]
    [InlineData("accept", true)]
    [InlineData("cancel", true)]
    public async Task Embedded_local_board_uses_its_owner_picker_and_retires_late_choices(string outcome, bool throughMenu)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-picker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var theme = TestUiThread.Run(() => (BoardsTheme.Mode, CuiSurfacePaletteCatalog.ActiveTheme,
            Application.Current!.RequestedThemeVariant));
        TestUiThread.Run(() =>
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
            BoardsTheme.SetMode(BoardsThemeMode.Dark);
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        });
        try
        {
            using var documents = new BoardsLocalDocuments("picker-profile", root);
            var id = await documents.CreateAsync();
            string anchorId;
            await using (var seed = await documents.OpenAsync(id))
            {
                var anchor = Assert.Single(seed.Document.Sections[0].Pages[0].Blocks);
                anchorId = anchor.Id; anchor.Text = "Keep until chosen";
                seed.MarkDirty(); await seed.SaveAsync();
            }
            var file = new ChosenFile(new Uri(Path.Combine(root, "chosen.txt")));
            await File.WriteAllTextAsync(file.Path.LocalPath, "picked bytes");
            var picker = new Picker();
            using var lifetime = new CancellationTokenSource();
            var presentation = Pump(TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(
                documents, id, (_, _) => Task.CompletedTask, lifetimeToken: lifetime.Token)));
            IDisposable? locatorScope = null; Window? window = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    locatorScope = AvaloniaLocator.EnterScope();
                    AvaloniaLocator.CurrentMutable.Bind<IStorageProviderFactory>().ToConstant(new Factory(picker));
                    window = new Window { Width = 1180, Height = 760, Content = presentation.Content };
                    window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                });
                var vm = presentation.ViewModel;
                Assert.NotNull(vm.PickFileAsync); // production embedded-local wiring, not a test callback
                var action = TestUiThread.Run(() => throughMenu ? StartMenuAction(window!, anchorId, outcome) :
                    vm.InsertKindRelativeToAsync("attachment", anchorId, "replace"));
                Assert.Equal(1, picker.Calls);
                Assert.False(action.IsCompleted);
                TestUiThread.Run(() =>
                {
                    Assert.Equal("Keep until chosen", Assert.Single(vm.PageBlocks).Text);
                    if (outcome == "detach") window!.Content = null;
                    if (outcome == "detach-and-back") { window!.Content = null; window.Content = presentation.Content; }
                    if (outcome == "lifetime") lifetime.Cancel();
                    picker.Choice.SetResult(outcome == "cancel" ? [] : [file]);
                });
                Pump(action);
                TestUiThread.Run(() =>
                {
                    var block = Assert.Single(vm.PageBlocks);
                    if (outcome == "accept")
                    {
                        Assert.NotEqual(anchorId, block.Id);
                        Assert.Equal("chosen.txt", block.AttachmentName);
                        Assert.Equal(12, block.AttachmentSize);
                        if (throughMenu)
                        {
                            Settle(window!, () => window!.GetVisualDescendants().OfType<TextBlock>()
                                .Any(text => text.Text == "chosen.txt" && text.IsEffectivelyVisible));
                            Capture(window!, "attachment-committed");
                        }
                    }
                    else { Assert.Equal(anchorId, block.Id); Assert.Null(block.AttachmentId); }
                });
                Assert.Equal(outcome != "cancel", file.Disposed);
                Assert.Equal("Choose a file", picker.Options!.Title); Assert.False(picker.Options.AllowMultiple);
            }
            finally
            {
                picker.Choice.TrySetResult([]);
                TestUiThread.Run(() => window?.Close());
                Pump(TestUiThread.Run(() => presentation.DisposeAsync().AsTask()));
                TestUiThread.Run(() => locatorScope?.Dispose());
            }
            await using var reopened = await documents.OpenAsync(id);
            var persisted = Assert.Single(reopened.Document.Sections[0].Pages[0].Blocks);
            if (outcome == "accept")
            {
                var resolved = await reopened.ResolveAttachmentAsync(persisted.AttachmentId!);
                Assert.Equal("picked bytes", System.Text.Encoding.UTF8.GetString(resolved.EmbeddedBytes!));
            }
            else { Assert.Equal(anchorId, persisted.Id); Assert.Null(persisted.AttachmentId); }
        }
        finally
        {
            TestUiThread.Run(() =>
            {
                CuiSurfacePaletteCatalog.ActiveTheme = theme.ActiveTheme;
                BoardsTheme.SetMode(theme.Mode); Application.Current!.RequestedThemeVariant = theme.RequestedThemeVariant;
            });
            Directory.Delete(root, true);
        }
    }

    private static T Pump<T>(Task<T> task)
    {
        Pump((Task)task); return task.GetAwaiter().GetResult();
    }
    private static void Pump(Task task) => BoardsAttachmentInsertionTests.OnUi(() => task);

    private static Task StartMenuAction(Window window, string anchorId, string outcome)
    {
        Button? insert = null;
        Settle(window, () => (insert = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
            ToolbarBuilder.HasInsertMenu(button) && button.GetVisualAncestors().OfType<Border>().Any(border =>
                border.Tag is BlockReorderRow row && row.BlockId == anchorId))) is not null);
        Click(window, insert!);
        var menu = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(insert!));
        Assert.True(menu.Popup.IsOpen);
        Click(window, BoardsCuiControlFinder.FindByAutomationId<Control>(menu.Popup.Child!, "InsertReplace")!);
        Settle(window, () => menu.Placement == "replace");
        Assert.True(menu.Search.IsFocused);
        window.KeyTextInput("Attachment");
        Settle(window, () => menu.Search.Text == "Attachment");
        if (outcome == "accept") Capture(window, "attachment-replace-choice");
        var attachment = menu.Popup.Child!.GetVisualDescendants().OfType<Button>().Single(button =>
            AutomationProperties.GetName(button) == "Insert Attachment…");
        Assert.True(attachment.IsEffectivelyEnabled);
        Click(window, attachment);
        return menu.WhenActionsIdleAsync();
    }
    private static void Click(Window window, Control control)
    {
        control.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
    }
    private static void Settle(Window window, Func<bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        do { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); if (ready()) return; Thread.Sleep(1); }
        while (DateTime.UtcNow < until);
        Assert.True(ready(), "The actual embedded Boards surface did not settle.");
    }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("NINETOONE_ATTACHMENT_CAPTURE_DIR") is not { Length: > 0 } root) return;
        Directory.CreateDirectory(root);
        using var bitmap = new RenderTargetBitmap(new PixelSize(1180, 760), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(root, name + ".png"));
    }

    private sealed class Factory(Picker picker) : IStorageProviderFactory
    { public IStorageProvider CreateProvider(TopLevel topLevel) => picker; }

    // Only the OS dialog result is substituted. The production owner, insertion action, session,
    // canonical codec and on-disk payload path remain real; this is headless evidence.
    private sealed class Picker : IStorageProvider
    {
        internal readonly TaskCompletionSource<IReadOnlyList<IStorageFile>> Choice = new();
        internal int Calls;
        internal FilePickerOpenOptions? Options;
        public bool CanOpen => true;
        public bool CanSave => false;
        public bool CanPickFolder => false;
        public Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
        { Calls++; Options = options; return Choice.Task; }
        public Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options) => throw new NotSupportedException();
        public Task<SaveFilePickerResult> SaveFilePickerWithResultAsync(FilePickerSaveOptions options) => throw new NotSupportedException();
        public Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options) => throw new NotSupportedException();
        public Task<IStorageBookmarkFile?> OpenFileBookmarkAsync(string bookmark) => throw new NotSupportedException();
        public Task<IStorageBookmarkFolder?> OpenFolderBookmarkAsync(string bookmark) => throw new NotSupportedException();
        public Task<IStorageFile?> TryGetFileFromPathAsync(Uri path) => throw new NotSupportedException();
        public Task<IStorageFolder?> TryGetFolderFromPathAsync(Uri path) => throw new NotSupportedException();
        public Task<IStorageFolder?> TryGetWellKnownFolderAsync(WellKnownFolder folder) => throw new NotSupportedException();
    }

    private sealed class ChosenFile(Uri path) : IStorageFile
    {
        internal bool Disposed;
        public string Name => System.IO.Path.GetFileName(path.LocalPath);
        public Uri Path => path;
        public bool CanBookmark => false;
        public void Dispose() => Disposed = true;
        public Task<Stream> OpenReadAsync() => Task.FromResult<Stream>(File.OpenRead(path.LocalPath));
        public Task<Stream> OpenWriteAsync() => throw new NotSupportedException();
        public Task<StorageItemProperties> GetBasicPropertiesAsync() => throw new NotSupportedException();
        public Task<string?> SaveBookmarkAsync() => throw new NotSupportedException();
        public Task<IStorageFolder?> GetParentAsync() => throw new NotSupportedException();
        public Task DeleteAsync() => throw new NotSupportedException();
        public Task<IStorageItem?> MoveAsync(IStorageFolder destination) => throw new NotSupportedException();
    }
}
