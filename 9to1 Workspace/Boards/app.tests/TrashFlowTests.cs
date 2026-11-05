using Avalonia.Controls;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// The Trash flow as the owner's clicks reach it: the impact is stated before anything leaves navigation,
/// Cancel changes nothing, confirming moves the subtree out and offers exactly one restore entry, and a
/// restore that could not return to its recorded parent says so in the status line. These are the behaviours
/// pinned in docs/development/boards-trash-restore-2026-11-05.md.
/// </summary>
[Collection(BoardSessionTestCollection.Name)]
public sealed class TrashFlowTests
{
    private static (BoardsViewModel ViewModel, InMemoryRichBoardSession Session, string SectionId,
        string PageId, string ChildId) Setup()
    {
        var session = new InMemoryRichBoardSession();
        session.OpenAsync($"memory://boards/trash-flow-{Guid.NewGuid():N}");
        var section = session.Document.Sections[0];
        var container = section.Pages[0];
        container.Title = "Criminal Law";
        container.Blocks.Add(new RichBoardBlock { Text = "AR" });
        container.Blocks.Add(new RichBoardBlock { Text = "MR" });
        var child = new RichBoardPage { Title = "Actus reus", ParentPageId = container.Id };
        child.Blocks.Add(new RichBoardBlock { Text = "causation" });
        section.Pages.Add(child);
        var leaf = new RichBoardPage { Title = "Homicide" };
        leaf.Blocks.Add(new RichBoardBlock { Text = "voluntary manslaughter" });
        section.Pages.Add(leaf);
        var viewModel = new BoardsViewModel(session);
        TestUiThread.Run(() =>
        {
            if (!viewModel.IsEditMode) viewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
        });
        return (viewModel, session, section.Id, container.Id, child.Id);
    }

    [Fact]
    public void The_confirmation_states_the_impact_before_anything_leaves_navigation()
    {
        var (viewModel, session, sectionId, pageId, childId) = Setup();
        try
        {
            var pages = session.Document.Sections[0].Pages;
            var page = pages.Single(item => item.Id == pageId);
            var expectedBlocks = page.Blocks.Count +
                pages.Single(item => item.Id == childId).Blocks.Count;

            TestUiThread.Run(() => viewModel.PromptMovePageToTrashAsync(sectionId, pageId)
                .GetAwaiter().GetResult());

            Assert.True(viewModel.Get("IsTrashConfirmOpen") as bool?, "the confirmation should be open");
            Assert.Contains("Criminal Law", (string)viewModel.Get("TrashConfirmTitle")!);
            var impact = (string)viewModel.Get("TrashConfirmImpact")!;
            Assert.Contains("1 subpage", impact);
            Assert.Contains($"{expectedBlocks} blocks", impact);
            // A container must not be described with a placeholder-looking count.
            Assert.False(impact.Contains("0 subpages"), $"a container must not read like a leaf: {impact}");
            // Nothing has happened yet: the decision comes before the change.
            Assert.Contains(pages, item => item.Id == pageId);
            Assert.Contains(pages, item => item.Id == childId);
            Assert.Empty(session.Document.Trash);
        }
        finally { TestUiThread.Run(viewModel.Dispose); }
    }

    [Fact]
    public void A_leaf_page_confirmation_says_so_rather_than_reading_like_a_placeholder_count()
    {
        var (viewModel, session, sectionId, _, _) = Setup();
        try
        {
            var leaf = session.Document.Sections[0].Pages.Single(page => page.Title == "Homicide");
            TestUiThread.Run(() => viewModel.PromptMovePageToTrashAsync(sectionId, leaf.Id)
                .GetAwaiter().GetResult());
            var impact = (string)viewModel.Get("TrashConfirmImpact")!;
            Assert.StartsWith("No subpages", impact);
            Assert.False(impact.Contains("0 "), $"the wording should read plainly, saw: {impact}");
        }
        finally { TestUiThread.Run(viewModel.Dispose); }
    }

    [Fact]
    public void Cancel_leaves_the_tree_exactly_as_it_was()
    {
        var (viewModel, session, sectionId, pageId, childId) = Setup();
        try
        {
            TestUiThread.Run(() =>
            {
                viewModel.PromptMovePageToTrashAsync(sectionId, pageId).GetAwaiter().GetResult();
                Assert.True(viewModel.Get("IsTrashConfirmOpen") as bool?);
                viewModel.CancelTrashConfirm();
            });
            Assert.False(viewModel.Get("IsTrashConfirmOpen") as bool?);
            var pages = session.Document.Sections[0].Pages;
            Assert.Contains(pages, page => page.Id == pageId);
            Assert.Contains(pages, page => page.Id == childId);
            Assert.Empty(session.Document.Trash);
        }
        finally { TestUiThread.Run(viewModel.Dispose); }
    }

    [Fact]
    public void Confirming_moves_the_subtree_out_and_the_panel_offers_one_restore_row()
    {
        var (viewModel, session, sectionId, pageId, childId) = Setup();
        try
        {
            TestUiThread.Run(() =>
            {
                viewModel.PromptMovePageToTrashAsync(sectionId, pageId).GetAwaiter().GetResult();
                viewModel.ConfirmMovePageToTrashAsync().GetAwaiter().GetResult();
            });
            var pages = session.Document.Sections[0].Pages;
            Assert.DoesNotContain(pages, page => page.Id == pageId);
            Assert.DoesNotContain(pages, page => page.Id == childId);
            Assert.False(viewModel.Get("IsTrashConfirmOpen") as bool?, "the confirmation closes with its decision");
            Assert.Contains("Moved to the Trash", (string)viewModel.Get("StatusText")!);
            Assert.True(((string)viewModel.Get("StatusText")!).Contains("restore",
                StringComparison.OrdinalIgnoreCase), "the status has to point at the way back");

            TestUiThread.Run(viewModel.OpenTrashPanel);
            Assert.True(viewModel.Get("IsTrashPanelOpen") as bool?);
            Assert.True(viewModel.Get("HasTrashEntries") as bool?);
            Assert.False(viewModel.Get("HasNoTrashEntries") as bool?);
            var entries = Assert.IsAssignableFrom<IReadOnlyList<RichBoardTrashEntry>>(viewModel.Get("TrashEntries"));
            var entry = Assert.Single(entries);
            Assert.Equal("Criminal Law", entry.Title);
            Assert.Equal(1, entry.SubtreePageCount);

            TestUiThread.Run(() => viewModel.RestoreTrashedPageAsync(entry).GetAwaiter().GetResult());
            var restored = session.Document.Sections[0].Pages;
            Assert.Contains(restored, page => page.Id == pageId);
            Assert.Contains(restored, page => page.Id == childId);
            Assert.Equal(pageId, restored.Single(page => page.Id == childId).ParentPageId);
            Assert.Contains("Restored Criminal Law", (string)viewModel.Get("StatusText")!);
            Assert.False(viewModel.Get("HasTrashEntries") as bool?, "the row leaves the panel once it is restored");
            Assert.True(viewModel.Get("HasNoTrashEntries") as bool?);
        }
        finally { TestUiThread.Run(viewModel.Dispose); }
    }

    [Fact]
    public void A_restore_that_could_not_return_to_its_parent_says_where_the_page_went()
    {
        var (viewModel, session, sectionId, pageId, childId) = Setup();
        try
        {
            // Child first, then its parent: restoring the child now has a recorded parent still in the Trash.
            TestUiThread.Run(() =>
            {
                viewModel.PromptMovePageToTrashAsync(sectionId, childId).GetAwaiter().GetResult();
                viewModel.ConfirmMovePageToTrashAsync().GetAwaiter().GetResult();
                viewModel.PromptMovePageToTrashAsync(sectionId, pageId).GetAwaiter().GetResult();
                viewModel.ConfirmMovePageToTrashAsync().GetAwaiter().GetResult();
                viewModel.OpenTrashPanel();
            });
            var entries = Assert.IsAssignableFrom<IReadOnlyList<RichBoardTrashEntry>>(viewModel.Get("TrashEntries"));
            var childEntry = entries.Single(entry => entry.Title == "Actus reus");
            TestUiThread.Run(() => viewModel.RestoreTrashedPageAsync(childEntry).GetAwaiter().GetResult());

            var status = (string)viewModel.Get("StatusText")!;
            Assert.Contains("Restored Actus reus", status);
            // The relocation has to be stated, not left for the owner to discover in the tree.
            Assert.True(status.Contains("original parent page is in the Trash"),
                $"the status must say where the page came back: {status}");
            // The restored page must not keep a pointer into the Trash.
            Assert.True(session.Document.Sections[0].Pages.Single(page => page.Id == childId).ParentPageId is null);
        }
        finally { TestUiThread.Run(viewModel.Dispose); }
    }

    [Fact]
    public async Task A_refused_trash_says_why_and_leaves_the_tree_untouched()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/trash-refusal-{Guid.NewGuid():N}");
        var viewModel = new BoardsViewModel(session);
        try
        {
            TestUiThread.Run(() =>
            {
                if (!viewModel.IsEditMode) viewModel.DispatchAsync("ToggleEditView", null).GetAwaiter().GetResult();
                // The only page in the section cannot be trashed, so the confirmation's accept must report it.
                var only = session.Document.Sections[0].Pages[0];
                viewModel.PromptMovePageToTrashAsync(session.Document.Sections[0].Id, only.Id).GetAwaiter().GetResult();
                if (viewModel.Get("IsTrashConfirmOpen") as bool? == true)
                    viewModel.ConfirmMovePageToTrashAsync().GetAwaiter().GetResult();
            });
            var status = (string)viewModel.Get("StatusText")!;
            Assert.True(status.Contains("final page") || status.Contains("not in this board"),
                $"a refusal has to give the owner a reason, saw: {status}");
            Assert.Single(session.Document.Sections[0].Pages);
            Assert.Empty(session.Document.Trash);
        }
        finally { TestUiThread.Run(viewModel.Dispose); }
    }

    /// <summary>
    /// The authored surfaces and their actions: the confirmation panel and the Trash panel have to exist in a
    /// real rendered surface, their controls have to be realised, and the row's Restore action has to carry the
    /// entry it was drawn for back to the command. A panel that only exists in the view model is invisible.
    /// </summary>
    /// <summary>
    /// The entry point the owner actually reaches: the page row's menu. The recoverable route replaces hard
    /// delete there rather than sitting beside it, because two destructive-sounding rows in one menu invite
    /// the wrong click, and UX27-BOARD-015 describes the recoverable one.
    /// </summary>
    [Fact]
    public void A_page_row_offers_Move_to_Trash_and_no_hard_delete_row()
    {
        var surface = BoardsRenderedSurface.Open("pagemenu");
        try
        {
            var menus = surface.NavContextMenus();
            Assert.NotEmpty(menus);
            var pageMenus = menus.Where(headers => headers.Contains("Add subpage")).ToList();
            // The fixture board's page rows should have built their menus.
            Assert.True(pageMenus.Count > 0, $"no page row menu was realised, saw menus: {menus}");
            foreach (var headers in pageMenus)
            {
                Assert.Contains("Move to Trash…", headers);
                Assert.DoesNotContain("Delete", headers);
                // The rest of the row's actions stay available.
                Assert.True(headers.Contains("Rename"), $"the row lost other actions: {headers}");
                Assert.Contains("Icon and color…", headers);
            }
        }
        finally
        {
            surface.Close();
        }
    }

    /// <summary>
    /// Live pages of a section, re-read from the session each time. A store-backed session re-projects its view
    /// model after a mutation, so a section reference held across a click is a snapshot, not the current tree.
    /// </summary>
    private static IReadOnlyList<RichBoardPage> LivePages(BoardsViewModel viewModel, string sectionId) =>
        viewModel.Session.Document.Sections.Single(section => section.Id == sectionId).Pages;

    [Fact]
    public void The_confirmation_and_the_Trash_panel_are_rendered_surfaces_with_working_targets()
    {
        var surface = BoardsRenderedSurface.Open("trashpanel");
        try
        {
            var viewModel = surface.ViewModel;
            surface.EnableEdit();
            var section = viewModel.Session.Document.Sections[0];
            var page = section.Pages[0];

            TestUiThread.Run(() =>
            {
                viewModel.PromptMovePageToTrashAsync(section.Id, page.Id).GetAwaiter().GetResult();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            var confirm = surface.Find<Control>("TrashConfirmPanel");
            Assert.NotNull(confirm);
            Assert.True(surface.Shown(confirm!), "the impact confirmation is drawn");
            var title = surface.Find<TextBlock>("TrashConfirmTitle");
            Assert.NotNull(title);
            Assert.False(string.IsNullOrWhiteSpace(surface.TextOf(title!)), "the confirmation names the page");
            var impactText = surface.Find<TextBlock>("TrashConfirmImpact");
            Assert.NotNull(impactText);
            Assert.False(string.IsNullOrWhiteSpace(surface.TextOf(impactText!)), "the confirmation states the impact");

            var move = surface.Find<Button>("TrashConfirmMove");
            Assert.NotNull(move);
            surface.Click(move!);
            // The document is re-read rather than using the section captured before the click: a store-backed
            // session re-projects its view model after every mutation, so the old section object is a snapshot
            // of the tree as it was. The status line is quoted because the confirm can fail in three ways -
            // the action not routing, the command refusing, or the panel state not reaching the session.
            var pagesAfterConfirm = LivePages(viewModel, section.Id);
            Assert.True(pagesAfterConfirm.All(item => item.Id != page.Id),
                $"the authored confirm did not move the page to the Trash; status said: " +
                $"'{viewModel.Get("StatusText")}', confirm open: {viewModel.Get("IsTrashConfirmOpen")}");
            Assert.True((bool?)viewModel.Get("IsTrashPanelOpen") == false, "the confirmation closes on its decision");

            TestUiThread.Run(() =>
            {
                viewModel.OpenTrashPanel();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            var panel = surface.Find<Control>("TrashPanel");
            Assert.NotNull(panel);
            Assert.True(surface.Shown(panel!), "the Trash panel is drawn when opened");

            var restore = surface.ButtonWithAccessibleName("Restore this page from the Trash");
            Assert.NotNull(restore);
            surface.Click(restore!);
            Assert.Contains(LivePages(viewModel, section.Id), item => item.Id == page.Id);
            Assert.Contains("Restored", (string)viewModel.Get("StatusText")!);

            // A page that cannot be trashed is answered in the status line rather than with a popover whose
            // accept button is known in advance to do nothing (canonical v2 line 9397).
            var remaining = LivePages(viewModel, section.Id).Where(item => item.TabberOwnerBlockId is null).ToList();
            var onlyPageLeft = remaining.Count == 1 ? remaining[0] : null;
            if (onlyPageLeft is not null)
            {
                TestUiThread.Run(() =>
                {
                    viewModel.PromptMovePageToTrashAsync(section.Id, onlyPageLeft.Id).GetAwaiter().GetResult();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                });
                Assert.False(viewModel.Get("IsTrashConfirmOpen") as bool?,
                    "no confirmation is offered for an action that would be refused");
                Assert.Contains("final page", (string)viewModel.Get("StatusText")!);
            }
        }
        finally
        {
            surface.Close();
        }
    }
}
