#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HomeAppsSnapshot = HavenOS.Home.Apps.HomeAppsSnapshot;
using HavenOS.Home.Core;

namespace Haven.Desktop;

public sealed partial class App
{
    private HomeInstalledApplicationLaunchResourceResolver? _actualInstalledHomeAppsResolver;
    private HomeInstalledApplicationLaunchActionPolicySource? _actualInstalledHomeAppsPolicy;
    private HomeCanonicalInstalledApplicationLaunchSource? _actualInstalledHomeAppsSource;
    private HomeAppsFeature? _actualInstalledHomeAppsFeature;
    private OriginalInstalledHomeAppsPort? _actualInstalledHomeAppsPort;
    private Task? _actualInstalledHomeAppsDrain, _actualInstalledHomeAppsSourceClose;
    private readonly List<OriginalInstalledHomeLaunch> _actualInstalledHomeLaunches = [];

    private void PrepareOriginalInstalledHomeApps(NativeWindowsHomeRootClient? sameClient)
    {
        if (sameClient is null) return;
        if (!ReferenceEquals(sameClient, _actualHomeRootClient) || _actualInstalledHomeAppsResolver is not null)
            throw new InvalidOperationException("Prepare the SAME installed Home Apps composition once.");
        _actualInstalledHomeAppsResolver = new(() => _actualInstalledHomeAppsSource
            ?? throw new InvalidOperationException("The actual installed Home launch source has not been retained."));
        _actualInstalledHomeAppsPolicy = new();
    }
    private IEnumerable<ICanonicalResourceAccessResolver> OriginalInstalledHomeAppsResolvers() =>
        _actualInstalledHomeAppsResolver is { } same ? [same] : [];
    private IEnumerable<IHomeActionPolicySource> OriginalInstalledHomeAppsPolicies() =>
        _actualInstalledHomeAppsPolicy is { } same ? [same] : [];

    private void RetainOriginalInstalledHomeApps(HomeNativeWindowsOwnerComponents owners,
        NativeWindowsHomeRootClient sameClient)
    {
        if (!ReferenceEquals(sameClient, _actualHomeRootClient) || _actualInstalledHomeAppsSource is not null ||
            _actualHomeRootRegistration is not { } registration || !ReferenceEquals(registration.OriginalClient, sameClient))
            throw new UnauthorizedAccessException("Retain the SAME original Root/Home/index before composing installed Apps.");
        var source = _actualInstalledHomeAppsSource = new(owners.StateStore, owners.Profiles,
            owners.Resources, owners.Broker, owners.Permissions, sameClient);
        sameClient.BindOriginalApplicationLaunchSource(source);
        if (!sameClient.HasOriginalApplicationLaunchSource(source) ||
            _actualInstalledHomeAppsResolver?.IsBoundToOriginalOwner(source) != true)
            throw new UnauthorizedAccessException("The actual installed Home launch source pairing failed.");
        _actualInstalledHomeAppsFeature = new(sameClient);
        _actualInstalledHomeAppsPort = new(this);
    }
    private void DemandOriginalInstalledHomeAppsJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(_actualHomeRootSourceOwner);
        _actualInstalledHomeAppsSource?.DemandExternalOriginalJoin();
    }
    private void RequestOriginalHomeApplicationLaunchRetirement(List<Exception> failures)
    {
        // This is the client-owned launch-only barrier. Its prepared-review child
        // waits accepted late publication before issuer withdrawal; transport and
        // unrelated Home host reads remain live until actual Home closes.
        if (_actualHomeRootClient is not { } same) return;
        try
        {
            _originalAppWork.RunCloseCallback(() => OriginalHomeRootSources().Invoke(() =>
            {
                var raw = same.DrainOriginalApplicationLaunchesBeforeHomeCloseAsync();
                _actualInstalledHomeAppsDrain = raw; _ = OriginalHomeRootSources().Track(raw);
                return true;
            }));
        }
        catch (Exception cause) { OriginalHomeRootSources().Retain(cause); AddAppCause(failures, cause); }
    }
    private async Task<bool> JoinOriginalInstalledHomeLaunchesBeforeHomeCloseAsync()
    {
        var sources = OriginalHomeRootSources(); var failures = new List<Exception>();
        RequestOriginalHomeApplicationLaunchRetirement(failures);
        if (_actualInstalledHomeAppsDrain is { } drain) await Join(drain);
        OriginalInstalledHomeLaunch[] all; lock (_actualHomeRootGate) all = _actualInstalledHomeLaunches.ToArray();
        foreach (var actual in all) await Join(actual.OriginalTask);
        if (failures.Count == 0 && _actualInstalledHomeAppsSource is { } source)
        {
            Task? raw = null;
            try { _originalAppWork.RunCloseCallback(() => sources.Invoke(() =>
            { raw = source.CloseAndDrainOriginalAsync(); _actualInstalledHomeAppsSourceClose = raw; _ = sources.Track(raw); return true; })); }
            catch (Exception cause) { Remember(cause); }
            if (raw is not null) await Join(raw);
        }
        foreach (var cause in failures) sources.Retain(cause);
        return failures.Count == 0;
        void Remember(Exception cause) { AddAppCause(failures, cause); }
        async Task Join(Task raw)
        {
            try { await sources.AwaitAsync(raw).ConfigureAwait(false); }
            catch (Exception cause)
            { sources.Capture(raw, cause); if (raw.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var direct in group.InnerExceptions) Remember(direct); else Remember(cause); }
        }
    }

    internal sealed class OriginalInstalledHomeLaunch
    {
        internal readonly App Issuer;
        internal readonly HomePackageActionRequest OriginalRequest;
        internal readonly Task<HomePackageActionResult> OriginalTask;
        internal OriginalInstalledHomeLaunch(App issuer, HomePackageActionRequest request, Task<HomePackageActionResult> task)
        { Issuer = issuer; OriginalRequest = request; OriginalTask = task; }
    }
    internal sealed class OriginalInstalledHomeAppsPort(App owner)
    {
        private readonly App _owner = owner;
        private void DemandSource()
        {
            if (_owner._actualHomeRootClient is not { } client || _owner._actualWindowsHome is not { } home ||
                _owner._actualInstalledHomeAppsSource is not { } source || _owner._actualInstalledHomeAppsFeature is null ||
                !ReferenceEquals(_owner._actualInstalledHomeAppsPort, this) ||
                !client.HasOriginalApplicationLaunchSource(source) ||
                !source.HasOriginalComposition(home.StateStore, home.Profiles, client) ||
                _owner._actualHomeRootRegistration?.HasOriginalComposition(home) != true)
                throw new UnauthorizedAccessException("Use the SAME actual installed Root/Home Apps source.");
        }
        internal Task<HomeAppsSnapshot> ReadAsync(HomeAppsQuery query, CancellationToken token) =>
            _owner._originalAppWork.RunAsync(async original =>
            {
                var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(_owner._actualHomeRootSourceOwner);
                Task<HomeAppsSnapshot>? raw = null;
                try
                {
                    _owner.AcquireOriginalAppSynchronous(original, () => sources.Invoke(() =>
                    {
                        DemandSource();
                        if (HasUnsettledLaunch()) return true;
                        raw = _owner._actualInstalledHomeAppsFeature!.RefreshAsync(query, token);
                        _ = sources.Track(raw); _ = _owner.OriginalHomeRootSources().Track(raw); return true;
                    }));
                }
                catch (Exception cause) { sources.Retain(cause); _owner.OriginalHomeRootSources().Retain(cause); original.Retain(cause); }
                if (raw is null)
                {
                    if (sources.OriginalErrors.Count != 0) throw new AggregateException("Installed Apps source acquisition failed.", sources.OriginalErrors);
                    // A feature refresh waits its business gate. Do not lend that
                    // long approval wait to a view: retain current metadata only.
                    return sources.Invoke(() => _owner._actualInstalledHomeAppsFeature!.Current with
                        { State = HomeAppsDataState.Partial, IsStale = true });
                }
                var result = await original.AwaitAsync(raw).ConfigureAwait(false);
                await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                if (sources.OriginalErrors.Count != 0)
                { foreach (var cause in sources.OriginalErrors) _owner.OriginalHomeRootSources().Retain(cause);
                    throw new AggregateException("Installed Apps originals remain unconfirmed.", sources.OriginalErrors); }
                return result;
            });
        internal OriginalInstalledHomeLaunch StartLaunch(HomeAppsSnapshot snapshot, HomePackageEntry row)
        {
            OriginalInstalledHomeLaunch? retained = null;
            _owner._originalAppWork.RunSynchronous(original => _owner.AcquireOriginalAppSynchronous(original, () =>
                _owner.OriginalHomeRootSources().Invoke(() =>
                {
                    DemandSource();
                    var feature = _owner._actualInstalledHomeAppsFeature!;
                    if (!ReferenceEquals(snapshot, feature.Current) || snapshot.IsStale ||
                        snapshot.State is not (HomeAppsDataState.Available or HomeAppsDataState.Partial) ||
                        !snapshot.Packages.Any(actual => ReferenceEquals(actual, row)) ||
                        !row.SupportedActions.Contains(HomePackageAction.Launch) || HasUnsettledLaunch())
                        throw new UnauthorizedAccessException("Refresh and choose the SAME current installed row before Launch.");
                    lock (_owner._actualHomeRootGate)
                    {
                        if (_owner._actualInstalledHomeLaunches.Count >= 128)
                            throw new InvalidOperationException("Retained installed launch originals require process retirement.");
                        var request = new HomePackageActionRequest(row.PackageId, HomePackageAction.Launch,
                            Guid.NewGuid().ToString("N"), ExpectedRevision: snapshot.Revision);
                        var raw = feature.ExecuteAsync(request, CancellationToken.None);
                        _ = _owner.OriginalHomeRootSources().Track(raw); // Before later callbacks or publication.
                        retained = new(_owner, request, raw); _owner._actualInstalledHomeLaunches.Add(retained);
                    }
                    original.DemandPublication(); return true;
                })));
            return retained ?? throw new InvalidOperationException("No actual installed launch original was retained.");
        }
        internal HomePackageActionResult? ObserveLaunch(OriginalInstalledHomeLaunch same) =>
            _owner.OriginalHomeRootSources().Invoke(() =>
            {
                DemandSource();
                lock (_owner._actualHomeRootGate)
                    if (!ReferenceEquals(same.Issuer, _owner) || !_owner._actualInstalledHomeLaunches.Any(actual => ReferenceEquals(actual, same)))
                        throw new UnauthorizedAccessException("The SAME process-owned installed launch is required.");
                if (!same.OriginalTask.IsCompleted) return null;
                try { return same.OriginalTask.GetAwaiter().GetResult(); }
                catch (Exception cause) { _owner.OriginalHomeRootSources().Capture(same.OriginalTask, cause); throw; }
            });
        private bool HasUnsettledLaunch()
        {
            OriginalInstalledHomeLaunch[] all; lock (_owner._actualHomeRootGate) all = _owner._actualInstalledHomeLaunches.ToArray();
            foreach (var actual in all)
            {
                if (!actual.OriginalTask.IsCompleted) return true;
                var result = ObserveLaunch(actual);
                if (result?.State is HomePackageOperationState.Unknown or HomePackageOperationState.Pending or HomePackageOperationState.Failed)
                    return true;
            }
            return false;
        }
    }
}
#endif
