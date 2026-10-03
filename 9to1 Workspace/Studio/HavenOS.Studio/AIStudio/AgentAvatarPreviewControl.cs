using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace HavenOS.AIStudio;

/// <summary>The actual native timer callback is retained before its first callback.
/// Synchronous tree detach requests retirement; the owning Window awaits the SAME
/// retirement Task before disposing its preview and selected Den.</summary>
public sealed class AgentAvatarPreviewControl : Image, IDisposable, IAsyncDisposable
{
    private readonly AgentAvatarPreview _preview;
    private WriteableBitmap? _bitmap;
    private bool _disposed;
    private bool _initializing = true;
    private bool _advancing;
    private readonly DispatcherTimer _animation = new();
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _ownedToken;
    private sealed class Original
    {
        public Task Task = Task.CompletedTask;
        public bool ObservedOwnedCancellation;
        public Exception? Failure;
    }
    private Original _original = new();
    private readonly List<Original> _originals = [];
    private Task? _retirement;

    public AgentAvatarPreviewControl(AgentAvatarPreview preview) : this(preview, false) { }
    private AgentAvatarPreviewControl(AgentAvatarPreview preview, bool defer)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _lifetime = new();
        _ownedToken = _lifetime.Token;
        try
        {
            Dispatcher.UIThread.VerifyAccess();
            Stretch = Stretch.Uniform; Width = 160; Height = 160;
            if (!defer) Initialize();
        }
        catch (Exception primary) { FailUnstarted(primary); throw; }
    }

    // The real production renderer registers the exact control before subscribing
    // or allocating its native bitmap. List.Add invokes no arbitrary user callback.
    public static AgentAvatarPreviewControl CreateOwned(AgentAvatarPreview preview,
        List<AgentAvatarPreviewControl>? ownedControls)
    {
        Dispatcher.UIThread.VerifyAccess();
        var control = new AgentAvatarPreviewControl(preview, true);
        try { ownedControls?.Add(control); control.Initialize(); return control; }
        catch (Exception primary) { control.FailUnstarted(primary); throw; }
    }

    private void Initialize()
    {
        _preview.Changed += OnChanged;
        _animation.Tick += OnAnimationTick;
        DetachedFromVisualTree += OnDetachedVisual;
        DetachedFromLogicalTree += OnDetachedLogical;
        RefreshCore();
        _initializing = false;
        ScheduleNextFrame();
    }

    private void FailUnstarted(Exception primary)
    {
        // No timer callback is admitted while construction/RefreshCore is in progress.
        _disposed = true;
        List<Exception> failures = [primary];
        void Attempt(Action action) { try { action(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); } }
        Attempt(() => _preview.Changed -= OnChanged);
        Attempt(() => _animation.Stop());
        Attempt(() => _animation.Tick -= OnAnimationTick);
        Attempt(() => DetachedFromVisualTree -= OnDetachedVisual);
        Attempt(() => DetachedFromLogicalTree -= OnDetachedLogical);
        Attempt(() => _lifetime.Cancel());
        Attempt(() => _preview.Clear());
        var bitmap = _bitmap; _bitmap = null;
        Attempt(() => Source = null);
        Attempt(() => bitmap?.Dispose());
        Attempt(() => _lifetime.Dispose());
        var retained = failures.Count == 1 ? primary : new AggregateException(failures);
        _retirement = Task.FromException(retained);
        _ = _retirement.Exception; // Constructor failures are also synchronously returned.
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(retained).Throw();
    }

    private void OnDetachedVisual(object? sender, VisualTreeAttachmentEventArgs args) => Dispose();
    private void OnDetachedLogical(object? sender, Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs args) => Dispose();
    private void OnChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(Refresh);
    private void Refresh()
    {
        if (_disposed || _initializing) return;
        RefreshCore();
    }
    private void RefreshCore()
    {
        if (_disposed) return;
        _animation.Stop();
        // Retain the SAME old bitmap before any native notification can close.
        var previous = _bitmap; _bitmap = null;
        List<Exception> failures = [];
        try { Source = null; } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        try { previous?.Dispose(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        StudioOriginalTaskDrain.Throw(failures);
        if (_disposed) return;
        AutomationProperties.SetName(this, _preview.AccessibleName);
        if (_disposed || _preview.Frame is not { } frame) return;
        var pixels = frame.CopyPixels();
        try
        {
            var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96,96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            try
            {
                var target = bitmap.Lock();
                List<Exception> transferFailures = [];
                try
                {
                    for (var row = 0; row < frame.Height; row++)
                        Marshal.Copy(pixels, row * frame.Stride, IntPtr.Add(target.Address, row * target.RowBytes), frame.Width * 4);
                }
                catch (Exception error) { StudioOriginalTaskDrain.Add(transferFailures, error); }
                try { target.Dispose(); } catch (Exception error) { StudioOriginalTaskDrain.Add(transferFailures, error); }
                StudioOriginalTaskDrain.Throw(transferFailures);
                // Publish ownership before Source can notify/reenter retirement.
                _bitmap = bitmap; Source = bitmap;
            }
            catch (Exception primary)
            {
                List<Exception> publicationFailures = [primary];
                if (ReferenceEquals(_bitmap, bitmap)) _bitmap = null;
                try { if (ReferenceEquals(Source, bitmap)) Source = null; }
                catch (Exception error) { StudioOriginalTaskDrain.Add(publicationFailures, error); }
                try { bitmap.Dispose(); } catch (Exception error) { StudioOriginalTaskDrain.Add(publicationFailures, error); }
                StudioOriginalTaskDrain.Throw(publicationFailures); throw;
            }
        }
        finally { Array.Clear(pixels); }
        ScheduleNextFrame();
    }
    private void ScheduleNextFrame()
    {
        _animation.Stop();
        if (_disposed || _initializing || _advancing || !_preview.IsAnimating || _preview.DelayMicroseconds <= 0) return;
        if (_preview.DelayMicroseconds > TimeSpan.MaxValue.Ticks / 10)
        {
            _preview.Clear();
            return;
        }
        _animation.Interval = TimeSpan.FromTicks(_preview.DelayMicroseconds * 10);
        _animation.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs args)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _initializing || _advancing) return;
        _advancing = true;
        _originals.RemoveAll(value => value.Task.IsCompletedSuccessfully);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new Original();
        original.Task = AdvanceOriginalAsync(published.Task, original);
        _original = original; _originals.Add(original);
        published.SetResult();
    }

    private async Task AdvanceOriginalAsync(Task published, Original original)
    {
        await published;
        List<Exception> failures = [];
        try { _animation.Stop(); await _preview.AdvanceAsync(_ownedToken); }
        catch (Exception primary)
        {
            original.Failure = primary;
            original.ObservedOwnedCancellation = primary is OperationCanceledException cancelled &&
                _lifetime.IsCancellationRequested && cancelled.CancellationToken == _ownedToken;
            StudioOriginalTaskDrain.Add(failures, primary);
            // An unavailable UI does not erase the original callback failure.
            try { _preview.Clear(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
            if (!_disposed)
                try { AutomationProperties.SetName(this, "Avatar preview unavailable."); }
                catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        }
        finally
        {
            _advancing = false;
            if (!_disposed)
                try { ScheduleNextFrame(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        }
        StudioOriginalTaskDrain.Throw(failures);
    }

    internal bool IsObservedOwnedCancellation(OperationCanceledException error) =>
        _originals.Any(original => original.ObservedOwnedCancellation &&
            ReferenceEquals(original.Failure, error) && error.CancellationToken == _ownedToken);

    public Task WhenAnimationIdleAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        return _original.Task;
    }

    public Task CloseAndDrainAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_retirement is not null) return _retirement;
        // Publish admission and the coalesced SAME close Task before Cancel or any
        // public native notification can reenter this control.
        _disposed = true;
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _retirement = RetireOriginalAsync(published.Task, _originals.ToArray());
        published.SetResult();
        return _retirement;
    }

    private async Task RetireOriginalAsync(Task published, Original[] originals)
    {
        await published;
        List<Exception> failures = [];
        void Attempt(Action action) { try { action(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); } }
        Attempt(() => _preview.Changed -= OnChanged);
        Attempt(() => _animation.Stop());
        Attempt(() => _animation.Tick -= OnAnimationTick);
        Attempt(() => DetachedFromVisualTree -= OnDetachedVisual);
        Attempt(() => DetachedFromLogicalTree -= OnDetachedLogical);
        Attempt(() => _lifetime.Cancel());
        foreach (var original in originals)
        {
            try { await original.Task; }
            catch (OperationCanceledException error) when (original.ObservedOwnedCancellation &&
                error.CancellationToken == _ownedToken) { }
            catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        }
        Attempt(() => _preview.Clear());
        var bitmap = _bitmap; _bitmap = null;
        Attempt(() => Source = null);
        Attempt(() => bitmap?.Dispose());
        Attempt(() => _lifetime.Dispose());
        StudioOriginalTaskDrain.Throw(failures);
    }

    public void Dispose() { _ = CloseAndDrainAsync(); }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
