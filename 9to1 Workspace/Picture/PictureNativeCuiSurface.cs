using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;

namespace HavenOS.Images;

/// <summary>Owning native Picture CUI over an exact authorized Files revision. The host supplies
/// its real authority handshake and canonical open operation; this surface grants no access.</summary>
public sealed class PictureNativeCuiSurface(
    Func<CancellationToken, Task<PictureFilesOpenResult>> open,
    PictureFilesSourceRenderer renderer,
    PictureGlycinDecoder decoder,
    ICuiSceneReadiness readiness,
    Func<PictureWorkspaceCommand, CancellationToken, ValueTask>? dispatchOwner = null,
    Func<PictureWorkspaceCommandKind, bool>? ownerAvailable = null,
    IMotionPreferenceSource? motionPreferences = null) : UserControl, IDisposable
{
    public event EventHandler? SourceUnavailable;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Border _viewport = new() { ClipToBounds=true };
    private readonly ImageViewportState _view = new();
    private IPointer? _panPointer;
    private Point _panPosition;
    private long _gestureGeneration;
    private long _panMoveVersion;
    private bool _panReady;
    private bool _showOriginal;
    public bool IsShowingOriginal => _showOriginal;
    public bool IsPreviewPanning => _panPointer is not null && _panReady;
    private bool CanNavigate => !_disposed && _available && TopLevel.GetTopLevel(_viewport) is not null;
    public double PreviewScale => _view.Scale;
    public (double X,double Y) PreviewOffset => (_view.OffsetX,_view.OffsetY);
    private PicturePinnedRasterSource? _source;
    private PictureArtifactEnvelope? _artifact;
    private PictureCuiWorkspace? _bindings;
    private CuiSceneHost? _scene;
    private Bitmap? _bitmap;
    private bool _initialized, _disposed, _available, _playing, _motionAllowsPlayback;
    private readonly DispatcherTimer _playTimer = new();
    private string _motionStatus = "Automatic playback is unavailable. Manual frame stepping remains available.";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("This captured Picture surface is already initialized.");
        _initialized = true;
        _playTimer.Tick += PlaybackTick;
        if (motionPreferences is not null) motionPreferences.Changed += MotionChanged;
        DetachedFromVisualTree += (_, _) => PausePlayback();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _operation.WaitAsync(linked.Token);
        try
        {
            await RequireReadyAsync(linked.Token);
            var opened = await open(linked.Token);
            _artifact = PictureArtifactCodec.Deserialize(PictureArtifactCodec.Serialize(opened.Artifact));
            _source = await renderer.LoadAnimationWithGlycinAsync(_artifact, opened.CasRevisionId.Value, decoder, linked.Token);
            await RequireReadyAsync(linked.Token);
            await _source.ValidateAccessAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _available = true;
            await RefreshMotionAsync(linked.Token);
            _bindings = new(DispatchAsync, kind => !_disposed && _available &&
                (kind switch
                {
                    PictureWorkspaceCommandKind.ShowOriginal => _source is not null,
                    PictureWorkspaceCommandKind.ShowEdited => _source is not null,
                    PictureWorkspaceCommandKind.ZoomIn => _source is not null,
                    PictureWorkspaceCommandKind.ZoomOut => _source is not null,
                    PictureWorkspaceCommandKind.FitView => _source is not null,
                    PictureWorkspaceCommandKind.PanLeft => _source is not null,
                    PictureWorkspaceCommandKind.PanRight => _source is not null,
                    PictureWorkspaceCommandKind.PanUp => _source is not null,
                    PictureWorkspaceCommandKind.PanDown => _source is not null,
                    PictureWorkspaceCommandKind.ShowInformation => _source is not null,
                    PictureWorkspaceCommandKind.NextFrame => !_playing && _source?.CanAdvanceFrames == true,
                    PictureWorkspaceCommandKind.PlayAnimation => !_playing && _motionAllowsPlayback && _source?.CanAdvanceFrames == true,
                    PictureWorkspaceCommandKind.PauseAnimation => _playing,
                    _ => dispatchOwner is not null && ownerAvailable?.Invoke(kind) == true
                }));
            RefreshBindings();
            var registry = new CuiControlRegistry();
            _viewport.Child=_image;
            _viewport.PointerWheelChanged+=PreviewWheel;
            _viewport.PointerPressed+=PreviewPressed;
            _viewport.PointerMoved+=PreviewMoved;
            _viewport.PointerReleased+=PreviewReleased;
            _viewport.PointerCaptureLost+=PreviewCaptureLost;
            _viewport.DetachedFromVisualTree+=PreviewDetached;
            _image.RenderTransformOrigin=Avalonia.RelativePoint.Center;
            registry.RegisterControlType("PictureRasterSurface", _ => _viewport);
            _scene = new(registry);
            var state = await _scene.ShowAsync(new("picture", "Picture", "Picture", PictureCuiWorkspace.LoadDocument(),
                _bindings, _bindings, readiness), linked.Token);
            if (state.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(state.Message);
            await _source.ValidateAccessAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            Render();
            Content = _scene;
        }
        catch { Invalidate(); throw; }
        finally { _operation.Release(); if (_disposed || !_available) ReleaseSource(); }
    }

    private async ValueTask DispatchAsync(PictureWorkspaceCommand command, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_available || _disposed || _artifact is null ||
            command.DocumentId != _artifact.Document.DocumentId || command.BaseRevision != _artifact.Document.Revision ||
            command.BackingFileId != _artifact.BackingFileId)
            throw new UnauthorizedAccessException("This Picture operation is unavailable.");
        if(command.Kind is PictureWorkspaceCommandKind.ShowOriginal or PictureWorkspaceCommandKind.ShowEdited)
        {
            await ValidateAccessAsync(cancellationToken);
            if(!CanNavigate)throw new UnauthorizedAccessException("Picture comparison authority changed or its view closed.");
            Render(command.Kind==PictureWorkspaceCommandKind.ShowOriginal);
            CancelPreviewPan();_view.Reset();ApplyPreviewTransform();return;
        }
        if (command.Kind is PictureWorkspaceCommandKind.ZoomIn or PictureWorkspaceCommandKind.ZoomOut or PictureWorkspaceCommandKind.FitView
            or PictureWorkspaceCommandKind.PanLeft or PictureWorkspaceCommandKind.PanRight or PictureWorkspaceCommandKind.PanUp or PictureWorkspaceCommandKind.PanDown)
        {
            await ValidateAccessAsync(cancellationToken);
            if (!CanNavigate) throw new UnauthorizedAccessException("Picture preview authority changed or its view closed.");
            CancelPreviewPan();
            var width=_viewport.Bounds.Width;var height=_viewport.Bounds.Height;
            if (!double.IsFinite(width) || !double.IsFinite(height) || width<=0 || height<=0)
                throw new InvalidOperationException("The Picture preview has no displayed viewport.");
            switch(command.Kind)
            {
                case PictureWorkspaceCommandKind.ZoomIn: _view.ZoomAt(1.25,width/2,height/2,width,height);break;
                case PictureWorkspaceCommandKind.ZoomOut: _view.ZoomAt(0.8,width/2,height/2,width,height);break;
                case PictureWorkspaceCommandKind.FitView: _view.Reset();break;
                case PictureWorkspaceCommandKind.PanLeft: _view.PanBy(-50,0);break;
                case PictureWorkspaceCommandKind.PanRight: _view.PanBy(50,0);break;
                case PictureWorkspaceCommandKind.PanUp: _view.PanBy(0,-50);break;
                case PictureWorkspaceCommandKind.PanDown: _view.PanBy(0,50);break;
            }
            ApplyPreviewTransform();return;
        }
        if (command.Kind == PictureWorkspaceCommandKind.ShowInformation)
        {
            await ValidateAccessAsync(cancellationToken);
            if (_disposed || !_available) throw new UnauthorizedAccessException("Picture information is unavailable.");
            _informationVisible = !_informationVisible; RefreshBindings(); return;
        }
        if (command.Kind == PictureWorkspaceCommandKind.PauseAnimation) { PausePlayback(); return; }
        if (command.Kind == PictureWorkspaceCommandKind.PlayAnimation)
        {
            if (_source?.CanAdvanceFrames != true) throw new NotSupportedException("This source has no supported animation.");
            await ValidateAccessAsync(cancellationToken);
            await RefreshMotionAsync(cancellationToken);
            if (!_motionAllowsPlayback || _disposed || !_available)
                throw new InvalidOperationException("Playback is paused by the current motion preference or unavailable source.");
            _playing = true; ScheduleFrame(); RefreshBindings(); return;
        }
        if (command.Kind != PictureWorkspaceCommandKind.NextFrame)
        {
            PausePlayback();
            if (dispatchOwner is null || ownerAvailable?.Invoke(command.Kind) != true)
                throw new UnauthorizedAccessException("The owning Picture action is unavailable.");
            await ValidateAccessAsync(cancellationToken);
            if (_disposed || !_available || ownerAvailable?.Invoke(command.Kind) != true)
                throw new UnauthorizedAccessException("Picture access changed before the owning action could begin.");
            await dispatchOwner(command, cancellationToken);
            return;
        }
        var automaticAdvance = _playing;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _operation.WaitAsync(linked.Token);
        try
        {
            if (!_available) throw new UnauthorizedAccessException("Picture access changed while waiting for the current frame.");
            await RequireReadyAsync(linked.Token);
            if (automaticAdvance)
            {
                await RefreshMotionAsync(linked.Token);
                if (!_playing) return;
            }
            await _source!.AdvanceFrameAsync(linked.Token);
            await RequireReadyAsync(linked.Token);
            await _source.ValidateAccessAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (automaticAdvance)
            {
                await RefreshMotionAsync(linked.Token);
                if (!_playing) return; // Keep the last presented frame when motion was reduced during decode.
            }
            Render(); RefreshBindings();
        }
        catch { Invalidate(); throw; }
        finally { _operation.Release(); if (_disposed || !_available) ReleaseSource(); }
    }

    private void MotionChanged(object? sender, EventArgs args)
    {
        // The shared source can notify from a storage-reader thread.
        Dispatcher.UIThread.Post(async () =>
        {
            if (_disposed) return;
            try { await RefreshMotionAsync(_lifetime.Token); }
            catch (OperationCanceledException) { }
        });
    }
    private async Task RefreshMotionAsync(CancellationToken cancellationToken)
    {
        var allowed = false;
        var status = "Automatic playback is unavailable. Manual frame stepping remains available.";
        try
        {
            if (motionPreferences is not null)
            {
                var observed = await motionPreferences.ReadAsync(cancellationToken);
                allowed = observed.IsAvailable && !observed.ReduceAnimations;
                status = !observed.IsAvailable ? "Motion preferences are unavailable; automatic playback is paused."
                    : observed.ReduceAnimations ? "Reduced motion is enabled; automatic playback is paused." : "";
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* An unavailable preference pauses automatic playback. */ }
        if (_disposed) return;
        _motionAllowsPlayback = allowed; _motionStatus = status;
        if (!allowed) PausePlayback();
        else RefreshBindings();
    }

    private void PausePlayback()
    {
        _playing = false; _playTimer.Stop(); RefreshBindings();
    }
    private void ScheduleFrame()
    {
        if (!_playing || !_available || _disposed) return;
        _playTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp((_source?.FrameDelayMicroseconds ?? 1000) / 1000d, 1, int.MaxValue));
        _playTimer.Start();
    }
    private async void PlaybackTick(object? sender, EventArgs args)
    {
        _playTimer.Stop(); // One donor frame request at a time; no accumulating timer tasks or frame buffers.
        if (!_playing || !_available || _disposed || _artifact is null) return;
        try
        {
            await RefreshMotionAsync(_lifetime.Token);
            if (!_playing) return;
            await DispatchAsync(new(PictureWorkspaceCommandKind.NextFrame, _artifact.Document.DocumentId,
                _artifact.Document.Revision, _artifact.Document.FileId, _artifact.BackingFileId), _lifetime.Token);
            ScheduleFrame();
        }
        catch { Invalidate(); }
    }

    public async Task ValidateAccessAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized || !_available) throw new UnauthorizedAccessException("The captured Picture source is unavailable.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _operation.WaitAsync(linked.Token);
        try
        {
            if (!_available) throw new UnauthorizedAccessException("Picture access changed while waiting for validation.");
            await RequireReadyAsync(linked.Token);
            await _source!.ValidateAccessAsync(linked.Token);
            await RequireReadyAsync(linked.Token);
        }
        catch { Invalidate(); throw; }
        finally { _operation.Release(); if (_disposed || !_available) ReleaseSource(); }
    }

    private async Task RequireReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var state = await readiness.CheckAsync(cancellationToken);
        if (state.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(state.Message);
    }
    private void ApplyPreviewTransform()
    {
        var transforms=new TransformGroup();transforms.Children.Add(new ScaleTransform(_view.Scale,_view.Scale));
        transforms.Children.Add(new TranslateTransform(_view.OffsetX,_view.OffsetY));_image.RenderTransform=transforms;
        RefreshBindings();
    }
    private void CancelPreviewPan()
    {
        ++_gestureGeneration; var pointer=_panPointer;_panPointer=null;_panReady=false;
        pointer?.Capture(null);
    }
    private async void PreviewWheel(object? sender,PointerWheelEventArgs args)
    {
        if(!CanNavigate || !double.IsFinite(args.Delta.Y) || args.Delta.Y==0) return;
        CancelPreviewPan();var generation=_gestureGeneration;var position=args.GetPosition(_viewport);
        var factor=Math.Pow(1.25,Math.Clamp(args.Delta.Y,-8,8));args.Handled=true;
        try
        {
            await ValidateAccessAsync(_lifetime.Token);
            if(!CanNavigate || generation!=_gestureGeneration)return;
            if(_view.ZoomAt(factor,position.X,position.Y,_viewport.Bounds.Width,_viewport.Bounds.Height))ApplyPreviewTransform();
        }
        catch { CancelPreviewPan(); }
    }
    private async void PreviewPressed(object? sender,PointerPressedEventArgs args)
    {
        if(!CanNavigate || _panPointer is not null)return;
        var point=args.GetCurrentPoint(_viewport);
        if(!point.Properties.IsMiddleButtonPressed)return;
        _panPointer=args.Pointer;_panPosition=point.Position;var generation=++_gestureGeneration;
        args.Pointer.Capture(_viewport);args.PreventGestureRecognition();args.Handled=true;
        try
        {
            await ValidateAccessAsync(_lifetime.Token);
            if(CanNavigate && generation==_gestureGeneration && _panPointer==args.Pointer)_panReady=true;
            else CancelPreviewPan();
        }
        catch { CancelPreviewPan(); }
    }
    private async void PreviewMoved(object? sender,PointerEventArgs args)
    {
        if(!CanNavigate || !_panReady || _panPointer!=args.Pointer)return;
        var point=args.GetCurrentPoint(_viewport);
        if(!point.Properties.IsMiddleButtonPressed){CancelPreviewPan();return;}
        var position=point.Position;var generation=_gestureGeneration;var move=++_panMoveVersion;args.Handled=true;
        try
        {
            await ValidateAccessAsync(_lifetime.Token);
            if(!CanNavigate || generation!=_gestureGeneration || move!=_panMoveVersion || _panPointer!=args.Pointer)return;
            var delta=position-_panPosition;
            if(_view.PanBy(delta.X,delta.Y)){_panPosition=position;ApplyPreviewTransform();}
        }
        catch { CancelPreviewPan(); }
    }
    private void PreviewReleased(object? sender,PointerReleasedEventArgs args)
    { if(_panPointer==args.Pointer){CancelPreviewPan();args.Handled=true;} }
    private void PreviewCaptureLost(object? sender,PointerCaptureLostEventArgs args)
    { if(_panPointer is not null)CancelPreviewPan(); }
    private void PreviewDetached(object? sender,Avalonia.VisualTreeAttachmentEventArgs args) => CancelPreviewPan();

    private bool _informationVisible;
    private void Render(bool? original=null)
    {
        if (_disposed || !_available) throw new UnauthorizedAccessException("Picture access changed.");
        var requestedMode=original??_showOriginal;
        var next = requestedMode ? _source!.RenderOriginal() : _source!.Render();
        _showOriginal=requestedMode;
        var previous = _bitmap; _bitmap = next; _image.Source = next; previous?.Dispose();
    }
    public void RefreshActionAvailability() => _bindings?.RefreshAvailability();
    private void RefreshBindings() => _bindings?.Refresh(_artifact?.Document,
        _available ? dispatchOwner is null ? "Canonical Files document · read-only preview" : "Canonical editable Picture document"
            : "Source access changed. Reopen the Picture document.",
        (_available ? dispatchOwner is null ? _source?.CanAdvanceFrames == true
            ? "Original source retained. Native frame stepping is available." : "Original source retained."
            : "Review non-destructive edits in Home before committing. The original source is retained."
            : "Picture preview unavailable") + (_available && _source?.CanAdvanceFrames == true && !_motionAllowsPlayback ? " " + _motionStatus : ""),
        _artifact?.BackingFileId, _available ? _source?.FrameDelayMicroseconds : null, _playing,
        _available && _informationVisible ? _source?.InformationSummary ?? "" : "", _available && _informationVisible,
        _available && _artifact?.SemanticHistory?.Undo.Count > 0,
        _available && _artifact?.SemanticHistory?.Redo.Count > 0,
        _artifact?.SemanticHistory is { } history
            ? $"Retained history: {history.Undo.Count} undo, {history.Redo.Count} redo." + (history.DiscardedEarlierEntries > 0 ? " Earlier history is unavailable." : "")
            : "History is unavailable for this revision.",
        $"{(_showOriginal ? "Original source frame" : "Current editable composition")} · Preview zoom: {_view.Scale*100:0.#}% of fit; pan {_view.OffsetX:0.#}, {_view.OffsetY:0.#}. View only.");
    private void Invalidate()
    {
        CancelPreviewPan();
        var notify = _available && !_disposed;
        _playing = false; _playTimer.Stop();
        _available = false; _showOriginal=false; _artifact = null; _informationVisible = false;
        _image.Source = null; _bitmap?.Dispose(); _bitmap = null;
        RefreshBindings();
        if (notify) SourceUnavailable?.Invoke(this, EventArgs.Empty);
    }
    private void ReleaseSource() { _source?.Dispose(); _source = null; }
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        _viewport.PointerWheelChanged-=PreviewWheel;_viewport.PointerPressed-=PreviewPressed;_viewport.PointerMoved-=PreviewMoved;
        _viewport.PointerReleased-=PreviewReleased;_viewport.PointerCaptureLost-=PreviewCaptureLost;_viewport.DetachedFromVisualTree-=PreviewDetached;
        CancelPreviewPan();
        if (motionPreferences is not null) motionPreferences.Changed -= MotionChanged;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); Invalidate(); _playTimer.Tick -= PlaybackTick; Content = null; _scene?.Dispose();
        if (_operation.Wait(0)) { try { ReleaseSource(); } finally { _operation.Release(); } }
        // In-flight operations retain their linked cancellation tokens and release donor state in finally.
    }
}
