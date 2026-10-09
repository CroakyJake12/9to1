using System.Runtime.CompilerServices;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>The actual registered Root process owns cold publisher/activation
/// admission and the original limited Home child. It creates no publisher key,
/// trust enrollment, canonical profile, package record or installation.</summary>
public sealed partial class NativeWindowsHomeRootServiceRuntime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly string _machineFile;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomePackageDatabase _packages;
    private readonly NativeWindowsHomeRegisteredRootServiceContext _kernel;
    private readonly NativeWindowsHomeInstallerBootstrapAdmission _publisher;
    private readonly NativeWindowsHomeInstalledRootAdmission _admission;
    private readonly CloudflareOriginalTaskLedger _startup = new();
    private readonly CloudflareOriginalTaskLedger _childSources = new();
    private readonly CloudflareOriginalTaskLedger _closing = new();
    private readonly ConditionalWeakTable<StartupObservation, object> _issued = new();
    private Task<StartupObservation>? _start;
    private NativeWindowsHomeRegisteredRootServiceContext.InteractiveUser? _user;
    private NativeWindowsHomeInstalledRootAdmission.Observation? _installed;
    private NativeWindowsHomeInstalledRootAdmission.OriginalHomeLaunchCohort? _homePins;
    private NativeWindowsHomeOriginalControlledProcess? _home;
    private Task? _homeExit, _close;
    private bool _retiring;

    public sealed class StartupObservation
    {
        internal readonly NativeWindowsHomeRootServiceRuntime Owner;
        internal StartupObservation(NativeWindowsHomeRootServiceRuntime owner, string? missing)
        { Owner = owner; MissingPrerequisite = missing; }
        public string? MissingPrerequisite { get; }
        // Accepted child launch is an observation only. Home is usable only after
        // its source-owned live listening/session observation authenticates below.
        public bool HasAcceptedOriginalHomeLaunch => MissingPrerequisite is null;
    }
    public NativeWindowsHomeRootServiceRuntime(string actualConfiguredMachineStateFile)
    {
        _machineFile = Path.GetFullPath(actualConfiguredMachineStateFile);
        _store = new(_machineFile);
        _kernel = new(_machineFile);
        _profiles = _kernel.CreateOriginalMachineProfiles(_store);
        _packages = new(_store);
        _publisher = new(_profiles);
        _admission = new(_publisher, _packages, _store, _profiles, _machineFile, actualRootServiceContext: _kernel);
        _startup.BindOriginalOwner(this); _childSources.BindOriginalOwner(this); _closing.BindOriginalOwner(this);
        _controlCleanup.BindOriginalOwner(this); _hostAttestationCleanup.BindOriginalOwner(this);
    }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public bool IsIssuedOriginalStartup(StartupObservation same)
    {
        lock (_gate) return same is not null && ReferenceEquals(same.Owner, this) && _issued.TryGetValue(same, out _) &&
            _start?.IsCompletedSuccessfully == true && ReferenceEquals(_start.Result, same) &&
            _startup.OriginalErrors.Count == 0 && _startup.OriginalTasks.All(raw => raw.IsCompletedSuccessfully);
    }
    public Task<StartupObservation> StartOriginalWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        DemandExternalOriginalJoin(); TaskCompletionSource? begin = null; Task<StartupObservation> actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_start is not null) return _start;
            // Startup arguments locate the original object; native admission below
            // establishes its authority. No missing file/profile is initialized.
            _startup.BindOriginalCallerCallback(body => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                () => { RunControlCallback(_startup, scope, body); return true; }));
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _start = StartPublished(begin.Task, token);
        }
        try { _startup.Invoke(() => { retain(actual); return true; }); }
        catch (Exception cause) { _startup.Retain(cause); }
        finally { begin.SetResult(); }
        return actual;
    }
    private async Task<StartupObservation> StartPublished(Task begin, CancellationToken token)
    {
        await begin.ConfigureAwait(false); using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        StartupObservation? result = null;
        try
        {
            if (!OperatingSystem.IsWindows()) result = new(this, "InstalledWindowsRootRequired");
            else
            {
                await _startup.CaptureOriginalAcquisitionAsync(() => _startup.Invoke(() =>
                    _kernel.AcquireOriginalInteractiveUserWithinSourceAsync(RunStartup, KeepStartup, token)), actual => _user = actual).ConfigureAwait(false);
                if (_user is null) result = new(this, "OriginalInteractiveWindowsUserRequired");
                else
                {
                    var read = _startup.Invoke(() => _admission.InspectOriginalRuntimeWithinSourceAsync(RunStartup, KeepStartup, token));
                    _ = _startup.Track(read);
                    _installed = await _startup.AwaitAsync(read).ConfigureAwait(false);
                    if (_installed is null)
                    {
                        if (!_admission.TryObserveOriginalUnavailable(read, out var reason))
                            throw new UnauthorizedAccessException("The actual Root admission did not issue a healthy missing-prerequisite observation.");
                        result = new(this, reason);
                    }
                    else
                    {
                        await _startup.CaptureOriginalAcquisitionAsync(() => _startup.Invoke(() =>
                            _admission.AcquireOriginalHomeLaunchCohortWithinSourceAsync(_installed, RunStartup, KeepStartup, token)), actual => _homePins = actual).ConfigureAwait(false);
                        _startup.Invoke(() =>
                        {
                            token.ThrowIfCancellationRequested();
                            lock (_gate) ObjectDisposedException.ThrowIf(_retiring, this);
                            if (_user.OperatingSystemPrincipalId != "windows-sid:" + _installed.Enrollment.OriginalOsPrincipal)
                                throw new UnauthorizedAccessException("The actual interactive user changed before Home launch.");
                            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                            _home = new(this); // Capture native process owner before any creation callback.
                            _home.CreateSuspendedOriginal(_homePins!.ProtectedEntrypoint, _homePins.ProtectedWorkingDirectory,
                                OriginalControlPipeName, _user.OperatingSystemPrincipalId, _childSources, _user,
                                actualMachineStateFile: _machineFile);
                            _homeExit = _home.AcquireOriginalExit(_childSources);
                            _homePins.BindOriginalChildLifetime(_home, _homeExit); // SAME exit/native cohort before resume.
                            return true;
                        });
                        var control = _startup.Invoke(PrepareOriginalControlForStartup); _ = _startup.Track(control);
                        await _startup.AwaitAsync(control).ConfigureAwait(false);
                        var attestation = _startup.Invoke(PrepareOriginalHostAttestationForStartup); _ = _startup.Track(attestation);
                        await _startup.AwaitAsync(attestation).ConfigureAwait(false);
                        _startup.Invoke(() =>
                        {
                            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                            _homePins!.ResumeOriginalChild(() =>
                            {
                                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                                _home!.ResumeOriginal(_childSources);
                            }); return true;
                        });
                        result = new(this, null);
                    }
                }
            }
        }
        catch (Exception cause) { _startup.Retain(cause); }
        await _startup.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (_startup.OriginalErrors.Count != 0)
            throw new AggregateException("Actual Root cold admission/Home launch remains unconfirmed; all originals are retained.", _startup.OriginalErrors);
        _issued.Add(result!, this); return result!;
    }
    private void RunStartup(Action body) => _startup.Invoke(() => { body(); return true; });
    private void KeepStartup(Task raw) { _ = _startup.Track(raw); }
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public void RequestOriginalRetirement() { lock (_gate) _retiring = true; }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? begin = null; Task actual;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = ClosePublished(begin.Task); }
            actual = _close;
        }
        begin?.SetResult(); return actual;
    }
    private async Task ClosePublished(Task begin)
    {
        await begin.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        if (_start is not null)
            try { await _closing.AwaitAsync(_start).ConfigureAwait(false); } catch (Exception cause) { _closing.Capture(_start, cause); }
        // Control cleanup and the actual accepted child's exit are independent
        // originals. A failed control close cannot skip the child's owning join.
        await JoinClose(CloseOriginalHostAttestationAsync).ConfigureAwait(false);
        await JoinClose(CloseOriginalControlAsync).ConfigureAwait(false);
        // Accepted Home is never terminated. This joins the SAME actual native exit;
        // unstarted acquisition failure alone may stop its own suspended child.
        if (_home is not null)
        {
            Task? close = null;
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                close = _home.CloseAndDrainOriginalAsync(_childSources); _ = _closing.Track(close);
            }
            catch (Exception cause) { _closing.Retain(cause); }
            if (close is not null)
                try { await _closing.AwaitAsync(close).ConfigureAwait(false); } catch (Exception cause) { _closing.Capture(close, cause); }
        }
        if (_homeExit is not null && !_homeExit.IsCompletedSuccessfully)
            throw new AggregateException("The original Home exit remains unknown; retain payload/user/publisher/Root pins.", _closing.OriginalErrors);
        if (_homePins is not null) await JoinClose(_homePins.CloseAndDrainOriginalAsync);
        await _childSources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        foreach (var cause in _childSources.OriginalErrors) _closing.Retain(cause);
        // Every accepted selected child independently joins its own actual exit
        // before any global Root/user/publisher source or immutable pins retire.
        await JoinClose(CloseOriginalSelectedApplications);
        await JoinClose(_admission.CloseAndDrainAsync);
        await JoinClose(_publisher.CloseAndDrainAsync);
        await JoinClose(_kernel.CloseAndDrainAsync);
        foreach (var cause in _startup.OriginalErrors) _closing.Retain(cause);
        await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (_closing.OriginalErrors.Count != 0)
            throw new AggregateException("Original Root/Home/channel/native cleanup failed and remains retained.", _closing.OriginalErrors);
    }
    private async Task JoinClose(Func<Task> close)
    {
        Task? actual = null;
        try { _closing.Invoke(() => { actual = close(); _ = _closing.Track(actual); return true; }); }
        catch (Exception cause) { _closing.Retain(cause); }
        if (actual is not null)
            try { await _closing.AwaitAsync(actual).ConfigureAwait(false); } catch (Exception cause) { _closing.Capture(actual, cause); }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}
