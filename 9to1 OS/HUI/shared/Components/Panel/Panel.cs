namespace Haven.UI.Components;

/// <summary>
/// Generic titled panel/section with optional collapse. Inspector sections in
/// Canvas and Boards compose this; the inspected model stays app-owned.
/// </summary>
public sealed class Panel : Container
{
    private string _title = string.Empty;
    private bool _collapsible = true;
    private bool _expanded = true;

    public Panel()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Percent(100), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "SurfaceRaised", HavenValueSource.Default);
        SetValue(HavenProperties.BorderColor, "Border", HavenValueSource.Default);
        SetValue(HavenProperties.BorderWidth, HavenLength.Px(1), HavenValueSource.Default);
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)), HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(12)), HavenValueSource.Default);
        SetValue(HavenProperties.Gap, HavenLength.Px(8), HavenValueSource.Default);

        Header = new Container { Name = "Panel.Header", Layout = HavenLayout.Horizontal };
        Header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        Add(Header);

        TitleText = new Text { Level = TextLevel.H4, Content = string.Empty };
        TitleText.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        Header.Add(TitleText);

        CollapseButton = new Button { Variant = ButtonVariant.Ghost, Content = "−" };
        CollapseButton.Accessibility.AccessibleName = "Collapse section";
        CollapseButton.SetValue(HavenProperties.Width, HavenLength.Px(32));
        CollapseButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        CollapseButton.SetValue(HavenProperties.Padding, HavenThickness.Zero);
        CollapseButton.Invoked += (_, _) => IsExpanded = !IsExpanded;
        Header.Add(CollapseButton);

        Content = new Container { Name = "Panel.Content", Layout = HavenLayout.Vertical };
        Content.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Content.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        Add(Content);

        SyncChrome();
    }

    public Container Header { get; }
    public Text TitleText { get; }
    public Button CollapseButton { get; }
    public Container Content { get; }

    public event EventHandler? ExpandedChanged;

    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            TitleText.Content = _title;
            Accessibility.AccessibleName = string.IsNullOrWhiteSpace(_title) ? "Section" : _title;
        }
    }

    public bool IsCollapsible
    {
        get => _collapsible;
        set
        {
            _collapsible = value;
            if (!value) IsExpanded = true;
            SyncChrome();
        }
    }

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (!_collapsible) value = true;
            if (_expanded == value) return;
            _expanded = value;
            SyncChrome();
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetContent(HavenElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        foreach (var child in Content.Children.ToArray()) Content.Remove(child);
        Content.Add(content);
    }

    private void SyncChrome()
    {
        CollapseButton.SetValue(HavenProperties.Visibility, _collapsible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        CollapseButton.Content = _expanded ? "−" : "+";
        CollapseButton.Accessibility.AccessibleName = _expanded ? "Collapse section" : "Expand section";
        Content.SetValue(HavenProperties.Visibility, _expanded ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    public override HavenComponentMetadata Metadata => new(
        "Panel",
        "HUI/shared/Components/Panel/Panel.cs",
        ["Panel"],
        [],
        "Titled collapsible section; content stays caller-owned.");
}
