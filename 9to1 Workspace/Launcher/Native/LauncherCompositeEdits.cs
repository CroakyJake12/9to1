namespace NineToOne.Launcher;

public static partial class LauncherLayoutEdits
{
    public static LauncherLayout AddPageWithApplications(LauncherLayout layout, string name, IReadOnlyList<Guid> applicationIds)
    {
        var ids = CompositeIds(applicationIds);
        var candidate = AddPage(layout, name);
        foreach (var id in ids) candidate = AddApplication(candidate, candidate.ActivePageId, id);
        return candidate;
    }
    public static LauncherLayout GroupPlacements(LauncherLayout layout, Guid pageId, string name, IReadOnlyList<Guid> placementIds)
    {
        var ids = CompositeIds(placementIds);
        var page = RequirePage(layout, pageId);
        var members = ids.Select(id => page.Items.SingleOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException("Choose shortcuts from the destination page.")).ToArray();
        if (members.Any(item => item.FolderId is not null)) throw new InvalidOperationException("Folders cannot contain other folders.");
        // Free the selected cells before creating the folder so grouping works on a full page.
        // Only the detached candidate changes; the owner later commits the whole reviewed plan once.
        var chosen = ids.ToHashSet();
        var candidate = Checked(layout with { Pages = layout.Pages.Select(p => p.Id == pageId
            ? p with { Items = p.Items.Where(item => !chosen.Contains(item.Id)).ToArray() } : p).ToArray() });
        candidate = CreateFolder(candidate, pageId, name);
        var folder = candidate.Folders.Single(f => !layout.Folders.Any(old => old.Id == f.Id));
        return Checked(candidate with { Folders = candidate.Folders.Select(f => f.Id == folder.Id ? f with
            { Items = members.Select((item, index) => item with { Column = index % f.Columns, Row = index / f.Columns }).ToArray() } : f).ToArray() });
    }
    private static Guid[] CompositeIds(IReadOnlyList<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count is < 1 or > 64) throw new ArgumentException("Choose one to 64 existing identities.");
        var copy = ids.ToArray();
        if (copy.Any(id => id == Guid.Empty) || copy.Distinct().Count() != copy.Length) throw new ArgumentException("Choose distinct existing identities.");
        return copy;
    }
}
