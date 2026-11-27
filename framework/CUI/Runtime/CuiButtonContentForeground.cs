using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>A compound button's ordinary authored label follows its actual ink.
/// This is a theme-level default, so authored label colours retain precedence.
/// Only this subscription is owned here; no button or text lifetime is transferred.</summary>
internal sealed class CuiButtonContentForeground : IDisposable
{
    private readonly TextBlock _text;
    private readonly Action<IBrush> _apply;
    private readonly Action _clear;
    private Button? _button;
    private bool _disposed;

    internal CuiButtonContentForeground(TextBlock text, Action<IBrush> apply, Action clear)
    {
        _text = text; _apply = apply; _clear = clear;
        text.AttachedToVisualTree += Attached;
        text.DetachedFromVisualTree += Detached;
        if (text.IsAttachedToVisualTree()) Bind();
    }

    private void Attached(object? sender, VisualTreeAttachmentEventArgs args) => Bind();
    private void Detached(object? sender, VisualTreeAttachmentEventArgs args) => Unbind();
    private void Bind()
    {
        if (_disposed) return;
        Unbind();
        _button = _text.GetVisualAncestors().OfType<Button>().FirstOrDefault();
        if (_button is null) return;
        _button.PropertyChanged += Changed;
        Apply();
    }
    private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property.Name == "Foreground") Apply();
    }
    private void Apply()
    {
        if (_button?.Foreground is { } brush) _apply(brush); else _clear();
    }
    private void Unbind()
    {
        if (_button is null) return;
        _button.PropertyChanged -= Changed;
        _button = null;
        _clear();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _text.AttachedToVisualTree -= Attached;
        _text.DetachedFromVisualTree -= Detached;
        Unbind();
    }
}
