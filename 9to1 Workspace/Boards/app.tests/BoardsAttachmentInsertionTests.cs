using System.Security.Cryptography;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsAttachmentInsertionTests
{
    [Theory]
    [InlineData("above", false)]
    [InlineData("below", false)]
    [InlineData("replace", false)]
    [InlineData("above", true)]
    [InlineData("below", true)]
    [InlineData("replace", true)]
    public Task Attachment_list_destinations_preserve_remaining_items_and_exact_history(string placement, bool whole) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot;
            var source = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "list.txt");
            await File.WriteAllTextAsync(source, "list payload");
            TestUiThread.Run(() => vm.PickFileAsync = _ => Task.FromResult<string?>(source));
            OnUi(() => vm.InsertKindRelativeToAsync("attachment", whole ? "anchor" : "anchor:two", placement));
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var blocks = saved.Sections[0].Pages[0].Blocks;
            var attachment = Assert.Single(blocks, block => block.Attachment is not null);
            Assert.Equal("list payload", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(attachment.Attachment!.DataBase64!)));
            var retained = blocks.Where(block => block.Kind == HavenRichBlockKind.BulletList).ToArray();
            Assert.All(retained, block => Assert.Equal("quote", block.StyleId));
            Assert.All(retained.SelectMany(block => block.Items), item => Assert.True(item.Checked));
            var order = blocks.SelectMany(block => block.Kind == HavenRichBlockKind.BulletList
                ? block.Items.Select(item => item.Id) : new[] { block == attachment ? "INSERT" : block.Id });
            var expected = whole ? placement switch
            {
                "above" => new[] { "first", "INSERT", "one", "two", "three", "last" },
                "below" => new[] { "first", "one", "two", "three", "INSERT", "last" },
                _ => new[] { "first", "INSERT", "last" }
            } : placement switch
            {
                "above" => new[] { "first", "one", "INSERT", "two", "three", "last" },
                "below" => new[] { "first", "one", "two", "INSERT", "three", "last" },
                _ => new[] { "first", "one", "INSERT", "three", "last" }
            };
            Assert.Equal(expected, order);
            var committed = adapter.Snapshot;
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(committed, adapter.Snapshot);
        }, rich =>
        {
            var list = rich.Sections[0].Pages[0].Blocks[1]; list.Kind = HavenRichBlockKind.BulletList; list.StyleId = "quote";
            list.Items = new[] { "one", "two", "three" }.Select(id => new HavenRichListItem { Id = id, Text = id, Checked = true }).ToList();
        });

    [Theory]
    [InlineData("above")]
    [InlineData("below")]
    [InlineData("replace")]
    public Task Positioned_attachment_replacement_keeps_geometry_and_unsupported_edges_do_not_open_picker(string placement) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot; var calls = 0;
            var source = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "positioned.txt");
            await File.WriteAllTextAsync(source, "spatial payload");
            TestUiThread.Run(() => vm.PickFileAsync = _ => { calls++; return Task.FromResult<string?>(source); });
            OnUi(() => vm.InsertKindRelativeToAsync("attachment", "anchor", placement));
            if (placement != "replace")
            { Assert.Equal(0, calls); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo); return; }
            Assert.Equal(1, calls);
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var block = Assert.Single(saved.Sections[0].Pages[0].Blocks, block => block.Attachment is not null);
            Assert.Equal(HavenRichBlockLayouts.Absolute, block.Layout);
            Assert.Equal((17d, 35d, 144d, 78d, 9), (block.X, block.Y, block.Width, block.Height, block.ZOrder));
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        }, rich =>
        {
            var block = rich.Sections[0].Pages[0].Blocks[1]; block.Layout = HavenRichBlockLayouts.Absolute;
            block.X = 17; block.Y = 35; block.Width = 144; block.Height = 78; block.ZOrder = 9;
        });

    [Fact]
    public Task Target_edit_while_picker_is_open_is_preserved_and_refuses_replacement() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var source = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "late.txt");
            await File.WriteAllTextAsync(source, "late payload");
            TestUiThread.Run(() => vm.PickFileAsync = _ =>
            {
                vm.PageBlocks.Single(block => block.Id == "anchor").Text = "Edited while choosing";
                return Task.FromResult<string?>(source);
            });
            OnUi(() => vm.InsertKindRelativeToAsync("attachment", "anchor", "replace"));
            Assert.Contains("changed", vm.Get("StatusText")?.ToString());
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var anchor = Assert.Single(saved.Sections[0].Pages[0].Blocks, block => block.Id == "anchor");
            Assert.Equal("Edited while choosing", anchor.PlainText); Assert.Null(anchor.Attachment);
        });

    [Fact]
    public Task Retirement_after_payload_import_keeps_content_and_retains_the_immutable_blob() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot; var revision = adapter.DocumentVersion;
            var source = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "late.bin");
            using (var file = File.Create(source)) { file.SetLength(26 * 1024 * 1024); file.WriteByte(42); }
            var directory = RichBoardSession.SidecarDirectoryFor(adapter.FilePath);
            var ticket = adapter.CaptureInsertionTarget(vm.SelectedPageId, "anchor", false)!;
            var result = await adapter.InsertRelativeAsync(ticket, new RichBoardBlock { Kind = "paragraph", Text = "" },
                BlockInsertionPlacement.Replace, () => !Directory.Exists(directory) || Directory.GetFiles(directory, "*.blob").Length == 0,
                attachmentSource: source);
            Assert.False(result.Applied); Assert.Equal(before, adapter.Snapshot);
            Assert.Equal(revision, adapter.DocumentVersion); Assert.False(adapter.CanUndo);
            var blob = Assert.Single(Directory.GetFiles(directory)); Assert.EndsWith(".blob", blob);
            Assert.Equal(Hash(source), Hash(blob));
        });

    [Fact]
    public async Task External_save_capability_refuses_local_import_even_if_a_host_supplies_a_picker()
    {
        await using var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(
            HavenBoardDocumentCodec.CreateNew("External"), (_, _) => ValueTask.FromResult(true));
        var vm = TestUiThread.Run(() => new BoardsViewModel(adapter));
        try
        {
            var before = adapter.Snapshot; var calls = 0;
            TestUiThread.Run(() => vm.PickFileAsync = _ => { calls++; return Task.FromResult<string?>("unused.txt"); });
            OnUi(() => vm.InsertKindRelativeToAsync("attachment", vm.PageBlocks[0].Id, "replace"));
            Assert.Equal(0, calls); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.Contains("does not grant local attachment import", vm.Get("StatusText")?.ToString());
        }
        finally { TestUiThread.Run(vm.Dispose); }
    }

    [Fact]
    public Task Splitting_a_list_cannot_commit_a_document_that_exceeds_the_read_limit() =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot; var revision = adapter.DocumentVersion;
            var source = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "small.txt");
            await File.WriteAllTextAsync(source, "x");
            TestUiThread.Run(() => vm.PickFileAsync = _ => Task.FromResult<string?>(source));
            OnUi(() => vm.InsertKindRelativeToAsync("attachment", "anchor:two", "replace"));
            Assert.Equal(before, adapter.Snapshot); Assert.Equal(revision, adapter.DocumentVersion); Assert.False(adapter.CanUndo);
            Assert.Contains("document size", vm.Get("StatusText")?.ToString());
            await adapter.GetDetachedContractDocumentAsync();
        }, rich =>
        {
            var list = rich.Sections[0].Pages[0].Blocks[1]; list.Kind = HavenRichBlockKind.BulletList;
            list.Items = new[] { "one", "two", "three" }.Select(id => new HavenRichListItem { Id = id, Text = id }).ToList();
            list.Attachment = new HavenRichAttachmentRef { DisplayName = "existing.bin", SizeBytes = 20 * 1024 * 1024,
                DataBase64 = Convert.ToBase64String(new byte[20 * 1024 * 1024]) };
        });

    [Theory]
    [InlineData("above", 0)]
    [InlineData("below", 0)]
    [InlineData("replace", 0)]
    [InlineData("above", 19)]
    [InlineData("below", 19)]
    [InlineData("replace", 19)]
    [InlineData("above", 25165825)]
    [InlineData("below", 25165825)]
    [InlineData("replace", 25165825)]
    public Task Chosen_attachment_is_one_portable_undoable_edit_at_the_exact_destination(string placement, int length) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot;
            var source = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "selected.txt");
            using (var file = File.Create(source))
            {
                file.SetLength(length);
                if (length > 0) { file.WriteByte(42); file.Position = length - 1; file.WriteByte(13); }
            }
            var expectedHash = Hash(source); var calls = 0;
            TestUiThread.Run(() => vm.PickFileAsync = kind =>
            {
                Assert.Equal("attach", kind); calls++; return Task.FromResult<string?>(source);
            });
            OnUi(() => vm.InsertKindRelativeToAsync("attachment", "anchor", placement));
            Assert.Equal(1, calls);
            var saved = await BoardsRelativeInsertionTests.ReadSaved(adapter);
            var blocks = saved.Sections[0].Pages[0].Blocks;
            var inserted = Assert.Single(blocks, block => block.Attachment is not null);
            Assert.Equal(HavenRichBlockKind.Paragraph, inserted.Kind);
            Assert.Equal(placement == "below" ? 2 : 1, blocks.IndexOf(inserted));
            Assert.Equal(placement == "replace" ? 3 : 4, blocks.Count);
            Assert.NotEqual("anchor", inserted.Id);
            Assert.Equal(placement != "replace", blocks.Any(block => block.Id == "anchor"));
            Assert.Equal("selected.txt", inserted.Attachment!.DisplayName);
            Assert.Equal(length, inserted.Attachment.SizeBytes);
            Assert.Equal(expectedHash, Hash(source)); // importing never changes the chosen file
            File.Delete(source); // owned fixture: prove the board does not depend on that path
            await AssertResolves(adapter.FilePath!, inserted.Attachment.Id, length, expectedHash);
            var committed = adapter.Snapshot;
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(committed, adapter.Snapshot);
            if (length > RichBoardSession.MaxEmbeddedAttachmentBytes)
            {
                var relocated = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "moved", "saved.9to1board");
                Directory.CreateDirectory(Path.GetDirectoryName(relocated)!);
                await adapter.SaveAsAsync(relocated);
                await AssertResolves(relocated, inserted.Attachment.Id, length, expectedHash);
            }
        });

    [Theory]
    [InlineData("cancel")]
    [InlineData("missing")]
    [InlineData("oversized")]
    [InlineData("page-away-and-back")]
    [InlineData("view-away-and-back")]
    [InlineData("dispose")]
    public Task Failed_or_retired_attachment_choice_preserves_content_revision_and_history(string transition) =>
        BoardsRelativeInsertionTests.Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot; var version = adapter.DocumentVersion; var calls = 0;
            var source = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "candidate.txt");
            if (transition != "missing")
            {
                using var file = File.Create(source);
                file.SetLength(transition == "oversized" ? RichBoardSession.MaxSidecarAttachmentBytes + 1 : 7);
            }
            TestUiThread.Run(() => vm.PickFileAsync = kind =>
            {
                Assert.Equal("attach", kind); calls++;
                if (transition == "page-away-and-back")
                { var page = vm.SelectedPageId; vm.SelectPage(vm.SelectedSectionId, "other"); vm.SelectPage(vm.SelectedSectionId, page); }
                if (transition == "view-away-and-back")
                { vm.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult(); vm.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult(); }
                if (transition == "dispose") vm.Dispose();
                return Task.FromResult<string?>(transition == "cancel" ? null : source);
            });
            OnUi(() => vm.InsertKindRelativeToAsync("attachment", "anchor", "replace"));
            Assert.Equal(1, calls);
            await adapter.SaveAsync();
            Assert.Equal(before, adapter.Snapshot); Assert.Equal(version, adapter.DocumentVersion); Assert.False(adapter.CanUndo);
            Assert.False(Directory.Exists(RichBoardSession.SidecarDirectoryFor(adapter.FilePath)));
        }, rich => rich.Sections[0].Pages.Add(new HavenRichPage { Id = "other", Title = "Other" }));

    private static async Task AssertResolves(string boardPath, string attachmentId, int length, byte[] hash)
    {
        using var store = new JsonFileHavenBoardStore(Path.GetDirectoryName(boardPath)!);
        await using var reopened = await ContractSessionAdapter.OpenAsync(store, boardPath);
        var resolution = await reopened.ResolveAttachmentAsync(attachmentId);
        if (length <= RichBoardSession.MaxEmbeddedAttachmentBytes)
        {
            Assert.Equal(HavenAttachmentStatus.Available, resolution.Status);
            Assert.Equal(hash, SHA256.HashData(Assert.IsType<byte[]>(resolution.EmbeddedBytes)));
        }
        else
        {
            Assert.Equal(HavenAttachmentStatus.Sidecar, resolution.Status);
            Assert.Equal(hash, Hash(resolution.SidecarPath!));
            Assert.Equal(Convert.ToHexString(hash).ToLowerInvariant() + ".blob", Path.GetFileName(resolution.SidecarPath));
        }
    }

    private static byte[] Hash(string path) { using var file = File.OpenRead(path); return SHA256.HashData(file); }

    internal static void OnUi(Func<Task> action) => TestUiThread.Run(() =>
    {
        var task = action(); var until = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Assert.True(task.IsCompleted, "Attachment action did not settle."); task.GetAwaiter().GetResult();
    });
}
