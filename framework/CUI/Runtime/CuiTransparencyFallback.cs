using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Alpha-aware, lifetime-owned projection that drives the CUI popup-root transparency fallback.
/// It observes BOTH the actual transparency level of the owning <see cref="PopupRoot"/> AND the live
/// opaque semantic fallback brush resource. When the destination negotiated real per-pixel alpha
/// (<see cref="WindowTransparencyLevel.Transparent"/>), the fallback border stays UNPAINTED (null) through
/// later theme changes — an independent LocalValue resource binding must not repaint it. When the
/// destination is positively opaque (<see cref="WindowTransparencyLevel.None"/>), it paints the LATEST
/// guaranteed-opaque semantic color. Unknown/unnegotiated levels never infer opacity from a default
/// or from a parent window's alpha. All subscriptions are per-owner and disposed on detach/close.
/// </summary>
internal sealed class CuiTransparencyFallback : IDisposable
{
    private static readonly ConditionalWeakTable<PopupRoot, CuiTransparencyFallback> Instances = new();

    private readonly PopupRoot _owner;
    private readonly Border _fallback;
    private IBrush? _opaqueBrush;
    private IDisposable? _brushSubscription;
    private bool _disposed;

    private CuiTransparencyFallback(PopupRoot owner, Border fallback)
    {
        _owner = owner;
        _fallback = fallback;
        _brushSubscription = owner.GetResourceObservable("CuiPopupOpaqueFallbackBrush")
            .Subscribe(new BrushObserver(value => { _opaqueBrush = value as IBrush; Apply(); }));
        owner.PropertyChanged += OnPropertyChanged;
        owner.DetachedFromVisualTree += OnDetached;
        Apply();
    }

    /// <summary>Attaches the projection to a CUI popup-root template instance. Exactly one
    /// projection is owned per PopupRoot; replacing an existing instance detaches the prior one.</summary>
    public static void Attach(PopupRoot owner, Border fallback)
    {
        if (Instances.TryGetValue(owner, out var existing)) existing.Dispose();
        Instances.Add(owner, new CuiTransparencyFallback(owner, fallback));
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == TopLevel.ActualTransparencyLevelProperty) Apply();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Dispose();

    private void Apply()
    {
        if (_disposed) return;
        var level = _owner.ActualTransparencyLevel;
        // Only a definitively opaque destination paints; Transparent (and any unknown level) keeps
        // the fallback null so a later theme change can never repaint a negotiated-through backdrop.
        _fallback.Background = CuiPopupPresentation.ResolveOpaquePaint(level, _opaqueBrush);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.PropertyChanged -= OnPropertyChanged;
        _owner.DetachedFromVisualTree -= OnDetached;
        _brushSubscription?.Dispose();
        _brushSubscription = null;
        Instances.Remove(_owner);
    }

    private sealed class BrushObserver(Action<object?> next) : IObserver<object?>
    {
        private readonly Action<object?> _next = next;
        public void OnNext(object? value) => _next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
    }
}
