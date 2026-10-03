using Haven.Application.Go;
using Haven.Application.Compatibility;
using HavenOS.Files.NativeHost;
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
        if (OperatingSystem.IsLinux())
        {
            var ownerExit = Authority.LinuxOriginalInstalledOwnerMainDispatch.TryRunAsync(args, CancellationToken.None).GetAwaiter().GetResult();
            if (ownerExit is not null) return ownerExit.Value;
            // A transition failure exits this process before any profile, backend or GUI IO.
            // The root supervisor still must issue independent actual launch evidence.
            try { args = Authority.LinuxControlledHomeChildStartup.PrepareBeforeProfileIo(args); }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or
                System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException)
            {
                Console.Error.WriteLine("The required administrator Home startup boundary is unavailable.");
                return 1;
            }
            var paired = Authority.LinuxRootPairedInstalledRuntimeCommand.TryRunAsync(args, CancellationToken.None).GetAwaiter().GetResult();
            if (paired is not null) return paired.Value;
            var supervisor = Authority.LinuxRootHomeSupervisorCommand.TryRunAsync(args, CancellationToken.None).GetAwaiter().GetResult();
            if (supervisor is not null) return supervisor.Value;
            var enrollment = Authority.LinuxInstalledPackageEnrollmentCommand.TryRunAsync(args, CancellationToken.None).GetAwaiter().GetResult();
            if (enrollment is not null) return enrollment.Value;
        }
        using var model = new ShellViewModel();
        using var home = new OsSessionHome(model);
        if (OperatingSystem.IsLinux() && Authority.LinuxControlledHomeChildStartup.ServiceOnly)
        {
            try { home.RunSupervisedServiceAsync(CancellationToken.None).GetAwaiter().GetResult(); return 0; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Net.Sockets.SocketException)
            { Console.Error.WriteLine("The original supervised Home service retired or could not start."); return 1; }
        }
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.Shell.cui") ?? throw new InvalidDataException("The canonical shell CUI document is missing.");
        using var reader = new StreamReader(stream);
        return CuiNativeHost.Run(new("os.shell", "9to1 OS", "os.shell", new CuiRichParser().Parse(reader.ReadToEnd()), model, model, home) { ControlRegistry = TaskbarLayerSurface.CreateRegistry(model) }, args);
    }
}

/// <summary>Designated central OS-session host. Child apps must attach through Home's authenticated transport.</summary>
internal sealed class OsSessionHome(ShellViewModel model) : ICuiSceneReadiness, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private ServiceProvider? _services;
    private HomeNativeSessionLease? _lease;
    private Authority.LinuxOriginalHomeLaunchAuthoritySlot? _originalHomeAuthority;
    private Task? _canonicalChannelTask;
    private Task? _widgetObservationChannelTask;
    private CancellationToken _canonicalChannelToken;
    private Authority.LinuxSessionDiscoveryServer? _discoveryServer;
    private Authority.LinuxNativeWidgetRegistrationServer? _widgetRegistrationServer;
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
            _widgetRegistrationServer ??= await Authority.LinuxNativeWidgetRegistrationServer.StartAsync(
                _lease ?? throw new InvalidOperationException("The central Home lease is unavailable."),
                result.Services.GetRequiredService<IAppPaths>(), result.Services.GetRequiredService<IAuthenticatedResourceActorSource>(),
                result.Services.GetRequiredService<HomeCoreRuntime>(), result.Services.GetRequiredService<HomeNativeWidgetRegistry>(), ct);
            if (Authority.LinuxControlledHomeChildStartup.OriginalCanonicalSocket is { } originalCanonicalSocket && _canonicalChannelTask is null)
                _canonicalChannelTask = Authority.LinuxOriginalHomeCanonicalChannel.ServeOriginalTupleWithWidgetRouteAsync(
                    originalCanonicalSocket, _lease ?? throw new InvalidOperationException("Original Home lease unavailable."), result.Services.GetRequiredService<IAuthenticatedResourceActorSource>(),
                    result.Services.GetRequiredService<IInstalledApplicationRegistry>(), result.Services.GetRequiredService<ResourceAuthorizationService>(),
                    _widgetRegistrationServer.Location, _canonicalChannelToken);
            if (Authority.LinuxControlledHomeChildStartup.OriginalWidgetObservationSocket is { } originalWidgetSocket && _widgetObservationChannelTask is null)
                _widgetObservationChannelTask = Authority.LinuxOriginalHomeCanonicalChannel.ServeOriginalWidgetObservationsAsync(
                    originalWidgetSocket, _lease ?? throw new InvalidOperationException("Original Home lease unavailable."),
                    result.Services.GetRequiredService<IAuthenticatedResourceActorSource>(), result.Services.GetRequiredService<IInstalledApplicationRegistry>(),
                    result.Services.GetRequiredService<ResourceAuthorizationService>(), result.Services.GetRequiredService<HomeNativeWidgetRegistry>(), _canonicalChannelToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException or InvalidOperationException)
        { return new(CuiSceneAvailabilityState.Unavailable, "HomeDiscoveryEndpointUnavailable", "The native Home endpoint could not be published safely. Existing Home data was preserved; repair the installed runtime location."); }
        finally { _endpointGate.Release(); }
        if (Authority.LinuxControlledHomeChildStartup.ServiceOnly)
            return new(CuiSceneAvailabilityState.Ready, "HomeServiceEndpointsReady", "The actual original Home graph and native service endpoints are ready.");
        model.OpenModels = token => OsModelPickerWindow.OpenAsync(result.Services, token);
        model.OpenDulche = (token, lifetime) => OsDulcheWindow.OpenAsync(result.Services, model.RefreshAsync, token, lifetime);
        await model.StartAsync(result.Services.GetRequiredService<ShellConfigurationService>(), result.Services.GetRequiredService<GoService>(), result.Services.GetRequiredService<LinuxApplicationLauncher>(), ct);
        return new(CuiSceneAvailabilityState.Ready, "HomeSessionReady", "The canonical OS session Home services are ready.");
    }
    private async Task<HomeNativeServiceSession> ComposeAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The original Home graph requires its Linux platform host.");
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        // Files content authority and OS navigation share this exact leased Home graph.
        services.AddFilesNativeHost();
        services.AddSingleton<CompatibilityPackageInspector>();
        services.AddSingleton<ICompatibilityPackageOpenHandler>(sp => new OsCompatibilityPackageWindow(sp, _lifetime.Token));
        services.AddSingleton<FilesCompatibilityPackageOpenCoordinator>();
        services.AddSingleton<IInstalledApplicationObservationProvider, LinuxInstalledApplications>();
        // No receipt, clean environment, process-local lease or developer output substitutes for trusted launch.
        if (Authority.LinuxControlledHomeChildStartup.OriginalCanonicalSocket is not null)
            services.AddSingleton<IHomeNativeControlledLaunchAuthority>(_originalHomeAuthority = new());
        else services.AddSingleton<IHomeNativeControlledLaunchAuthority, UnavailableHomeNativeControlledLaunchAuthority>();
        // Home always uses its own actually held local lease. A registered owner-side
        // remote context port must not change construction mode through greedy DI.
        services.AddSingleton<Authority.LinuxControlledLaunchGate>(sp => new(
            sp.GetRequiredService<IAuthenticatedResourceActorSource>(),
            sp.GetRequiredService<IHomeNativeControlledLaunchAuthority>()));
        services.AddSingleton<Authority.LinuxInstallationPeerVerifier>();
        services.AddSingleton<IHomeNativeInstalledPeerVerifier>(sp => sp.GetRequiredService<Authority.LinuxInstallationPeerVerifier>());
        services.AddSingleton<IHomeNativeSessionHostVerifier>(sp => sp.GetRequiredService<Authority.LinuxInstallationPeerVerifier>());
        services.AddSingleton<HomeNativeDiscoverySession>();
        services.AddSingleton<ICanonicalResourceAccessResolver, ShellConfigurationResourceResolver>();
        services.AddSingleton<ICanonicalResourceAccessResolver, InstalledApplicationResourceResolver>();
        services.AddSingleton<IShellConfigurationStore, HomeShellConfigurationStore>();
        services.AddSingleton<ShellConfigurationService>();
        services.AddSingleton<IHomeActionPolicySource, ShellSemanticActionPolicies>();
        services.AddSingleton<ShellSemanticFeatureProvider>();
        services.AddSingleton<LinuxApplicationLauncher>();
        services.AddOsCompatibilityManager(_lifetime.Token);
        services.AddSingleton<IGoProvider, InstalledApplicationsGoProvider>();
        services.AddSingleton<IGoProvider, ShellNavigationGoProvider>();
        services.AddSingleton<GoService>();
        _services = services.BuildServiceProvider();
        var actors = _services.GetRequiredService<IAuthenticatedResourceActorSource>();
        // No runtime or app authority is started until this process owns the actual profile session lease.
        _lease = await HomeNativeSessionLease.TryAcquireAsync(actors, _services.GetRequiredService<IAppPaths>(), ct);
        if (_lease is null) throw new InvalidOperationException("Another designated Home session host is active. Attach to it or recover Home; a second authority was not started.");
        _services.GetRequiredService<Authority.LinuxControlledLaunchGate>().BindHeldLease(_lease);
        if (_originalHomeAuthority is not null)
        {
            var authority = await Authority.LinuxAdministratorOriginalHomeLaunchClient.CaptureAsync(
                Authority.LinuxControlledHomeChildStartup.OriginalAdministratorSocket ?? throw new UnauthorizedAccessException("Original administrator route required."),
                _lease, actors, ct);
            if (authority is null) throw new UnauthorizedAccessException("Original Home authority could not be captured.");
            if (!_originalHomeAuthority.BindOriginal(authority))
            { await authority.DisposeAsync(); throw new UnauthorizedAccessException("Original Home authority binding refused."); }
        }
        return new(_services, _services.GetRequiredService<HomeCoreRuntime>(), actors);
    }
    internal async Task RunSupervisedServiceAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux supervised Home required.");
        if (Authority.LinuxControlledHomeChildStartup.OriginalAdministratorSocket is not { } socket)
            throw new UnauthorizedAccessException("No admitted original administrator startup.");
        if (Authority.LinuxControlledHomeChildStartup.OriginalCanonicalSocket is { } canonicalSocket)
        {
            await RunCanonicalSupervisedServiceAsync(socket, canonicalSocket, ct);
            return;
        }
        var ready = await CheckAsync(ct);
        if (ready.State != CuiSceneAvailabilityState.Ready || _services is null || _lease is null || !_lease.IsHeld)
            throw new InvalidOperationException("The actual maintained Home graph was not ready.");
        await Authority.LinuxHomeChildLeaseChannel.ServeOriginalLeaseAsync(socket, _lease,
            _services.GetRequiredService<IAuthenticatedResourceActorSource>(),
            _services.GetRequiredService<IAppPaths>(), ct);
    }
    private async Task RunCanonicalSupervisedServiceAsync(string leaseSocket, string canonicalSocket, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The original canonical Home route requires its Linux platform host.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        _canonicalChannelToken = lifetime.Token;
        var graph = await HomeNativeServiceHost.Process.EnsureAsync("9to1.os.session.bundled-home", ComposeAsync,
            ["home.core", "home.state", "apps.installed"], lifetime.Token);
        if (graph.State != HomeNativeHostState.Ready || _services is null || _lease is null || !_lease.IsHeld)
            throw new InvalidOperationException("Actual original Home graph/lease unavailable.");
        var actors = _services.GetRequiredService<IAuthenticatedResourceActorSource>();
        var registry = _services.GetRequiredService<IInstalledApplicationRegistry>();
        // This channel reports the actual held lease, never service/UI readiness.
        var leaseTask = Authority.LinuxHomeChildLeaseChannel.ServeOriginalLeaseAsync(leaseSocket, _lease,
            actors, _services.GetRequiredService<IAppPaths>(), lifetime.Token);
        Task? canonicalTask = null;
        Exception? primary = null;
        try
        {
            if (!await Authority.LinuxOriginalHomeCanonicalInitialization.ReconcileForDesignatedHomeAsync(_lease, actors, registry, lifetime.Token))
                throw new UnauthorizedAccessException("Original canonical inventory initialization refused.");
            var ready = await CheckAsync(lifetime.Token);
            if (ready.State != CuiSceneAvailabilityState.Ready)
                throw new InvalidOperationException("Actual maintained Home services unavailable.");
            canonicalTask = _canonicalChannelTask ?? throw new InvalidOperationException("Actual original canonical route channel unavailable.");
            var channels = _widgetObservationChannelTask is { } widgets ? new[] { leaseTask, canonicalTask, widgets } : new[] { leaseTask, canonicalTask };
            await await Task.WhenAny(channels);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try { lifetime.Cancel(); } catch (Exception error) { primary ??= error; }
            foreach (var task in new[] { leaseTask, canonicalTask ?? _canonicalChannelTask, _widgetObservationChannelTask })
            {
                if (task is null) continue;
                try { await task; }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception error) { primary ??= error; }
            }
            if (_originalHomeAuthority is not null)
                try { await _originalHomeAuthority.DisposeAsync(); } catch (Exception error) { primary ??= error; }
        }
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }
    public void Dispose()
    {
        Exception? first = null;
        try { _lifetime.Cancel(); } catch (Exception error) { first = error; }
        try { if (OperatingSystem.IsLinux()) _originalHomeAuthority?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch (Exception error) { first ??= error; }
        foreach (IDisposable? owned in new IDisposable?[] { _widgetRegistrationServer, _discoveryServer, _services, _lease, _lifetime, _endpointGate })
            try { owned?.Dispose(); } catch (Exception error) { first ??= error; }
        if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
    }
}
