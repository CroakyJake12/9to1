using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>Generic finite command browsing scope. All geometry, buttons, icons and position
/// indicators are authored by CUI children. Browsing wraps deliberately; it never dispatches
/// a host command. Only an immutable current identity captured at activation can dispatch.
/// The host supplies at most five actual eligible commands, with no repeated/filler identities.</summary>
public sealed class CuiActionShelf : CuiContainerControl, ICuiBindingContext,
    ICuiRepeatItemBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    public const string ActivateCommand = "shelf.activate";
    public const string NextCommand = "shelf.next";
    public const string PreviousCommand = "shelf.previous";
    private CuiActionShelfItem[] _items = [];
    private int _index, _generation;
    private int? _pressGeneration;
    private int _pressSequence;
    private Key? _activationKey;
    private int _keyGeneration;
    private bool _keyConsumed;
    private bool _withdrawn, _navigating, _dragging;
    private Point? _pressPoint;
    private readonly DispatcherTimer _navigationTimer;
    public CuiActionShelf() : base("Overlay")
    {
        _navigationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _navigationTimer.Tick += HideNavigation;
        AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerPressedEvent, OnPress, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerMovedEvent, OnMove, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerReleasedEvent, OnRelease, RoutingStrategies.Tunnel);
    }
    public IReadOnlyList<CuiActionShelfItem>? Source
    {
        get => Array.AsReadOnly(_items);
        set
        {
            if (_withdrawn) return;
            var items = value?.ToArray() ?? [];
            if (items.Length > 5 || items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) ||
                string.IsNullOrWhiteSpace(item.Command) || string.IsNullOrWhiteSpace(item.Label)) ||
                items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length ||
                items.Select(item => item.Command).Distinct(StringComparer.Ordinal).Count() != items.Length)
                throw new ArgumentException("ActionShelf requires at most five distinct eligible command identities.", nameof(value));
            if (_items.SequenceEqual(items)) return;
            _items = items;
            _index = 0; // A changed eligibility/order starts at the host's primary action.
            _generation++;
            Publish();
        }
    }
    internal ICuiBindingContext? InnerContext { get; set; }
    internal ICuiActionDispatcher? InnerDispatcher { get; set; }
    public Action? RefreshHost { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private CuiActionShelfItem? Front => _items.Length == 0 ? null : _items[_index];
    private CuiActionShelfItem? Rear => _items.Length < 2 ? null : _items[(_index + 1) % _items.Length];
    private void Publish()
    {
        if (_withdrawn) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        RefreshHost?.Invoke();
    }
    private void HideNavigation(object? sender, EventArgs args)
    {
        _navigationTimer.Stop();
        if (!_navigating) return;
        _navigating = false;
        Publish();
    }
    private void Browse(int direction)
    {
        if (_withdrawn || _items.Length < 2) return;
        _index = (_index + direction + _items.Length) % _items.Length;
        _generation++;
        _navigating = true;
        _navigationTimer.Stop();
        _navigationTimer.Start();
        Publish();
    }
    private static Button? ShelfButton(object? source)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
        {
            if (node is Button button && button.Tag is string tag &&
                tag is ActivateCommand or NextCommand or PreviousCommand) return button;
            if (node is CuiActionShelf) break;
        }
        return null;
    }
    private void OnWheel(object? sender, PointerWheelEventArgs args)
    {
        if (_withdrawn || _items.Length < 2 || args.Handled) return;
        var delta = args.Delta.Y != 0 ? args.Delta.Y : args.Delta.X;
        if (delta == 0) return;
        Browse(delta > 0 ? -1 : 1);
        args.Handled = true; // Only the actual shelf's routed subtree consumes browsing.
    }
    private void OnKey(object? sender, KeyEventArgs args)
    {
        if (_withdrawn || ShelfButton(args.Source) is null || args.KeyModifiers != KeyModifiers.None) return;
        if (args.Key is Key.Left or Key.Up or Key.Right or Key.Down)
        {
            Browse(args.Key is Key.Left or Key.Up ? -1 : 1);
            args.Handled = true;
        }
        else if (args.Key is Key.Enter or Key.Space)
        {
            if (_activationKey is null)
            {
                _activationKey = args.Key;
                _keyGeneration = _generation;
                _keyConsumed = false;
            }
            _pressGeneration = _keyGeneration;
        }
    }
    private void OnKeyUp(object? sender, KeyEventArgs args)
    {
        if (_activationKey != args.Key) return;
        var key = _activationKey;
        Dispatcher.UIThread.Post(() =>
        {
            if (_activationKey == key) { _activationKey = null; _keyConsumed = false; _pressGeneration = null; }
        });
    }
    private void OnPress(object? sender, PointerPressedEventArgs args)
    {
        if (_withdrawn || ShelfButton(args.Source) is null || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressSequence++;
        _activationKey = null;
        _keyConsumed = false;
        _pressGeneration = args.ClickCount > 1 ? -1 : _generation;
        _pressPoint = args.GetPosition(this);
        _dragging = false;
    }
    private void OnMove(object? sender, PointerEventArgs args)
    {
        if (_withdrawn || _pressPoint is not { } origin || _items.Length < 2) return;
        var current = args.GetPosition(this);
        var dx = current.X - origin.X;
        var dy = current.Y - origin.Y;
        var delta = Math.Abs(dx) > Math.Abs(dy) ? dx : dy;
        if (Math.Abs(delta) < 12) return;
        _dragging = true;
        _pressGeneration = -1;
        _pressPoint = current;
        Browse(delta < 0 ? 1 : -1);
        args.Handled = true;
    }
    private void OnRelease(object? sender, PointerReleasedEventArgs args)
    {
        _pressPoint = null;
        if (_dragging) args.Handled = true;
        // Button.Click occurs during this same release. Keep its original press identity until
        // synchronous loader capture; clear an unused arm afterwards without dispatching work.
        var sequence = _pressSequence;
        Dispatcher.UIThread.Post(() =>
        {
            if (_pressSequence == sequence) { _pressGeneration = null; _dragging = false; }
        });
    }
    internal bool TryCaptureActivation(out object? capture)
    {
        capture = null;
        var arm = _pressGeneration;
        _pressGeneration = null;
        if (_withdrawn || Front is not { } front || arm is { } generation && generation != _generation ||
            _dragging || _activationKey is not null && _keyConsumed) return false;
        if (_activationKey is not null) _keyConsumed = true;
        capture = new CuiActionShelfActivation(this, _generation, front);
        return true;
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (_withdrawn) return ValueTask.CompletedTask;
        if (command is NextCommand or PreviousCommand)
        {
            Browse(command == NextCommand ? 1 : -1);
            return ValueTask.CompletedTask;
        }
        if (command != ActivateCommand)
            return InnerDispatcher?.DispatchAsync(command, parameter, cancellationToken) ??
                ValueTask.FromException(new InvalidOperationException("The action shelf has no owning command dispatcher."));
        if (parameter is not CuiActionShelfActivation actual || !ReferenceEquals(actual.Owner, this) ||
            actual.Generation != _generation || actual.Item != Front) return ValueTask.CompletedTask;
        if (InnerDispatcher is ICuiActionAvailability availability && availability.IsActionAvailable(actual.Item.Command) == false)
            return ValueTask.CompletedTask;
        return InnerDispatcher?.DispatchAsync(actual.Item.Command, actual.Item.Parameter, cancellationToken) ??
            ValueTask.FromException(new InvalidOperationException("The action shelf has no owning command dispatcher."));
    }
    public bool? IsActionAvailable(string command) => command switch
    {
        ActivateCommand => !_withdrawn && Front is not null,
        NextCommand or PreviousCommand => !_withdrawn && _items.Length > 1,
        _ => InnerDispatcher is ICuiActionAvailability availability ? availability.IsActionAvailable(command) : null
    };
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Shelf.HasFront" => Front is not null,
            "Shelf.HasNext" => Rear is not null,
            "Shelf.IsNavigating" => _navigating && _items.Length > 1,
            "Shelf.Count" => _items.Length,
            "Shelf.Position" => _items.Length == 0 ? 0 : _index + 1,
            "Shelf.Dots" => string.Join(" ", Enumerable.Range(0, _items.Length).Select(i => i == _index ? "●" : "○")),
            "Shelf.FrontLabel" => Front?.Label ?? "",
            "Shelf.FrontIcon" => Front?.IconSource,
            "Shelf.FrontTooltip" => Front?.Tooltip ?? "",
            "Shelf.FrontAnnouncement" => Front is { } front ? $"{front.Label}, {_index + 1} of {_items.Length}" : "",
            "Shelf.RearIcon" => Rear?.IconSource,
            "Shelf.RearAnnouncement" => Rear is { } rear ? $"Show {rear.Label}, next action" : "",
            _ => null
        };
        if (path.StartsWith("Shelf.", StringComparison.Ordinal)) return value is not null;
        return InnerContext?.TryGetValue(path, out value) == true;
    }
    bool ICuiRepeatItemBindingContext.TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        return InnerContext is ICuiRepeatItemBindingContext inner && inner.TryGetItemValue(item, path, out value);
    }
    bool ICuiRepeatItemBindingContext.TrySetItemValue(object item, string path, object? value) =>
        InnerContext is ICuiRepeatItemBindingContext inner && inner.TrySetItemValue(item, path, value);
    internal void Withdraw()
    {
        _withdrawn = true;
        _generation++;
        _navigationTimer.Stop();
        _navigationTimer.Tick -= HideNavigation;
        RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        RemoveHandler(InputElement.KeyDownEvent, OnKey);
        RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        RemoveHandler(InputElement.PointerPressedEvent, OnPress);
        RemoveHandler(InputElement.PointerMovedEvent, OnMove);
        RemoveHandler(InputElement.PointerReleasedEvent, OnRelease);
        _items = [];
        InnerContext = null;
        InnerDispatcher = null;
        RefreshHost = null;
        PropertyChanged = null;
    }
}
