using Haven.Application.Go;
namespace NineToOne.Os.Shell;

/// <summary>Presentation-only grouping of actual owner results. Neither this wrapper nor group text is an invocation grant.</summary>
public sealed record GoHomeResultGroup(string Id, string Title, string Availability, int FontSize, IReadOnlyList<GoResult> Results, int Column, int Row);
public static class GoHomeResultPresentation
{
    public static IReadOnlyList<GoHomeResultGroup> Create(GoHomeConfiguration home,
        IReadOnlyList<(GoHomeSectionKind Section, GoResult Result)> results, string activeView)
    {
        home.Validate();
        var sections = home.Sections.Where(s => s.Visible && (home.Layout == GoHomeLayout.Dashboard ||
            (s.Kind == GoHomeSectionKind.AllApps ? "All Apps" : s.Kind.ToString()) == activeView)).ToArray();
        var groups = new List<GoHomeResultGroup>();
        foreach (var group in sections.GroupBy(s => s.Group is { } name ? "named:" + name : "section:" + s.Kind, StringComparer.Ordinal))
        {
            var kinds = group.Select(s => s.Kind).ToHashSet();
            var labels = string.Join(", ", group.Select(s => s.Kind == GoHomeSectionKind.AllApps ? "All Apps" : s.Kind.ToString()));
            var title = group.First().Group is { } name ? name + " · " + labels : labels;
            var unavailable = group.Where(s => s.Kind is GoHomeSectionKind.Recent or GoHomeSectionKind.Suggested).Select(s => s.Kind + " unavailable");
            var rows = results.Where(r => kinds.Contains(r.Section)).Select(r => r.Result).ToArray();
            // A canonical app may appear in both actual sections; render one exact owner instance per group identity.
            rows = rows.DistinctBy(r => ShellGoDisplayKey.Create(r), StringComparer.Ordinal).ToArray();
            groups.Add(new(group.Key, title, string.Join("; ", unavailable), group.Max(s => s.Size switch {
                GoHomeSectionSize.Compact => 12, GoHomeSectionSize.Standard => 16, _ => 22 }), rows, home.Layout == GoHomeLayout.Dashboard ? groups.Count % 2 : 0, home.Layout == GoHomeLayout.Dashboard ? groups.Count / 2 : groups.Count));
        }
        return groups;
    }
}
