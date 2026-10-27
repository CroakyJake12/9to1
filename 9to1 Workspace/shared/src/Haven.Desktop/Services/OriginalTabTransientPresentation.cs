using Avalonia.Controls;
using Avalonia.Threading;

namespace Haven.Desktop.Services;

internal interface IOriginalTabTransientPresentationOwner
{
    OriginalTabTransientPresentation TransientPresentation { get; }
}

/// <summary>Stable tab view; the original app owns its lifetime and close preparation.
/// This contains presentation only and supplies no document, Files or profile authority.</summary>
internal sealed class OriginalTabTransientPresentation : Grid
{
    private readonly object _gate = new();
    private readonly Border _originalLayer = new();
    private readonly Border _transientLayer = new() { IsVisible = false };
    private Control? _original;
    internal Control? VisibleContent => _pending is null ? _original : _transientLayer.Child;
    internal Control? OriginalContent => _original;
    internal OriginalTabTransientPresentation()
    { Children.Add(_originalLayer); Children.Add(_transientLayer); }
    private CancellationTokenSource? _pending;
    private bool _closeReserved;
    internal IDisposable? TryReserveClose()
    {
        lock (_gate)
        {
            if (_pending is not null || _closeReserved) return null;
            _closeReserved = true;
            return new CloseReservation(this);
        }
    }
    private sealed class CloseReservation(OriginalTabTransientPresentation owner) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_released) return;
                _released = true; owner._closeReserved = false;
            }
        }
    }
    internal Control BindOriginal(Control original)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_original is not null && !ReferenceEquals(_original, original))
            throw new InvalidOperationException("The original tab presentation changed.");
        if (_original is null) { _original = original; _originalLayer.Child = original; }
        return this;
    }
    internal void CancelPending() { lock (_gate) _pending?.Cancel(); }
    internal async Task<T> PresentAsync<T>(Control page, Func<CancellationToken, Task<T>> operation,
        CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_gate)
        {
            if (_original is null || _pending is not null || _closeReserved || page.Parent is not null)
                throw new InvalidOperationException("The original tab is not available for a transient presentation.");
            _pending = pending;
        }
        _transientLayer.Child = page;
        _originalLayer.IsVisible = false;
        _originalLayer.IsHitTestVisible = false;
        _originalLayer.IsEnabled = false;
        _transientLayer.IsVisible = true;
        try { return await operation(pending.Token); }
        finally
        {
            // The continuation runs on the UI thread captured on admission.
            lock (_gate) { pending.Cancel(); _pending = null; }
            _transientLayer.IsVisible = false;
            _transientLayer.Child = null;
            _originalLayer.IsVisible = true;
            _originalLayer.IsHitTestVisible = true;
            _originalLayer.IsEnabled = true;
        }
    }
}
