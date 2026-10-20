using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Haven.Application;
using Haven.Application.Call;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Shell.VoiceVision;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>
/// Voice &amp; Vision host for the ACTUAL current Spaces route
/// (<see cref="SpacesBrowseAppContentSession"/>) → <see cref="Overlay.OverlayChatSessionFactory"/>
/// → <see cref="NewChatPage"/>. This partial owns the canonical Voice host adapter, borrows REAL
/// composition state (conversation/space/model/policy) from the CURRENT published chat at use time,
/// wires the composer's waveform opener for both fresh and restored chats, and presents the shared
/// same-session pill/full through the typed native-control transport. Presentation changes are
/// never End; explicit End alone ends the session, and the shared singleton runtime is never
/// disposed here.
/// </summary>
internal sealed partial class SpacesBrowseAppContentSession
{
    private readonly object _voiceGate = new();
    private VoiceVisionHostAdapter? _voiceAdapter;
    private Control? _voicePillControl;
    private Control? _voiceFullSurface;
    private bool _voiceSurfaceShowingFull;
    private bool _voiceRetired;
    private Panel? _voiceOverlayHost;
    private Control? _voiceOverlayBorrowed;

    /// <summary>Wires the real shared same-session Voice opener into a page created by this
    /// session's factory. Called from the host seam BEFORE original initialization/Fresh/Load, so
    /// both fresh and restored chats receive the opener. Without a registered runtime the toggle
    /// stays truthfully hidden. The opener only requests presentation; it never starts the runtime
    /// or invents a model.</summary>
    private void WireVoiceIntoPage(NewChatPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (_services.GetService<IVoiceVisionRuntime>() is null) return;
        page.ConfigureVoiceOpener(() =>
        {
            PresentVoicePill();
            return ValueTask.CompletedTask;
        });
    }

    private VoiceVisionHostAdapter? EnsureVoiceAdapter()
    {
        lock (_voiceGate)
        {
            if (_voiceRetired) return null;
            if (_voiceAdapter is not null) return _voiceAdapter;

            var runtime = _services.GetService<IVoiceVisionRuntime>();
            if (runtime is null) return null;

            // Providers read CURRENT published-chat state at use time (after Fresh/Load/AssignSpace).
            var adapter = new VoiceVisionHostAdapter(
                runtime,
                conversationProvider: CurrentVoiceConversationId,
                startOptionsProvider: BuildVoiceStartOptions,
                spaceIdProvider: CurrentVoiceSpaceId,
                isLocalOnlyProvider: CurrentVoiceLocalOnly,
                policyProvider: CurrentVoicePolicy);
            adapter.RequestFullSurfaceAsync += ShowFullVoiceSurfaceAsync;
            adapter.PresentationClosedAsync += () =>
            {
                HideVoiceOverlay();
                return Task.CompletedTask;
            };
            _voiceAdapter = adapter;
            return adapter;
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

    /// <summary>Presents the shared Voice &amp; Vision pill for the current conversation over the still
    /// -usable chat. It reuses the one canonical adapter/runtime and never starts a second one.</summary>
    private void PresentVoicePill()
    {
        Dispatcher.UIThread.VerifyAccess();
        var adapter = EnsureVoiceAdapter();
        if (adapter is null) return;

        if (_voicePillControl is null)
        {
            _voicePillControl = adapter.LoadPill();
            _voicePillControl.HorizontalAlignment = HorizontalAlignment.Right;
            _voicePillControl.VerticalAlignment = VerticalAlignment.Bottom;
            _voicePillControl.Margin = new Thickness(20);
            _voicePillControl.SetValue(Panel.ZIndexProperty, 40);
            AttachVoiceOverlay(_voicePillControl);
        }

        if (_voiceFullSurface is not null) _voiceFullSurface.IsVisible = false;
        _voiceSurfaceShowingFull = false;
        _voicePillControl.IsVisible = true;
    }

    /// <summary>Expands the shared Voice &amp; Vision pill into the full session surface using the same
    /// canonical adapter/runtime (never a second runtime).</summary>
    private async Task ShowFullVoiceSurfaceAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        var adapter = EnsureVoiceAdapter();
        if (adapter is null) return;

        if (_voiceFullSurface is null)
        {
            _voiceFullSurface = adapter.LoadSession();
            _voiceFullSurface.HorizontalAlignment = HorizontalAlignment.Stretch;
            _voiceFullSurface.VerticalAlignment = VerticalAlignment.Stretch;
            _voiceFullSurface.SetValue(Panel.ZIndexProperty, 45);
            AttachVoiceOverlay(_voiceFullSurface);
        }

        if (_voicePillControl is not null) _voicePillControl.IsVisible = false;
        _voiceFullSurface.IsVisible = true;
        _voiceSurfaceShowingFull = true;
        await Task.CompletedTask;
    }

    private void HideVoiceOverlay()
    {
        if (_voicePillControl is not null) _voicePillControl.IsVisible = false;
        if (_voiceFullSurface is not null) _voiceFullSurface.IsVisible = false;
        _voiceSurfaceShowingFull = false;
    }

    /// <summary>Closes the Voice presentation WITHOUT ending the session. The adapter settles loader
    /// actions before the close-ACK; End remains the only explicit runtime stop.</summary>
    private async Task CloseVoicePresentationAsync()
    {
        if (_voiceAdapter is not null) await _voiceAdapter.ClosePresentationAsync();
        else HideVoiceOverlay();
    }

    /// <summary>
    /// Hosts a compiled Voice root (pill/full) as a floating overlay over the session's live content.
    /// This is transport/lifecycle only: the authored surface remains the compiled canonical CUI
    /// document. The overlay is created once and, when first shown, borrows the current root content
    /// into a floating host, restoring it on retirement.
    /// </summary>
    private void AttachVoiceOverlay(Control control)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_voiceOverlayHost is null)
        {
            _voiceOverlayHost = new Panel();
            _voiceOverlayBorrowed = _root.Content as Control;
            if (_voiceOverlayBorrowed is not null)
                _voiceOverlayHost.Children.Add(_voiceOverlayBorrowed);
            _root.Content = _voiceOverlayHost;
        }
        if (!_voiceOverlayHost.Children.Contains(control))
            _voiceOverlayHost.Children.Add(control);
    }

    /// <summary>Restores the borrowed live content and detaches the floating Voice overlay. Called on
    /// session retirement; presentation close uses <see cref="HideVoiceOverlay"/> instead.</summary>
    private void DetachVoiceOverlay()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_voiceOverlayHost is null) return;
        if (_voicePillControl is not null) _voiceOverlayHost.Children.Remove(_voicePillControl);
        if (_voiceFullSurface is not null) _voiceOverlayHost.Children.Remove(_voiceFullSurface);
        if (_voiceOverlayBorrowed is not null && !_voiceOverlayHost.Children.Contains(_voiceOverlayBorrowed))
            _voiceOverlayHost.Children.Add(_voiceOverlayBorrowed);
        if (_voiceOverlayHost.Children.Count == 1 && ReferenceEquals(_voiceOverlayHost.Children[0], _voiceOverlayBorrowed))
            _root.Content = _voiceOverlayBorrowed;
        _voiceOverlayHost = null;
        _voiceOverlayBorrowed = null;
    }

    private void DisposeVoiceOverlay()
    {
        try { DetachVoiceOverlay(); }
        catch { /* Retirement retains its own fault bookkeeping. */ }
        lock (_voiceGate)
        {
            _voiceRetired = true;
            var adapter = _voiceAdapter;
            _voiceAdapter = null;
            _voicePillControl = null;
            _voiceFullSurface = null;
            if (adapter is not null)
            {
                try { adapter.Dispose(); }
                catch { /* Async join observes retained faults. */ }
            }
        }
    }
}
