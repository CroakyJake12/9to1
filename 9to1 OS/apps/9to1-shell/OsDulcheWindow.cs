using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Cui.AI;

namespace NineToOne.Os.Shell;

/// <summary>Native window over the shared AI bar and actual Home review surface.</summary>
internal static class OsDulcheWindow
{
    public static async Task OpenAsync(IServiceProvider services, Func<CancellationToken, Task> refreshShell, CancellationToken ct, CancellationToken hostLifetime)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
                ?? throw new InvalidOperationException("The native shell window is unavailable.");
            var configuration = services.GetRequiredService<ShellConfigurationService>();
            var expected = (await configuration.GetAsync(ct)).Stored;
            if (expected.SessionActor is null) throw new UnauthorizedAccessException("The current Home shell session is unavailable.");
            using var stream = typeof(OsDulcheWindow).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.ShellAiReview.cui")
                ?? throw new InvalidDataException("The shell review surface is missing.");
            using var reader = new StreamReader(stream);
            var footerDocument = new CuiRichParser().Parse(await reader.ReadToEndAsync(ct));
            var owner = services.GetRequiredService<ShellSemanticFeatureProvider>();
            var actions = new ShellAppAiActions(configuration, owner);
            FloatingAiBarState state;
            try { state = services.GetRequiredService<IAppAiCoordinatorFactory>().Create(new ShellAppAiContext(configuration, expected), actions); }
            catch { actions.Dispose(); throw; }
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime);
            using var initial = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            var window = new Window { Title = "Dulche · 9to1 OS", Width = 780, Height = 650 };
            var sharedScene = new CuiSceneHost(); var footerScene = new CuiSceneHost();
            async Task RequireSession(CancellationToken token)
            { if (!await configuration.IsCurrentSessionAsync(expected, token)) throw new UnauthorizedAccessException("Home changed; reopen the shell assistant."); }
            async Task Review(string requestId, CancellationToken token)
            {
                await RequireSession(token);
                using var surface = new HomeApprovalCuiSurface(services.GetRequiredService<HomeCoreRuntime>(),
                    services.GetRequiredService<HomeLocalProfileIdentity>(), services.GetRequiredService<HomePermissionTrustService>());
                await surface.InitializeAsync(token); await surface.FocusRequestAsync(requestId, token);
                var dialog = new Window { Title = "Home permissions", Width = 880, Height = 720, Content = surface };
                using var cancel = token.Register(() => Dispatcher.UIThread.Post(dialog.Close));
                await dialog.ShowDialog(window);
            }
            async Task<bool> HasAudit(CancellationToken token) => (await owner.ReadPendingAuditIdsAsync(expected, token)).Count > 0;
            async Task RecoverAudit(CancellationToken token)
            {
                foreach (var id in await owner.ReadPendingAuditIdsAsync(expected, token))
                    if (!await owner.RetryAuditAsync(id, token)) throw new IOException("The activity record is still pending. Retry recording; do not repeat the preview.");
            }
            var bindings = new ShellDulcheBindings(state, actions, RequireSession, Review, refreshShell, HasAudit, RecoverAudit,
                () => Dispatcher.UIThread.Post(window.Close), lifetime.Token);
            window.Content = new ScrollViewer { Content = new StackPanel { Spacing = 12, Children = { sharedScene, footerScene } } };
            CancellationTokenRegistration cancelWindow = default;
            var closed = false;
            void DisposeOwned()
            {
                if (closed) return; closed = true;
                cancelWindow.Dispose(); lifetime.Cancel(); bindings.Dispose(); sharedScene.Dispose(); footerScene.Dispose(); window.Content = null; lifetime.Dispose();
            }
            window.Closed += (_, _) => DisposeOwned();
            window.Deactivated += (_, _) => { if (!closed) { bindings.SetActive(false); state.Cancel(); } };
            window.Activated += (_, _) => { if (!closed) bindings.SetActive(true); };
            try
            {
                var readiness = new HomeProfileCuiReadiness(services.GetRequiredService<HomeCoreRuntime>(), services.GetRequiredService<IAuthenticatedResourceActorSource>());
                await RequireSession(initial.Token);
                var available = await sharedScene.ShowAsync(new(ShellSemanticFeatureProvider.AppId, "Shell assistant", "os.shell.dulche",
                    new CuiRichParser().Parse(FloatingAiBarScene.ReadSource()), bindings, bindings, readiness), initial.Token);
                if (available.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(available.Message);
                var footerReady = await footerScene.ShowAsync(new(ShellSemanticFeatureProvider.AppId, "Shell preview review", "os.shell.dulche.review", footerDocument, bindings, bindings, readiness), initial.Token);
                if (footerReady.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(footerReady.Message);
                await RequireSession(initial.Token);
                cancelWindow = hostLifetime.Register(() => Dispatcher.UIThread.Post(window.Close));
                state.Expand(); window.Show(mainWindow); bindings.Refresh();
            }
            catch { window.Close(); DisposeOwned(); throw; }
        });
    }
}

internal sealed class ShellDulcheBindings : ICuiWritableBindingContext, ICuiActionDispatcher, INotifyPropertyChanged, IDisposable
{
    private readonly CuiViewModel _bindings = new();
    private readonly FloatingAiBarState _state;
    private readonly ShellAppAiActions _actions;
    private readonly Func<CancellationToken, Task> _requireSession;
    private readonly Func<string, CancellationToken, Task> _review;
    private readonly Func<CancellationToken, Task> _refreshShell;
    private readonly Func<CancellationToken, Task<bool>> _hasAudit;
    private readonly Func<CancellationToken, Task> _recoverAudit;
    private readonly Action _close;
    private readonly CancellationToken _lifetime;
    private bool _disposed;
    private bool _active = true;
    public ShellDulcheBindings(FloatingAiBarState state, ShellAppAiActions actions, Func<CancellationToken, Task> requireSession,
        Func<string, CancellationToken, Task> review, Func<CancellationToken, Task> refreshShell, Func<CancellationToken, Task<bool>> hasAudit,
        Func<CancellationToken, Task> recoverAudit, Action close, CancellationToken lifetime)
    { _state = state; _actions = actions; _requireSession = requireSession; _review = review; _refreshShell = refreshShell; _hasAudit = hasAudit; _recoverAudit = recoverAudit; _close = close; _lifetime = lifetime; state.Changed += Changed; }
    public event PropertyChangedEventHandler? PropertyChanged
    { add => _bindings.PropertyChanged += value; remove => _bindings.PropertyChanged -= value; }
    private void Changed(object? sender, EventArgs args) => Refresh();
    public void Refresh() => _ = RefreshSafelyAsync();
    private async Task RefreshSafelyAsync()
    {
        if (_disposed) return;
        try
        {
            await _requireSession(_lifetime);
            var hasAudit = await _hasAudit(_lifetime);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || !_active || _lifetime.IsCancellationRequested) return;
                _bindings.Set("AccessModeLabel", _state.AccessModeLabel); _bindings.Set("ModelPickerLabel", _state.ModelPickerLabel);
                _bindings.Set("ModelPickerAvailable", _state.ModelPickerAvailable); _bindings.Set("IsCollapsed", _state.Mode == FloatingAiBarMode.Collapsed);
                _bindings.Set("IsExpanded", _state.Mode != FloatingAiBarMode.Collapsed); _bindings.Set("ContextLabel", _state.ContextLabel ?? string.Empty);
                _bindings.Set("Prompt", _state.Prompt); _bindings.Set("RequestStateLabel", _state.RequestStateLabel);
                _bindings.Set("IsStreaming", _state.Mode == FloatingAiBarMode.Streaming); _bindings.Set("Response", _state.Response);
                _bindings.Set("Error", _state.Error ?? string.Empty); _bindings.Set("HasError", !string.IsNullOrEmpty(_state.Error));
                _bindings.Set("HasPendingReview", _actions.PendingReviewRequestId is not null); _bindings.Set("HasPendingAudit", hasAudit);
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { await Dispatcher.UIThread.InvokeAsync(() => { ClearResponse(); _close(); }); }
    }
    public void SetActive(bool active) { _active = active; if (active) Refresh(); else ClearResponse(); }
    public void ClearResponse() { _bindings.Set("Response", string.Empty); _bindings.Set("ContextLabel", string.Empty); }
    public bool TryGetValue(string path, out object? value) => _bindings.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value)
    { if (_disposed || path != "Prompt") return false; _state.Prompt = value?.ToString() ?? string.Empty; return true; }
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime);
        try
        {
            await _requireSession(request.Token);
            switch (command)
            {
                case "SetReadOnly": _state.SetReadOnly(); break;
                case "SetWriteMode": _state.SetWriteMode(); break;
                case "Expand": _state.Expand(); break;
                case "Collapse": _state.Collapse(); break;
                case "Cancel": _state.Cancel(); break;
                case "SelectNextModel": await _state.SelectNextModelAsync(request.Token); break;
                case "Submit": await _state.SubmitAsync(request.Token); break;
                case "Review": if (_actions.PendingReviewRequestId is { } id) await _review(id, request.Token); break;
                case "Retry": if (_actions.PendingActionRequest is { } retained) await _state.ExecuteActionAsync(retained, request.Token); break;
                case "RetryAudit": await _recoverAudit(request.Token); break;
                default: return;
            }
            await _requireSession(request.Token); await _refreshShell(request.Token); Refresh();
        }
        catch (UnauthorizedAccessException)
        {
            ClearResponse(); _close(); throw;
        }
    }
    public void Dispose()
    { if (_disposed) return; _disposed = true; _state.Changed -= Changed; _actions.Dispose(); _state.Dispose(); ClearResponse(); }
}
