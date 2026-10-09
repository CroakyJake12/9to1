using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using NineToOne.Accounts.Native;

namespace AvaloniaHome;

internal sealed partial class HomeApp
{
    private readonly HomeNativeCakeAccountOwner _nativeCakeAccount;
    private void RegisterNativeCakeAccountActions()
    {
        _nativeCakeAccount.InitializeStandalone();
        _viewModel.Set("IsNativeHomeShell", true);
        _viewModel.Set("IsNativeCakeAccountWindow", false);
        _viewModel.Set("CakeAccountHeading", "CAKE ID account");
        _viewModel.Set("CakeAccountStatus", "Connect your CAKE ID account. Local Home uses your operating-system profile; account sign-in grants no Home permissions.");
        _viewModel.On("OpenHomeCakeAccount", _ => _ = RunOriginalNativeWork(() => _nativeCakeAccount.OpenAsync(_workspaceWindow)));
        _viewModel.SetActionAvailability("OpenHomeCakeAccount", true); // Opening setup UI, not authentication availability.
    }
    private Task PrepareOriginalNativeAccountClose()
    {
        // Refuse self-joins before even an existing close retrieval or parent admission change.
        _nativeCakeAccount.DemandExternalJoin();
        return _nativeCakeAccount.CloseAndDrainAsync();
    }
    internal HomeNativeCakeAccountOwner NativeCakeAccount => _nativeCakeAccount;
}

/// <summary>Home window/source custody. Standalone owns its client+Access helper; composition
/// injection borrows the exact existing session. Neither variant grants Home identity.</summary>
internal sealed class HomeNativeCakeAccountOwner
{
    internal const string Origin = "https://cake-id-release-validation.jcbailey008.workers.dev";
    internal const string PublicClientId = "xwKChQGPLRpdokMkLAMvzimLZxnwmnJt";
    private readonly object _gate = new();
    private readonly bool _ownsSession;
    private INativeCakeAccountSession? _session;
    private INativeCakeAccessCredentialSource? _access;
    private readonly List<HomeNativeCakeAccountWindow> _windows = new();
    private readonly List<Task> _opens = new();
    private HomeNativeCakeAccountWindow? _active;
    private Task? _close;
    private bool _closing, _initialized;
    internal HomeNativeCakeAccountOwner(INativeCakeAccountSession? borrowedSession = null)
    { _session = borrowedSession; _ownsSession = borrowedSession is null; _initialized = borrowedSession is not null; }
    internal HomeNativeCakeAccountOwner(INativeCakeAccountSession ownedSession, INativeCakeAccessCredentialSource ownedAccess)
    { _session = ownedSession; _access = ownedAccess; _ownsSession = true; _initialized = true; }
    internal static NativeCakeClientOptions ExactOptions()
    {
        var issuer = Origin + "/api/auth";
        return new(issuer, PublicClientId, "native", "none", NativeCakeClientOptions.RequiredRedirect, Origin,
            new Uri(Origin + "/"), new Uri(issuer + "/.well-known/openid-configuration"),
            new Uri(issuer + "/oauth2/authorize"), new Uri(issuer + "/oauth2/token"), new Uri(issuer + "/jwks"));
    }
    internal void InitializeStandalone()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_initialized) return;
            _initialized = true;
            var helper = Path.Combine(AppContext.BaseDirectory, "auth", "cloudflared.exe");
            if (OperatingSystem.IsWindows() && File.Exists(helper))
                _access = new CloudflaredNativeCakeAccessCredentialSource(helper); // Published before client acquisition.
            _session = new NativeCakeAccountSession(_access is null ? null : ExactOptions(), _access);
        }
    }
    internal void DemandExternalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate) foreach (var window in _windows) window.DemandExternalJoin();
    }
    internal Task OpenAsync(Window? parent)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (!_initialized || _session is null) throw new InvalidOperationException("The original native account client is not initialized.");
            _opens.RemoveAll(original => original.IsCompletedSuccessfully);
            _windows.RemoveAll(window => window.IsRetired && window.OriginalClose?.IsCompletedSuccessfully == true && window.Initialization.IsCompletedSuccessfully);
            if (_opens.Count >= 16 || _windows.Count >= 16) throw new InvalidOperationException("Close Home before admitting more retained account presentations.");
            var start = new TaskCompletionSource(); var original = OpenCoreAsync(start.Task, parent);
            _opens.Add(original); start.SetResult(); return original;
        }
    }
    private async Task OpenCoreAsync(Task start, Window? parent)
    {
        await start.ConfigureAwait(true);
        using var scope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        HomeNativeCakeAccountWindow acquired;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_active is { IsRetired: false, OriginalClose: null } current)
            { current.Activate(); acquired = current; }
            else
            {
                acquired = new HomeNativeCakeAccountWindow(_session!, _ownsSession);
                _windows.Add(acquired); _active = acquired; // Actual window custody before native Show/initialization.
                var initialization = acquired.StartOriginalInitialization();
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { if (parent is null) acquired.Show(); else acquired.Show(parent); return true; });
            }
        }
        await acquired.Initialization.ConfigureAwait(true);
    }
    internal Task CloseAndDrainAsync()
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_close is not null) return _close;
            _closing = true;
            var start = new TaskCompletionSource();
            _close = CloseCoreAsync(start.Task, _opens.ToArray(), _windows.ToArray()); start.SetResult(); return _close;
        }
    }
    private async Task CloseCoreAsync(Task start, Task[] opens, HomeNativeCakeAccountWindow[] windows)
    {
        await start.ConfigureAwait(true);
        using var scope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var errors = new List<Exception>(); var originals = new List<Task>(opens);
        foreach (var window in windows)
        {
            try
            {
                var actual = Dispatcher.UIThread.CheckAccess() ? window.CloseAndDrainAsync()
                    : Dispatcher.UIThread.InvokeAsync(window.CloseAndDrainAsync);
                originals.Add(actual);
            }
            catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        }
        // Stop actual owned network/helper sources before joining any operation that they encompass.
        // Borrowed client's owner remains responsible; view cancellation already reached its calls.
        if (_ownsSession && _session is not null)
            try { originals.Add(CloudflareOriginalExecutionGuard.InvokeOriginal(this, _session.CloseAndDrainAsync)); }
            catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        if (_ownsSession && _access is not null)
            try { originals.Add(CloudflareOriginalExecutionGuard.InvokeOriginal(this, _access.CloseAndDrainAsync)); }
            catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        foreach (var actual in originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await HomeNativeCakeCauses.JoinAsync(actual, errors).ConfigureAwait(true);
        HomeNativeCakeCauses.Throw(errors); _active = null;
    }
}
