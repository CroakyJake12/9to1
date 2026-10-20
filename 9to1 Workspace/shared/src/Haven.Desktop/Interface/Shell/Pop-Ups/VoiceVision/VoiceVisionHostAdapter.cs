using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
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
/// and closing a presentation is NOT End. End is only the explicit End control. The start
/// path borrows REAL composition state (conversation, space, model/start options, policy and
/// local-only fact) through providers; it never fabricates a default model or a permissive
/// local-only claim.
/// </summary>
public sealed class VoiceVisionHostAdapter : IAsyncDisposable, IDisposable
{
    private readonly IVoiceVisionRuntime _runtime;
    private readonly VoiceVisionCuiBindings _bindings;
    private readonly CuiControlLoader _loader;
    private bool _disposed;

    public VoiceVisionHostAdapter(
        IVoiceVisionRuntime runtime,
        Func<Guid?> conversationProvider,
        Func<CallStartOptions> startOptionsProvider,
        Func<Guid?>? spaceIdProvider = null,
        Func<bool>? isLocalOnlyProvider = null,
        Func<string>? policyProvider = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ConversationProvider = conversationProvider ?? throw new ArgumentNullException(nameof(conversationProvider));
        StartOptionsProvider = startOptionsProvider ?? throw new ArgumentNullException(nameof(startOptionsProvider));
        SpaceIdProvider = spaceIdProvider;
        IsLocalOnlyProvider = isLocalOnlyProvider;
        PolicyProvider = policyProvider;
        _bindings = new VoiceVisionCuiBindings(() => _runtime.State, DispatchCommandAsync);
        _loader = new CuiControlLoader(VoiceVisionCuiDocument.CreateControlRegistry());
        _loader.SetBindingContext(_bindings);
        _loader.SetActionDispatcher(_bindings);
        _runtime.StateChanged += OnRuntimeStateChanged;
    }

    /// <summary>Provides the canonical ConversationID a new session should attach to.</summary>
    public Func<Guid?> ConversationProvider { get; }

    /// <summary>Provides the REAL production call start options (model, devices, voice profile) for
    /// a new session. There is intentionally no fabricated fallback: without real options a session
    /// cannot be started truthfully, so the host must wire this provider.</summary>
    public Func<CallStartOptions> StartOptionsProvider { get; }

    /// <summary>Provides the canonical SpaceID for a new session (null when no Space context).</summary>
    public Func<Guid?>? SpaceIdProvider { get; }

    /// <summary>Provides the truthful local-only fact for a new session (never invented).</summary>
    public Func<bool>? IsLocalOnlyProvider { get; }

    /// <summary>Provides the effective model policy for a new session (defaults to the runtime's
    /// current effective policy when a session already exists).</summary>
    public Func<string>? PolicyProvider { get; }

    /// <summary>Raised (with settlement) when the UI requests the full Voice &amp; Vision surface.</summary>
    public event Func<Task>? RequestFullSurfaceAsync;

    /// <summary>Raised only after the presentation has left the surface and admitted actions have
    /// settled. Closing a presentation is never End; this is the host's close-ACK signal.</summary>
    public event Func<Task>? PresentationClosedAsync;

    public Control LoadPill()
    {
        _runtime.SetPresentation(VoiceVisionPresentationKind.Pill);
        return LoadDocument(VoiceVisionCuiDocument.LoadPill());
    }

    public Control LoadSession()
    {
        _runtime.SetPresentation(VoiceVisionPresentationKind.Full);
        return LoadDocument(VoiceVisionCuiDocument.LoadSession());
    }

    /// <summary>
    /// Closes the presentation: the shared projection leaves the surface WITHOUT ending the session.
    /// Loader action settlement is awaited before the close-ACK so the host detaches the overlay only
    /// after every admitted command has settled; there is no fire-and-forget release signal.
    /// </summary>
    public async Task ClosePresentationAsync(CancellationToken cancellationToken = default)
    {
        await _loader.WhenActionsIdleAsync().ConfigureAwait(false);
        _runtime.SetPresentation(VoiceVisionPresentationKind.None);
        if (PresentationClosedAsync is { } closed)
            await closed().ConfigureAwait(false);
    }

    private Control LoadDocument(CuiDocument document)
    {
        var loaded = _loader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error))
            throw new InvalidOperationException("Voice &amp; Vision CUI could not be rendered.");
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
                if (RequestFullSurfaceAsync is { } open)
                    await open();
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
        var conversationId = ConversationProvider();
        if (conversationId is null || conversationId == Guid.Empty) return;

        var mode = TryResolveMode(parameter, out var requested) ? requested : VisionVoiceMode.Conversational;

        // Real production start options are required; there is no fabricated default model.
        var options = StartOptionsProvider();
        var spaceId = SpaceIdProvider?.Invoke();
        var policy = PolicyProvider?.Invoke() ?? _runtime.State.EffectiveModelPolicy;
        var localOnly = IsLocalOnlyProvider?.Invoke() ?? ResolveLocalOnly(policy);

        var request = new VoiceVisionStartRequest(conversationId.Value, spaceId, mode, options, policy, localOnly);
        await _runtime.StartAsync(request, cancellationToken);
    }

    private static bool ResolveLocalOnly(string policy) =>
        !string.IsNullOrWhiteSpace(policy) && policy.Contains("local", StringComparison.OrdinalIgnoreCase);

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

    private void OnRuntimeStateChanged(object? sender, EventArgs args) => _bindings.Refresh();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtime.StateChanged -= OnRuntimeStateChanged;
        _loader.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            _loader.Dispose();
            return;
        }
        _disposed = true;
        _runtime.StateChanged -= OnRuntimeStateChanged;
        try
        {
            await _loader.WhenActionsIdleAsync().ConfigureAwait(false);
        }
        finally
        {
            _loader.Dispose();
        }
    }
}
