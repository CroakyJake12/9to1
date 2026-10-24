using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Container hierarchy over the original Notes pages. ParentPageId is
/// persisted; VisibleParentPageId skips trashed ancestors for navigation only.</summary>
public sealed record BoardsPageHierarchyItem(NotesPage Page, Guid SectionId, Guid? ParentPageId,
    Guid? VisibleParentPageId, int Depth, bool HasChildren);

public sealed partial class BoardsWorkspaceService
{
    public const string PageParentsKey = "boards.page-parents.v1";

    // Missing metadata is the original flat document. Invalid known metadata is
    // refused, never replaced with an empty map or silently normalized on read.
    private static Dictionary<Guid, Guid> ReadPageParents(NotesDocument notebook)
    {
        EnsureBoards(notebook);
        if (!notebook.Metadata.TryGetValue(PageParentsKey, out var raw)) return [];
        Dictionary<Guid, Guid> parents;
        try { parents = JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(raw, Json) ?? throw new InvalidDataException("Boards page hierarchy is empty or invalid."); }
        catch (JsonException error) { throw new InvalidDataException("Boards page hierarchy could not be read. The original document is preserved.", error); }
        var pages = notebook.Sections.SelectMany(section => section.Pages.Select(page => (Page: page, Section: section.Id)))
            .ToDictionary(item => item.Page.Id);
        foreach (var pair in parents)
            if (pair.Key == Guid.Empty || pair.Value == Guid.Empty || pair.Key == pair.Value ||
                !pages.TryGetValue(pair.Key, out var child) || !pages.TryGetValue(pair.Value, out var parent) || child.Section != parent.Section)
                throw new InvalidDataException("Boards page hierarchy contains an orphan, self-parent or cross-section link.");
        var done = new HashSet<Guid>();
        foreach (var start in parents.Keys)
        {
            var path = new HashSet<Guid>(); var cursor = start;
            while (!done.Contains(cursor) && parents.TryGetValue(cursor, out var parent))
            {
                if (!path.Add(cursor)) throw new InvalidDataException("Boards page hierarchy contains a cycle.");
                cursor = parent;
            }
            done.UnionWith(path);
        }
        return parents;
    }
    private static void WritePageParents(NotesDocument notebook, Dictionary<Guid, Guid> parents)
    {
        if (parents.Count == 0) notebook.Metadata.Remove(PageParentsKey);
        else notebook.Metadata[PageParentsKey] = JsonSerializer.Serialize(parents.OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value), Json);
    }
    private static Guid ParentKey(Dictionary<Guid, Guid> parents, Guid pageId) => parents.GetValueOrDefault(pageId);
    private static HashSet<Guid> PageSubtree(Dictionary<Guid, Guid> parents, Guid pageId)
    {
        var children = parents.GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).ToArray());
        var result = new HashSet<Guid>(); var pending = new Stack<Guid>(); pending.Push(pageId);
        while (pending.TryPop(out var current))
        {
            if (!result.Add(current)) continue;
            if (children.TryGetValue(current, out var nested)) foreach (var child in nested) pending.Push(child);
        }
        return result;
    }
    private static void RequireEditableSubtree(NotesDocument notebook, IEnumerable<Guid> pageIds)
    { foreach (var id in pageIds) EnsureEditablePage(notebook, id); }

    public IReadOnlyList<BoardsPageHierarchyItem> ListPageHierarchy(NotesDocument notebook, bool includeDeleted = false)
    {
        var parents = ReadPageParents(notebook);
        var deleted = includeDeleted ? new HashSet<Guid>() : GetDeletedPageIds(notebook);
        var result = new List<BoardsPageHierarchyItem>();
        foreach (var section in notebook.Sections)
        {
            var visibleParents = new Dictionary<Guid, Guid>();
            var groups = new Dictionary<Guid, List<NotesPage>>();
            foreach (var page in section.Pages.OrderBy(page => page.Order).ThenBy(page => page.Id))
            {
                if (deleted.Contains(page.Id)) continue;
                var parent = ParentKey(parents, page.Id);
                while (parent != Guid.Empty && deleted.Contains(parent)) parent = ParentKey(parents, parent);
                visibleParents[page.Id] = parent;
                if (!groups.TryGetValue(parent, out var siblings)) groups[parent] = siblings = [];
                siblings.Add(page);
            }
            var stack = new Stack<(NotesPage Page, int Depth)>();
            if (groups.TryGetValue(Guid.Empty, out var roots))
                for (var i = roots.Count - 1; i >= 0; i--) stack.Push((roots[i], 0));
            while (stack.TryPop(out var next))
            {
                var originalParent = ParentKey(parents, next.Page.Id);
                var visibleParent = visibleParents[next.Page.Id];
                var hasChildren = groups.TryGetValue(next.Page.Id, out var children);
                result.Add(new(next.Page, section.Id, originalParent == Guid.Empty ? null : originalParent,
                    visibleParent == Guid.Empty ? null : visibleParent, next.Depth, hasChildren));
                if (children is not null)
                    for (var i = children.Count - 1; i >= 0; i--) stack.Push((children[i], next.Depth + 1));
            }
        }
        return result;
    }

    public NotesPage AddChildPage(NotesDocument notebook, Guid parentPageId, string? title = null)
    {
        var parents = ReadPageParents(notebook);
        _ = RequireRecoveryPage(notebook, parentPageId);
        var section = notebook.Sections.Single(item => item.Pages.Any(page => page.Id == parentPageId));
        var added = AddPage(notebook, section.Id, title);
        parents[added.Id] = parentPageId;
        WritePageParents(notebook, parents); ReorderSectionTree(section, parents);
        TouchRecovery(notebook); return added;
    }

    public void SetPageParent(NotesDocument notebook, Guid pageId, Guid? parentPageId)
    {
        var parents = ReadPageParents(notebook);
        _ = RequireRecoveryPage(notebook, pageId);
        var subtree = PageSubtree(parents, pageId);
        var source = notebook.Sections.Single(section => section.Pages.Any(page => page.Id == pageId));
        var target = source;
        if (parentPageId is { } parent)
        {
            if (subtree.Contains(parent)) throw new InvalidOperationException("A page cannot become its own ancestor.");
            _ = RequireRecoveryPage(notebook, parent);
            target = notebook.Sections.Single(section => section.Pages.Any(page => page.Id == parent));
        }
        var priorParent = ParentKey(parents, pageId);
        if (priorParent == (parentPageId ?? Guid.Empty)) return;
        RequireEditableSubtree(notebook, subtree);
        if (!ReferenceEquals(source, target) && source.Pages.All(page => subtree.Contains(page.Id)))
            throw new InvalidOperationException("Leave a page in the source section before moving its final branch.");
        if (priorParent != Guid.Empty) EnsureEditablePage(notebook, priorParent);
        if (parentPageId is { } newParent) parents[pageId] = newParent;
        else parents.Remove(pageId);
        if (!ReferenceEquals(source, target))
        {
            var moved = source.Pages.Where(page => subtree.Contains(page.Id)).ToArray();
            source.Pages.RemoveAll(page => subtree.Contains(page.Id)); target.Pages.AddRange(moved);
            NormalizePages(source);
        }
        // Reparenting appends to the intended parent's siblings, not whichever
        // flat array position the old document happened to use.
        var siblings = target.Pages.Where(page => page.Id != pageId && ParentKey(parents, page.Id) == (parentPageId ?? Guid.Empty))
            .OrderBy(page => page.Order).ThenBy(page => page.Id).Select(page => page.Id).Append(pageId).ToArray();
        ReorderSectionTree(target, parents, parentPageId ?? Guid.Empty, siblings);
        WritePageParents(notebook, parents); TouchRecovery(notebook);
    }

    public void MovePageWithinParent(NotesDocument notebook, Guid pageId, int direction)
    {
        var parents = ReadPageParents(notebook);
        _ = RequireRecoveryPage(notebook, pageId);
        if (direction == 0) return;
        var section = notebook.Sections.Single(item => item.Pages.Any(page => page.Id == pageId));
        var parent = ParentKey(parents, pageId);
        if (parent != Guid.Empty) EnsureEditablePage(notebook, parent);
        var all = section.Pages.Where(page => ParentKey(parents, page.Id) == parent).OrderBy(page => page.Order).ThenBy(page => page.Id).ToArray();
        var deleted = GetDeletedPageIds(notebook);
        var visible = all.Where(page => !deleted.Contains(page.Id)).ToArray();
        var index = Array.FindIndex(visible, page => page.Id == pageId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= visible.Length) return;
        RequireEditableSubtree(notebook, PageSubtree(parents, pageId));
        RequireEditableSubtree(notebook, PageSubtree(parents, visible[target].Id));
        var order = all.Select(page => page.Id).ToArray();
        var first = Array.IndexOf(order, pageId); var second = Array.IndexOf(order, visible[target].Id);
        (order[first], order[second]) = (order[second], order[first]);
        ReorderSectionTree(section, parents, parent, order); TouchRecovery(notebook);
    }

    // Existing cross-section MovePage callers retain the same stable API. Move
    // the complete subtree and detach only its root when changing section.
    private void MovePageTree(NotesDocument notebook, Guid pageId, Guid targetSectionId, int targetIndex)
    {
        var parents = ReadPageParents(notebook);
        _ = RequireRecoveryPage(notebook, pageId);
        var source = notebook.Sections.Single(section => section.Pages.Any(page => page.Id == pageId));
        var target = RequireRecoverySection(notebook, targetSectionId);
        var subtree = PageSubtree(parents, pageId); RequireEditableSubtree(notebook, subtree);
        if (!ReferenceEquals(source, target) && source.Pages.All(page => subtree.Contains(page.Id)))
            throw new InvalidOperationException("Leave a page in the source section before moving its final branch.");
        var parent = ParentKey(parents, pageId);
        if (parent != Guid.Empty) EnsureEditablePage(notebook, parent);
        var moved = source.Pages.Where(page => subtree.Contains(page.Id)).ToArray();
        source.Pages.RemoveAll(page => subtree.Contains(page.Id));
        target.Pages.InsertRange(Math.Clamp(targetIndex, 0, target.Pages.Count), moved);
        if (!ReferenceEquals(source, target)) parents.Remove(pageId);
        NormalizePages(source); if (!ReferenceEquals(source, target)) NormalizePages(target);
        WritePageParents(notebook, parents); TouchRecovery(notebook);
    }

    private static void ReorderSectionTree(NotesSection section, Dictionary<Guid, Guid> parents,
        Guid? overrideParent = null, IReadOnlyList<Guid>? overrideOrder = null)
    {
        var groups = section.Pages.GroupBy(page => ParentKey(parents, page.Id)).ToDictionary(group => group.Key,
            group => group.OrderBy(page => page.Order).ThenBy(page => page.Id).ToArray());
        if (overrideParent is { } parent && overrideOrder is not null)
        {
            var pages = section.Pages.ToDictionary(page => page.Id);
            groups[parent] = overrideOrder.Select(id => pages[id]).ToArray();
        }
        var result = new List<NotesPage>(); var pending = new Stack<NotesPage>();
        if (groups.TryGetValue(Guid.Empty, out var roots)) for (var i = roots.Length - 1; i >= 0; i--) pending.Push(roots[i]);
        while (pending.TryPop(out var page))
        {
            result.Add(page);
            if (groups.TryGetValue(page.Id, out var children)) for (var i = children.Length - 1; i >= 0; i--) pending.Push(children[i]);
        }
        if (result.Count != section.Pages.Count) throw new InvalidDataException("The page hierarchy cannot be ordered without losing pages.");
        section.Pages = result; NormalizePages(section);
    }
}
