using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Application.Call;
using Haven.Core;

namespace Haven.Desktop.Views.Shell.VoiceVision;

/// <summary>
/// Genuine typed start-policy decision borrowed from a real authority: the effective model policy
/// string and the truthful local-only fact for a new Voice session. Both values must come from a
/// real typed source; this record is never synthesized from heuristic text.
/// </summary>
public sealed record VoiceSessionStartPolicy(string EffectivePolicy, bool IsLocalOnly);

/// <summary>
/// Authored presentation-navigation intent raised by the Voice &amp; Vision documents. The host (the
/// external owner) performs the actual mount/close; the adapter never loads a document from inside
/// an action whose settlement it owns (see <see cref="VoiceVisionHostAdapter.CaptureActionSettlement"/>).
/// </summary>
public enum VoicePresentationIntent
{
    /// <summary>Toggle the floating pill (show if hidden, hide if shown).</summary>
    TogglePill,

    /// <summary>Hide the shared presentation without ending the session.</summary>
    Hide,

    /// <summary>Open the full Voice &amp; Vision surface.</summary>
    OpenFull,

    /// <summary>Return from the full surface to the floating pill.</summary>
    ReturnToPill
}

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
/// local-only claim, and it never infers local-only from policy text.
///
/// Causal close/disposal ownership: the loader documents that an action must never await a
/// snapshot containing itself, so presentation close captures a FRESH settlement snapshot at
/// the actual close point and joins it OUTSIDE any dispatched action. Teardown reserves ONE
/// retained operation identity before any external callback runs, seals actual admission before
/// taking the final settlement snapshot, and then retires the runtime subscription and loader in
/// returned real post-Dispose cleanup settlement — never under the ownership lock, never missing a
/// later dispatch, and never performing reentrant double teardown. The shared singleton runtime is
/// never End/disposed here; explicit End is the only real stop, and only runtime subscriptions
/// owned by THIS adapter are retired after genuinely admitted work settles.
/// </summary>
public sealed class VoiceVisionHostAdapter : IAsyncDisposable, IDisposable
{
    private readonly IVoiceVisionRuntime _runtime;
    private readonly VoiceVisionCuiBindings _bindings;
    private readonly CuiControlLoader _loader;
    private readonly Func<VoiceSessionStartPolicy>? _policyAuthority;
    private readonly object _gate = new();
    private Task? _teardown;
    private long _loadGeneration;
    private volatile bool _retiring;

    public VoiceVisionHostAdapter(
        IVoiceVisionRuntime runtime,
        Func<Guid?> conversationProvider,
        Func<CallStartOptions> startOptionsProvider,
        Func<Guid?>? spaceIdProvider = null,
        Func<bool>? isLocalOnlyProvider = null,
        Func<string>? policyProvider = null,
        Func<VoiceSessionStartPolicy>? policyAuthority = null,
        CuiThemePreferenceContext? themePreference = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ConversationProvider = conversationProvider ?? throw new ArgumentNullException(nameof(conversationProvider));
        StartOptionsProvider = startOptionsProvider ?? throw new ArgumentNullException(nameof(startOptionsProvider));
        SpaceIdProvider = spaceIdProvider;
        IsLocalOnlyProvider = isLocalOnlyProvider;
        PolicyProvider = policyProvider;
        _policyAuthority = policyAuthority;
        _bindings = new VoiceVisionCuiBindings(() => _runtime.State, DispatchCommandAsync);
        _loader = new CuiControlLoader(VoiceVisionCuiDocument.CreateControlRegistry());
        _loader.SetBindingContext(_bindings);
        _loader.SetActionDispatcher(_bindings);
        // Borrow the genuine current Home theme before any compiled document load. The source owner
        // retains the context lifetime; the adapter only subscribes the loader (retired on teardown).
        if (themePreference is not null) _loader.SetThemePreferenceContext(themePreference);
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

    /// <summary>The current mount generation. Incremented on every pill/full load so hosts and
    /// stale-close/reopen checks verify they protect the ACTUAL loaded session, never an unused
    /// adapter epoch.</summary>
    public long MountGeneration
    {
        get { lock (_gate) return Interlocked.Read(ref _loadGeneration); }
    }

    /// <summary>Raised when an authored navigation command requests a presentation change. The host
    /// (external owner) accepts the typed intent and performs the actual mount/close; the adapter
    /// never loads a document from inside an action whose settlement it owns.</summary>
    public event Func<VoicePresentationIntent, Task>? PresentationIntentRequested;

    /// <summary>Raised only after the presentation has left the surface and admitted actions have
    /// settled. Closing a presentation is never End; this is the host's close-ACK signal.</summary>
    public event Func<Task>? PresentationClosedAsync;

    /// <summary>
    /// Captures the loader's CURRENT action-settlement snapshot. The external owner joins this
    /// OUTSIDE the dispatching action (for example after a yield) and only then replaces the
    /// presented surface, so a loader load never runs from inside the action whose settlement it
    /// owns and no admitted later work is missed.
    /// </summary>
    public Task CaptureActionSettlement() => _loader.WhenActionsIdleAsync();

    public Control LoadPill()
    {
        _runtime.SetPresentation(VoiceVisionPresentationKind.Pill);
        Interlocked.Increment(ref _loadGeneration);
        return LoadDocument(VoiceVisionCuiDocument.LoadPill());
    }

    public Control LoadSession()
    {
        _runtime.SetPresentation(VoiceVisionPresentationKind.Full);
        Interlocked.Increment(ref _loadGeneration);
        return LoadDocument(VoiceVisionCuiDocument.LoadSession());
    }

    /// <summary>
    /// Closes the presentation: the shared projection leaves the surface WITHOUT ending the session
    /// and WITHOUT disposing the loader/runtime. A FRESH loader action settlement is captured at the
    /// actual close point (never a stale cached snapshot), and the close-ACK fires only after every
    /// admitted command has settled; there is no fire-and-forget release signal.
    /// </summary>
    public async Task ClosePresentationAsync(CancellationToken cancellationToken = default)
    {
        var settlement = _loader.WhenActionsIdleAsync();
        _runtime.SetPresentation(VoiceVisionPresentationKind.None);
        await settlement.ConfigureAwait(false);
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

    internal async ValueTask DispatchCommandAsync(string command, object? parameter, CancellationToken cancellationToken)
    {
        if (_retiring)
            throw new ObjectDisposedException(nameof(VoiceVisionHostAdapter));

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
                await RaisePresentationIntentAsync(VoicePresentationIntent.OpenFull);
                break;

            case "voice.return":
                await RaisePresentationIntentAsync(VoicePresentationIntent.ReturnToPill);
                break;

            case "voice.toggle":
                await RaisePresentationIntentAsync(VoicePresentationIntent.TogglePill);
                break;

            case "voice.hide":
                await RaisePresentationIntentAsync(VoicePresentationIntent.Hide);
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

    private async Task RaisePresentationIntentAsync(VoicePresentationIntent intent)
    {
        // The adapter only forwards the authored intent. The external owner joins the original action
        // settlement OUTSIDE its own dispatch and then performs the actual mount/close.
        if (PresentationIntentRequested is { } requested)
            await requested(intent);
    }

    private async Task StartFromCommandAsync(object? parameter, CancellationToken cancellationToken)
    {
        var conversationId = ConversationProvider();
        if (conversationId is null || conversationId == Guid.Empty) return;

        var mode = TryResolveMode(parameter, out var requested) ? requested : VisionVoiceMode.Conversational;

        // Real production start options are required; there is no fabricated default model.
        var options = StartOptionsProvider();
        var spaceId = SpaceIdProvider?.Invoke();

        var (policy, localOnly) = ResolveEffectivePolicy();

        var request = new VoiceVisionStartRequest(conversationId.Value, spaceId, mode, options, policy, localOnly);
        await _runtime.StartAsync(request, cancellationToken);
    }

    /// <summary>
    /// Resolves the genuine effective model policy and local-only fact for a new session. Priority:
    /// the typed policy authority, then the host's explicit genuine providers, then the live session's
    /// own stored policy when attaching to an existing session. There is never a heuristic string
    /// inference and never a permissive fabricated local-only claim; with no genuine source a start is
    /// refused truthfully.
    /// </summary>
    private (string Policy, bool IsLocalOnly) ResolveEffectivePolicy()
    {
        if (_policyAuthority is not null)
        {
            var decision = _policyAuthority();
            if (decision is null || string.IsNullOrWhiteSpace(decision.EffectivePolicy))
                throw new InvalidOperationException(
                    "The Voice policy authority returned no effective model policy; the session was not started.");
            return (decision.EffectivePolicy, decision.IsLocalOnly);
        }

        if (PolicyProvider is not null || IsLocalOnlyProvider is not null)
        {
            // Both the effective policy and the local-only fact must come from genuine typed sources.
            // A single partial source would force a fabricated default for the other half.
            if (PolicyProvider is null || IsLocalOnlyProvider is null)
                throw new InvalidOperationException(
                    "Both the effective Voice model policy and the local-only fact must come from genuine typed sources; the session was not started.");
            var policy = PolicyProvider() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(policy))
                throw new InvalidOperationException(
                    "No effective Voice model policy is available; the session was not started.");
            return (policy, IsLocalOnlyProvider());
        }

        // Attaching to an already-live session reuses its genuine stored policy/local-only fact; it is
        // never a fabricated fallback.
        var live = _runtime.State;
        if (live.SessionId != Guid.Empty && live.IsActive)
            return (live.EffectiveModelPolicy, live.IsLocalOnly);

        throw new InvalidOperationException(
            "No genuine Voice start policy is available (effective model policy and local-only fact). Provide a typed policy authority before starting.");
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

    private void OnRuntimeStateChanged(object? sender, EventArgs args) => _bindings.Refresh();

    /// <summary>
    /// Reserves the ONE retained teardown identity BEFORE any external callback runs and returns the
    /// shared completion. Concurrent close/dispose callers share this exact completion; a synchronous
    /// retirement followed by an async join is covered. Sealing actual admission is part of the runner.
    /// </summary>
    private Task BeginRetainedTeardown()
    {
        lock (_gate)
        {
            if (_teardown is not null) return _teardown;
            if (_retiring)
                throw new ObjectDisposedException(nameof(VoiceVisionHostAdapter));
            _retiring = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _teardown = RunRetainedTeardownAsync(start.Task);
            start.SetResult();
            return _teardown;
        }
    }

    /// <summary>Retires only this adapter's owned runtime subscription and the loader after genuinely
    /// admitted work settles. No callbacks/awaits run under the ownership lock.</summary>
    private async Task RunRetainedTeardownAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        try
        {
            // Seal actual admission (no new dispatches after this point) BEFORE taking the final
            // settlement snapshot, so later in-flight admitted work is never missed and no work
            // begins after this snapshot.
            Task settlement;
            lock (_gate) settlement = _loader.WhenActionsIdleAsync();
            try { await settlement.ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }

            // Retire this adapter's own runtime subscription only after admitted work has settled.
            try { _runtime.StateChanged -= OnRuntimeStateChanged; }
            catch (Exception error) { failures.Add(error); }

            // Dispose the loader (outside the lock) then retain AND join the actual post-Dispose
            // cleanup settlement, which includes the loader's terminal disposal task. This is the
            // retained teardown/operation identity every close/dispose caller joins.
            //
            // The teardown starts on a pool thread after ConfigureAwait(false), but the loader's
            // Dispose unwinds live authored controls (action handlers, themed presentation, contexts)
            // which must run on the owning UI context. Marshal to the UI dispatcher when not already
            // there, and fail truthfully if the dispatcher cannot run it.
            try { await DisposeLoaderOnUiContextAsync(); }
            catch (Exception error) { failures.Add(error); }
            Task terminal;
            lock (_gate) terminal = _loader.WhenActionsIdleAsync();
            try { await terminal.ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
        catch (Exception error) { failures.Add(error); }
        ThrowTeardownFailures(failures);
    }

    private Task DisposeLoaderOnUiContextAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _loader.Dispose();
            return Task.CompletedTask;
        }
        // The approved fork's Dispatcher.InvokeAsync returns a DispatcherOperation; GetTask()
        // supplies its actual Task so we join the real disposal completion rather than a
        // discarded/complete stub.
        return Dispatcher.UIThread.InvokeAsync(() => _loader.Dispose()).GetTask();
    }

    /// <summary>Synchronous retirement. Reserves the retained teardown identity before any callback
    /// runs; the actual external unsubscribe/dispose callbacks and awaits happen inside the retained
    /// runner OUTSIDE the ownership lock, and a later async join observes the exact same teardown
    /// (including original and cleanup failures). The shared singleton runtime is never End/disposed here.</summary>
    public void Dispose()
    {
        _ = BeginRetainedTeardown();
    }

    /// <summary>Async retirement. Concurrent callers share the SAME retained teardown completion, and a
    /// prior synchronous <see cref="Dispose"/> is joined by this path. The shared singleton runtime is
    /// never End/disposed here.</summary>
    public async ValueTask DisposeAsync()
    {
        var teardown = BeginRetainedTeardown();
        await teardown.ConfigureAwait(false);
    }

    private static void ThrowTeardownFailures(List<Exception> failures)
    {
        if (failures.Count == 0) return;
        if (failures.Count == 1) throw failures[0];
        throw new AggregateException("The Voice host teardown retained original and cleanup failures.", failures);
    }
}
