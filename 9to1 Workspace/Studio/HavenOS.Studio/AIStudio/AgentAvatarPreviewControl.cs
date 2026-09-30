using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace HavenOS.AIStudio;

public sealed class AgentAvatarPreviewControl : Image, IDisposable
{
    private readonly AgentAvatarPreview _preview;
    private WriteableBitmap? _bitmap;
    private bool _disposed;
    private bool _advancing;
    private readonly DispatcherTimer _animation = new();
    private readonly CancellationTokenSource _lifetime = new();
    public AgentAvatarPreviewControl(AgentAvatarPreview preview)
    {
        _preview = preview; Stretch = Stretch.Uniform; Width = 160; Height = 160;
        _preview.Changed += OnChanged;
        _animation.Tick += OnAnimationTick;
        DetachedFromVisualTree += (_, _) => Dispose();
        DetachedFromLogicalTree += (_, _) => Dispose();
        Refresh();
    }
    private void OnChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(Refresh);
    private void Refresh()
    {
        if (_disposed) return;
        _animation.Stop();
        Source = null; _bitmap?.Dispose(); _bitmap = null;
        AutomationProperties.SetName(this, _preview.AccessibleName);
        if (_preview.Frame is not { } frame) return;
        var pixels = frame.CopyPixels();
        try
        {
            var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96,96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            try
            {
                using var target = bitmap.Lock();
                for (var row = 0; row < frame.Height; row++)
                    Marshal.Copy(pixels, row * frame.Stride, IntPtr.Add(target.Address, row * target.RowBytes), frame.Width * 4);
                _bitmap = bitmap; Source = bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
        finally { Array.Clear(pixels); }
        ScheduleNextFrame();
    }
    private void ScheduleNextFrame()
    {
        _animation.Stop();
        if (_disposed || _advancing || !_preview.IsAnimating || _preview.DelayMicroseconds <= 0) return;
        if (_preview.DelayMicroseconds > TimeSpan.MaxValue.Ticks / 10)
        {
            _preview.Clear();
            return;
        }
        _animation.Interval = TimeSpan.FromTicks(_preview.DelayMicroseconds * 10);
        _animation.Start();
    }
    private async void OnAnimationTick(object? sender, EventArgs args)
    {
        _animation.Stop();
        if (_disposed || _advancing) return;
        _advancing = true;
        try { await _preview.AdvanceAsync(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            _preview.Clear();
            if (!_disposed) AutomationProperties.SetName(this, "Avatar preview unavailable.");
        }
        finally { _advancing = false; if (!_disposed) ScheduleNextFrame(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _preview.Changed -= OnChanged;
        _animation.Stop(); _animation.Tick -= OnAnimationTick;
        _lifetime.Cancel();
        _preview.Clear();
        _lifetime.Dispose();
        Source = null; _bitmap?.Dispose(); _bitmap = null;
    }
}
