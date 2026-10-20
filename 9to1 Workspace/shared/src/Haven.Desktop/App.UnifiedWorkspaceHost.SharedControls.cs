#if !ANDROID
using System.ComponentModel;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Desktop.Accounts;
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

        /// <summary>Monotonic generation for the currently mounted shared panel. A panel that
        /// opened earlier is retired the moment another panel or close replaces it; stale async
        /// completions from a retired panel must never mutate the replacement surface.</summary>
        private long _sharedPanelGeneration;

        /// <summary>The typed asynchronous gateway the authored rail uses to open the genuine
        /// Home-owned shared surfaces. Wired by the integration patch (lane5-owned host).</summary>
        private HavenGateway? _sharedGateway;

        /// <summary>Retained original-session leases for composed model providers; disposed at close.</summary>
        private readonly List<HomeModelPickerOriginalSessionProvider> _modelSessions = [];

        /// <summary>The authored shared-panel shell loader/bindings and its named host element.</summary>
        private CuiControlLoader? _shellLoader;
        private SharedPanelShellBindings? _shellBindings;
        private Control? _sharedPanelHost;

        /// <summary>The currently mounted shared panel loader (if any). Its retained action/disposal
        /// settlement is joined at close before borrowed owners are released.</summary>
        private CuiControlLoader? _sharedPanelLoader;

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
                return await produce();
            }
        }

        /// <summary>Composes the authored main scene and the authored shared-panel shell. All
        /// shell structure, backdrop/dimming, spacing and visibility are authored in packaged
        /// .cui documents; this host only mounts the scene and the active panel into named host
        /// elements. It never builds modal UI or visible chrome in C#.</summary>
        private void ComposeSharedFrame()
        {
            Dispatcher.UIThread.VerifyAccess();
            var frame = new Grid();
            frame.Children.Add(_scene!);
            // The authored shared-panel shell is mounted from the packaged compiled transport.
            // If the compiled bundle registration is not yet aligned (BLOCKED dependency), the host
            // degrades truthfully: the scene still mounts and panels remain unavailable until root
            // emits the bundle. Never silently reparses raw source.
            var shellBindings = new SharedPanelShellBindings();
            var shellLoader = new CuiControlLoader();
            try
            {
                shellLoader.SetThemePreferenceContext(_homeTheme.Context);
                shellLoader.SetBindingContext(shellBindings);
                shellLoader.SetActionDispatcher(shellBindings);
                var loaded = shellLoader.TryLoad(HavenSharedPanelShellDocument.Load());
                if (loaded.Root is null || loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
                {
                    shellLoader.Dispose();
                    _shellBindings = null; _sharedPanelHost = null;
                }
                else
                {
                    shellLoader.WireBindings(loaded.Root);
                    var host = FindNamedChild(loaded.Root, SharedPanelHostId);
                    if (host is null)
                    {
                        shellLoader.Dispose();
                        _shellBindings = null; _sharedPanelHost = null;
                    }
                    else
                    {
                        _shellLoader = shellLoader; _shellBindings = shellBindings; _sharedPanelHost = host;
                        frame.Children.Add(loaded.Root);
                    }
                }
            }
            catch (CuiRuntimeLoadException)
            {
                shellLoader.Dispose();
                _shellBindings = null; _sharedPanelHost = null;
            }
            Content = frame;
        }

        private const string SharedPanelHostId = "shared-panel-host";

        private static Control? FindNamedChild(Control root, string name) =>
            root.GetLogicalDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name);

        /// <summary>Retires the previous panel BEFORE any synchronous Dispose/cancellation reentry:
        /// dispose is first reserved, its settlement (including failures and post-Dispose cleanup)
        /// is retained, and a late callback from the retired panel can never retire the replacement.</summary>
        private void ReserveRetirementCustody(CuiControlLoader previous)
        {
            try { previous.Dispose(); }
            catch (Exception error) { Track(Task.FromException(error)); }
            Track(previous.WhenActionsIdleAsync());
        }

        private void ShowSharedPanel(CuiControlLoader loader, Control root)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_sharedPanelHost is null) { loader.Dispose(); return; }
            if (_sharedPanelLoader is { } present && !ReferenceEquals(present, loader))
                ReserveRetirementCustody(present);
            _sharedPanelLoader = loader;
            _sharedPanelHost.Children.Clear();
            _sharedPanelHost.Children.Add(root);
            if (_shellBindings is not null) _shellBindings.HasOpenPanel = true;
        }

        private void HideSharedPanel()
        {
            Dispatcher.UIThread.VerifyAccess();
            // Retire generation first so stale completions from the retired panel refuse immediately.
            Interlocked.Increment(ref _sharedPanelGeneration);
            if (_sharedPanelLoader is { } present)
            {
                ReserveRetirementCustody(present);
                _sharedPanelLoader = null;
            }
            if (_sharedPanelHost is not null) _sharedPanelHost.Children.Clear();
            if (_shellBindings is not null) _shellBindings.HasOpenPanel = false;
        }

        /// <summary>Full host currentness (app/window/chrome retirement) plus the mounted panel
        /// generation and the actual profile this panel was opened for. Never the weak
        /// _closed/_startupRetirement-only check.</summary>
        private bool IsPanelCurrent(long generation, string? profile) =>
            IsCurrent &&
            generation == Volatile.Read(ref _sharedPanelGeneration) &&
            StringComparer.Ordinal.Equals(profile, _actor?.ProfileId);

        /// <summary>Joins the shared-panel settlement for host close: the mounted panel's retained
        /// action/disposal settlement (including any post-Dispose async cleanup), then disposal of
        /// composed original-session leases. This is invoked by the lane5 close path via the
        /// exact-base integration patch (App.UnifiedWorkspaceHost.cs is lane5-owned).</summary>
        internal async Task JoinSharedPanelSettlementAsync(List<Exception> failures)
        {
            if (_sharedPanelLoader is { } panelLoader)
            {
                try { await JoinOriginalAppTaskAsync(panelLoader.WhenActionsIdleAsync(), failures); }
                catch (Exception error) { AddAppCause(failures, error); }
                try { panelLoader.Dispose(); } catch (Exception error) { AddAppCause(failures, error); }
                _sharedPanelLoader = null;
            }
            foreach (var session in _modelSessions.ToArray())
            {
                try { session.Dispose(); } catch (Exception error) { AddAppCause(failures, error); }
            }
            _modelSessions.Clear();
            if (_shellLoader is not null)
            {
                try { _shellLoader.Dispose(); } catch (Exception error) { AddAppCause(failures, error); }
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
            try
            {
                // The native owner reserves its own open ledger before invoking any callback and
                // requires the UI thread; retain its genuine task so close can join it.
                var original = owner.OpenAsync(window);
                Track(original);
                return AdmitOriginal(async () =>
                {
                    await original.ConfigureAwait(false);
                    return HavenSharedOpenResult.Succeeded();
                });
            }
            catch (Exception error)
            {
                return Task.FromResult(HavenSharedOpenResult.Refused("ProfileOpenFailed", error.Message));
            }
        }

        private Task<HavenSharedOpenResult> OpenNotificationsPanelAsync(CancellationToken cancellationToken)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsCurrent) return Task.FromResult(HavenSharedOpenResult.Denied);
            var service = _services.GetService<HavenNotificationService>();
            var theme = _homeTheme?.Context;
            if (service is null || theme is null)
                return Task.FromResult(HavenSharedOpenResult.Refused("NotificationsUnavailable", "The durable Home notification service is unavailable."));
            var generation = checked(++_sharedPanelGeneration);
            var profile = _actor?.ProfileId;
            var bindings = new HavenNotificationsCuiBindings(this, service, generation, profile);
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
                // Resolve the real, same-owner composition once, bound to the current actor.
                var actor = await ObserveActorAsync(cancellationToken).ConfigureAwait(false);
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
                    var begun = await session.BeginAsync(cancellationToken).ConfigureAwait(false);
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
                loader.SetActionDispatcher(new HomeModelPickerDispatcher(controller, this, generation, actor.ProfileId));
                var loaded = loader.TryLoad(controller.Surface.Document);
                if (loaded.Root is null || loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
                {
                    loader.Dispose();
                    return HavenSharedOpenResult.Refused("PanelLoadFailed", "The authored model-picker panel failed to load.");
                }
                loader.WireBindings(loaded.Root);
                _modelSessions.Add(session);
                // Admission was reserved by this outer AdmitOriginal before the provider call; the
                // refresh is joined through the same ledger.
                _ = AdmitOriginal(() => RefreshModelPickerAsync(controller, generation, actor.ProfileId, CancellationToken.None));
                ShowSharedPanel(loader, loaded.Root);
                return HavenSharedOpenResult.Succeeded();
            });
        }

        private async Task<HavenSharedOpenResult> RefreshModelPickerAsync(
            HomeModelPickerCuiController controller, long generation, string? expectedProfile, CancellationToken cancellationToken)
        {
            // Re-observe the ACTUAL current Home actor (not the startup-cached _actor) and refuse
            // before any work if it no longer matches this panel's profile.
            var actor = await ObserveActorAsync(cancellationToken).ConfigureAwait(false);
            if (actor is null || !StringComparer.Ordinal.Equals(actor.ProfileId, expectedProfile)) return HavenSharedOpenResult.Denied;
            if (!IsPanelCurrent(generation, expectedProfile)) return HavenSharedOpenResult.Denied;
            // controller.PublicationGate revalidates immediately before applying surface state, so
            // a retirement between here and publication still cannot publish.
            await controller.OpenAsync("User", "chat", cancellationToken).ConfigureAwait(false);
            return IsPanelCurrent(generation, expectedProfile) ? HavenSharedOpenResult.Succeeded() : HavenSharedOpenResult.Denied;
        }

        private static async Task<HavenSharedOpenResult> ThenResult(Task load)
        {
            try { await load.ConfigureAwait(false); }
            catch (OperationCanceledException) { return HavenSharedOpenResult.Refused("Cancelled", "The shared panel load was cancelled."); }
            catch (Exception error) { return HavenSharedOpenResult.Refused("LoadFailed", error.Message); }
            return HavenSharedOpenResult.Succeeded();
        }

        /// <summary>Reads the authored Haven shared-panel shell CUI document from the packaged
        /// compiled transport (hard format/ABI/registry/capability admission) with no raw parser
        /// fallback.</summary>
        private static class HavenSharedPanelShellDocument
        {
            public const string CompiledSourceName = "Views/Pages/Home/HavenSharedPanelShell.cui";
            public static CuiDocument Load() => CompiledCuiDocument.Load(CompiledSourceName);
        }

        /// <summary>Reads the authored Haven notifications CUI document from the packaged compiled
        /// transport. The compiled bundle is emitted by the CUI build target; if the bundle or the
        /// requested source is absent this fails fast with a structured load exception rather than
        /// silently reparsing raw source (BLOCKED on the aligned build emission; see the lane4
        /// integration patch for the registration).</summary>
        private static class HavenNotificationsCuiDocument
        {
            public const string CompiledSourceName = "Views/Pages/Home/HavenNotifications.cui";
            public static CuiDocument Load() => CompiledCuiDocument.Load(CompiledSourceName);
        }

        /// <summary>Shared consumer for the packaged compiled CUI bundle. It validates format,
        /// version, language, runtime ABI, component registry and required capabilities before the
        /// validated source snapshot is reconstructed via the canonical parser — exactly the root/
        /// lane2 agreed V2 corrective transport. No loose raw-source fallback exists.</summary>
        private static class CompiledCuiDocument
        {
            public const string CompiledArtifactResource = "CUI.Compilation.v1";

            public static CuiDocument Load(string sourceName)
            {
                var assembly = typeof(UnifiedWorkspaceHost).Assembly;
                using var stream = assembly.GetManifestResourceStream(CompiledArtifactResource)
                    ?? throw new CuiRuntimeLoadException(new CuiDiagnostic(
                        "CUIR069", CuiDiagnosticSeverity.Error,
                        $"The packaged compiled CUI bundle '{CompiledArtifactResource}' was not found. The project must emit the canonical compiled bundle.", Span));
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return Load(buffer.ToArray(), sourceName);
            }

            public static CuiDocument Load(byte[] artifactBytes, string sourceName)
            {
                var artifact = CuiCompiledArtifactCodec.ReadArtifact(artifactBytes);
                if (!string.Equals(artifact.Format, CuiCompiledArtifactCodec.FormatName, StringComparison.Ordinal))
                    throw new CuiRuntimeLoadException(new CuiDiagnostic(
                        "CUIR060", CuiDiagnosticSeverity.Error,
                        $"Unsupported compiled CUI artifact format '{artifact.Format}'.", Span));
                if (artifact.FormatVersion != CuiCompiledArtifactCodec.FormatVersion)
                    throw new CuiRuntimeLoadException(new CuiDiagnostic(
                        "CUIR061", CuiDiagnosticSeverity.Error,
                        $"Unsupported compiled CUI artifact format version {artifact.FormatVersion}.", Span));
                var document = artifact.Documents.FirstOrDefault(entry => string.Equals(entry.Source, sourceName, StringComparison.Ordinal))
                    ?? throw new CuiRuntimeLoadException(new CuiDiagnostic(
                        "CUIR062", CuiDiagnosticSeverity.Error,
                        $"Compiled CUI bundle does not contain '{sourceName}'.", Span));
                var metadata = document.Compilation.Metadata;
                if (!CuiRuntimeCompatibility.IsLanguageVersionCompatible(metadata.LanguageVersion))
                    throw new CuiRuntimeLoadException(new CuiDiagnostic(
                        "CUIR064", CuiDiagnosticSeverity.Error,
                        $"Compiled CUI language version '{metadata.LanguageVersion}' is not supported by this runtime.", Span));
                if (!string.Equals(metadata.RuntimeAbiVersion, CuiRuntimeCompatibility.RuntimeAbiVersion, StringComparison.Ordinal))
                    throw new CuiRuntimeLoadException(new CuiDiagnostic(
                        "CUIR065", CuiDiagnosticSeverity.Error,
                        $"Compiled CUI runtime ABI '{metadata.RuntimeAbiVersion}' does not match the current runtime ABI '{CuiRuntimeCompatibility.RuntimeAbiVersion}'.", Span));
                if (!string.Equals(metadata.ComponentRegistryVersion, CuiControlRegistry.ComponentRegistryVersion, StringComparison.Ordinal))
                    throw new CuiRuntimeLoadException(new CuiDiagnostic(
                        "CUIR066", CuiDiagnosticSeverity.Error,
                        $"Compiled CUI component registry '{metadata.ComponentRegistryVersion}' does not match the current registry '{CuiControlRegistry.ComponentRegistryVersion}'.", Span));
                foreach (var capability in metadata.RequiredCapabilities)
                {
                    if (!CuiRuntimeCompatibility.Capabilities.Contains(capability))
                        throw new CuiRuntimeLoadException(new CuiDiagnostic(
                            "CUIR067", CuiDiagnosticSeverity.Error,
                            $"Compiled CUI document requires unsupported capability '{capability}'.", Span));
                }
                var parser = new CuiRichParser();
                var semantics = parser.Parse(document.Compilation.SourceText, sourceName);
                if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
                    throw new CuiRuntimeLoadException(new CuiDiagnostic(
                        "CUIR068", CuiDiagnosticSeverity.Error,
                        $"Compiled CUI bundle for '{sourceName}' failed snapshot revalidation.", Span));
                if (semantics.Components.Count == 0)
                    throw new CuiRuntimeLoadException(new CuiDiagnostic(
                        "CUIR063", CuiDiagnosticSeverity.Error,
                        $"Compiled semantic model for '{sourceName}' is empty.", Span));
                return semantics;
            }

            private static readonly CuiSourceSpan Span = CuiSourceSpan.At("compiled.cui", 1, 1);
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
            long generation, string? profile)
            : ICuiActionDispatcher
        {
            private readonly HomeModelPickerCuiController _controller = controller;
            private readonly UnifiedWorkspaceHost _host = host;
            private readonly long _generation = generation;
            private readonly string? _profile = profile;

            public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
            {
                // Admission fence: refuse before invoking any owning operation when this panel is
                // no longer the mounted/current one.
                if (!_host.IsPanelCurrent(_generation, _profile)) return ValueTask.CompletedTask;
                return command switch
                {
                    "Refresh" => Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(
                        HomeModelPickerAction.Refresh,
                        Scope: (parameter as HomeModelPickerScopeSelection)?.Scope ?? _controller.Surface.Scope,
                        Category: (parameter as HomeModelPickerScopeSelection)?.Category ?? _controller.Surface.Category), cancellationToken)),
                    "SelectCategory" => Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(
                        HomeModelPickerAction.SelectCategory, Category: parameter as string), cancellationToken)),
                    "SelectRoute" => Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(
                        HomeModelPickerAction.SelectRoute, RouteId: (parameter as HomeModelPickerRouteSelection)?.RouteId), cancellationToken)),
                    "SetCandidateEnabled" => DispatchCandidateToggle(parameter, cancellationToken),
                    "MoveCandidateUp" => DispatchCandidateMove(HomeModelPickerAction.MoveCandidateUp, parameter, cancellationToken),
                    "MoveCandidateDown" => DispatchCandidateMove(HomeModelPickerAction.MoveCandidateDown, parameter, cancellationToken),
                    "SaveRoute" => Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(HomeModelPickerAction.SaveRoute), cancellationToken)),
                    "FinishAudit" => Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(HomeModelPickerAction.FinishAudit), cancellationToken)),
                    "PreviewResolution" => Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(
                        HomeModelPickerAction.PreviewResolution, Capability: parameter as string), cancellationToken)),
                    _ => ValueTask.CompletedTask
                };
            }

            private ValueTask DispatchCandidateToggle(object? parameter, CancellationToken token) =>
                parameter is HomeModelPickerCandidateToggleSelection sel
                    ? Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(
                        HomeModelPickerAction.SetCandidateEnabled, ProviderId: sel.ProviderId, ModelId: sel.ModelId,
                        ArtifactRevision: sel.ArtifactRevision, Enabled: sel.Enabled), token))
                    : ValueTask.CompletedTask;

            private ValueTask DispatchCandidateMove(HomeModelPickerAction action, object? parameter, CancellationToken token) =>
                parameter is HomeModelPickerCandidateSelection sel
                    ? Dispatch(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(action,
                        ProviderId: sel.ProviderId, ModelId: sel.ModelId, ArtifactRevision: sel.ArtifactRevision), token))
                    : ValueTask.CompletedTask;

            private ValueTask Dispatch(Func<Task> action)
            {
                var task = action();
                _host.Track(task);
                return new(task);
            }
        }

        /// <summary>Read-only CUI projection over the durable Home notification service. It
        /// fences every completion against the panel generation/profile it was opened for, so a
        /// retired panel can never publish or mutate a replacement surface. Commands revalidate
        /// currentness BEFORE invoking the owning service, so a retired panel refuses without
        /// service mutation.</summary>
        private sealed class HavenNotificationsCuiBindings(UnifiedWorkspaceHost host, HavenNotificationService service,
            long generation, string? profile)
            : ICuiBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
        {
            private readonly UnifiedWorkspaceHost _host = host;
            private readonly HavenNotificationService _service = service;
            private readonly long _generation = generation;
            private readonly string? _profile = profile;
            private HavenNotificationRow[] _rows = [];

            public event PropertyChangedEventHandler? PropertyChanged;

            private bool IsCurrent() => _host.IsPanelCurrent(_generation, _profile);

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
                var items = await _service.GetRecentAsync(50, includeDismissed: false, token).ConfigureAwait(false);
                if (!IsCurrent()) return;
                _rows = items.Where(item => !item.IsDismissed)
                    .Select(item => new HavenNotificationRow(item.Id, item.Title, item.Message,
                        item.CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture), !item.IsRead))
                    .ToArray();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            }

            public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
            {
                switch (command)
                {
                    case "9to1.Haven.ClosePanel":
                        if (!IsCurrent()) return ValueTask.CompletedTask;
                        _host.HideSharedPanel();
                        return ValueTask.CompletedTask;
                    case "9to1.Haven.MarkRead" when TryNotificationId(parameter, out var readId):
                        return Retain(MarkReadCoreAsync(readId, true, token));
                    case "9to1.Haven.Dismiss" when TryNotificationId(parameter, out var dismissId):
                        return Retain(DismissCoreAsync(dismissId, token));
                    default:
                        return ValueTask.CompletedTask;
                }
            }

            private ValueTask Retain(Task task)
            {
                _host.Track(task);
                return new(task);
            }

            private async Task MarkReadCoreAsync(Guid id, bool read, CancellationToken token)
            {
                if (!IsCurrent()) return; // refuse before service mutation
                await _service.MarkReadAsync(id, read, token).ConfigureAwait(false);
                if (!IsCurrent()) return; // revalidate after async acquisition, before publication
                await LoadCoreAsync(token).ConfigureAwait(false);
            }

            private async Task DismissCoreAsync(Guid id, CancellationToken token)
            {
                if (!IsCurrent()) return;
                await _service.DismissAsync(id, token).ConfigureAwait(false);
                if (!IsCurrent()) return;
                await LoadCoreAsync(token).ConfigureAwait(false);
            }

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
