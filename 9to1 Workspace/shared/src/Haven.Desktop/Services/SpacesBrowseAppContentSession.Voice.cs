using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Application.Call;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Shell.VoiceVision;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>
/// Voice &amp; Vision host for the ACTUAL current Spaces route
/// (<see cref="SpacesBrowseAppContentSession"/>) → <see cref="Overlay.OverlayChatSessionFactory"/>
/// → <see cref="NewChatPage"/>. This partial owns the canonical Voice host adapter, borrows REAL
/// composition state (conversation/space/model/policy) from the CURRENT published chat at use time,
/// wires the composer's waveform opener for both fresh and restored chats, and presents the shared
/// same-session pill/full as the authored compiled CUI composition. Presentation changes are never
/// End; explicit End alone ends the session, and the shared singleton runtime is never disposed here.
///
/// Hosting model: the FLOATING composed CUI host (the compiled <c>VoiceVisionHostShell</c>) owns the
/// full composition — the live-chat scene slot plus the floating pill and full-surface overlays. The
/// native session only mounts genuine owned controls into the authored named <see cref="CuiContainerControl"/>
/// slots (<c>voice-scene-host</c>, <c>voice-pill-host</c>, <c>voice-full-host</c>) and flips the
/// authored <c>Host.ShowPill</c>/<c>Host.ShowFull</c> visibility bindings. The ACTUAL chat control is
/// mounted once into the scene slot and stays attached and usable while the pill floats over it; the
/// full surface is deliberate and overlays the same scene; return reveals the same chat/pill/session.
///
/// Causal self-join-safe presentation: a presentation intent is accepted as a typed request that
/// captures the SAME original loader settlement (the snapshot that includes the requesting action),
/// reserves it in this owner's retained operation queue, and returns the admission ACK immediately so
/// the requesting action can settle. A separate external pump later awaits each reserved settlement
/// before remounting, so a compiled document is never loaded from inside the action whose settlement
/// it owns. The opener is caller-fenced: a stale retired page can never open Voice against a
/// replacement chat.
/// </summary>
internal sealed partial class SpacesBrowseAppContentSession
{
    private const string VoiceSceneHostId = "voice-scene-host";
    private const string VoicePillHostId = "voice-pill-host";
    private const string VoiceFullHostId = "voice-full-host";

    private readonly object _voiceGate = new();
    private VoiceVisionHostAdapter? _voiceAdapter;
    private bool _voiceRetired;
    private VoiceCallerIdentity? _voiceCaller;

    // Composed floating shell state (authored CUI; native host only mounts owned controls into slots).
    private CuiControlLoader? _voiceShellLoader;
    private VoiceHostShellBindings? _voiceShellBindings;
    private Control? _voiceShellRoot;
    private CuiContainerControl? _voiceSceneHost;
    private CuiContainerControl? _voicePillHost;
    private CuiContainerControl? _voiceFullHost;
    private Control? _voicePillControl;
    private Control? _voiceFullControl;
    private Control? _voiceBorrowedRoot;    // the ACTUAL root content (chat) mounted into the scene slot
    private long _voiceConsumedGeneration;  // the adapter mount generation actually consumed by a mount
    private bool _voicePresented;

    // Deferred-present queue + external pump (self-join-free: admission ACK returns immediately).
    private sealed record VoicePendingPresent(
        VoicePresentationIntent Intent,
        Task Settlement,
        VoiceCallerIdentity? Caller,
        VoiceVisionHostAdapter Adapter);
    private readonly Queue<VoicePendingPresent> _voicePendingPresents = new();
    private bool _voicePumpRunning;
    private Task? _voicePumpTask;
    private List<Exception>? _voiceRetainedFailures;

    private NativeHomeThemePreferences? _voiceTheme;
    private CuiThemePreferenceContext? _voiceThemeContext;

    /// <summary>Identity of the source page that wired the opener, fenced at use time so a retired
    /// page can never open Voice against a replacement chat.</summary>
    private sealed record VoiceCallerIdentity(Guid TabId, string ProfileId, NewChatPage Page);

    /// <summary>Wires the real shared same-session Voice opener into a page created by this
    /// session's factory. Called from the host seam BEFORE original initialization/Fresh/Load, so
    /// both fresh and restored chats receive the opener. Without a registered runtime the toggle
    /// stays truthfully hidden. The opener toggles presentation; it never starts the runtime or
    /// invents a model.</summary>
    private void WireVoiceIntoPage(NewChatPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (_services.GetService<IVoiceVisionRuntime>() is null) return;
        var caller = new VoiceCallerIdentity(_request.TabId, _request.ProfileId, page);
        _voiceCaller = caller;
        page.ConfigureVoiceOpener(() => ToggleVoiceFromOpenerAsync(caller));
    }

    private async Task<VoiceVisionHostAdapter?> EnsureVoiceAdapterAsync()
    {
        lock (_voiceGate)
        {
            if (_voiceRetired) return null;
            if (_voiceAdapter is not null) return _voiceAdapter;
        }

        // Borrow the genuine current Home theme before any compiled load, outside the gate.
        var theme = await EnsureVoiceThemeAsync().ConfigureAwait(true);

        var runtime = _services.GetService<IVoiceVisionRuntime>();
        if (runtime is null) return null;

        var adapter = new VoiceVisionHostAdapter(
            runtime,
            conversationProvider: CurrentVoiceConversationId,
            startOptionsProvider: BuildVoiceStartOptions,
            spaceIdProvider: CurrentVoiceSpaceId,
            isLocalOnlyProvider: CurrentVoiceLocalOnly,
            policyProvider: CurrentVoicePolicy,
            themePreference: theme);

        adapter.PresentationIntentRequested += intent => HandlePresentationIntentAsync(adapter, intent);
        adapter.PresentationClosedAsync += () =>
        {
            Dispatcher.UIThread.VerifyAccess();
            HideVoiceOverlay();
            return Task.CompletedTask;
        };
        // Anchor the retained mount generation to the fresh adapter's baseline so the first deferred
        // present can be admitted against the exact surface this host owns.
        Volatile.Write(ref _voiceConsumedGeneration, adapter.MountGeneration);

        lock (_voiceGate)
        {
            if (_voiceRetired) { adapter.Dispose(); return null; }
            if (_voiceAdapter is not null) { adapter.Dispose(); return _voiceAdapter; }
            _voiceAdapter = adapter;
            return adapter;
        }
    }

    /// <summary>Borrows the genuine current Home theme source (never a donor/default theme). The
    /// source owner retains lifetime; this session retires its borrowed context on retirement. A
    /// cancelled or failed refresh NEVER publishes a fabricated/default context — it disposes the
    /// borrowed source and surfaces the failure truthfully.</summary>
    private async Task<CuiThemePreferenceContext?> EnsureVoiceThemeAsync()
    {
        if (_voiceThemeContext is not null) return _voiceThemeContext;

        var preferences = _services.GetRequiredService<UserPreferencesService>();
        var home = _services.GetRequiredService<HomeNativeWindowsComposition>();
        var profileId = _request.ProfileId;
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, profileId);
        Func<CancellationToken, ValueTask<bool>> authorise = async token =>
        {
            var actor = await home.Profiles.GetCurrentAsync(token).ConfigureAwait(false);
            return actor is not null && string.Equals(actor.ProfileId, profileId, StringComparison.Ordinal);
        };

        var theme = new NativeHomeThemePreferences(preferences.HomeThemePreferencePath, scope,
            UserPreferencesService.ProjectLegacyHomeTheme, authorise,
            callback => Dispatcher.UIThread.Post(callback));
        try
        {
            await theme.RefreshAsync(_token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            await theme.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception error)
        {
            await theme.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("The genuine Home theme could not be borrowed for Voice.", error);
        }
        _voiceTheme = theme;
        _voiceThemeContext = theme.Context;
        return _voiceThemeContext;
    }

    /// <summary>
    /// Accepts an authored presentation intent as a typed request, captures the SAME original loader
    /// settlement (whose snapshot includes the requesting action), reserves it in this owner's
    /// retained operation queue and returns the admission ACK immediately so the requesting action
    /// can settle. The external pump later awaits each reserved settlement before remounting; this
    /// handler never awaits a settlement containing its own action.
    ///
    /// For a CUI action raised from the pill/full documents the originating settlement is the Voice
    /// adapter loader's snapshot. For the composer waveform opener the caller passes the genuine
    /// originating composer action settlement via <paramref name="originatingSettlement"/>, so a first
    /// open is deferred until that composer action settles (never reparented synchronously inside the
    /// still-running composer action). Each request also captures the CURRENT caller identity so the
    /// pump later re-checks that exact identity, never a newer mutable page.
    /// </summary>
    private Task HandlePresentationIntentAsync(VoiceVisionHostAdapter adapter, VoicePresentationIntent intent,
        Task? originatingSettlement = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_voiceGate)
        {
            if (_voiceRetired) return Task.CompletedTask;
            var settlement = originatingSettlement ?? adapter.CaptureActionSettlement();
            _voicePendingPresents.Enqueue(new VoicePendingPresent(intent, settlement, _voiceCaller, adapter));
            if (!_voicePumpRunning)
            {
                _voicePumpRunning = true;
                _voicePumpTask = RunVoicePumpAsync();
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// External owner pump. Runs outside any dispatching action: it dequeues admitted presentation
    /// operations, awaits their reserved original settlements (which complete once the requesting
    /// action has settled), re-checks the ORIGINAL captured caller identity and retirement immediately
    /// before mount, and only then performs the mount. Faults are retained and never leave the pump
    /// permanently running: it always drains to empty and then clears its running flag/task.
    /// </summary>
    private async Task RunVoicePumpAsync()
    {
        while (true)
        {
            VoicePendingPresent op;
            lock (_voiceGate)
            {
                if (_voicePendingPresents.Count == 0)
                {
                    _voicePumpRunning = false;
                    _voicePumpTask = null;
                    return;
                }
                op = _voicePendingPresents.Dequeue();
            }

            // Await the reserved original settlement OUTSIDE the dispatching action.
            try { await op.Settlement.ConfigureAwait(true); }
            catch (Exception error)
            {
                lock (_voiceGate) (_voiceRetainedFailures ??= []).Add(error);
                continue;
            }

            try
            {
                Dispatcher.UIThread.VerifyAccess();
                lock (_voiceGate) if (_voiceRetired) continue;
                // Recheck the ORIGINAL captured caller identity/profile/tab/publication (never the
                // mutable latest `_voiceCaller`), so a later page B cannot authorize an admitted A.
                if (op.Caller is { } caller && !IsCallerStillCurrent(caller)) continue;
                ApplyVoiceIntent(op.Adapter, op.Intent);
            }
            catch (Exception error)
            {
                lock (_voiceGate) (_voiceRetainedFailures ??= []).Add(error);
            }
        }
    }

    private void ApplyVoiceIntent(VoiceVisionHostAdapter adapter, VoicePresentationIntent intent)
    {
        switch (intent)
        {
            case VoicePresentationIntent.OpenFull:
                PresentFull(adapter);
                break;
            case VoicePresentationIntent.ReturnToPill:
                PresentPill(adapter);
                break;
            case VoicePresentationIntent.TogglePill:
                if (_voicePresented) HideVoiceOverlay();
                else PresentPill(adapter);
                break;
            case VoicePresentationIntent.Hide:
                HideVoiceOverlay();
                break;
        }
    }

    /// <summary>The composer waveform opener toggles the shared presentation; it never starts the
    /// runtime and never invents a model. It returns the admission ACK immediately; the actual mount
    /// happens later on the external pump after the genuine originating composer action settles.</summary>
    private async ValueTask ToggleVoiceFromOpenerAsync(VoiceCallerIdentity caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!IsCallerStillCurrent(caller)) return; // stale retired page must not open against replacement chat
        var adapter = await EnsureVoiceAdapterAsync().ConfigureAwait(true);
        if (adapter is null) return;
        // Recheck the SAME caller after the held theme/presentation await, immediately before any
        // hide/present action. A stale page must neither hide nor open against replacement chat.
        if (!IsCallerStillCurrent(caller)) return;
        _voiceCaller = caller;
        if (_voicePresented) { HideVoiceOverlay(); return; }
        // Carry the genuine originating COMPOSER action settlement into the external owner queue and
        // return the admission ACK before awaiting it, so a first open is never mounted synchronously
        // inside the still-running composer action.
        Task? origin = null;
        try { origin = caller.Page.CaptureComposerActionSettlement(); }
        catch { /* composer not yet live; fall back to the adapter snapshot below */ }
        await HandlePresentationIntentAsync(adapter, VoicePresentationIntent.ReturnToPill, origin).ConfigureAwait(true);
    }

    /// <summary>Fences the original caller identity/profile/tab/publication: the opener only acts when
    /// the SAME page that wired it is still the published chat owner of this session.</summary>
    private bool IsCallerStillCurrent(VoiceCallerIdentity caller)
    {
        if (!IsPublicationCurrent) return false;
        lock (_gate)
        {
            var route = _publishedChatRoute;
            return route is not null
                && ReferenceEquals(route.Page, caller.Page)
                && caller.TabId == _request.TabId
                && string.Equals(caller.ProfileId, _request.ProfileId, StringComparison.Ordinal);
        }
    }

    /// <summary>The currently published chat page (guarded). The adapter providers read through this
    /// so they always observe the live conversation/model/space, never construction-time identity.</summary>
    private NewChatPage? CurrentVoiceChat()
    {
        lock (_gate) return _publishedChatRoute?.Page;
    }

    private Guid? CurrentVoiceConversationId() => CurrentVoiceChat()?.CurrentConversation.Id;

    private Guid? CurrentVoiceSpaceId() => CurrentVoiceChat()?.CurrentConversation.SpaceId;

    private CallStartOptions BuildVoiceStartOptions()
    {
        var chat = CurrentVoiceChat();
        var model = chat?.SelectedModel;
        if (model is null)
            throw new InvalidOperationException("No Voice-ready model is selected in the current Space conversation.");
        return new CallStartOptions(model);
    }

    /// <summary>Genuine local-only fact from the real typed privacy authority; never inferred from
    /// policy text and never fabricated.</summary>
    private bool CurrentVoiceLocalOnly()
    {
        var privacy = _services.GetService<IPrivacyPreferenceStore>()
            ?? throw new InvalidOperationException(
                "The genuine privacy policy authority is unavailable; the local-only Voice fact cannot be resolved.");
        return privacy.Current.LocalOnlyMode || RuntimeSafetyState.IsSafeMode;
    }

    /// <summary>Genuine effective model policy derived from the real typed privacy/safety authority;
    /// never synthesized from heuristic text.</summary>
    private string CurrentVoicePolicy()
    {
        var privacy = _services.GetService<IPrivacyPreferenceStore>()
            ?? throw new InvalidOperationException(
                "The genuine privacy policy authority is unavailable; the effective Voice model policy cannot be resolved.");
        return (privacy.Current.LocalOnlyMode || RuntimeSafetyState.IsSafeMode) ? "local-only" : "default";
    }

    private void EnsureBorrowedRoot()
    {
        if (_voiceBorrowedRoot is null)
            _voiceBorrowedRoot = _root.Content as Control;
    }

    /// <summary>
    /// Loads the compiled floating <c>VoiceVisionHostShell</c> once and mounts the ACTUAL chat control
    /// into the authored scene slot. Native code only mounts genuine owned controls into named
    /// <see cref="CuiContainerControl"/> slots and flips authored visibility bindings; it never builds
    /// Panel/Grid/Border layout or raw-parses. The shell root becomes the session's live root while
    /// presented, with the same chat instance still attached in the scene slot.
    /// </summary>
    private void EnsureComposedShellMounted(VoiceVisionHostAdapter adapter)
    {
        if (_voiceShellRoot is not null) return;

        EnsureBorrowedRoot();
        var shellBindings = new VoiceHostShellBindings();
        var shellLoader = new CuiControlLoader(VoiceVisionCuiDocument.CreateControlRegistry());
        shellLoader.SetBindingContext(shellBindings);
        if (_voiceThemeContext is { } theme) shellLoader.SetThemePreferenceContext(theme);

        var loaded = shellLoader.TryLoad(VoiceVisionCuiDocument.LoadHostShell());
        if (loaded.Root is null || loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
        {
            shellLoader.Dispose();
            throw new InvalidOperationException("The Voice host shell could not be rendered.");
        }
        shellLoader.WireBindings(loaded.Root);

        var sceneHost = FindNamedContainer(loaded.Root, VoiceSceneHostId);
        var pillHost = FindNamedContainer(loaded.Root, VoicePillHostId);
        var fullHost = FindNamedContainer(loaded.Root, VoiceFullHostId);
        if (sceneHost is null || pillHost is null || fullHost is null)
        {
            shellLoader.Dispose();
            throw new InvalidOperationException("The Voice host shell is missing authored slots.");
        }

        // Detach the ACTUAL chat from the session root BEFORE reparenting it into the scene slot: a
        // Control may only ever have one logical parent at a time, so the borrowed chat must leave
        // `_root` before `AddChild` re-parents it into the authored scene container.
        SetRootContent(null);
        sceneHost.AddChild(_voiceBorrowedRoot!);
        _voiceShellLoader = shellLoader;
        _voiceShellBindings = shellBindings;
        _voiceShellRoot = loaded.Root;
        _voiceSceneHost = sceneHost;
        _voicePillHost = pillHost;
        _voiceFullHost = fullHost;
        SetRootContent(loaded.Root);
    }

    /// <summary>
    /// Finds an authored <see cref="CuiContainerControl"/> by its authored <c>id</c>. The shell's
    /// hidden overlay slots (pill/full) are authored with <c>IsVisible=false</c> at load, so the CUI
    /// container removes their layout cells from the ACTIVE visual/logical tree. A plain logical
    /// descendant search therefore misses them. This traversal includes the root and every retained
    /// authored child (<see cref="CuiContainerControl.AuthoredChildren"/>) in addition to active
    /// logical descendants, so hidden authored slots are found without making any overlay initially
    /// visible or building any Panel/Grid/Border layout in C#.
    /// </summary>
    private static CuiContainerControl? FindNamedContainer(Control root, string name)
    {
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Control>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            if (node.Name == name && node is CuiContainerControl found) return found;
            // Active logical descendants (visible tree).
            foreach (var child in node.GetLogicalDescendants())
                if (visited.Add(child)) stack.Push(child);
            // Retained authored children, including hidden/inactive overlay slots.
            if (node is CuiContainerControl container)
                foreach (var child in container.AuthoredChildren)
                    if (visited.Add(child)) stack.Push(child);
        }
        return null;
    }

    private void SetShellVisibility(bool pill, bool full)
    {
        if (_voiceShellBindings is { } b) b.SetVisibility(pill, full);
    }

    /// <summary>Verifies the retained mount generation is still the one the actual host consumed, so
    /// a stale deferred present never mounts over a replaced/closed surface. The adapter's generation
    /// advances only via LoadPill/LoadSession, which this serialized pump fully owns.</summary>
    private bool IsMountGenerationCurrent(VoiceVisionHostAdapter adapter) =>
        adapter.MountGeneration == Volatile.Read(ref _voiceConsumedGeneration);

    /// <summary>Presents the compiled pill mounted into the authored pill slot over the still-usable
    /// chat scene. The pill document is genuinely reloaded on every transition: the single adapter
    /// loader retires any prior surface, and <see cref="VoiceVisionHostAdapter.LoadPill"/> publishes
    /// the Pill presentation so runtime <c>Presentation</c> always matches the visible surface. The
    /// same live chat root remains mounted in the scene slot throughout.</summary>
    private void PresentPill(VoiceVisionHostAdapter adapter)
    {
        Dispatcher.UIThread.VerifyAccess();
        EnsureComposedShellMounted(adapter);
        if (!IsMountGenerationCurrent(adapter)) return; // stale: a replacement surface owns the host
        ReplaceSlotChild(_voicePillHost!, ref _voicePillControl, adapter.LoadPill());
        Volatile.Write(ref _voiceConsumedGeneration, adapter.MountGeneration);
        SetShellVisibility(pill: true, full: false);
        _voicePresented = true;
    }

    /// <summary>Presents the compiled full surface mounted into the authored full slot, overlaying
    /// the same scene/chat. The full document is genuinely reloaded on every transition and
    /// <see cref="VoiceVisionHostAdapter.LoadSession"/> publishes the Full presentation, so repeated
    /// OpenFull always reflects the current surface and mode — never a reused stale control/lineage.</summary>
    private void PresentFull(VoiceVisionHostAdapter adapter)
    {
        Dispatcher.UIThread.VerifyAccess();
        EnsureComposedShellMounted(adapter);
        if (!IsMountGenerationCurrent(adapter)) return; // stale: a replacement surface owns the host
        ReplaceSlotChild(_voiceFullHost!, ref _voiceFullControl, adapter.LoadSession());
        Volatile.Write(ref _voiceConsumedGeneration, adapter.MountGeneration);
        SetShellVisibility(pill: false, full: true);
        _voicePresented = true;
    }

    /// <summary>Replaces the control currently occupying an authored slot with a freshly reloaded
    /// surface, so no retired/inert prior lineage stays mounted in the slot.</summary>
    private static void ReplaceSlotChild(CuiContainerControl host, ref Control? previous, Control next)
    {
        if (previous is not null) host.RemoveChild(previous);
        host.AddChild(next);
        previous = next;
    }

    /// <summary>Hides the floating overlays, revealing the same still-mounted chat scene. Presentation
    /// hide never End/disposes the singleton Voice runtime and never detaches the chat control.</summary>
    private void HideVoiceOverlay()
    {
        Dispatcher.UIThread.VerifyAccess();
        SetShellVisibility(pill: false, full: false);
        _voicePresented = false;
    }

    /// <summary>
    /// Full teardown of the composed shell: removes the borrowed chat from the scene slot, restores
    /// the ACTUAL root content, and retires the shell loader. Used at close/dispose only; presentation
    /// hide keeps the shell and chat mounted (see <see cref="HideVoiceOverlay"/>).
    /// </summary>
    private void RestoreBorrowedRoot()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_voiceShellRoot is null) return;
        if (_voiceBorrowedRoot is not null && _voiceSceneHost is not null)
            _voiceSceneHost.RemoveChild(_voiceBorrowedRoot);
        SetRootContent(_voiceBorrowedRoot);
        RetireShellLoader();
        _voiceBorrowedRoot = null;
        _voicePresented = false;
    }

    private void RetireShellLoader()
    {
        var loader = _voiceShellLoader;
        _voiceShellLoader = null;
        _voiceShellBindings = null;
        _voiceShellRoot = null;
        _voiceSceneHost = null;
        _voicePillHost = null;
        _voiceFullHost = null;
        _voicePillControl = null;
        _voiceFullControl = null;
        if (loader is not null) loader.Dispose();
    }

    /// <summary>Closes the Voice presentation WITHOUT ending the session. The adapter settles loader
    /// actions before the close-ACK; End remains the only explicit runtime stop.</summary>
    private async Task CloseVoicePresentationAsync()
    {
        VoiceVisionHostAdapter? adapter;
        lock (_voiceGate) adapter = _voiceAdapter;
        if (adapter is not null) await adapter.ClosePresentationAsync();
        else HideVoiceOverlay();
    }

    /// <summary>Retires the Voice host with the rest of the tab, joining the adapter's actual async
    /// teardown, every admitted operation's settlement, and the borrowed theme lifetime into the SAME
    /// close/failure list. Every admitted original settlement and the pump are joined BEFORE the
    /// composed shell/borrowed chat state is retired. Failed owners stay retained; nothing is
    /// swallowed or fired-and-forgotten.</summary>
    private async Task DisposeVoiceOverlayAsync(List<Exception> failures)
    {
        VoiceVisionHostAdapter? adapter;
        lock (_voiceGate)
        {
            _voiceRetired = true;
            adapter = _voiceAdapter;
            _voiceAdapter = null;
        }

        // Join every admitted operation (original settlements) before retiring UI/borrowed state.
        while (true)
        {
            VoicePendingPresent op;
            lock (_voiceGate)
            {
                if (_voicePendingPresents.Count == 0) break;
                op = _voicePendingPresents.Dequeue();
            }
            try { await op.Settlement.ConfigureAwait(true); }
            catch (Exception error) { failures.Add(error); }
        }
        lock (_voiceGate)
        {
            if (_voiceRetainedFailures is { } retained)
                foreach (var error in retained) failures.Add(error);
            _voiceRetainedFailures = null;
        }
        if (_voicePumpTask is { } pump)
        {
            try { await pump.ConfigureAwait(true); }
            catch (Exception error) { failures.Add(error); }
            _voicePumpTask = null;
        }

        // Now retire the composed shell and restore the ACTUAL chat root.
        try { RestoreBorrowedRoot(); }
        catch (Exception error) { failures.Add(error); }

        if (adapter is not null)
        {
            try { await adapter.DisposeAsync().ConfigureAwait(true); }
            catch (Exception error) { failures.Add(error); }
        }
        if (_voiceTheme is not null)
        {
            try { await _voiceTheme.DisposeAsync().ConfigureAwait(true); }
            catch (Exception error) { failures.Add(error); }
        }
        _voiceTheme = null;
        _voiceThemeContext = null;
        _voicePresented = false;
    }

    /// <summary>
    /// Binding owner for the authored floating host shell. It publishes only the two authored
    /// visibility flags (<c>Host.ShowPill</c>/<c>Host.ShowFull</c>); it owns no actions and never
    /// claims a mode/device/capture is operational.
    /// </summary>
    private sealed class VoiceHostShellBindings : ICuiBindingContext, INotifyPropertyChanged
    {
        private bool _showPill;
        private bool _showFull;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetVisibility(bool pill, bool full)
        {
            if (_showPill == pill && _showFull == full) return;
            _showPill = pill;
            _showFull = full;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ShowPill"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ShowFull"));
        }

        public bool TryGetValue(string path, out object? value)
        {
            switch (path)
            {
                case "Host.ShowPill": value = _showPill; return true;
                case "Host.ShowFull": value = _showFull; return true;
                default: value = null; return false;
            }
        }
    }
}
