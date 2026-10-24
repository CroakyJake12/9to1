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
    public const string PageStructureHistoryKey = "boards.page-structure-history.v1";
    private const int MaxPageStructureHistory = 64;
    private sealed record PagePosition(Guid SectionId, int Order);
    private sealed record PageStructureState(Guid[] SectionIds, Dictionary<Guid, Guid> Parents,
        Dictionary<Guid, PagePosition> Positions, Guid[] Deleted);
    private sealed record PageStructureHistory(List<PageStructureState> Undo, List<PageStructureState> Redo);
    private sealed record PageStructureRestorePlan(Guid[] MutatedSectionIds, Dictionary<Guid, List<NotesPage>> Pages,
        Dictionary<Guid, Guid> Parents, Guid[] Deleted, string? ParentJson, string? DeletedJson,
        PageStructureState ResultState);

    private static PageStructureState CapturePageStructure(NotesDocument notebook) => new(
        notebook.Sections.Select(section => section.Id).ToArray(), ReadPageParents(notebook),
        notebook.Sections.SelectMany(section => section.Pages.Select(page =>
                (Page: page, SectionId: section.Id)))
            .ToDictionary(item => item.Page.Id, item => new PagePosition(item.SectionId, item.Page.Order)),
        GetDeletedPageIds(notebook).Order().ToArray());

    private static PageStructureHistory ReadPageStructureHistory(NotesDocument notebook)
    {
        if (!notebook.Metadata.TryGetValue(PageStructureHistoryKey, out var json)) return new([], []);
        try
        {
            var history = JsonSerializer.Deserialize<PageStructureHistory>(json, Json);
            if (history is null || history.Undo is null || history.Redo is null
                || history.Undo.Count > MaxPageStructureHistory || history.Redo.Count > MaxPageStructureHistory
                || history.Undo.Concat(history.Redo).Any(state => state is null || state.SectionIds is null || state.Parents is null
                    || state.Positions is null || state.Deleted is null
                    || state.Positions.Values.Any(position => position is null)))
                throw new InvalidDataException("Boards page structure history has an invalid shape or exceeds its retained history limit.");
            return history;
        }
        catch (JsonException error) { throw new InvalidDataException("Boards page structure history is invalid; the original document is preserved.", error); }
    }

    private static void WritePageStructureHistory(NotesDocument notebook, PageStructureHistory history) =>
        notebook.Metadata[PageStructureHistoryKey] = JsonSerializer.Serialize(history, Json);

    private static void BeginPageStructureEdit(NotesDocument notebook)
    {
        var history = ReadPageStructureHistory(notebook);
        history.Undo.Add(CapturePageStructure(notebook));
        if (history.Undo.Count > MaxPageStructureHistory) history.Undo.RemoveAt(0);
        history.Redo.Clear();
        WritePageStructureHistory(notebook, history);
    }

    private static PageStructureRestorePlan PreparePageStructureRestore(NotesDocument notebook, PageStructureState state)
    {
        ValidatePageStructureState(notebook, state);
        var capturedSections = state.SectionIds.ToHashSet();
        var currentPositions = notebook.Sections.SelectMany(section => section.Pages.Select(page => (Page: page, SectionId: section.Id)))
            .ToDictionary(item => item.Page.Id);
        var current = CapturePageStructure(notebook);
        if (current.Deleted.Any(id => !currentPositions.ContainsKey(id)))
            throw new InvalidDataException("Boards page Trash metadata references a missing page; the original document is preserved.");
        var pages = currentPositions.ToDictionary(pair => pair.Key, pair => pair.Value.Page);
        var planned = notebook.Sections.ToDictionary(section => section.Id, section =>
            capturedSections.Contains(section.Id) ? new List<NotesPage>() : section.Pages.ToList());
        foreach (var pair in state.Positions.OrderBy(pair => pair.Value.SectionId).ThenBy(pair => pair.Value.Order))
            planned[pair.Value.SectionId].Add(pages[pair.Key]);
        var actual = pages.Keys.ToHashSet();
        var unrecordedPagesInExistingSections = actual.Except(state.Positions.Keys)
            .Where(id => capturedSections.Contains(currentPositions[id].SectionId)).ToHashSet();
        foreach (var id in unrecordedPagesInExistingSections)
            planned[currentPositions[id].SectionId].Add(pages[id]);
        foreach (var section in notebook.Sections.Where(section => capturedSections.Contains(section.Id)))
        {
            planned[section.Id] = planned[section.Id]
                .OrderBy(page => state.Positions.GetValueOrDefault(page.Id)?.Order ?? int.MaxValue)
                .ThenBy(page => page.Id).ToList();
        }

        // History snapshots are scoped to sections that existed when the operation
        // ran, but Notes metadata is notebook-global. Preserve current metadata for
        // later sections and for pages created after the snapshot (which are trashed
        // by undo but retain their parent edge for recovery).
        var parents = current.Parents.Where(pair => !state.Positions.ContainsKey(pair.Key)
                || !capturedSections.Contains(currentPositions[pair.Key].SectionId))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var pair in state.Parents) parents[pair.Key] = pair.Value;
        var deleted = current.Deleted.Where(id => !capturedSections.Contains(currentPositions[id].SectionId))
            .Concat(state.Deleted).Concat(unrecordedPagesInExistingSections).ToHashSet();

        var resultPositions = new Dictionary<Guid, PagePosition>();
        foreach (var section in notebook.Sections)
        {
            var resultingPages = capturedSections.Contains(section.Id) ? planned[section.Id] : section.Pages;
            for (var index = 0; index < resultingPages.Count; index++)
                resultPositions[resultingPages[index].Id] = new PagePosition(section.Id, index);
        }
        var resultState = new PageStructureState(notebook.Sections.Select(section => section.Id).ToArray(),
            parents, resultPositions, deleted.Order().ToArray());
        ValidatePageStructureState(notebook, resultState);
        foreach (var sectionId in state.SectionIds)
            if (!planned[sectionId].Any(page => !deleted.Contains(page.Id)))
                throw new InvalidDataException("Boards page structure history would leave a captured section without a visible page.");

        var parentsUnchanged = parents.Count == current.Parents.Count
            && parents.All(pair => current.Parents.TryGetValue(pair.Key, out var value) && value == pair.Value);
        var parentJson = parentsUnchanged ? notebook.Metadata.GetValueOrDefault(PageParentsKey)
            : parents.Count == 0 ? null : JsonSerializer.Serialize(parents.OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value), Json);
        var deletedUnchanged = deleted.SetEquals(current.Deleted);
        var deletedJson = deletedUnchanged ? notebook.Metadata.GetValueOrDefault(DeletedPagesKey)
            : deleted.Count == 0 ? null : JsonSerializer.Serialize(deleted.Order().ToArray());
        var changedSections = state.SectionIds.Where(sectionId =>
        {
            var before = notebook.Sections.Single(section => section.Id == sectionId).Pages.Select(page => page.Id);
            var after = planned[sectionId].Select(page => page.Id);
            return !before.SequenceEqual(after);
        }).ToArray();
        return new PageStructureRestorePlan(changedSections, planned, parents, deleted.Order().ToArray(),
            parentJson, deletedJson, resultState);
    }

    private static void PublishPageStructureRestore(NotesDocument notebook, PageStructureRestorePlan plan)
    {
        // Publish only after every reference, cycle, ordering and replacement list
        // has been validated and staged against the canonical Notes pages.
        foreach (var section in notebook.Sections.Where(section => plan.MutatedSectionIds.Contains(section.Id)))
        {
            section.Pages = plan.Pages[section.Id];
            NormalizePages(section);
        }
        if (plan.ParentJson is null) notebook.Metadata.Remove(PageParentsKey);
        else notebook.Metadata[PageParentsKey] = plan.ParentJson;
        if (plan.DeletedJson is null) notebook.Metadata.Remove(DeletedPagesKey);
        else notebook.Metadata[DeletedPagesKey] = plan.DeletedJson;
        TouchRecovery(notebook);
    }

    private static void ValidatePageStructureState(NotesDocument notebook, PageStructureState state)
    {
        var sections = notebook.Sections.ToDictionary(section => section.Id);
        var actual = notebook.Sections.SelectMany(section => section.Pages).Select(page => page.Id).ToHashSet();
        if (state.SectionIds.Length == 0 || state.SectionIds.Any(id => id == Guid.Empty || !sections.ContainsKey(id))
            || state.SectionIds.Distinct().Count() != state.SectionIds.Length
            || state.Positions.Any(pair => pair.Key == Guid.Empty || !actual.Contains(pair.Key)
                || pair.Value is null || pair.Value.SectionId == Guid.Empty || !sections.ContainsKey(pair.Value.SectionId)
                || !state.SectionIds.Contains(pair.Value.SectionId) || pair.Value.Order < 0)
            || state.Positions.Values.GroupBy(position => position.SectionId)
                .Any(group => group.GroupBy(position => position.Order).Any(orders => orders.Count() > 1))
            || state.Deleted.Any(id => id == Guid.Empty || !state.Positions.ContainsKey(id)))
            throw new InvalidDataException("Boards page structure history contains invalid page, section, order or trash references.");

        foreach (var pair in state.Parents)
            if (pair.Key == Guid.Empty || pair.Value == Guid.Empty || pair.Key == pair.Value
                || !state.Positions.TryGetValue(pair.Key, out var child)
                || !state.Positions.TryGetValue(pair.Value, out var parent)
                || child.SectionId != parent.SectionId)
                throw new InvalidDataException("Boards page structure history contains an invalid parent reference.");

        var complete = new HashSet<Guid>();
        foreach (var start in state.Parents.Keys)
        {
            var path = new HashSet<Guid>();
            var cursor = start;
            while (!complete.Contains(cursor) && state.Parents.TryGetValue(cursor, out var parent))
            {
                if (!path.Add(cursor)) throw new InvalidDataException("Boards page structure history contains a parent cycle.");
                cursor = parent;
            }
            complete.UnionWith(path);
        }
    }

    public bool UndoPageStructure(NotesDocument notebook) => ChangePageStructureHistory(notebook, undo: true);
    public bool RedoPageStructure(NotesDocument notebook) => ChangePageStructureHistory(notebook, undo: false);

    private static HashSet<Guid> ChangedPageStructureScope(PageStructureState current, PageStructureState target)
    {
        var affected = new HashSet<Guid>();
        foreach (var id in current.Positions.Keys.Union(target.Positions.Keys))
        {
            if (!target.Positions.ContainsKey(id) && current.Positions.TryGetValue(id, out var currentOnly)
                && !target.SectionIds.Contains(currentOnly.SectionId)) continue;
            if (!current.Positions.TryGetValue(id, out var currentPosition)
                || !target.Positions.TryGetValue(id, out var targetPosition)
                || currentPosition != targetPosition)
                affected.Add(id);
        }

        foreach (var id in current.Parents.Keys.Union(target.Parents.Keys))
        {
            var hasCurrent = current.Parents.TryGetValue(id, out var currentParent);
            var hasTarget = target.Parents.TryGetValue(id, out var targetParent);
            if (hasCurrent == hasTarget && currentParent == targetParent) continue;
            affected.Add(id);
            if (hasCurrent && currentParent != Guid.Empty) affected.Add(currentParent);
            if (hasTarget && targetParent != Guid.Empty) affected.Add(targetParent);
        }

        var currentDeleted = current.Deleted.ToHashSet();
        var targetDeleted = target.Deleted.ToHashSet();
        foreach (var pageId in currentDeleted.Except(targetDeleted).Union(targetDeleted.Except(currentDeleted)))
        {
            // Hiding/restoring an ancestor changes every descendant's visible
            // navigation parent/depth even though their canonical parent links
            // remain stable. Include both historical and resulting subtrees.
            affected.UnionWith(PageSubtree(current.Parents, pageId));
            affected.UnionWith(PageSubtree(target.Parents, pageId));
            var currentParent = current.Parents.GetValueOrDefault(pageId);
            var targetParent = target.Parents.GetValueOrDefault(pageId);
            if (currentParent != Guid.Empty) affected.Add(currentParent);
            if (targetParent != Guid.Empty) affected.Add(targetParent);
        }
        return affected;
    }

    private static bool ChangePageStructureHistory(NotesDocument notebook, bool undo)
    {
        EnsureBoards(notebook);
        var history = ReadPageStructureHistory(notebook);
        var source = undo ? history.Undo : history.Redo;
        if (source.Count == 0) return false;
        var target = undo ? history.Redo : history.Undo;
        var state = source[^1];
        var current = CapturePageStructure(notebook);
        var plan = PreparePageStructureRestore(notebook, state);
        RequireEditableSubtree(notebook, ChangedPageStructureScope(current, plan.ResultState));
        target.Add(current);
        source.RemoveAt(source.Count - 1);
        var historyJson = JsonSerializer.Serialize(history, Json);
        PublishPageStructureRestore(notebook, plan);
        notebook.Metadata[PageStructureHistoryKey] = historyJson;
        return true;
    }

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
        BeginPageStructureEdit(notebook);
        var added = AddPageWithoutHistory(notebook, section.Id, title);
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
        BeginPageStructureEdit(notebook);
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
        BeginPageStructureEdit(notebook);
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
        var sourceRemaining = source.Pages.Where(page => !subtree.Contains(page.Id)).ToList();
        var destination = ReferenceEquals(source, target) ? sourceRemaining : target.Pages.ToList();
        destination.InsertRange(Math.Clamp(targetIndex, 0, destination.Count), moved);
        if (ReferenceEquals(source, target)
            && destination.Select(page => page.Id).SequenceEqual(source.Pages.Select(page => page.Id))) return;

        BeginPageStructureEdit(notebook);
        source.Pages = sourceRemaining;
        target.Pages = destination;
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
