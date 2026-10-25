using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CakeOS.Cui.Runtime;

/// <summary>Directional chrome for one existing native horizontal viewport.
/// Extent, offset, focus navigation and wheel input remain owned by that viewport.</summary>
internal sealed class CuiHorizontalOverflowControls : IDisposable
{
    private readonly CuiContainerControl _owner;
    private readonly ScrollViewer _scroll;
    private readonly Button _left = new() { Name = "CuiOverflowLeft" };
    private readonly Button _right = new() { Name = "CuiOverflowRight" };
    private readonly Border _leftBackdrop;
    private readonly Border _rightBackdrop;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _enabled, _translateWheel;
    public Panel Host { get; }

    public CuiHorizontalOverflowControls(CuiContainerControl owner, ScrollViewer scroll)
    {
        _owner = owner; _scroll = scroll;
        _leftBackdrop = CreateArrow(_left, true);
        _rightBackdrop = CreateArrow(_right, false);
        Host = new Panel { Children = { scroll, _leftBackdrop, _rightBackdrop } };
        _left.Click += ScrollLeft; _right.Click += ScrollRight;
        _left.KeyDown += OnArrowKey; _right.KeyDown += OnArrowKey;
        scroll.ScrollChanged += OnScrollChanged;
        owner.PointerWheelChanged += OnWheel;
        owner.PropertyChanged += OwnerChanged;
        foreach (var key in new[] { "CuiPanel2Brush", "CuiTextBrush", "CuiLineBrush", "CuiShadowBrush", "CuiHighContrast", "CuiDisplayScale" })
            _subscriptions.Add(owner.GetResourceObservable(key).Subscribe(new Observer(_ => RefreshAppearance())));
        RefreshAppearance(); RefreshAvailability();
    }

    private Border CreateArrow(Button button, bool left)
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(left ? "M8,0 L2,6 L8,12 L10,10 L6,6 L10,2 Z" : "M2,0 L8,6 L2,12 L0,10 L4,6 L0,2 Z"),
            Width = 10, Height = 12, Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        icon.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, button.GetObservable(TemplatedControl.ForegroundProperty));
        const string source = "CUI overflow arrow";
        foreach (var (property, value) in new (string, object)[]
        {
            ("Padding", new Thickness(0)), ("Background", Brushes.Transparent),
            ("BorderWidth", new Thickness(0)), ("CornerRadius", new CornerRadius(10)),
            ("Chromeless", true), ("Content", icon)
        }) CuiPropertyPresentation.Set(button, property, value, CuiPropertyLevel.Theme, source, 1);
        AutomationProperties.SetName(button, left ? "Scroll left" : "Scroll right");
        ToolTip.SetTip(button, left ? "Scroll left" : "Scroll right");
        return new Border
        {
            Child = button, CornerRadius = new CornerRadius(10), Margin = new Thickness(3),
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = false
        };
    }

    public void SetEnabled(bool enabled, bool verticalScrolling)
    {
        _enabled = enabled;
        _translateWheel = !verticalScrolling;
        RefreshAvailability();
    }
    private object? Resource(string key) => _owner.TryFindResource(key, out var value) ? value : null;
    private void RefreshAppearance()
    {
        var scale = Resource("CuiDisplayScale") is double value ? value : 1;
        var high = Resource("CuiHighContrast") is true;
        var shadowColour = Resource("CuiShadowBrush") is ISolidColorBrush shadow ? shadow.Color : Colors.Black;
        shadowColour = Color.FromArgb((byte)(shadowColour.A / 2), shadowColour.R, shadowColour.G, shadowColour.B);
        foreach (var (backdrop, button, direction) in new[] { (_leftBackdrop, _left, 1), (_rightBackdrop, _right, -1) })
        {
            backdrop.Background = Resource("CuiPanel2Brush") as IBrush;
            backdrop.BorderBrush = Resource("CuiLineBrush") as IBrush;
            backdrop.BorderThickness = new Thickness(high ? scale : 0);
            backdrop.CornerRadius = new CornerRadius(10 * scale);
            backdrop.BoxShadow = high ? new BoxShadows() : new BoxShadows(new BoxShadow
            { OffsetX = direction * 3 * scale, Blur = 14 * scale, Color = shadowColour });
            CuiPropertyPresentation.Set(button, "Foreground", Resource("CuiTextBrush") as IBrush ?? Brushes.White,
                CuiPropertyLevel.Theme, "CUI overflow arrow", 1);
            foreach (var property in new[] { "Width", "Height", "MinWidth", "MinHeight" })
                CuiPropertyPresentation.Set(button, property, 36 * scale, CuiPropertyLevel.Theme, "CUI overflow arrow", 1);
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    { if (ReferenceEquals(args.Source, _scroll)) RefreshAvailability(); }
    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    { if (args.Property.Name is "IsEnabled" or "IsEffectivelyEnabled") RefreshAvailability(); }
    private void RefreshAvailability()
    {
        var end = Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width);
        var available = _enabled && _owner.IsEffectivelyEnabled && end > .5;
        _leftBackdrop.IsVisible = available && _scroll.Offset.X > .5;
        _rightBackdrop.IsVisible = available && end - _scroll.Offset.X > .5;
    }
    private bool Move(double delta)
    {
        var end = Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width);
        var next = Math.Clamp(_scroll.Offset.X + delta, 0, end);
        if (!_enabled || !_owner.IsEffectivelyEnabled || Math.Abs(next - _scroll.Offset.X) < .01) return false;
        _scroll.Offset = new Vector(next, _scroll.Offset.Y);
        RefreshAvailability();
        return true;
    }
    private void ScrollLeft(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Move(-Math.Max(48, _scroll.Viewport.Width * .75));
    private void ScrollRight(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Move(Math.Max(48, _scroll.Viewport.Width * .75));
    private void OnArrowKey(object? sender, KeyEventArgs args)
    {
        if (args.Key is Key.Left or Key.Right)
            args.Handled = Move(args.Key == Key.Left ? -48 : 48);
        else if (args.Key is Key.Home or Key.End)
            args.Handled = Move(args.Key == Key.Home ? -_scroll.Extent.Width : _scroll.Extent.Width);
    }
    private void OnWheel(object? sender, PointerWheelEventArgs args)
    {
        // Native horizontal/Shift wheel runs first. Translate an otherwise
        // unconsumed vertical wheel only for a horizontal-only strip, and let
        // ancestor scrolling continue once this viewport reaches its boundary.
        if (!args.Handled && _translateWheel && Math.Abs(args.Delta.X) < .01 && Math.Abs(args.Delta.Y) > .01)
            args.Handled = Move(-args.Delta.Y * 50);
    }
    public void Dispose()
    {
        _left.Click -= ScrollLeft; _right.Click -= ScrollRight;
        _left.KeyDown -= OnArrowKey; _right.KeyDown -= OnArrowKey;
        _scroll.ScrollChanged -= OnScrollChanged;
        _owner.PointerWheelChanged -= OnWheel;
        _owner.PropertyChanged -= OwnerChanged;
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
        CuiPropertyPresentation.Detach(_left); CuiPropertyPresentation.Detach(_right);
    }
    private sealed class Observer(Action<object?> next) : IObserver<object?>
    {
        public void OnNext(object? value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
    }
}
