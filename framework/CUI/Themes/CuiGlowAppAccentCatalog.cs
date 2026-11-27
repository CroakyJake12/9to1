namespace CakeOS.Cui.Themes;

/// <summary>Atlas v5 native Glow anchors. Professional keeps its approved catalogue;
/// explicit Home accent preferences always take precedence over these defaults.</summary>
public static class CuiGlowAppAccentCatalog
{
    public sealed record AppAccent(string AppId, string Surface, string Colour, params string[] Aliases);
    public static IReadOnlyList<AppAccent> Defaults { get; } = Array.AsReadOnly<AppAccent>(
    [
        new("app.home", "Home", "#405BD2", "Haven", "Dashboard", "Launcher"),
        new("app.browse", "Browse", "#2566B6"),
        new("app.spaces", "Spaces", "#7650C7", "Chat"),
        new("app.write", "Write", "#005ABA"),
        new("app.present", "Present", "#A81726"),
        new("app.boards", "Boards", "#7650C7"),
        new("app.canvas", "Canvas", "#157777"),
        new("app.data", "Data", "#24784F"),
        new("app.files", "Files", "#8C6520"),
        new("app.forms", "Forms", "#24784F"),
        new("app.plan", "Planner", "#2678B0", "Plan"),
        new("app.automations", "Automations", "#986414"),
        new("app.picture", "Picture", "#B53661", "Imagine"),
        new("app.wave", "Wave", "#157487"),
        new("app.motion", "Motion", "#8543AE"),
        new("app.dev", "Build", "#B55224", "Dev"),
        new("app.studio", "AI Studio", "#456FB8", "Studio"),
        new("app.terminal", "Terminal", "#7650C7"),
        new("app.connect", "Connect", "#2678A6", "Mail"),
        new("app.maps", "Maps", "#368164"),
        new("app.shelf", "Shelf", "#8853AC"),
        new("app.play", "Play", "#7650C7"),
        new("app.games", "Games", "#B14B43"),
        new("app.vision", "Vision and Voice", "#157B80", "Vision", "Voice")
    ]);

    public static AppAccent? Find(string appIdOrSurface) => Defaults.FirstOrDefault(app =>
        app.AppId.Equals(appIdOrSurface, StringComparison.OrdinalIgnoreCase) ||
        app.Surface.Equals(appIdOrSurface, StringComparison.OrdinalIgnoreCase) ||
        app.Aliases.Any(alias => alias.Equals(appIdOrSurface, StringComparison.OrdinalIgnoreCase)));
}
