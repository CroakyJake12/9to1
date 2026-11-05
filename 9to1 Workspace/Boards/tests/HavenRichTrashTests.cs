using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

/// <summary>
/// Trash and restore for pages. UX27-BOARD-015 requires that trashing removes an entity from normal
/// navigation while retaining recoverable content and parent/order metadata, that deleting a container shows
/// its impact on descendants, and that a restore "does not silently orphan subpages". Boards previously had
/// only <c>RemovePage</c>, which deleted the page and its blocks outright and re-parented the children at the
/// moment of deletion - the opposite of every one of those clauses.
/// </summary>
public sealed class HavenRichTrashTests
{
    private static HavenRichNotes BoardWithTree()
    {
        // One section with a container page holding two subpages, and a leaf page elsewhere, so counts,
        // grouping and ordering can all be checked against real structure rather than a single flat page.
        var notes = HavenRichNotes.Create("Law");
        var section = notes.Sections[0];
        section.Title = "Workpacks";
        var container = section.Pages[0];
        container.Title = "Criminal Law";
        container.Blocks.Add(new HavenRichBlock { PlainText = "AR" });
        container.Blocks.Add(new HavenRichBlock { PlainText = "MR" });
        var first = new HavenRichPage { Title = "Actus reus", ParentPageId = container.Id };
        first.Blocks.Add(new HavenRichBlock { PlainText = "causation" });
        var second = new HavenRichPage { Title = "Mens rea", ParentPageId = container.Id };
        var leaf = new HavenRichPage { Title = "Homicide" };
        leaf.Blocks.Add(new HavenRichBlock { PlainText = "voluntary" });
        section.Pages.Add(first);
        section.Pages.Add(second);
        section.Pages.Add(leaf);
        HavenRichNotesValidator.Validate(notes);
        return notes;
    }

    [Fact]
    public void Trashing_a_leaf_page_keeps_its_content_and_records_where_it_was()
    {
        var notes = BoardWithTree();
        var section = notes.Sections[0];
        var leaf = section.Pages.Single(page => page.Title == "Homicide");
        var recordedIndex = section.Pages.IndexOf(leaf);

        Assert.True(HavenRichNotesOps.TryMovePageToTrash(notes, leaf.Id, out var refusal));
        Assert.Null(refusal);
        Assert.DoesNotContain(section.Pages, page => page.Id == leaf.Id);
        var entry = Assert.Single(notes.Trash);
        Assert.Equal(leaf.Id, entry.Page.Id);
        Assert.Equal("Homicide", entry.Page.Title);
        Assert.Single(entry.Page.Blocks, block => block.PlainText == "voluntary");
        Assert.Equal(section.Id, entry.SectionId);
        Assert.Null(entry.ParentPageId);
        Assert.Equal(recordedIndex, entry.SortIndex);
        Assert.True(entry.IsGroupRoot);
        Assert.NotEqual(default, entry.TrashedUtc);
        HavenRichNotesValidator.Validate(notes);
    }

    [Fact]
    public void Trashing_a_container_takes_its_subtree_as_one_group_and_lists_only_the_container()
    {
        var notes = BoardWithTree();
        var section = notes.Sections[0];
        var container = section.Pages.Single(page => page.Title == "Criminal Law");

        Assert.True(HavenRichNotesOps.TryMovePageToTrash(notes, container.Id, out _));
        Assert.DoesNotContain(section.Pages, page => page.ParentPageId == container.Id);
        var group = notes.Trash.Where(entry => entry.GroupId ==
            notes.Trash.Single(item => item.IsGroupRoot).GroupId).ToList();
        Assert.Equal(3, group.Count);
        Assert.Single(HavenRichNotesOps.TrashRoots(notes), root => root.Page.Title == "Criminal Law");
        Assert.Contains(group, entry => entry.Page.Title == "Actus reus" && entry.ParentPageId == container.Id);
        HavenRichNotesValidator.Validate(notes);
    }

    [Fact]
    public void Restore_puts_the_subtree_back_under_its_parent_at_the_recorded_place()
    {
        var notes = BoardWithTree();
        var section = notes.Sections[0];
        var container = section.Pages.Single(page => page.Title == "Criminal Law");
        var recordedIndex = section.Pages.IndexOf(container);
        var childIds = section.Pages.Where(page => page.ParentPageId == container.Id)
            .Select(page => page.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.True(HavenRichNotesOps.TryMovePageToTrash(notes, container.Id, out _));
        Assert.Equal(["Homicide"], section.Pages.Select(page => page.Title).ToArray());

        var outcome = HavenRichTrashRestoreFor(notes, container.Id);
        Assert.True(outcome.Succeeded);
        Assert.False(outcome.ParentWasMissing);
        Assert.Equal(section.Title, outcome.SectionTitle);
        var restored = section.Pages.Single(page => page.Title == "Criminal Law");
        // It came back in front of the page that stayed live, which is where it was recorded, rather than
        // being appended because a convenient insertion point was easier.
        Assert.True(section.Pages.IndexOf(restored) < section.Pages.IndexOf(
            section.Pages.Single(page => page.Title == "Homicide")),
            $"expected the restored page at {recordedIndex}, saw order {string.Join(", ", section.Pages.Select(page => page.Title))}");
        var backChildren = section.Pages.Where(page => page.ParentPageId == restored.Id)
            .Select(page => page.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(childIds, backChildren);
        // Content survives rather than a particular block count: a page carries its own default block, and
        // asserting the count here would test the fixture rather than the retention the requirement asks for.
        Assert.Contains(section.Pages.Single(page => page.Title == "Actus reus").Blocks,
            block => block.PlainText == "causation");
        Assert.Empty(notes.Trash);
        HavenRichNotesValidator.Validate(notes);
    }

    [Fact]
    public void Restore_says_so_when_the_recorded_parent_is_gone_instead_of_orphaning_the_page()
    {
        var notes = BoardWithTree();
        var section = notes.Sections[0];
        var child = section.Pages.Single(page => page.Title == "Actus reus");
        Assert.True(HavenRichNotesOps.TryMovePageToTrash(notes, child.Id, out _));

        // The parent is then trashed by itself, so the child's recorded parent is no longer restorable.
        var container = section.Pages.Single(page => page.Title == "Criminal Law");
        Assert.True(HavenRichNotesOps.TryMovePageToTrash(notes, container.Id, out _));

        var outcome = HavenRichTrashRestoreFor(notes, child.Id);
        Assert.True(outcome.Succeeded);
        Assert.True(outcome.ParentWasMissing,
            "the recorded parent is still in the Trash, so the restore has to admit it relocated the page");
        var restored = section.Pages.Single(page => page.Id == child.Id);
        Assert.True(restored.ParentPageId is null,
            "a live page may not point at a parent that stayed in the Trash - that is the silent orphaning the requirement forbids");
        HavenRichNotesValidator.Validate(notes);
    }

    [Fact]
    public void The_final_page_of_a_section_a_tab_pane_and_an_unknown_page_are_all_refused_with_a_reason()
    {
        var notes = new HavenRichNotes
        {
            Sections =
            [
                new HavenRichSection { Title = "Solo", Pages = [new HavenRichPage { Title = "Only page" }] }
            ]
        };
        HavenRichNotesValidator.Validate(notes);
        var solo = notes.Sections[0].Pages[0];
        Assert.False(HavenRichNotesOps.TryMovePageToTrash(notes, solo.Id, out var soloReason));
        Assert.Equal("The final page of a section cannot be trashed.", soloReason);

        Assert.False(HavenRichNotesOps.TryMovePageToTrash(notes, "page-does-not-exist", out var missing));
        Assert.Equal("That page is not in this board.", missing);

        var tabbed = BoardWithTree();
        var tabSection = tabbed.Sections[0];
        var owner = tabSection.Pages.Single(page => page.Title == "Homicide");
        var tabberBlock = new HavenRichBlock { Tabber = new HavenTabber() };
        owner.Blocks.Add(tabberBlock);
        var pane = new HavenRichPage { Title = "Pane", TabberOwnerBlockId = tabberBlock.Id };
        tabSection.Pages.Add(pane);
        Assert.False(HavenRichNotesOps.TryMovePageToTrash(tabbed, pane.Id, out var paneReason));
        Assert.Equal("This page is a tab inside another page; close that tab instead.", paneReason);

        // The pane is not abandoned when its owner is trashed: it travels with the owner and comes back with it.
        Assert.True(HavenRichNotesOps.TryMovePageToTrash(tabbed, owner.Id, out _));
        Assert.Contains(tabbed.Trash, entry => entry.Page.Title == "Pane");
        Assert.DoesNotContain(tabSection.Pages, page => page.Title == "Pane");
        var restored = HavenRichNotesOps.RestorePageFromTrash(tabbed, owner.Id);
        Assert.True(restored.Succeeded);
        Assert.Contains(tabSection.Pages, page => page.Title == "Pane");
    }

    [Fact]
    public void An_unknown_page_is_reported_as_absent_rather_than_as_a_page_with_nothing_in_it()
    {
        // Zero counts would be a real answer about an empty page, and the confirmation has to tell those two
        // apart - a blank popover beside a Move button is exactly what the requirement forbids.
        var notes = BoardWithTree();
        Assert.Null(HavenRichNotesOps.TrashImpact(notes, "page-not-here"));
        var known = notes.Sections[0].Pages.Single(page => page.Title == "Mens rea");
        var impact = HavenRichNotesOps.TrashImpact(notes, known.Id);
        Assert.True(impact is not null, "a page that is in the board gets a count, not null");
        Assert.Equal(0, impact!.Value.SubpageCount);
    }

    [Fact]
    public void Impact_counts_the_subpages_and_blocks_that_would_leave_navigation()
    {
        var notes = BoardWithTree();
        var container = notes.Sections[0].Pages.Single(page => page.Title == "Criminal Law");
        var counted = HavenRichNotesOps.TrashImpact(notes, container.Id)
            ?? throw new InvalidOperationException("the page is in the board");
        var (subpages, blocks) = counted;
        Assert.Equal(2, subpages);
        var expectedBlocks = notes.Sections[0].Pages
            .Where(page => page.Id == container.Id || page.ParentPageId == container.Id)
            .Sum(page => page.Blocks.Count);
        // The count has to be every block in the subtree, which is what the owner is being asked to accept.
        Assert.Equal(expectedBlocks, blocks);

        var leaf = notes.Sections[0].Pages.Single(page => page.Title == "Homicide");
        var leafImpact = HavenRichNotesOps.TrashImpact(notes, leaf.Id)
            ?? throw new InvalidOperationException("the page is in the board");
        var (leafSubpages, leafBlocks) = leafImpact;
        Assert.Equal(0, leafSubpages);
        // Counted against the fixture rather than assumed, so a fresh page's own default block cannot make
        // this test agree with a wrong implementation.
        Assert.Equal(leaf.Blocks.Count, leafBlocks);
    }

    [Fact]
    public async Task Trash_data_survives_a_real_save_and_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-trash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var boardPath = Path.Combine(root, "Law.9to1board");
            string pageId;
            await using (var session = await RichBoardSession.CreateNewAsync(new JsonFileHavenBoardStore(root), "Law"))
            {
                await session.SaveAsAsync(boardPath);
                var rich = session.Rich;
                rich.Sections[0].Pages[0].Title = "Criminal Law";
                rich.Sections[0].Pages[0].Blocks.Add(new HavenRichBlock { PlainText = "AR" });
                rich.Sections[0].Pages.Add(new HavenRichPage { Title = "Tort" });
                await session.SaveAsync();
                pageId = rich.Sections[0].Pages[0].Id;
                Assert.True(HavenRichNotesOps.TryMovePageToTrash(rich, pageId, out _));
                await session.SaveAsync();
            }

            var text = File.ReadAllText(boardPath);
            Assert.Contains("\"trash\"", text, StringComparison.OrdinalIgnoreCase);
            await using var reopened = await RichBoardSession.OpenAtPathAsync(new JsonFileHavenBoardStore(root), boardPath);
            var roots = HavenRichNotesOps.TrashRoots(reopened.Rich);
            var root0 = Assert.Single(roots);
            Assert.Equal("Criminal Law", root0.Page.Title);
            Assert.Single(root0.Page.Blocks, block => block.PlainText == "AR");
            Assert.Equal(pageId, root0.Page.Id);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void A_board_written_before_the_trash_field_existed_still_opens_with_an_empty_trash()
    {
        // The same compatibility rule the icon fields passed: absence is an empty Trash, not a schema change,
        // so every board already on disk keeps opening exactly as it did.
        var notes = BoardWithTree();
        var node = JsonSerializer.SerializeToNode(notes)!.AsObject();
        node.Remove("trash");
        var readBack = JsonSerializer.Deserialize<HavenRichNotes>(node.ToJsonString())!;
        HavenRichNotesValidator.Validate(readBack);
        Assert.Empty(readBack.Trash);
        Assert.NotEmpty(readBack.Sections);
    }

    [Fact]
    public void A_page_cannot_be_both_open_and_in_the_trash_and_a_stray_descendant_is_refused()
    {
        var notes = BoardWithTree();
        var section = notes.Sections[0];
        var leaf = section.Pages.Single(page => page.Title == "Homicide");
        notes.Trash.Add(new HavenRichTrashedPage
        {
            Page = leaf, GroupId = "g1", IsGroupRoot = true, SectionId = section.Id, SortIndex = 0,
            TrashedUtc = DateTimeOffset.UtcNow
        });
        var duplicate = Assert.Throws<InvalidOperationException>(() => HavenRichNotesValidator.Validate(notes));
        Assert.Contains("both open and in the Trash", duplicate.Message);
        notes.Trash.Clear();

        notes.Trash.Add(new HavenRichTrashedPage
        {
            Page = new HavenRichPage { Title = "Orphan", ParentPageId = "page-gone" }, GroupId = "g2",
            IsGroupRoot = false, SectionId = section.Id, SortIndex = 0, TrashedUtc = DateTimeOffset.UtcNow
        });
        var stray = Assert.Throws<InvalidOperationException>(() => HavenRichNotesValidator.Validate(notes));
        Assert.Contains("travel with the page it came from", stray.Message);
    }

    private static HavenRichTrashRestore HavenRichTrashRestoreFor(HavenRichNotes notes, string pageId) =>
        HavenRichNotesOps.RestorePageFromTrash(notes, pageId);

}
