using Avalonia.Threading;
using Haven.Core.Media;

namespace HavenOS.Apps.Wave;

public sealed partial class WaveCuiWorkspace
{
    private readonly IMediaEngine? _originalMediaEngine;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly List<WaveFilesProjectService.OriginalSourceAudition> _originalPreviewOwners = [];
    private readonly List<Task> _originalPreviewSources = [];
    private readonly object _previewGate = new();
    private WaveFilesProjectService.OriginalSourceAudition? _sourcePreview;
    private Task? _originalPreviewPoll, _originalPreviewWithdrawal;
    private bool _previewPositionEstablished;
    private string _sourcePreviewStatus = "Preview the selected original audio source. Gain, fades and the arrangement mix are not applied to this preview.";
    private string _sourcePreviewPosition = "Source preview stopped.";
    public IReadOnlyList<Task> OriginalPreviewSources { get { lock (_previewGate) return _originalPreviewSources.ToArray(); } }
    private bool CanPlayOriginalSource => _files is not null && _originalMediaEngine is not null && Clip is { SourceFileID: not null, SourceRevisionID: not null } &&
        _originalPreviewWithdrawal is not { IsCompletedSuccessfully: false };
    private bool CurrentOriginalPreviewMatchesSelection() => _sourcePreview is { HasNativeSession: true } owner && Clip is { } clip &&
        owner.OriginalProject.ProjectId == _snapshot.ProjectId && owner.OriginalProject.SampleRate == _snapshot.SampleRate &&
        owner.OriginalProject.Channels == _snapshot.Channels && SameOriginalWaveformSource(owner.OriginalClip, clip);
    private bool TryGetSourcePreviewValue(string path, out object? value)
    {
        value = path switch
        {
            "SourcePreviewStatus" => _originalMediaEngine is null ? "Open Wave from Home to connect audio playback." : _sourcePreviewStatus,
            "SourcePreviewPosition" => _sourcePreviewPosition, _ => null
        };
        return value is not null;
    }
    private void RetainOriginalPreview(Task source)
    { lock (_previewGate) { _originalPreviewSources.RemoveAll(task => task.IsCompletedSuccessfully); _originalPreviewSources.Add(source); } }
    private async Task PlayOriginalSourceAsync(CancellationToken token)
    {
        if (_originalPreviewWithdrawal is not null) await _originalPreviewWithdrawal;
        if (!CurrentOriginalPreviewMatchesSelection())
        {
            await StopOriginalSourceAsync();
            _originalPreviewOwners.RemoveAll(owner => owner.OriginalClose?.IsCompletedSuccessfully == true);
            if (_originalPreviewOwners.Count >= 16) throw new InvalidOperationException("Wave retains unresolved original audio previews. Recover this same workspace.");
            _sourcePreview = WithinOriginalPreview(() => _files!.CreateOriginalSourceAudition(_snapshot, _snapshot.Revision,
                Clip!.ClipId, _originalMediaEngine!, ScopeOriginalPreview, RetainOriginalPreview, token));
            _previewPositionEstablished = false;
            _originalPreviewOwners.Add(_sourcePreview); RetainOriginalPreview(_sourcePreview.OriginalOpen);
            var opened = await _sourcePreview.OriginalOpen;
            if (!opened.IsSuccess)
            { _sourcePreviewStatus = opened.Error!.Message; await StopOriginalSourceAsync(); return; }
        }
        if (!_previewPositionEstablished && !await SeekOriginalSourceAsync(token)) return;
        var result = await WithinOriginalPreview(() => CaptureExternal(() => _sourcePreview!.SetStateAsync(MediaPlaybackState.Playing, token)));
        _sourcePreviewStatus = result.IsSuccess ? "Playing original source audio. Arrangement gain, fades and mix are not applied." : result.Error!.Message;
        if (result.IsSuccess) _previewTimer.Start();
    }
    private async Task PauseOriginalSourceAsync(CancellationToken token)
    {
        _previewTimer.Stop();
        var result = await WithinOriginalPreview(() => CaptureExternal(() => _sourcePreview!.SetStateAsync(MediaPlaybackState.Paused, token)));
        _sourcePreviewStatus = result.IsSuccess ? "Original source preview paused." : result.Error!.Message;
    }
    private async Task<bool> SeekOriginalSourceAsync(CancellationToken token)
    {
        var owner = _sourcePreview!; var clip = Clip ?? throw new InvalidOperationException("Select the current original source clip.");
        var offset = Math.Clamp(_playhead - clip.TimelineStartFrame, 0, clip.FrameCount);
        var result = await WithinOriginalPreview(() => CaptureExternal(() => owner.SeekAsync(checked(clip.SourceStartFrame + offset), token)));
        if (result.IsSuccess)
        { _previewPositionEstablished = true; _sourcePreviewPosition = "Source preview · " + Time(result.Value.ConvertTo(MediaTimebase.SamplesPerSecond(_snapshot.SampleRate)).Ticks); }
        else _sourcePreviewStatus = result.Error!.Message;
        return result.IsSuccess;
    }
    private async Task StopOriginalSourceAsync()
    {
        _previewTimer.Stop();
        if (_sourcePreview is not { } owner) return;
        var close = WithinOriginalPreview(() => CaptureExternal(owner.CloseOriginalAsync)); RetainOriginalPreview(close);
        await close;
        if (ReferenceEquals(_sourcePreview, owner)) { _sourcePreview = null; _previewPositionEstablished = false; }
        _sourcePreviewStatus = "Original source preview stopped."; _sourcePreviewPosition = "Source preview stopped.";
    }
    private void SynchronizeOriginalPreviewSelection()
    {
        if (_retiring || _disposed || _sourcePreview is not { HasNativeSession: true } || CurrentOriginalPreviewMatchesSelection() ||
            _originalPreviewWithdrawal is { IsCompleted: false }) return;
        _previewTimer.Stop(); _sourcePreviewStatus = "Stopping the previous source preview…";
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _originalPreviewWithdrawal = WithdrawOriginalPreviewAsync(start.Task); RetainOriginalPreview(_originalPreviewWithdrawal); start.SetResult();
        ObserveOriginalPreview(_originalPreviewWithdrawal);
    }
    private async Task WithdrawOriginalPreviewAsync(Task start)
    {
        using var driver = EnterOriginalPreviewDriver(); await start;
        await StopOriginalSourceAsync(); Changed();
    }
    private void OnOriginalPreviewTick(object? sender, EventArgs args)
    {
        if (_retiring || _disposed || _busy || _sourcePreview is not { HasNativeSession: true } owner ||
            _originalPreviewPoll is { IsCompleted: false }) return;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _originalPreviewPoll = PollOriginalPreviewAsync(start.Task, owner); RetainOriginalPreview(_originalPreviewPoll); start.SetResult();
        ObserveOriginalPreview(_originalPreviewPoll);
    }
    private async Task PollOriginalPreviewAsync(Task start, WaveFilesProjectService.OriginalSourceAudition owner)
    {
        using var driver = EnterOriginalPreviewDriver(); await start;
        var result = await WithinOriginalPreview(() => CaptureExternal(() => owner.ReadPositionAsync()));
        if (_retiring || _disposed || !ReferenceEquals(_sourcePreview, owner) || !CurrentOriginalPreviewMatchesSelection()) return;
        if (result.IsSuccess) _sourcePreviewPosition = "Source preview · " + Time(result.Value.ConvertTo(MediaTimebase.SamplesPerSecond(_snapshot.SampleRate)).Ticks);
        else { _previewTimer.Stop(); _sourcePreviewStatus = result.Error!.Message; }
        Changed();
    }
    private void ObserveOriginalPreview(Task actual)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = ObserveOriginalPreviewAsync(start.Task, actual); RetainOriginalPreview(observer); start.SetResult();
    }
    private async Task ObserveOriginalPreviewAsync(Task start, Task actual)
    {
        using var driver = EnterOriginalPreviewDriver(); await start;
        try { await actual; }
        catch (Exception failure)
        {
            _previewTimer.Stop();
            if (!_retiring && !_disposed)
            { _sourcePreviewStatus = "Audio preview retained its original failure: " + failure.Message; Changed(); }
        }
    }
    private async Task CloseOriginalPreviewsAsync()
    {
        _previewTimer.Stop(); var failures = new List<Exception>();
        foreach (var owner in _originalPreviewOwners.ToArray())
            try { var close = WithinOriginalPreview(() => CaptureExternal(owner.CloseOriginalAsync)); RetainOriginalPreview(close); await close; }
            catch (Exception failure) { failures.Add(failure); }
        _previewTimer.Tick -= OnOriginalPreviewTick;
        if (failures.Count != 0) throw new AggregateException("Wave retains its original audio preview retirement failures.", failures);
    }
    private sealed class PreviewInvocation(WaveCuiWorkspace owner, PreviewInvocation? parent)
    { public WaveCuiWorkspace Owner { get; } = owner; public PreviewInvocation? Parent { get; } = parent; public volatile bool Active = true; }
    private static readonly AsyncLocal<PreviewInvocation?> LogicalPreview = new();
    [ThreadStatic] private static PreviewInvocation? PhysicalPreview;
    private sealed class PreviewDriver(PreviewInvocation invocation, PreviewInvocation? prior) : IDisposable
    { public void Dispose() { invocation.Active = false; LogicalPreview.Value = prior; } }
    private IDisposable EnterOriginalPreviewDriver()
    { var prior = LogicalPreview.Value; var invocation = new PreviewInvocation(this, prior); LogicalPreview.Value = invocation; return new PreviewDriver(invocation, prior); }
    private T WithinOriginalPreview<T>(Func<T> callback)
    {
        var prior = PhysicalPreview; var invocation = new PreviewInvocation(this, prior ?? LogicalPreview.Value); PhysicalPreview = invocation;
        try { return callback(); } finally { invocation.Active = false; PhysicalPreview = prior; }
    }
    private void ScopeOriginalPreview(Action body) => WithinOriginalPreview(() => { body(); return true; });
    internal void DemandExternalPreviewJoin()
    {
        static bool Contains(PreviewInvocation? current, WaveCuiWorkspace owner)
        { for (; current is not null; current = current.Parent) if (current.Active && ReferenceEquals(current.Owner, owner)) return true; return false; }
        if (Contains(PhysicalPreview, this) || Contains(LogicalPreview.Value, this))
            throw new InvalidOperationException("The original source preview callback cannot join its owning Wave workspace retirement.");
    }
}
