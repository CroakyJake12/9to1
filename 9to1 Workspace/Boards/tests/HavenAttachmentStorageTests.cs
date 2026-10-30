using System.Reflection;
using System.Security.Cryptography;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

public sealed class HavenAttachmentStorageTests
{
    [Fact]
    public Task Json_escaping_cannot_make_an_individually_valid_attachment_unreadable() => WithSession(async (session, store, root) =>
    {
        var source = Path.Combine(root, "plus-rich.bin");
        var bytes = new byte[16 * 1024 * 1024]; Array.Fill(bytes, (byte)0xFB);
        await File.WriteAllBytesAsync(source, bytes); // base64 contains many '+' characters escaped by JSON
        var attachment = await session.ImportAttachmentAsync(source);
        await Add(session, attachment); await session.SaveAsync();
        _ = HavenBoardDocumentCodec.Deserialize(await File.ReadAllBytesAsync(session.FilePath!));
        Assert.Null(attachment.DataBase64); Assert.StartsWith("sidecar:", attachment.LocalReference);
        var resolution = await session.ResolveAttachmentAsync(attachment);
        Assert.Equal(Hash(source), Hash(resolution.SidecarPath!));
    });

    [Fact]
    public Task Repeated_medium_imports_remain_readable_by_the_production_document_codec() => WithSession(async (session, store, root) =>
    {
        var source = Source(root, 20 * 1024 * 1024);
        var first = await session.ImportAttachmentAsync(source);
        Assert.NotNull(first.DataBase64);
        await Add(session, first);
        var second = await session.ImportAttachmentAsync(source);
        await Add(session, second);
        await session.SaveAsync();
        var document = HavenBoardDocumentCodec.Deserialize(await File.ReadAllBytesAsync(session.FilePath!));
        Assert.Equal(2, document.RichNotes!.Sections[0].Pages[0].Blocks.Count(block => block.Attachment is not null));
        Assert.Null(second.DataBase64); Assert.StartsWith("sidecar:", second.LocalReference);
        var resolution = await session.ResolveAttachmentAsync(second);
        Assert.Equal(HavenAttachmentStatus.Sidecar, resolution.Status);
        Assert.Equal(Hash(source), Hash(resolution.SidecarPath!));
    });

    [Fact]
    public Task Refused_save_as_preserves_the_other_boards_file_and_sidecar_directory() => WithSession(async (session, store, root) =>
    {
        var attachment = await session.ImportAttachmentAsync(Source(root, 26 * 1024 * 1024));
        await Add(session, attachment); await session.SaveAsync();
        var otherPath = Path.Combine(root, "other.9to1board");
        await store.SaveDocumentAtPathAsync(HavenBoardDocumentCodec.CreateNew("Other owner"), otherPath);
        var otherHash = Hash(otherPath);
        var directory = RichBoardSession.SidecarDirectoryFor(otherPath); Directory.CreateDirectory(directory);
        var owned = Path.Combine(directory, "preserve.blob"); await File.WriteAllTextAsync(owned, "other owner's bytes");
        var payloadHash = Hash(owned);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveAsAsync(otherPath));
        Assert.Equal(otherHash, Hash(otherPath)); Assert.Equal(payloadHash, Hash(owned));
        Assert.Equal(new[] { owned }, Directory.GetFiles(directory));
    });

    [Fact]
    public Task Deduplication_preserves_shared_blobs_and_rejected_corrupt_blobs_remove_only_owned_temps() => WithSession(async (session, store, root) =>
    {
        var source = Source(root, 26 * 1024 * 1024);
        var first = await session.ImportAttachmentAsync(source);
        var second = await session.ImportAttachmentAsync(source);
        Assert.Equal(first.LocalReference, second.LocalReference);
        var directory = RichBoardSession.SidecarDirectoryFor(session.FilePath);
        var blob = Assert.Single(Directory.GetFiles(directory));
        Assert.Equal(Hash(source), Hash(blob));
        await File.WriteAllTextAsync(blob, "pre-existing corrupt fixture; never overwrite");
        var before = Hash(blob);
        await Assert.ThrowsAsync<IOException>(() => session.ImportAttachmentAsync(source));
        Assert.Equal(before, Hash(blob)); Assert.Equal(new[] { blob }, Directory.GetFiles(directory));
        Assert.False(session.CanUndo); Assert.False(session.HasUnsavedChanges);
    });

    [Fact]
    public Task Cancellation_before_import_creates_no_payload_or_history() => WithSession(async (session, store, root) =>
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ImportAttachmentAsync(
            Source(root, 26 * 1024 * 1024), cancellation.Token));
        Assert.False(Directory.Exists(RichBoardSession.SidecarDirectoryFor(session.FilePath)));
        Assert.False(session.CanUndo); Assert.False(session.HasUnsavedChanges);
    });

    [Fact]
    public async Task Payload_copy_observes_cancellation_after_bytes_were_written()
    {
        // Component-level cancellation evidence using the actual copy implementation. Filesystem
        // temp cleanup is independently exercised above; this is not a native picker/import claim.
        using var source = new PausedAfterFirstChunk(); using var target = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        var method = typeof(RichBoardSession).GetMethod("CopyAttachmentPayloadAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var copying = (Task<(long Size, string Digest)>)method.Invoke(null, [source, target, 1024L * 1024, cancellation.Token])!;
        await source.Paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(65536, target.Length); Assert.False(copying.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copying.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(65536, target.Length);
    }

    private sealed class PausedAfterFirstChunk() : MemoryStream(new byte[65536])
    {
        internal readonly TaskCompletionSource Paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position == 0) return await base.ReadAsync(buffer, token);
            Paused.TrySetResult();
            await new TaskCompletionSource().Task.WaitAsync(token); return 0;
        }
    }

    private static Task Add(RichBoardSession session, HavenRichAttachmentRef attachment) => session.MutateAsync(rich =>
    {
        var page = rich.Sections[0].Pages[0];
        var block = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Paragraph, "");
        HavenRichNotesOps.AttachToBlock(rich, page.Id, block.Id, attachment);
    });
    private static string Source(string root, long size)
    {
        var path = Path.Combine(root, "source.bin"); using var file = File.Create(path);
        file.SetLength(size); file.WriteByte(42); return path;
    }
    private static byte[] Hash(string path) { using var file = File.OpenRead(path); return SHA256.HashData(file); }
    private static async Task WithSession(Func<RichBoardSession, JsonFileHavenBoardStore, string, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-attachment-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var session = await RichBoardSession.CreateNewAsync(store, "Storage checks");
            await session.SaveAsAsync(Path.Combine(root, "board.9to1board"));
            await action(session, store, root);
        }
        finally { Directory.Delete(root, true); }
    }
}
