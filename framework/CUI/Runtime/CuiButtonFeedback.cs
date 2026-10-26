using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using CakeOS.Cui.Themes;

namespace CakeOS.Cui.Runtime;

/// <summary>Decorative feedback on the CUI-owned template visual. The owner's
/// effective property store remains the sole authority for semantic paint.</summary>
internal sealed class CuiButtonFeedback
{
    private readonly Button _owner;
    private readonly Border _frame;
    private readonly CuiEffectivePropertyStore _store;
    private readonly List<IDisposable> _subscriptions = [];
    private TimeSpan _duration;
    private bool _active;

    public CuiButtonFeedback(Button owner, Border frame)
    {
        _owner = owner; _frame = frame; _store = CuiPropertyPresentation.For(owner);
        frame.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        frame.AttachedToVisualTree += (_, _) => Attach();
        frame.DetachedFromVisualTree += (_, _) => Detach();
    }

    private void Attach()
    {
        if (_active) return;
        _active = true;
        _owner.PropertyChanged += OwnerChanged;
        _store.Changed += EffectiveChanged;
        foreach (var key in new[] { "CuiTheme", "CuiReduceMotion", "CuiHighContrast", "CuiButtonMotionDuration",
            "CuiAccentPrimaryHoverEffect", "CuiAccentSecondaryHoverEffect", "CuiAccentTertiaryHoverEffect", "CuiAccentNegativeHoverEffect" })
            _subscriptions.Add(_owner.GetResourceObservable(key).Subscribe(new Observer(_ => Refresh())));
        Refresh();
    }

    private void Detach()
    {
        if (!_active) return;
        _active = false;
        _owner.PropertyChanged -= OwnerChanged;
        _store.Changed -= EffectiveChanged;
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
        _frame.Transitions = null; _duration = TimeSpan.Zero;
        _frame.Effect = null; _frame.RenderTransform = null;
    }

    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property.Name is "IsPointerOver" or "IsPressed" or "IsEnabled" or "IsEffectivelyEnabled") Refresh();
    }
    private void EffectiveChanged(object? sender, CuiEffectiveProperty property)
    {
        if (property.Name is "AccentRole" or "Chromeless") Refresh();
    }
    private object? Resource(string key) => _owner.TryFindResource(key, out var value) ? value : null;

    private void Refresh()
    {
        var store = _store;
        var role = store.Resolve("AccentRole").Value is CuiAccentRole value ? value : CuiAccentRole.None;
        var decorated = role is CuiAccentRole.Primary or CuiAccentRole.Secondary or CuiAccentRole.Tertiary or CuiAccentRole.Negative;
        var enabled = Resource("CuiTheme") is CuiTheme.Glow && Resource("CuiReduceMotion") is not true &&
            Resource("CuiHighContrast") is not true && store.Resolve("Chromeless").Value is not true && decorated;
        var duration = enabled && Resource("CuiButtonMotionDuration") is TimeSpan time ? time : TimeSpan.Zero;
        if (duration != _duration)
        {
            // These native transitions run on the internal frame's bindings,
            // not on Animation-priority CUI winner transport on the owner.
            _frame.Transitions = duration > TimeSpan.Zero ? new Transitions
            {
                new BrushTransition { Property = Border.BackgroundProperty, Duration = duration },
                new EffectTransition { Property = Visual.EffectProperty, Duration = duration },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = new BackEaseOut() }
            } : null;
            _duration = duration;
        }
        if (!enabled)
        {
            _frame.Effect = null; _frame.RenderTransform = null;
            return;
        }
        _frame.Effect = _owner.IsEffectivelyEnabled && _owner.IsPointerOver
            ? Resource($"CuiAccent{role}HoverEffect") as IEffect : null;
        _frame.RenderTransform = TransformOperations.Parse(_owner.IsEffectivelyEnabled && _owner.IsPressed ? "scale(1.025)" : "scale(1)");
    }

    private sealed class Observer(Action<object?> next) : IObserver<object?>
    {
        public void OnNext(object? value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
    }
}

/// <summary>Applies a background-only hover wash derived from the current effective brush. The
/// source brush and foreground stay untouched, so authored colours and text contrast remain owned
/// by the normal CUI cascade.</summary>
internal sealed class CuiHoverFeedback
{
    private readonly Control _owner;
    private readonly CuiMaterialBorder _frame;
    private readonly CuiEffectivePropertyStore _store;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _active;

    public CuiHoverFeedback(Control owner, CuiMaterialBorder frame)
    {
        _owner = owner; _frame = frame; _store = CuiPropertyPresentation.For(owner);
        frame.AttachedToVisualTree += (_, _) => Attach();
        frame.DetachedFromVisualTree += (_, _) => Detach();
    }

    private void Attach()
    {
        if (_active) return;
        _active = true;
        _owner.PropertyChanged += OwnerChanged;
        _store.Changed += EffectiveChanged;
        _subscriptions.Add(_owner.GetResourceObservable("CuiHighContrast").Subscribe(new Observer(_ => Refresh())));
        Refresh();
    }

    private void Detach()
    {
        if (!_active) return;
        _active = false;
        _owner.PropertyChanged -= OwnerChanged;
        _store.Changed -= EffectiveChanged;
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
        _frame.SetHoverTint(null);
    }

    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property.Name is "IsPointerOver" or "IsPressed" or "IsEnabled" or "IsEffectivelyEnabled") Refresh();
    }

    private void EffectiveChanged(object? sender, CuiEffectiveProperty property)
    {
        if (property.Name is "Background" or "Chromeless") Refresh();
    }

    private void Refresh()
    {
        var chromeless = _store.Contains("Chromeless") && _store.Resolve("Chromeless").Value is true;
        var active = _owner.IsEffectivelyEnabled && _owner.IsPointerOver
            && _owner is not ButtonBase { IsPressed: true }
            && !CuiPropertyPresentation.IsEmbeddedInput(_owner)
            && !chromeless
            && Resource("CuiHighContrast") is not true;
        _frame.SetHoverTint(active ? DeriveTint(_store.Resolve("Background").Value as IBrush) : null);
    }

    private object? Resource(string key) => _owner.TryFindResource(key, out var value) ? value : null;

    private static Color? DeriveTint(IBrush? brush)
    {
        var colour = Representative(brush);
        if (colour is null || colour.Value.A == 0) return null;
        var value = colour.Value;
        var luminance = .2126 * Linear(value.R / 255d) + .7152 * Linear(value.G / 255d) + .0722 * Linear(value.B / 255d);
        var tint = luminance > .5 ? Colors.Black : Colors.White;
        // Preserve the effective hue and alpha; this subtle neutral wash only changes lightness.
        var alpha = (byte)Math.Clamp(Math.Round(value.A * .075), 0, 20);
        return Color.FromArgb(alpha, tint.R, tint.G, tint.B);
    }

    private static double Linear(double channel) => channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);

    private static Color? Representative(IBrush? brush)
    {
        if (brush is CuiMaterialBrush material) return material.Tint;
        if (brush is CuiCustomMaterialBrush custom) return CustomRepresentative(custom.Fill);
        if (brush is ISolidColorBrush solid) return solid.Color;
        if (brush is IGradientBrush gradient) return Average(gradient.GradientStops.Select(stop => stop.Color));
        return null;
    }

    private static Color? CustomRepresentative(CuiCustomFill fill) => fill.Kind switch
    {
        CuiCustomFillKind.SolidColour => Parse(fill.Colour),
        CuiCustomFillKind.Gradient when fill.Gradient is { } gradient => Average(gradient.Stops.Select(stop => Parse(stop.Colour)).Where(colour => colour.HasValue).Select(colour => colour!.Value)),
        CuiCustomFillKind.Pattern when fill.Pattern is { } pattern => Average(new Color?[] { Parse(pattern.Foreground), Parse(pattern.Background) }.Where(colour => colour.HasValue).Select(colour => colour!.Value)),
        _ => null
    };

    private static Color? Parse(CuiCustomColour colour)
    {
        try
        {
            var parsed = Color.Parse(colour.Hex);
            var alpha = (byte)Math.Clamp(Math.Round(parsed.A * (1 - colour.TransparencyPercent / 100d)), 0, 255);
            return Color.FromArgb(alpha, parsed.R, parsed.G, parsed.B);
        }
        catch (FormatException) { return null; }
    }

    private static Color? Average(IEnumerable<Color> colours)
    {
        var values = colours.ToArray();
        if (values.Length == 0) return null;
        return Color.FromArgb(
            (byte)Math.Round(values.Average(colour => (double)colour.A)),
            (byte)Math.Round(values.Average(colour => (double)colour.R)),
            (byte)Math.Round(values.Average(colour => (double)colour.G)),
            (byte)Math.Round(values.Average(colour => (double)colour.B));
    }

    private sealed class Observer(Action<object?> next) : IObserver<object?>
    {
        public void OnNext(object? value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
    }
}
