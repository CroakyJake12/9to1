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
/// opaque semantic fallback brush resource. Destination alpha is modelled HONESTLY: until the owning
/// top-level positively reports a transparency level (via <see cref="TopLevel.ActualTransparencyLevelProperty"/>
/// or an explicit producer receipt), the state is UNKNOWN/unobserved and the fallback stays UNPAINTED.
/// When the destination negotiated real per-pixel alpha (<see cref="WindowTransparencyLevel.Transparent"/>),
/// the fallback border stays UNPAINTED (null) through later theme changes — an independent LocalValue
/// resource binding must not repaint it. When the destination is positively opaque
/// (<see cref="WindowTransparencyLevel.None"/>), it paints the LATEST guaranteed-opaque semantic color.
/// A default/unnegotiated value is never inferred as a positive observation of opacity. All subscriptions
/// are per-owner and disposed on detach/close.
/// </summary>
internal sealed class CuiTransparencyFallback : IDisposable
{
    private static readonly ConditionalWeakTable<PopupRoot, CuiTransparencyFallback> Instances = new();

    private readonly PopupRoot _owner;
    private readonly Border _fallback;
    private IBrush? _opaqueBrush;
    private WindowTransparencyLevel? _observedLevel;
    private IDisposable? _brushSubscription;
    private bool _disposed;

    private CuiTransparencyFallback(PopupRoot owner, Border fallback)
    {
        _owner = owner;
        _fallback = fallback;
        _brushSubscription = owner.GetResourceObservable("CuiPopupOpaqueFallbackBrush")
            .Subscribe(new BrushObserver(value => { _opaqueBrush = value as IBrush; Apply(); }));
        // Subscribe to the TopLevel transparency REPORT hook (not the ActualTransparencyLevel property
        // change), so every processed report is delivered — including unchanged None→None reports,
        // the OnApplyTemplate re-report, and retemplate. This settles receipt-before-template/retemplate
        // and rebinds per new destination because each PopupRoot owns its own projection.
        owner.TransparencyLevelReported += OnReported;
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

    /// <summary>Record a producer receipt of the destination's negotiated transparency level. This is
    /// the explicit observation channel; it is never inferred from a default or parent-window alpha.</summary>
    internal static void Observe(PopupRoot owner, WindowTransparencyLevel level)
    {
        if (Instances.TryGetValue(owner, out var projection)) projection.OnObserved(level);
    }

    private void OnObserved(WindowTransparencyLevel level)
    {
        _observedLevel = level;
        Apply();
    }

    private void OnReported(WindowTransparencyLevel level)
    {
        _observedLevel = level;
        Apply();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Dispose();

    private void Apply()
    {
        if (_disposed) return;
        // Only a positively observed opaque destination paints; unobserved/unknown and every
        // alpha-capable level keep the fallback null so a later theme change never repaints a
        // negotiated-through backdrop.
        _fallback.Background = _observedLevel == WindowTransparencyLevel.None ? _opaqueBrush : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.TransparencyLevelReported -= OnReported;
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
