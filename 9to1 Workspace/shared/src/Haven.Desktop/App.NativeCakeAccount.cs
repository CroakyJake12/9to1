#if !ANDROID
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Accounts.Native;
using Haven.Desktop.Accounts;
using Haven.Desktop.Services;

namespace Haven.Desktop;

public sealed partial class App
{
    private NativeCakeAccountUiOwner? _actualNativeCakeAccountOwner;
    private INativeCakeAccountSession? _actualNativeCakeAccountSession;
    private INativeCakeAccessCredentialSource? _actualNativeCakeAccountAccess;
    private bool _nativeCakeBorrowerTransferred;

    // Observation of this actual App's permanent admission seal, never identity or authority.
    internal static bool IsOriginalDesktopRetiring => Avalonia.Application.Current is App actual &&
        (actual._originalAppWork.IsRetiring || actual._actualShutdownDelivery is not null);

    private void AddNativeCakeAccountServices(IServiceCollection services)
    {
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
        original.DemandPublication();
        Type[] owners = [typeof(INativeCakeAccountSession), typeof(INativeCakeAccessCredentialSource), typeof(NativeCakeAccountUiOwner)];
        if (services.Any(row => owners.Contains(row.ServiceType)))
            throw new InvalidOperationException("The actual native CAKE account owners are already configured.");
        const string origin = "https://cake-id-release-validation.jcbailey008.workers.dev";
        const string issuer = origin + "/api/auth";
        var options = new NativeCakeClientOptions(issuer, "xwKChQGPLRpdokMkLAMvzimLZxnwmnJt", "native", "none",
            NativeCakeClientOptions.RequiredRedirect, origin, new Uri(origin + "/"),
            new Uri(issuer + "/.well-known/openid-configuration"), new Uri(issuer + "/oauth2/authorize"),
            new Uri(issuer + "/oauth2/token"), new Uri(issuer + "/jwks"));
        // The reviewed package stages the exact pinned official Windows helper here.
        // No PATH/env discovery, browser-cookie borrowing or Access-policy change occurs.
        // Missing helper leaves sign-in explicitly unavailable; a wrong pin is refused by
        // the existing original helper before any authenticated process is launched.
        var helper = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "auth", "cloudflared.exe"));
        if (OperatingSystem.IsWindows() && File.Exists(helper))
            services.AddSingleton<INativeCakeAccessCredentialSource>(_ =>
                _actualNativeCakeAccountAccess = new CloudflaredNativeCakeAccessCredentialSource(helper));
        services.AddSingleton<INativeCakeAccountSession>(provider =>
            _actualNativeCakeAccountSession = new NativeCakeAccountSession(options,
                provider.GetService<INativeCakeAccessCredentialSource>()));
        services.AddSingleton<NativeCakeAccountUiOwner>(provider =>
            _actualNativeCakeAccountOwner = new(provider.GetRequiredService<INativeCakeAccountSession>(),
                provider.GetService<INativeCakeAccessCredentialSource>()));
        original.DemandPublication();
        return true;
        }));
    }

    private object CaptureOriginalNativeCakeAccountBorrower(IServiceProvider provider)
    {
        NativeCakeAccountUiOwner? actual = null;
        _originalAppWork.RunSynchronous(original =>
        {
            actual = AcquireOriginalAppSynchronous(original, provider.GetRequiredService<NativeCakeAccountUiOwner>);
            original.DemandPublication();
        });
        return actual ?? throw new InvalidOperationException("The actual native CAKE account owner is unavailable.");
    }

    private void DemandOriginalNativeCakeAccountRetirementJoin() =>
        _actualNativeCakeAccountOwner?.DemandExternalOriginalRetirementJoin();

    private async Task JoinOriginalUntransferredNativeCakeAccountAsync()
    {
        lock (_actualStartupAcquisitionGate) if (_nativeCakeBorrowerTransferred) return;
        DemandOriginalNativeCakeAccountRetirementJoin();
        List<Exception> failures = []; List<Task> originals = [];
        void Acquire(Func<Task> source)
        {
            try { _originalAppWork.RunCloseCallback(() => originals.Add(source())); }
            catch (Exception cause) { AddAppCause(failures, cause); }
        }
        if (_actualNativeCakeAccountOwner is { } owner) Acquire(owner.CloseAndDrainAsync);
        // Partial provider construction may have acquired these before a later constructor
        // failed. Capture their same closes independently; coalescing prevents duplicate work.
        if (_actualNativeCakeAccountSession is { } session) Acquire(session.CloseAndDrainAsync);
        if (_actualNativeCakeAccountAccess is { } access) Acquire(access.CloseAndDrainAsync);
        foreach (var actual in originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalAppTaskAsync(actual, failures);
        ThrowAppCauses(failures);
    }
}
#endif
