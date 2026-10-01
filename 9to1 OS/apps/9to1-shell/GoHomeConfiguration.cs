namespace NineToOne.Os.Shell;

public enum GoHomeLayout { CompactSearch, StartMenu, Dashboard }
public enum GoHomeSectionKind { Pinned, Recent, Suggested, AllApps }
public enum GoHomeSectionSize { Compact, Standard, Large }
public sealed record GoHomeSection(GoHomeSectionKind Kind, bool Visible, GoHomeSectionSize Size, string? Group);
/// <summary>Presentation preferences only. No section configuration grants provider access or supplies entities.</summary>
public sealed record GoHomeConfiguration(GoHomeLayout Layout, IReadOnlyList<GoHomeSection> Sections)
{
    public static GoHomeConfiguration Default() => new(GoHomeLayout.StartMenu,
        Enum.GetValues<GoHomeSectionKind>().Select(k => new GoHomeSection(k, true, GoHomeSectionSize.Standard, null)).ToArray());
    public void Validate()
    {
        if (!Enum.IsDefined(Layout) || Sections is null || Sections.Count != 4 ||
            Sections.Any(s => s is null || !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Size)) ||
            Sections.Select(s => s.Kind).Distinct().Count() != 4)
            throw new InvalidDataException("Go home requires each supported section exactly once and a supported layout/size.");
        if (Sections.Any(s => s.Group is { } g && (string.IsNullOrWhiteSpace(g) || g.Length > 64 || g.Any(char.IsControl))))
            throw new InvalidDataException("Go home group names must be bounded printable text.");
    }
    public GoHomeConfiguration Detached() { Validate(); return this with { Sections = Sections.ToArray() }; }
}
public static class GoHomeEdits
{
    public static ShellConfiguration Configure(ShellConfiguration original, GoHomeConfiguration presentation)
    {
        original.Validate(); presentation.Validate();
        return original with { GoHome = presentation.Detached() };
    }
    public static ShellConfiguration Move(ShellConfiguration original, GoHomeSectionKind section, int destination)
    {
        var home = original.EffectiveGoHome.Detached();
        if (!Enum.IsDefined(section) || destination is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(destination));
        var ordered = home.Sections.ToList(); var item = ordered.Single(s => s.Kind == section);
        ordered.Remove(item); ordered.Insert(destination, item);
        return Configure(original, home with { Sections = ordered.ToArray() });
    }
    public static ShellConfiguration Present(ShellConfiguration original, GoHomeSectionKind section, bool visible, GoHomeSectionSize size, string? group)
    {
        if (!Enum.IsDefined(section)) throw new ArgumentOutOfRangeException(nameof(section));
        var home = original.EffectiveGoHome;
        return Configure(original, home with { Sections = home.Sections.Select(s => s.Kind == section ? s with { Visible = visible, Size = size, Group = group } : s).ToArray() });
    }
}
