namespace Haven.CUI.DevTools;

public enum DevToolsDockPosition
{
    Undocked,
    Left,
    Right,
    Bottom
}

public enum DevToolsPanel
{
    Elements,
    Layout,
    Styles,
    Resources,
    Bindings,
    State,
    Events,
    Focus,
    Accessibility,
    Input,
    Invalidation,
    Performance,
    Platform,
    Console
}

public sealed record DevToolsDockState
{
    public DevToolsDockState(
        DevToolsDockPosition position = DevToolsDockPosition.Right,
        double sizeRatio = 0.35,
        DevToolsPanel activePanel = DevToolsPanel.Elements,
        bool isOpen = false)
    {
        if (!double.IsFinite(sizeRatio) || sizeRatio is < 0.15 or > 0.85)
            throw new ArgumentOutOfRangeException(nameof(sizeRatio), "Dock size ratio must be between 0.15 and 0.85.");

        Position = position;
        SizeRatio = sizeRatio;
        ActivePanel = activePanel;
        IsOpen = isOpen;
    }

    public DevToolsDockPosition Position { get; init; }

    public double SizeRatio { get; init; }

    public DevToolsPanel ActivePanel { get; init; }

    public bool IsOpen { get; init; }

    public DevToolsDockState Resize(double sizeRatio) => new(Position, sizeRatio, ActivePanel, IsOpen);

    public DevToolsDockState Dock(DevToolsDockPosition position) => new(position, SizeRatio, ActivePanel, IsOpen);

    public DevToolsDockState Show(DevToolsPanel panel) => new(Position, SizeRatio, panel, true);

    public DevToolsDockState Close() => new(Position, SizeRatio, ActivePanel, false);
}
