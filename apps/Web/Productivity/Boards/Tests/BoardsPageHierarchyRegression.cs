using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Productivity.Boards;

// Focused implementation smoke using the original Notes repository. No native,
// donor-parity, collaboration or release acceptance is implied.
internal static class BoardsPageHierarchyRegression
{
    public static async Task Run(INotesRepository repository)
    {
        var assertions = 0;
        void Check(bool passed, string name)
        { if (!passed) throw new InvalidOperationException(name); assertions++; Console.WriteLine("ASSERT " + name); }
        var owner = new BoardsWorkspaceService(repository);
        var core = new BoardsBrowserCore(repository);
        Check((await core.CreateAsync("Revision subjects")).Succeeded, "Create the actual owner-backed board");
        var original = core.GetDocumentSnapshot()!;
        var boardId = original.Id; var sectionId = original.Sections[0].Id; var maths = original.Sections[0].Pages[0].Id;
        var flatBefore = JsonSerializer.Serialize(original);
        Check(owner.ListPageHierarchy(original).All(item => item.ParentPageId is null && item.Depth == 0) &&
            JsonSerializer.Serialize(original) == flatBefore && !original.Metadata.ContainsKey(BoardsWorkspaceService.PageParentsKey),
            "Original flat documents remain roots and are not rewritten merely by reading hierarchy");
        await core.RenamePageAsync(maths, "Maths");
        var computing = (await core.AddPageAsync(sectionId, "Computer science")).Value;
        var algebra = (await core.AddChildPageAsync(maths, "Algebra")).Value;
        var quadratics = (await core.AddChildPageAsync(algebra, "Quadratics")).Value;
        var geometry = (await core.AddChildPageAsync(maths, "Geometry")).Value;
        var tree = Tree(owner, core);
        Check(tree.Single(item => item.Page.Id == algebra) is { ParentPageId: var algebraParent, Depth: 1 } && algebraParent == maths &&
            tree.Single(item => item.Page.Id == quadratics) is { ParentPageId: var quadraticParent, Depth: 2 } && quadraticParent == algebra,
            "Child and grandchild creation persist stable parent IDs on the original pages");
        Check(core.GetDocumentSnapshot()!.Metadata.ContainsKey(BoardsWorkspaceService.PageParentsKey) &&
            tree.Select(item => item.Page.Id).Distinct().Count() == tree.Length,
            "Versioned hierarchy metadata creates no duplicate page identities");
        await core.SelectPageAsync(quadratics);
        Check(Rows(core).Single(row => row.Id == quadratics) is { Depth: 2, Selected: true } &&
            core.TryGetValue("Breadcrumb", out var breadcrumb) && ((string)breadcrumb!).Contains("Maths / Algebra / Quadratics", StringComparison.Ordinal),
            "The same CUI tree, breadcrumb and selected editor resolve the nested page");
        Check((await core.SaveAsync()).Succeeded && !core.IsDirty, "Save the nested board through actual Notes CAS");
        var beforeCollapse = Canonical(core);
        await core.DispatchAsync("TogglePageBranch", Rows(core).Single(row => row.Id == maths));
        Check(Rows(core).All(row => row.Id != algebra && row.Id != quadratics) && !core.IsDirty && Canonical(core) == beforeCollapse,
            "Disclosure collapses descendants as client navigation state without altering saved content");
        await core.SelectPageAsync(quadratics);
        Check(Rows(core).Any(row => row.Id == quadratics) && !core.IsDirty,
            "Selecting a nested page reveals its collapsed ancestors without authoring");
        var beforeCycle = Canonical(core);
        Check(!(await core.ReparentPageAsync(maths, quadratics)).Succeeded && Canonical(core) == beforeCycle &&
            !(await core.ReparentPageAsync(algebra, Guid.NewGuid())).Succeeded && Canonical(core) == beforeCycle,
            "Cycles and orphan parent targets are denied without modifying the draft");
        var originalAlgebraBlocks = JsonSerializer.Serialize(Page(core, algebra).Blocks);
        var originalQuadraticBlocks = JsonSerializer.Serialize(Page(core, quadratics).Blocks);
        Check((await core.MoveSiblingPageAsync(geometry, -1)).Succeeded &&
            Tree(owner, core).Where(item => item.ParentPageId == maths).Select(item => item.Page.Id).SequenceEqual(new[] { geometry, algebra }),
            "Sibling movement reorders whole branches without mixing descendants into siblings");
        Check(JsonSerializer.Serialize(Page(core, algebra).Blocks) == originalAlgebraBlocks &&
            JsonSerializer.Serialize(Page(core, quadratics).Blocks) == originalQuadraticBlocks,
            "Branch movement preserves original content and block identities exactly");
        Check((await core.ReparentPageAsync(geometry, computing)).Succeeded &&
            Tree(owner, core).Single(item => item.Page.Id == geometry).ParentPageId == computing,
            "Reparenting attaches the same original page under another subject");
        await core.SetEditModeAsync(quadratics, BoardsPageEditMode.View);
        var beforeProtectedBranch = Canonical(core);
        Check(!(await core.ReparentPageAsync(algebra, computing)).Succeeded && Canonical(core) == beforeProtectedBranch,
            "A View-protected descendant prevents moving its containing branch");
        await core.SetEditModeAsync(quadratics, BoardsPageEditMode.Edit);
        await core.SelectPageAsync(algebra);
        await core.InsertRichTextAsync("Factorisation revision notes");
        Check(core.NativeRichEditor!.CanUndo, "The same shared rich editor owns text history before a page operation");
        Check((await core.ReparentPageAsync(algebra, null)).Succeeded && (await core.ReparentPageAsync(algebra, maths)).Succeeded &&
            !core.NativeRichEditor!.CanUndo && Page(core, algebra).Blocks.Any(block => string.Concat(block.Runs.Select(run => run.Text)).Contains("Factorisation", StringComparison.Ordinal)),
            "Owner page transactions preserve text and retire stale rich snapshots that could rewind hierarchy metadata");
        var beforeOldUndo = Canonical(core);
        Check(!(await core.UndoRichTextAsync()).Value && Canonical(core) == beforeOldUndo,
            "Retired rich undo cannot erase a newer persisted-parent operation");
        Check((await core.DeletePageAsync(maths)).Value, "Parent deletion uses existing recoverable page trash");
        tree = Tree(owner, core);
        Check(tree.All(item => item.Page.Id != maths) && tree.Single(item => item.Page.Id == algebra) is
            { ParentPageId: var trashedParent, VisibleParentPageId: null, Depth: 0 } && trashedParent == maths &&
            tree.Single(item => item.Page.Id == quadratics).VisibleParentPageId == algebra,
            "Subpages remain accessible below the nearest live ancestor while the original parent edge is preserved");
        Check((await core.SelectPageAsync(quadratics)).Succeeded, "A child below a trashed ancestor remains selectable and intact");
        Check((await core.RestorePageAsync(maths)).Value &&
            Tree(owner, core).Single(item => item.Page.Id == quadratics).Depth == 2 &&
            Tree(owner, core).Single(item => item.Page.Id == algebra).VisibleParentPageId == maths,
            "Recovering the parent restores original nested placement without copying pages");
        var targetSection = (await core.AddSectionAsync("Revision archive")).Value;
        Check((await core.MovePageAsync(maths, targetSection, 0)).Succeeded, "Existing cross-section MovePage API remains available");
        var moved = core.GetDocumentSnapshot()!;
        Check(new[] { maths, algebra, quadratics }.All(id => moved.Sections.Single(section => section.Id == targetSection).Pages.Any(page => page.Id == id)) &&
            moved.Sections.Single(section => section.Id == sectionId).Pages.Any(page => page.Id == computing) &&
            Tree(owner, core).Single(item => item.Page.Id == quadratics).ParentPageId == algebra,
            "Cross-section movement transfers the whole original branch and keeps all child links valid");
        var soloSectionId = (await core.AddSectionAsync("Single-page section")).Value;
        var soloPage = core.GetDocumentSnapshot()!.Sections.Single(section => section.Id == soloSectionId).Pages.Single().Id;
        var beforeEmptySection = Canonical(core);
        Check(!(await core.MovePageAsync(soloPage, targetSection, 0)).Succeeded && Canonical(core) == beforeEmptySection,
            "A subtree move cannot leave the original Notes section invalid and empty");
        Check((await core.SaveAsync()).Succeeded && !core.IsDirty, "Actual durable save acknowledges the reorganised board");
        var savedTree = Describe(Tree(owner, core));
        var savedQuadratics = JsonSerializer.Serialize(Page(core, quadratics));
        await core.CloseAsync(); await core.DisposeAsync();
        var reopened = new BoardsBrowserCore(repository);
        Check((await reopened.OpenAsync(boardId)).Succeeded && Describe(Tree(owner, reopened)) == savedTree &&
            JsonSerializer.Serialize(Page(reopened, quadratics)) == savedQuadratics,
            "A new core reopens exact hierarchy, page identities, child content and section placement");
        await reopened.SelectPageAsync(algebra);
        var readonlyCore = new BoardsBrowserCore(repository); await readonlyCore.OpenAsync(boardId, readOnly: true);
        var readonlyBefore = Canonical(readonlyCore);
        Check(!(await readonlyCore.AddChildPageAsync(maths, "Denied")).Succeeded &&
            !(await readonlyCore.ReparentPageAsync(algebra, null)).Succeeded && Canonical(readonlyCore) == readonlyBefore,
            "Read-only owner admission independently denies hierarchy mutation");
        await readonlyCore.CloseAsync(); await readonlyCore.DisposeAsync();
        var snapshot = reopened.GetDocumentSnapshot()!;
        snapshot.Metadata[BoardsWorkspaceService.PageParentsKey] = JsonSerializer.Serialize(new Dictionary<Guid, Guid> { [maths] = algebra, [algebra] = maths });
        var invalidBefore = JsonSerializer.Serialize(snapshot); var refused = false;
        try { owner.ListPageHierarchy(snapshot); } catch (InvalidDataException) { refused = true; }
        Check(refused && JsonSerializer.Serialize(snapshot) == invalidBefore,
            "Known cyclic metadata is refused without silently resetting or rewriting the original snapshot");
        snapshot.Metadata[BoardsWorkspaceService.PageParentsKey] = JsonSerializer.Serialize(new Dictionary<Guid, Guid> { [algebra] = Guid.NewGuid() });
        invalidBefore = JsonSerializer.Serialize(snapshot); refused = false;
        try { owner.ListPageHierarchy(snapshot); } catch (InvalidDataException) { refused = true; }
        Check(refused && JsonSerializer.Serialize(snapshot) == invalidBefore,
            "Orphan metadata is refused without replacing it with a new flat hierarchy");
        await reopened.CloseAsync(); await reopened.DisposeAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "hierarchy", assertions,
            scope = "Original shared Boards owner/repository basic implementation smoke; native, cross-client and release acceptance NOT RUN" }));
    }

    private static BoardsPageHierarchyItem[] Tree(BoardsWorkspaceService owner, BoardsBrowserCore core) => owner.ListPageHierarchy(core.GetDocumentSnapshot()!).ToArray();
    private static BoardsPageRow[] Rows(BoardsBrowserCore core)
    { if (!core.TryGetValue("PageRows", out var rows)) throw new InvalidOperationException("Missing real page rows."); return (BoardsPageRow[])rows!; }
    private static NotesPage Page(BoardsBrowserCore core, Guid id) => core.GetDocumentSnapshot()!.Sections.SelectMany(section => section.Pages).Single(page => page.Id == id);
    private static string Canonical(BoardsBrowserCore core) => JsonSerializer.Serialize(core.GetDocumentSnapshot());
    private static string Describe(IEnumerable<BoardsPageHierarchyItem> tree) => JsonSerializer.Serialize(tree.Select(item => new
        { item.Page.Id, item.Page.Title, item.SectionId, item.ParentPageId, item.VisibleParentPageId, item.Depth }));
}
