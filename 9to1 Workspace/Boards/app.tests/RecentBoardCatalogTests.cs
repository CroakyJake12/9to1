using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App.Tests;

public sealed class RecentBoardCatalogTests
{
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
