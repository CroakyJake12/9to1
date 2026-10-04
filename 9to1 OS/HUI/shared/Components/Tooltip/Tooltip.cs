namespace Haven.UI.Components;

public enum TooltipPlacement
{
    Above,
    Below
}

/// <summary>
/// Generic non-interactive hover hint. Attaching sets the anchor's accessible
/// description (so screen-reader parity never depends on hover) and shows the
/// bubble while the anchor is hovered, using only existing router hover state.
/// </summary>
public sealed class Tooltip : Container
{
    private const double EstimatedHeight = 30d;
    private const double Gap = 6d;

    public Tooltip(string text = "")
    {
        Accessibility.Role = HavenAccessibleRole.Text;
        Accessibility.Focusable = false;
        SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None, HavenValueSource.Default);
        SetValue(HavenProperties.Background, "SurfaceRaised", HavenValueSource.Default);
        SetValue(HavenProperties.BorderColor, "Border", HavenValueSource.Default);
        SetValue(HavenProperties.BorderWidth, HavenLength.Px(1), HavenValueSource.Default);
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(8)), HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Parse("6px 10px"), HavenValueSource.Default);
        SetValue(HavenProperties.ZIndex, 600);
        SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        Label = new Text { Level = TextLevel.Caption, Content = text ?? string.Empty };
        Add(Label);
        AccessibleText = text ?? string.Empty;
    }

    public Text Label { get; }

    public string AccessibleText
    {
        get => Label.Content;
        set
        {
            Label.Content = value ?? string.Empty;
            Accessibility.AccessibleName = Label.Content;
        }
    }

    /// <summary>
    /// Binds a tooltip to an anchor: the anchor's accessible description carries
    /// the text, and the bubble follows anchor hover. The bubble is added to
    /// <paramref name="sceneRoot"/> as an overlay.
    /// </summary>
    public static Tooltip Attach(HavenElement anchor, HavenElement sceneRoot, string? text, TooltipPlacement placement = TooltipPlacement.Above)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(sceneRoot);
        if (string.IsNullOrWhiteSpace(anchor.Accessibility.Description))
            anchor.Accessibility.Description = text ?? string.Empty;

        var tooltip = new Tooltip(text ?? string.Empty) { Placement = placement };
        sceneRoot.Add(tooltip);

        EventHandler onInvalidated = (_, _) => tooltip.Sync(anchor, sceneRoot);
        anchor.Invalidated += onInvalidated;
        tooltip.DetachAction = () =>
        {
            anchor.Invalidated -= onInvalidated;
            if (tooltip.Parent is not null) tooltip.Parent.Remove(tooltip);
        };
        tooltip.Sync(anchor, sceneRoot);
        return tooltip;
    }

    public TooltipPlacement Placement { get; set; } = TooltipPlacement.Above;

    private Action? DetachAction { get; set; }

    public void Detach() => DetachAction?.Invoke();

    private void Sync(HavenElement anchor, HavenElement sceneRoot)
    {
        var visible = anchor.State.HasFlag(HavenElementState.Hover);
        SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        if (!visible) return;
        var anchorLeft = anchor.Bounds.X - sceneRoot.Bounds.X;
        var anchorTop = anchor.Bounds.Y - sceneRoot.Bounds.Y;
        SetValue(HavenProperties.Left, HavenLength.Px(Math.Max(0d, anchorLeft)));
        var top = Placement == TooltipPlacement.Above
            ? anchorTop - EstimatedHeight - Gap
            : anchor.Bounds.Bottom - sceneRoot.Bounds.Y + Gap;
        SetValue(HavenProperties.Top, HavenLength.Px(Math.Max(0d, top)));
    }

    public override HavenComponentMetadata Metadata => new(
        "Tooltip",
        "HUI/shared/Components/Tooltip/Tooltip.cs",
        ["Tooltip"],
        [],
        "Hover hint with accessible-description parity; never interactive.");
}
