#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Core;
using HavenOS.Apps.Sites.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    // These are launch locators only. The actual native client independently
    // authenticates its SCM Root, signed enrollment and complete activation.
    private sealed record OriginalHomeRootLaunchLocators(string MachineStateFile);
    private static readonly object OriginalHomeRootLaunchGate = new();
    private static OriginalHomeRootLaunchLocators? _originalHomeRootLaunchLocators;
    private readonly object _actualHomeRootGate = new();
    private readonly object _actualHomeRootSourceOwner = new();
    private CloudflareOriginalTaskLedger? _actualHomeRootSources;
    private NativeWindowsHomeRootClient? _actualHomeRootClient;
    private OriginalWindowsHomeRootClientRegistration? _actualHomeRootRegistration;
    private OriginalAssistantSpacesDependencyOwner? _actualHomeRootSpacesDependency;
    private Task? _actualHomeRootStart, _actualHomeRootClose;
    private Task? _actualHomeRootConnect, _actualHomeRootListening;
    private Task? _actualHomeRootNativeHomeClose, _actualHomeRootRegistrationClose;

    internal static string[] CaptureOriginalHomeRootLaunchLocators(string[] actualArguments)
    {
        ArgumentNullException.ThrowIfNull(actualArguments);
        var hasRootLocator = actualArguments.Any(value => value is "--home-root-control" or "--home-machine-state");
        if (!hasRootLocator) return actualArguments;
        if (!OperatingSystem.IsWindows() || actualArguments.Length != 4 ||
            actualArguments[0] != "--home-root-control" ||
            actualArguments[1] != NativeWindowsHomeRootServiceRuntime.OriginalControlPipeName ||
            actualArguments[2] != "--home-machine-state" ||
            string.IsNullOrWhiteSpace(actualArguments[3]) || !Path.IsPathFullyQualified(actualArguments[3]))
            throw new InvalidDataException("Use only the actual Root's fixed Home launch locators.");
        var location = new OriginalHomeRootLaunchLocators(Path.GetFullPath(actualArguments[3]));
        lock (OriginalHomeRootLaunchGate)
        {
            if (_originalHomeRootLaunchLocators is { } previous && previous != location)
                throw new InvalidOperationException("The original Home launch location cannot be replaced.");
            _originalHomeRootLaunchLocators = location;
        }
        return [];
    }

    private CloudflareOriginalTaskLedger OriginalHomeRootSources()
    {
        lock (_actualHomeRootGate)
        {
            if (_actualHomeRootSources is null)
            {
                _actualHomeRootSources = new();
                _actualHomeRootSources.BindOriginalOwner(_actualHomeRootSourceOwner);
            }
            return _actualHomeRootSources;
        }
    }

    private NativeWindowsHomeRootClient? RetainOriginalHomeRootClient(DesktopOriginalWorkLifetime.Original original)
    {
        original.DemandPublication();
        OriginalHomeRootLaunchLocators? location;
        lock (OriginalHomeRootLaunchGate) location = _originalHomeRootLaunchLocators;
        if (location is null) return null;
        if (_actualHomeRootClient is not null || _actualHomeRootRegistration is not null)
            throw new InvalidOperationException("Compose the actual Home Root client once before Home publication.");
        _ = OriginalHomeRootSources();
        // Capture before any Home callback can fail. This client supplies only
        // this real Root-controlled Home's host identity, never another app's.
        _actualHomeRootClient = new(location.MachineStateFile, original.Token);
        original.DemandPublication();
        return _actualHomeRootClient;
    }

    private IReadOnlyDictionary<Type, object> CaptureOriginalHomeRootOwnerComponents(
        DesktopOriginalWorkLifetime.Original original, HomeNativeWindowsOwnerComponents owners,
        NativeWindowsHomeRootClient? sameRootClient, SiteNativeAuthoringSession sameSites)
    {
        original.DemandPublication();
        var result = new Dictionary<Type, object> { [typeof(SiteNativeAuthoringSession)] = sameSites };
        if (sameRootClient is null) return result;
        if (!ReferenceEquals(sameRootClient, _actualHomeRootClient) || _actualHomeRootRegistration is not null)
            throw new UnauthorizedAccessException("Retain the SAME actual cold Home Root client before owner publication.");
        _actualHomeRootRegistration = new(owners, sameRootClient);
        RetainOriginalInstalledHomeApps(owners, sameRootClient);
        foreach (var pair in _actualHomeRootRegistration.OriginalServices) result.Add(pair.Key, pair.Value);
        original.DemandPublication();
        return result;
    }

    private void RegisterOriginalHomeRootOwners(IServiceCollection collection,
        HomeNativeWindowsComposition sameHome, NativeWindowsHomeRootClient? sameRootClient)
    {
        if (sameRootClient is null) return;
        var registration = _actualHomeRootRegistration
            ?? throw new InvalidOperationException("The actual Home Root registry owner was not captured.");
        if (!ReferenceEquals(sameRootClient, _actualHomeRootClient) ||
            !ReferenceEquals(registration.OriginalClient, sameRootClient) || !registration.HasOriginalComposition(sameHome))
            throw new UnauthorizedAccessException("Use the SAME original Home/client/index/Spaces composition.");
        _actualHomeRootSpacesDependency = new(sameHome, registration.OriginalRegistry,
            sameRootClient, registration.OriginalSpaces);
        foreach (var pair in registration.OriginalServices)
        {
            if (collection.Any(descriptor => descriptor.ServiceType == pair.Key))
                throw new InvalidOperationException("The actual Root Home service already has another registration: " + pair.Key.Name);
            collection.AddSingleton(pair.Key, pair.Value);
        }
        if (collection.Any(descriptor => descriptor.ServiceType == typeof(OriginalAssistantSpacesDependencyOwner)))
            throw new InvalidOperationException("The actual installed Spaces dependency owner is already configured.");
        collection.AddSingleton(_actualHomeRootSpacesDependency);
    }

    private Task StartOriginalHomeWithRootAsync(HomeNativeWindowsComposition sameHome)
    {
        if (_actualHomeRootClient is null) return sameHome.StartOriginalAsync();
        lock (_actualHomeRootGate)
            return _actualHomeRootStart ??= _originalAppWork.RunAsync(async original =>
            {
                var client = _actualHomeRootClient
                    ?? throw new InvalidOperationException("The SAME original Root client is unavailable.");
                var registration = _actualHomeRootRegistration
                    ?? throw new InvalidOperationException("The original Root Home registration is unavailable.");
                var sources = OriginalHomeRootSources();
                void DemandCurrent()
                {
                    original.DemandPublication();
                    if (!ReferenceEquals(client, _actualHomeRootClient) || !ReferenceEquals(sameHome, _actualWindowsHome) ||
                        !ReferenceEquals(registration, _actualHomeRootRegistration) || !registration.HasOriginalComposition(sameHome))
                        throw new UnauthorizedAccessException("The original App/Home/Root composition changed before startup.");
                }
                void Scope(Action body) => AcquireOriginalAppSynchronous(original, () =>
                    sources.Invoke(() => { DemandCurrent(); body(); DemandCurrent(); return true; }));
                void Retain(Task raw) { ArgumentNullException.ThrowIfNull(raw); _ = sources.Track(raw); }
                async Task Take(Func<Task> acquire, Action<Task> capture)
                {
                    Task? raw = null;
                    try { Scope(() => { raw = acquire(); Retain(raw); capture(raw); }); }
                    catch (Exception cause) { sources.Retain(cause); original.Retain(cause); }
                    if (raw is not null)
                        try { await original.AwaitAsync(raw).ConfigureAwait(false); }
                        catch (Exception cause) { sources.Capture(raw, cause); }
                    await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                    if (sources.OriginalErrors.Count != 0)
                        throw new AggregateException("The actual cold Home Root startup failed and remains retained.", sources.OriginalErrors);
                    if (raw is null) throw new InvalidOperationException("The actual Home Root startup supplied no original Task.");
                }
                DemandCurrent();
                await Take(() => client.ConnectOriginalWithinSourceAsync(Scope, Retain, original.Token),
                    actual => _actualHomeRootConnect = actual).ConfigureAwait(false);
                DemandCurrent();
                await Take(sameHome.StartOriginalAsync, _ => { }).ConfigureAwait(false);
                DemandCurrent();
                await Take(() => client.PublishOriginalHomeListeningWithinSourceAsync(sameHome,
                    registration.OriginalRegistry, Scope, Retain, original.Token),
                    actual => _actualHomeRootListening = actual).ConfigureAwait(false);
                DemandCurrent();
            });
    }

    private void DemandOriginalHomeRootRetirementJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(_actualHomeRootSourceOwner);
        _actualHomeRootRegistration?.DemandExternalOriginalJoin();
        _actualHomeRootClient?.DemandExternalOriginalJoin();
        _actualNativeHomeStartupOwner?.DemandExternalOriginalJoin();
        DemandOriginalInstalledHomeAppsJoin();
    }

    private Task JoinOriginalHomeThenRootAfterBorrowersAsync()
    {
        DemandOriginalHomeRootRetirementJoin();
        var home = _actualWindowsHome;
        var client = _actualHomeRootClient;
        if (client is null) return _actualNativeHomeStartupOwner is null
            ? home?.CloseAndDrainAsync() ?? Task.CompletedTask : JoinOriginalSelectedNativeHomeAfterBorrowersAsync(home);
        TaskCompletionSource? begin = null; Task actual;
        lock (_actualHomeRootGate)
        {
            if (_actualHomeRootClose is null)
            {
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _actualHomeRootClose = Close(begin.Task);
            }
            actual = _actualHomeRootClose;
        }
        begin?.SetResult(); return actual;

        async Task Close(Task start)
        {
            await start.ConfigureAwait(false);
            var sources = OriginalHomeRootSources();
            using var own = CloudflareOriginalExecutionGuard.EnterOriginal(_actualHomeRootSourceOwner);
            var launchHealthy = await JoinOriginalInstalledHomeLaunchesBeforeHomeCloseAsync().ConfigureAwait(false);
            var homeHealthy = launchHealthy && await Join(() => home?.CloseAndDrainAsync() ?? Task.CompletedTask,
                raw => _actualHomeRootNativeHomeClose = raw).ConfigureAwait(false);
            // The Root client remains live for all native Home server/session
            // borrowers. An unknown Home close must retain that dependency.
            if (homeHealthy)
                await Join(() => _actualHomeRootRegistration is { } registration
                        ? registration.CloseAndDrainOriginalAsync() : client.CloseAndDrainOriginalAsync(),
                    raw => _actualHomeRootRegistrationClose = raw).ConfigureAwait(false);
            await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (sources.OriginalErrors.Count != 0)
                throw new AggregateException("Actual Home then Root retirement failed and remains retained.", sources.OriginalErrors);

            async Task<bool> Join(Func<Task> acquire, Action<Task> capture)
            {
                Task? raw = null; var healthy = true;
                try
                {
                    _originalAppWork.RunCloseCallback(() => sources.Invoke(() =>
                    { raw = acquire(); _ = sources.Track(raw); capture(raw); return true; }));
                }
                catch (Exception cause) { healthy = false; sources.Retain(cause); }
                if (raw is not null)
                    try { await sources.AwaitAsync(raw).ConfigureAwait(false); }
                    catch (Exception cause) { healthy = false; sources.Capture(raw, cause); }
                return raw is not null && healthy;
            }
        }
    }
}
#endif
