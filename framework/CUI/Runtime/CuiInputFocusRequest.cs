using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>One deferred focus operation per authored false-to-true request.</summary>
internal sealed class CuiInputFocusRequest : IDisposable
{
    private readonly Control _control;
    private readonly Func<bool> _selectText;
    private long _generation;
    private bool _requested, _pending, _disposed;

    public CuiInputFocusRequest(Control control, Func<bool> selectText)
    {
        _control = control; _selectText = selectText;
        control.AttachedToVisualTree += Attached;
        control.DetachedFromVisualTree += Detached;
    }

    public void SetRequested(bool requested)
    {
        if (_disposed || requested == _requested) return;
        _requested = requested; _generation++; _pending = requested;
        if (requested) Schedule();
    }

    private void Attached(object? sender, VisualTreeAttachmentEventArgs args) => Schedule();
    private void Detached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        // Returning to a retained view must not replay a request from its old visit.
        _generation++; _pending = false;
    }

    private void Schedule()
    {
        if (_disposed || !_pending || TopLevel.GetTopLevel(_control) is not { } root) return;
        var generation = _generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_pending || !_requested || generation != _generation) return;
            _pending = false;
            if (!ReferenceEquals(root, TopLevel.GetTopLevel(_control)) || !_control.Focusable
                || !_control.IsEffectivelyEnabled || !_control.IsEffectivelyVisible
                || _control.Bounds.Width <= 0 || _control.Bounds.Height <= 0
                || _control.GetVisualAncestors().Prepend(_control).Any(visual => visual.Opacity <= 0)) return;
            if (_control.Focus(NavigationMethod.Unspecified) && !_disposed && _requested
                && generation == _generation && _selectText() && _control is TextBox text && text.IsFocused)
                text.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _generation++; _pending = false;
        _control.AttachedToVisualTree -= Attached;
        _control.DetachedFromVisualTree -= Detached;
    }
}
