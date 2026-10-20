/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Call/IVoiceVisionRuntime.cs, in the Vision & Voice application contract.
 * What: This file declares the typed production contract that connects canonical Voice & Vision
 *       presentation to the live session/audio/capture runtime.
 * Why: The CUI pill and full surface dispatch real commands through this contract and read a
 *       truthful projection. There is no second app runtime and no hidden orchestration.
 * Maintenance: Keep the contract small and platform-neutral; the implementation adapts the shared
 *       ICallCoordinator and the canonical MultimodalSession store.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Haven.Application.Call;

/// <summary>Describes which Voice &amp; Vision modes the present backend actually runs, versus
/// merely having an enum option. Unsupported modes must surface as truthful unavailable state.</summary>
public sealed record VoiceVisionBackendProfile(
    bool SupportsConversational,
    bool SupportsMonologue,
    bool SupportsLiveListener,
    bool SupportsLiveTranslate,
    bool SupportsCamera,
    bool SupportsScreenShare,
    bool SupportsDeviceHotSwitch)
{
    /// <summary>Conservative default: only modes/capture the shared coordinator can genuinely run.</summary>
    public static VoiceVisionBackendProfile Default { get; } = new(
        SupportsConversational: true,
        SupportsMonologue: false,
        SupportsLiveListener: false,
        SupportsLiveTranslate: false,
        SupportsCamera: false,
        SupportsScreenShare: false,
        SupportsDeviceHotSwitch: true);
}

/// <summary>One authorized attempt to start, attach to or reuse a canonical Voice &amp; Vision session.</summary>
public sealed record VoiceVisionStartRequest(
    Guid ConversationId,
    Guid? SpaceId,
    VisionVoiceMode Mode,
    CallStartOptions RuntimeOptions,
    string EffectiveModelPolicy,
    bool IsLocalOnly,
    LiveTranslateLanguageSet? Languages = null);

/// <summary>The typed outcome of a start/attach attempt. AttachedExisting means the runtime was
/// reused for the same interaction and Start was NOT called again.</summary>
public sealed record VoiceVisionStartResult(
    Guid SessionId,
    bool AttachedExisting,
    VisionVoiceError? Error)
{
    public bool Succeeded => Error is null && SessionId != Guid.Empty;
    public static VoiceVisionStartResult Success(Guid sessionId, bool attachedExisting) => new(sessionId, attachedExisting, null);
    public static VoiceVisionStartResult Failure(VisionVoiceError error) => new(Guid.Empty, false, error);
}

/// <summary>A typed command outcome with an optional structured failure.</summary>
public readonly record struct VoiceVisionOperation(bool Succeeded, VisionVoiceError? Error = null)
{
    public static VoiceVisionOperation Ok => new(true, null);
    public static VoiceVisionOperation Failed(VisionVoiceError error) => new(false, error);
}

/// <summary>
/// Typed production contract for Voice &amp; Vision session control.
///
/// Ownership rule: one runtime singleton coordinates the ONE live coordinator. Presenting a second
/// surface reuses the same runtime for the same ConversationID; it never acquires a second runtime
/// and never calls Start twice for the same interaction.
/// </summary>
public interface IVoiceVisionRuntime
{
    /// <summary>The latest truthful projection of the shared session.</summary>
    VoiceVisionRuntimeState State { get; }

    /// <summary>Raised whenever the projection changes; CUI bindings refresh on this event.</summary>
    event EventHandler? StateChanged;

    /// <summary>Starts a new canonical session, or attaches to the existing live session for the same
    /// interaction without calling Start again. Concurrent starts for the same interaction coalesce.</summary>
    Task<VoiceVisionStartResult> StartAsync(VoiceVisionStartRequest request, CancellationToken cancellationToken);

    /// <summary>Stops the live runtime and drains it before finishing the canonical session. Closing a
    /// presentation is not End; End is an explicit runtime stop/drain.</summary>
    Task<VoiceVisionOperation> EndAsync(CancellationToken cancellationToken);

    Task<VoiceVisionOperation> SetMutedAsync(bool muted, CancellationToken cancellationToken);
    Task<VoiceVisionOperation> PauseAsync(CancellationToken cancellationToken);
    Task<VoiceVisionOperation> ResumeAsync(CancellationToken cancellationToken);
    Task<VoiceVisionOperation> InterruptAsync(CancellationToken cancellationToken);
    Task<VoiceVisionOperation> SubmitTextAsync(string text, CancellationToken cancellationToken);

    /// <summary>Camera capture only starts after an explicit permitted request. Never auto-starts
    /// from a voice/model toggle. A denied or unsupported backend yields zero capture starts.</summary>
    Task<VoiceVisionOperation> SetCameraAsync(bool enabled, CancellationToken cancellationToken);

    /// <summary>Screen/window sharing only starts after an explicit permitted request. Stopping
    /// sharing must stop new frames from reaching the runtime.</summary>
    Task<VoiceVisionOperation> SetScreenShareAsync(bool enabled, CancellationToken cancellationToken);

    Task<VoiceVisionOperation> SetCaptionsAsync(bool enabled, CancellationToken cancellationToken);
    Task<VoiceVisionOperation> SetTranscriptVisibleAsync(bool visible, CancellationToken cancellationToken);
    Task<VoiceVisionOperation> SetModeAsync(VisionVoiceMode mode, CancellationToken cancellationToken);
    Task<VoiceVisionOperation> SetRetentionAsync(bool retainAudio, bool retainTranscript, CancellationToken cancellationToken);
    Task<VoiceVisionOperation> SetLiveTranslateLanguagesAsync(LiveTranslateLanguageSet languages, CancellationToken cancellationToken);

    /// <summary>Sets which surface (pill / full / none) reflects the shared session without
    /// altering runtime state. Closing a presentation is never End.</summary>
    void SetPresentation(VoiceVisionPresentationKind presentation);
}
