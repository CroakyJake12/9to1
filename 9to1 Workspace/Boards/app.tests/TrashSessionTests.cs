using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// The Trash route as the application reaches it: through a session, against the projected document the
/// navigation and the Trash panel read. The contract layer is already pinned by its own suite; what these
/// tests hold is that a session does the same thing, that the refusal reasons arrive intact, and that a
/// trashed page survives the application's own save-and-reopen - the case the owner actually hits.
/// </summary>
public sealed class TrashSessionTests
{
    [Fact]
    public async Task Trashing_a_page_takes_the_subtree_out_of_navigation_and_offers_one_restore_entry()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/trash-{Guid.NewGuid():N}");
        var section = session.Document.Sections[0];
        var container = section.Pages[0];
        container.Title = "Criminal Law";
        section.Pages.Add(new RichBoardPage { Title = "Actus reus", ParentPageId = container.Id });
        section.Pages.Add(new RichBoardPage { Title = "Homicide" });

        var impact = await session.GetTrashImpactAsync(container.Id);
        Assert.Equal(1, impact.SubpageCount);
        Assert.True(impact.BlockCount >= 1, "the confirmation has to count the blocks that would leave");

        var result = await session.MovePageToTrashAsync(container.Id);
        Assert.True(result.Succeeded, result.Reason ?? "the trash should have been accepted");
        Assert.DoesNotContain(section.Pages, page => page.Id == container.Id);
        Assert.DoesNotContain(section.Pages, page => page.ParentPageId == container.Id);

        var entry = Assert.Single(session.Document.Trash);
        Assert.Equal("Criminal Law", entry.Title);
        Assert.Equal(1, entry.SubtreePageCount);
        Assert.Equal(section.Title, entry.SectionTitle);
        Assert.True(entry.BlockCount >= 1);
    }

    [Fact]
    public async Task Refusals_arrive_with_their_reason_instead_of_a_silent_nothing()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/trash-{Guid.NewGuid():N}");
        var section = session.Document.Sections[0];

        var lastPage = Assert.Single(section.Pages);
        var refused = await session.MovePageToTrashAsync(lastPage.Id);
        Assert.False(refused.Succeeded);
        Assert.Equal("The final page of a section cannot be trashed.", refused.Reason);

        var unknown = await session.MovePageToTrashAsync("page-not-here");
        Assert.False(unknown.Succeeded);
        Assert.Equal("That page is not in this board.", unknown.Reason);

        var restoreUnknown = await session.RestorePageFromTrashAsync("page-not-here");
        Assert.False(restoreUnknown.Succeeded);
        Assert.Equal("That page is not in the Trash.", restoreUnknown.Reason);
    }

    [Fact]
    public async Task Restore_returns_the_subtree_and_reports_when_it_could_not_go_back_where_it_was()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/trash-{Guid.NewGuid():N}");
        var section = session.Document.Sections[0];
        var container = section.Pages[0];
        container.Title = "Criminal Law";
        var child = new RichBoardPage { Title = "Actus reus", ParentPageId = container.Id };
        section.Pages.Add(child);
        section.Pages.Add(new RichBoardPage { Title = "Homicide" });

        Assert.True((await session.MovePageToTrashAsync(container.Id)).Succeeded);
        var restored = await session.RestorePageFromTrashAsync(container.Id);
        Assert.True(restored.Succeeded, restored.Reason ?? "restore should have succeeded");
        Assert.False(restored.RelocatedToSectionRoot);
        Assert.Equal(section.Title, restored.SectionTitle);
        Assert.Contains(section.Pages, page => page.Id == container.Id);
        Assert.Contains(section.Pages, page => page.Id == child.Id);
        Assert.Equal(container.Id, section.Pages.Single(page => page.Id == child.Id).ParentPageId);
        Assert.Empty(session.Document.Trash);

        // A different order, to make the relocation case real rather than theoretical: trash the child on its
        // own, then its parent. Restoring the child now has a recorded parent that is still in the Trash, and
        // the result has to say the page was relocated instead of quietly pointing at a page that is not there.
        Assert.True((await session.MovePageToTrashAsync(child.Id)).Succeeded);
        Assert.True((await session.MovePageToTrashAsync(container.Id)).Succeeded);

        var relocated = await session.RestorePageFromTrashAsync(child.Id);
        Assert.True(relocated.Succeeded, relocated.Reason ?? "the page should come back rather than be refused");
        Assert.True(relocated.RelocatedToSectionRoot,
            "the recorded parent is still in the Trash, so the restore must admit the relocation");
        var backChild = section.Pages.Single(page => page.Id == child.Id);
        Assert.True(backChild.ParentPageId is null,
            "a live page may not keep a parent pointer into the Trash - that is the silent orphaning the requirement forbids");
    }

    [Fact]
    public async Task A_trashed_page_survives_the_applications_own_save_and_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-trash-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var boardPath = Path.Combine(root, "Law.9to1board");
            string sectionId;
            string pageId;
            string childId;
            await using (var session = await ContractSessionAdapter.CreateNewAtPathAsync(
                             new JsonFileHavenBoardStore(root), boardPath))
            {
                // Built through the same session calls the application makes, so this is the owner's route
                // rather than a hand-written file: a page, a subpage under it, and a sibling.
                var section = session.Document.Sections[0];
                sectionId = section.Id;
                pageId = section.Pages[0].Id;
                await session.RenamePageAsync(pageId, "Criminal Law");
                childId = await session.AddSubpageAsync(sectionId, pageId, "Actus reus");
                await session.AddPageAsync(sectionId, "Homicide");
                var trashed = await session.MovePageToTrashAsync(pageId);
                Assert.True(trashed.Succeeded, trashed.Reason ?? "the trash should have been accepted");
                await session.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(
                new JsonFileHavenBoardStore(root), boardPath);
            var entry = Assert.Single(reopened.Document.Trash);
            Assert.Equal("Criminal Law", entry.Title);
            Assert.Equal(1, entry.SubtreePageCount);
            Assert.DoesNotContain(reopened.Document.Sections.Single(section => section.Id == sectionId).Pages,
                page => page.Id == pageId);

            var restored = await reopened.RestorePageFromTrashAsync(pageId);
            Assert.True(restored.Succeeded, restored.Reason ?? "restore should have worked after reopen");
            var pages = reopened.Document.Sections.Single(section => section.Id == sectionId).Pages;
            Assert.Contains(pages, page => page.Id == pageId);
            Assert.Contains(pages, page => page.Id == childId);
            // The subpage comes back under the same parent, not as a new orphan.
            Assert.Equal(pageId, pages.Single(page => page.Id == childId).ParentPageId);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
