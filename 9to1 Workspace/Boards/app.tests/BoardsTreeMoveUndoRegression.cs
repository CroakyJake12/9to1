// BF-06 (ACCEPTANCE.md) — Board page tree moves with exact undo.
// Assertions are derived from the v3 Boards contract (V3-BOARDS-FEA-01, V3-BOARDS-ENT-01)
// and the acceptance recipe, not from current implementation output:
//  * nest/reorder keep page identities and subtree references valid;
//  * every supported reversible move restores the complete tree, validated against the
//    RESTORATION destination rather than the node's current parent;
//  * a move into itself or a descendant is refused without mutation and without consuming
//    an undo step;
//  * multi-move history unwinds last-in-first-out.
// Fixtures are generated in a temporary directory only; no real study board is touched.

using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsTreeMoveUndoRegression
{
    // (root page count, index of the moved page, index of the destination parent,
    //  children already under that parent) — covers old-parent counts 2/3/5, original
    //  first/middle/last positions and new-parent counts 0/1/4.
    [Theory]
    [InlineData(2, 0, 1, 0)]
    [InlineData(3, 1, 0, 0)]   // the recipe's named case: index 1 into an empty parent
    [InlineData(3, 0, 2, 1)]
    [InlineData(3, 2, 0, 4)]
    [InlineData(5, 2, 3, 1)]
    [InlineData(5, 4, 0, 0)]
    public async Task Cross_parent_move_then_undo_restores_the_original_destination(
        int rootCount, int movedIndex, int parentIndex, int existingChildren)
    {
        var (path, store, root) = TempBoard("tree-cross-parent");
        try
        {
            await SeedAsync(path, rootCount, existingChildren, extraSection: false);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var movedId = PageId(movedIndex);
            var parentId = PageId(parentIndex);
            var before = Shape(adapter);

            await adapter.SetPageParentAsync(movedId, parentId);
            AssertTreeInvariants(adapter);
            Assert.Equal(parentId, adapter.Document.Sections[0].Pages
                .Single(page => page.Id == movedId).ParentPageId);

            Assert.True(await adapter.UndoAsync());
            Assert.Equal(before, Shape(adapter));
            // The restored node must sit where it was before the move, independent of the
            // parent it had just been nested under.
            var section = adapter.Document.Sections[0];
            Assert.Null(section.Pages.Single(page => page.Id == movedId).ParentPageId);
            Assert.Equal(movedId, FlatIds(section)[movedIndex]);
            AssertTreeInvariants(adapter);
        }
        finally { Delete(store, root); }
    }

    // (sibling count, original index, requested destination index) — same-parent reorder
    // with the destination at the beginning and at the end of the sibling list.
    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(3, 0, 1)]
    [InlineData(3, 2, 0)]
    [InlineData(5, 1, 2)]
    [InlineData(5, 3, 4)]
    [InlineData(5, 0, 4)]
    [InlineData(5, 4, 0)]
    public async Task Same_parent_reorder_undoes_to_the_exact_previous_order(int count, int index, int target)
    {
        var (path, store, root) = TempBoard("tree-reorder");
        try
        {
            await SeedAsync(path, count, 0, extraSection: false);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var sectionId = adapter.Document.Sections[0].Id;
            var before = FlatIds(adapter.Document.Sections[0]);
            var movedId = before[index];

            await adapter.MovePageAsync(movedId, sectionId, target);
            var after = FlatIds(adapter.Document.Sections[0]);
            Assert.Equal(count, after.Count);
            Assert.Equal(movedId, after[Math.Min(target, count - 1)]);
            AssertTreeInvariants(adapter);

            Assert.True(await adapter.UndoAsync());
            Assert.Equal(before, FlatIds(adapter.Document.Sections[0]));
        }
        finally { Delete(store, root); }
    }

    [Fact]
    public async Task Several_moves_undo_last_in_first_out()
    {
        var (path, store, root) = TempBoard("tree-lifo");
        try
        {
            await SeedAsync(path, 3, 1, extraSection: false);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var sectionId = adapter.Document.Sections[0].Id;
            var history = new List<string> { Shape(adapter) };

            await adapter.MovePageAsync(PageId(0), sectionId, 2);
            history.Add(Shape(adapter));
            await adapter.SetPageParentAsync(PageId(1), PageId(2));
            history.Add(Shape(adapter));
            await adapter.MovePageAsync(PageId(1), sectionId, 0);
            history.Add(Shape(adapter));
            Assert.True(history.Count == 4);

            for (var step = 3; step >= 1; step--)
            {
                Assert.True(await adapter.UndoAsync());
                Assert.Equal(history[step - 1], Shape(adapter));
                AssertTreeInvariants(adapter);
            }
        }
        finally { Delete(store, root); }
    }

    [Fact]
    public async Task A_refused_move_mutates_nothing_and_consumes_no_undo_step()
    {
        var (path, store, root) = TempBoard("tree-refused");
        try
        {
            await SeedAsync(path, 2, 0, extraSection: false);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);

            _ = await adapter.AddPageAsync(adapter.Document.Sections[0].Id, "Third page");
            var afterFirstTree = Shape(adapter);

            // page 1 under page 0 …
            await adapter.SetPageParentAsync(PageId(1), PageId(0));
            var nested = Shape(adapter);
            Assert.NotEqual(afterFirstTree, nested);

            // … so nesting page 0 beneath its own descendant must be refused.
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await adapter.SetPageParentAsync(PageId(0), PageId(1)));

            Assert.Equal(nested, Shape(adapter));
            AssertTreeInvariants(adapter);

            // One undo has to remove the refused attempt's *predecessor*, proving the refusal
            // did not consume an empty history step.
            Assert.True(await adapter.UndoAsync());
            Assert.Equal(afterFirstTree, Shape(adapter));
        }
        finally { Delete(store, root); }
    }

    [Fact]
    public async Task A_cross_section_move_carries_the_nested_subtree_and_undoes_exactly()
    {
        var (path, store, root) = TempBoard("tree-cross-section");
        try
        {
            await SeedAsync(path, 2, 2, extraSection: true);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var sourceId = adapter.Document.Sections[0].Id;
            var targetId = adapter.Document.Sections[1].Id;
            var before = Shape(adapter);
            var parentId = PageId(0);
            var childIds = adapter.Document.Sections.SelectMany(section => section.Pages)
                .Where(page => page.ParentPageId == parentId)
                .Select(page => page.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            Assert.NotEmpty(childIds);

            await adapter.MovePageAsync(parentId, targetId, 0);

            var target = adapter.Document.Sections.Single(section => section.Id == targetId);
            var source = adapter.Document.Sections.Single(section => section.Id == sourceId);
            Assert.Equal(parentId, FlatIds(target)[0]);
            foreach (var child in childIds)
            {
                Assert.Contains(child, FlatIds(target));
                Assert.DoesNotContain(child, FlatIds(source));
            }
            AssertTreeInvariants(adapter);

            Assert.True(await adapter.UndoAsync());
            Assert.Equal(before, Shape(adapter));
            AssertTreeInvariants(adapter);
        }
        finally { Delete(store, root); }
    }

    private static async Task SeedAsync(string path, int rootCount, int childrenUnderFirstPage, bool extraSection)
    {
        var directory = Path.GetDirectoryName(path)!;
        await using var owner = await RichBoardSession.CreateNewAsync(new JsonFileHavenBoardStore(directory), "Move undo board");
        await owner.MutateAsync(rich =>
        {
            var section = rich.Sections[0];
            section.Title = "Product";
            section.Pages = [.. Enumerable.Range(0, rootCount).Select(index =>
                new HavenRichPage { Id = PageId(index), Title = "Page " + index, Order = index })];
            for (var child = 0; child < childrenUnderFirstPage; child++)
                section.Pages.Add(new HavenRichPage
                {
                    Id = "child-" + child,
                    Title = "Child " + child,
                    ParentPageId = PageId(0),
                    Order = rootCount + child,
                });
            if (extraSection)
                rich.Sections.Add(new HavenRichSection
                {
                    Title = "Reference",
                    Pages = [new HavenRichPage { Id = "page-other-0", Title = "Other page", Order = 0 }],
                });
        });
        await owner.SaveAsAsync(path);
    }

    private static string PageId(int index) => "page-" + index.ToString("D3");

    private static List<string> FlatIds(RichBoardSection section) =>
        section.Pages.Select(page => page.Id).ToList();

    /// <summary>The stored tree the user sees: section order, then each section's page list
    /// with every page's parent reference.</summary>
    private static string Shape(ContractSessionAdapter adapter)
    {
        var sections = new List<string>();
        foreach (var section in adapter.Document.Sections)
        {
            var pages = new List<string>();
            foreach (var page in section.Pages)
                pages.Add(page.ParentPageId is null ? page.Id : page.Id + "@" + page.ParentPageId);
            sections.Add(section.Id + ":" + string.Join(",", pages));
        }
        return string.Join("|", sections);
    }

    private static void AssertTreeInvariants(ContractSessionAdapter adapter)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in adapter.Document.Sections)
        {
            foreach (var page in section.Pages)
            {
                Assert.True(ids.Add(page.Id), "A page ID was duplicated by the move: " + page.Id);
                if (page.ParentPageId is { Length: > 0 } parent)
                    Assert.True(section.Pages.Any(candidate => candidate.Id == parent),
                        $"Page '{page.Id}' references parent '{parent}' outside its own section.");
            }
        }
    }

    private static (string Path, JsonFileHavenBoardStore Store, string Root) TempBoard(string label)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-" + label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (Path.Combine(root, "board.9to1board"), new JsonFileHavenBoardStore(root), root);
    }

    private static void Delete(JsonFileHavenBoardStore store, string root)
    {
        store.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
