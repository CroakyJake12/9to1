using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Sockets;
using HavenOS.Home.Core;
using HavenOS.Home.Apps;
using Haven.Infrastructure.Native.Windows;

namespace Haven.Infrastructure.Native.Linux;

public sealed partial class LinuxHomeRootServiceRuntime
{
    // Owned by the one authenticated Home control loop. Choices contain no UID,
    // path or argv: the current logind user and retained signed activation own all three.
    private sealed class OriginalSelectedApplications(
        LinuxHomeRootServiceRuntime owner, LinuxHomeBootstrapActivationOwner.PublishedCode code,
        LinuxHomeRegisteredRootServiceContext kernel, LinuxHomeRegisteredRootServiceContext.InteractiveUser user,
        HomePackageOriginalInstalledActivationRecord root, HomePackageOriginalInstalledActivationRecord home,
        LinuxHomeOriginalControlledProcess originalHome, Func<NativeWindowsHomeRootWire.Listening?> currentListening,
        Socket listener, string endpoint, CancellationToken originalServiceLifetime)
    {
        private sealed class Selection(NativeWindowsHomeRootWire.SelectedChoice choice, HomePackageOriginalInstalledActivationRecord package)
        {
            internal readonly NativeWindowsHomeRootWire.SelectedChoice Choice = choice;
            internal readonly HomePackageOriginalInstalledActivationRecord Package = package;
            internal LinuxHomeOriginalControlledProcess? Child;
            internal Task? Prepare, Launch, Close;
            internal NativeWindowsHomeRootWire.SelectedWitness? Witness;
            internal bool Retired;
            internal readonly TaskCompletionSource LaunchAdmitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private readonly object _gate = new();
        private readonly Dictionary<Guid, Selection> _operations = [];
        private bool _retiring;
        private Func<string, CancellationToken, Task<bool>>? _packageAvailable;
        internal void BindOriginalPackageAvailability(Func<string, CancellationToken, Task<bool>> original)
        { lock (_gate) { if (_packageAvailable is not null) throw new InvalidOperationException("Bind the same canonical package availability once."); _packageAvailable = original; } }
        private async Task DemandPackageAvailable(string package, CancellationToken token)
        {
            var current = _packageAvailable ?? throw new UnauthorizedAccessException("The original canonical package owner is not bound.");
            if (!await current(package, token).ConfigureAwait(false)) throw new UnauthorizedAccessException("The original package is removed or its canonical outcome is pending.");
        }

        private readonly CancellationTokenSource _peerStop = CancellationTokenSource.CreateLinkedTokenSource(originalServiceLifetime);
        private Task? _peerServer;
        internal Task StartOriginalPeerServer() { lock (_gate) return _peerServer ??= ServePeers(); }
        private async Task ServePeers()
        {
            try
            {
                while (!_peerStop.IsCancellationRequested)
                {
                    using var accepted = await listener.AcceptAsync(_peerStop.Token).ConfigureAwait(false);
                    var observed = HomeNativePeerObservation.FromAcceptedUnixSocket(accepted);
                    Selection? selected;
                    lock (_gate) selected = observed?.OperatingSystemPrincipalId == user.OperatingSystemPrincipalId
                        ? _operations.Values.SingleOrDefault(value => value.Child?.ProcessId == observed.ProcessId && !value.Retired) : null;
                    if (selected is null) continue; // an unissued process receives no installed metadata
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_peerStop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    try
                    {
                        await selected.LaunchAdmitted.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                        await selected.Launch!.WaitAsync(deadline.Token).ConfigureAwait(false);
                        using var stream = new NetworkStream(accepted, ownsSocket: false);
                        var request = await ReadFrame<LinuxHomeRootControlWire.Request>(stream, deadline.Token).ConfigureAwait(false);
                        if (request is null) continue;
                        if (request is not { SchemaVersion: 1, Command: "observe-home-for-selected" } || request.CorrelationId == Guid.Empty)
                            throw new UnauthorizedAccessException("Use only the bounded source-owned selected-app Home observation.");
                        await DemandPackageAvailable(selected.Package.PackageId, deadline.Token).ConfigureAwait(false);
                        selected.Child!.DemandOriginalCurrent(); originalHome.DemandOriginalCurrent();
                        kernel.DemandOriginalCurrent(user); await code.DemandPinnedCurrentAsync(deadline.Token).ConfigureAwait(false);
                        var listening = currentListening();
                        if (listening is null || selected.Choice.OriginalListening != listening || selected.Witness is null ||
                            HomeNativePeerObservation.FromAcceptedUnixSocket(accepted) != observed)
                            throw new UnauthorizedAccessException("The same actual selected child and original Home listening lease are required.");
                        await WriteFrame(stream, new LinuxHomeRootControlWire.Response(1, request.CorrelationId, true, null) {
                            SelectedChoice = selected.Choice, SelectedWitness = selected.Witness, Listening = listening, Activation = home,
                            OriginalHomeProcess = new(originalHome.ProcessId, user.OperatingSystemPrincipalId,
                                originalHome.ProcessStartIdentity!, "home", home.PackageId,
                                Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(home))),
                                "sha256:" + home.ActivatedFiles.Single(value => value.RelativePath == home.EntrypointRelativePath).Sha256)
                        }, deadline.Token).ConfigureAwait(false);
                        selected.Child.DemandOriginalCurrent(); originalHome.DemandOriginalCurrent();
                    }
                    catch (OperationCanceledException cause) when (deadline.IsCancellationRequested && cause.CancellationToken == deadline.Token)
                    { if (_peerStop.IsCancellationRequested) _peerStop.Token.ThrowIfCancellationRequested(); }
                    catch (UnauthorizedAccessException) { /* exact connected peer did not satisfy current installed ownership */ }
                }
            }
            catch (OperationCanceledException cause) when (_peerStop.IsCancellationRequested && cause.CancellationToken == _peerStop.Token) { }
            catch { owner._stop.Cancel(); throw; }
        }
        internal async Task<LinuxHomeRootControlWire.Response?> HandleAsync(LinuxHomeRootControlWire.Request request,
            NativeWindowsHomeRootWire.Listening? listening, CancellationToken token)
        {
            if (request.Command is not ("prepare-selected-application" or "launch-selected-application" or "release-selected-application" or "observe-selected-process")) return null;
            kernel.DemandOriginalCurrent(user); await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
            if (listening is null) throw new UnauthorizedAccessException("The original controlled Home must publish its actual listener before selected launch.");
            if (request.Command == "observe-selected-process")
            {
                Selection? actual;
                lock (_gate) actual = _operations.Values.SingleOrDefault(value => value.Child?.ProcessId == request.ProcessId &&
                    value.Witness is not null && !value.Retired);
                if (actual is null) return new(1, request.CorrelationId, true, null);
                if (_packageAvailable is null || !await _packageAvailable(actual.Package.PackageId, token).ConfigureAwait(false)) return new(1, request.CorrelationId, true, null);
                actual.Child!.DemandOriginalCurrent();
                return new(1, request.CorrelationId, true, null) { SelectedChoice = actual.Choice, SelectedWitness = actual.Witness, Activation = actual.Package };
            }
            var choice = request.SelectedChoice;
            if (choice is null || choice.OperationId == Guid.Empty || choice.OriginalListening != listening ||
                choice.InstalledApplicationId == Guid.Empty || choice.InstalledApplicationRevision < 0 ||
                choice.AppId is "root" or "home" || string.IsNullOrWhiteSpace(choice.AppId))
                throw new UnauthorizedAccessException("Only the same actual Home listening lease may issue an installed selected-app choice.");
            var package = code.Packages.SingleOrDefault(value => value.PackageId == choice.PackageId && value.AppId == choice.AppId)
                ?? throw new UnauthorizedAccessException("The selected app is not in the same retained signed activation.");
            if (request.Command != "release-selected-application") await DemandPackageAvailable(package.PackageId, token).ConfigureAwait(false);
            var descriptor = HomePackageOriginalArtifactDescriptorParser.Parse(Convert.FromBase64String(package.SignedDescriptorBase64),
                Convert.FromBase64String(package.DescriptorPayloadBase64), package.CatalogueRevision,
                new(package.PackageId, HomePackageAction.Install, "linux.root.selected", package.Version, null, package.CatalogueRevision)).Descriptor;
            if (descriptor.ComponentClass is not ("optional-app" or "mandatory-shared-core") ||
                choice.ActivationSha256 != Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(package))) ||
                choice.DescriptorSha256 != Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(package.SignedDescriptorBase64))))
                throw new UnauthorizedAccessException("The actual selected package activation and signed descriptor changed.");
            Selection selected;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_retiring, this);
                if (!_operations.TryGetValue(choice.OperationId, out selected!))
                {
                    if (request.Command != "prepare-selected-application" || _operations.Count >= 64)
                        throw new UnauthorizedAccessException("Prepare one bounded original operation before launch; operation IDs are never replayed.");
                    _operations.Add(choice.OperationId, selected = new(choice, package));
                }
                if (selected.Choice != choice || !ReferenceEquals(selected.Package, package) || selected.Retired)
                    throw new UnauthorizedAccessException("The retained operation cannot be rebound or replayed after retirement.");
            }
            if (request.Command == "prepare-selected-application")
            {
                Task prepare;
                lock (_gate) prepare = selected.Prepare ??= Prepare(selected, token);
                await prepare.ConfigureAwait(false);
            }
            else if (request.Command == "launch-selected-application")
            {
                if (selected.Prepare is not { IsCompletedSuccessfully: true } || selected.Child is null)
                    throw new UnauthorizedAccessException("The same actual native child must finish preparation before its individual launch.");
                Task launch;
                lock (_gate) { launch = selected.Launch ??= Launch(selected, token); selected.LaunchAdmitted.TrySetResult(); }
                await launch.ConfigureAwait(false);
                selected.Child.DemandOriginalCurrent();
            }
            else
            {
                // A witnessed resumed child transfers to the retained Root service
                // lifetime. An unused paused reservation is retired without exec.
                if (selected.Witness is null) { lock (_gate) selected.Retired = true; await Close(selected).ConfigureAwait(false); }
                else selected.Child!.DemandOriginalCurrent();
            }
            kernel.DemandOriginalCurrent(user); await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
            return new(1, request.CorrelationId, true, null) { SelectedChoice = choice, SelectedWitness = selected.Witness };
        }
        private async Task Prepare(Selection selected, CancellationToken token)
        {
            // CreateOriginalAsync retains and joins every acquired native original on failure.
            selected.Child = await LinuxHomeOriginalControlledProcess.CreateOriginalAsync(owner, code, kernel, user,
                root, selected.Package, endpoint, token).ConfigureAwait(false);
        }
        private async Task Launch(Selection selected, CancellationToken token)
        {
            await selected.Child!.ResumeOriginalAsync(token).ConfigureAwait(false);
            selected.Child.DemandOriginalCurrent();
            selected.Witness = new(selected.Choice, selected.Child.ProcessId, user.OperatingSystemPrincipalId,
                selected.Child.ProcessStartIdentity ?? throw new IOException("The actual resumed selected child start identity is unavailable."),
                "sha256:" + selected.Package.ActivatedFiles.Single(value => value.RelativePath == selected.Package.EntrypointRelativePath).Sha256);
        }
        private Task Close(Selection selected)
        {
            lock (_gate) return selected.Close ??= Drain();
            async Task Drain()
            {
                var failures = new List<Exception>();
                foreach (var original in new[] { selected.Prepare, selected.Launch })
                    if (original is not null) try { await original.ConfigureAwait(false); } catch (Exception cause) { failures.Add(original.Exception ?? cause); }
                if (selected.Child is { } child)
                    try { await child.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                if (failures.Count != 0) throw new AggregateException("The original selected launch and child cleanup retained actual failures.", failures);
            }
        }
        internal bool HasLiveOriginalPackageChild(string packageId)
        {
            lock (_gate)
            {
                foreach (var selection in _operations.Values.Where(value => value.Package.PackageId == packageId && !value.Retired))
                {
                    if (selection.Child is not { HasOriginalResumeAttempt: true } child) continue;
                    var exit = child.OriginalExit;
                    if (exit is null || !exit.IsCompleted) return true;
                    // A failed original exit reader is not proof that the child stopped.
                    var frame = exit.GetAwaiter().GetResult();
                    if (!frame.StartsWith("OriginalLinuxHomeExitedV1:", StringComparison.Ordinal))
                        throw new InvalidDataException("The original selected child exit observation is invalid.");
                }
                return false;
            }
        }
        internal async Task RetireOriginalPackageChildrenAsync(string packageId)
        {
            Selection[] all;
            lock (_gate)
            {
                if (HasLiveOriginalPackageChild(packageId)) throw new InvalidOperationException("Close the running app before removing it.");
                all = _operations.Values.Where(value => value.Package.PackageId == packageId && !value.Retired).ToArray();
                foreach (var item in all) item.Retired = true;
            }
            var failures = new List<Exception>();
            foreach (var item in all) try { await Close(item).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0) throw new AggregateException("Original package child retirement retained actual failures.", failures);
        }
        internal async Task CloseAndDrainAsync()
        {
            Selection[] all; lock (_gate) { _retiring = true; all = _operations.Values.ToArray(); }
            var failures = new List<Exception>();
            try { _peerStop.Cancel(); } catch (Exception cause) { failures.Add(cause); }
            if (_peerServer is { } server) try { await server.ConfigureAwait(false); } catch (Exception cause) { failures.Add(server.Exception ?? cause); }
            foreach (var actual in all) try { await Close(actual).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            try { _peerStop.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0) throw new AggregateException("Original selected-app children did not all drain.", failures);
        }
    }
}
