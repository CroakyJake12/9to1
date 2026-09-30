namespace NineToOne.Launcher;

public enum LauncherDrawerSort { Alphabetical, ReverseAlphabetical, CategoryOrder }
public sealed record LauncherDrawerCategory(Guid Id, string Name, IReadOnlyList<Guid> Applications);
public sealed record LauncherDrawer(LauncherDrawerSort Sort, IReadOnlyList<LauncherDrawerCategory> Categories)
{
    public static LauncherDrawer Empty => new(LauncherDrawerSort.Alphabetical, []);
    internal void Validate(HashSet<Guid> identities)
    {
        if (!Enum.IsDefined(Sort) || Categories is null || Categories.Count > 64)
            throw new InvalidDataException("Launcher drawer configuration is unsupported.");
        var count = 0;
        foreach (var category in Categories)
        {
            if (category is null || category.Id == Guid.Empty || !identities.Add(category.Id) ||
                !LauncherLayout.ValidName(category.Name) || category.Applications is null || category.Applications.Count > 10000 ||
                category.Applications.Any(id => id == Guid.Empty) || category.Applications.Distinct().Count() != category.Applications.Count)
                throw new InvalidDataException("Launcher category identity or membership is invalid.");
            count += category.Applications.Count;
        }
        if (count > 10000) throw new InvalidDataException("Launcher category capacity is exceeded.");
    }
    internal LauncherDrawer Copy() => this with { Categories = Categories.Select(c => c with { Applications = c.Applications.ToArray() }).ToArray() };
}

public static partial class LauncherLayoutEdits
{
    public static LauncherLayout AddDrawerCategory(LauncherLayout layout, string name)
    {
        var drawer = layout.Drawer ?? LauncherDrawer.Empty;
        return Checked(layout with { Drawer = drawer with { Categories = [.. drawer.Categories, new(Guid.NewGuid(), name, [])] } });
    }
    public static LauncherLayout SetDrawerSort(LauncherLayout layout, LauncherDrawerSort sort)
        => Checked(layout with { Drawer = (layout.Drawer ?? LauncherDrawer.Empty) with { Sort = sort } });
    public static LauncherLayout RenameDrawerCategory(LauncherLayout layout, Guid id, string name)
        => EditDrawerCategory(layout, id, category => category with { Name = name });
    public static LauncherLayout SetDrawerCategoryMembership(LauncherLayout layout, Guid id, Guid applicationId, bool included)
        => EditDrawerCategory(layout, id, category => category with { Applications = included
            ? category.Applications.Append(applicationId).Distinct().ToArray() : category.Applications.Where(app => app != applicationId).ToArray() });
    public static LauncherLayout ReorderDrawerApplication(LauncherLayout layout, Guid id, Guid applicationId, int delta)
        => EditDrawerCategory(layout, id, category =>
        {
            var apps = category.Applications.ToList(); var index = apps.IndexOf(applicationId);
            if (index < 0) throw new InvalidOperationException("This application is no longer in the category.");
            var next = Math.Clamp(index + Math.Sign(delta), 0, apps.Count - 1);
            (apps[index], apps[next]) = (apps[next], apps[index]); return category with { Applications = apps.ToArray() };
        });
    public static LauncherLayout RemoveDrawerCategory(LauncherLayout layout, Guid id)
    {
        var drawer = layout.Drawer ?? LauncherDrawer.Empty;
        if (!drawer.Categories.Any(c => c.Id == id)) throw new InvalidOperationException("This category is no longer available.");
        // Categories are views, so removing one never removes app placements or hidden rules.
        return Checked(layout with { Drawer = drawer with { Categories = drawer.Categories.Where(c => c.Id != id).ToArray() } });
    }
    private static LauncherLayout EditDrawerCategory(LauncherLayout layout, Guid id, Func<LauncherDrawerCategory, LauncherDrawerCategory> edit)
    {
        var drawer = layout.Drawer ?? LauncherDrawer.Empty;
        if (!drawer.Categories.Any(c => c.Id == id)) throw new InvalidOperationException("This category is no longer available.");
        return Checked(layout with { Drawer = drawer with { Categories = drawer.Categories.Select(c => c.Id == id ? edit(c) : c).ToArray() } });
    }
}
