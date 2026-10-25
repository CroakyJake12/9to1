using System.Runtime.CompilerServices;
using System.Collections.Frozen;
using HomeAppsSnapshot = HavenOS.Home.Apps.HomeAppsSnapshot;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Haven.Infrastructure.Native.Windows;

namespace Haven.Infrastructure.Native.Linux;

public sealed partial class LinuxHomeRootClient : IHomeAppsFeatureSource, ICanonicalInstalledApplicationLaunchProducer,
    ICanonicalOriginalWriteSettlementPinOwner<ICanonicalInstalledApplicationLaunchIntent, ICanonicalInstalledApplicationLaunchAcknowledgment>
{
    private sealed record AppsCursor(string Revision, HomeAppsQuery Query, int Offset);
    private sealed record AppsView(string Revision, IReadOnlyList<InstalledApplicationReference> Rows);
    private readonly Dictionary<string, AppsCursor> _appsCursors = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<HomeAppsSnapshot, AppsView> _appsSnapshots = new();
    private HomeAppsSnapshot? _currentAppsSnapshot;
    private sealed record AppsOperation(HomePackageActionRequest OriginalRequest, Guid OriginalOperation,
        Task<HomePackageActionResult> OriginalTask);
    private readonly Dictionary<string, AppsOperation> _appsOperations = new(StringComparer.Ordinal);
    public Task<HomeAppsSnapshot> GetSnapshotAsync(HomeAppsQuery query, CancellationToken token) => Admit(body => body(), _ => { }, async (source, caller) =>
    {
        var actualQuery = query.Validate();
        var rows = await source.Read(() => ObserveOriginalApplicationLaunchChoicesWithinSourceAsync(source.Run, source.Retain, caller)).ConfigureAwait(false);
        if (_listening is null || _launchHome is null || _connect?.IsCompletedSuccessfully != true)
            return new(string.Empty, HomeAppsDataState.Unavailable, [], null, null, false,
                new("HomeApps.InstalledRootRequired", "The installed enrolled Root and actual Home listening session are required.", "HomeApps.Catalogue", true, true));
        var revision = source.Invoke(() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { Listening = _listening, Rows = rows.Select(row => new { row, Package = Fingerprint(_choices.GetValue(row, _ => throw new UnauthorizedAccessException("The actual installed choice is unavailable.")).Package) }).ToArray() }))));
        var offset = 0;
        if (actualQuery.PageToken is { } cursorToken)
        {
            AppsCursor? cursor; lock (_gate) _appsCursors.TryGetValue(cursorToken, out cursor);
            if (cursor is null || cursor.Revision != revision || cursor.Query != (actualQuery with { PageToken = null }))
                return new(revision, HomeAppsDataState.Unavailable, [], null, null, false,
                    new("HomeApps.PageExpired", "Refresh the current installed catalogue before continuing this page.", "HomeApps.Catalogue", true, true));
            offset = cursor.Offset;
        }
        var filtered = rows.Where(row => (actualQuery.InstallationState is null or HomePackageInstallationState.Installed) &&
                actualQuery.UpdatesOnly != true && (actualQuery.SearchText is null || row.Label.Contains(actualQuery.SearchText, StringComparison.OrdinalIgnoreCase) ||
                    row.StableLaunchIdentity!.Contains(actualQuery.SearchText, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(row => row.Label, StringComparer.Ordinal).ThenBy(row => row.ApplicationId).ToArray();
        var page = filtered.Skip(offset).Take(actualQuery.PageSize).ToArray();
        string? next = null;
        if (offset + page.Length < filtered.Length)
        {
            lock (_gate)
            {
                // Detached read cursor metadata owns no resources or operation grant.
                // Expiry gives a healthy explicit page-refresh outcome above.
                if (_appsCursors.Count >= 128) _appsCursors.Remove(_appsCursors.Keys.First());
                next = Guid.NewGuid().ToString("N"); _appsCursors.Add(next, new(revision, actualQuery with { PageToken = null }, offset + page.Length));
            }
        }
        var packages = source.Invoke(() => page.Select(row =>
        {
            if (!_choices.TryGetValue(row, out var choice)) throw new UnauthorizedAccessException("The SAME installed row/source choice is required.");
            return new HomePackageEntry(choice.Package.PackageId, choice.Package.AppId, row.Label, null, row.Version, null, null,
                HomePackageInstallationState.Installed, HomePackageCompatibilityState.Unknown,
                "Launch revalidates the actual protected runtime and requests individual Home approval.", null, null, null,
                new[] { HomePackageAction.Launch }.ToFrozenSet(), "home");
        }).ToArray());
        var snapshot = new HomeAppsSnapshot(revision, packages.Length == 0 ? HomeAppsDataState.Empty : HomeAppsDataState.Available,
            Array.AsReadOnly(packages), filtered.Length, next, false);
        lock (_gate) { _appsSnapshots.Add(snapshot, new(revision, rows)); _currentAppsSnapshot = snapshot; }
        return snapshot;
    }, token);
    private HomeCanonicalInstalledApplicationLaunchSource? _launchHome;
    private readonly ConditionalWeakTable<InstalledApplicationReference, LaunchChoice> _choices = new();
    private readonly ConditionalWeakTable<ICanonicalInstalledApplicationLaunchIntent, LaunchWork> _intents = new();
    private readonly List<LaunchWork> _launches = [];
    private bool _launchRetiring;
    private Task? _launchDrain;
    private sealed record LaunchChoice(InstalledApplicationReference Row, HomePackageOriginalInstalledActivationRecord Package,
        NativeWindowsHomeRootWire.Listening Listening);
    private sealed class LaunchIntent(LinuxHomeRootClient issuer, LaunchChoice choice, Guid operation) : ICanonicalInstalledApplicationLaunchIntent
    {
        internal readonly LinuxHomeRootClient Issuer = issuer;
        internal readonly LaunchChoice Choice = choice;
        public AuthenticatedResourceActor Actor => Choice.Listening.Actor;
        public Guid OperationId => operation;
        public string AppId => Choice.Package.AppId;
        public string PackageId => Choice.Package.PackageId;
        public Guid InstalledApplicationId => Choice.Row.ApplicationId;
        public long InstalledApplicationRevision => Choice.Row.Revision;
        public string ActivationSha256 => Fingerprint(Choice.Package);
        public string DescriptorSha256 => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(Choice.Package.SignedDescriptorBase64)));
        public string OriginalHomeLeaseIdentity => Choice.Listening.LeaseIdentity.ToString("D");
        internal NativeWindowsHomeRootWire.SelectedChoice Wire => new(OperationId, AppId, PackageId, InstalledApplicationId,
            InstalledApplicationRevision, ActivationSha256, DescriptorSha256, Choice.Listening);
    }
    private sealed record LaunchAcknowledgment(LaunchIntent Intent, NativeWindowsHomeRootWire.SelectedWitness? Witness, string Reason)
        : ICanonicalInstalledApplicationLaunchAcknowledgment
    {
        public ICanonicalInstalledApplicationLaunchIntent OriginalIntent => Intent;
        public bool Applied => Witness is not null;
        public int ProcessId => Witness?.ProcessId ?? 0;
        public string ProcessStartIdentity => Witness?.ProcessStartIdentity ?? "";
        public string ExecutableIdentity => Witness?.ExecutableIdentity ?? "";
    }
    private sealed class LaunchWork(LaunchIntent intent)
    {
        internal readonly LaunchIntent Intent = intent;
        internal readonly TaskCompletionSource AtomicStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Dispatched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ICanonicalInstalledApplicationLaunchHomeClaim? Claim;
        internal Task<ICanonicalInstalledApplicationLaunchHomeClaim>? Acquisition;
        internal Task<ICanonicalInstalledApplicationLaunchAcknowledgment>? Driver, Atomic;
        internal Task? ClaimClose, DispatchWait, Release;
        internal ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalInstalledApplicationLaunchIntent, ICanonicalInstalledApplicationLaunchAcknowledgment>? ReleasePhase;
        internal bool Prepared, AtomicRetained, Released;
        internal LaunchAcknowledgment? Acknowledgment;
    }
    public void BindOriginalApplicationLaunchSource(HomeCanonicalInstalledApplicationLaunchSource same)
    {
        lock (_gate)
        {
            DemandActive();
            if (_homeStore is null || _profiles is null || _launchHome is not null || _launchRetiring ||
                !same.HasOriginalComposition(_homeStore, _profiles, this)) throw new UnauthorizedAccessException("Bind the SAME original Linux Home launch source once.");
            _launchHome = same;
        }
    }
    public bool HasOriginalApplicationLaunchSource(HomeCanonicalInstalledApplicationLaunchSource same) => ReferenceEquals(_launchHome, same);
    public Task<IReadOnlyList<InstalledApplicationReference>> ObserveOriginalApplicationLaunchChoicesWithinSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit<IReadOnlyList<InstalledApplicationReference>>(scope, retain, async (source, caller) => {
            if (_launchHome is null || _registry is null || _listening is null) return Array.Empty<InstalledApplicationReference>();
            var actor = await Actor(source, caller).ConfigureAwait(false);
            var rows = await source.Read(() => _registry.RefreshForActorWithinOriginalSourceAsync(actor, source.Run, source.Retain, caller).AsTask()).ConfigureAwait(false);
            var result = new List<InstalledApplicationReference>();
            foreach (var row in rows.Where(value => value.ProviderId == ProviderId && value.Enabled && value.ProfileAccessible &&
                value.StableLaunchIdentity is not null && value.StableLaunchIdentity is not ("root" or "home")))
            {
                var packageId = row.OsApplicationId.StartsWith("9to1.package:", StringComparison.Ordinal) ? row.OsApplicationId[13..] : "";
                if (!_packageIds.Contains(packageId, StringComparer.Ordinal)) continue;
                var package = await ReadPackage(source, packageId, caller).ConfigureAwait(false);
                if (package.AppId != row.StableLaunchIdentity || row.HomeProfileId != actor.ProfileId ||
                    row.Entrypoint != Path.Combine(package.InstallationRoot, package.EntrypointRelativePath)) continue;
                if (ParseDescriptor(package).ComponentClass is not ("optional-app" or "mandatory-shared-core")) continue;
                var choice = new LaunchChoice(row, package, _listening);
                lock (_gate) { _choices.Remove(row); _choices.Add(row, choice); }
                result.Add(row);
            }
            if (actor != await Actor(source, caller).ConfigureAwait(false)) throw new UnauthorizedAccessException("The actual Linux Home launch actor changed.");
            return Array.AsReadOnly(result.ToArray());
        }, token);
    public Task<ICanonicalInstalledApplicationLaunchIntent> PrepareOriginalApplicationLaunchWithinSourceAsync(InstalledApplicationReference sameRow,
        Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Admit<ICanonicalInstalledApplicationLaunchIntent>(scope, retain, async (source, caller) => {
            if (operationId == Guid.Empty || !_choices.TryGetValue(sameRow, out var choice)) throw new UnauthorizedAccessException("Use the exact current source-issued Linux installed choice.");
            await DemandChoice(source, choice, caller).ConfigureAwait(false);
            lock (_gate)
            {
                if (_launchRetiring || _launches.Count >= 64 || _launches.Any(value => value.Intent.OperationId == operationId))
                    throw new InvalidOperationException("Settle retained Linux launch originals before creating another operation.");
                var intent = new LaunchIntent(this, choice, operationId); var work = new LaunchWork(intent);
                _intents.Add(intent, work); _launches.Add(work); return intent;
            }
        }, token);
    private LaunchWork RequireLaunch(ICanonicalInstalledApplicationLaunchIntent intent) => intent is LaunchIntent actual &&
        ReferenceEquals(actual.Issuer, this) && _intents.TryGetValue(intent, out var work) && ReferenceEquals(work.Intent, actual)
            ? work : throw new UnauthorizedAccessException("The SAME Linux Root source-issued original launch intent is required.");
    public bool IsIssuedOriginalLaunchIntent(ICanonicalInstalledApplicationLaunchIntent intent) => intent is LaunchIntent actual &&
        ReferenceEquals(actual.Issuer, this) && _intents.TryGetValue(intent, out var work) && ReferenceEquals(work.Intent, actual);
    public string GetOriginalLaunchIntentDigest(ICanonicalInstalledApplicationLaunchIntent intent)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(RequireLaunch(intent).Intent.Wire)));
    public Task ValidateOriginalLaunchIntentWithinSourceAsync(ICanonicalInstalledApplicationLaunchIntent intent, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Admit(scope, retain, async (source, caller) => { await DemandChoice(source, RequireLaunch(intent).Intent.Choice, caller).ConfigureAwait(false); return true; }, token);
    private async Task DemandChoice(CanonicalSqliteOriginalSourceScope source, LaunchChoice choice, CancellationToken token)
    {
        if (_registry is null || _launchHome is null || _listening != choice.Listening || _home is null || _actualListening is null ||
            !_home.IsCurrentOriginalRootListeningObservation(_actualListening)) throw new UnauthorizedAccessException("The original Linux Home launch tuple retired.");
        var row = await source.Read(() => _registry.ResolveLaunchForActorWithinOriginalSourceAsync(choice.Row.ApplicationId,
            choice.Row.Revision, choice.Listening.Actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        var package = await ReadPackage(source, choice.Package.PackageId, token).ConfigureAwait(false);
        var listening = await Exchange(source, new(1, Guid.NewGuid(), "check-home"), token).ConfigureAwait(false);
        if (row != choice.Row || Fingerprint(package) != Fingerprint(choice.Package) || listening.Listening != choice.Listening ||
            choice.Listening.Actor != await Actor(source, token).ConfigureAwait(false)) throw new UnauthorizedAccessException("The actual Linux installed choice/actor/activation/listener changed.");
    }
    public void DemandOriginalPinnedLaunchIntent(ICanonicalInstalledApplicationLaunchIntent intent)
    {
        var work = RequireLaunch(intent); DemandActive(); DemandRoot();
        if (_launchHome is null || !work.Prepared || _listening != work.Intent.Choice.Listening || _home is null || _actualListening is null ||
            !_home.IsCurrentOriginalRootListeningObservation(_actualListening)) throw new UnauthorizedAccessException("The original Root-issued runtime preparation and Home lease must remain pinned.");
    }
    public Task<ICanonicalInstalledApplicationLaunchAcknowledgment> ExecuteOriginalApplicationLaunchWithinSourceAsync(ICanonicalInstalledApplicationLaunchIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var work = RequireLaunch(intent);
        lock (_gate)
        {
            if (work.Driver is not null) return work.Driver;
            if (_launchRetiring) throw new ObjectDisposedException(nameof(LinuxHomeRootClient));
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            work.Driver = Admit(scope, retain, async (source, caller) => { await begin.Task.ConfigureAwait(false); return await Execute(work, source, caller).ConfigureAwait(false); }, token);
            begin.SetResult(); return work.Driver;
        }
    }
    private async Task<ICanonicalInstalledApplicationLaunchAcknowledgment> Execute(LaunchWork work, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        var home = _launchHome ?? throw new UnauthorizedAccessException("The actual Linux Home launch source is unavailable.");
        Exception? primary = null;
        try
        {
            work.Acquisition = home.AcquireOriginalLaunchWithinSourceAsync(work.Intent, source.Run, source.Retain,
                claim => { work.Claim = claim; work.Acquisition = claim.OriginalAcquisition; }, token);
            try { await source.Read(() => (Task)work.Acquisition).ConfigureAwait(false); }
            catch
            {
                work.Dispatched.TrySetResult();
                if (work.Claim is { } declined)
                { work.ClaimClose ??= declined.CloseAndDrainOriginalAsync(); await source.Read(() => work.ClaimClose).ConfigureAwait(false); }
                if (!source.AcknowledgeOriginalExternalPreEffectRefusal(work.Acquisition, home.IsAcknowledgedOriginalLaunchRefusal)) throw;
                work.Acknowledgment = new(work.Intent, null, "The individual Home launch review was declined.");
                return work.Acknowledgment;
            }
            if (work.Claim is null || !ReferenceEquals(work.Acquisition.Result, work.Claim))
                throw new UnauthorizedAccessException("The SAME original Home launch claim capture is required.");
            await DemandChoice(source, work.Intent.Choice, token).ConfigureAwait(false);
            var prepared = await Exchange(source, new(1, Guid.NewGuid(), "prepare-selected-application") { SelectedChoice = work.Intent.Wire }, token).ConfigureAwait(false);
            if (prepared.SelectedChoice != work.Intent.Wire) throw new UnauthorizedAccessException("The actual Linux Root did not retain the same runtime reservation.");
            work.Prepared = true;
            await source.Read(() => home.AcquireOriginalLaunchEntryWithinSourceAsync(work.Claim, source.Run, source.Retain, token)).ConfigureAwait(false);
            source.Run(() => {
                home.DemandOriginalLaunch(work.Claim, work.Intent);
                work.Atomic = InvokeOriginalLaunchWithinSourceAsync(work.Intent, source.Run, source.Retain, CancellationToken.None);
                home.RetainOriginalLaunch(work.Claim, work.Atomic); work.AtomicRetained = true;
            });
            work.AtomicStart.TrySetResult();
            try { work.Acknowledgment = (LaunchAcknowledgment)await source.Read(() => work.Atomic).ConfigureAwait(false); }
            finally { work.Dispatched.TrySetResult(); }
            await source.Read(() => home.CompleteOriginalLaunchWithinSourceAsync(work.Claim, work.Atomic!, source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false);
            return work.Acknowledgment;
        }
        catch (Exception cause) { primary = cause; throw; }
        finally
        {
            var failures = new List<Exception>();
            work.AtomicStart.TrySetResult();
            if (work.Atomic is { } atomic)
                try { await atomic.ConfigureAwait(false); } catch (Exception cause) { if (!ReferenceEquals(cause, primary)) failures.Add(atomic.Exception ?? cause); }
            work.Dispatched.TrySetResult();
            if (work.Claim is { } claim)
                try { work.ClaimClose ??= claim.CloseAndDrainOriginalAsync(); await source.Read(() => work.ClaimClose).ConfigureAwait(false); }
                catch (Exception cause) { failures.Add(work.ClaimClose?.Exception ?? cause); }
            if (failures.Count != 0) throw new AggregateException("Original Linux native launch and Home claim cleanup retained actual failures.",
                primary is null ? failures : new[] { primary }.Concat(failures));
        }
    }
    public Task<ICanonicalInstalledApplicationLaunchAcknowledgment> InvokeOriginalLaunchWithinSourceAsync(ICanonicalInstalledApplicationLaunchIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var work = RequireLaunch(intent); _launchHome!.DemandOriginalLaunch(work.Claim!, intent); DemandOriginalPinnedLaunchIntent(intent);
        lock (_gate) return work.Atomic ??= Admit<ICanonicalInstalledApplicationLaunchAcknowledgment>(scope, retain, async (source, caller) => {
            await work.AtomicStart.Task.ConfigureAwait(false);
            if (!work.AtomicRetained) throw new UnauthorizedAccessException("Retain the SAME native launch in its held Home WRITE before dispatch.");
            var result = await Exchange(source, new(1, Guid.NewGuid(), "launch-selected-application") { SelectedChoice = work.Intent.Wire }, caller).ConfigureAwait(false);
            var witness = result.SelectedWitness;
            if (result.SelectedChoice != work.Intent.Wire || witness?.OriginalChoice != work.Intent.Wire || witness.ProcessId <= 0 ||
                witness.OperatingSystemPrincipalId != "unix-euid:" + LinuxOriginalDownloadPaths.CurrentUid() ||
                !witness.ProcessStartIdentity.StartsWith("linux-proc-start:", StringComparison.Ordinal) ||
                witness.ExecutableIdentity != "sha256:" + work.Intent.Choice.Package.ActivatedFiles.Single(value => value.RelativePath == work.Intent.Choice.Package.EntrypointRelativePath).Sha256)
                throw new UnauthorizedAccessException("The original Root did not witness the same actual Linux selected child/start/image.");
            return new LaunchAcknowledgment(work.Intent, witness, "The installed application started.");
        }, token);
    }
    public bool IsOriginalLaunchTask(ICanonicalInstalledApplicationLaunchIntent intent, Task<ICanonicalInstalledApplicationLaunchAcknowledgment> same)
        => _intents.TryGetValue(intent, out var work) && ReferenceEquals(work.Atomic, same);
    public bool IsOwnedOriginalLaunchAcknowledgment(ICanonicalInstalledApplicationLaunchIntent intent, ICanonicalInstalledApplicationLaunchAcknowledgment acknowledgment,
        Task<ICanonicalInstalledApplicationLaunchAcknowledgment> same) => IsOriginalLaunchTask(intent, same) && same.IsCompletedSuccessfully &&
        ReferenceEquals(same.Result, acknowledgment) && acknowledgment is LaunchAcknowledgment && ReferenceEquals(acknowledgment.OriginalIntent, intent);
    public Task WaitOriginalSettlementDispatchWithinSourceAsync(ICanonicalInstalledApplicationLaunchIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var work = RequireLaunch(intent);
        lock (_gate) return work.DispatchWait ??= Admit(scope, retain, async (_, _) => {
            await work.Dispatched.Task.ConfigureAwait(false); return true;
        }, token);
    }
    public bool IsOwnedOriginalSettlementDispatch(ICanonicalInstalledApplicationLaunchIntent intent, Task same,
        Task<ICanonicalInstalledApplicationLaunchAcknowledgment>? atomic)
        => _intents.TryGetValue(intent, out var work) && ReferenceEquals(work.DispatchWait, same) && same.IsCompletedSuccessfully &&
            work.Dispatched.Task.IsCompletedSuccessfully && ReferenceEquals(work.Atomic, atomic) && (atomic is null || atomic.IsCompleted);
    public Task ReleaseOriginalSettlementPinsWithinSourceAsync(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalInstalledApplicationLaunchIntent, ICanonicalInstalledApplicationLaunchAcknowledgment> phase,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var work = RequireLaunch(phase.OriginalIntent);
        if (_launchHome?.IsIssuedOriginalSettlementReleasePhase(phase) != true ||
            !ReferenceEquals(phase.OriginalAtomicSqlTask, work.Atomic) || work.DispatchWait?.IsCompletedSuccessfully != true)
            throw new UnauthorizedAccessException("Only the SAME original Home terminal release phase may close or transfer Linux runtime preparation custody.");
        lock (_gate)
        {
            if (work.ReleasePhase is not null && !ReferenceEquals(work.ReleasePhase, phase)) throw new UnauthorizedAccessException("Another release phase cannot replay this Linux launch.");
            work.ReleasePhase = phase;
            return work.Release ??= Admit(scope, retain, async (source, caller) => {
                if (work.Prepared)
                {
                    var response = await Exchange(source, new(1, Guid.NewGuid(), "release-selected-application") { SelectedChoice = work.Intent.Wire }, caller).ConfigureAwait(false);
                    if (response.SelectedChoice != work.Intent.Wire || work.Acknowledgment?.Applied == true && response.SelectedWitness != work.Acknowledgment.Witness)
                        throw new UnauthorizedAccessException("The actual Linux Root did not acknowledge this exact preparation/controlled-child custody transfer.");
                }
                work.Released = true; return true;
            }, token);
        }
    }
    public bool IsOwnedOriginalSettlementPinRelease(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalInstalledApplicationLaunchIntent, ICanonicalInstalledApplicationLaunchAcknowledgment> phase, Task same)
        => _launchHome?.IsIssuedOriginalSettlementReleasePhase(phase) == true && _intents.TryGetValue(phase.OriginalIntent, out var work) &&
            ReferenceEquals(work.ReleasePhase, phase) && ReferenceEquals(work.Release, same) && same.IsCompletedSuccessfully && work.Released;
    private async Task<HomeNativeInstalledPeer?> VerifyOriginalSelectedProcess(CanonicalSqliteOriginalSourceScope source,
        HomeNativeObservedPeer observed, AuthenticatedResourceActor expected, CancellationToken token)
    {
        if (_listening is null || expected != _listening.Actor || observed.OperatingSystemPrincipalId != "unix-euid:" + LinuxOriginalDownloadPaths.CurrentUid() ||
            _home is null || _actualListening is null || !_home.IsCurrentOriginalRootListeningObservation(_actualListening)) return null;
        var response = await Exchange(source, new(1, Guid.NewGuid(), "observe-selected-process") { ProcessId = observed.ProcessId }, token).ConfigureAwait(false);
        var witness = response.SelectedWitness; var choice = response.SelectedChoice; var package = response.Activation;
        if (witness is null || choice is null || package is null || witness.OriginalChoice != choice ||
            witness.ProcessId != observed.ProcessId || witness.OperatingSystemPrincipalId != observed.OperatingSystemPrincipalId ||
            choice.OriginalListening != _listening || choice.AppId is "root" or "home" || choice.PackageId != package.PackageId ||
            choice.AppId != package.AppId || choice.ActivationSha256 != Fingerprint(package) ||
            choice.DescriptorSha256 != Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(package.SignedDescriptorBase64)))) return null;
        var row = await source.Read(() => _registry!.ResolveLaunchForActorWithinOriginalSourceAsync(choice.InstalledApplicationId,
            choice.InstalledApplicationRevision, expected, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (row is null || row.ProviderId != ProviderId || row.StableLaunchIdentity != package.AppId || row.HomeProfileId != expected.ProfileId ||
            row.OsApplicationId != "9to1.package:" + package.PackageId || row.Entrypoint != Path.Combine(package.InstallationRoot, package.EntrypointRelativePath) ||
            expected != await Actor(source, token).ConfigureAwait(false)) return null;
        var descriptor = ParseDescriptor(package);
        var executable = package.ActivatedFiles.Single(value => value.RelativePath == package.EntrypointRelativePath);
        if (witness.ExecutableIdentity != "sha256:" + executable.Sha256 || !witness.ProcessStartIdentity.StartsWith("linux-proc-start:", StringComparison.Ordinal)) return null;
        return new(package.AppId, row.ApplicationId, choice.ActivationSha256, witness.ExecutableIdentity,
            new HashSet<string>(descriptor.RequiredServiceIds, StringComparer.Ordinal));
    }

    public Task DrainOriginalApplicationLaunchesBeforeHomeCloseAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_launchDrain is not null) return _launchDrain;
            _launchRetiring = true; _launchHome?.RequestOriginalRetirement();
            return _launchDrain = Drain(_launches.ToArray());
        }
        static async Task Drain(LaunchWork[] launches)
        {
            var failures = new List<Exception>();
            foreach (var work in launches)
                if (work.Driver is { } driver) try { await driver.ConfigureAwait(false); } catch (Exception cause) { failures.Add(driver.Exception ?? cause); }
            if (failures.Count != 0) throw new AggregateException("Original Linux application launch parents remain failed or unsettled.", failures);
        }
    }
    public Task<HomePackageActionResult> ExecuteAsync(HomePackageActionRequest request, CancellationToken token)
    {
        if (request.IdempotencyKey is not { Length: > 0 and <= 1024 })
            return Task.FromResult(AppsDecision(request, HomePackageOperationState.Rejected, "HomeApps.OperationRequired", "Refresh Apps before launching."));
        lock (_gate)
        {
            if (_appsOperations.TryGetValue(request.IdempotencyKey, out var known))
            {
                if (known.OriginalRequest != request) throw new UnauthorizedAccessException("An existing original operation cannot be rebound to another request.");
                return known.OriginalTask;
            }
            token.ThrowIfCancellationRequested();
            if (_launchRetiring || _appsOperations.Count >= 64) throw new InvalidOperationException("Settle the retained original Apps operations before another launch.");
            var snapshot = _currentAppsSnapshot;
            if (request.Action != HomePackageAction.Launch || request.RequestedVersion is not null || request.RequestedChannel is not null ||
                snapshot is null || request.ExpectedRevision != snapshot.Revision || !_appsSnapshots.TryGetValue(snapshot, out var view))
                return Task.FromResult(AppsDecision(request, HomePackageOperationState.Rejected, "HomeApps.CurrentLaunchChoiceRequired", "Refresh Apps and choose Launch."));
            var row = view.Rows.SingleOrDefault(value => _choices.TryGetValue(value, out var choice) && choice.Package.PackageId == request.PackageId);
            if (row is null || !_choices.TryGetValue(row, out var selected))
                return Task.FromResult(AppsDecision(request, HomePackageOperationState.Rejected, "HomeApps.CurrentLaunchChoiceRequired", "Refresh Apps and choose Launch."));
            var operation = Guid.NewGuid(); var intent = new LaunchIntent(this, selected, operation); var work = new LaunchWork(intent);
            _intents.Add(intent, work); _launches.Add(work);
            var actualLaunch = ExecuteOriginalApplicationLaunchWithinSourceAsync(intent, body => body(), _ => { }, CancellationToken.None);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = Admit(body => body(), _ => { }, async (source, caller) => {
                await begin.Task.ConfigureAwait(false);
                var result = await source.Read(() => actualLaunch).ConfigureAwait(false);
                return AppsDecision(request, result.Applied ? HomePackageOperationState.Succeeded : HomePackageOperationState.Rejected,
                    result.Applied ? "HomeApps.ControlledChildLaunched" : "HomeApps.IndividualReviewDeclined",
                    result.Applied ? "The application started." : "Launch was cancelled.");
            }, CancellationToken.None);
            _appsOperations.Add(request.IdempotencyKey, new(request, operation, actual)); begin.SetResult(); return actual;
        }
    }
    private static HomePackageActionResult AppsDecision(HomePackageActionRequest request, HomePackageOperationState state, string code, string message)
        => new(request.IdempotencyKey, request.PackageId, request.Action, state, code, message, false, true, true, [], [], [], []);

}
