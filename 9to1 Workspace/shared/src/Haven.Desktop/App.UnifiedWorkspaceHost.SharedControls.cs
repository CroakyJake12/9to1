#if !ANDROID
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Desktop.Accounts;
using Haven.SharedHeader;
using HavenOS.Home;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    internal sealed partial class UnifiedWorkspaceHost
    {
        /// <summary>The embedded Haven frame owns the shared model/notifications/profile
        /// cluster. Availability is resolved from the real composed services; it is never
        /// invented from visual state or route text.</summary>
        private sealed class SharedHostContext(UnifiedWorkspaceHost host) : IHavenHostContext
        {
            private readonly UnifiedWorkspaceHost _host = host;

            public bool IsEmbedded => true;
            public bool ModelSupported => _host._services.GetService<IModelProviderRegistry>() is not null
                && _host._services.GetService<IPrivacyPreferenceStore>() is not null;
            public bool NotificationsSupported => _host._services.GetService<HavenNotificationService>() is not null;
            public bool ProfileSupported => _host._services.GetService<NativeCakeAccountUiOwner>() is not null;
            public IHavenSharedControlGateway? SharedControls => _host._sharedGateway;
        }

        /// <summary>Typed, asynchronous gateway to the genuine Home-owned shared surfaces.
        /// Every open returns the actual settlement of the borrowed surface's show/init/account
        /// operation; faults and refusals surface as structured results and are never swallowed
        /// as success. The host reserves admission into its original-task ledger before any
        /// external owner/callback is invoked, so close can join genuine failures.</summary>
        private sealed class HavenGateway(UnifiedWorkspaceHost host) : IHavenSharedControlGateway
        {
            private readonly UnifiedWorkspaceHost _host = host;

            public Task<HavenSharedOpenResult> OpenModelPickerAsync(CancellationToken cancellationToken = default) =>
                _host.OpenModelPickerPanelAsync(cancellationToken);
            public Task<HavenSharedOpenResult> OpenNotificationsAsync(CancellationToken cancellationToken = default) =>
                _host.OpenNotificationsPanelAsync(cancellationToken);
            public Task<HavenSharedOpenResult> OpenProfileAsync(CancellationToken cancellationToken = default) =>
                _host.OpenProfilePanelAsync(cancellationToken);
        }

        /// <summary>Admission outcome for re-observing the current authenticated Home actor after
        /// an await. Current is the only state that may proceed to an owning operation; Changed
        /// means a different full actor/auth revision is now current; Cancelled/Faulted keep
        /// caller cancellation and source faults distinct from a changed actor.</summary>
        internal enum ActorAdmissionStatus { Current, Changed, Cancelled, Faulted }

        /// <summary>Monotonic generation for the currently mounted shared panel. A panel that
        /// opened earlier is retired the moment another panel or close replaces it; stale async
        /// completions from a retired panel must never mutate the replacement surface.</summary>
        private long _sharedPanelGeneration;

        /// <summary>The typed asynchronous gateway the authored rail uses to open the genuine
        /// Home-owned shared surfaces. Wired by the integration patch (lane5-owned host).</summary>
        private HavenGateway? _sharedGateway;

        /// <summary>Retained original-session leases for composed model providers; disposed at close.</summary>
        private readonly List<HomeModelPickerOriginalSessionProvider> _modelSessions = [];

        /// <summary>The authored shared-panel shell loader/bindings and its named host elements.
        /// Host elements are the real CUI containers (AddChild/RemoveChild contract), never a
        /// donor Panel/Children escape.</summary>
        private CuiControlLoader? _shellLoader;
        private SharedPanelShellBindings? _shellBindings;
        private CuiContainerControl? _sharedPanelHost;

        /// <summary>The currently mounted shared panel loader (if any). Its retained action/disposal
        /// settlement is joined at close before borrowed owners are released.</summary>
        private CuiControlLoader? _sharedPanelLoader;

        /// <summary>The authored root control of the currently mounted shared panel. It is the ONLY
        /// child this host adds to the panel host; retirement removes exactly this view through real
        /// CuiContainerControl authored bookkeeping and never clears unrelated/new children.</summary>
        private Control? _sharedPanelRoot;

        /// <summary>Reserves a slot in the host original-task ledger BEFORE invoking an external
        /// owner/callback, preserving the caller's synchronization context and the original task
        /// identity/failure so close can join it (pattern: Host.cs RunObservation start-barrier).</summary>
        private Task<T> AdmitOriginal<T>(Func<Task<T>> produce)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<T> result;
            lock (_gate)
            {
                DemandCurrent();
                _sources.RemoveWhere(task => task.IsCompletedSuccessfully);
                if (_sources.Count >= 128) throw new InvalidOperationException("Original host observations must settle before more are admitted.");
                result = Run(start.Task); _sources.Add(result);
            }
            start.SetResult();
            return result;

            async Task<T> Run(Task gate)
            {
                await gate;
                // Enter the same original execution guard/ledger protocol as ordinary host
                // observations so a reentrant provider callback requesting close cannot join its
                // own in-flight open.
                using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                return await produce();
            }
        }

        /// <summary>Composes the authored main scene and the authored shared-panel shell. The
        /// whole frame structure, backdrop/dimming, spacing and visibility are authored in the
        /// packaged shell .cui; this host only mounts the scene control into the named scene
        /// host element and records the authored panel host element. It never builds grid/modal
        /// UI or visible chrome in C#. If the compiled shell bundle is unavailable (BLOCKED
        /// dependency), the host degrades truthfully: the scene mounts directly and shared
        /// panels remain unavailable until root emits the bundle.</summary>
        private void ComposeSharedFrame()
        {
            Dispatcher.UIThread.VerifyAccess();
            var theme = _homeTheme?.Context
                ?? throw new InvalidOperationException("The original Home theme owner is unavailable.");
            var shellBindings = new SharedPanelShellBindings();
            var shellLoader = new CuiControlLoader();
            try
            {
                shellLoader.SetThemePreferenceContext(theme);
                shellLoader.SetBindingContext(shellBindings);
                // The authored shell has no actions; it must NOT register a dispatcher.
                // SharedPanelShellBindings is a binding/INotifyPropertyChanged context only,
                // so registering it as an ICuiActionDispatcher was a compile error. The shell
                // is CUI-owned; the host only changes HasOpenPanel and mounts the active panel.
                var loaded = shellLoader.TryLoad(HavenSharedPanelShellDocument.Load());
                if (loaded.Root is null || loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
                {
                    shellLoader.Dispose();
                    SetSceneOnlyFallback();
                    return;
                }
                shellLoader.WireBindings(loaded.Root);
                var sceneHost = FindNamedContainer(loaded.Root, SharedSceneHostId);
                if (sceneHost is null)
                {
                    shellLoader.Dispose();
                    SetSceneOnlyFallback();
                    return;
                }
                sceneHost.AddChild(_scene!);
                _shellLoader = shellLoader;
                _shellBindings = shellBindings;
                _sharedPanelHost = FindNamedContainer(loaded.Root, SharedPanelHostId);
                Content = loaded.Root;
            }
            catch (CuiRuntimeLoadException)
            {
                shellLoader.Dispose();
                SetSceneOnlyFallback();
            }
        }

        private const string SharedSceneHostId = "shared-scene-host";
        private const string SharedPanelHostId = "shared-panel-host";

        /// <summary>Truthful degradation when the authored shell cannot be mounted: the scene
        /// is shown directly and shared panels remain unavailable. No raw reparse fallback.</summary>
        private void SetSceneOnlyFallback()
        {
            _shellLoader = null;
            _shellBindings = null;
            _sharedPanelHost = null;
            Content = _scene;
        }

        private static CuiContainerControl? FindNamedContainer(Control root, string name) =>
            FindNamedContainerRecursive(root, name);

        private static CuiContainerControl? FindNamedContainerRecursive(Control control, string name)
        {
            if (control is CuiContainerControl named && named.Name == name) return named;
            switch (control)
            {
                case CuiContainerControl container:
                    // Every authored child (including hidden cells no longer in the active layout)
                    // is part of the retained authored graph.
                    foreach (var child in container.AuthoredChildren)
                        if (FindNamedContainerRecursive(child, name) is { } found) return found;
                    break;
                case Panel panel:
                    foreach (var child in panel.Children)
                        if (FindNamedContainerRecursive(child, name) is { } found) return found;
                    break;
                case Decorator { Child: Control child }:
                    return FindNamedContainerRecursive(child, name);
                case ContentControl { Content: Control child }:
                    return FindNamedContainerRecursive(child, name);
            }
            return null;
        }

        /// <summary>Retires a panel loader and retains BOTH disposal phases: the synchronous
        /// Dispose runs unsubscribe/cancel callbacks (which may reenter and mount a replacement),
        /// and the post-Dispose cleanup task is retained through the host original-task ledger so
        /// close joins real cleanup and failures. Dispose failure is preserved by identity.</summary>
        private void RetireLoader(CuiControlLoader loader)
        {
            try { loader.Dispose(); }
            catch (Exception error) { Track(Task.FromException(error)); }
            Track(loader.WhenActionsIdleAsync());
        }

        private void ShowSharedPanel(CuiControlLoader loader, Control root)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_sharedPanelHost is null) { RetireLoader(loader); return; }
            if (_sharedPanelLoader is { } present && !ReferenceEquals(present, loader))
            {
                // Capture exactly A's ownership (loader + authored root) before any callback runs.
                var presentRoot = _sharedPanelRoot;
                _sharedPanelLoader = null;
                _sharedPanelRoot = null;
                // Remove only the exact retiring A view through real CUI container bookkeeping;
                // never clear unrelated/new children.
                if (presentRoot is not null) _sharedPanelHost.RemoveChild(presentRoot);
                // Dispose A; a callback may synchronously mount a replacement B. B's mount is
                // preserved: it took over _sharedPanelLoader/_sharedPanelRoot during A's settle.
                // Do NOT bump the generation here: B reserved its exact generation when it opened
                // (OpenModelPickerPanelAsync/OpenNotificationsPanelAsync), so a bump would invalidate
                // B's own callbacks/controller PublicationGate. A's stale completions already refuse
                // because A's captured generation predates B's.
                RetireLoader(present);
                // Recheck the same incoming owner after callbacks before installing B.
                if (_sharedPanelLoader is { } replacement && !ReferenceEquals(replacement, loader))
                { RetireLoader(loader); return; }
            }
            _sharedPanelLoader = loader;
            _sharedPanelRoot = root;
            _sharedPanelHost.AddChild(root);
            if (_shellBindings is not null) _shellBindings.HasOpenPanel = true;
        }

        private void HideSharedPanel()
        {
            Dispatcher.UIThread.VerifyAccess();
            // Capture/detach exact A ownership (loader + root) before any callback runs, so a
            // synchronous dispose/cancel that mounts a replacement B is preserved.
            var present = _sharedPanelLoader;
            var root = _sharedPanelRoot;
            _sharedPanelLoader = null;
            _sharedPanelRoot = null;
            if (present is null) { if (_shellBindings is not null) _shellBindings.HasOpenPanel = false; return; }
            // Retire A's exact authored root BEFORE any disposal callback runs. If A.Dispose
            // synchronously mounts B, B's own Show takes over _sharedPanelLoader/_sharedPanelRoot
            // and adds B's root, so retiring A's exact root here never clears B and never bumps
            // B's generation. If no replacement mounts, A's root is removed once and the panel
            // state is cleared below. Only the exact captured A root is removed through real CUI
            // container bookkeeping; unrelated/new children are never touched.
            if (_sharedPanelHost is not null && root is not null)
                _sharedPanelHost.RemoveChild(root);
            RetireLoader(present);
            // Only mutate shared-panel state if no replacement was mounted during A's settle.
            if (_sharedPanelLoader is null)
            {
                // No replacement: ensure stale completions from the retired A refuse.
                Interlocked.Increment(ref _sharedPanelGeneration);
                if (_shellBindings is not null) _shellBindings.HasOpenPanel = false;
            }
            // If B mounted, B's own Show already set HasOpenPanel and reserved B's generation and
            // root; A's stale completions already refuse against B's newer generation.
        }

        /// <summary>Full host currentness (app/window/chrome retirement) plus the mounted panel
        /// generation and the actual profile this panel was opened for. Never the weak
        /// _closed/_startupRetirement-only check.</summary>
        private bool IsPanelCurrent(long generation, string? profile) =>
            IsCurrent &&
            generation == Volatile.Read(ref _sharedPanelGeneration) &&
            StringComparer.Ordinal.Equals(profile, _actor?.ProfileId);

        /// <summary>Re-observe the ACTUAL current Home actor/auth revision and require it to
        /// still equal the full authenticated actor/session/auth revision a shared panel was
        /// opened for (not merely the same profile string). Distinguished outcomes keep caller
        /// cancellation and source faults apart from a genuinely changed actor, so a caller can
        /// publish a precise structured refusal instead of conflating them.</summary>
        internal async Task<ActorAdmissionStatus> ReobserveActorAsync(AuthenticatedResourceActor expected, CancellationToken token)
        {
            if (expected is null) return ActorAdmissionStatus.Changed;
            try
            {
                var actor = await ObserveActorAsync(token);
                return actor is not null && actor == expected ? ActorAdmissionStatus.Current : ActorAdmissionStatus.Changed;
            }
            catch (OperationCanceledException) { return ActorAdmissionStatus.Cancelled; }
            catch { return ActorAdmissionStatus.Faulted; }
        }

        /// <summary>Structured status code for a failed actor admission, kept distinct from a
        /// changed actor so callers never masquerade a cancellation/source fault as a profile
        /// change (or vice versa).</summary>
        internal static string ActorAdmissionCode(ActorAdmissionStatus status) => status switch
        {
            ActorAdmissionStatus.Cancelled => "Cancelled",
            ActorAdmissionStatus.Faulted => "ActorReadFailed",
            _ => "ActorChanged"
        };

        /// <summary>Surfaces a structured shared-panel command refusal through the real rail
        /// status channel. Genuine stale/malformed command refusals are published, never
        /// silently swallowed.</summary>
        internal void PublishSharedRefusal(string code, string message)
        {
            if (code is null || message is null) return;
            _workspace?.PublishSharedRefusal(code, message);
        }

        /// <summary>Joins the shared-panel settlement for host close: the mounted panel's retained
        /// action/disposal settlement (including any post-Dispose async cleanup), then disposal of
        /// composed original-session leases. This is invoked by the lane5 close path via the
        /// exact-base integration patch (App.UnifiedWorkspaceHost.cs is lane5-owned).</summary>
        internal async Task JoinSharedPanelSettlementAsync(List<Exception> failures)
        {
            // Dispose FIRST so the post-Dispose cleanup task is reserved, then join the FULL
            // settlement (which includes that disposal task via WhenActionsIdleAsync). Awaits
            // before Dispose never captured the new disposal task.
            if (_sharedPanelLoader is { } panelLoader)
            {
                _sharedPanelLoader = null;
                _sharedPanelRoot = null;
                try { panelLoader.Dispose(); } catch (Exception error) { AddAppCause(failures, error); }
                try { await JoinOriginalAppTaskAsync(panelLoader.WhenActionsIdleAsync(), failures); }
                catch (Exception error) { AddAppCause(failures, error); }
            }
            foreach (var session in _modelSessions.ToArray())
            {
                try { session.Dispose(); } catch (Exception error) { AddAppCause(failures, error); }
            }
            _modelSessions.Clear();
            if (_shellLoader is not null)
            {
                try { _shellLoader.Dispose(); } catch (Exception error) { AddAppCause(failures, error); }
                try { await JoinOriginalAppTaskAsync(_shellLoader.WhenActionsIdleAsync(), failures); }
                catch (Exception error) { AddAppCause(failures, error); }
                _shellLoader = null;
            }
        }

        private Task<HavenSharedOpenResult> OpenProfilePanelAsync(CancellationToken cancellationToken)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsCurrent) return Task.FromResult(HavenSharedOpenResult.Denied);
            var owner = _services.GetService<NativeCakeAccountUiOwner>();
            if (owner is null) return Task.FromResult(HavenSharedOpenResult.Refused("ProfileUnavailable", "The native profile owner is unavailable."));
            var window = TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The Haven host window is unavailable.");
            // Admission is reserved BEFORE the native owner is invoked (AdmitOriginal reserves a
            // slot in the host ledger before producing), so close joins the genuine open task.
            return AdmitOriginal(async () =>
            {
                Task original;
                try { original = owner.OpenAsync(window); }
                catch (Exception error) { return HavenSharedOpenResult.Refused("ProfileOpenFailed", error.Message); }
                original = Track(original);
                try { await original; }
                catch (Exception error) { return HavenSharedOpenResult.Refused("ProfileOpenFailed", error.Message); }
                return HavenSharedOpenResult.Succeeded();
            });
        }

        private Task<HavenSharedOpenResult> OpenNotificationsPanelAsync(CancellationToken cancellationToken)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsCurrent) return Task.FromResult(HavenSharedOpenResult.Denied);
            var service = _services.GetService<HavenNotificationService>();
            var theme = _homeTheme?.Context;
            if (service is null || theme is null)
                return Task.FromResult(HavenSharedOpenResult.Refused("NotificationsUnavailable", "The durable Home notification service is unavailable."));
            var actor = _actor;
            if (actor is null) return Task.FromResult(HavenSharedOpenResult.Denied);
            var generation = checked(++_sharedPanelGeneration);
            var bindings = new HavenNotificationsCuiBindings(this, service, generation, actor);
            var loader = new CuiControlLoader();
            // Configure binding/theme/action context before loading so the authored Repeat
            // captures this panel's real notification source and the genuine Home theme.
            loader.SetThemePreferenceContext(theme);
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            CuiDocument document;
            try { document = HavenNotificationsCuiDocument.Load(); }
            catch (CuiRuntimeLoadException error)
            {
                loader.Dispose();
                return Task.FromResult(HavenSharedOpenResult.Refused("PanelLoadBlocked", error.Message));
            }
            var loaded = loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            {
                loader.Dispose();
                return Task.FromResult(HavenSharedOpenResult.Refused("PanelLoadFailed", "The authored notifications panel failed to load."));
            }
            loader.WireBindings(loaded.Root);
            // Admission is reserved before invoking the durable provider read.
            var load = AdmitOriginal(async () =>
            {
                await bindings.LoadCoreAsync(cancellationToken);
                return HavenSharedOpenResult.Succeeded();
            });
            ShowSharedPanel(loader, loaded.Root);
            return ThenResult(load);
        }

        /// <summary>Opens the authentic Home Universal Model Picker for the current profile,
        /// composed from the REAL provider/repository/actor-session with the SAME Home profile/
        /// state/broker/authorization/provider-registry/privacy owners (User scope only).
        /// If the genuine composition is unavailable the rail model control stays truthfully
        /// refused — never a fixture catalogue or permanently disabled stand-in.</summary>
        private Task<HavenSharedOpenResult> OpenModelPickerPanelAsync(CancellationToken cancellationToken)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsCurrent) return Task.FromResult(HavenSharedOpenResult.Denied);
            var theme = _homeTheme?.Context;
            if (theme is null) return Task.FromResult(HavenSharedOpenResult.Refused("ThemeUnavailable", "The genuine Home theme is not ready."));
            var generation = checked(++_sharedPanelGeneration);
            return AdmitOriginal(async () =>
            {
                // Re-observe the ACTUAL current Home actor (not the startup-cached _actor) and
                // refuse before any work if this panel's profile no longer matches. The whole
                // open runs on the UI thread (no ConfigureAwait(false) after real profile/provider
                // I/O) so control load/mount and Surface.ApplyState/PropertyChanged publication
                // are fenced before visible mutation on the UI thread.
                var actor = await ObserveActorAsync(cancellationToken);
                if (actor is null || !IsPanelCurrent(generation, actor.ProfileId))
                    return HavenSharedOpenResult.Denied;
                HomeModelPickerOriginalSessionProvider session;
                try
                {
                    var routes = new HomeVersionedModelRouteRepository(_home.StateStore);
                    var provider = new HomeModelPickerFeatureProvider(_home.Profiles, routes,
                        _services.GetRequiredService<IModelProviderRegistry>(),
                        _services.GetRequiredService<IPrivacyPreferenceStore>(),
                        _home.Resources, _home.Broker);
                    session = new HomeModelPickerOriginalSessionProvider(provider, _home.Profiles);
                    var begun = await session.BeginAsync(cancellationToken);
                    if (!begun) return HavenSharedOpenResult.Refused("OriginalModelSessionUnavailable",
                        "The originating model manager session could not be bound to the current Home actor.");
                }
                catch (Exception error)
                {
                    return HavenSharedOpenResult.Refused("ModelCompositionUnavailable", error.Message);
                }

                var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(session));
                // Fence publication BEFORE the controller mutates its surface: a retired panel or
                // actor switch must never publish. Preserves standalone callers (gate null).
                controller.PublicationGate = () => IsPanelCurrent(generation, actor.ProfileId);
                var loader = new CuiControlLoader();
                loader.SetThemePreferenceContext(theme);
                loader.SetBindingContext(controller.Surface);
                loader.SetActionDispatcher(new HomeModelPickerDispatcher(controller, this, generation, actor));
                var loaded = loader.TryLoad(controller.Surface.Document);
                if (loaded.Root is null || loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
                {
                    RetireLoader(loader);
                    return HavenSharedOpenResult.Refused("PanelLoadFailed", "The authored model-picker panel failed to load.");
                }
                loader.WireBindings(loaded.Root);
                // Await the real session/controller/catalogue initialization BEFORE reporting
                // success; surface its typed refusal/failure rather than discarding it.
                var opened = await controller.OpenAsync("User", "chat", cancellationToken);
                if (!opened.Succeeded || !IsPanelCurrent(generation, actor.ProfileId))
                {
                    RetireLoader(loader);
                    return opened.Succeeded ? HavenSharedOpenResult.Denied
                        : HavenSharedOpenResult.Refused(opened.Code ?? "ModelOpenFailed", opened.Message);
                }
                _modelSessions.Add(session);
                ShowSharedPanel(loader, loaded.Root);
                return HavenSharedOpenResult.Succeeded();
            });
        }

        private static async Task<HavenSharedOpenResult> ThenResult(Task load)
        {
            try { await load.ConfigureAwait(false); }
            catch (OperationCanceledException) { return HavenSharedOpenResult.Refused("Cancelled", "The shared panel load was cancelled."); }
            catch (Exception error) { return HavenSharedOpenResult.Refused("LoadFailed", error.Message); }
            return HavenSharedOpenResult.Succeeded();
        }

        /// <summary>Reads the authored Haven shared-panel shell CUI document from the GENUINE V1
        /// compiled transport owned by the Haven.SharedHeader component (hard format/ABI/registry/
        /// capability admission on the whole bundle) with no raw parser fallback.</summary>
        private static class HavenSharedPanelShellDocument
        {
            public static CuiDocument Load() => HavenSharedHeaderDocuments.LoadShell();
        }

        /// <summary>Reads the authored Haven notifications CUI document from the packaged compiled
        /// V1 transport. The compiled bundle is emitted by the CUI build target in the
        /// Haven.SharedHeader component; if the bundle or the requested source is absent this fails
        /// fast with a structured load exception rather than silently reparsing raw source.</summary>
        private static class HavenNotificationsCuiDocument
        {
            public static CuiDocument Load() => HavenSharedHeaderDocuments.LoadNotifications();
        }

        /// <summary>Nonvisual binding context for the authored shared-panel shell. The authored
        /// shell drives backdrop/visibility from HasOpenPanel; the host only changes this value and
        /// mounts the active panel control into the authored host element.</summary>
        private sealed class SharedPanelShellBindings : ICuiBindingContext, INotifyPropertyChanged
        {
            private bool _hasOpenPanel;
            public event PropertyChangedEventHandler? PropertyChanged;
            public bool HasOpenPanel
            {
                get => _hasOpenPanel;
                set { if (_hasOpenPanel == value) return; _hasOpenPanel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HasOpenPanel")); }
            }
            public bool TryGetValue(string path, out object? value)
            {
                value = path switch { "HasOpenPanel" => _hasOpenPanel, _ => null };
                return value is not null;
            }
        }

        /// <summary>Routes model-picker CUI actions through the Home controller using canonical
        /// named &lt;Action parameter=...&gt; declarations. Every command is fenced against the exact
        /// mounted panel generation/profile before any owning operation or surface mutation; a
        /// retired panel refuses and never reaches the service. Missing/malformed parameters refuse
        /// structurally rather than silently succeeding.</summary>
        private sealed class HomeModelPickerDispatcher(HomeModelPickerCuiController controller, UnifiedWorkspaceHost host,
            long generation, AuthenticatedResourceActor actor)
            : ICuiActionDispatcher
        {
            private readonly HomeModelPickerCuiController _controller = controller;
            private readonly UnifiedWorkspaceHost _host = host;
            private readonly long _generation = generation;
            private readonly AuthenticatedResourceActor _actor = actor;
            private readonly string? _profile = actor.ProfileId;

            public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
            {
                // Admission fence: refuse before invoking any owning operation when this panel is
                // no longer the mounted/current one.
                if (!_host.IsPanelCurrent(_generation, _profile))
                { _host.PublishSharedRefusal("StalePanel", "This model-picker panel is no longer current."); return; }
                // Full current actor/admission re-validation BEFORE calling the owner, so a
                // retired/actor-switched panel refuses before touching the owning service. The
                // exact mounted panel/generation is re-checked AFTER the await, because the panel
                // may retire during the awaited re-observation.
                var admission = await _host.ReobserveActorAsync(_actor, cancellationToken);
                if (admission != ActorAdmissionStatus.Current || !_host.IsPanelCurrent(_generation, _profile))
                {
                    _host.PublishSharedRefusal(ActorAdmissionCode(admission), admission == ActorAdmissionStatus.Cancelled
                        ? "The model-picker admission was cancelled."
                        : "This model-picker panel is no longer current for the authenticated profile.");
                    return;
                }
                switch (command)
                {
                    case "Refresh":
                        await Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(
                            HomeModelPickerAction.Refresh,
                            Scope: (parameter as HomeModelPickerScopeSelection)?.Scope ?? _controller.Surface.Scope,
                            Category: (parameter as HomeModelPickerScopeSelection)?.Category ?? _controller.Surface.Category), cancellationToken));
                        return;
                    case "SelectCategory" when parameter is string category:
                        await Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(
                            HomeModelPickerAction.SelectCategory, Category: category), cancellationToken));
                        return;
                    case "SelectCategory":
                        RefuseMalformed(command);
                        return;
                    case "SelectRoute" when parameter is HomeModelPickerRouteSelection route:
                        await Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(
                            HomeModelPickerAction.SelectRoute, RouteId: route.RouteId), cancellationToken));
                        return;
                    case "SelectRoute":
                        RefuseMalformed(command);
                        return;
                    case "SetCandidateEnabled" when parameter is HomeModelPickerCandidateToggleSelection toggle:
                        await Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(
                            HomeModelPickerAction.SetCandidateEnabled, ProviderId: toggle.ProviderId, ModelId: toggle.ModelId,
                            ArtifactRevision: toggle.ArtifactRevision, Enabled: toggle.Enabled), cancellationToken));
                        return;
                    case "SetCandidateEnabled":
                        RefuseMalformed(command);
                        return;
                    case "MoveCandidateUp" or "MoveCandidateDown" when parameter is HomeModelPickerCandidateSelection move:
                        var moveAction = command == "MoveCandidateUp" ? HomeModelPickerAction.MoveCandidateUp : HomeModelPickerAction.MoveCandidateDown;
                        await Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(moveAction,
                            ProviderId: move.ProviderId, ModelId: move.ModelId, ArtifactRevision: move.ArtifactRevision), cancellationToken));
                        return;
                    case "MoveCandidateUp" or "MoveCandidateDown":
                        RefuseMalformed(command);
                        return;
                    case "SaveRoute":
                        await Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(HomeModelPickerAction.SaveRoute), cancellationToken));
                        return;
                    case "FinishAudit":
                        await Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(HomeModelPickerAction.FinishAudit), cancellationToken));
                        return;
                    case "PreviewResolution" when parameter is string capability:
                        await Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(
                            HomeModelPickerAction.PreviewResolution, Capability: capability), cancellationToken));
                        return;
                    case "PreviewResolution":
                        RefuseMalformed(command);
                        return;
                    default:
                        _host.PublishSharedRefusal("UnknownCommand", $"Unknown model-picker command '{command}'.");
                        return;
                }
            }

            private void RefuseMalformed(string command)
                => _host.PublishSharedRefusal("MalformedCommand", $"The model-picker command '{command}' received a malformed parameter.");

            private ValueTask Dispatch(Func<Task> action)
            {
                // Reserve the retained original task slot in the host ledger BEFORE the owning
                // action begins, so close joins a synchronous fault as reliably as an async one;
                // never swallow a fault as successful dispatch.
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var retained = Run(gate.Task, action);
                _host.Track(retained);
                gate.SetResult();
                return new(retained);

                static async Task Run(Task start, Func<Task> body)
                {
                    await start;
                    await body();
                }
            }
        }

        /// <summary>Read-only CUI projection over the durable Home notification service. It
        /// fences every completion against the panel generation/profile it was opened for, so a
        /// retired panel can never publish or mutate a replacement surface. Commands revalidate
        /// currentness BEFORE invoking the owning service, so a retired panel refuses without
        /// service mutation.</summary>
        private sealed class HavenNotificationsCuiBindings(UnifiedWorkspaceHost host, HavenNotificationService service,
            long generation, AuthenticatedResourceActor actor)
            : ICuiBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
        {
            private readonly UnifiedWorkspaceHost _host = host;
            private readonly HavenNotificationService _service = service;
            private readonly long _generation = generation;
            private readonly AuthenticatedResourceActor _actor = actor;
            private readonly string? _profile = actor.ProfileId;
            private HavenNotificationRow[] _rows = [];

            public event PropertyChangedEventHandler? PropertyChanged;

            private bool IsCurrent() => _host.IsPanelCurrent(_generation, _profile);

            /// <summary>Re-observe the ACTUAL full current Home actor/session/auth revision (never
            /// the startup-cached profile alone) and require it to still equal the panel's opened
            /// actor before an owning read/mutation proceeds. Distinguished outcomes keep
            /// cancellation/source faults apart from a changed actor.</summary>
            private async Task<ActorAdmissionStatus> ReobserveActorAsync(CancellationToken token)
                => await _host.ReobserveActorAsync(_actor, token);

            public bool TryGetValue(string path, out object? value)
            {
                value = path switch
                {
                    "Notifications" => _rows,
                    "Empty" => _rows.Length == 0,
                    _ => null
                };
                return value is not null;
            }

            public Task LoadAsync(CancellationToken token) => LoadCoreAsync(token);

            public Task LoadCoreAsync(CancellationToken token) => LoadCoreCoreAsync(token);

            private async Task LoadCoreCoreAsync(CancellationToken token)
            {
                // Current actor admission FIRST, before any owning service read.
                if (!IsCurrent()) { Refuse("StalePanel", "This notifications panel is no longer current."); return; }
                var admission = await ReobserveActorAsync(token);
                if (admission != ActorAdmissionStatus.Current)
                {
                    Refuse(UnifiedWorkspaceHost.ActorAdmissionCode(admission), admission == ActorAdmissionStatus.Cancelled
                        ? "The notifications read was cancelled."
                        : "The authenticated profile changed; reopen notifications.");
                    return;
                }
                // Re-check exact panel/generation/full actor AFTER the admission await and
                // immediately BEFORE acquisition; a replacement panel mounted during the await
                // must never be read or published onto.
                if (!IsCurrent()) { Refuse("StalePanel", "This notifications panel is no longer current."); return; }
                var items = await _service.GetRecentAsync(50, includeDismissed: false, token).ConfigureAwait(false);
                // Marshal the visible row mutation and PropertyChanged publication to the UI
                // thread (real async service reads resume off-UI). Revalidate the ACTUAL actor
                // (not merely the cached profile) immediately before publication so a retired
                // or actor-switched panel never publishes onto a replacement surface.
                if (!IsCurrent() || await ReobserveActorAsync(token) != ActorAdmissionStatus.Current) return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsCurrent()) return;
                    _rows = items.Where(item => !item.IsDismissed)
                        .Select(item => new HavenNotificationRow(item.Id, item.Title, item.Message,
                            item.CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture), !item.IsRead))
                        .ToArray();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                }).GetTask().ConfigureAwait(false);
            }

            public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
            {
                switch (command)
                {
                    case "9to1.Haven.ClosePanel":
                        if (!IsCurrent()) { Refuse("StalePanel", "This notifications panel is no longer current."); return; }
                        _host.HideSharedPanel();
                        return;
                    case "9to1.Haven.MarkRead" when TryNotificationId(parameter, out var readId):
                        await RetainBeforeOwner(ct => MarkReadCoreAsync(readId, true, ct), token);
                        return;
                    case "9to1.Haven.MarkRead":
                        Refuse("MalformedCommand", "The notifications mark-read command received a malformed identifier.");
                        return;
                    case "9to1.Haven.Dismiss" when TryNotificationId(parameter, out var dismissId):
                        await RetainBeforeOwner(ct => DismissCoreAsync(dismissId, ct), token);
                        return;
                    case "9to1.Haven.Dismiss":
                        Refuse("MalformedCommand", "The notifications dismiss command received a malformed identifier.");
                        return;
                    default:
                        Refuse("UnknownCommand", $"Unknown notifications command '{command}'.");
                        return;
                }
            }

            private void Refuse(string code, string message) => _host.PublishSharedRefusal(code, message);

            /// <summary>Reserves the retained original-task slot in the host ledger BEFORE the
            /// owning action begins, so a synchronous completion or reentrant close cannot acquire
            /// service work outside the reserved ledger. Mirrors the model-picker dispatcher's
            /// admission gate; faults/refusals are retained, never swallowed as success.</summary>
            private ValueTask RetainBeforeOwner(Func<CancellationToken, Task> produce, CancellationToken token)
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var retained = Run(gate.Task, produce, token);
                _host.Track(retained);
                gate.SetResult();
                return new(retained);

                static async Task Run(Task start, Func<CancellationToken, Task> body, CancellationToken ct)
                {
                    await start;
                    await body(ct);
                }
            }

            private async Task MarkReadCoreAsync(Guid id, bool read, CancellationToken token)
            {
                // Admission + exact currentness BEFORE any service/actor callback.
                if (!IsCurrent()) { Refuse("StalePanel", "This notifications panel is no longer current."); return; }
                var before = await ReobserveActorAsync(token);
                if (before != ActorAdmissionStatus.Current)
                {
                    Refuse(UnifiedWorkspaceHost.ActorAdmissionCode(before), before == ActorAdmissionStatus.Cancelled
                        ? "The notifications mark-read was cancelled."
                        : "The authenticated profile changed; reopen notifications.");
                    return;
                }
                // Validate the requested identifier against the current authorized row membership.
                if (!IsMemberRow(id)) { Refuse("MalformedCommand", "The notification is not in the current authorized list."); return; }
                // Re-check exact panel/generation/full actor AFTER the admission await and
                // immediately BEFORE acquisition; a panel retired (A→B) or actor switched during
                // the await must never allow A's stale mutation to proceed.
                var again = await ReobserveActorAsync(token);
                if (!IsCurrent() || again != ActorAdmissionStatus.Current)
                {
                    Refuse(UnifiedWorkspaceHost.ActorAdmissionCode(again), again == ActorAdmissionStatus.Cancelled
                        ? "The notifications mark-read was cancelled."
                        : "This notifications panel is no longer current.");
                    return;
                }
                await _service.MarkReadAsync(id, read, token).ConfigureAwait(false);
                // Revalidate AFTER async acquisition and immediately before publication. If the
                // panel/actor retired AFTER the committed mutation, preserve the genuine committed
                // result and do not publish onto a replacement surface; never pretend the late
                // retirement rolled the commit back.
                if (!IsCurrent() || await ReobserveActorAsync(token) != ActorAdmissionStatus.Current) return;
                await LoadCoreAsync(token).ConfigureAwait(false);
            }

            private async Task DismissCoreAsync(Guid id, CancellationToken token)
            {
                if (!IsCurrent()) { Refuse("StalePanel", "This notifications panel is no longer current."); return; }
                var before = await ReobserveActorAsync(token);
                if (before != ActorAdmissionStatus.Current)
                {
                    Refuse(UnifiedWorkspaceHost.ActorAdmissionCode(before), before == ActorAdmissionStatus.Cancelled
                        ? "The notifications dismiss was cancelled."
                        : "The authenticated profile changed; reopen notifications.");
                    return;
                }
                if (!IsMemberRow(id)) { Refuse("MalformedCommand", "The notification is not in the current authorized list."); return; }
                // Re-check exact panel/generation/full actor AFTER the admission await and
                // immediately BEFORE acquisition (same fence as mark-read).
                var again = await ReobserveActorAsync(token);
                if (!IsCurrent() || again != ActorAdmissionStatus.Current)
                {
                    Refuse(UnifiedWorkspaceHost.ActorAdmissionCode(again), again == ActorAdmissionStatus.Cancelled
                        ? "The notifications dismiss was cancelled."
                        : "This notifications panel is no longer current.");
                    return;
                }
                await _service.DismissAsync(id, token).ConfigureAwait(false);
                if (!IsCurrent() || await ReobserveActorAsync(token) != ActorAdmissionStatus.Current) return;
                await LoadCoreAsync(token).ConfigureAwait(false);
            }

            private bool IsMemberRow(Guid id) => Array.Exists(_rows, row => row.Id == id);

            private static bool TryNotificationId(object? parameter, out Guid id)
            {
                if (parameter is Guid guid) { id = guid; return true; }
                if (parameter is string text && Guid.TryParse(text, out var parsed)) { id = parsed; return true; }
                id = default;
                return false;
            }
        }

        private sealed record HavenNotificationRow(Guid Id, string Title, string Message, string Time, bool Unread)
            : ICuiBindingContext
        {
            public bool TryGetValue(string path, out object? value)
            {
                value = path switch
                {
                    "Id" => Id,
                    "Title" => Title,
                    "Message" => Message,
                    "Time" => Time,
                    "Unread" => Unread,
                    _ => null
                };
                return value is not null;
            }
        }
    }
}
#endif
