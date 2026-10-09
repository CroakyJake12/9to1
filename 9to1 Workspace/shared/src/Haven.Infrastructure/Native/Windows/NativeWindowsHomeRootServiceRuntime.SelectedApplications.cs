using System.Security.Cryptography;
using Haven.Application;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeRootServiceRuntime
{
    // Exact issuer records are process-owned. A correlation ID, Home listener or
    // signed service declaration cannot create a child or lend its action consent.
    private readonly Dictionary<Guid, SelectedApplication> _selectedApplications = [];
    // One-use operation metadata outlives healthy native custody. This receipt has
    // no Task, handle, resource ledger or authority to resume another child.
    private readonly Dictionary<Guid, RetiredSelectedOperation> _retiredSelectedOperations = [];
    private sealed record RetiredSelectedOperation(NativeWindowsHomeRootWire.SelectedChoice Choice,
        ControlConnection Connection, NativeWindowsHomeRootWire.SelectedWitness? Witness);
    private void RetireIndependentlyHealthySelectedOperations()
    {
        foreach (var pair in _selectedApplications.ToArray())
        {
            var actual = pair.Value; var close = actual.Close; var lifetime = actual.Lifetime;
            if (close?.IsCompletedSuccessfully != true || lifetime is not null && !lifetime.IsCompletedSuccessfully) continue;
            close.GetAwaiter().GetResult(); lifetime?.GetAwaiter().GetResult();
            // The cached encompassing close already independently joined every raw
            // cohort. These checks deny removal if any retained sibling is unknown.
            if (actual.Cleanup.OriginalErrors.Count != 0 || actual.Preparation.OriginalErrors.Count != 0 ||
                actual.Launch.OriginalErrors.Count != 0 || actual.Effect.OriginalErrors.Count != 0 ||
                actual.InitialReadSources.OriginalErrors.Count != 0 || actual.Child.OriginalErrors.Count != 0 ||
                actual.Prepared.IsCompletedSuccessfully != true || actual.Launched is not null && !actual.Launched.IsCompletedSuccessfully ||
                actual.Exit is not null && !actual.Exit.IsCompletedSuccessfully) continue;
            actual.Prepared.GetAwaiter().GetResult(); actual.Launched?.GetAwaiter().GetResult(); actual.Exit?.GetAwaiter().GetResult();
            _retiredSelectedOperations.Add(pair.Key, new(actual.Choice, actual.Connection, actual.Witness));
            _selectedApplications.Remove(pair.Key);
        }
    }
    private sealed class SelectedApplication(NativeWindowsHomeRootWire.SelectedChoice choice, ControlConnection connection,
        object owner)
    {
        internal readonly NativeWindowsHomeRootWire.SelectedChoice Choice = choice;
        internal readonly ControlConnection Connection = connection;
        internal readonly CloudflareOriginalTaskLedger Preparation = OriginalSource(owner);
        internal readonly CloudflareOriginalTaskLedger Launch = OriginalSource(owner);
        internal readonly CloudflareOriginalTaskLedger Effect = OriginalSource(owner);
        internal readonly CloudflareOriginalTaskLedger Child = OriginalSource(owner);
        internal readonly CloudflareOriginalTaskLedger InitialReadSources = OriginalSource(owner);
        internal readonly CloudflareOriginalTaskLedger Cleanup = OriginalSource(owner);
        internal readonly TaskCompletionSource DispatchSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal NativeWindowsHomeInstalledRootAdmission.OriginalSelectedApplicationLaunchCohort? Pins;
        internal NativeWindowsHomeOriginalControlledProcess? Process;
        internal NativeWindowsHomeOriginalProcessRead? InitialReader;
        internal Task? InitialReaderClose, Exit, Lifetime, Close;
        internal Task<NativeWindowsHomeRootWire.SelectedWitness>? Launched;
        internal Task<bool> Prepared = null!;
        internal NativeWindowsHomeRootWire.SelectedWitness? Witness;
        internal bool Aborted, Transferred;
        private static CloudflareOriginalTaskLedger OriginalSource(object owner)
        { var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(owner); return source; }
    }
    private async Task<NativeWindowsHomeRootWire.Response> DispatchOriginalSelectedApplication(ControlConnection connection,
        NativeWindowsHomeRootWire.Request request, CloudflareOriginalTaskLedger caller)
    {
        var response = new NativeWindowsHomeRootWire.Response(1, request.CorrelationId, false, null);
        if (request.Command == "observe-selected-application")
        {
            SelectedApplication? selected;
            lock (_gate) selected = _selectedApplications.Values.SingleOrDefault(value => value.Witness?.ProcessId == request.ProcessId &&
                ReferenceEquals(value.Connection, connection));
            if (selected is null || selected.Launched?.IsCompletedSuccessfully != true || selected.Exit?.IsCompleted != false ||
                selected.Close is not null || selected.Witness is null) return response with { Reason = "ActualLiveControlledSelectedApplicationRequired" };
            await DemandOriginalSelectedApplicationCurrent(selected, caller).ConfigureAwait(false);
            return response with { Accepted = true, SelectedWitness = selected.Witness };
        }
        var choice = request.SelectedChoice;
        caller.Invoke(() =>
        {
            if (choice is null || choice.OperationId == Guid.Empty || choice.InstalledApplicationId == Guid.Empty ||
                choice.InstalledApplicationRevision < 1 || choice.OriginalListening != connection.Listening ||
                choice.OriginalListening.Actor.OrganisationId is not null || choice.AppId is "root" or "home" ||
                string.IsNullOrWhiteSpace(choice.AppId) || choice.AppId.Length > 256 ||
                string.IsNullOrWhiteSpace(choice.PackageId) || choice.PackageId.Length > 256 ||
                choice.ActivationSha256.Length != 64 || choice.DescriptorSha256.Length != 64 || _installed is null || _user is null ||
                !_installed.Enrollment.SelectedPackageIds.Contains(choice.PackageId, StringComparer.Ordinal))
                throw new UnauthorizedAccessException("The SAME controlled Home's exact current installed application choice is required.");
            return true;
        });
        SelectedApplication actual;
        lock (_gate)
        {
            RetireIndependentlyHealthySelectedOperations();
            if (_retiredSelectedOperations.TryGetValue(choice!.OperationId, out var completed))
            {
                if (!ReferenceEquals(completed.Connection, connection) || completed.Choice != choice)
                    throw new UnauthorizedAccessException("A retired original operation cannot be rebound to another choice or Home channel.");
                return request.Command == "release-selected-application"
                    ? response with { Accepted = true, SelectedChoice = choice, SelectedWitness = completed.Witness }
                    : response with { Reason = "OriginalSelectedApplicationOperationAlreadyClosed" };
            }
        }
        if (request.Command == "prepare-selected-application")
        {
            TaskCompletionSource? begin = null;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_retiring || _controlSealed, this);
                if (!_selectedApplications.TryGetValue(choice!.OperationId, out actual!))
                {
                    if (_selectedApplications.Values.Count(value => value.Close?.IsCompletedSuccessfully != true) >= 128)
                        throw new InvalidOperationException("Unresolved original selected application lifetimes remain retained.");
                    actual = new(choice, connection, this); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    actual.Prepared = PreparePublished(actual, begin.Task); _selectedApplications.Add(choice.OperationId, actual);
                }
                RequireOriginalSelectedChoice(actual, connection, choice!);
            }
            begin?.SetResult(); _ = caller.Track(actual.Prepared); await caller.AwaitAsync(actual.Prepared).ConfigureAwait(false);
            return response with { Accepted = true, SelectedChoice = choice };
        }
        lock (_gate)
        {
            if (!_selectedApplications.TryGetValue(choice!.OperationId, out actual!))
                return response with { Reason = "OriginalSelectedApplicationPreparationRequired" };
            RequireOriginalSelectedChoice(actual, connection, choice);
        }
        if (request.Command == "release-selected-application")
        {
            // Preparation was not productive. Only no-dispatch closes it; accepted
            // child/payload custody transfers to this actual Root lifetime owner.
            await caller.AwaitAsync(actual.Prepared).ConfigureAwait(false);
            Task<NativeWindowsHomeRootWire.SelectedWitness>? launch;
            lock (_gate) launch = actual.Launched;
            if (launch is not null)
            {
                _ = caller.Track(launch); await caller.AwaitAsync(launch).ConfigureAwait(false);
                if (actual.Witness is null || actual.Process is null) throw new InvalidOperationException("The selected child transfer is unconfirmed.");
                lock (_gate) actual.Transferred = true;
                return response with { Accepted = true, SelectedChoice = choice, SelectedWitness = actual.Witness };
            }
            lock (_gate) { actual.Aborted = true; actual.DispatchSettled.TrySetResult(); }
            var close = CloseOriginalSelectedApplication(actual); _ = caller.Track(close); await caller.AwaitAsync(close).ConfigureAwait(false);
            return response with { Accepted = true, SelectedChoice = choice };
        }
        if (request.Command != "launch-selected-application") return response with { Reason = "OriginalSelectedApplicationCommandUnavailable" };
        TaskCompletionSource? start = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || _controlSealed || actual.Aborted || actual.Close is not null, this);
            if (actual.Launched is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual.Launched = LaunchPublished(actual, start.Task);
                // The lifetime is published before any native effect. It owns late
                // process/exit/pins even if the response cannot be delivered.
                actual.Lifetime = SettleOriginalSelectedLifetime(actual);
            }
        }
        start?.SetResult(); _ = caller.Track(actual.Launched!);
        var witness = await caller.AwaitAsync(actual.Launched!).ConfigureAwait(false);
        return response with { Accepted = true, SelectedChoice = choice, SelectedWitness = witness };
    }
    private static void RequireOriginalSelectedChoice(SelectedApplication actual, ControlConnection connection,
        NativeWindowsHomeRootWire.SelectedChoice same)
    {
        if (!ReferenceEquals(actual.Connection, connection) || actual.Choice != same)
            throw new UnauthorizedAccessException("An original selected operation cannot be rebound to another choice or Home channel.");
    }
    private async Task<bool> PreparePublished(SelectedApplication actual, Task begin)
    {
        await begin.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var source = actual.Preparation;
        try
        {
            await source.CaptureOriginalAcquisitionAsync(() => source.Invoke(() =>
                _admission.AcquireOriginalSelectedApplicationLaunchCohortWithinSourceAsync(_installed!, actual.Choice.PackageId,
                    actual.Choice.ActivationSha256, body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, CancellationToken.None)),
                value => actual.Pins = value).ConfigureAwait(false);
            source.Invoke(() =>
            {
                var package = actual.Pins!.Package;
                if (package.Descriptor.AppId != actual.Choice.AppId || Convert.ToHexString(SHA256.HashData(package.SignedArtifact.SignedDescriptorBytes.Span)) != actual.Choice.DescriptorSha256)
                    throw new UnauthorizedAccessException("The actual selected signed package differs from Home's privately captured choice.");
                actual.Pins.DemandOriginalPreparedCurrent(); return true;
            });
        }
        catch (Exception cause) { source.Retain(cause); }
        await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (source.OriginalErrors.Count != 0)
        {
            if (actual.Pins is not null)
            {
                Task? close = null;
                try { source.Invoke(() => { close = actual.Pins.CloseAndDrainOriginalAsync(); _ = source.Track(close); return true; }); }
                catch (Exception cause) { source.Retain(cause); }
                if (close is not null) try { await source.AwaitAsync(close).ConfigureAwait(false); } catch (Exception cause) { source.Capture(close, cause); }
            }
            throw new AggregateException("Original selected runtime preparation failed; native resources remain source-owned.", source.OriginalErrors);
        }
        return true;
    }
    private async Task<NativeWindowsHomeRootWire.SelectedWitness> LaunchPublished(SelectedApplication actual, Task begin)
    {
        await begin.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var driver = actual.Launch; var readSources = actual.InitialReadSources;
        NativeWindowsHomeRootWire.SelectedWitness? witness = null;
        try
        {
            _ = driver.Track(actual.Prepared); await driver.AwaitAsync(actual.Prepared).ConfigureAwait(false);
            await driver.CaptureOriginalAcquisitionAsync(() => driver.Invoke(() =>
                _admission.RunOriginalSelectedLaunchWithinSourceAsync(actual.Pins!, CreateAndResume,
                    body => driver.Invoke(() => { body(); return true; }), raw => { _ = driver.Track(raw); }, CancellationToken.None)),
                value => witness = value).ConfigureAwait(false);
        }
        catch (Exception cause) { driver.Retain(cause); }
        finally { actual.DispatchSettled.TrySetResult(); }
        await driver.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (driver.OriginalErrors.Count != 0)
            throw new AggregateException("The actual selected native launch/current package window failed or remains unknown.", driver.OriginalErrors);
        return witness ?? throw new InvalidOperationException("The selected launch produced no original kernel witness.");

        async Task<NativeWindowsHomeRootWire.SelectedWitness> CreateAndResume()
        {
            var source = actual.Effect;
            try
            {
            source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows selected child required.");
                lock (_gate) ObjectDisposedException.ThrowIf(_retiring || _controlSealed || actual.Aborted, this);
                actual.Pins!.DemandOriginalPreparedCurrent();
                actual.Process = new(this);
                actual.Process.CreateSuspendedOriginal(actual.Pins.ProtectedEntrypoint, actual.Pins.ProtectedWorkingDirectory,
                    OriginalHostAttestationPipeName, _user!.OperatingSystemPrincipalId, actual.Child, _user,
                    actualMachineStateFile: _machineFile, actualSelectedApplication: true);
                actual.Exit = actual.Process.AcquireOriginalExit(actual.Child);
                actual.Pins.BindOriginalChildLifetime(actual.Process, actual.Exit);
                actual.InitialReader = new(this);
                if (!actual.InitialReader.OpenOriginal(new(actual.Process.ProcessId, _user.OperatingSystemPrincipalId), readSources))
                    throw new UnauthorizedAccessException("The SAME suspended selected child kernel identity is unavailable.");
                return true;
            });
            Task? image = null;
            source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows selected image required.");
                image = actual.InitialReader!.ReadOriginalImageAsync(readSources, raw => { _ = readSources.Track(raw); }, CancellationToken.None);
                _ = source.Track(image); return true;
            });
            await source.AwaitAsync(image!).ConfigureAwait(false);
            source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows selected image required.");
                var reader = actual.InitialReader!;
                if (reader.ProcessId != actual.Process!.ProcessId || reader.OperatingSystemPrincipalId != _user!.OperatingSystemPrincipalId ||
                    reader.ExecutableIdentity != "sha256:" + actual.Pins!.Package.EntrypointSha256 ||
                    !StringComparer.OrdinalIgnoreCase.Equals(reader.ExecutablePath, actual.Pins.ProtectedEntrypoint))
                    throw new UnauthorizedAccessException("The actual suspended native child differs from its signed immutable runtime.");
                witness = new(actual.Choice, reader.ProcessId, reader.OperatingSystemPrincipalId, reader.ProcessStartIdentity, reader.ExecutableIdentity);
                return true;
            });
        }
        catch (Exception cause) { source.Retain(cause); }
        if (actual.InitialReader is not null)
        {
            try { source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows selected reader close required.");
                actual.InitialReaderClose ??= actual.InitialReader.CloseAndDrainOriginalAsync(readSources); _ = source.Track(actual.InitialReaderClose); return true;
            }); }
            catch (Exception cause) { source.Retain(cause); }
            if (actual.InitialReaderClose is not null) try { await source.AwaitAsync(actual.InitialReaderClose).ConfigureAwait(false); } catch (Exception cause) { source.Capture(actual.InitialReaderClose, cause); }
        }
        await readSources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        foreach (var cause in readSources.OriginalErrors) source.Retain(cause);
        try
        {
            if (source.OriginalErrors.Count == 0) source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows selected resume required.");
                actual.Pins!.ResumeOriginalChild(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows selected resume required.");
                    _ = _kernel.DemandOriginalUserToken(_user!, _user!.OperatingSystemPrincipalId);
                    actual.Process!.ResumeOriginal(actual.Child);
                });
                actual.Witness = witness; return true;
            });
        }
        catch (Exception cause) { source.Retain(cause); }
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0)
                throw new AggregateException("The actual selected native launch is failed or unknown and remains retained.", source.OriginalErrors);
            return witness ?? throw new InvalidOperationException("The selected launch produced no original kernel witness.");
        }
    }
    private async Task DemandOriginalSelectedApplicationCurrent(SelectedApplication actual, CloudflareOriginalTaskLedger source)
    {
        source.Invoke(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows selected application required.");
            if (actual.Close is not null || actual.Exit?.IsCompleted != false || actual.Launched?.IsCompletedSuccessfully != true ||
                actual.Witness is null || actual.Connection.Listening != actual.Choice.OriginalListening ||
                _homeExit?.IsCompleted != false) throw new UnauthorizedAccessException("The actual controlled selected application/Home lifetime retired.");
            actual.Launched.GetAwaiter().GetResult(); actual.Pins!.DemandOriginalPreparedCurrent();
            _ = _kernel.DemandOriginalUserToken(_user!, actual.Witness.OperatingSystemPrincipalId); return true;
        });
        NativeWindowsHomeInstalledRootAdmission.OriginalActivationRead? current = null; Task? close = null;
        try
        {
            await source.CaptureOriginalAcquisitionAsync(() => source.Invoke(() =>
                _admission.AcquireOriginalActivationReadWithinSourceAsync(_installed!, actual.Choice.PackageId,
                    body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, CancellationToken.None)),
                read => current = read).ConfigureAwait(false);
            source.Invoke(() =>
            {
                if (NativeWindowsHomeInstalledRootAdmission.OriginalSelectedPackageFingerprint(current!.Package) != actual.Choice.ActivationSha256)
                    throw new UnauthorizedAccessException("The currently protected selected activation changed.");
                actual.Pins!.DemandOriginalPreparedCurrent(); return true;
            });
        }
        finally
        {
            if (current is not null)
            {
                try { source.Invoke(() => { close = current.CloseAndDrainOriginalAsync(); _ = source.Track(close); return true; }); }
                catch (Exception cause) { source.Retain(cause); }
                if (close is not null) try { await source.AwaitAsync(close).ConfigureAwait(false); } catch (Exception cause) { source.Capture(close, cause); }
            }
        }
        if (source.OriginalErrors.Count != 0) throw new AggregateException("Actual selected currentness/independent activation close failed.", source.OriginalErrors);
    }
    private async Task SettleOriginalSelectedLifetime(SelectedApplication actual)
    {
        await actual.DispatchSettled.Task.ConfigureAwait(false);
        // Only an actual never-resume-attempted failed acquisition may stop its own
        // suspended process. Accepted or uncertain launches are never terminated.
        if (actual.Witness is not null && actual.Exit is not null)
        {
            // A live accepted application remains usable. Publishing its cached
            // close now would seal currentness before the actual exit occurred.
            _ = actual.Cleanup.Track(actual.Exit);
            try { await actual.Cleanup.AwaitAsync(actual.Exit).ConfigureAwait(false); }
            catch (Exception cause) { actual.Cleanup.Capture(actual.Exit, cause); }
        }
        await CloseOriginalSelectedApplication(actual).ConfigureAwait(false);
    }
    private Task CloseOriginalSelectedApplication(SelectedApplication actual)
    {
        TaskCompletionSource? begin = null; Task original;
        lock (_gate)
        {
            if (actual.Close is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); actual.Close = ClosePublished(begin.Task); }
            original = actual.Close;
        }
        begin?.SetResult(); return original;
        async Task ClosePublished(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var source = actual.Cleanup;
            try { _ = source.Track(actual.Prepared); await source.AwaitAsync(actual.Prepared).ConfigureAwait(false); } catch (Exception cause) { source.Retain(cause); }
            if (actual.Launched is not null) try { _ = source.Track(actual.Launched); await source.AwaitAsync(actual.Launched).ConfigureAwait(false); } catch (Exception cause) { source.Retain(cause); }
            if (actual.Process is not null)
            {
                Task? close = null;
                try { source.Invoke(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows selected child close required.");
                    close = actual.Process.CloseAndDrainOriginalAsync(actual.Child); _ = source.Track(close); return true;
                }); }
                catch (Exception cause) { source.Retain(cause); }
                if (close is not null) try { await source.AwaitAsync(close).ConfigureAwait(false); } catch (Exception cause) { source.Capture(close, cause); }
            }
            // An unknown original exit keeps every payload pin and user resource.
            if (actual.Exit is not null && !actual.Exit.IsCompletedSuccessfully)
                throw new AggregateException("The actual selected child exit remains unknown; immutable runtime pins remain retained.", source.OriginalErrors);
            if (actual.Pins is not null)
            {
                Task? close = null;
                try { source.Invoke(() => { close = actual.Pins.CloseAndDrainOriginalAsync(); _ = source.Track(close); return true; }); }
                catch (Exception cause) { source.Retain(cause); }
                if (close is not null) try { await source.AwaitAsync(close).ConfigureAwait(false); } catch (Exception cause) { source.Capture(close, cause); }
            }
            await actual.Effect.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in actual.Effect.OriginalErrors) source.Retain(cause);
            await actual.InitialReadSources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in actual.InitialReadSources.OriginalErrors) source.Retain(cause);
            await actual.Child.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in actual.Child.OriginalErrors) source.Retain(cause);
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0) throw new AggregateException("Original selected application lifetime cleanup failed and remains retained.", source.OriginalErrors);
        }
    }
    private async Task CloseOriginalSelectedApplications()
    {
        SelectedApplication[] all; lock (_gate) all = _selectedApplications.Values.ToArray();
        foreach (var actual in all)
        {
            lock (_gate) if (actual.Launched is null) { actual.Aborted = true; actual.DispatchSettled.TrySetResult(); }
            await JoinClose(() => CloseOriginalSelectedApplication(actual)).ConfigureAwait(false);
            if (actual.Lifetime is not null)
                try { _ = _closing.Track(actual.Lifetime); await _closing.AwaitAsync(actual.Lifetime).ConfigureAwait(false); }
                catch (Exception cause) { _closing.Capture(actual.Lifetime, cause); }
        }
    }
}
