namespace Haven.UI.Components;

/// <summary>
/// Generic application header bar: leading icon, title/subtitle block, and a
/// trailing action slot. Canvas (Rnote) and Boards (AppFlowy) headers compose
/// this instead of forking their own; product-specific controls stay in the apps.
/// </summary>
public sealed class HeaderBar : Container
{
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _iconKey = string.Empty;
    private bool _showDivider = true;

    public HeaderBar()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Header";
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Percent(100), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Surface", HavenValueSource.Default);
        SetValue(HavenProperties.Gap, HavenLength.Px(0), HavenValueSource.Default);

        Row = new Container { Name = "HeaderBar.Row", Layout = HavenLayout.Horizontal };
        Row.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Row.SetValue(HavenProperties.MinHeight, HavenLength.Px(56));
        Row.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        Row.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 16px"));
        Row.SetValue(HavenProperties.Background, "Transparent");

        LeadingIcon = new Icon { Key = string.Empty };
        LeadingIcon.SetValue(HavenProperties.Width, HavenLength.Px(28));
        LeadingIcon.SetValue(HavenProperties.Height, HavenLength.Px(28));
        LeadingIcon.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Row.Add(LeadingIcon);

        TitleBlock = new Container { Name = "HeaderBar.TitleBlock", Layout = HavenLayout.Vertical };
        TitleBlock.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        TitleBlock.SetValue(HavenProperties.Gap, HavenLength.Px(2));

        TitleText = new Text { Level = TextLevel.H3, Content = string.Empty };
        TitleBlock.Add(TitleText);

        SubtitleText = new Text { Level = TextLevel.Caption, Content = string.Empty };
        SubtitleText.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        TitleBlock.Add(SubtitleText);
        Row.Add(TitleBlock);

        Actions = new Container { Name = "HeaderBar.Actions", Layout = HavenLayout.Horizontal };
        Actions.Accessibility.AccessibleName = "Header actions";
        Actions.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        Row.Add(Actions);

        Add(Row);

        Divider = new Separator { Orientation = SeparatorOrientation.Horizontal };
        Divider.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Add(Divider);
    }

    public Container Row { get; }
    public Icon LeadingIcon { get; }
    public Container TitleBlock { get; }
    public Text TitleText { get; }
    public Text SubtitleText { get; }
    public Container Actions { get; }
    public Separator Divider { get; }

    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            TitleText.Content = _title;
            Accessibility.AccessibleName = string.IsNullOrWhiteSpace(_title) ? "Header" : _title;
        }
    }

    public string Subtitle
    {
        get => _subtitle;
        set
        {
            _subtitle = value ?? string.Empty;
            SubtitleText.Content = _subtitle;
            SubtitleText.SetValue(HavenProperties.Visibility,
                string.IsNullOrWhiteSpace(_subtitle) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        }
    }

    public string IconKey
    {
        get => _iconKey;
        set
        {
            _iconKey = value ?? string.Empty;
            LeadingIcon.Key = _iconKey;
            LeadingIcon.SetValue(HavenProperties.Visibility,
                string.IsNullOrWhiteSpace(_iconKey) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        }
    }

    public bool ShowDivider
    {
        get => _showDivider;
        set
        {
            _showDivider = value;
            Divider.SetValue(HavenProperties.Visibility, value ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        }
    }

    public void AddAction(Button action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Actions.Add(action);
    }

    public override HavenComponentMetadata Metadata => new(
        "HeaderBar",
        "HUI/shared/Components/HeaderBar/HeaderBar.cs",
        ["HeaderBar"],
        [],
        "Generic app header composed from Haven primitives; product controls live in the action slot, not in HUI.");
}
