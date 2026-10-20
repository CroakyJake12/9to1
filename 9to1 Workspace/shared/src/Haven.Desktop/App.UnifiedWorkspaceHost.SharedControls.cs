#if !ANDROID
using System.ComponentModel;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
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
            public bool ModelSupported => _host._services.GetService<IHomeModelPickerFeatureProvider>() is not null;
            public bool NotificationsSupported => _host._services.GetService<HavenNotificationService>() is not null;
            public bool ProfileSupported => _host._services.GetService<NativeCakeAccountUiOwner>() is not null;
            public IHavenSharedControlGateway? SharedControls => _host._sharedGateway;
        }

        /// <summary>Typed gateway to the genuine Home-owned shared surfaces owned by this host.</summary>
        private sealed class HavenGateway(UnifiedWorkspaceHost host) : IHavenSharedControlGateway
        {
            private readonly UnifiedWorkspaceHost _host = host;

            public bool OpenModelPicker() => _host.OpenModelPickerPanel();
            public bool OpenNotifications() => _host.OpenNotificationsPanel();
            public bool OpenProfile() => _host.OpenProfilePanel();
        }

        /// <summary>Monotonic generation for the currently mounted shared panel. A panel that
        /// opened earlier is retired the moment another panel or close replaces it; stale async
        /// completions from a retired panel must never mutate the replacement surface.</summary>
        private long _sharedPanelGeneration;

        /// <summary>Composes the authored main scene with a thin native placement seam. All
        /// shared-panel structure, styling, dimming, visibility and interaction are authored in
        /// packaged .cui documents; this host never builds modal UI in C#.</summary>
        private void ComposeSharedFrame()
        {
            Dispatcher.UIThread.VerifyAccess();
            var frame = _frameGrid = new Grid();
            frame.Children.Add(_scene!);
            _sharedOverlay = new Border { IsVisible = false };
            frame.Children.Add(_sharedOverlay);
            Content = frame;
        }

        private void ShowSharedPanel(CuiControlLoader loader, Control root)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_sharedOverlay is null) { loader.Dispose(); return; }
            if (_sharedPanelLoader is { } previous)
            {
                // Join the replaced panel's retained action/disposal settlement before
                // discarding it so its failed originals remain observable to host close.
                previous.Dispose();
                Track(previous.WhenActionsIdleAsync());
            }
            _sharedPanelLoader = loader;
            _sharedOverlay.Child = root;
            _sharedOverlay.IsVisible = true;
        }

        private void HideSharedPanel()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_sharedPanelLoader is { } present)
            {
                present.Dispose();
                Track(present.WhenActionsIdleAsync());
                _sharedPanelLoader = null;
            }
            if (_sharedOverlay is not null)
            {
                _sharedOverlay.Child = null;
                _sharedOverlay.IsVisible = false;
            }
            Interlocked.Increment(ref _sharedPanelGeneration);
        }

        private bool OpenProfilePanel()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_closed || _startupRetirement) return false;
            var owner = _services.GetService<NativeCakeAccountUiOwner>();
            if (owner is null) return false;
            var window = TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The Haven host window is unavailable.");
            try
            {
                Track(owner.OpenAsync(window));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool OpenNotificationsPanel()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_closed || _startupRetirement) return false;
            var service = _services.GetService<HavenNotificationService>();
            var theme = _homeTheme?.Context;
            if (service is null || theme is null) return false;
            var generation = checked(++_sharedPanelGeneration);
            var profile = _actor?.ProfileId;
            var bindings = new HavenNotificationsCuiBindings(this, service, generation, profile);
            var loader = new CuiControlLoader();
            // Configure binding/theme/action context before loading so the authored Repeat
            // captures this panel's real notification source and the genuine Home theme.
            loader.SetThemePreferenceContext(theme);
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            var loaded = loader.TryLoad(HavenNotificationsCuiDocument.Load());
            if (loaded.Root is null || loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            {
                loader.Dispose();
                return false;
            }
            loader.WireBindings(loaded.Root);
            Track(bindings.LoadAsync(CancellationToken.None));
            ShowSharedPanel(loader, loaded.Root);
            return true;
        }

        /// <summary>Opens the authentic Home Universal Model Picker for the current profile
        /// (User scope only; "global" is unsupported). If the real feature provider is not
        /// composed this host degrades truthfully: the rail model button stays disabled.</summary>
        private bool OpenModelPickerPanel()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_closed || _startupRetirement) return false;
            var provider = _services.GetService<IHomeModelPickerFeatureProvider>();
            var theme = _homeTheme?.Context;
            if (provider is null || theme is null) return false;
            var generation = checked(++_sharedPanelGeneration);
            var profile = _actor?.ProfileId;
            HomeModelPickerCuiController controller;
            try
            {
                controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(provider));
            }
            catch
            {
                return false;
            }
            var loader = new CuiControlLoader();
            loader.SetThemePreferenceContext(theme);
            loader.SetBindingContext(controller.Surface);
            loader.SetActionDispatcher(new HomeModelPickerDispatcher(controller, this));
            var loaded = loader.TryLoad(controller.Surface.Document);
            if (loaded.Root is null || loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            {
                loader.Dispose();
                return false;
            }
            loader.WireBindings(loaded.Root);
            Track(RefreshModelPickerAsync(controller, generation, profile));
            ShowSharedPanel(loader, loaded.Root);
            return true;
        }

        private async Task RefreshModelPickerAsync(HomeModelPickerCuiController controller, long generation, string? profile)
        {
            await controller.OpenAsync("user", "chat", CancellationToken.None).ConfigureAwait(false);
            if (!IsPanelCurrent(generation, profile)) return;
        }

        private bool IsPanelCurrent(long generation, string? profile) =>
            !_closed && !_startupRetirement &&
            generation == Volatile.Read(ref _sharedPanelGeneration) &&
            StringComparer.Ordinal.Equals(profile, _actor?.ProfileId);

        /// <summary>Reads the authored Haven notifications CUI document from the packaged
        /// compiled transport rather than an inline C# string constant.</summary>
        private static class HavenNotificationsCuiDocument
        {
            public static CuiDocument Load()
            {
                using var stream = typeof(App).Assembly.GetManifestResourceStream("Haven.Desktop.Home.HavenNotifications.cui")
                    ?? throw new InvalidOperationException("The authored Haven notifications CUI document is missing.");
                using var reader = new StreamReader(stream);
                return new CuiRichParser().Parse(reader.ReadToEnd());
            }
        }

        /// <summary>Routes model-picker CUI actions through the Home controller using canonical
        /// named &lt;Action parameter=...&gt; declarations. Every parameter-bearing action builds its
        /// typed request from the bound selection object; no command is a silent no-op unless the
        /// feature provider is genuinely unavailable.</summary>
        private sealed class HomeModelPickerDispatcher(HomeModelPickerCuiController controller, UnifiedWorkspaceHost host)
            : ICuiActionDispatcher
        {
            private readonly HomeModelPickerCuiController _controller = controller;
            private readonly UnifiedWorkspaceHost _host = host;

            public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
            {
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
        /// retired panel can never publish or mutate a replacement surface.</summary>
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

            private async Task LoadCoreAsync(CancellationToken token)
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
                await _service.MarkReadAsync(id, read, token).ConfigureAwait(false);
                if (!IsCurrent()) return;
                await LoadCoreAsync(token).ConfigureAwait(false);
            }

            private async Task DismissCoreAsync(Guid id, CancellationToken token)
            {
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
