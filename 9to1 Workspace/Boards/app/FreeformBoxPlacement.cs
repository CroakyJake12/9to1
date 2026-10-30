namespace CakeOS.Apps.Boards.App;

// Admission agrees with the existing canvas-object renderer. Never let a relative
// replacement silently clamp the original frame, or invent flow coordinates.
internal static class FreeformBoxPlacement
{
    internal static bool CanRepresent(double x, double y, double width, double height) =>
        double.IsFinite(x) && double.IsFinite(y) && x >= 0 && y >= 0 &&
        double.IsFinite(width) && double.IsFinite(height) &&
        width is >= 24 and <= 5000 && height is >= 24 and <= 5000;
}
