/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Call/VoiceVisionLiveRuntime.cs, in the Vision & Voice application contract.
 * What: This file adapts the shared live ICallCoordinator (audio/speech/capture runtime) and the
 *       canonical MultimodalSession store into one typed, single-owner Voice & Vision runtime facade.
 * How: One runtime singleton owns one live usage. Start/attach is serialized and coalesced; a
 *       presentation that reopens the same ConversationID attaches to the same canonical SessionID
 *       and never calls Start again. Camera/screen capture only ever begins after an explicit
 *       permitted request and stops frames on stop.
 * Why: The CUI pill and full surface consume this contract, so they never duplicate the runtime,
 *       keep a second speech/capture stack, or invent local app state.
 * Maintenance: Preserve the single-session identity, the start gate and the capture permission
 *       gates. Do not silently start camera/screen/voice from unrelated toggles.
 */

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Haven.Core;

namespace Haven.Application.Call;

/// <summary>
/// Live Voice &amp; Vision runtime facade. The same instance must be shared by the compact pill and
/// the full session surface so they drive one canonical session.
/// </summary>
public sealed class VoiceVisionLiveRuntime : IVoiceVisionRuntime, IAsyncDisposable
{
    private const string SessionByConversationKeyPrefix = "visionvoice.session-by-conversation.v1.";

    private readonly ICallCoordinator _runtime;
    private readonly VisionVoiceSessionService _service;
    private readonly IVersionedSettingsStore? _mappingStore;
    private readonly TimeProvider _time;
    private readonly VoiceVisionBackendProfile _backend;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly Dictionary<Guid, Guid> _sessionByConversation = new();

    private MultimodalSession? _session;
    private VoiceVisionPresentationKind _presentation = VoiceVisionPresentationKind.None;
    private double _audioLevel;
    private bool _muted;
    private bool _paused;
    private bool _captions;
    private bool _transcriptVisible;
    private IReadOnlyList<VisualSourceDescriptor> _visualSources = [];
    private string _permissionNotice = string.Empty;
    private bool _disposed;
    private bool _wired;
    private int _generation;

    // Single-disposal / lifecycle-drain state. Dispose elects exactly one disposer and late
    // callers join the same teardown, so a disposed semaphore is never Released by a stale
    // waiter. _gateBusy counts operations inside admission (queued or admitted) so resources
    // are disposed only after every one has drained.
    private int _disposalStarted;
    private TaskCompletionSource<bool>? _disposalCompletion;
    private int _gateBusy;
    private TaskCompletionSource? _gateIdleTcs;

    /// <summary>When false (after an explicit End), late coordinator callbacks are suppressed and
    /// the projection stays inactive until a new start/attach re-opens the session.</summary>
    private bool _acceptsRuntimeEvents = true;

    public VoiceVisionLiveRuntime(
        ICallCoordinator runtime,
        VisionVoiceSessionService service,
        IVersionedSettingsStore? mappingStore = null,
        TimeProvider? timeProvider = null,
        VoiceVisionBackendProfile? backendProfile = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _mappingStore = mappingStore;
        _time = timeProvider ?? TimeProvider.System;
        _backend = backendProfile ?? VoiceVisionBackendProfile.Default;
        WireRuntime();
    }

    public event EventHandler? StateChanged;

    public VoiceVisionRuntimeState State => BuildState();

    public async Task<VoiceVisionStartResult> StartAsync(VoiceVisionStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ConversationId == Guid.Empty)
            return VoiceVisionStartResult.Failure(new(VisionVoiceErrorCode.InvalidRequest, "A canonical conversation is required.", "ConversationID", "StartSession", false));

        // A sealed/disposed runtime admits no new session.
        if (Volatile.Read(ref _disposed))
            return VoiceVisionStartResult.Failure(new(VisionVoiceErrorCode.SessionNotFound,
                "The runtime is disposed; no session can be started.", "SessionID", "StartSession", true));

        // One published coalescing owner for identity resolution/creation/attachment: the whole
        // start runs under the lifecycle gate, so two callers continuing the same actual interaction
        // resolve the SAME canonical SessionID and never open a second runtime. An End that runs
        // while this start is queued bumps the lifetime generation, which rejects this stale wait.
        var generationAtEntry = Volatile.Read(ref _generation);
        BusyEnter();
        try
        {
            await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            BusyLeave();
            throw;
        }
        try
        {
            if (_disposed || Volatile.Read(ref _generation) != generationAtEntry)
                return VoiceVisionStartResult.Failure(new(VisionVoiceErrorCode.SessionNotFound,
                    "The runtime was sealed while this start was queued.", "SessionID", "StartSession", true));

            var attached = await TryAttachExistingAsync(request, cancellationToken).ConfigureAwait(false);
            if (attached is not null) return attached;
            return await StartNewAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _startGate.Release();
            BusyLeave();
        }
    }

    public async Task<VoiceVisionOperation> EndAsync(CancellationToken cancellationToken)
    {
        // A sealed/disposed runtime cannot end a session it no longer owns.
        if (Volatile.Read(ref _disposed))
            return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.SessionNotFound,
                "The runtime is disposed; no session can be ended.", "SessionID", "EndSession", true));

        // Seal admission synchronously under the same lifecycle gate as Start, so a Start queued
        // before End cannot publish afterwards, and an in-flight Start has fully settled by the
        // time we acquire the gate (Start holds it across its whole run).
        BusyEnter();
        try
        {
            await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            BusyLeave();
            throw;
        }
        try
        {
            Interlocked.Increment(ref _generation);

            if (_runtime.IsActive)
            {
                try
                {
                    // End is a real runtime stop/drain: wait for the coordinator to stop capture/speech.
                    await _runtime.EndAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.SessionDisconnected, "End was cancelled before the runtime drained.", "SessionID", "EndSession", true));
                }
                catch (Exception exception)
                {
                    return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.UnknownFailure, exception.Message, "SessionID", "EndSession", true));
                }
            }

            if (_session is { } session && session.State is not MultimodalSessionState.Ended)
            {
                var ended = await TransitionSessionAsync(session, MultimodalSessionState.Ended, cancellationToken).ConfigureAwait(false);
                if (!ended)
                    return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.UnknownFailure,
                        "The session could not be transitioned to Ended; the session state is preserved.", "SessionID", "EndSession", true));
            }

            _session = null;
            _acceptsRuntimeEvents = false;
            _audioLevel = 0;
            _muted = false;
            _paused = false;
            _visualSources = [];
            _permissionNotice = string.Empty;
            OnStateChanged();
            return VoiceVisionOperation.Ok;
        }
        finally
        {
            _startGate.Release();
            BusyLeave();
        }
    }

    public async Task<VoiceVisionOperation> SetMutedAsync(bool muted, CancellationToken cancellationToken)
    {
        if (!_runtime.IsActive) return VoiceVisionOperation.Ok;
        try
        {
            await _runtime.SetMutedAsync(muted, cancellationToken).ConfigureAwait(false);
            _muted = muted;
            OnStateChanged();
            return VoiceVisionOperation.Ok;
        }
        catch (OperationCanceledException) { return VoiceVisionOperation.Failed(OperationCancelled("SetMuted")); }
        catch (Exception exception) { return VoiceVisionOperation.Failed(UnknownFailure("SetMuted", exception)); }
    }

    public async Task<VoiceVisionOperation> PauseAsync(CancellationToken cancellationToken)
    {
        if (!_runtime.IsActive) return VoiceVisionOperation.Ok;
        try
        {
            await _runtime.PauseAsync(cancellationToken).ConfigureAwait(false);
            _paused = true;
            OnStateChanged();
            return VoiceVisionOperation.Ok;
        }
        catch (OperationCanceledException) { return VoiceVisionOperation.Failed(OperationCancelled("Pause")); }
        catch (Exception exception) { return VoiceVisionOperation.Failed(UnknownFailure("Pause", exception)); }
    }

    public async Task<VoiceVisionOperation> ResumeAsync(CancellationToken cancellationToken)
    {
        if (!_runtime.IsActive) return VoiceVisionOperation.Ok;
        try
        {
            await _runtime.ResumeAsync(cancellationToken).ConfigureAwait(false);
            _paused = false;
            OnStateChanged();
            return VoiceVisionOperation.Ok;
        }
        catch (OperationCanceledException) { return VoiceVisionOperation.Failed(OperationCancelled("Resume")); }
        catch (Exception exception) { return VoiceVisionOperation.Failed(UnknownFailure("Resume", exception)); }
    }

    public async Task<VoiceVisionOperation> InterruptAsync(CancellationToken cancellationToken)
    {
        if (!_runtime.IsActive || _paused) return VoiceVisionOperation.Ok;
        try
        {
            await _runtime.InterruptAsync(cancellationToken).ConfigureAwait(false);
            return VoiceVisionOperation.Ok;
        }
        catch (OperationCanceledException) { return VoiceVisionOperation.Failed(OperationCancelled("Interrupt")); }
        catch (Exception exception) { return VoiceVisionOperation.Failed(UnknownFailure("Interrupt", exception)); }
    }

    public async Task<VoiceVisionOperation> SubmitTextAsync(string text, CancellationToken cancellationToken)
    {
        if (!_runtime.IsActive) return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.InvalidRequest, "No active session accepts typed text.", "SessionID", "SendText", false));
        if (string.IsNullOrWhiteSpace(text)) return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.InvalidRequest, "Typed text is empty.", "Text", "SendText", false));
        try
        {
            await _runtime.SubmitTextAsync(text, cancellationToken).ConfigureAwait(false);
            return VoiceVisionOperation.Ok;
        }
        catch (OperationCanceledException) { return VoiceVisionOperation.Failed(OperationCancelled("SendText")); }
        catch (Exception exception) { return VoiceVisionOperation.Failed(UnknownFailure("SendText", exception)); }
    }

    public Task<VoiceVisionOperation> SetCameraAsync(bool enabled, CancellationToken cancellationToken)
    {
        // Camera capture NEVER starts from a voice/model toggle. It requires an explicit permitted
        // request AND a backend that supports camera capture. Otherwise zero capture starts.
        if (!enabled)
        {
            _visualSources = _visualSources.Where(source => source.Type != VisualSourceType.Camera).ToArray();
            OnStateChanged();
            return Task.FromResult(VoiceVisionOperation.Ok);
        }
        if (!_backend.SupportsCamera)
            return Task.FromResult(VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.CameraUnavailable,
                "Camera capture is not available in this build; no camera was started.", "Camera", "SetCamera", false)));
        return Task.FromResult(VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.PermissionRequired,
            "Camera capture requires explicit grant from the host permission broker.", "Camera", "SetCamera", false)));
    }

    public async Task<VoiceVisionOperation> SetScreenShareAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            try
            {
                if (_runtime.IsScreenSharing) await _runtime.StopScreenShareAsync(cancellationToken).ConfigureAwait(false);
                _visualSources = _visualSources.Where(source => source.Type is not (VisualSourceType.WindowShare or VisualSourceType.DisplayShare or VisualSourceType.ScreenRegion)).ToArray();
                OnStateChanged();
                return VoiceVisionOperation.Ok;
            }
            catch (OperationCanceledException) { return VoiceVisionOperation.Failed(OperationCancelled("StopScreenShare")); }
            catch (Exception exception) { return VoiceVisionOperation.Failed(UnknownFailure("StopScreenShare", exception)); }
        }

        if (!_runtime.Capabilities.CanShareScreen)
            return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.ScreenCapturePermissionDenied,
                "Screen sharing is unavailable or permission was denied; nothing was shared.", "ScreenShare", "SetScreenShare", false));
        if (_runtime.IsScreenSharing) return VoiceVisionOperation.Ok;
        try
        {
            await _runtime.StartScreenShareAsync(cancellationToken).ConfigureAwait(false);
            _visualSources = [new(Guid.NewGuid(), VisualSourceType.DisplayShare, "User-selected display", "Screen", VisualSourceState.Active, true, true, _time.GetUtcNow())];
            OnStateChanged();
            return VoiceVisionOperation.Ok;
        }
        catch (OperationCanceledException) { return VoiceVisionOperation.Failed(OperationCancelled("StartScreenShare")); }
        catch (Exception exception) { return VoiceVisionOperation.Failed(UnknownFailure("StartScreenShare", exception)); }
    }

    public Task<VoiceVisionOperation> SetCaptionsAsync(bool enabled, CancellationToken cancellationToken)
    {
        _captions = enabled;
        OnStateChanged();
        return Task.FromResult(VoiceVisionOperation.Ok);
    }

    public Task<VoiceVisionOperation> SetTranscriptVisibleAsync(bool visible, CancellationToken cancellationToken)
    {
        _transcriptVisible = visible;
        OnStateChanged();
        return Task.FromResult(VoiceVisionOperation.Ok);
    }

    public async Task<VoiceVisionOperation> SetModeAsync(VisionVoiceMode mode, CancellationToken cancellationToken)
    {
        if (_session is not { } session)
            return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.SessionNotFound, "No active session exists to change mode.", "SessionID", "SetMode", false));
        if (!IsModeSupported(mode))
            return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.ModelCapabilityMismatch,
                $"The selected backend does not run {mode} mode; the control is not operational.", "VoiceMode", "SetMode", false));
        try
        {
            var result = await _service.SetModeAsync(session.SessionId, session.Revision, mode, null, cancellationToken).ConfigureAwait(false);
            if (result.Error is { } error) return FailureFrom(error);
            _session = result.Value;
            OnStateChanged();
            return VoiceVisionOperation.Ok;
        }
        catch (OperationCanceledException) { return VoiceVisionOperation.Failed(OperationCancelled("SetMode")); }
        catch (Exception exception) { return VoiceVisionOperation.Failed(UnknownFailure("SetMode", exception)); }
    }

    public async Task<VoiceVisionOperation> SetRetentionAsync(bool retainAudio, bool retainTranscript, CancellationToken cancellationToken)
    {
        if (_session is not { } session)
            return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.SessionNotFound, "No active session exists to change retention.", "SessionID", "SetRetention", false));
        try
        {
            var result = await _service.SetRetentionAsync(session.SessionId, session.Revision, retainAudio, retainTranscript, userExplicitlyOptedIn: retainAudio || retainTranscript, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess) return FailureFrom(result.Error!);
            _session = result.Value;
            OnStateChanged();
            return VoiceVisionOperation.Ok;
        }
        catch (OperationCanceledException) { return VoiceVisionOperation.Failed(OperationCancelled("SetRetention")); }
        catch (Exception exception) { return VoiceVisionOperation.Failed(UnknownFailure("SetRetention", exception)); }
    }

    public async Task<VoiceVisionOperation> SetLiveTranslateLanguagesAsync(LiveTranslateLanguageSet languages, CancellationToken cancellationToken)
    {
        if (_session is not { } session)
            return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.SessionNotFound, "No active session exists to change translation languages.", "SessionID", "SetLiveTranslateLanguages", false));
        if (!_backend.SupportsLiveTranslate)
            return VoiceVisionOperation.Failed(new(VisionVoiceErrorCode.LiveTranslateCapabilityUnavailable,
                "Live Translate is not available in this build design nothing was interpreted.", "VoiceMode", "SetLiveTranslateLanguages", false));
        try
        {
            var result = await _service.SetModeAsync(session.SessionId, session.Revision, VisionVoiceMode.LiveTranslate, languages, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess) return FailureFrom(result.Error!);
            _session = result.Value;
            OnStateChanged();
            return VoiceVisionOperation.Ok;
        }
        catch (OperationCanceledException) { return VoiceVisionOperation.Failed(OperationCancelled("SetLiveTranslateLanguages")); }
        catch (Exception exception) { return VoiceVisionOperation.Failed(UnknownFailure("SetLiveTranslateLanguages", exception)); }
    }

    /// <summary>Sets which surface reflects the session without altering runtime state.</summary>
    public void SetPresentation(VoiceVisionPresentationKind presentation)
    {
        if (_presentation == presentation) return;
        _presentation = presentation;
        OnStateChanged();
    }

    private async Task<VoiceVisionStartResult?> TryAttachExistingAsync(VoiceVisionStartRequest request, CancellationToken cancellationToken)
    {
        if (!_runtime.IsActive || _runtime.CurrentConversation is not { } live || live.Id != request.ConversationId)
            return null;

        var sessionId = await GetCanonicalSessionIdAsync(request.ConversationId, cancellationToken).ConfigureAwait(false);
        var session = sessionId is { } id ? await ReadSessionAsync(id, cancellationToken).ConfigureAwait(false) : null;
        if (session is null)
        {
            // We are joining an already-live runtime for this conversation. Create the canonical
            // metadata ONCE so every surface shares one SessionID; do not call Start again.
            var created = await _service.StartAsync(request.ConversationId, request.SpaceId, request.Mode,
                request.EffectiveModelPolicy, request.IsLocalOnly, request.Languages, cancellationToken).ConfigureAwait(false);
            if (created.Error is { } createError)
                return VoiceVisionStartResult.Failure(createError);
            // A success result must actually carry a session payload; without one, no identity or
            // runtime effect may be published.
            if (created.Value is not { } established)
                return VoiceVisionStartResult.Failure(new(VisionVoiceErrorCode.UnknownFailure,
                    "The server returned success without a session; nothing was attached.", "SessionID", "StartSession", true));
            session = established;
            await RememberCanonicalSessionAsync(request.ConversationId, session.SessionId, cancellationToken).ConfigureAwait(false);
        }

        _session = session;
        _acceptsRuntimeEvents = true;
        _paused = _runtime.State == CallState.Paused;
        _muted = _runtime.IsMuted;
        OnStateChanged();
        return VoiceVisionStartResult.Success(session.SessionId, attachedExisting: true);
    }

    private async Task<VoiceVisionStartResult> StartNewAsync(VoiceVisionStartRequest request, CancellationToken cancellationToken)
    {
        if (!IsModeSupported(request.Mode))
            return VoiceVisionStartResult.Failure(new(VisionVoiceErrorCode.ModelCapabilityMismatch,
                $"The selected backend does not run {request.Mode} mode; the session was not started.", "VoiceMode", "StartSession", false));

        var created = await _service.StartAsync(request.ConversationId, request.SpaceId, request.Mode,
            request.EffectiveModelPolicy, request.IsLocalOnly, request.Languages, cancellationToken).ConfigureAwait(false);
        if (created.Error is { } createError)
            return VoiceVisionStartResult.Failure(createError);
        if (created.Value is not { } session)
            return VoiceVisionStartResult.Failure(new(VisionVoiceErrorCode.UnknownFailure,
                "The server returned success without a session; the session was not started.", "SessionID", "StartSession", true));

        try
        {
            // Reserve the canonical SessionID before any external work/callback so a concurrent
            // End/Dispose cannot observe this interaction as unknown while the runtime starts.
            await RememberCanonicalSessionAsync(request.ConversationId, session.SessionId, cancellationToken).ConfigureAwait(false);

            var runtimeOptions = request.RuntimeOptions with { ConversationId = request.ConversationId };
            var callSession = await _runtime.StartAsync(runtimeOptions, null, cancellationToken).ConfigureAwait(false);
            _session = session;
            _acceptsRuntimeEvents = true;
            _muted = false;
            _paused = false;
            OnStateChanged();
            _ = callSession;
            return VoiceVisionStartResult.Success(session.SessionId, attachedExisting: false);
        }
        catch (OperationCanceledException)
        {
            await MarkFailedAsync(session, "Start was cancelled before the runtime began.", cancellationToken).ConfigureAwait(false);
            return VoiceVisionStartResult.Failure(new(VisionVoiceErrorCode.SessionDisconnected, "Start was cancelled.", "SessionID", "StartSession", true));
        }
        catch (Exception exception)
        {
            await MarkFailedAsync(session, exception.Message, cancellationToken).ConfigureAwait(false);
            return VoiceVisionStartResult.Failure(new(VisionVoiceErrorCode.UnknownFailure, exception.Message, "SessionID", "StartSession", true));
        }
    }

    private async Task MarkFailedAsync(MultimodalSession session, string message, CancellationToken cancellationToken)
    {
        var failed = await _service.TransitionAsync(session.SessionId, session.Revision, MultimodalSessionState.Failed, cancellationToken).ConfigureAwait(false);
        if (failed.IsSuccess) _session = failed.Value;
        _permissionNotice = message;
        OnStateChanged();
    }

    private async Task<bool> TransitionSessionAsync(MultimodalSession session, MultimodalSessionState next, CancellationToken cancellationToken)
    {
        var result = await _service.TransitionAsync(session.SessionId, session.Revision, next, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && result.Value is { } updated)
        {
            _session = updated;
            return true;
        }
        return false;
    }

    private async Task<MultimodalSession?> ReadSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _service.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    private async Task<Guid?> GetCanonicalSessionIdAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (_sessionByConversation.TryGetValue(conversationId, out var cached)) return cached;
        if (_mappingStore is not null)
        {
            var mapped = await _mappingStore.GetAsync<SessionMapping>(SessionByConversationKey(conversationId), cancellationToken).ConfigureAwait(false);
            if (mapped is not null)
            {
                _sessionByConversation[conversationId] = mapped.SessionId;
                return mapped.SessionId;
            }
        }
        return null;
    }

    private async Task RememberCanonicalSessionAsync(Guid conversationId, Guid sessionId, CancellationToken cancellationToken)
    {
        _sessionByConversation[conversationId] = sessionId;
        if (_mappingStore is not null)
            await _mappingStore.SetAsync(SessionByConversationKey(conversationId), new SessionMapping(sessionId), cancellationToken).ConfigureAwait(false);
    }

    private static string SessionByConversationKey(Guid conversationId) => SessionByConversationKeyPrefix + conversationId.ToString("N");

    private VoiceVisionRuntimeState BuildState()
    {
        var session = _session;
        var isActive = _acceptsRuntimeEvents && _runtime.IsActive;
        var callState = _runtime.State;
        var paused = _paused || callState == CallState.Paused;
        var muted = _muted || _runtime.IsMuted;
        var listening = isActive && !paused && callState is CallState.Listening;
        var speaking = isActive && !paused && callState == CallState.Speaking;
        var processing = isActive && !paused && callState is CallState.Transcribing or CallState.Thinking;
        var failed = callState == CallState.Error;
        var disconnected = isActive && callState is CallState.Error;

        var mode = session?.VoiceMode ?? VisionVoiceMode.Conversational;
        var modeNames = ModeNames();
        var modeIndex = Math.Max(0, Array.IndexOf(modeNames, mode.ToString()));

        var voices = _runtime.Capabilities.Voices;
        var voiceNames = voices.Select(voice => voice.Name).ToArray();
        var selectedVoice = string.IsNullOrEmpty(_runtime.CurrentSession?.VoiceName) ? null : _runtime.CurrentSession!.VoiceName;
        var voiceIndex = selectedVoice is null ? -1 : Array.FindIndex(voiceNames, name => string.Equals(name, selectedVoice, StringComparison.OrdinalIgnoreCase));

        var canShare = _backend.SupportsScreenShare && _runtime.Capabilities.CanShareScreen;
        var stateLabel = failed ? "Failed"
            : disconnected ? "Reconnecting"
            : !isActive ? "Ready"
            : paused ? "Paused"
            : processing ? "Processing"
            : speaking ? "Speaking"
            : listening ? "Listening"
            : "Active";

        return new VoiceVisionRuntimeState(
            SessionId: session?.SessionId ?? Guid.Empty,
            ConversationId: session?.ConversationId ?? _runtime.CurrentConversation?.Id,
            SpaceId: session?.SpaceId,
            Mode: mode,
            State: session?.State ?? (isActive ? MultimodalSessionState.Listening : MultimodalSessionState.Stopped),
            Presentation: _presentation,
            IsActive: isActive,
            IsMuted: muted,
            IsPaused: paused,
            IsListening: listening,
            IsSpeaking: speaking,
            IsProcessing: processing,
            IsReconnecting: disconnected,
            IsFailed: failed,
            AudioLevel: _audioLevel,
            StatusText: stateLabel,
            PermissionNotice: _permissionNotice,
            IsLocalOnly: session?.IsLocalOnly ?? false,
            EffectiveModelPolicy: session?.EffectiveModelPolicy ?? string.Empty,
            VoiceModelId: session?.VoiceModelId,
            VoiceId: session?.VoiceId,
            SelectedVoiceName: selectedVoice,
            ModeLabel: mode.ToString(),
            VoiceNames: voiceNames,
            SelectedVoiceIndex: voiceIndex,
            ModeNames: modeNames,
            SelectedModeIndex: modeIndex,
            CanStart: !isActive && _runtime.Capabilities.HasSpeechInput,
            CanEnd: isActive,
            CanShareScreen: canShare,
            CanUseCamera: _backend.SupportsCamera,
            CanEnableCaptions: true,
            IsScreenSharing: _runtime.IsScreenSharing,
            HasCameraSource: _visualSources.Any(source => source.Type == VisualSourceType.Camera),
            AreCaptionsEnabled: _captions,
            IsTranscriptVisible: _transcriptVisible,
            VisualSources: _visualSources);
    }

    private string[] ModeNames()
    {
        var names = new List<string>();
        if (_backend.SupportsConversational) names.Add(VisionVoiceMode.Conversational.ToString());
        if (_backend.SupportsMonologue) names.Add(VisionVoiceMode.Monologue.ToString());
        if (_backend.SupportsLiveListener) names.Add(VisionVoiceMode.LiveListener.ToString());
        if (_backend.SupportsLiveTranslate) names.Add(VisionVoiceMode.LiveTranslate.ToString());
        if (names.Count == 0) names.Add(VisionVoiceMode.Conversational.ToString());
        return names.ToArray();
    }

    private bool IsModeSupported(VisionVoiceMode mode) => mode switch
    {
        VisionVoiceMode.Conversational => _backend.SupportsConversational,
        VisionVoiceMode.Monologue => _backend.SupportsMonologue,
        VisionVoiceMode.LiveListener => _backend.SupportsLiveListener,
        VisionVoiceMode.LiveTranslate => _backend.SupportsLiveTranslate,
        _ => false
    };

    private void WireRuntime()
    {
        if (_wired) return;
        _wired = true;
        _runtime.StateChanged += OnRuntimeStateChanged;
        _runtime.AudioLevelChanged += OnAudioLevelChanged;
        if (_runtime is IVoiceInputStatusSource inputSource)
            inputSource.InputStatusChanged += OnInputStatusChanged;
    }

    private void UnwireRuntime()
    {
        if (!_wired) return;
        _wired = false;
        _runtime.StateChanged -= OnRuntimeStateChanged;
        _runtime.AudioLevelChanged -= OnAudioLevelChanged;
        if (_runtime is IVoiceInputStatusSource inputSource)
            inputSource.InputStatusChanged -= OnInputStatusChanged;
    }

    private void OnRuntimeStateChanged(object? sender, CallStateChangedEventArgs args)
    {
        if (!_acceptsRuntimeEvents) return;
        if (_runtime.State == CallState.Idle)
        {
            _paused = false;
            if (_session is { } session && session.State is not MultimodalSessionState.Ended)
                _ = TransitionSessionAsync(session, MultimodalSessionState.Stopped, CancellationToken.None);
            _audioLevel = 0;
        }
        else if (_runtime.State == CallState.Error)
        {
            _permissionNotice = args.Status;
            if (_session is { } failedSession)
                _ = MarkFailedAsync(failedSession, args.Status, CancellationToken.None);
        }
        OnStateChanged();
    }

    private void OnAudioLevelChanged(object? sender, CallAudioLevelEventArgs args)
    {
        if (!_acceptsRuntimeEvents) return;
        _audioLevel = Math.Clamp(args.Level, 0, 1);
        OnStateChanged();
    }

    private void OnInputStatusChanged(object? sender, VoiceInputStatusChangedEventArgs args)
    {
        if (!_acceptsRuntimeEvents) return;
        _permissionNotice = args.Status.State switch
        {
            VoiceInputState.PermissionDenied => "Microphone permission is denied.",
            VoiceInputState.Unavailable => args.Status.Message,
            VoiceInputState.Error => args.Status.Message,
            _ => string.Empty
        };
        OnStateChanged();
    }

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static VoiceVisionOperation FailureFrom(VisionVoiceError error) => VoiceVisionOperation.Failed(error);
    private static VisionVoiceError OperationCancelled(string action) =>
        new(VisionVoiceErrorCode.SessionDisconnected, "The operation was cancelled.", "SessionID", action, true);
    private static VisionVoiceError UnknownFailure(string action, Exception exception) =>
        new(VisionVoiceErrorCode.UnknownFailure, exception.Message, "SessionID", action, true);

    public async ValueTask DisposeAsync()
    {
        // Elect one real disposal original; every late caller joins the same teardown so a
        // disposed semaphore is never Released by a stale waiter.
        var completion = _disposalCompletion;
        if (completion is null)
        {
            var created = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            completion = Interlocked.CompareExchange(ref _disposalCompletion, created, null) ?? created;
        }

        if (Interlocked.CompareExchange(ref _disposalStarted, 1, 0) != 0)
        {
            await completion.Task.ConfigureAwait(false);
            return;
        }

        Exception? fault = null;
        try
        {
            // Seal admission synchronously before any await. New Starts/Ends fail fast and the
            // generation bump rejects anything already queued that has not yet run.
            Volatile.Write(ref _disposed, true);
            Interlocked.Increment(ref _generation);

            // Join the already-admitted original (the in-flight Start/End that still holds the
            // gate) before we release runtime resources.
            BusyEnter();
            try
            {
                await _startGate.WaitAsync().ConfigureAwait(false);
            }
            catch
            {
                BusyLeave();
                throw;
            }

            try
            {
                if (_runtime.IsActive)
                    await StopWithBoundedRetryAsync().ConfigureAwait(false);

                if (_session is { } session && session.State is not MultimodalSessionState.Ended)
                {
                    var ended = await TransitionSessionAsync(session, MultimodalSessionState.Ended, CancellationToken.None).ConfigureAwait(false);
                    if (!ended)
                        throw new InvalidOperationException(
                            "The session could not be transitioned to Ended; custody of the session is retained.");
                }

                _session = null;
                _acceptsRuntimeEvents = false;
                _audioLevel = 0;
                _muted = false;
                _paused = false;
                _visualSources = [];
                _permissionNotice = string.Empty;
                UnwireRuntime();
            }
            finally
            {
                _startGate.Release();
                BusyLeave();
            }

            // Wait for every queued/admitted operation (including cancelled borrowers) to drain
            // before disposing the gate, so no waiter can finally Release a disposed semaphore.
            await WaitForGateQuiescenceAsync().ConfigureAwait(false);
            _startGate.Dispose();
        }
        catch (Exception exception)
        {
            fault = exception;
        }

        if (fault is not null) completion.TrySetException(fault);
        else completion.TrySetResult(true);

        if (fault is not null) throw fault;
    }

    /// <summary>Stops the live runtime with a bounded number of genuine owner-contract attempts.
    /// A still-live runtime/capture retains custody and preserves the original fault identity.</summary>
    private async Task StopWithBoundedRetryAsync()
    {
        const int maxAttempts = 3;
        Exception? fault = null;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (!_runtime.IsActive) return;
            try
            {
                await _runtime.EndAsync(CancellationToken.None).ConfigureAwait(false);
                if (!_runtime.IsActive) return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                fault ??= exception;
            }
        }
        throw fault ?? new InvalidOperationException("The live Voice runtime could not be stopped; custody is retained.");
    }

    private void BusyEnter() => Interlocked.Increment(ref _gateBusy);

    private void BusyLeave()
    {
        if (Interlocked.Decrement(ref _gateBusy) == 0 &&
            Volatile.Read(ref _gateIdleTcs) is { } idle)
            idle.TrySetResult();
    }

    private async Task WaitForGateQuiescenceAsync()
    {
        if (Volatile.Read(ref _gateBusy) == 0) return;
        var idle = _gateIdleTcs;
        if (idle is null)
        {
            idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            idle = Interlocked.CompareExchange(ref _gateIdleTcs, idle, null) ?? idle;
            if (Volatile.Read(ref _gateBusy) == 0)
                idle.TrySetResult();
        }
        await idle.Task.ConfigureAwait(false);
    }

    /// <summary>Durable conversation→canonical SessionID mapping kept in the shared settings store.</summary>
    private sealed record SessionMapping(Guid SessionId)
    {
        public static SessionMapping Parse(string json) => JsonSerializer.Deserialize<SessionMapping>(json)!;
    }
}
