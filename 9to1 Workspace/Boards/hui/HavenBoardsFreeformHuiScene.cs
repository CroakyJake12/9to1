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
/// Pointer dragging and keyboard-accessible movement both emit the same typed neutral frame
/// command, so the application session persists either interaction before publishing it.
/// </summary>
public sealed class HavenBoardsFreeformHuiScene : IDisposable
{
    public const double NudgeDistance = 24d;

    private readonly List<HavenButton> _wiredButtons = [];
    private HavenBoardSnapshot _snapshot = HavenBoardSnapshot.CreateDefault();
    private PopupMenu? _contextMenu;
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
        CloseContextMenu();
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
        surface.Accessibility.Description = "Right-click for card position actions.";
        surface.Accessibility.Focusable = true;
        surface.SecondaryInvoked += (_, _) => OpenContextMenu(surface, card, frame, persistedFrame);

        var dragHandle = new HavenBoardFreeformDragHandle(surface, card, frame);
        dragHandle.DragCompleted += (_, command) => CommandRequested?.Invoke(this, command);
        surface.Add(dragHandle);

        var title = new HavenText { Content = card.Title };
        title.SetValue(HavenProperties.FontSize, 13d);
        title.SetValue(HavenProperties.FontWeight, 700);
        title.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
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
        metadata.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        surface.Add(metadata);

        var actions = new Container { Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        actions.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        actions.Add(NudgeButton("Left", card, frame, -NudgeDistance, 0));
        actions.Add(NudgeButton("Up", card, frame, 0, -NudgeDistance));
        actions.Add(NudgeButton("Down", card, frame, 0, NudgeDistance));
        actions.Add(NudgeButton("Right", card, frame, NudgeDistance, 0));
        var contextActions = new HavenButton { Content = "Actions", Variant = ButtonVariant.Tertiary };
        contextActions.Accessibility.AccessibleName = $"Open position actions for {card.Title}";
        contextActions.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));
        contextActions.Invoked += (_, _) => OpenContextMenu(surface, card, frame, persistedFrame);
        _wiredButtons.Add(contextActions);
        actions.Add(contextActions);
        surface.Add(actions);

        return surface;
    }

    private void OpenContextMenu(
        HavenElement anchor,
        HavenBoardCard card,
        HavenBoardFreeformItem frame,
        bool persistedFrame)
    {
        CloseContextMenu();

        var highestZIndex = (_snapshot.Freeform?.Items ?? []).Select(item => item.ZIndex).DefaultIfEmpty(0).Max();
        var canBringForward = highestZIndex < HavenBoardReducer.FreeformZIndexLimit
            && frame.ZIndex <= highestZIndex;
        var nextZIndex = canBringForward ? highestZIndex + 1 : frame.ZIndex;

        _contextMenu = new PopupMenu(
            anchor,
            Root,
            [
                new PopupMenuItem(
                    "Bring to front",
                    () => CommandRequested?.Invoke(this, new SetFreeformCardFrameCommand(
                        card.Id,
                        frame.X,
                        frame.Y,
                        frame.Width,
                        frame.Height,
                        nextZIndex)),
                    Enabled: canBringForward),
                new PopupMenuItem(
                    "Reset position",
                    () => CommandRequested?.Invoke(this, new RemoveFreeformCardFrameCommand(card.Id)),
                    Enabled: persistedFrame),
                new PopupMenuItem("Close", () => { })
            ],
            accessibleName: $"Position actions for {card.Title}");
        _contextMenu.Dismissed += OnContextMenuDismissed;
        Root.Add(_contextMenu);
    }

    private void OnContextMenuDismissed(object? sender, EventArgs args)
    {
        if (ReferenceEquals(_contextMenu, sender))
            _contextMenu = null;
    }

    private void CloseContextMenu()
    {
        if (_contextMenu is null) return;
        var menu = _contextMenu;
        _contextMenu = null;
        menu.Dismissed -= OnContextMenuDismissed;
        menu.Dismiss();
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
        => HavenBoardsMarkup.Load("Boards.Freeform.cui");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseContextMenu();
        UnwireButtons();
        CommandRequested = null;
    }

    private sealed record LocatedCard(HavenBoardGroup Group, HavenBoardCard Card);
}

internal sealed class HavenBoardFreeformDragHandle : Container, IHavenPointerInputTarget
{
    private readonly HavenElement _cardSurface;
    private readonly HavenBoardCard _card;
    private readonly HavenBoardFreeformItem _frame;
    private HavenPoint _startPointer;
    private double _currentX;
    private double _currentY;
    private bool _dragging;
    private bool _moved;

    internal HavenBoardFreeformDragHandle(
        HavenElement cardSurface,
        HavenBoardCard card,
        HavenBoardFreeformItem frame)
    {
        _cardSurface = cardSurface;
        _card = card;
        _frame = frame;
        _currentX = frame.X;
        _currentY = frame.Y;

        Name = $"FreeformDragHandle_{SafeName(card.Id)}";
        Layout = HavenLayout.Horizontal;
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
        SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 8px"));
        SetValue(HavenProperties.Background, "SurfaceRaised");
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(8)));
        SetValue(HavenProperties.Cursor, HavenCursor.Grab);
        Accessibility.AccessibleName = $"Drag {card.Title} on freeform board";
        Accessibility.Description = "Pointer drag handle. Use the card movement buttons for keyboard positioning.";

        var label = new HavenText { Content = "Drag card" };
        label.SetValue(HavenProperties.FontSize, 11d);
        label.SetValue(HavenProperties.Foreground, "TextSecondary");
        label.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        Add(label);
    }

    internal event EventHandler<SetFreeformCardFrameCommand>? DragCompleted;

    public bool PointerPressed(HavenPointerInput input)
    {
        if (input.Button != HavenPointerButton.Primary) return false;
        _dragging = true;
        _moved = false;
        _startPointer = input.Position;
        _currentX = _frame.X;
        _currentY = _frame.Y;
        SetValue(HavenProperties.Cursor, HavenCursor.Grabbing);
        return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        if (!_dragging) return false;
        var nextX = Math.Clamp(
            _frame.X + input.Position.X - _startPointer.X,
            -HavenBoardReducer.FreeformCoordinateLimit,
            HavenBoardReducer.FreeformCoordinateLimit);
        var nextY = Math.Clamp(
            _frame.Y + input.Position.Y - _startPointer.Y,
            -HavenBoardReducer.FreeformCoordinateLimit,
            HavenBoardReducer.FreeformCoordinateLimit);
        _moved |= Math.Abs(nextX - _frame.X) >= .0001d || Math.Abs(nextY - _frame.Y) >= .0001d;
        _currentX = nextX;
        _currentY = nextY;
        _cardSurface.SetValue(HavenProperties.Left, HavenLength.Px(nextX));
        _cardSurface.SetValue(HavenProperties.Top, HavenLength.Px(nextY));
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        if (!_dragging) return false;
        _dragging = false;
        SetValue(HavenProperties.Cursor, HavenCursor.Grab);
        if (_moved)
        {
            DragCompleted?.Invoke(this, new SetFreeformCardFrameCommand(
                _card.Id,
                _currentX,
                _currentY,
                _frame.Width,
                _frame.Height,
                _frame.ZIndex));
        }
        return true;
    }

    public bool PointerCancelled(HavenPointerInput input)
    {
        if (!_dragging) return false;
        _dragging = false;
        _moved = false;
        _currentX = _frame.X;
        _currentY = _frame.Y;
        _cardSurface.SetValue(HavenProperties.Left, HavenLength.Px(_frame.X));
        _cardSurface.SetValue(HavenProperties.Top, HavenLength.Px(_frame.Y));
        SetValue(HavenProperties.Cursor, HavenCursor.Grab);
        return true;
    }

    private static string SafeName(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "Item" : new string(chars);
    }
}
