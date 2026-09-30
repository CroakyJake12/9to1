using Haven.Application.Go;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace NineToOne.Os.Shell;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var model = new ShellViewModel();
        using var home = new OsSessionHome(model);
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.Shell.cui") ?? throw new InvalidDataException("The canonical shell CUI document is missing.");
        using var reader = new StreamReader(stream);
        return CuiNativeHost.Run(new("os.shell", "9to1 OS", "os.shell", new CuiRichParser().Parse(reader.ReadToEnd()), model, model, home), args);
    }
}

/// <summary>Designated central OS-session host. Child apps must attach through Home's authenticated transport.</summary>
internal sealed class OsSessionHome(ShellViewModel model) : ICuiSceneReadiness, IDisposable
{
    private ServiceProvider? _services;
    private HomeNativeSessionLease? _lease;
    private Authority.LinuxSessionDiscoveryServer? _discoveryServer;
    private readonly SemaphoreSlim _endpointGate = new(1, 1);
    public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) return new(CuiSceneAvailabilityState.Unavailable, "LinuxHostRequired", "This session shell requires its Linux platform host.");
        var result = await HomeNativeServiceHost.Process.EnsureAsync("9to1.os.session.bundled-home", ComposeAsync,
            ["home.core", "home.state", "apps.installed"], ct);
        if (result.State != HomeNativeHostState.Ready || result.Services is null)
            return new(CuiSceneAvailabilityState.Unavailable, result.Code, result.Message);
        await _endpointGate.WaitAsync(ct);
        try
        {
            _discoveryServer ??= await Authority.LinuxSessionDiscoveryServer.StartAsync(_lease ?? throw new InvalidOperationException("The central Home lease is unavailable."),
                result.Services.GetRequiredService<IAppPaths>(), result.Services.GetRequiredService<IAuthenticatedResourceActorSource>(),
                result.Services.GetRequiredService<HomeCoreRuntime>(), result.Services.GetRequiredService<HomeNativeDiscoverySession>(), ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException or InvalidOperationException)
        { return new(CuiSceneAvailabilityState.Unavailable, "HomeDiscoveryEndpointUnavailable", "The native Home endpoint could not be published safely. Existing Home data was preserved; repair the installed runtime location."); }
        finally { _endpointGate.Release(); }
        model.OpenModels = token => OsModelPickerWindow.OpenAsync(result.Services, token);
        await model.StartAsync(result.Services.GetRequiredService<ShellConfigurationService>(), result.Services.GetRequiredService<GoService>(), result.Services.GetRequiredService<LinuxApplicationLauncher>(), ct);
        return new(CuiSceneAvailabilityState.Ready, "HomeSessionReady", "The canonical OS session Home services are ready.");
    }
    private async Task<HomeNativeServiceSession> ComposeAsync(CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        services.AddSingleton<IInstalledApplicationObservationProvider, LinuxInstalledApplications>();
        // No receipt, clean environment, process-local lease or developer output substitutes for trusted launch.
        services.AddSingleton<IHomeNativeControlledLaunchAuthority, UnavailableHomeNativeControlledLaunchAuthority>();
        services.AddSingleton<Authority.LinuxControlledLaunchGate>();
        services.AddSingleton<Authority.LinuxInstallationPeerVerifier>();
        services.AddSingleton<IHomeNativeInstalledPeerVerifier>(sp => sp.GetRequiredService<Authority.LinuxInstallationPeerVerifier>());
        services.AddSingleton<IHomeNativeSessionHostVerifier>(sp => sp.GetRequiredService<Authority.LinuxInstallationPeerVerifier>());
        services.AddSingleton<HomeNativeDiscoverySession>();
        services.AddSingleton<ICanonicalResourceAccessResolver, ShellConfigurationResourceResolver>();
        services.AddSingleton<ICanonicalResourceAccessResolver, InstalledApplicationResourceResolver>();
        services.AddSingleton<IShellConfigurationStore, HomeShellConfigurationStore>();
        services.AddSingleton<ShellConfigurationService>();
        services.AddSingleton<LinuxApplicationLauncher>();
        services.AddSingleton<IGoProvider, InstalledApplicationsGoProvider>();
        services.AddSingleton<GoService>();
        _services = services.BuildServiceProvider();
        var actors = _services.GetRequiredService<IAuthenticatedResourceActorSource>();
        // No runtime or app authority is started until this process owns the actual profile session lease.
        _lease = await HomeNativeSessionLease.TryAcquireAsync(actors, _services.GetRequiredService<IAppPaths>(), ct);
        if (_lease is null) throw new InvalidOperationException("Another designated Home session host is active. Attach to it or recover Home; a second authority was not started.");
        _services.GetRequiredService<Authority.LinuxControlledLaunchGate>().BindHeldLease(_lease);
        return new(_services, _services.GetRequiredService<HomeCoreRuntime>(), actors);
    }
    public void Dispose() { _discoveryServer?.Dispose(); _services?.Dispose(); _lease?.Dispose(); _endpointGate.Dispose(); }
}
