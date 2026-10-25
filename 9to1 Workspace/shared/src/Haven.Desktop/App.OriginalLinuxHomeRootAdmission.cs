#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private static readonly object OriginalLinuxRootLocatorGate = new();
    private static string? _originalLinuxRootEndpoint;
    private LinuxHomeRootClient? _actualLinuxRootClient;
    private DesktopOriginalLinuxHomeRootClientRegistration? _actualLinuxRootRegistration;
    private Task? _actualLinuxRootStart, _actualLinuxRootClose;
    internal static string[] CaptureOriginalLinuxRootLaunchLocators(string[] arguments)
    {
        if (!arguments.Any(value => value == "--home-linux-root-control")) return arguments;
        if (!OperatingSystem.IsLinux() || arguments.Length != 2 || arguments[0] != "--home-linux-root-control" ||
            !Path.IsPathFullyQualified(arguments[1]) || System.Text.Encoding.UTF8.GetByteCount(arguments[1]) > 100)
            throw new InvalidDataException("Use only the actual Linux Root's bounded controlled Home endpoint locator.");
        lock (OriginalLinuxRootLocatorGate)
        {
            if (_originalLinuxRootEndpoint is { } old && old != arguments[1]) throw new InvalidOperationException("The original Linux Root location cannot be replaced.");
            _originalLinuxRootEndpoint = arguments[1];
        }
        return [];
    }
    private LinuxHomeRootClient? RetainOriginalLinuxRootClient(DesktopOriginalWorkLifetime.Original original)
    {
        string? location; lock (OriginalLinuxRootLocatorGate) location = _originalLinuxRootEndpoint;
        if (location is null) return null;
        original.DemandPublication();
        if (_actualLinuxRootClient is not null || _actualHomeRootClient is not null)
            throw new InvalidOperationException("Compose this actual Linux-controlled Home once, with one Root authority.");
        return _actualLinuxRootClient = new(location, original.Token);
    }
    private void CaptureOriginalLinuxRootOwners(HomeNativeWindowsOwnerComponents owners, LinuxHomeRootClient? client, Dictionary<Type, object> result)
    {
        if (client is null) return;
        if (!ReferenceEquals(client, _actualLinuxRootClient) || _actualLinuxRootRegistration is not null)
            throw new UnauthorizedAccessException("The SAME actual Linux Root client must own this Home registration.");
        _actualLinuxRootRegistration = new(owners, client);
        RetainOriginalInstalledHomeApps(owners, client);
        foreach (var pair in _actualLinuxRootRegistration.OriginalServices) result.Add(pair.Key, pair.Value);
    }
    private void RegisterOriginalLinuxRootOwners(IServiceCollection collection, HomeNativeWindowsComposition home, LinuxHomeRootClient? client)
    {
        if (client is null) return;
        var registration = _actualLinuxRootRegistration ?? throw new InvalidOperationException("The original Linux Root registration is unavailable.");
        if (!ReferenceEquals(client, _actualLinuxRootClient) || !registration.HasOriginalComposition(home))
            throw new UnauthorizedAccessException("The SAME actual Linux Home/Root/index tuple is required.");
        foreach (var pair in registration.OriginalServices)
        {
            if (collection.Any(value => value.ServiceType == pair.Key)) throw new InvalidOperationException("The actual Linux Root service was already registered: " + pair.Key.Name);
            collection.AddSingleton(pair.Key, pair.Value);
        }
    }
    private Task StartOriginalLinuxHomeWithRootAsync(HomeNativeWindowsComposition home)
    {
        lock (_actualHomeRootGate) return _actualLinuxRootStart ??= _originalAppWork.RunAsync(async original => {
            var client = _actualLinuxRootClient ?? throw new InvalidOperationException("The SAME original Linux Root client is absent.");
            var registration = _actualLinuxRootRegistration ?? throw new InvalidOperationException("The SAME original Linux Root registration is absent.");
            void Scope(Action body) => AcquireOriginalAppSynchronous(original, () => {
                original.DemandPublication();
                if (!ReferenceEquals(home, _actualWindowsHome) || !registration.HasOriginalComposition(home))
                    throw new UnauthorizedAccessException("The actual Linux Home/Root composition changed during startup.");
                body(); original.DemandPublication(); return true;
            });
            void Retain(Task raw) { /* each client/source privately retains its exact raw child first */ }
            await original.AwaitAsync(client.ConnectOriginalWithinSourceAsync(Scope, Retain, original.Token));
            await original.AwaitAsync(home.StartOriginalAsync());
            await original.AwaitAsync(client.PublishOriginalHomeListeningWithinSourceAsync(home, registration.OriginalRegistry, Scope, Retain, original.Token));
        });
    }
    private Task JoinOriginalLinuxHomeThenRootAsync(HomeNativeWindowsComposition? home)
    {
        lock (_actualHomeRootGate) return _actualLinuxRootClose ??= Close();
        async Task Close()
        {
            var failures = new List<Exception>();
            if (_actualLinuxRootStart is { } start) try { await start.ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            var launchesDrained = await JoinOriginalInstalledHomeLaunchesBeforeHomeCloseAsync().ConfigureAwait(false);
            var homeDrained = home is null && launchesDrained;
            if (home is not null && launchesDrained)
                try { await home.CloseAndDrainAsync().ConfigureAwait(false); homeDrained = true; } catch (Exception cause) { failures.Add(cause); }
            if (homeDrained)
                try
                {
                    if (_actualLinuxRootRegistration is { } registration) await registration.CloseAndDrainAsync().ConfigureAwait(false);
                    else if (_actualLinuxRootClient is { } client) await client.CloseAndDrainAsync().ConfigureAwait(false);
                }
                catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0) throw new AggregateException("The original Linux Home/Root lifetime did not drain; dependencies remain retained.", failures);
        }
    }
}
#endif
