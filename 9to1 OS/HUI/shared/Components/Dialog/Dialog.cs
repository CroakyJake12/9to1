namespace Haven.UI.Components;

/// <summary>
/// Generic modal dialog: centered card with title, caller-owned content, and an
/// action row. Apps (Canvas export, Boards delete-group confirm) own the words
/// and commands; HUI owns overlay, focus, Escape, and accessibility.
/// </summary>
public sealed class Dialog : Container, IHavenKeyboardInputTarget
{
    private const double Edge = 16d;
    private string _title = string.Empty;
    private bool _dismissable = true;

    public Dialog(string title = "")
    {
        Accessibility.Role = HavenAccessibleRole.Dialog;
        Accessibility.Focusable = true;
        Layout = HavenLayout.Canvas;
        Name = "DialogOverlay";
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Percent(100));
        SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        SetValue(HavenProperties.ZIndex, 600);
        SetValue(HavenProperties.PointerEvents, HavenPointerEvents.ChildrenOnly);
        SetValue(HavenProperties.Background, "Overlay");

        DismissLayer = new Button { Variant = ButtonVariant.Text, Content = string.Empty, Name = "DialogDismiss" };
        DismissLayer.Accessibility.AccessibleName = "Dismiss dialog";
        DismissLayer.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        DismissLayer.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        DismissLayer.SetValue(HavenProperties.MinHeight, HavenLength.Px(0));
        DismissLayer.SetValue(HavenProperties.Padding, HavenThickness.Zero);
        DismissLayer.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(0)));
        DismissLayer.SetValue(HavenProperties.Background, "Transparent");
        DismissLayer.SetValue(HavenProperties.ZIndex, 0);
        DismissLayer.Invoked += (_, _) => RequestClose();
        Add(DismissLayer);

        Card = new Container { Layout = HavenLayout.Vertical, Name = "DialogCard" };
        Card.SetValue(HavenProperties.Width, HavenLength.Px(PreferredWidth));
        Card.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(92));
        Card.SetValue(HavenProperties.Background, "SurfaceRaised");
        Card.SetValue(HavenProperties.BorderColor, "Border");
        Card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        Card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(18)));
        Card.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        Card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        Card.SetValue(HavenProperties.Shadow, "Popup");
        Card.SetValue(HavenProperties.ZIndex, 1);
        Add(Card);

        TitleText = new Text { Level = TextLevel.H3, Content = string.Empty };
        Card.Add(TitleText);

        Content = new Container { Layout = HavenLayout.Vertical, Name = "DialogContent" };
        Content.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Content.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        Card.Add(Content);

        ActionsRow = new Container { Layout = HavenLayout.Horizontal, Name = "DialogActions" };
        ActionsRow.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        ActionsRow.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        ActionsRow.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        Card.Add(ActionsRow);

        Title = title;
    }

    public Container Card { get; }
    public Text TitleText { get; }
    public Container Content { get; }
    public Container ActionsRow { get; }
    public Button DismissLayer { get; }

    public double PreferredWidth { get; set; } = 420d;
    public double PreferredHeight { get; set; } = 280d;

    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            TitleText.Content = _title;
            Accessibility.AccessibleName = string.IsNullOrWhiteSpace(_title) ? "Dialog" : _title;
        }
    }

    /// <summary>Whether overlay click and Escape request close. Default true.</summary>
    public bool IsDismissable
    {
        get => _dismissable;
        set
        {
            _dismissable = value;
            DismissLayer.SetValue(HavenProperties.Visibility, value ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        }
    }

    /// <summary>Raised for light-dismiss and Escape. The host validates then calls <see cref="Close"/>.</summary>
    public event EventHandler? RequestCloseInvoked;

    public event EventHandler? Closed;

    public void SetContent(HavenElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        foreach (var child in Content.Children.ToArray()) Content.Remove(child);
        Content.Add(content);
    }

    public void AddAction(Button action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        ActionsRow.Add(action);
    }

    /// <summary>Centers the card in the scene root and adds the overlay.</summary>
    public void ShowIn(HavenElement sceneRoot)
    {
        ArgumentNullException.ThrowIfNull(sceneRoot);
        if (Parent is not null) Parent.Remove(this);
        var rootWidth = Math.Max(sceneRoot.Bounds.Width, PreferredWidth + Edge * 2);
        var rootHeight = Math.Max(sceneRoot.Bounds.Height, PreferredHeight + Edge * 2);
        var width = Math.Min(PreferredWidth, rootWidth - Edge * 2);
        Card.SetValue(HavenProperties.Width, HavenLength.Px(width));
        Card.SetValue(HavenProperties.Left, HavenLength.Px(Math.Max(Edge, (rootWidth - width) / 2)));
        Card.SetValue(HavenProperties.Top, HavenLength.Px(Math.Max(Edge, (rootHeight - PreferredHeight) / 2)));
        Card.SetValue(HavenProperties.MaxHeight, HavenLength.Px(Math.Max(80d, rootHeight - Edge * 2)));
        Card.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        sceneRoot.Add(this);
    }

    public void Close()
    {
        var parent = Parent;
        if (parent is not null) parent.Remove(this);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public bool KeyDown(HavenKeyInput input)
    {
        if (input.Key != HavenKey.Escape) return false;
        RequestClose();
        return true;
    }

    private void RequestClose()
    {
        if (!_dismissable) return;
        RequestCloseInvoked?.Invoke(this, EventArgs.Empty);
    }

    public override HavenComponentMetadata Metadata => new(
        "Dialog",
        "HUI/shared/Components/Dialog/Dialog.cs",
        ["Dialog"],
        [],
        "Modal overlay with Escape semantics; words and commands stay app-owned.");
}
