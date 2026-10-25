using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>A one-way navigation request against exact UTF-8 text content. Offsets use UTF-16,
/// matching native text input and language-server coordinates. Reusing the same immutable request
/// preserves subsequent user selection; a new request can navigate to the same location again.</summary>
public sealed record CuiTextSelection(long RequestId, int Start, int Length, string TextSha256, bool Focus = true)
{
    public static CuiTextSelection ForText(long requestId, string text, int start, int length, bool focus = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (requestId < 1) throw new ArgumentOutOfRangeException(nameof(requestId));
        if (start < 0 || length < 0 || (long)start + length > text.Length)
            throw new ArgumentOutOfRangeException(nameof(start), "The complete selection must lie within this text.");
        return new(requestId, start, length, Hash(text), focus);
    }

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>Renderer-only deferred selection. No source IO, parsing, editor model or permission
/// is created here. A withdrawn, detached or superseded request never moves a later editor.</summary>
internal sealed class CuiTextSelectionRequest : IDisposable
{
    private readonly TextBox _input;
    private readonly Func<bool> _isCurrent;
    private CuiTextSelection? _request;
    private long _generation;
    private bool _pending, _disposed;

    public CuiTextSelectionRequest(TextBox input, Func<bool> isCurrent)
    {
        _input = input; _isCurrent = isCurrent;
        input.AttachedToVisualTree += Attached;
        input.DetachedFromVisualTree += Detached;
    }

    public void Set(CuiTextSelection? request)
    {
        if (_disposed || Equals(_request, request)) return;
        _request = request; _generation++; _pending = request is not null;
        Schedule();
    }

    private void Attached(object? sender, VisualTreeAttachmentEventArgs args) => Schedule();
    private void Detached(object? sender, VisualTreeAttachmentEventArgs args)
    { _generation++; _pending = false; }

    private void Schedule()
    {
        if (_disposed || !_pending || TopLevel.GetTopLevel(_input) is not { } root) return;
        var generation = _generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_pending || generation != _generation || !_isCurrent() || _request is not { } request) return;
            _pending = false;
            if (!ReferenceEquals(root, TopLevel.GetTopLevel(_input)) || !_input.IsEffectivelyVisible || !_input.IsEffectivelyEnabled ||
                _input.Bounds.Width <= 0 || _input.Bounds.Height <= 0 ||
                _input.GetVisualAncestors().Prepend(_input).Any(visual => visual.Opacity <= 0)) return;
            var text = _input.Text ?? "";
            if ((long)request.Start + request.Length > text.Length ||
                !StringComparer.OrdinalIgnoreCase.Equals(request.TextSha256, CuiTextSelection.Hash(text))) return;
            if (request.Focus && (!_input.Focusable || !_input.Focus(NavigationMethod.Unspecified))) return;
            // Focus callbacks can synchronously withdraw the view or replace its text.
            if (_disposed || generation != _generation || !_isCurrent() ||
                !ReferenceEquals(root, TopLevel.GetTopLevel(_input)) || !StringComparer.Ordinal.Equals(text, _input.Text ?? "")) return;
            var end = request.Start + request.Length;
            _input.CaretIndex = end;
            _input.SelectionStart = request.Start;
            _input.SelectionEnd = end;
        }, DispatcherPriority.Loaded);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _generation++; _pending = false;
        _input.AttachedToVisualTree -= Attached;
        _input.DetachedFromVisualTree -= Detached;
    }
}
