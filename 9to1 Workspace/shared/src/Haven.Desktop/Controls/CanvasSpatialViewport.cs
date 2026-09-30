using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using CanvasSvgRasterizer = Haven.Desktop.Services.CanvasSvgRasterizer;
using HavenOS.Apps.Canvas;
using System.Runtime.InteropServices;

namespace Haven.Desktop.Controls;

/// <summary>A retained native viewport over the owning structured Canvas document.
/// The route owns the document lifetime; this control owns only its operation raster.</summary>
public sealed class CanvasSpatialViewport : UserControl, IDisposable
{
    private readonly CanvasRnoteDocument _document;
    private readonly ICuiSceneReadiness _readiness;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WriteableBitmap? _bitmap;
    private RnoteRenderFrame? _frame;
    private bool _disposed;

    public CanvasSpatialViewport(CanvasRnoteDocument document, ICuiSceneReadiness readiness)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        Content = new Grid { Children = { _image, _status } };
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(operation.Token);
        try
        {
            var before = await _readiness.CheckAsync(operation.Token);
            if (before.State != CuiSceneAvailabilityState.Ready) { Clear(before.Message); return false; }
            var snapshot = _document.Snapshot;
            var output = await Task.Run(() =>
            {
                var frame = _document.Render();
                if (!double.IsFinite(frame.X) || !double.IsFinite(frame.Y) || !double.IsFinite(frame.Width) || !double.IsFinite(frame.Height) || frame.Width <= 0 || frame.Height <= 0)
                    throw new InvalidDataException("Canvas returned invalid document bounds.");
                var scale = 1600 / Math.Max(frame.Width, frame.Height);
                var width = Math.Clamp((int)Math.Round(frame.Width * scale), 1, 1600);
                var height = Math.Clamp((int)Math.Round(frame.Height * scale), 1, 1600);
                return (Frame: frame, Raster: CanvasSvgRasterizer.Render(frame.Svg, width, height));
            }, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            var current = _document.Snapshot;
            var after = await _readiness.CheckAsync(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (after.State != CuiSceneAvailabilityState.Ready) { Clear(after.Message); return false; }
            if (current.ArtifactId != snapshot.ArtifactId || current.RevisionId != snapshot.RevisionId)
            {
                Clear("Canvas changed while rendering. Refresh the current document.");
                return false;
            }
            var raster = output.Raster;
            var bitmap = new WriteableBitmap(new(raster.Width, raster.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            try
            {
                using (var buffer = bitmap.Lock())
                    for (var row = 0; row < raster.Height; row++)
                        Marshal.Copy(raster.PremultipliedBgra, row * raster.Stride,
                            IntPtr.Add(buffer.Address, row * buffer.RowBytes), raster.Width * 4);
                var old = _bitmap;
                _bitmap = bitmap;
                _frame = output.Frame;
                _image.Source = bitmap;
                _status.IsVisible = false;
                old?.Dispose();
                return true;
            }
            catch { bitmap.Dispose(); throw; }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            Clear(error.Message);
            return false;
        }
        finally { _gate.Release(); }
    }

    public Point? ToDocumentPoint(Point viewportPosition)
    {
        if (_disposed || _bitmap is null || _frame is null || Bounds.Width <= 0 || Bounds.Height <= 0) return null;
        var imageScale = Math.Min(Bounds.Width / _bitmap.PixelSize.Width, Bounds.Height / _bitmap.PixelSize.Height);
        var width = _bitmap.PixelSize.Width * imageScale;
        var height = _bitmap.PixelSize.Height * imageScale;
        var left = (Bounds.Width - width) / 2;
        var top = (Bounds.Height - height) / 2;
        if (viewportPosition.X < left || viewportPosition.X > left + width || viewportPosition.Y < top || viewportPosition.Y > top + height) return null;
        return new(_frame.X + (viewportPosition.X - left) / width * _frame.Width,
            _frame.Y + (viewportPosition.Y - top) / height * _frame.Height);
    }

    private void Clear(string message)
    {
        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _frame = null;
        _status.Text = message;
        _status.IsVisible = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Clear("Canvas viewport closed.");
        _lifetime.Dispose();
    }
}
