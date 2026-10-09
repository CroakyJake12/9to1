using System.Reflection;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsCloseSaveTests
{
    [Fact]
    public async Task Failed_physical_close_save_keeps_draft_and_target_then_actual_retry_reopens()
    {
        var root = CreateRoot();
        try
        {
            var parent = Path.Combine(root, "chosen");
            var moved = Path.Combine(root, "retained");
            var path = Path.Combine(parent, "board.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.CreateNewAtPathAsync(store, path);
            var original = await File.ReadAllBytesAsync(path);
            var identity = (await store.LoadDocumentAtPathAsync(path))!.DocumentId;
            var pageId = adapter.Document.Sections[0].Pages[0].Id;
            var paragraph = adapter.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
            paragraph.Text = "Draft retained after a rejected physical save";
            var owner = new BoardsCloseSaveOwner(() => adapter);

            // Real physical failure: the chosen parent has become a file. The
            // original board stays retained at the moved directory; no fake ACK.
            Directory.Move(parent, moved);
            await File.WriteAllTextAsync(parent, "occupied parent");
            var failed = owner.SaveBeforeCloseAsync();
            await Assert.ThrowsAnyAsync<IOException>(() => failed);
            Assert.False(owner.SaveAccepted);
            Assert.True(owner.OriginalSaveTask!.IsFaulted);
            Assert.Equal(path, adapter.FilePath);
            Assert.Equal("Draft retained after a rejected physical save", paragraph.Text);
            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(moved, "board.9to1board")));
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = owner.DrainAfterSaveAsync(() => { }, () => Task.CompletedTask);
            });

            File.Delete(parent);
            Directory.Move(moved, parent);
            var retry = owner.SaveBeforeCloseAsync();
            Assert.NotSame(failed, retry);
            await retry.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(owner.SaveAccepted);
            Assert.True(owner.OriginalSaveTask!.IsCompletedSuccessfully);
            await owner.DrainAfterSaveAsync(() => { }, () => Task.CompletedTask);
            Assert.True(owner.OriginalDisposeTask!.IsCompletedSuccessfully);

            using var freshStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await ContractSessionAdapter.OpenAsync(freshStore, path);
            Assert.Equal(pageId, reopened.Document.Sections[0].Pages[0].Id);
            Assert.Equal("Draft retained after a rejected physical save", reopened.Document.Sections[0]
                .Pages[0].Blocks.First(b => b.Kind == "paragraph").Text);
            Assert.Equal(identity, (await freshStore.LoadDocumentAtPathAsync(path))!.DocumentId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Repeated_close_requests_share_the_held_actual_store_save_and_cannot_dispose_early()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "coalesced.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.CreateNewAtPathAsync(store, path);
            var original = await File.ReadAllBytesAsync(path);
            adapter.Document.Title = "One actual close save";
            var owner = new BoardsCloseSaveOwner(() => adapter);
            var gate = ActualStoreGate(store);
            await gate.WaitAsync();
            Task first;
            try
            {
                first = owner.SaveBeforeCloseAsync();
                Assert.Same(first, owner.SaveBeforeCloseAsync());
                await WaitForAsync(() => owner.OriginalSaveTask is not null);
                Assert.False(first.IsCompleted);
                Assert.False(owner.OriginalSaveTask!.IsCompleted);
                Assert.False(owner.SaveAccepted);
                Assert.Equal(original, await File.ReadAllBytesAsync(path));
                Assert.Throws<InvalidOperationException>(() =>
                {
                    _ = owner.DrainAfterSaveAsync(() => { }, () => Task.CompletedTask);
                });
            }
            finally { gate.Release(); }
            await first.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(owner.SaveAccepted);
            Assert.Same(first, owner.SaveBeforeCloseAsync());
            var drain = owner.DrainAfterSaveAsync(() => { }, () => Task.CompletedTask);
            Assert.Same(drain, owner.DrainAfterSaveAsync(() => { }, () => Task.CompletedTask));
            await drain;
            using var freshStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await ContractSessionAdapter.OpenAsync(freshStore, path);
            Assert.Equal("One actual close save", reopened.Document.Title);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editor_input_during_actual_save_or_save_as_survives_ack_and_next_save(bool saveAs)
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "original.9to1board");
            var copy = Path.Combine(root, "copy.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.CreateNewAtPathAsync(store, path);
            var identity = (await store.LoadDocumentAtPathAsync(path))!.DocumentId;
            var page = adapter.Document.Sections[0].Pages[0];
            var paragraph = page.Blocks.First(b => b.Kind == "paragraph");
            paragraph.Text = "Submitted draft";
            var gate = ActualStoreGate(store);
            await gate.WaitAsync();
            Task save;
            try
            {
                save = (saveAs ? adapter.SaveAsAsync(copy) : adapter.SaveAsync()).AsTask();
                Assert.False(save.IsCompleted);
                paragraph.Text = "Typed while the real store save was held";
                page.Blocks.Add(new RichBoardBlock { Kind = "paragraph", Text = "New late block" });
                adapter.MarkDirty();
            }
            finally { gate.Release(); }
            await save.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("Typed while the real store save was held", adapter.Document.Sections[0]
                .Pages[0].Blocks.First(b => b.Kind == "paragraph").Text);
            Assert.Contains(adapter.Document.Sections[0].Pages[0].Blocks, b => b.Text == "New late block");
            Assert.Equal(saveAs ? copy : path, adapter.FilePath);
            await adapter.SaveAsync();
            await adapter.DisposeAsync();

            using var freshStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await ContractSessionAdapter.OpenAsync(freshStore, saveAs ? copy : path);
            Assert.Equal("Typed while the real store save was held", reopened.Document.Sections[0]
                .Pages[0].Blocks.First(b => b.Kind == "paragraph").Text);
            Assert.Single(reopened.Document.Sections[0].Pages[0].Blocks.Where(b => b.Text == "New late block"));
            Assert.Equal(identity, (await freshStore.LoadDocumentAtPathAsync(saveAs ? copy : path))!.DocumentId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Held_callback_drain_preserves_fault_siblings_and_disposes_the_exact_saved_session()
    {
        var root = CreateRoot();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var saved = await ContractSessionAdapter.CreateNewAtPathAsync(store,
                Path.Combine(root, "saved.9to1board"));
            await using var other = await ContractSessionAdapter.CreateNewAtPathAsync(store,
                Path.Combine(root, "other.9to1board"));
            IRichBoardSession current = saved;
            var owner = new BoardsCloseSaveOwner(() => current);
            await owner.SaveBeforeCloseAsync();
            current = other;
            var callback = new TaskCompletionSource();
            var drain = owner.DrainAfterSaveAsync(() => { }, () => callback.Task);
            Assert.Same(drain, owner.DrainAfterSaveAsync(() => { }, () => Task.CompletedTask));
            await WaitForAsync(() => owner.OriginalActionDrainTask is not null);
            Assert.Same(callback.Task, owner.OriginalActionDrainTask);
            Assert.False(drain.IsCompleted);
            Assert.Null(owner.OriginalDisposeTask);
            var canceledLikeFault = new OperationCanceledException("Faulted callback payload");
            var sibling = new IOException("Original callback sibling");
            callback.SetException(new Exception[] { canceledLikeFault, sibling });
            var error = await Assert.ThrowsAsync<AggregateException>(() => drain);
            Assert.Contains(error.InnerExceptions, failure => ReferenceEquals(failure, canceledLikeFault));
            Assert.Contains(error.InnerExceptions, failure => ReferenceEquals(failure, sibling));
            Assert.True(drain.IsFaulted);
            Assert.True(owner.OriginalDisposeTask!.IsCompletedSuccessfully);
            Assert.Throws<ObjectDisposedException>(() => saved.MarkDirty());
            other.Document.Title = "The unsaved session was not disposed";
            await other.SaveAsync();
            using var fresh = new JsonFileHavenBoardStore(root);
            Assert.Equal("The unsaved session was not disposed",
                (await fresh.LoadDocumentAtPathAsync(other.FilePath!))!.RichNotes!.Title);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Test-only scheduling control over the existing real physical store. This
    // neither replaces the store nor issues success, permission or readiness.
    private static SemaphoreSlim ActualStoreGate(JsonFileHavenBoardStore store) =>
        (SemaphoreSlim)(typeof(JsonFileHavenBoardStore).GetField("_gate",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(store)
            ?? throw new InvalidOperationException("The real store scheduling gate was not found."));

    private static async Task WaitForAsync(Func<bool> observed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!observed()) await Task.Delay(1, timeout.Token);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Boards-close-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
