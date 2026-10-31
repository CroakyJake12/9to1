using System.Collections.Specialized;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
internal sealed class CuiHoverFeedback : IDisposable
{
    private readonly Control _owner;
    private readonly CuiMaterialBorder _frame;
    private readonly CuiEffectivePropertyStore _store;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _active;
    private bool _dragging;
    private IDisposable? _backgroundBrushSubscription;
    private IDisposable? _panelBrushSubscription;
    private IBrush? _observedBackground;
    private IBrush? _observedPanel;
    private readonly string? _requiredClass;

    public CuiHoverFeedback(Control owner, CuiMaterialBorder frame)
        : this(owner, frame, CuiPropertyPresentation.For(owner), null)
    {
    }

    internal CuiHoverFeedback(Control owner, CuiMaterialBorder frame, CuiEffectivePropertyStore store, string? requiredClass)
    {
        _owner = owner; _frame = frame; _store = store; _requiredClass = requiredClass;
        frame.AttachedToVisualTree += FrameAttached;
        frame.DetachedFromVisualTree += FrameDetached;
        if (_requiredClass is not null)
        {
            owner.Classes.CollectionChanged += ClassesChanged;
            UpdateOptIn();
        }
    }

    private void FrameAttached(object? sender, VisualTreeAttachmentEventArgs e) => UpdateOptIn();
    private void FrameDetached(object? sender, VisualTreeAttachmentEventArgs e) => Detach();
    private void ClassesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateOptIn();

    private void UpdateOptIn()
    {
        if (_requiredClass is null) { if (_frame.IsAttachedToVisualTree()) Attach(); return; }
        if (_owner.Classes.Contains(_requiredClass))
        {
            if (_frame.IsAttachedToVisualTree()) Attach();
        }
        else Detach();
    }

    private void Attach()
    {
        if (_active) return;
        _active = true;
        _owner.PropertyChanged += OwnerChanged;
        if (_owner is Thumb thumb)
        {
            thumb.DragStarted += DragStarted;
            thumb.DragCompleted += DragCompleted;
        }
        if (_owner is MenuItem menuItem) menuItem.Classes.CollectionChanged += MenuItemClassesChanged;
        _store.Changed += EffectiveChanged;
        _subscriptions.Add(_owner.GetResourceObservable("CuiHighContrast").Subscribe(new Observer(_ => Refresh())));
        _subscriptions.Add(_owner.GetResourceObservable("CuiPanelBrush").Subscribe(new Observer(value =>
        {
            var brush = value as IBrush;
            if (ReferenceEquals(brush, _observedPanel)) { Refresh(); return; }
            _panelBrushSubscription?.Dispose(); _observedPanel = brush;
            _panelBrushSubscription = WatchMutableBrush(brush, Refresh);
            Refresh();
        })));
        ObserveBackgroundBrush();
        Refresh();
    }

    private void Detach()
    {
        if (!_active) return;
        _active = false;
        _owner.PropertyChanged -= OwnerChanged;
        if (_owner is Thumb thumb)
        {
            thumb.DragStarted -= DragStarted;
            thumb.DragCompleted -= DragCompleted;
        }
        if (_owner is MenuItem menuItem) menuItem.Classes.CollectionChanged -= MenuItemClassesChanged;
        _dragging = false;
        _store.Changed -= EffectiveChanged;
        _backgroundBrushSubscription?.Dispose(); _backgroundBrushSubscription = null; _observedBackground = null;
        _panelBrushSubscription?.Dispose(); _panelBrushSubscription = null; _observedPanel = null;
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
        _frame.SetHoverTint(null);
    }

    public void Dispose()
    {
        Detach();
        _frame.AttachedToVisualTree -= FrameAttached;
        _frame.DetachedFromVisualTree -= FrameDetached;
        if (_requiredClass is not null) _owner.Classes.CollectionChanged -= ClassesChanged;
    }

    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property.Name is "IsPointerOver" or "IsPressed" or "IsEnabled" or "IsEffectivelyEnabled") Refresh();
    }

    private void DragStarted(object? sender, VectorEventArgs e) { _dragging = true; Refresh(); }
    private void DragCompleted(object? sender, VectorEventArgs e) { _dragging = false; Refresh(); }
    private void MenuItemClassesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void EffectiveChanged(object? sender, CuiEffectiveProperty property)
    {
        if (property.Name == "Background") { ObserveBackgroundBrush(); Refresh(); }
        else if (property.Name == "Chromeless") Refresh();
    }

    private void ObserveBackgroundBrush()
    {
        var brush = _store.Contains("Background") ? _store.Resolve("Background").Value as IBrush : null;
        if (ReferenceEquals(brush, _observedBackground)) return;
        _backgroundBrushSubscription?.Dispose(); _observedBackground = brush;
        _backgroundBrushSubscription = WatchMutableBrush(brush, Refresh);
    }

    private static IDisposable? WatchMutableBrush(IBrush? brush, Action changed)
    {
        var subscriptions = new List<Action>();
        if (brush is AvaloniaObject mutable)
        {
            EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, _) => changed();
            mutable.PropertyChanged += handler;
            subscriptions.Add(() => mutable.PropertyChanged -= handler);
        }
        if (brush is IGradientBrush gradient)
        {
            var stopSubscriptions = new List<Action>();
            void RefreshStops()
            {
                foreach (var dispose in stopSubscriptions) dispose();
                stopSubscriptions.Clear();
                foreach (var stop in gradient.GradientStops)
                {
                    if (stop is not AvaloniaObject mutableStop) continue;
                    EventHandler<AvaloniaPropertyChangedEventArgs> stopChanged = (_, _) => changed();
                    mutableStop.PropertyChanged += stopChanged;
                    stopSubscriptions.Add(() => mutableStop.PropertyChanged -= stopChanged);
                }
            }
            RefreshStops();
            if (gradient.GradientStops is INotifyCollectionChanged stops)
            {
                NotifyCollectionChangedEventHandler handler = (_, _) => { RefreshStops(); changed(); };
                stops.CollectionChanged += handler;
                subscriptions.Add(() => stops.CollectionChanged -= handler);
            }
            subscriptions.Add(() =>
            {
                foreach (var dispose in stopSubscriptions) dispose();
                stopSubscriptions.Clear();
            });
        }
        return subscriptions.Count == 0 ? null : new CallbackDisposable(() =>
        { foreach (var dispose in subscriptions) dispose(); });
    }

    private void Refresh()
    {
        var chromeless = _store.Contains("Chromeless") && _store.Resolve("Chromeless").Value is true;
        var highContrast = Resource("CuiHighContrast") is true;
        var optedIn = _requiredClass is null || _owner.Classes.Contains(_requiredClass);
        var selectedMenuItem = _owner is MenuItem { IsSelected: true };
        var active = optedIn && _owner.IsEffectivelyEnabled && (_owner.IsPointerOver || selectedMenuItem)
            && _owner is not Button { IsPressed: true }
            && !(_owner is MenuItem && _owner.Classes.Contains(":pressed"))
            && !_dragging
            && !CuiPropertyPresentation.IsEmbeddedInput(_owner)
            && !chromeless;
        var background = _store.Contains("Background") ? _store.Resolve("Background").Value as IBrush : null;
        var tint = DeriveTint(background, highContrast);
        if (active && tint is null)
            tint = DeriveTint(Resource("CuiPanelBrush") as IBrush, highContrast);
        // Authored opt-out: CuiHoverTintPolicy is what lets a surface say "no hover fill on this control"
        // (HoverTint="None") without dropping its rest chrome the way Chromeless would. An Outline mode control
        // gets an accent border instead of a wash. Both default to today's behaviour when nothing is authored.
        var paintsFill = active && CuiHoverTintPolicy.PaintsFill(_owner);
        _frame.SetHoverTint(paintsFill ? tint : null);
        if (CuiHoverTintPolicy.WantsOutline(_owner))
        {
            if (active && Resource("CuiAccentBrush") is IBrush accentBrush)
            {
                _frame.BorderBrush = accentBrush;
                _frame.BorderThickness = new Thickness(1.5);
            }
            else
            {
                _frame.BorderThickness = new Thickness(0);
            }
        }
    }

    private object? Resource(string key) => _owner.TryFindResource(key, out var value) ? value : null;

    private static Color? DeriveTint(IBrush? brush, bool highContrast)
    {
        var colour = Representative(brush);
        if (colour is null || colour.Value.A == 0) return null;
        var value = colour.Value;
        var luminance = .2126 * Linear(value.R / 255d) + .7152 * Linear(value.G / 255d) + .0722 * Linear(value.B / 255d);
        var tint = luminance > .5 ? Colors.Black : Colors.White;
        // This low-opacity neutral wash preserves the source hue while changing lightness.
        var alpha = (byte)Math.Clamp(Math.Round(value.A * (highContrast ? .14 : .08)), 0, highContrast ? 36 : 20);
        return Color.FromArgb(alpha, tint.R, tint.G, tint.B);
    }

    private static double Linear(double channel) => channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);

    private static Color? Representative(IBrush? brush)
    {
        if (brush is CuiMaterialBrush material) return ScaleAlpha(material.Tint, material.TintOpacity);
        if (brush is CuiCustomMaterialBrush custom) return CustomRepresentative(custom.Fill);
        if (brush is ISolidColorBrush solid) return ScaleAlpha(solid.Color, solid.Opacity);
        if (brush is IGradientBrush gradient)
        {
            var average = Average(gradient.GradientStops.Select(stop => stop.Color));
            return average is { } colour ? ScaleAlpha(colour, gradient.Opacity) : null;
        }
        return null;
    }

    private static Color ScaleAlpha(Color colour, double opacity) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(colour.A * Math.Clamp(opacity, 0, 1)), 0, 255), colour.R, colour.G, colour.B);

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
            (byte)Math.Round(values.Average(colour => (double)colour.B)));
    }

    private sealed class Observer(Action<object?> next) : IObserver<object?>
    {
        public void OnNext(object? value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
    }

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
