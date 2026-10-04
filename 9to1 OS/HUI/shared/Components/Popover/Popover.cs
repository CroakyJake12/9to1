namespace Haven.UI.Components;

/// <summary>
/// Generic anchored popover: a content-agnostic overlay card positioned beside
/// an anchor with light-dismiss. PopupMenu stays the menu specialization;
/// colour pickers, calendars, and rich previews compose Popover instead of
/// copying its overlay math.
/// </summary>
public sealed class Popover : Container
{
    private const double Edge = 8d;
    private const double AnchorGap = 4d;

    public Popover(HavenElement anchor, HavenElement sceneRoot, string accessibleName = "Popover")
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(sceneRoot);

        Layout = HavenLayout.Canvas;
        Name = "PopoverOverlay";
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Percent(100));
        SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        SetValue(HavenProperties.ZIndex, 500);
        SetValue(HavenProperties.PointerEvents, HavenPointerEvents.ChildrenOnly);

        DismissLayer = new Button { Variant = ButtonVariant.Text, Content = string.Empty, Name = "PopoverDismiss" };
        DismissLayer.Accessibility.AccessibleName = "Dismiss dialog";
        DismissLayer.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        DismissLayer.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        DismissLayer.SetValue(HavenProperties.MinHeight, HavenLength.Px(0));
        DismissLayer.SetValue(HavenProperties.Padding, HavenThickness.Zero);
        DismissLayer.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(0)));
        DismissLayer.SetValue(HavenProperties.Background, "Transparent");
        DismissLayer.SetValue(HavenProperties.ZIndex, 0);
        DismissLayer.Invoked += (_, _) => { if (LightDismiss) Dismiss(); };
        Add(DismissLayer);

        Card = new Container { Layout = HavenLayout.Vertical, Name = "PopoverCard" };
        Card.Accessibility.Role = HavenAccessibleRole.Group;
        Card.Accessibility.AccessibleName = accessibleName;
        Card.SetValue(HavenProperties.Width, HavenLength.Px(PreferredWidth));
        Card.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(92));
        Card.SetValue(HavenProperties.Background, "SurfaceRaised");
        Card.SetValue(HavenProperties.BorderColor, "Border");
        Card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        Card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(12)));
        Card.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        Card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        Card.SetValue(HavenProperties.Shadow, "Popup");
        Card.SetValue(HavenProperties.ZIndex, 1);
        Add(Card);

        Content = new Container { Layout = HavenLayout.Vertical, Name = "PopoverContent" };
        Content.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Content.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        Card.Add(Content);

        Position(anchor, sceneRoot);
    }

    public Container Card { get; }
    public Container Content { get; }
    public Button DismissLayer { get; }

    public double PreferredWidth { get; set; } = 260d;
    public double PreferredHeight { get; set; } = 240d;
    public bool LightDismiss { get; set; } = true;

    public event EventHandler? Dismissed;

    /// <summary>Adds the popover to the scene root (overlay; no layout shift).</summary>
    public void ShowIn(HavenElement sceneRoot)
    {
        ArgumentNullException.ThrowIfNull(sceneRoot);
        if (Parent is not null) Parent.Remove(this);
        sceneRoot.Add(this);
    }

    public void Dismiss()
    {
        var parent = Parent;
        if (parent is not null) parent.Remove(this);
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    public void SetContent(HavenElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        foreach (var child in Content.Children.ToArray()) Content.Remove(child);
        Content.Add(content);
    }

    private void Position(HavenElement anchor, HavenElement sceneRoot)
    {
        var width = PreferredWidth;
        var rootWidth = Math.Max(sceneRoot.Bounds.Width, width + Edge * 2);
        var rootHeight = Math.Max(sceneRoot.Bounds.Height, 120d);
        var anchorTop = anchor.Bounds.Y - sceneRoot.Bounds.Y;
        var anchorRight = anchor.Bounds.Right - sceneRoot.Bounds.X;
        var anchorBottom = anchor.Bounds.Bottom - sceneRoot.Bounds.Y;
        var estimatedHeight = Math.Max(80d, PreferredHeight);

        var left = Math.Clamp(anchorRight - width, Edge, Math.Max(Edge, rootWidth - width - Edge));
        var top = anchorBottom + AnchorGap;
        if (top + estimatedHeight > rootHeight - Edge)
            top = Math.Max(Edge, anchorTop - estimatedHeight - AnchorGap);

        Card.SetValue(HavenProperties.Left, HavenLength.Px(left));
        Card.SetValue(HavenProperties.Top, HavenLength.Px(top));
        Card.SetValue(HavenProperties.MaxHeight, HavenLength.Px(Math.Max(80d, rootHeight - Edge * 2)));
        Card.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
    }

    public override HavenComponentMetadata Metadata => new(
        "Popover",
        "HUI/shared/Components/Popover/Popover.cs",
        ["Popover"],
        [],
        "Content-agnostic anchored overlay; menus use PopupMenu, modals use Dialog.");
}
