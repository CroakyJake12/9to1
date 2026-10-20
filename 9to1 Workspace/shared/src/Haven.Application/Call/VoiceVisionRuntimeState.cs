/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Call/VoiceVisionRuntimeState.cs, in the Vision & Voice application contract.
 * What: This file owns the truthful read-model projection that the canonical Voice & Vision CUI
 *       presentation consumes. It is a typed snapshot: it never starts audio, camera or screen
 *       capture, never fabricates a model/voice/readiness value and never accepts a click as proof
 *       that a mode is operational.
 * Why: The compact voice pill and the full session surface must present one shared, truthful
 *       session state without inventing local presentation state or a second runtime.
 * Maintenance: Keep this projection derived from the live coordinator plus the canonical
 *       MultimodalSession store; do not add ad-hoc state here.
 */

using System;
using System.Collections.Generic;

namespace Haven.Application.Call;

/// <summary>Which canonical presentation surface is currently reflecting the shared session.</summary>
public enum VoiceVisionPresentationKind
{
    /// <summary>No Voice &amp; Vision surface is shown.</summary>
    None = 0,
    /// <summary>The compact floating voice pill is shown over the still-usable conversation.</summary>
    Pill = 1,
    /// <summary>The full session-first Voice &amp; Vision surface is shown.</summary>
    Full = 2
}

/// <summary>
/// Truthful current projection of one shared Voice &amp; Vision session, consumed by CUI bindings.
/// It combines the live runtime (audio/speech/capture states) with the canonical session metadata
/// (SessionID/ConversationID/SpaceID, mode, retention, visual sources) into one snapshot.
/// </summary>
public sealed record VoiceVisionRuntimeState(
    Guid SessionId,
    Guid? ConversationId,
    Guid? SpaceId,
    VisionVoiceMode Mode,
    MultimodalSessionState State,
    VoiceVisionPresentationKind Presentation,
    bool IsActive,
    bool IsMuted,
    bool IsPaused,
    bool IsListening,
    bool IsSpeaking,
    bool IsProcessing,
    bool IsReconnecting,
    bool IsFailed,
    double AudioLevel,
    string StatusText,
    string PermissionNotice,
    bool IsLocalOnly,
    string EffectiveModelPolicy,
    string? VoiceModelId,
    string? VoiceId,
    string? SelectedVoiceName,
    string ModeLabel,
    IReadOnlyList<string> VoiceNames,
    int SelectedVoiceIndex,
    IReadOnlyList<string> ModeNames,
    int SelectedModeIndex,
    bool CanStart,
    bool CanEnd,
    bool CanShareScreen,
    bool CanUseCamera,
    bool CanEnableCaptions,
    bool IsScreenSharing,
    bool HasCameraSource,
    bool AreCaptionsEnabled,
    bool IsTranscriptVisible,
    IReadOnlyList<VisualSourceDescriptor> VisualSources)
{
    /// <summary>Stable empty projection before a session exists.</summary>
    public static VoiceVisionRuntimeState Empty { get; } = new(
        SessionId: Guid.Empty,
        ConversationId: null,
        SpaceId: null,
        Mode: VisionVoiceMode.Conversational,
        State: MultimodalSessionState.Stopped,
        Presentation: VoiceVisionPresentationKind.None,
        IsActive: false,
        IsMuted: false,
        IsPaused: false,
        IsListening: false,
        IsSpeaking: false,
        IsProcessing: false,
        IsReconnecting: false,
        IsFailed: false,
        AudioLevel: 0,
        StatusText: "Ready",
        PermissionNotice: string.Empty,
        IsLocalOnly: false,
        EffectiveModelPolicy: string.Empty,
        VoiceModelId: null,
        VoiceId: null,
        SelectedVoiceName: null,
        ModeLabel: "Conversational",
        VoiceNames: [],
        SelectedVoiceIndex: -1,
        ModeNames: ["Conversational"],
        SelectedModeIndex: 0,
        CanStart: false,
        CanEnd: false,
        CanShareScreen: false,
        CanUseCamera: false,
        CanEnableCaptions: false,
        IsScreenSharing: false,
        HasCameraSource: false,
        AreCaptionsEnabled: false,
        IsTranscriptVisible: false,
        VisualSources: []);
}
