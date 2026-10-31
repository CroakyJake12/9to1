using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// CUI's own separation construct. The Stage A mirrors measured 1,626 donor separators (GTK
/// GtkSeparator, LibreOffice toolbox/menu separators, XUL separator, Flutter Divider) that had nowhere to
/// go in CUI and were approximated as empty containers - which kept the gap but threw away the meaning:
/// a separator separates, and in a menu it is a semantic item rather than whitespace.
///
/// The rule is painted from the theme-owned CuiLineBrush (or CuiLineStrongBrush when the author asks for a
/// strong rule), observed live through CUI's resource observation, so it follows theme and high-contrast
/// changes instead of carrying a colour of its own. Avalonia's Panel.Render is sealed, so the rule is a
/// real child element - which also keeps it hit-testable and animatable through CUI's property store.
/// </summary>
public sealed class CuiSeparatorPanel : Panel, IDisposable
{
    private const double DefaultThickness = 1;
    private readonly Rectangle _rule = new();
    private readonly List<IDisposable> _subscriptions = [];
    private object? _lineBrush;
    private object? _strongBrush;
    private object? _thicknessResource;
    private bool _strong;
    private bool _disposed;

    public CuiSeparatorPanel()
    {
        Children.Add(_rule);
        foreach (var (key, apply) in new[]
                 {
                     ("CuiLineBrush", (Action<object?>)(value => { _lineBrush = value; ApplyBrush(); })),
                     ("CuiLineStrongBrush", (Action<object?>)(value => { _strongBrush = value; ApplyBrush(); })),
                     ("CuiSeparatorThickness", (Action<object?>)(value => { _thicknessResource = value; InvalidateMeasure(); }))
                 })
            _subscriptions.Add(this.GetResourceObservable(key).Subscribe(new ResourceObserver(apply)));
        ApplyBrush();
    }

    /// <summary>True when the author asked for the stronger rule (CuiLineStrongBrush).</summary>
    public bool Strong
    {
        get => _strong;
        set
        {
            if (_strong == value) return;
            _strong = value;
            ApplyBrush();
        }
    }

    /// <summary>The resolved rule element, for tests and for surfaces that must align to it.</summary>
    public Rectangle Rule => _rule;

    private double Thickness => _thicknessResource is double size && size > 0 ? size : DefaultThickness;

    private void ApplyBrush()
    {
        if (_disposed) return;
        // No resolved theme line brush means draw nothing: a separator must never invent a default colour.
        _rule.Fill = (_strong ? _strongBrush : _lineBrush) as IBrush;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var thickness = Thickness;
        // A separator spans the region it separates: it accepts the along-axis length offered to it and
        // contributes only its rule thickness across the other axis.
        var horizontal = !double.IsInfinity(availableSize.Width)
                         && (double.IsInfinity(availableSize.Height) || availableSize.Width >= availableSize.Height);
        var size = horizontal
            ? new Size(availableSize.Width, thickness)
            : new Size(thickness, double.IsInfinity(availableSize.Height) ? thickness : availableSize.Height);
        _rule.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var thickness = Thickness;
        if (finalSize.Width >= finalSize.Height)
        {
            var y = Math.Max(0, (finalSize.Height - thickness) / 2);
            _rule.Arrange(new Rect(0, y, finalSize.Width, Math.Max(thickness, finalSize.Height - 2 * y)));
        }
        else
        {
            var x = Math.Max(0, (finalSize.Width - thickness) / 2);
            _rule.Arrange(new Rect(x, 0, Math.Max(thickness, finalSize.Width - 2 * x), finalSize.Height));
        }
        return finalSize;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
    }

    private sealed class ResourceObserver(Action<object?> next) : IObserver<object?>
    {
        public void OnNext(object? value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
