namespace Haven.UI.Components;

/// <summary>
/// Generic status bar: a persistent footer with a status message and a trailing
/// slot for sync/state indicators. Canvas save state and Boards persistence
/// status compose this; stores stay app-owned.
/// </summary>
public sealed class StatusBar : Container
{
    private string _status = string.Empty;

    public StatusBar()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Status";
        Layout = HavenLayout.Horizontal;
        SetValue(HavenProperties.Width, HavenLength.Percent(100), HavenValueSource.Default);
        SetValue(HavenProperties.MinHeight, HavenLength.Px(32), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Surface", HavenValueSource.Default);
        SetValue(HavenProperties.Gap, HavenLength.Px(8), HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 16px"), HavenValueSource.Default);

        StatusText = new Text { Level = TextLevel.Caption, Content = string.Empty };
        StatusText.Accessibility.AccessibleName = "Status message";
        StatusText.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        Add(StatusText);

        Trailing = new Container { Name = "StatusBar.Trailing", Layout = HavenLayout.Horizontal };
        Trailing.Accessibility.AccessibleName = "Status indicators";
        Trailing.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        Add(Trailing);
    }

    public Text StatusText { get; }
    public Container Trailing { get; }

    public string Status
    {
        get => _status;
        set => SetStatus(value);
    }

    public void SetStatus(string? status)
    {
        _status = status ?? string.Empty;
        StatusText.Content = _status;
        StatusText.SetValue(HavenProperties.Visibility,
            string.IsNullOrWhiteSpace(_status) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void AddIndicator(HavenElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Trailing.Add(element);
    }

    public override HavenComponentMetadata Metadata => new(
        "StatusBar",
        "HUI/shared/Components/StatusBar/StatusBar.cs",
        ["StatusBar"],
        [],
        "Persistent footer for status plus indicators; state stays app-owned.");
}
