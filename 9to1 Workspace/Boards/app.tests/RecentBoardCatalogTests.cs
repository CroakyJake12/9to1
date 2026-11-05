using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

public sealed class RecentBoardCatalogTests
{

    /// <summary>
    /// The landing cards are only honest if they read what the file carries. This writes the Board and page
    /// icons through the real session, closes it, and then builds the cards from the saved file - the same
    /// route the landing view takes - so a card cannot agree with the editor and disagree with the disk.
    /// </summary>
    [Fact]
    public async Task Saved_Board_and_page_icons_reach_the_landing_cards()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-card-icons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(Path.Combine(root, "store"));
            var boardPath = Path.Combine(root, "Marked.9to1board");
            string sectionId;
            string pageId;
            await using (var session = await ContractSessionAdapter.CreateNewAtPathAsync(store, boardPath))
            {
                var section = Assert.Single(session.Document.Sections);
                var page = Assert.Single(section.Pages);
                sectionId = section.Id;
                pageId = page.Id;
                await session.SetBoardIconAsync(HavenRichSectionIcons.Emoji, "📚", null);
                await session.SetPageIconEmojiAsync(page.Id, "⚖️", null);
                await session.SaveAsync();
            }

            var catalog = new RecentBoardCatalog(Path.Combine(root, "recent-board-paths.json"));
            catalog.Record(boardPath);
            catalog.RecordPage(boardPath, sectionId, pageId);

            var boardCards = await catalog.LoadCardsAsync(store);
            var boardCard = Assert.Single(boardCards);
            Assert.True(boardCard.IconIsEmoji, "the Board's own mark comes from the saved file");
            Assert.Equal("📚", boardCard.IconGlyph);
            Assert.Equal("books board icon", boardCard.IconAccessibleName);

            var pageCards = await catalog.LoadRecentPagesAsync(store);
            var pageCard = Assert.Single(pageCards);
            Assert.True(pageCard.IconIsEmoji);
            Assert.Equal("⚖️", pageCard.IconGlyph);
            // The name comes from the CLDR catalogue rather than from this test's guess about what a glyph is
            // called, so the assertion checks the composition rule and that a real name was resolved.
            var scalesName = Haven.UI.Emoji.EmojiCatalogue.Find("⚖️")?.Name;
            Assert.False(string.IsNullOrEmpty(scalesName), "the catalogue should know this emoji");
            Assert.Equal($"{scalesName} page icon", pageCard.IconAccessibleName);
            Assert.False(pageCard.IconIsVector);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Landing_cards_resolve_real_board_and_recent_page_references()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-recents", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(Path.Combine(root, "store"));
            var path = Path.Combine(root, "Project.9to1board");
            string sectionId;
            string pageId;
            await using (var session = await ContractSessionAdapter.CreateNewAtPathAsync(store, path))
            {
                session.Document.Title = "Project Plan";
                var section = Assert.Single(session.Document.Sections);
                var page = Assert.Single(section.Pages);
                sectionId = section.Id;
                pageId = page.Id;
                page.Title = "Launch notes";
                if (page.Blocks.Count == 0) page.Blocks.Add(new RichBoardBlock { Kind = "paragraph" });
                page.Blocks[0].Text = "Launch checklist";
                session.MarkDirty();
                await session.SaveAsync();
            }

            var catalogPath = Path.Combine(root, "recent-board-paths.json");
            var catalog = new RecentBoardCatalog(catalogPath);
            catalog.Record(path);
            catalog.RecordPage(path, sectionId, pageId);
            var reopenedCatalog = new RecentBoardCatalog(catalogPath);
            var boards = await reopenedCatalog.LoadCardsAsync(store);
            var boardCard = Assert.Single(boards);
            Assert.True(boardCard.IsAvailable);
            Assert.Equal("Project Plan", boardCard.Title);
            Assert.Contains("Launch checklist", boardCard.Preview);

            var pageCard = Assert.Single(await reopenedCatalog.LoadRecentPagesAsync(store));
            Assert.Equal(pageId, pageCard.PageId);
            Assert.Equal("Launch notes", pageCard.PageTitle);
            Assert.Equal("Project Plan", pageCard.BoardTitle);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Corrupt_and_future_version_recents_are_isolated_per_item()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-recents", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(Path.Combine(root, "store"));
            var validPath = Path.Combine(root, "Valid.9to1board");
            string sectionId;
            string pageId;
            await using (var session = await ContractSessionAdapter.CreateNewAtPathAsync(store, validPath))
            {
                var section = Assert.Single(session.Document.Sections);
                var page = Assert.Single(section.Pages);
                sectionId = section.Id;
                pageId = page.Id;
                await session.SaveAsync();
            }

            var corruptPath = Path.Combine(root, "Corrupt.9to1board");
            await File.WriteAllTextAsync(corruptPath, "{ this is not valid board JSON");
            await File.WriteAllTextAsync(corruptPath + ".bak", "{ neither is this");
            var futurePath = Path.Combine(root, "Future.9to1board");
            await File.WriteAllTextAsync(futurePath, """{"format":"9to1.board","schemaVersion":999}""");

            var catalog = new RecentBoardCatalog(Path.Combine(root, "recent-board-paths.json"));
            foreach (var path in new[] { validPath, corruptPath, futurePath })
            {
                catalog.Record(path);
                catalog.RecordPage(path, sectionId, pageId);
            }

            var cards = await catalog.LoadCardsAsync(store);
            Assert.Equal(3, cards.Count);
            Assert.True(Assert.Single(cards, card => card.Path == validPath).IsAvailable);
            foreach (var invalidPath in new[] { corruptPath, futurePath })
            {
                var card = Assert.Single(cards, item => item.Path == invalidPath);
                Assert.False(card.IsAvailable);
                Assert.Contains("could not be opened", card.Title, StringComparison.OrdinalIgnoreCase);
            }

            var pages = await catalog.LoadRecentPagesAsync(store);
            var onlyValidPage = Assert.Single(pages);
            Assert.Equal(validPath, onlyValidPage.Path);
            Assert.Equal(pageId, onlyValidPage.PageId);

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.LoadCardsAsync(store, cancelled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.LoadRecentPagesAsync(store, cancelled.Token));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Landing_card_reports_missing_board_instead_of_fabricating_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-recents", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(Path.Combine(root, "store"));
            var missingPath = Path.Combine(root, "Missing.9to1board");
            var catalog = new RecentBoardCatalog(Path.Combine(root, "recent-board-paths.json"));
            catalog.Record(missingPath);

            var boardCard = Assert.Single(await catalog.LoadCardsAsync(store));
            Assert.False(boardCard.IsAvailable);
            Assert.Contains("unavailable", boardCard.Title, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await catalog.LoadRecentPagesAsync(store));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
