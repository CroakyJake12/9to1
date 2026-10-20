using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CakeOS.Cui.Runtime;
using Haven.Application.Call;

namespace Haven.Desktop.Views.Shell.VoiceVision;

/// <summary>
/// CUI binding owner for the Voice &amp; Vision pill and full session documents.
///
/// The authored surfaces only read projected values and raise commands. This class publishes
/// the current <see cref="VoiceVisionRuntimeState"/> projection and dispatches every action
/// back through the host to the shared <see cref="IVoiceVisionRuntime"/>, so the CUI document
/// never calls runtime methods directly and the host never substitutes a legacy control for
/// the authored surface. Help is capability-checked: nothing here reports a mode, device or
/// capture as operational unless the projection says it can be.
/// </summary>
public sealed class VoiceVisionCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private static readonly IReadOnlySet<string> KnownCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "voice.start", "voice.end", "voice.mute", "voice.pause-resume", "voice.screen-share",
        "voice.camera", "voice.captions", "voice.open-full", "voice.return", "voice.toggle",
        "voice.hide", "voice.set-mode", "voice.submit-text", "voice.toggle-transcript"
    };

    private readonly Func<VoiceVisionRuntimeState> _state;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private VoiceVisionRuntimeState _snapshot = VoiceVisionRuntimeState.Empty;
    private int _selectedModeIndex = -1;
    private string _textInput = string.Empty;

    public VoiceVisionCuiBindings(
        Func<VoiceVisionRuntimeState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Rebuilds the published projection from the shared runtime state.</summary>
    public void Refresh()
    {
        _snapshot = _state();
        if (_selectedModeIndex < 0 || _selectedModeIndex >= _snapshot.ModeNames.Count)
            _selectedModeIndex = Math.Clamp(_snapshot.SelectedModeIndex, 0, Math.Max(0, _snapshot.ModeNames.Count - 1));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>Clears the typed footer text after a successful send.</summary>
    public void ClearText()
    {
        if (_textInput.Length == 0) return;
        _textInput = string.Empty;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("session.textInput"));
    }

    public bool TryGetValue(string path, out object? value)
    {
        var snapshot = _snapshot;
        value = path switch
        {
            _ when path.Equals("pill.statusText", StringComparison.OrdinalIgnoreCase) => snapshot.StatusText,
            _ when path.Equals("pill.modeLabel", StringComparison.OrdinalIgnoreCase) => snapshot.ModeLabel,
            _ when path.Equals("pill.isListening", StringComparison.OrdinalIgnoreCase) => snapshot.IsListening,
            _ when path.Equals("pill.canStart", StringComparison.OrdinalIgnoreCase) => snapshot.CanStart,
            _ when path.Equals("pill.canEnd", StringComparison.OrdinalIgnoreCase) => snapshot.CanEnd,
            _ when path.Equals("pill.canToggleMute", StringComparison.OrdinalIgnoreCase) => snapshot.IsActive,
            _ when path.Equals("pill.canPause", StringComparison.OrdinalIgnoreCase) => snapshot.IsActive,
            _ when path.Equals("pill.canShareScreen", StringComparison.OrdinalIgnoreCase) => snapshot.CanShareScreen,
            _ when path.Equals("pill.canUseCamera", StringComparison.OrdinalIgnoreCase) => snapshot.CanUseCamera,
            _ when path.Equals("pill.canEnableCaptions", StringComparison.OrdinalIgnoreCase) => true,
            _ when path.Equals("pill.canOpenFull", StringComparison.OrdinalIgnoreCase) => true,
            _ when path.Equals("pill.muteOpacity", StringComparison.OrdinalIgnoreCase) => snapshot.IsMuted ? 0.35 : 1.0,
            _ when path.Equals("pill.isLocalOnly", StringComparison.OrdinalIgnoreCase) => snapshot.IsLocalOnly,
            _ when path.Equals("pill.permissionNotice", StringComparison.OrdinalIgnoreCase) => snapshot.PermissionNotice,
            _ when path.Equals("pill.hasPermissionNotice", StringComparison.OrdinalIgnoreCase) => snapshot.PermissionNotice.Length > 0,
            _ when path.Equals("pill.modeNames", StringComparison.OrdinalIgnoreCase) => snapshot.ModeNames,
            _ when path.Equals("pill.selectedModeIndex", StringComparison.OrdinalIgnoreCase) => _selectedModeIndex,

            _ when path.Equals("session.statusText", StringComparison.OrdinalIgnoreCase) => snapshot.StatusText,
            _ when path.Equals("session.modeLabel", StringComparison.OrdinalIgnoreCase) => snapshot.ModeLabel,
            _ when path.Equals("session.isListening", StringComparison.OrdinalIgnoreCase) => snapshot.IsListening,
            _ when path.Equals("session.isActive", StringComparison.OrdinalIgnoreCase) => snapshot.IsActive,
            _ when path.Equals("session.canStart", StringComparison.OrdinalIgnoreCase) => snapshot.CanStart,
            _ when path.Equals("session.canEnd", StringComparison.OrdinalIgnoreCase) => snapshot.CanEnd,
            _ when path.Equals("session.canToggleMute", StringComparison.OrdinalIgnoreCase) => snapshot.IsActive,
            _ when path.Equals("session.canPause", StringComparison.OrdinalIgnoreCase) => snapshot.IsActive,
            _ when path.Equals("session.canShareScreen", StringComparison.OrdinalIgnoreCase) => snapshot.CanShareScreen,
            _ when path.Equals("session.canUseCamera", StringComparison.OrdinalIgnoreCase) => snapshot.CanUseCamera,
            _ when path.Equals("session.canEnableCaptions", StringComparison.OrdinalIgnoreCase) => true,
            _ when path.Equals("session.canSendText", StringComparison.OrdinalIgnoreCase) => snapshot.IsActive,
            _ when path.Equals("session.muteOpacity", StringComparison.OrdinalIgnoreCase) => snapshot.IsMuted ? 0.35 : 1.0,
            _ when path.Equals("session.isLocalOnly", StringComparison.OrdinalIgnoreCase) => snapshot.IsLocalOnly,
            _ when path.Equals("session.permissionNotice", StringComparison.OrdinalIgnoreCase) => snapshot.PermissionNotice,
            _ when path.Equals("session.hasPermissionNotice", StringComparison.OrdinalIgnoreCase) => snapshot.PermissionNotice.Length > 0,
            _ when path.Equals("session.isScreenSharing", StringComparison.OrdinalIgnoreCase) => snapshot.IsScreenSharing,
            _ when path.Equals("session.hasCameraSource", StringComparison.OrdinalIgnoreCase) => snapshot.HasCameraSource,
            _ when path.Equals("session.areCaptionsEnabled", StringComparison.OrdinalIgnoreCase) => snapshot.AreCaptionsEnabled,
            _ when path.Equals("session.isTranscriptVisible", StringComparison.OrdinalIgnoreCase) => snapshot.IsTranscriptVisible,
            _ when path.Equals("session.transcriptToggleLabel", StringComparison.OrdinalIgnoreCase) => snapshot.IsTranscriptVisible ? "Hide" : "Show",
            _ when path.Equals("session.modeNames", StringComparison.OrdinalIgnoreCase) => snapshot.ModeNames,
            _ when path.Equals("session.selectedModeIndex", StringComparison.OrdinalIgnoreCase) => _selectedModeIndex,
            _ when path.Equals("session.selectedVoiceName", StringComparison.OrdinalIgnoreCase) => string.IsNullOrEmpty(snapshot.SelectedVoiceName) ? "Default voice" : snapshot.SelectedVoiceName,
            _ when path.Equals("session.effectiveModelPolicy", StringComparison.OrdinalIgnoreCase) => string.IsNullOrEmpty(snapshot.EffectiveModelPolicy) ? "default" : snapshot.EffectiveModelPolicy,
            _ when path.Equals("session.audioPercent", StringComparison.OrdinalIgnoreCase) => ((int)Math.Round(Math.Clamp(snapshot.AudioLevel, 0, 1) * 100)).ToString(System.Globalization.CultureInfo.CurrentCulture) + "%",
            _ when path.Equals("session.visualSources", StringComparison.OrdinalIgnoreCase) => snapshot.VisualSources,
            _ when path.Equals("session.hasNoVisualSources", StringComparison.OrdinalIgnoreCase) => snapshot.VisualSources.Count == 0,
            _ when path.Equals("session.textInput", StringComparison.OrdinalIgnoreCase) => _textInput,

            _ => Unresolved
        };
        return !ReferenceEquals(value, Unresolved);
    }

    public bool TrySetValue(string path, object? value)
    {
        if (path.Equals("session.textInput", StringComparison.OrdinalIgnoreCase)
            || path.Equals("pill.textInput", StringComparison.OrdinalIgnoreCase))
        {
            _textInput = value as string ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(path));
            return true;
        }

        if (path.Equals("pill.selectedModeIndex", StringComparison.OrdinalIgnoreCase)
            || path.Equals("session.selectedModeIndex", StringComparison.OrdinalIgnoreCase))
        {
            if (value is not int index) return false;
            _selectedModeIndex = index;
            var modeName = index >= 0 && index < _snapshot.ModeNames.Count ? _snapshot.ModeNames[index] : null;
            if (modeName is not null)
                _ = _dispatch("voice.set-mode", modeName, CancellationToken.None);
            return true;
        }

        return false;
    }

    public bool? IsActionAvailable(string command)
    {
        if (!KnownCommands.Contains(command)) return null;
        var snapshot = _snapshot;
        return command switch
        {
            "voice.start" => snapshot.CanStart,
            "voice.end" => snapshot.CanEnd,
            "voice.mute" => snapshot.IsActive,
            "voice.pause-resume" => snapshot.IsActive,
            "voice.screen-share" => snapshot.IsScreenSharing || snapshot.CanShareScreen,
            "voice.camera" => snapshot.HasCameraSource || snapshot.CanUseCamera,
            "voice.captions" => true,
            "voice.open-full" => true,
            "voice.return" => true,
            "voice.toggle" => true,
            "voice.hide" => true,
            "voice.set-mode" => snapshot.CanEnd,
            "voice.submit-text" => snapshot.IsActive,
            "voice.toggle-transcript" => snapshot.CanEnd,
            _ => true
        };
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (!KnownCommands.Contains(command))
            throw new InvalidOperationException($"Voice &amp; Vision CUI command '{command}' is not owned by this binding.");

        if (command.Equals("voice.set-mode", StringComparison.OrdinalIgnoreCase))
            parameter ??= ResolveSelectedModeName();

        await _dispatch(command, parameter, cancellationToken);
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is not VisualSourceDescriptor source) return false;
        value = path switch
        {
            _ when path.Equals("displayName", StringComparison.OrdinalIgnoreCase) => source.DisplayName,
            _ when path.Equals("typeLabel", StringComparison.OrdinalIgnoreCase) => TypeLabel(source.Type),
            _ when path.Equals("stateLabel", StringComparison.OrdinalIgnoreCase) => StateLabel(source.State),
            _ => null
        };
        return value is not null;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    private string ResolveSelectedModeName() =>
        _selectedModeIndex >= 0 && _selectedModeIndex < _snapshot.ModeNames.Count
            ? _snapshot.ModeNames[_selectedModeIndex]
            : "Conversational";

    private static string TypeLabel(VisualSourceType type) => type switch
    {
        VisualSourceType.UploadedImage => "Uploaded image",
        VisualSourceType.Screenshot => "Screenshot",
        VisualSourceType.ScreenRegion => "Screen region",
        VisualSourceType.WindowShare => "Window share",
        VisualSourceType.DisplayShare => "Display share",
        VisualSourceType.Camera => "Camera",
        VisualSourceType.AppProvidedVisualContext => "App context",
        _ => type.ToString()
    };

    private static string StateLabel(VisualSourceState state) => state switch
    {
        VisualSourceState.Active => "Active",
        VisualSourceState.Paused => "Paused",
        VisualSourceState.Stopped => "Stopped",
        VisualSourceState.Unavailable => "Unavailable",
        _ => state.ToString()
    };

    private static readonly object Unresolved = new();
}
