namespace Haven.UI.Components;

public enum ToolbarOrientation
{
    Horizontal,
    Vertical
}

/// <summary>
/// Generic toolbar: an ordered row (or column) of buttons, separators, and
/// custom elements with keyboard-reachable actions. Reuses Button/Separator so
/// Rnote and AppFlowy toolstrips do not fork their own.
/// </summary>
public sealed class Toolbar : Container
{
    private ToolbarOrientation _orientation = ToolbarOrientation.Horizontal;

    public Toolbar()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Toolbar";
        ApplyOrientation();
        SetValue(HavenProperties.Width, HavenLength.Percent(100), HavenValueSource.Default);
        SetValue(HavenProperties.MinHeight, HavenLength.Px(48), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Surface", HavenValueSource.Default);
        SetValue(HavenProperties.Gap, HavenLength.Px(6), HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 12px"), HavenValueSource.Default);
        SetValue(HavenProperties.Overflow, HavenOverflow.Scroll, HavenValueSource.Default);
        SetValue(HavenProperties.Clip, true, HavenValueSource.Default);
    }

    public ToolbarOrientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value) return;
            _orientation = value;
            ApplyOrientation();
        }
    }

    public void AddButton(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        Add(button);
    }

    public Separator AddSeparator()
    {
        var separator = new Separator
        {
            Orientation = _orientation == ToolbarOrientation.Horizontal
                ? SeparatorOrientation.Vertical
                : SeparatorOrientation.Horizontal
        };
        if (_orientation == ToolbarOrientation.Horizontal)
        {
            separator.SetValue(HavenProperties.Width, HavenLength.Px(1));
            separator.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        }
        else
        {
            separator.SetValue(HavenProperties.Height, HavenLength.Px(1));
            separator.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        }
        Add(separator);
        return separator;
    }

    public void AddElement(HavenElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Add(element);
    }

    private void ApplyOrientation()
    {
        Layout = _orientation == ToolbarOrientation.Horizontal ? HavenLayout.Horizontal : HavenLayout.Vertical;
    }

    public override HavenComponentMetadata Metadata => new(
        "Toolbar",
        "HUI/shared/Components/Toolbar/Toolbar.cs",
        ["Toolbar"],
        [],
        "Generic action strip; tool/product semantics stay in the consuming app.");
}
