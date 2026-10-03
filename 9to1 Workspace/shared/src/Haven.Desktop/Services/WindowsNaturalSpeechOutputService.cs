/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/WindowsNaturalSpeechOutputService.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns WindowsNaturalSpeechOutputService, PlaybackState. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

#if HAVEN_WINDOWS_DESKTOP
using Haven.Application;
using Haven.Core;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Haven.Desktop.Services;

/// <summary>
/// Desktop-only speech output using the modern Windows speech synthesis voice bank.
/// Playback is process-local and interruptible without waiting behind the active utterance.
/// </summary>
public sealed class WindowsNaturalSpeechOutputService : ISpeechOutputService, IContinuableSpeechOutputService, IOriginalSpeechPlaybackReceiptIssuer, IAsyncDisposable
{
    /// <summary>
    /// Stores utterance gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _utteranceGate = new(1, 1);
    /// <summary>
    /// Stores state gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly object _stateGate = new();
    /// <summary>
    /// Stores current locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlaybackState? _current;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try { return SpeechSynthesizer.AllVoices.Count > 0; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
    /// </summary>
    public string? UnavailableReason => IsAvailable
        ? null
        : OperatingSystem.IsWindows()
            ? "No modern Windows speech voices are installed. Add a Windows language speech pack."
            : "Modern Windows speech synthesis requires Windows.";

    /// <summary>
    /// Gets or updates devices, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<CallAudioDevice> Devices { get; } =
        [new CallAudioDevice("default", "Windows default output", true)];

    public IReadOnlyList<CallVoice> Voices
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return [];
            try
            {
                var defaultId = SpeechSynthesizer.DefaultVoice?.Id;
                return SpeechSynthesizer.AllVoices
                    .OrderByDescending(voice => string.Equals(voice.Id, defaultId, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(voice => voice.Language, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(voice => new CallVoice(
                        voice.Id,
                        voice.DisplayName,
                        voice.Language,
                        string.Equals(voice.Id, defaultId, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
            }
            catch (Exception)
            {
                return [];
            }
        }
    }

    /// <summary>
    /// Performs speak asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken)
        => SpeakCoreAsync(text, voiceName, outputDeviceId, cancellationToken, null);

    public bool CanContinueVoice(string voiceId) => !_disposed && !string.IsNullOrWhiteSpace(voiceId) &&
        Voices.Any(voice => string.Equals(voice.Id, voiceId, StringComparison.OrdinalIgnoreCase));

    public bool WasIssuedPlayback(ISpeechPlaybackContinuation playback) =>
        playback is OriginalPlayback issued && issued.WasIssuedBy(this);

    public bool IsOriginalPlayback(ISpeechPlaybackContinuation playback)
    {
        lock (_stateGate)
            return !_disposed && playback is OriginalPlayback issued && issued.BelongsTo(this, _current);
    }

    public Task<ISpeechPlaybackContinuation> StartContinuableAsync(string text, string? voiceName,
        string? outputDeviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("An original utterance is required.", nameof(text));
        if (string.IsNullOrWhiteSpace(voiceName)) throw new ArgumentException("An exact original Windows voice ID is required.", nameof(voiceName));
        var opened = new TaskCompletionSource<ISpeechPlaybackContinuation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = SpeakCoreAsync(text, voiceName, outputDeviceId, cancellationToken,
            state => opened.TrySetResult(new OriginalPlayback(this, state)));
        _ = ObserveStartAsync(operation, opened);
        return opened.Task;
    }
    private static async Task ObserveStartAsync(Task operation, TaskCompletionSource<ISpeechPlaybackContinuation> opened)
    {
        try { await operation.ConfigureAwait(false); }
        catch (OperationCanceledException error) { opened.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { opened.TrySetException(error); }
    }

    private async Task SpeakCoreAsync(string text, string? voiceName, string? outputDeviceId,
        CancellationToken cancellationToken, Action<PlaybackState>? opened)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!string.IsNullOrWhiteSpace(outputDeviceId)
            && !string.Equals(outputDeviceId, "default", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The modern Windows speech service currently supports only the default output device.", nameof(outputDeviceId));
        }

        await _utteranceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        PlaybackState? state = null;
        try
        {
            lock (_stateGate) { ObjectDisposedException.ThrowIf(_disposed, this); }
            StopCurrent();
            cancellationToken.ThrowIfCancellationRequested();

            var synthesizer = new SpeechSynthesizer();
            SpeechSynthesisStream? stream = null;
            MediaSource? source = null;
            MediaPlayer? player = null;
            try
            {
                var selected = SpeechSynthesizer.AllVoices.FirstOrDefault(voice =>
                    string.Equals(voice.Id, voiceName, StringComparison.OrdinalIgnoreCase) ||
                    opened is null && string.Equals(voice.DisplayName, voiceName, StringComparison.OrdinalIgnoreCase));
                if (selected is not null) synthesizer.Voice = selected;
                if (opened is not null && (selected is null || !string.Equals(selected.Id, voiceName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The original Windows voice is unavailable; continuation never selects a fallback.");
                stream = await synthesizer.SynthesizeTextToStreamAsync(text);
                cancellationToken.ThrowIfCancellationRequested();
                source = MediaSource.CreateFromStream(stream, stream.ContentType);
                player = new MediaPlayer
                {
                    AutoPlay = false,
                    Source = source
                };

                state = new PlaybackState(synthesizer, stream, source, player, synthesizer.Voice.Id, cancellationToken);
                lock (_stateGate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _current = state;
                    opened?.Invoke(state);
                    player.Play();
                }

                await state.Completion.Task.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                state?.Completion.TrySetException(error);
                if (state is null)
                {
                    player?.Dispose();
                    source?.Dispose();
                    stream?.Dispose();
                    synthesizer.Dispose();
                }
                throw;
            }
        }
        finally
        {
            if (state is not null)
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_current, state)) _current = null;
                }
                state.Dispose();
            }
            _utteranceGate.Release();
        }
    }

    private sealed class OriginalPlayback(WindowsNaturalSpeechOutputService owner, PlaybackState original)
        : IOriginalSpeechPlaybackStop
    {
        public bool WasIssuedBy(WindowsNaturalSpeechOutputService candidate) => ReferenceEquals(owner, candidate);
        public bool BelongsTo(WindowsNaturalSpeechOutputService candidate, PlaybackState? current) =>
            ReferenceEquals(owner, candidate) && ReferenceEquals(original, current) && !original.Completion.Task.IsCompleted;
        public Guid PlaybackId => original.Id;
        public string VoiceId => original.VoiceId;
        public Task Completion => original.Completion.Task;
        public Task<SpeechPlaybackCheckpoint> PauseAsync(CancellationToken ct) => owner.ControlOriginalAsync(original, true, ct);
        public Task<SpeechPlaybackCheckpoint> ResumeAsync(CancellationToken ct) => owner.ControlOriginalAsync(original, false, ct);
        public Task<bool> StopOriginalAsync(CancellationToken ct) => owner.StopOriginalAsync(original, ct);
    }
    // Cleanup can retire only the privately retained original. Do not resolve ambient _current
    // and do not wait behind a paused control: cancellation wakes its actual Completion waiter.
    private Task<bool> StopOriginalAsync(PlaybackState original, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_stateGate)
        {
            if (!ReferenceEquals(_current, original) || original.Completion.Task.IsCompleted)
                return Task.FromResult(false);
            _current = null;
        }
        original.Cancel();
        return Task.FromResult(true);
    }

    private void RequireOriginal(PlaybackState original)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(_current, original) || original.Completion.Task.IsCompleted)
        {
            // Retain refusal for this exact original, but expose its actual terminal evidence.
            // A native MediaFailed fault must not disappear behind the lifetime rejection.
            var completion = original.Completion.Task;
            var reason = completion.IsFaulted ? "faulted" : completion.IsCanceled ? "canceled" :
                completion.IsCompletedSuccessfully ? "ended" : "replaced";
            throw new InvalidOperationException("The original speech playback is no longer live (" + reason + ").",
                completion.Exception);
        }
    }
    private async Task<SpeechPlaybackCheckpoint> ControlOriginalAsync(PlaybackState original, bool pause, CancellationToken ct)
    {
        await original.Controls.WaitAsync(ct).ConfigureAwait(false);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MediaPlaybackSession? session = null;
        void Changed(MediaPlaybackSession sender, object args)
        {
            try
            {
                if (sender.PlaybackState == (pause ? MediaPlaybackState.Paused : MediaPlaybackState.Playing)) observed.TrySetResult();
            }
            catch (ObjectDisposedException) { } // Terminal disposal is observed through the original completion task.
        }
        var subscribed = false;
        try
        {
            lock (_stateGate)
            {
                ct.ThrowIfCancellationRequested(); RequireOriginal(original);
                session = original.Player.PlaybackSession;
                session.PlaybackStateChanged += Changed; subscribed = true;
                if (pause) original.Player.Pause(); else original.Player.Play();
                Changed(session, new object());
            }
            await Task.WhenAny(observed.Task, original.Completion.Task).WaitAsync(ct).ConfigureAwait(false);
            lock (_stateGate)
            {
                ct.ThrowIfCancellationRequested(); RequireOriginal(original);
                if (session!.PlaybackState != (pause ? MediaPlaybackState.Paused : MediaPlaybackState.Playing))
                    throw new InvalidOperationException("Original playback did not confirm the requested state.");
                return new(original.Id, original.VoiceId, session.Position, pause);
            }
        }
        finally
        {
            if (subscribed) { try { session!.PlaybackStateChanged -= Changed; } catch (ObjectDisposedException) { } }
            original.Controls.Release();
        }
    }

    /// <summary>
    /// Performs stop asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        StopCurrent();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs the stop current step owned by this component.
    /// </summary>
    private void StopCurrent()
    {
        PlaybackState? state;
        lock (_stateGate)
        {
            state = _current;
            _current = null;
        }
        state?.Cancel();
    }

    /// <summary>
    /// Performs dispose asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        StopCurrent();
        await _utteranceGate.WaitAsync().ConfigureAwait(false);
        _utteranceGate.Release();
        // Existing queued callers must observe disposal and release safely. No native
        // wait handle is created: AvailableWaitHandle is never exposed by this service.
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents playback state and keeps its related state and behavior together.
    /// </summary>
    private sealed class PlaybackState : IDisposable
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal string VoiceId { get; }
        internal SemaphoreSlim Controls { get; } = new(1, 1);
        internal MediaPlayer Player => _player;
        /// <summary>
        /// Stores synthesizer locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly SpeechSynthesizer _synthesizer;
        /// <summary>
        /// Stores stream locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly SpeechSynthesisStream _stream;
        /// <summary>
        /// Stores source locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly MediaSource _source;
        /// <summary>
        /// Stores player locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly MediaPlayer _player;
        /// <summary>
        /// Stores playback cancellation locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly CancellationTokenSource _playbackCancellation;
        /// <summary>
        /// Stores registration locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly CancellationTokenRegistration _registration;
        /// <summary>
        /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private int _disposed;

        /// <summary>
        /// Gets or updates completion, the bindable or domain state represented by this property.
        /// </summary>
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PlaybackState(
            SpeechSynthesizer synthesizer,
            SpeechSynthesisStream stream,
            MediaSource source,
            MediaPlayer player,
            string voiceId,
            CancellationToken cancellationToken)
        {
            VoiceId = voiceId;
            _synthesizer = synthesizer;
            _stream = stream;
            _source = source;
            _player = player;
            _playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _registration = _playbackCancellation.Token.Register(
                () => Completion.TrySetCanceled(_playbackCancellation.Token));
            _player.MediaEnded += OnEnded;
            _player.MediaFailed += OnFailed;
        }

        /// <summary>
        /// Reports whether cancel is true for the current state.
        /// </summary>
        public void Cancel()
        {
            try { _playbackCancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            try
            {
                _player.Pause();
                _player.Source = null;
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Handles the ended event raised by the UI or runtime.
        /// </summary>
        private void OnEnded(MediaPlayer sender, object args) => Completion.TrySetResult();

        /// <summary>
        /// Handles the failed event raised by the UI or runtime.
        /// </summary>
        private void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            // The native error text can be empty. Preserve its actual error category,
            // extended HRESULT and exception instead of discarding the terminal cause.
            var extended = args.ExtendedErrorCode;
            var code = extended is null ? "unavailable" : "0x" + extended.HResult.ToString(
                "X8", System.Globalization.CultureInfo.InvariantCulture);
            Completion.TrySetException(new InvalidOperationException(
                "Windows speech playback failed (" + args.Error + ", HRESULT " + code + "): " + args.ErrorMessage,
                extended));
        }

        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _player.MediaEnded -= OnEnded;
            _player.MediaFailed -= OnFailed;
            _registration.Dispose();
            try
            {
                _player.Pause();
                _player.Source = null;
            }
            catch (ObjectDisposedException) { }
            _player.Dispose();
            _source.Dispose();
            _stream.Dispose();
            _synthesizer.Dispose();
            _playbackCancellation.Dispose();
        }
    }
}
#else
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Explicitly unavailable speech adapter for non-Windows desktop targets.
/// The Windows implementation remains unchanged; this seam keeps Linux startup
/// fail-closed without pretending that Windows speech APIs are available.
/// </summary>
public sealed class WindowsNaturalSpeechOutputService : ISpeechOutputService, IContinuableSpeechOutputService, IOriginalSpeechPlaybackReceiptIssuer, IAsyncDisposable
{
    public bool IsAvailable => false;

    public string? UnavailableReason => "Modern Windows speech synthesis requires Windows.";

    public IReadOnlyList<CallAudioDevice> Devices { get; } =
        [new CallAudioDevice("default", "Windows default output", true)];

    public IReadOnlyList<CallVoice> Voices { get; } = [];

    public Task SpeakAsync(
        string text,
        string? voiceName,
        string? outputDeviceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(new PlatformNotSupportedException(UnavailableReason));
    }

    public bool WasIssuedPlayback(ISpeechPlaybackContinuation playback) => false;
    public bool IsOriginalPlayback(ISpeechPlaybackContinuation playback) => false;

    public bool CanContinueVoice(string voiceId) => false;

    public Task<ISpeechPlaybackContinuation> StartContinuableAsync(string text, string? voiceName,
        string? outputDeviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ISpeechPlaybackContinuation>(new PlatformNotSupportedException(UnavailableReason));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
#endif
