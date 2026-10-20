using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Call;
using Haven.Core;

namespace Haven.Desktop.Views.Shell.VoiceVision;

/// <summary>
/// Host adapter that owns the CUI loader and the shared Voice &amp; Vision runtime facade.
/// It renders the authored pill or full session document into an Avalonia control tree,
/// wires the binding/action context, and translates every `voice.*` command into a typed
/// call on <see cref="IVoiceVisionRuntime"/>. The same adapter instance is shared by the
/// pill and the full surface so both drive one canonical session.
///
/// Ownership rules: opening a second surface reuses the same runtime (never a second run),
/// and closing a presentation is NOT End. End is only the explicit End control.
/// </summary>
public sealed class VoiceVisionHostAdapter : IDisposable
{
    private readonly IVoiceVisionRuntime _runtime;
    private readonly VoiceVisionCuiBindings _bindings;
    private readonly CuiControlLoader _loader;
    private bool _disposed;

    public VoiceVisionHostAdapter(
        IVoiceVisionRuntime runtime,
        Func<Guid?>? conversationProvider = null,
        Func<CallStartOptions>? startOptionsProvider = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ConversationProvider = conversationProvider;
        StartOptionsProvider = startOptionsProvider;
        _bindings = new VoiceVisionCuiBindings(() => _runtime.State, DispatchCommandAsync);
        _loader = new CuiControlLoader(VoiceVisionCuiDocument.CreateControlRegistry());
        _loader.SetBindingContext(_bindings);
        _loader.SetActionDispatcher(_bindings);
        _runtime.StateChanged += OnRuntimeStateChanged;
    }

    /// <summary>Provides the canonical ConversationID a new session should attach to.</summary>
    public Func<Guid?>? ConversationProvider { get; }

    /// <summary>Provides production call start options (model, devices, voice profile) for a new session.</summary>
    public Func<CallStartOptions>? StartOptionsProvider { get; }

    /// <summary>Raised when the UI requests the full Voice &amp; Vision surface.</summary>
    public event Action? RequestFullSurface;

    /// <summary>Raised when the presentation is closed (pill/full overlay dismissed). Closing a
    /// presentation is never End; this is the host's close-ACK signal so it can detach the
    /// overlay AFTER the shared projection has returned to no surface.</summary>
    public event Action? PresentationClosed;

    public Control LoadPill()
    {
        _runtime.SetPresentation(VoiceVisionPresentationKind.Pill);
        return LoadDocument(VoiceVisionCuiDocument.ParsePill());
    }

    public Control LoadSession()
    {
        _runtime.SetPresentation(VoiceVisionPresentationKind.Full);
        return LoadDocument(VoiceVisionCuiDocument.ParseSession());
    }

    /// <summary>Points the projection at no surface. Closing a presentation is never End. The
    /// close-ACK (<see cref="PresentationClosed"/>) is raised only after the projection has
    /// left the surface, so the host detaches the overlay thereafter.</summary>
    public void ClosePresentation()
    {
        _runtime.SetPresentation(VoiceVisionPresentationKind.None);
        PresentationClosed?.Invoke();
    }

    private Control LoadDocument(CuiDocument document)
    {
        var loaded = _loader.TryLoad(document);
        if (loaded.Root is null)
            throw new InvalidOperationException("Voice &amp; Vision CUI returned no control.");
        _loader.WireBindings(loaded.Root);
        return loaded.Root;
    }

    private async ValueTask DispatchCommandAsync(string command, object? parameter, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case "voice.start":
                await StartFromCommandAsync(parameter, cancellationToken);
                break;

            case "voice.end":
                await _runtime.EndAsync(cancellationToken);
                break;

            case "voice.mute":
                await _runtime.SetMutedAsync(!_runtime.State.IsMuted, cancellationToken);
                break;

            case "voice.pause-resume":
                if (_runtime.State.IsPaused) await _runtime.ResumeAsync(cancellationToken);
                else await _runtime.PauseAsync(cancellationToken);
                break;

            case "voice.screen-share":
                await _runtime.SetScreenShareAsync(!_runtime.State.IsScreenSharing, cancellationToken);
                break;

            case "voice.camera":
                await _runtime.SetCameraAsync(!_runtime.State.HasCameraSource, cancellationToken);
                break;

            case "voice.captions":
                await _runtime.SetCaptionsAsync(!_runtime.State.AreCaptionsEnabled, cancellationToken);
                break;

            case "voice.toggle-transcript":
                await _runtime.SetTranscriptVisibleAsync(!_runtime.State.IsTranscriptVisible, cancellationToken);
                break;

            case "voice.set-mode":
                if (TryResolveMode(parameter, out var mode))
                    await _runtime.SetModeAsync(mode, cancellationToken);
                break;

            case "voice.open-full":
                RequestFullSurface?.Invoke();
                break;

            case "voice.submit-text":
                if (parameter is string text && !string.IsNullOrWhiteSpace(text))
                {
                    await _runtime.SubmitTextAsync(text, cancellationToken);
                    _bindings.ClearText();
                }
                break;

            default:
                throw new InvalidOperationException($"Voice &amp; Vision command '{command}' is not handled by the host.");
        }
    }

    private async Task StartFromCommandAsync(object? parameter, CancellationToken cancellationToken)
    {
        var conversationId = ConversationProvider?.Invoke() ?? Guid.Empty;
        if (conversationId == Guid.Empty) return;

        var mode = TryResolveMode(parameter, out var requested) ? requested : VisionVoiceMode.Conversational;
        var options = StartOptionsProvider?.Invoke() ?? DefaultStartOptions();
        var request = new VoiceVisionStartRequest(
            conversationId,
            SpaceId: null,
            mode,
            options,
            EffectivePolicy(),
            IsLocalOnly: true);

        await _runtime.StartAsync(request, cancellationToken);
    }

    private static bool TryResolveMode(object? parameter, out VisionVoiceMode mode)
    {
        mode = VisionVoiceMode.Conversational;
        switch (parameter)
        {
            case string name when Enum.TryParse<VisionVoiceMode>(name, ignoreCase: true, out var parsed):
                mode = parsed;
                return true;
            case int index:
                // The markup binds the Select's SelectedIndex; the host resolves it through the
                // projection's supported-mode list so an unavailable mode can never be chosen.
                return false;
            default:
                return false;
        }
    }

    private string EffectivePolicy()
    {
        var policy = _runtime.State.EffectiveModelPolicy;
        return string.IsNullOrWhiteSpace(policy) ? "local-default" : policy;
    }

    private static CallStartOptions DefaultStartOptions() => new(
        new ModelDescriptor(
            "default", 0, "default", string.Empty, string.Empty,
            new HashSet<ToolCapability>(), DateTimeOffset.UtcNow),
        InputMode: CallInputMode.HandsFree);

    private void OnRuntimeStateChanged(object? sender, EventArgs args) => _bindings.Refresh();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtime.StateChanged -= OnRuntimeStateChanged;
        _loader.Dispose();
    }
}
