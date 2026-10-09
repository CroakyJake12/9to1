#if !ANDROID
using System.Reflection;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Core;

namespace Haven.Desktop;

public sealed partial class App
{
    private sealed record OriginalNativeHomeLaunchLocators(string MachineStateFile);
    private static readonly object OriginalNativeHomeLaunchGate = new();
    private static OriginalNativeHomeLaunchLocators? _originalNativeHomeLaunchLocators;
    private NativeWindowsHomeApplicationStartupOwner? _actualNativeHomeStartupOwner;
    private HomeNativeStartupObservation? _actualNativeHomeStartupObservation;
    private Task? _actualNativeHomeStart, _actualNativeHomeConnection, _actualNativeHomeInitialization;
    private Task? _actualNativeHomeDependencyClose, _actualSelectedNativeHomeClose;

    internal static string[] CaptureOriginalNativeHomeLaunchLocators(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!arguments.Any(value => value == "--home-root-host")) return arguments;
        if (!OperatingSystem.IsWindows() || arguments.Length != 4 ||
            arguments[0] != "--home-root-host" ||
            arguments[1] != NativeWindowsHomeRootServiceRuntime.OriginalHostAttestationPipeName ||
            arguments[2] != "--home-machine-state" || string.IsNullOrWhiteSpace(arguments[3]) ||
            !Path.IsPathFullyQualified(arguments[3]))
            throw new InvalidDataException("Use only the actual Root's fixed selected-app launch locators.");
        var same = new OriginalNativeHomeLaunchLocators(Path.GetFullPath(arguments[3]));
        lock (OriginalNativeHomeLaunchGate)
        {
            if (_originalNativeHomeLaunchLocators is { } old && old != same)
                throw new InvalidOperationException("The original native Home location cannot be replaced.");
            _originalNativeHomeLaunchLocators = same;
        }
        return [];
    }
    internal static HomeCompatibilityRequest CaptureOriginalNativeHomeRequirements(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var appId = ValidateOriginalInitialProductDeclaration(assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            ?? throw new InvalidDataException("A selected native executable requires its signed compiled product declaration.");
        var version = assembly.GetName().Version?.ToString()
            ?? throw new InvalidDataException("The selected executable has no compiled version.");
        // This consumer uses the maintained control-plane state contract. The
        // authenticated installed descriptor and Home still restrict this caller's
        // declared service IDs. Compatibility declares no data or action consent.
        var contract = HomeCoreServiceCatalog.CurrentContractVersion;
        return new(appId, version, Array.AsReadOnly(new[] { new HomeServiceRequirement("home.state", contract.Major, contract.Minor) }));
    }
    private void RetainOriginalNativeHomeStartup(DesktopOriginalWorkLifetime.Original original)
    {
        OriginalNativeHomeLaunchLocators? location;
        lock (OriginalNativeHomeLaunchGate) location = _originalNativeHomeLaunchLocators;
        if (location is null) return;
        if (_actualHomeRootClient is not null || _actualNativeHomeStartupOwner is not null)
            throw new InvalidOperationException("A process cannot borrow both controlled Home and selected-app Root identities.");
        _ = OriginalHomeRootSources();
        _actualNativeHomeStartupOwner = new(location.MachineStateFile,
            CaptureOriginalNativeHomeRequirements(typeof(App).Assembly), original.Token);
        original.DemandPublication();
    }
    private Task StartOriginalNativeHomeDependencyAsync(HomeNativeWindowsComposition sameHome)
    {
        if (_actualNativeHomeStartupOwner is null) return StartOriginalHomeWithRootAsync(sameHome);
        lock (_actualHomeRootGate) return _actualNativeHomeStart ??= _originalAppWork.RunAsync(async original =>
        {
            var owner = _actualNativeHomeStartupOwner;
            var sources = OriginalHomeRootSources();
            void DemandCurrent()
            {
                original.DemandPublication();
                if (owner is null || !ReferenceEquals(owner, _actualNativeHomeStartupOwner) ||
                    !ReferenceEquals(sameHome, _actualWindowsHome) || _actualHomeRootClient is not null)
                    throw new UnauthorizedAccessException("The actual selected-app startup composition changed.");
            }
            void Scope(Action body) => AcquireOriginalAppSynchronous(original, () => sources.Invoke(() =>
                { DemandCurrent(); body(); DemandCurrent(); return true; }));
            void Retain(Task raw) { _ = sources.Track(raw); }
            Task<HomeNativeStartupObservation>? connection = null;
            try { Scope(() => { connection = owner!.ConnectWithinOriginalSourceAsync(Scope, Retain, original.Token);
                _actualNativeHomeConnection = connection; Retain(connection); }); }
            catch (Exception cause) { sources.Retain(cause); original.Retain(cause); }
            HomeNativeStartupObservation? observed = null;
            if (connection is not null)
                try { observed = await original.AwaitAsync(connection).ConfigureAwait(false); }
                catch (Exception cause) { sources.Capture(connection, cause); }
            await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (sources.OriginalErrors.Count != 0) throw new AggregateException("Actual native startup source failed and remains retained.", sources.OriginalErrors);
            DemandCurrent();
            _actualNativeHomeStartupObservation = observed
                ?? throw new InvalidOperationException("No actual native Home startup observation was returned.");
            if (!observed.CanStartNormally)
                throw new InvalidOperationException(observed.Message + " Open the installed Home to complete the review or repair, then restart this app. App initialization has not been authorized.");
            Task? initialization = null;
            try { Scope(() => { initialization = owner!.InitializeWithinOriginalSourceAsync(
                _ => StartOriginalHomeWithRootAsync(sameHome), Scope, Retain, original.Token);
                _actualNativeHomeInitialization = initialization; Retain(initialization); }); }
            catch (Exception cause) { sources.Retain(cause); original.Retain(cause); }
            if (initialization is not null)
                try { await original.AwaitAsync(initialization).ConfigureAwait(false); }
                catch (Exception cause) { sources.Capture(initialization, cause); }
            await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (sources.OriginalErrors.Count != 0) throw new AggregateException("Actual native startup initialization failed and remains retained.", sources.OriginalErrors);
            if (initialization is null) throw new InvalidOperationException("No actual authorized initialization Task was retained.");
            DemandCurrent();
        });
    }
    private Task JoinOriginalSelectedNativeHomeAfterBorrowersAsync(HomeNativeWindowsComposition? home)
    {
        // Preflight precedes even cached close access; capture the SAME actual
        // selected dependency before callbacks or any accepted await.
        DemandOriginalHomeRootRetirementJoin();
        var sameStartup = _actualNativeHomeStartupOwner
            ?? throw new InvalidOperationException("The actual selected native Home startup owner is absent.");
        if (!ReferenceEquals(home, _actualWindowsHome) || _actualHomeRootClient is not null)
            throw new UnauthorizedAccessException("The actual selected Home dependency tuple changed before close.");
        Task actual; TaskCompletionSource? begin = null;
        lock (_actualHomeRootGate)
        {
            if (_actualSelectedNativeHomeClose is null)
            {
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _actualSelectedNativeHomeClose = Close(begin.Task);
            }
            actual = _actualSelectedNativeHomeClose;
        }
        begin?.SetResult(); return actual;
        async Task Close(Task start)
        {
        await start.ConfigureAwait(false);
        var sources = OriginalHomeRootSources(); Task? local = null; var healthy = true;
        try { _originalAppWork.RunCloseCallback(() => sources.Invoke(() =>
        { local = home?.CloseAndDrainAsync() ?? Task.CompletedTask; _actualHomeRootNativeHomeClose = local; _ = sources.Track(local); return true; })); }
        catch (Exception cause) { healthy = false; sources.Retain(cause); }
        healthy &= local is not null;
        if (local is not null)
            try { await sources.AwaitAsync(local).ConfigureAwait(false); }
            catch (Exception cause) { healthy = false; sources.Capture(local, cause); }
        if (healthy)
        {
            Task? raw = null;
            try { _originalAppWork.RunCloseCallback(() => sources.Invoke(() =>
            { raw = sameStartup.CloseAndDrainOriginalAsync(); _actualNativeHomeDependencyClose = raw; _ = sources.Track(raw); return true; })); }
            catch (Exception cause) { sources.Retain(cause); }
            if (raw is not null) try { await sources.AwaitAsync(raw).ConfigureAwait(false); }
                catch (Exception cause) { sources.Capture(raw, cause); }
        }
        await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (sources.OriginalErrors.Count != 0)
            throw new AggregateException("Actual local Home/native dependency retirement remains failed or unknown.", sources.OriginalErrors);
        }
    }
}
#endif
