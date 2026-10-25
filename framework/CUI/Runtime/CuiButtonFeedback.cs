using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
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
