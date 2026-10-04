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
                WriteControlledStartupRefusal(error);
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
        var home = new OsSessionHome(model);
        if (OperatingSystem.IsLinux() && Authority.LinuxControlledHomeChildStartup.ServiceOnly)
        {
            try
            {
                RunOriginalBodyAndCloseAsync(() => home.RunSupervisedServiceAsync(CancellationToken.None),
                    home.DisposeAsync).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception original)
            {
                Console.Error.WriteLine("The original supervised Home service retired or could not start.");
                home.WriteOriginalSupervisedFailure(original);
                return 1;
            }
        }
        using var originalHome = home;
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.Shell.cui") ?? throw new InvalidDataException("The canonical shell CUI document is missing.");
        using var reader = new StreamReader(stream);
        return CuiNativeHost.Run(new("os.shell", "9to1 OS", "os.shell", new CuiRichParser().Parse(reader.ReadToEnd()), model, model, home) { ControlRegistry = TaskbarLayerSurface.CreateRegistry(model) }, args);
    }
    internal static async Task RunOriginalBodyAndCloseAsync(Func<Task> originalBody, Func<ValueTask> originalClose)
    {
        var failures = new OsOriginalFailures();
        try { await originalBody().ConfigureAwait(false); }
        catch (Exception original) { failures.Retain(original); }
        try { await originalClose().ConfigureAwait(false); }
        catch (Exception original) { failures.Retain(original); }
        failures.ThrowIfAny("Original supervised Home body and async close failures are retained.");
    }
    private static void WriteControlledStartupRefusal(Exception error)
    {
        // Fixed source-reviewed labels only: no path, credential, arbitrary Message or stack output.
        var type = error switch
        {
            UnauthorizedAccessException => "UnauthorizedAccessException",
            IOException => "IOException",
            System.ComponentModel.Win32Exception => "Win32Exception",
            EntryPointNotFoundException => "EntryPointNotFoundException",
            DllNotFoundException => "DllNotFoundException",
            _ => "UnknownException"
        };
        var reason = error is UnauthorizedAccessException ? error.Message switch
        {
            "The administrator Home child requires exact non-root UID and GID arguments." => "ExactNonRootIdsRequired",
            "Protected original supervisor socket required." => "ProtectedOriginalSocketRequired",
            "A genuine administrator child with safe pre-exec inherited controls and non-root target IDs is required." => "OriginalAdministratorControlsRequired",
            "Actual non-dumpable credential-transition policy is required." => "NonDumpableCredentialPolicyRequired",
            "The child privilege transition could not be confirmed." => "ChildCredentialTransitionUnconfirmed",
            "Bounded actual runtime thread observations required." => "BoundedRuntimeThreadsRequired",
            "Actual kernel thread identity required." => "KernelThreadIdentityRequired",
            "Kernel thread status exceeded its bound." => "KernelThreadStatusTooLarge",
            "An actual runtime thread retained privileges or mismatched credentials." => "RuntimeThreadCredentialsMismatch",
            "The runtime thread cohort changed during privilege observation." => "RuntimeThreadCohortChanged",
            "The required early child privilege transition failed." => "EarlyCredentialSyscallRefused",
            _ => "UnknownStartupRefusal"
        } : "UnknownStartupRefusal";
        Console.Error.WriteLine("ControlledHomeStartupRefusal:" + type + ":" + reason);
    }

}

/// <summary>Designated central OS-session host. Child apps must attach through Home's authenticated transport.</summary>
internal sealed class OsSessionHome : ICuiSceneReadiness, IDisposable, IAsyncDisposable
{
    private readonly ShellViewModel model;
    private readonly OsSessionOriginalWork _originalWork;
    private readonly object _originalFailureGate = new();
    private Exception? _firstOriginalFailure;
    private string _firstOriginalStage = "NoOriginalExceptionObserved";
    private string _hostFailureCode = "UnknownHostResult";
    private bool _originalHostFailureRecorded;
    private HomeNativeOriginalFailureStage _hostFailureStage;
    private Task? _originalProviderClose;
    internal OsSessionHome(ShellViewModel model)
    {
        this.model = model;
        _originalWork = new(_lifetime, CloseOwnedResourcesAsync);
    }
    private void RetainOriginalFailure(string fixedStage, Exception original)
    {
        lock (_originalFailureGate)
            if (_firstOriginalFailure is null) { _firstOriginalFailure = original; _firstOriginalStage = fixedStage; }
    }
    private void RetainHostFailure(HomeNativeHostResult result)
    {
        lock (_originalFailureGate)
        {
            if (!_originalHostFailureRecorded)
            {
            _hostFailureCode = result.Code switch
            {
                "HomeBootstrapFailed" => "HomeBootstrapFailed",
                "HomeCompositionInvalid" => "HomeCompositionInvalid",
                "HomeCompositionConflict" => "HomeCompositionConflict",
                "HomeIdentityUnavailable" => "HomeIdentityUnavailable",
                "HomeServiceUnavailable" => "HomeServiceUnavailable",
                "HomeProfileChanged" => "HomeProfileChanged",
                "HomeRecoveryRequired" => "HomeRecoveryRequired",
                _ => "UnknownHostResult"
            };
            _hostFailureStage = result.ObserveOriginalFailureStage();
            _originalHostFailureRecorded = true;
            }
        }
        try { result.RethrowOriginalFailureIfPresent(); }
        catch (Exception original) { RetainOriginalFailure("RetainedHomeHostResult", original); }
    }
    private void RethrowOriginalFailureIfPresent()
    {
        Exception? original;
        lock (_originalFailureGate) original = _firstOriginalFailure;
        if (original is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();
    }
    internal void WriteOriginalSupervisedFailure(Exception original)
    {
        // Fixed labels only. Original objects stay private; no Message/stack/path output is added.
        string stage, code; HomeNativeOriginalFailureStage hostStage; Exception? first;
        lock (_originalFailureGate)
        { stage = _firstOriginalStage; code = _hostFailureCode; hostStage = _hostFailureStage; first = _firstOriginalFailure; }
        var hostLabel = hostStage switch
        {
            HomeNativeOriginalFailureStage.Composition => "Composition",
            HomeNativeOriginalFailureStage.RuntimeStartup => "RuntimeStartup",
            HomeNativeOriginalFailureStage.RequiredServiceStartup => "RequiredServiceStartup",
            _ => "None"
        };
        Console.Error.WriteLine("OriginalHomeServiceRefusal:" + stage + ":" + code + ":" + hostLabel + ":" + FixedOriginalExceptionType(first ?? original));
        IEnumerable<Exception> retained = original is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : new[] { original };
        foreach (var error in retained)
            Console.Error.WriteLine("OriginalHomeServiceRetainedFailure:" + FixedOriginalExceptionType(error));
    }
    internal static string FixedOriginalExceptionType(Exception original) => original switch
    {
        UnauthorizedAccessException => "UnauthorizedAccessException",
        System.Net.Sockets.SocketException => "SocketException",
        IOException => "IOException",
        OperationCanceledException => "OperationCanceledException",
        ObjectDisposedException => "ObjectDisposedException",
        InvalidOperationException => "InvalidOperationException",
        AggregateException => "AggregateException",
        ArgumentException => "ArgumentException",
        System.ComponentModel.Win32Exception => "Win32Exception",
        EntryPointNotFoundException => "EntryPointNotFoundException",
        DllNotFoundException => "DllNotFoundException",
        _ => "UnknownException"
    };
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
    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct) =>
        new(_originalWork.RunAsync(CheckOriginalAsync, ct));
    private async Task<CuiSceneAvailability> CheckOriginalAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) return new(CuiSceneAvailabilityState.Unavailable, "LinuxHostRequired", "This session shell requires its Linux platform host.");
        var result = await HomeNativeServiceHost.Process.EnsureAsync("9to1.os.session.bundled-home", ComposeAsync,
            ["home.core", "home.state", "apps.installed"], ct);
        if (result.State != HomeNativeHostState.Ready || result.Services is null)
        {
            RetainHostFailure(result);
            return new(CuiSceneAvailabilityState.Unavailable, result.Code, result.Message);
        }
        await _endpointGate.WaitAsync(ct);
        var endpointStage = "PublishDiscoveryEndpoint";
        try
        {
            _discoveryServer ??= await Authority.LinuxSessionDiscoveryServer.StartAsync(_lease ?? throw new InvalidOperationException("The central Home lease is unavailable."),
                result.Services.GetRequiredService<IAppPaths>(), result.Services.GetRequiredService<IAuthenticatedResourceActorSource>(),
                result.Services.GetRequiredService<HomeCoreRuntime>(), result.Services.GetRequiredService<HomeNativeDiscoverySession>(), ct);
            endpointStage = "PublishWidgetEndpoint";
            _widgetRegistrationServer ??= await Authority.LinuxNativeWidgetRegistrationServer.StartAsync(
                _lease ?? throw new InvalidOperationException("The central Home lease is unavailable."),
                result.Services.GetRequiredService<IAppPaths>(), result.Services.GetRequiredService<IAuthenticatedResourceActorSource>(),
                result.Services.GetRequiredService<HomeCoreRuntime>(), result.Services.GetRequiredService<HomeNativeWidgetRegistry>(), ct);
            endpointStage = "StartCanonicalChannel";
            if (Authority.LinuxControlledHomeChildStartup.OriginalCanonicalSocket is { } originalCanonicalSocket && _canonicalChannelTask is null)
                _canonicalChannelTask = Authority.LinuxOriginalHomeCanonicalChannel.ServeOriginalTupleWithWidgetRouteAsync(
                    originalCanonicalSocket, _lease ?? throw new InvalidOperationException("Original Home lease unavailable."), result.Services.GetRequiredService<IAuthenticatedResourceActorSource>(),
                    result.Services.GetRequiredService<IInstalledApplicationRegistry>(), result.Services.GetRequiredService<ResourceAuthorizationService>(),
                    _widgetRegistrationServer.Location, _canonicalChannelToken);
            endpointStage = "StartWidgetObservationChannel";
            if (Authority.LinuxControlledHomeChildStartup.OriginalWidgetObservationSocket is { } originalWidgetSocket && _widgetObservationChannelTask is null)
                _widgetObservationChannelTask = Authority.LinuxOriginalHomeCanonicalChannel.ServeOriginalWidgetObservationsAsync(
                    originalWidgetSocket, _lease ?? throw new InvalidOperationException("Original Home lease unavailable."),
                    result.Services.GetRequiredService<IAuthenticatedResourceActorSource>(), result.Services.GetRequiredService<IInstalledApplicationRegistry>(),
                    result.Services.GetRequiredService<ResourceAuthorizationService>(), result.Services.GetRequiredService<HomeNativeWidgetRegistry>(), _canonicalChannelToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            RetainOriginalFailure(endpointStage, ex);
            return new(CuiSceneAvailabilityState.Unavailable, "HomeDiscoveryEndpointUnavailable", "The native Home endpoint could not be published safely. Existing Home data was preserved; repair the installed runtime location.");
        }
        finally { _endpointGate.Release(); }
        if (Authority.LinuxControlledHomeChildStartup.ServiceOnly)
            return new(CuiSceneAvailabilityState.Ready, "HomeServiceEndpointsReady", "The actual original Home graph and native service endpoints are ready.");
        model.OpenModels = token => OsModelPickerWindow.OpenAsync(result.Services, token);
        model.OpenDulche = (token, lifetime) => OsDulcheWindow.OpenAsync(result.Services, model.RefreshAsync, token, lifetime);
        await model.StartAsync(result.Services.GetRequiredService<ShellConfigurationService>(), result.Services.GetRequiredService<GoService>(), result.Services.GetRequiredService<LinuxApplicationLauncher>(), ct);
        return new(CuiSceneAvailabilityState.Ready, "HomeSessionReady", "The canonical OS session Home services are ready.");
    }
    private Task<HomeNativeServiceSession> ComposeAsync(CancellationToken ct) =>
        _originalWork.RunAsync(ComposeOriginalAsync, ct);
    private async Task<HomeNativeServiceSession> ComposeOriginalAsync(CancellationToken ct)
    {
        var compositionStage = "ComposeRegisterServices";
        try
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
        compositionStage = "ComposeBuildProvider";
        _services = services.BuildServiceProvider();
        compositionStage = "ComposeResolveActor";
        var actors = _services.GetRequiredService<IAuthenticatedResourceActorSource>();
        // No runtime or app authority is started until this process owns the actual profile session lease.
        compositionStage = "ComposeAcquireLease";
        _lease = await HomeNativeSessionLease.TryAcquireAsync(actors, _services.GetRequiredService<IAppPaths>(), ct);
        if (_lease is null) throw new InvalidOperationException("Another designated Home session host is active. Attach to it or recover Home; a second authority was not started.");
        compositionStage = "ComposeBindHeldLease";
        _services.GetRequiredService<Authority.LinuxControlledLaunchGate>().BindHeldLease(_lease);
        if (_originalHomeAuthority is not null)
        {
            compositionStage = "ComposeCaptureAdministrator";
            var authority = await Authority.LinuxAdministratorOriginalHomeLaunchClient.CaptureAsync(
                Authority.LinuxControlledHomeChildStartup.OriginalAdministratorSocket ?? throw new UnauthorizedAccessException("Original administrator route required."),
                _lease, actors, ct);
            if (authority is null) throw new UnauthorizedAccessException("Original Home authority could not be captured.");
            compositionStage = "ComposeBindAdministrator";
            if (!_originalHomeAuthority.BindOriginal(authority))
            { await authority.DisposeAsync(); throw new UnauthorizedAccessException("Original Home authority binding refused."); }
        }
        compositionStage = "ComposeResolveRuntime";
        return new(_services, _services.GetRequiredService<HomeCoreRuntime>(), actors);
        }
        catch (Exception original) { RetainOriginalFailure(compositionStage, original); throw; }
    }
    internal Task RunSupervisedServiceAsync(CancellationToken ct) =>
        _originalWork.RunAsync(token => RunSupervisedOriginalAsync(token, ct), ct);
    private async Task<int> RunSupervisedOriginalAsync(CancellationToken ct, CancellationToken originalCaller)
    {
        try
        {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux supervised Home required.");
        if (Authority.LinuxControlledHomeChildStartup.OriginalAdministratorSocket is not { } socket)
            throw new UnauthorizedAccessException("No admitted original administrator startup.");
        if (Authority.LinuxControlledHomeChildStartup.OriginalCanonicalSocket is { } canonicalSocket)
        {
            await RunCanonicalSupervisedServiceAsync(socket, canonicalSocket, ct, originalCaller);
            return 0;
        }
        var ready = await CheckAsync(originalCaller);
        if (ready.State != CuiSceneAvailabilityState.Ready || _services is null || _lease is null || !_lease.IsHeld)
        {
            RethrowOriginalFailureIfPresent();
            throw new InvalidOperationException("The actual maintained Home graph was not ready.");
        }
        await Authority.LinuxHomeChildLeaseChannel.ServeOriginalLeaseAsync(socket, _lease,
            _services.GetRequiredService<IAuthenticatedResourceActorSource>(),
            _services.GetRequiredService<IAppPaths>(), ct);
        return 0;
        }
        catch (Exception original) { RetainOriginalFailure("SupervisedServiceBody", original); throw; }
    }
    private async Task RunCanonicalSupervisedServiceAsync(string leaseSocket, string canonicalSocket, CancellationToken ct, CancellationToken originalCaller)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The original canonical Home route requires its Linux platform host.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        _canonicalChannelToken = lifetime.Token;
        var graph = await HomeNativeServiceHost.Process.EnsureAsync("9to1.os.session.bundled-home", ComposeAsync,
            ["home.core", "home.state", "apps.installed"], lifetime.Token);
        if (graph.State != HomeNativeHostState.Ready || _services is null || _lease is null || !_lease.IsHeld)
        {
            RetainHostFailure(graph); RethrowOriginalFailureIfPresent();
            throw new InvalidOperationException("Actual original Home graph/lease unavailable.");
        }
        var actors = _services.GetRequiredService<IAuthenticatedResourceActorSource>();
        var registry = _services.GetRequiredService<IInstalledApplicationRegistry>();
        // This channel reports the actual held lease, never service/UI readiness.
        var leaseTask = Authority.LinuxHomeChildLeaseChannel.ServeOriginalLeaseAsync(leaseSocket, _lease,
            actors, _services.GetRequiredService<IAppPaths>(), lifetime.Token);
        Task? canonicalTask = null;
        var failures = new OsOriginalFailures();
        try
        {
            if (!await Authority.LinuxOriginalHomeCanonicalInitialization.ReconcileForDesignatedHomeAsync(_lease, actors, registry, lifetime.Token))
                throw new UnauthorizedAccessException("Original canonical inventory initialization refused.");
            var ready = await CheckAsync(originalCaller);
            if (ready.State != CuiSceneAvailabilityState.Ready)
            {
                RethrowOriginalFailureIfPresent();
                throw new InvalidOperationException("Actual maintained Home services unavailable.");
            }
            canonicalTask = _canonicalChannelTask ?? throw new InvalidOperationException("Actual original canonical route channel unavailable.");
            var channels = _widgetObservationChannelTask is { } widgets ? new[] { leaseTask, canonicalTask, widgets } : new[] { leaseTask, canonicalTask };
            await await Task.WhenAny(channels);
        }
        catch (Exception error) { failures.Retain(error); }
        finally
        {
            try { lifetime.Cancel(); } catch (Exception error) { failures.Retain(error); }
            foreach (var task in new[] { leaseTask, canonicalTask ?? _canonicalChannelTask, _widgetObservationChannelTask })
            {
                if (task is null) continue;
                try { await task; }
                catch (OperationCanceledException error) when (error.CancellationToken == lifetime.Token &&
                    lifetime.IsCancellationRequested && !originalCaller.IsCancellationRequested) { }
                catch (Exception error) { failures.Retain(error); }
            }
            if (_originalHomeAuthority is not null)
                try { await _originalHomeAuthority.DisposeAsync(); } catch (Exception error) { failures.Retain(error); }
        }
        failures.ThrowIfAny("Original canonical Home body and channel-close failures are retained.");
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public ValueTask DisposeAsync() => _originalWork.DisposeAsync();
    private async Task CloseOwnedResourcesAsync()
    {
        // SAME coalesced work owner has drained all original tasks/finally before this cleanup.
        var failures = new OsOriginalFailures();
        if (OperatingSystem.IsLinux() && _originalHomeAuthority is not null)
            try { await _originalHomeAuthority.DisposeAsync().ConfigureAwait(false); }
            catch (Exception original) { failures.Retain(original); }
        foreach (IAsyncDisposable? endpoint in new IAsyncDisposable?[] { _widgetRegistrationServer, _discoveryServer })
            try { if (endpoint is not null) await endpoint.DisposeAsync().ConfigureAwait(false); }
            catch (Exception original) { failures.Retain(original); }
        if (_services is not null)
            try
            {
                _originalProviderClose = _services.DisposeAsync().AsTask();
                await _originalProviderClose.ConfigureAwait(false);
            }
            catch (Exception original) { failures.Retain(original); }
        // Retire the real held lease/lifetime only after actual async provider cleanup settles.
        foreach (IDisposable? owned in new IDisposable?[] { _lease, _lifetime, _endpointGate })
            try { owned?.Dispose(); } catch (Exception original) { failures.Retain(original); }
        failures.ThrowIfAny("Original Home provider, endpoint and lease cleanup failures are retained.");
    }
}
