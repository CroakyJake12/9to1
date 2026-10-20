using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
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
/// Hosting model: only the authored compiled CUI surface is ever shown. Presenting Voice mounts the
/// ACTUAL current Spaces root (chat/space) aside and mounts the compiled surface as the session's
/// one live root; hiding restores the same root instance, so draft/selection/queue survive every
/// toggle/full/return/hide. Every load of a compiled document is deferred until the original action
/// that requested it has settled (the external owner joins that settlement outside its own dispatch),
/// so a loader never runs from inside the action whose settlement it owns. The opener is caller-fenced:
/// a stale retired page can never open Voice against a replacement chat.
/// </summary>
internal sealed partial class SpacesBrowseAppContentSession
{
    private readonly object _voiceGate = new();
    private VoiceVisionHostAdapter? _voiceAdapter;
    private bool _voiceRetired;
    private Control? _voiceSurface;         // the single live authored root (pill or full)
    private bool _voiceFullMode;
    private bool _voicePresented;
    private Control? _voiceBorrowedRoot;    // the ACTUAL root content borrowed on first present
    private NativeHomeThemePreferences? _voiceTheme;
    private CuiThemePreferenceContext? _voiceThemeContext;
    private Task? _voiceThemeRefresh;

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
            RestoreBorrowedRoot();
            return Task.CompletedTask;
        };

        lock (_voiceGate)
        {
            if (_voiceRetired) { adapter.Dispose(); return null; }
            if (_voiceAdapter is not null) { adapter.Dispose(); return _voiceAdapter; }
            _voiceAdapter = adapter;
            return adapter;
        }
    }

    /// <summary>Borrows the genuine current Home theme source (never a donor/default theme). The
    /// source owner retains lifetime; this session retires its borrowed context on retirement.</summary>
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

        _voiceTheme = new NativeHomeThemePreferences(preferences.HomeThemePreferencePath, scope,
            UserPreferencesService.ProjectLegacyHomeTheme, authorise,
            callback => Dispatcher.UIThread.Post(callback));
        _voiceThemeRefresh = _voiceTheme.RefreshAsync(_token);
        try { await _voiceThemeRefresh.ConfigureAwait(true); }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        catch (Exception error)
        {
            // A genuine theme-authority failure is surfaced truthfully rather than silently swapped for
            // a donor/default theme.
            throw new InvalidOperationException("The genuine Home theme could not be borrowed for Voice.", error);
        }
        _voiceThemeContext = _voiceTheme.Context;
        return _voiceThemeContext;
    }

    private async Task HandlePresentationIntentAsync(VoiceVisionHostAdapter adapter, VoicePresentationIntent intent)
    {
        switch (intent)
        {
            case VoicePresentationIntent.OpenFull:
                await DeferPresentAsync(adapter, a => PresentFull(a)).ConfigureAwait(true);
                break;

            case VoicePresentationIntent.ReturnToPill:
                await DeferPresentAsync(adapter, a => PresentPill(a)).ConfigureAwait(true);
                break;

            case VoicePresentationIntent.TogglePill:
                if (_voicePresented) HideVoiceOverlay();
                else await DeferPresentAsync(adapter, a => PresentPill(a)).ConfigureAwait(true);
                break;

            case VoicePresentationIntent.Hide:
                HideVoiceOverlay();
                break;
        }
    }

    /// <summary>External owner accepts typed presentation intent, joins the original action settlement
    /// OUTSIDE its own dispatch, then replaces the presented surface only after that action settles.
    /// This guarantees a compiled document is never loaded from inside the action whose settlement it
    /// owns.</summary>
    private async Task DeferPresentAsync(VoiceVisionHostAdapter adapter, Action<VoiceVisionHostAdapter> present)
    {
        Dispatcher.UIThread.VerifyAccess();
        await Task.Yield();
        var settlement = adapter.CaptureActionSettlement();
        await settlement.ConfigureAwait(true);
        Dispatcher.UIThread.VerifyAccess();
        lock (_voiceGate) if (_voiceRetired) return;
        present(adapter);
    }

    /// <summary>The composer waveform opener toggles the shared presentation; it never starts the
    /// runtime and never invents a model.</summary>
    private async ValueTask ToggleVoiceFromOpenerAsync(VoiceCallerIdentity caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!IsCallerStillCurrent(caller)) return; // stale retired page must not open against replacement chat
        var adapter = await EnsureVoiceAdapterAsync().ConfigureAwait(true);
        if (adapter is null) return;
        if (_voicePresented) { HideVoiceOverlay(); return; }
        await DeferPresentAsync(adapter, a => PresentPill(a)).ConfigureAwait(true);
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

    /// <summary>Presents the compiled pill as the session's one live root, saving the ACTUAL root
    /// content for restoration. Loading a fresh pill retires any prior surface lineage; the retired
    /// lineage is never revived.</summary>
    private void PresentPill(VoiceVisionHostAdapter adapter)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_voiceFullMode || _voiceSurface is null)
        {
            EnsureBorrowedRoot();
            _voiceSurface = adapter.LoadPill();
            _voiceFullMode = false;
        }
        SetRootContent(_voiceSurface);
        _voiceSurface.IsVisible = true;
        _voicePresented = true;
    }

    /// <summary>Presents the compiled full surface as the session's one live root.</summary>
    private void PresentFull(VoiceVisionHostAdapter adapter)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_voiceFullMode || _voiceSurface is null)
        {
            EnsureBorrowedRoot();
            _voiceSurface = adapter.LoadSession();
            _voiceFullMode = true;
        }
        SetRootContent(_voiceSurface);
        _voiceSurface.IsVisible = true;
        _voicePresented = true;
    }

    /// <summary>Restores the borrowed ACTUAL root and drops the retired surface lineage. Presentation
    /// close never End/disposes the singleton Voice runtime.</summary>
    private void RestoreBorrowedRoot()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_voiceBorrowedRoot is not null)
        {
            SetRootContent(_voiceBorrowedRoot);
            _voiceBorrowedRoot = null;
            _voiceSurface = null;
            _voiceFullMode = false;
            _voicePresented = false;
        }
    }

    private void HideVoiceOverlay() => RestoreBorrowedRoot();

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
    /// teardown and the borrowed theme lifetime into the SAME close/failure list. Failed owners stay
    /// retained; nothing is swallowed or fired-and-forgotten.</summary>
    private async Task DisposeVoiceOverlayAsync(List<Exception> failures)
    {
        VoiceVisionHostAdapter? adapter;
        lock (_voiceGate)
        {
            _voiceRetired = true;
            adapter = _voiceAdapter;
            _voiceAdapter = null;
        }
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
        _voiceSurface = null;
        _voiceFullMode = false;
        _voicePresented = false;
    }
}
