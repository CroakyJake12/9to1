namespace CakeOS.Cui.Themes;

/// <summary>Visible app accents keyed by existing, stable application identities.</summary>
public static class CuiAppAccentCatalog
{
    public sealed record AppAccent(string AppId, string Surface, string Colour);

    public static IReadOnlyList<AppAccent> Defaults { get; } = Array.AsReadOnly<AppAccent>(
    [
        new("app.write", "Write", "#2563EB"),
        new("app.present", "Present", "#DB3545"),
        new("app.data", "Data", "#168B4F"),
        new("app.boards", "Boards", "#7026D9"),
        new("app.picture", "Picture", "#ED4D83"),
        new("app.wave", "Wave", "#10AEC7"),
        new("app.motion", "Motion", "#C65BCF")
    ]);

    public static AppAccent? Find(string appIdOrSurface) => Defaults.FirstOrDefault(app =>
        app.AppId.Equals(appIdOrSurface, StringComparison.OrdinalIgnoreCase)
        || app.Surface.Equals(appIdOrSurface, StringComparison.OrdinalIgnoreCase));

    public static string SurfaceForRoute(string routeId) => Find(routeId)?.Surface ?? "Home";
}
