using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using NineToOne.Accounts.Native;

namespace AvaloniaHome;

/// <summary>Native owning window loads the same canonical Home.cui account section.
/// It borrows the original client; its close drains view work, never closes a borrowed session.</summary>
internal sealed class HomeNativeCakeAccountWindow : Window
{
    private readonly HomeNativeCakeAccountBindings _bindings;
    private readonly CuiControlLoader _loader = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<Exception> _ownErrors = new();
    private bool _closing, _retired;
    private Task? _close;
    internal Task Initialization { get; private set; } = Task.CompletedTask;
    internal bool IsRetired => _retired;
    internal Task? OriginalClose => _close;
    internal HomeNativeCakeAccountWindow(INativeCakeAccountSession originalSession, bool ownsOriginalSession)
    {
        Title = "CAKE ID · 9-1 Home"; Width = 720; Height = 720; MinWidth = 360; MinHeight = 420;
        _bindings = new(originalSession, () => Dispatcher.UIThread.Post(() => { if (!_closing && !_retired) Close(); }), ownsOriginalSession);
        _bindings.PropertyChanged += Changed;
        _clock.Tick += Tick; Closing += RequestClose;
    }
    internal Task StartOriginalInitialization()
    {
        Dispatcher.UIThread.VerifyAccess();
        var start = new TaskCompletionSource(); Initialization = InitializeAsync(start.Task); start.SetResult(); return Initialization;
    }
    private async Task InitializeAsync(Task start)
    {
        await start.ConfigureAwait(true);
        using var scope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        if (_closing) throw new ObjectDisposedException(nameof(HomeNativeCakeAccountWindow));
        var path = Program.FindCuiFile() ?? throw new FileNotFoundException("The canonical Home account surface is unavailable.");
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            _loader.SetBindingContext(_bindings); _loader.SetActionDispatcher(_bindings);
            var (root, diagnostics) = _loader.LoadFile(path);
            if (root is null || diagnostics.Count != 0) throw new InvalidDataException("The canonical Home account surface could not be loaded without diagnostics.");
            _loader.WireBindings(root); Content = root; _clock.Start(); return true;
        });
        var originalRefresh = _bindings.Start("RefreshCakeAccount");
        await originalRefresh.ConfigureAwait(true);
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (_closing) return;
        Dispatcher.UIThread.VerifyAccess();
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _loader.RefreshBindings(); return true; });
    }
    private void Tick(object? sender, EventArgs args)
    {
        if (_closing) return;
        try { _bindings.RefreshPrivacyFence(); }
        catch (Exception error) { HomeNativeCakeCauses.Add(_ownErrors, error); _ = CloseAndDrainAsync(); }
    }
    private void RequestClose(object? sender, WindowClosingEventArgs args)
    { if (_retired) return; args.Cancel = true; _ = CloseAndDrainAsync(); }
    internal void DemandExternalJoin()
    { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _bindings.DemandExternalJoin(); }
    internal Task CloseAndDrainAsync()
    {
        Dispatcher.UIThread.VerifyAccess(); DemandExternalJoin();
        if (_close is not null) return _close;
        _closing = true;
        var start = new TaskCompletionSource(); _close = CloseCoreAsync(start.Task); start.SetResult(); return _close;
    }
    private async Task CloseCoreAsync(Task start)
    {
        await start.ConfigureAwait(true);
        using var scope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var errors = new List<Exception>(); Task? viewClose = null;
        // SAME published window close before stop/event callbacks. Stop before initialization join.
        try { viewClose = _bindings.CloseAndDrainAsync(); } catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _clock.Stop(); _clock.Tick -= Tick; return true; }); } catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        await HomeNativeCakeCauses.JoinAsync(Initialization, errors).ConfigureAwait(true);
        if (viewClose is not null) await HomeNativeCakeCauses.JoinAsync(viewClose, errors).ConfigureAwait(true);
        foreach (var error in _ownErrors) HomeNativeCakeCauses.Add(errors, error);
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _bindings.PropertyChanged -= Changed; _loader.Dispose(); Content = null; return true; }); }
        catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        _retired = true; Closing -= RequestClose;
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { Close(); return true; }); } catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        HomeNativeCakeCauses.Throw(errors);
    }
}
