using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui;

namespace NineToOne.Os.Shell;

/// <summary>Owning taskbar input; commands use the same Home preview transaction as visible buttons.</summary>
public sealed class TaskbarLayerSurface(ICuiActionDispatcher actions) : Border
{
    private CancellationTokenSource? _inputLifetime = new();
    private bool _dispatching;
    private double _wheel;

    public static CuiControlRegistry CreateRegistry(ICuiActionDispatcher actions)
    {
        var registry = new CuiControlRegistry();
        registry.RegisterControlType("TaskbarLayer", _ => new TaskbarLayerSurface(actions));
        return registry;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _inputLifetime ??= new();
        base.OnAttachedToVisualTree(e);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        var lifetime = _inputLifetime; _inputLifetime = null;
        lifetime?.Cancel(); lifetime?.Dispose(); _wheel = 0;
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!e.Handled && e.Delta.Y != 0 && e.KeyModifiers == KeyModifiers.None)
        {
            // Accumulate small touchpad deltas, but one event always selects one adjacent layer.
            if (Math.Sign(_wheel) != Math.Sign(e.Delta.Y)) _wheel = 0;
            _wheel += e.Delta.Y; e.Handled = true;
            if (Math.Abs(_wheel) >= 1)
            {
                var command = _wheel > 0 ? "PreviousLayer" : "NextLayer";
                _wheel = 0; _ = DispatchAsync(command);
            }
        }
        base.OnPointerWheelChanged(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && e.KeyModifiers == KeyModifiers.Control && e.Key is Key.PageUp or Key.PageDown)
        {
            e.Handled = true;
            _ = DispatchAsync(e.Key == Key.PageUp ? "PreviousLayer" : "NextLayer");
        }
        base.OnKeyDown(e);
    }
    private async Task DispatchAsync(string command)
    {
        if (_dispatching || _inputLifetime is not { } lifetime) return;
        _dispatching = true;
        try { await actions.DispatchAsync(command, null, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { _dispatching = false; }
    }
}
