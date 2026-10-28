using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsDatabaseReferenceTests
{
    [Fact]
    public async Task Database_reference_survives_save_reopen_and_undo_without_copying_records()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-data-reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "reference.9to1board");
            await using (var initial = await RichBoardSession.CreateNewAsync(store, "References"))
            {
                // A freshly created page carries the contract's starter paragraph. This test
                // is about the database reference only, so the fixture states its starting
                // blocks explicitly instead of leaving an unrelated content block in the page.
                await initial.MutateAsync(rich => rich.Sections[0].Pages[0].Blocks.Clear());
                await initial.SaveAsAsync(path);
            }
            var reference = new HavenBoardDatabaseReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await using (var board = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var page = board.Document.Sections[0].Pages[0];
                page.Blocks.Add(new RichBoardBlock { Id = "database-block", Kind = "database", Database = reference });
                board.MarkDirty(); await board.SaveAsync();
                Assert.Equal(reference, Assert.Single(board.Document.Sections[0].Pages[0].Blocks).Database);
                Assert.True(await board.UndoAsync());
                Assert.Empty(board.Document.Sections[0].Pages[0].Blocks);
                Assert.True(await board.RedoAsync());
                await board.SaveAsync();
            }
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Equal(reference, Assert.Single(reopened.Document.Sections[0].Pages[0].Blocks).Database);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var payload = json.RootElement.GetProperty("richNotes").GetProperty("sections")[0]
                .GetProperty("pages")[0].GetProperty("blocks")[0].GetProperty("database");
            Assert.Equal(4, payload.EnumerateObject().Count());
            Assert.False(payload.TryGetProperty("rows", out _));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Schema_three_text_board_migrates_without_creating_a_database_or_changing_ids()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-data-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "older.9to1board");
            var document = HavenBoardDocumentCodec.CreateNew() with { SchemaVersion = 3 };
            // State the migrated page's content explicitly: this check is about migration not
            // creating a database block or changing IDs, so the starter paragraph is removed
            // rather than left as an unrelated extra block.
            document.RichNotes!.Sections[0].Pages[0].Blocks.Clear();
            document.RichNotes.Sections[0].Pages[0].Blocks.Add(HavenRichBlock.Paragraph("Existing text"));
            var id = document.RichNotes.Sections[0].Pages[0].Blocks[0].Id;
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            using var store = new JsonFileHavenBoardStore(root);
            await using var board = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Equal(id, Assert.Single(board.Document.Sections[0].Pages[0].Blocks).Id);
            Assert.Null(Assert.Single(board.Document.Sections[0].Pages[0].Blocks).Database);
            await board.SaveAsync();
            var saved = (await store.LoadDocumentAtPathAsync(path))!;
            Assert.Equal(HavenBoardDocument.CurrentSchemaVersion, saved.SchemaVersion);
            Assert.Equal(document.DocumentId, saved.DocumentId);
        }
        finally { Directory.Delete(root, true); }
    }
}
