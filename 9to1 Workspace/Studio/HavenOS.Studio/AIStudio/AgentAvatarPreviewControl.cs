using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace HavenOS.AIStudio;

public sealed class AgentAvatarPreviewControl : Image, IDisposable, IAsyncDisposable
{
    private readonly AgentAvatarPreview _preview;
    private readonly StudioOriginalCallbackLifetime _callbacks = new();
    private WriteableBitmap? _bitmap;
    private bool _disposed, _advancing;
    private readonly DispatcherTimer _animation = new();
    private Task? _close;

    public AgentAvatarPreviewControl(AgentAvatarPreview preview) : this(preview, null) { }

    internal AgentAvatarPreviewControl(AgentAvatarPreview preview,
        Action<AgentAvatarPreviewControl>? originalAcquired)
    {
        ArgumentNullException.ThrowIfNull(preview);
        _preview = preview;
        // The native owning renderer captures this SAME acquired control before notifications.
        originalAcquired?.Invoke(this);
        RequireLive(CancellationToken.None);
        Stretch = Stretch.Uniform;
        RequireLive(CancellationToken.None);
        Width = 160;
        RequireLive(CancellationToken.None);
        Height = 160;
        RequireLive(CancellationToken.None);
        _preview.Changed += OnChanged;
        _animation.Tick += OnAnimationTick;
        DetachedFromVisualTree += OnDetachedVisual;
        DetachedFromLogicalTree += OnDetachedLogical;
        Refresh(CancellationToken.None);
    }

    public Task? OriginalCloseTask => _close;
    public bool OriginalCallbacksSettled => _callbacks.OriginalsCapturedAndSettled;
    private void OnDetachedVisual(object? sender, VisualTreeAttachmentEventArgs args) => Dispose();
    private void OnDetachedLogical(object? sender, LogicalTreeAttachmentEventArgs args) => Dispose();

    private void OnChanged(object? sender, EventArgs args)
    {
        if (_disposed || _callbacks.IsClosing) return;
        _callbacks.Run(async token =>
        {
            await Dispatcher.UIThread.InvokeAsync(() => Refresh(token));
        });
    }

    private void RequireLive(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed || _callbacks.IsClosing, this);
    }

    private void Refresh(CancellationToken token)
    {
        if (_disposed || _callbacks.IsClosing || token.IsCancellationRequested) return;
        _animation.Stop();
        var previous = _bitmap; _bitmap = null;
        List<Exception> errors = [];
        try { Source = null; } catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        try { previous?.Dispose(); } catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        StudioOriginalCallbackLifetime.Throw(errors);
        if (_disposed || _callbacks.IsClosing || token.IsCancellationRequested) return;
        AutomationProperties.SetName(this, _preview.AccessibleName);
        if (_disposed || _callbacks.IsClosing || token.IsCancellationRequested) return;
        if (_preview.Frame is not { } frame) return;
        var pixels = frame.CopyPixels();
        WriteableBitmap? acquired = null;
        try
        {
            acquired = new WriteableBitmap(new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var target = acquired.Lock())
                for (var row = 0; row < frame.Height; row++)
                    Marshal.Copy(pixels, row * frame.Stride, IntPtr.Add(target.Address, row * target.RowBytes), frame.Width * 4);
            RequireLive(token);
            _bitmap = acquired; acquired = null;
            Source = _bitmap;
            // Do not bypass independent cleanup failures after a notifying setter retires us.
        }
        catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        finally
        {
            try { Array.Clear(pixels); } catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
            try { acquired?.Dispose(); } catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        }
        StudioOriginalCallbackLifetime.Throw(errors);
        ScheduleNextFrame();
    }

    private void ScheduleNextFrame()
    {
        _animation.Stop();
        if (_disposed || _callbacks.IsClosing || _advancing || !_preview.IsAnimating || _preview.DelayMicroseconds <= 0) return;
        if (_preview.DelayMicroseconds > TimeSpan.MaxValue.Ticks / 10)
        {
            _preview.Clear();
            return;
        }
        _animation.Interval = TimeSpan.FromTicks(_preview.DelayMicroseconds * 10);
        if (_disposed || _callbacks.IsClosing) return;
        _animation.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs args)
    {
        _animation.Stop();
        if (_disposed || _callbacks.IsClosing || _advancing) return;
        _callbacks.Run(AdvanceOriginalAsync);
    }

    private async Task AdvanceOriginalAsync(CancellationToken token)
    {
        RequireLive(token);
        if (_advancing) return;
        _advancing = true;
        List<Exception> errors = [];
        try { await _preview.AdvanceAsync(token); }
        catch (Exception error)
        {
            StudioOriginalCallbackLifetime.Add(errors, error);
            if (error is not OperationCanceledException)
            {
                try { _preview.Clear(); } catch (Exception cleanup) { StudioOriginalCallbackLifetime.Add(errors, cleanup); }
                try
                {
                    if (!_disposed && !_callbacks.IsClosing)
                        AutomationProperties.SetName(this, "Avatar preview unavailable.");
                }
                catch (Exception cleanup) { StudioOriginalCallbackLifetime.Add(errors, cleanup); }
            }
        }
        finally
        {
            _advancing = false;
            try { if (!_disposed && !_callbacks.IsClosing) ScheduleNextFrame(); }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        }
        StudioOriginalCallbackLifetime.Throw(errors);
    }

    /// <summary>Request-only synchronous disposal. The native owner must join OriginalCloseTask
    /// while its dispatcher is alive before releasing preview, Den or provider resources.</summary>
    public void Dispose() { _ = CloseAndDrainAsync(); }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    public Task CloseAndDrainAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_close is not null) return _close;
        _disposed = true;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _close = CloseOriginalAsync(start.Task);
        start.SetResult();
        return _close;
    }

    private async Task CloseOriginalAsync(Task start)
    {
        await start;
        List<Exception> errors = [];
        Task? originalCallbacks = null;
        try { originalCallbacks = _callbacks.CloseAndDrainAsync(); }
        catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        Attempt(() => _preview.Changed -= OnChanged);
        Attempt(_animation.Stop);
        Attempt(() => _animation.Tick -= OnAnimationTick);
        Attempt(() => DetachedFromVisualTree -= OnDetachedVisual);
        Attempt(() => DetachedFromLogicalTree -= OnDetachedLogical);
        // Reset requests actual decoder retirement before held Advance finally is joined.
        Attempt(_preview.Clear);
        if (originalCallbacks is not null)
            try { await originalCallbacks; }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        if (originalCallbacks is null || !_callbacks.OriginalsCapturedAndSettled)
            StudioOriginalCallbackLifetime.Add(errors, new InvalidOperationException("Actual preview callback settlement is missing."));
        Attempt(() => Source = null);
        var acquired = _bitmap; _bitmap = null;
        Attempt(() => acquired?.Dispose());
        StudioOriginalCallbackLifetime.Throw(errors);

        void Attempt(Action action)
        {
            try { action(); } catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        }
    }
}
