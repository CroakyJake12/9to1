using CakeOS.Apps.Boards.Contract;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenCanvas = Haven.UI.Components.Canvas;
using HavenText = Haven.UI.Components.Text;

namespace CakeOS.Apps.Boards.Hui;

/// <summary>
/// HUI-native spatial projection of the neutral Haven Boards snapshot.
///
/// This scene deliberately shares card identity, hierarchy, attachment metadata, commands, and
/// persistence with the structured projection. AppFlowy remains outside this renderer boundary.
/// Pointer dragging is intentionally not claimed by this first slice; every position mutation is
/// available through keyboard-accessible nudge controls that emit typed neutral commands.
/// </summary>
public sealed class HavenBoardsFreeformHuiScene : IDisposable
{
    public const double NudgeDistance = 24d;

    private readonly List<HavenButton> _wiredButtons = [];
    private HavenBoardSnapshot _snapshot = HavenBoardSnapshot.CreateDefault();
    private bool _disposed;

    public HavenBoardsFreeformHuiScene()
    {
        Root = BuildRoot();
        Surface = Get<HavenCanvas>("FreeformSurface");
        BoardTitle = Get<HavenText>("FreeformBoardTitle");
        Status = Get<HavenText>("FreeformStatus");
    }

    public Page Root { get; }
    public HavenCanvas Surface { get; }
    public HavenText BoardTitle { get; }
    public HavenText Status { get; }

    public event HavenBoardCommandRequestedHandler? CommandRequested;

    public void SetSnapshot(HavenBoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ThrowIfDisposed();
        HavenBoardReducer.Validate(snapshot);

        _snapshot = snapshot;
        BoardTitle.Content = snapshot.Title;
        RebuildSurface();
    }

    public void SetStatus(string? status)
    {
        ThrowIfDisposed();
        Status.Content = status ?? string.Empty;
        Status.SetValue(
            HavenProperties.Visibility,
            string.IsNullOrWhiteSpace(status) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private void RebuildSurface()
    {
        UnwireButtons();
        foreach (var child in Surface.Children.ToArray())
            Surface.Remove(child);

        var cards = _snapshot.Groups
            .SelectMany(group => group.Cards.Select(card => new LocatedCard(group, card)))
            .ToArray();

        if (cards.Length == 0)
        {
            var empty = new HavenText { Content = "This board has no cards yet." };
            empty.SetValue(HavenProperties.Left, HavenLength.Px(24));
            empty.SetValue(HavenProperties.Top, HavenLength.Px(24));
            empty.SetValue(HavenProperties.Foreground, "TextSecondary");
            Surface.Add(empty);
            return;
        }

        var explicitFrames = (_snapshot.Freeform?.Items ?? [])
            .ToDictionary(item => item.CardId, StringComparer.Ordinal);

        for (var index = 0; index < cards.Length; index++)
        {
            var located = cards[index];
            var frame = explicitFrames.TryGetValue(located.Card.Id, out var persisted)
                ? persisted
                : DefaultFrame(located.Card.Id, index);
            Surface.Add(BuildCard(located.Group, located.Card, frame, explicitFrames.ContainsKey(located.Card.Id)));
        }
    }

    private Container BuildCard(
        HavenBoardGroup group,
        HavenBoardCard card,
        HavenBoardFreeformItem frame,
        bool persistedFrame)
    {
        var surface = new Container { Layout = HavenLayout.Vertical };
        surface.Name = $"FreeformCard_{SafeName(card.Id)}";
        surface.SetValue(HavenProperties.Left, HavenLength.Px(frame.X));
        surface.SetValue(HavenProperties.Top, HavenLength.Px(frame.Y));
        surface.SetValue(HavenProperties.Width, HavenLength.Px(frame.Width));
        surface.SetValue(HavenProperties.Height, HavenLength.Px(frame.Height));
        surface.SetValue(HavenProperties.ZIndex, frame.ZIndex);
        surface.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(10)));
        surface.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        surface.SetValue(HavenProperties.Background, "Surface");
        surface.SetValue(HavenProperties.BorderColor, "Border");
        surface.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        surface.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        surface.Accessibility.AccessibleName = $"Freeform board card {card.Title}";

        var title = new HavenText { Content = card.Title };
        title.SetValue(HavenProperties.FontSize, 13d);
        title.SetValue(HavenProperties.FontWeight, 700);
        surface.Add(title);

        var details = new List<string> { group.Title };
        if (!persistedFrame)
            details.Add("Default position");
        if (!string.IsNullOrWhiteSpace(card.ParentCardId))
            details.Add("Nested card");
        var attachmentCount = card.Attachments?.Count ?? 0;
        if (attachmentCount > 0)
            details.Add(attachmentCount == 1 ? "1 attachment" : $"{attachmentCount} attachments");

        var metadata = new HavenText { Content = string.Join(" · ", details) };
        metadata.SetValue(HavenProperties.Foreground, "TextSecondary");
        metadata.SetValue(HavenProperties.FontSize, 11d);
        surface.Add(metadata);

        var actions = new Container { Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        actions.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        actions.Add(NudgeButton("Left", card, frame, -NudgeDistance, 0));
        actions.Add(NudgeButton("Up", card, frame, 0, -NudgeDistance));
        actions.Add(NudgeButton("Down", card, frame, 0, NudgeDistance));
        actions.Add(NudgeButton("Right", card, frame, NudgeDistance, 0));
        surface.Add(actions);

        return surface;
    }

    private HavenButton NudgeButton(
        string content,
        HavenBoardCard card,
        HavenBoardFreeformItem frame,
        double deltaX,
        double deltaY)
    {
        var nextX = frame.X + deltaX;
        var nextY = frame.Y + deltaY;
        var enabled = Math.Abs(nextX) <= HavenBoardReducer.FreeformCoordinateLimit
            && Math.Abs(nextY) <= HavenBoardReducer.FreeformCoordinateLimit;
        var direction = content.ToLowerInvariant();

        var command = enabled
            ? new SetFreeformCardFrameCommand(
                card.Id,
                nextX,
                nextY,
                frame.Width,
                frame.Height,
                frame.ZIndex)
            : null;

        return ActionButton(
            content,
            $"Move {card.Title} {direction} on freeform board",
            enabled,
            command);
    }

    private HavenButton ActionButton(
        string content,
        string accessibleName,
        bool enabled,
        HavenBoardCommand? command)
    {
        var button = new HavenButton { Content = content, Variant = ButtonVariant.Tertiary };
        button.Accessibility.AccessibleName = accessibleName;
        SetEnabled(button, enabled);
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));
        if (command is not null)
        {
            EventHandler handler = (_, _) => CommandRequested?.Invoke(this, command);
            button.Invoked += handler;
        }

        _wiredButtons.Add(button);
        return button;
    }

    private static HavenBoardFreeformItem DefaultFrame(string cardId, int index)
    {
        const double startX = 24d;
        const double startY = 24d;
        const double horizontalStep = 320d;
        const double verticalStep = 220d;
        const int columns = 3;

        var column = index % columns;
        var row = index / columns;
        return new HavenBoardFreeformItem(
            cardId,
            startX + (column * horizontalStep),
            startY + (row * verticalStep),
            280,
            160,
            index);
    }

    private static void SetEnabled(HavenElement element, bool enabled)
    {
        element.SetValue(HavenProperties.Enabled, enabled);
        element.Accessibility.Enabled = enabled;
        element.SetState(HavenElementState.Disabled, !enabled);
    }

    private void UnwireButtons()
    {
        _wiredButtons.Clear();
    }

    private T Get<T>(string name) where T : HavenElement =>
        (T)Root.DescendantsAndSelf().Single(element => element.Name == name);

    private static string SafeName(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "Item" : new string(chars);
    }

    private static Page BuildRoot()
    {
        const string markup = """
            <Page Name="BoardsFreeformRoot" Layout="Grid" Width="100%" Height="100%" Rows="Auto Auto 1fr Auto" Gap="12px" Padding="22px" Background="Surface">
              <Text Name="FreeformBoardTitle" Row="0" Content="Haven Boards" Level="H1" />
              <Text Row="1" Content="Freeform board · local first" Foreground="TextSecondary" FontSize="12" />
              <Canvas Name="FreeformSurface" Row="2" Width="100%" Height="100%" Overflow="Scroll" Clip="true" Background="SurfaceRaised" />
              <Text Name="FreeformStatus" Row="3" Content="" Foreground="TextSecondary" FontSize="11" Visibility="Collapsed" />
            </Page>
            """;
        return (Page)new HavenMarkupParser().Parse(markup, "Boards.Freeform.hui");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnwireButtons();
        CommandRequested = null;
    }

    private sealed record LocatedCard(HavenBoardGroup Group, HavenBoardCard Card);
}
