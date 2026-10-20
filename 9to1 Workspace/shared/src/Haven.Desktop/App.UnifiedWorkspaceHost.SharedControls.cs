#if !ANDROID
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
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

        private void ComposeSharedFrame()
        {
            Dispatcher.UIThread.VerifyAccess();
            var frame = _frameGrid = new Grid();
            frame.Children.Add(_scene!);
            _sharedOverlay = new Border
            {
                IsVisible = false,
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            };
            frame.Children.Add(_sharedOverlay);
            Content = frame;
        }

        private void ShowSharedPanel(CuiControlLoader loader, Control root)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_sharedOverlay is null) return;
            _sharedPanelLoader?.Dispose();
            _sharedPanelLoader = loader;
            _sharedOverlay.Child = root;
            _sharedOverlay.IsVisible = true;
        }

        private void HideSharedPanel()
        {
            Dispatcher.UIThread.VerifyAccess();
            _sharedPanelLoader?.Dispose(); _sharedPanelLoader = null;
            if (_sharedOverlay is not null)
            {
                _sharedOverlay.Child = null;
                _sharedOverlay.IsVisible = false;
            }
        }

        private void ObserveSharedGate(Task actual)
        {
            lock (_gate) _sources.Add(actual);
            _ = ObserveSharedGateAsync(actual);
        }

        private static async void ObserveSharedGateAsync(Task actual)
        {
            try { await actual; }
            catch (Exception failure) { System.Diagnostics.Trace.TraceError("Haven shared control retained failure: {0}", failure); }
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
                ObserveSharedGate(owner.OpenAsync(window));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private const string NotificationsMarkup = """
<Cui>
  <Actions>
    <Action name="CloseNotifications" command="9to1.Haven.ClosePanel" />
    <Action name="MarkRead" command="9to1.Haven.MarkRead" parameter="{Binding n.Id}" />
    <Action name="Dismiss" command="9to1.Haven.Dismiss" parameter="{Binding n.Id}" />
  </Actions>
  <Theme Name="user_preference">
    <Page AccessibleName="Haven notifications">
      <Container Type="Vertical Stack" Padding="24" CellGap="12" Width="520" MaxHeight="600" Background="{Resource CuiBackgroundBrush}" BorderColor="{Resource CuiLineBrush}" BorderWidth="1" CornerRadius="12">
        <Container Type="Grid" ColumnSizes="1fr Auto">
          <Text Text="Notifications" FontSize="20" FontWeight="Bold" VerticalAlignment="Center" />
          <Button action="CloseNotifications" Column="1" AccessibleName="Close notifications" Tooltip="Close notifications" Width="32" Height="32" Padding="4" FontSize="16" Background="Transparent" BorderWidth="0" CornerRadius="8">×</Button>
        </Container>
        <Container Type="Vertical Stack" CellGap="8" VerticalScrolling="True" HorizontalScrolling="False">
          <Repeat Source="{Binding Notifications}" As="n" Key="{Binding n.Id}">
            <Container Type="Vertical Stack" CellGap="4" Padding="12" Background="{Resource CuiPanelBrush}" CornerRadius="8" BorderColor="{Resource CuiLineBrush}" BorderWidth="1">
              <Text Text="{Binding n.Title}" FontWeight="SemiBold" />
              <Text Text="{Binding n.Message}" TextWrapping="Wrap" Foreground="{Resource CuiTextSoftBrush}" />
              <Container Type="Horizontal Stack" CellGap="8">
                <Text Text="{Binding n.Time}" FontSize="11" Foreground="{Resource CuiTextSoftBrush}" VerticalAlignment="Center" />
                <Button action="MarkRead" Text="Mark read" Interactive="{Binding n.Unread}" AccessibleName="Mark this notification read" />
                <Button action="Dismiss" Text="Dismiss" AccessibleName="Dismiss this notification" />
              </Container>
            </Container>
          </Repeat>
          <If condition="{Binding Empty}">
            <Text Text="No notifications." Foreground="{Resource CuiTextSoftBrush}" />
          </If>
        </Container>
      </Container>
    </Page>
  </Theme>
</Cui>
""";

        private bool OpenNotificationsPanel()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_closed || _startupRetirement) return false;
            var service = _services.GetService<HavenNotificationService>();
            if (service is null) return false;
            var bindings = new HavenNotificationsCuiBindings(this, service);
            var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup(NotificationsMarkup, "HavenNotifications.cui");
            if (root is null || diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            {
                loader.Dispose();
                return false;
            }
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            loader.WireBindings(root);
            ObserveSharedGate(LoadNotificationsAsync(bindings));
            ShowSharedPanel(loader, root);
            return true;
        }

        private async Task LoadNotificationsAsync(HavenNotificationsCuiBindings bindings)
        {
            try { await bindings.LoadAsync(CancellationToken.None); }
            catch (Exception failure) { System.Diagnostics.Trace.TraceError("Haven notifications refresh failed: {0}", failure); }
        }

        /// <summary>Opens the authentic Home Universal Model Picker for the current profile
        /// (User scope only; "global" is unsupported). If the real feature provider is not
        /// composed this host degrades truthfully: the rail model button stays disabled.</summary>
        private bool OpenModelPickerPanel()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_closed || _startupRetirement) return false;
            var provider = _services.GetService<IHomeModelPickerFeatureProvider>();
            if (provider is null) return false;
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
            loader.SetBindingContext(controller.Surface);
            loader.SetActionDispatcher(new HomeModelPickerDispatcher(controller, this));
            var loaded = loader.TryLoad(controller.Surface.Document);
            if (loaded.Root is null || loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            {
                loader.Dispose();
                return false;
            }
            loader.WireBindings(loaded.Root);
            ObserveSharedGate(RefreshModelPickerAsync(controller));
            ShowSharedPanel(loader, loaded.Root);
            return true;
        }

        private async Task RefreshModelPickerAsync(HomeModelPickerCuiController controller)
        {
            try { await controller.OpenAsync("user", "chat", CancellationToken.None); }
            catch (Exception failure) { System.Diagnostics.Trace.TraceError("Haven model picker refresh failed: {0}", failure); }
        }

        /// <summary>Routes model-picker CUI actions through the Home controller. The authored
        /// document conveys row-specific attributes (route/provider/model/capability) as action
        /// metadata that the standard dispatcher contract does not deliver, so only the
        /// self-contained structural actions are genuinely executed here; the parameter-bearing
        /// mutations are a coordinated framework dependency and are never fabricated.</summary>
        private sealed class HomeModelPickerDispatcher(HomeModelPickerCuiController controller, UnifiedWorkspaceHost host)
            : ICuiActionDispatcher
        {
            private readonly HomeModelPickerCuiController _controller = controller;
            private readonly UnifiedWorkspaceHost _host = host;

            public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
            {
                return command switch
                {
                    "Refresh" => DispatchAsync(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(HomeModelPickerAction.Refresh), cancellationToken).AsTask()),
                    "SaveRoute" => DispatchAsync(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(HomeModelPickerAction.SaveRoute), cancellationToken).AsTask()),
                    "FinishAudit" => DispatchAsync(() => _controller.ExecuteAsync(new HomeModelPickerActionRequest(HomeModelPickerAction.FinishAudit), cancellationToken).AsTask()),
                    _ => ValueTask.CompletedTask
                };
            }

            private ValueTask DispatchAsync(Func<Task> action)
            {
                var task = action();
                _host.ObserveSharedGate(task);
                return new(task);
            }
        }

        /// <summary>Read-only CUI projection over the durable Home notification service.</summary>
        private sealed class HavenNotificationsCuiBindings(UnifiedWorkspaceHost host, HavenNotificationService service)
            : ICuiBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
        {
            private readonly UnifiedWorkspaceHost _host = host;
            private readonly HavenNotificationService _service = service;
            private HavenNotificationRow[] _rows = [];

            public event PropertyChangedEventHandler? PropertyChanged;

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

            public async Task LoadAsync(CancellationToken token)
            {
                var items = await _service.GetRecentAsync(50, includeDismissed: false, token).ConfigureAwait(false);
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
                    case "9to1.Haven.MarkRead":
                        if (TryNotificationId(parameter, out var readId)) return MarkReadAsync(readId, true, token);
                        return ValueTask.CompletedTask;
                    case "9to1.Haven.Dismiss":
                        if (TryNotificationId(parameter, out var dismissId)) return DismissAsync(dismissId, token);
                        return ValueTask.CompletedTask;
                    default:
                        return ValueTask.CompletedTask;
                }
            }

            private static bool TryNotificationId(object? parameter, out Guid id)
            {
                if (parameter is Guid guid) { id = guid; return true; }
                if (parameter is string text && Guid.TryParse(text, out var parsed)) { id = parsed; return true; }
                id = default;
                return false;
            }

            private async ValueTask MarkReadAsync(Guid id, bool read, CancellationToken token)
            {
                try { await _service.MarkReadAsync(id, read, token); await LoadAsync(token); }
                catch (Exception failure) { System.Diagnostics.Trace.TraceError("Haven notification read failed: {0}", failure); }
            }

            private async ValueTask DismissAsync(Guid id, CancellationToken token)
            {
                try { await _service.DismissAsync(id, token); await LoadAsync(token); }
                catch (Exception failure) { System.Diagnostics.Trace.TraceError("Haven notification dismiss failed: {0}", failure); }
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
