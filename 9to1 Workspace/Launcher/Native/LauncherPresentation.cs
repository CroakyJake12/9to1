namespace NineToOne.Launcher;

/// <summary>Portable launcher appearance; OS widget bindings and package identity remain platform-owned.</summary>
public sealed record LauncherPresentation(int IconSizeDp = 52, int LabelSizeSp = 12,
    int HorizontalSpacingDp = 4, int VerticalSpacingDp = 4, bool ShowLabels = true, bool ShowPackages = false)
{
    public void Validate()
    {
        if (IconSizeDp is < 24 or > 96 || LabelSizeSp is < 10 or > 24 ||
            HorizontalSpacingDp is < 0 or > 24 || VerticalSpacingDp is < 0 or > 24)
            throw new InvalidDataException("Launcher presentation is outside supported accessible dimensions.");
    }
    public static LauncherPresentation Comfortable => new(52, 14, 8, 8);
    public static LauncherPresentation Compact => new(36, 12, 2, 2);
    public static LauncherPresentation Large => new(72, 18, 12, 12);
}

public static partial class LauncherLayoutEdits
{
    public static LauncherLayout SetPresentation(LauncherLayout layout, LauncherPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation); presentation.Validate();
        return Checked(layout with { Presentation = presentation });
    }
}
