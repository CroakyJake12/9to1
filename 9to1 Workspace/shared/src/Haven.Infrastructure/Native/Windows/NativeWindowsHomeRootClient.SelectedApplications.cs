using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeRootClient : ICanonicalInstalledApplicationLaunchProducer,
    ICanonicalOriginalWriteSettlementPinOwner<ICanonicalInstalledApplicationLaunchIntent, ICanonicalInstalledApplicationLaunchAcknowledgment>
{
    private HomeCanonicalInstalledApplicationLaunchSource? _launchHome;
    private readonly ConditionalWeakTable<InstalledApplicationReference, OriginalLaunchChoice> _launchChoices = new();
    private readonly ConditionalWeakTable<ICanonicalInstalledApplicationLaunchIntent, LaunchInvocation> _launchIntents = new();
    private readonly ConditionalWeakTable<Task, LaunchInvocation> _launchHomeSources = new();
    private readonly List<LaunchInvocation> _liveLaunches = [];
    private Task? _launchWithdrawal, _launchBusinessClose;
    public Task? OriginalApplicationLaunchClose { get { lock (_gate) return _launchBusinessClose; } }
    private bool _launchWithdrawalRequested;
    private CloudflareOriginalTaskLedger? _launchWithdrawalSources;
    private sealed class LaunchReadParent(LaunchInvocation work)
    { internal readonly LaunchInvocation Work = work; internal bool Live = true; }
    private readonly AsyncLocal<LaunchReadParent?> _originalLaunchReadParent = new();
    private LaunchInvocation? OriginalLaunchReadParent(AuthenticatedResourceActor? actor = null, string? appId = null)
    {
        var frame = _originalLaunchReadParent.Value;
        return frame is { Live: true } && IsLiveOriginalLaunchReadParent(frame.Work) &&
            (actor is null || actor == frame.Work.Intent.Actor) && (appId is null || appId == frame.Work.Intent.AppId)
            ? frame.Work : null;
    }
    private bool IsLiveOriginalLaunchReadParent(LaunchInvocation? work)
    {
        lock (_gate) return work is not null && work.Driver is { IsCompleted: false } &&
            _launchIntents.TryGetValue(work.Intent, out var issued) && ReferenceEquals(issued, work) &&
            _liveLaunches.Any(known => ReferenceEquals(known, work));
    }
    private static bool IsIndependentlyHealthyLaunch(LaunchInvocation work)
    {
        var driver = work.Driver;
        if (driver?.IsCompletedSuccessfully != true) return false;
        _ = driver.GetAwaiter().GetResult(); // Exact independent enclosing join, bounded by its completed status.
        return work.ClaimClose?.IsCompletedSuccessfully == true && work.Released &&
            work.HomeCleanup.OriginalErrors.Count == 0 && work.HomeCleanup.OriginalTasks.All(raw => raw.IsCompletedSuccessfully) &&
            work.HomeSources.All(raw => raw.IsCompletedSuccessfully || work.KnownDeclined && ReferenceEquals(raw, work.Acquisition)) &&
            work.DispatchWait?.IsCompletedSuccessfully == true && work.Release?.IsCompletedSuccessfully == true;
    }
    public void BindOriginalApplicationLaunchSource(HomeCanonicalInstalledApplicationLaunchSource sameSource)
    {
        if (_homeStore is null || _homeProfiles is null || !sameSource.HasOriginalComposition(_homeStore, _homeProfiles, this))
            throw new UnauthorizedAccessException("The SAME actual Home/Root client/installed launch source tuple is required.");
        lock (_gate)
        {
            if (_launchHome is not null || _retiring || _launchWithdrawalRequested || _liveLaunches.Count != 0)
                throw new InvalidOperationException("Bind the actual Home launch source once before any launch admission.");
            _launchHome = sameSource;
        }
    }
    public bool HasOriginalApplicationLaunchSource(HomeCanonicalInstalledApplicationLaunchSource same) => ReferenceEquals(_launchHome, same);
    private sealed record OriginalLaunchChoice(InstalledApplicationReference Row, PackageObservation Package,
        NativeWindowsHomeRootWire.Listening Listening);
    private sealed class LaunchIntent(NativeWindowsHomeRootClient issuer, OriginalLaunchChoice choice, Guid operation)
        : ICanonicalInstalledApplicationLaunchIntent
    {
        internal readonly NativeWindowsHomeRootClient Issuer = issuer;
        internal readonly OriginalLaunchChoice OriginalChoice = choice;
        public AuthenticatedResourceActor Actor => OriginalChoice.Package.Actor;
        public Guid OperationId { get; } = operation;
        public string AppId => OriginalChoice.Package.AppId;
        public string PackageId => OriginalChoice.Package.PackageId;
        public Guid InstalledApplicationId => OriginalChoice.Row.ApplicationId;
        public long InstalledApplicationRevision => OriginalChoice.Row.Revision;
        public string ActivationSha256 => OriginalChoice.Package.Fingerprint;
        public string DescriptorSha256 => Convert.ToHexString(SHA256.HashData(OriginalChoice.Package.Artifact.SignedDescriptorBytes.Span));
        public string OriginalHomeLeaseIdentity => OriginalChoice.Listening.LeaseIdentity.ToString("D");
        internal NativeWindowsHomeRootWire.SelectedChoice ToWire() => new(OperationId, AppId, PackageId,
            InstalledApplicationId, InstalledApplicationRevision, ActivationSha256, DescriptorSha256, OriginalChoice.Listening);
        internal string Digest => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { Actor, OperationId, OriginalChoice.Row, PackageId, AppId, ActivationSha256, DescriptorSha256, OriginalChoice.Listening })));
    }
    private sealed class LaunchAcknowledgment(LaunchIntent intent, NativeWindowsHomeRootWire.SelectedWitness? witness, string reason)
        : ICanonicalInstalledApplicationLaunchAcknowledgment
    {
        public ICanonicalInstalledApplicationLaunchIntent OriginalIntent => intent;
        public bool Applied => witness is not null;
        public int ProcessId => witness?.ProcessId ?? 0;
        public string ProcessStartIdentity => witness?.ProcessStartIdentity ?? string.Empty;
        public string ExecutableIdentity => witness?.ExecutableIdentity ?? string.Empty;
        public string Reason => reason;
    }
    private sealed class LaunchInvocation(LaunchIntent intent, object owner)
    {
        internal readonly LaunchIntent Intent = intent;
        internal readonly object Gate = new();
        internal readonly List<Task> HomeSources = [];
        internal readonly CloudflareOriginalTaskLedger HomeCleanup = NewSource(owner);
        internal readonly TaskCompletionSource ReviewPublished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Dispatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource AtomicStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ICanonicalInstalledApplicationLaunchHomeClaim? Claim;
        internal Task<ICanonicalInstalledApplicationLaunchHomeClaim>? Acquisition;
        internal Task<ICanonicalInstalledApplicationLaunchAcknowledgment>? Driver, Atomic;
        internal NativeWindowsHomeRootWire.Response? PreparationReceipt;
        internal Task? ClaimClose, DispatchWait, Release;
        internal CloudflareOriginalTaskLedger? WaitSources, ReleaseSources;
        internal ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalInstalledApplicationLaunchIntent, ICanonicalInstalledApplicationLaunchAcknowledgment>? Phase;
        internal LaunchAcknowledgment? Acknowledgment;
        internal bool AtomicAdmitted, Released, KnownDeclined;
        private static CloudflareOriginalTaskLedger NewSource(object issuer)
        { var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(issuer); return source; }
    }
    private LaunchInvocation RequireOriginalLaunch(ICanonicalInstalledApplicationLaunchIntent actual)
    {
        if (actual is not LaunchIntent intent || !ReferenceEquals(intent.Issuer, this) ||
            !_launchIntents.TryGetValue(actual, out var work) || !ReferenceEquals(work.Intent, intent))
            throw new UnauthorizedAccessException("Only the SAME Root client's private installed choice can issue a launch intent.");
        return work;
    }
    public bool IsIssuedOriginalLaunchIntent(ICanonicalInstalledApplicationLaunchIntent actual) =>
        actual is LaunchIntent intent && ReferenceEquals(intent.Issuer, this) && _launchIntents.TryGetValue(actual, out var work) &&
        ReferenceEquals(work.Intent, intent) && IsIssuedOriginalPackageObservation(intent.OriginalChoice.Package);
    public string GetOriginalLaunchIntentDigest(ICanonicalInstalledApplicationLaunchIntent actual) => RequireOriginalLaunch(actual).Intent.Digest;
    public Task<IReadOnlyList<InstalledApplicationReference>> ObserveOriginalApplicationLaunchChoicesWithinSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit<IReadOnlyList<InstalledApplicationReference>>(scope, retain, async source =>
        {
            if (_launchHome is null || _homeProfiles is null || _registry is null || _publishedListening is null ||
                _actualListeningHome is null || _actualListening is null || !_actualListeningHome.IsCurrentOriginalRootListeningObservation(_actualListening))
                return Array.Empty<InstalledApplicationReference>();
            var actor = await Take(source, () => _homeProfiles.GetCurrentWithinOriginalSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The actual current Home actor is unavailable.");
            var rows = await Take(source, () => _registry.RefreshForActorWithinOriginalSourceAsync(actor,
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token).AsTask()).ConfigureAwait(false);
            var result = new List<InstalledApplicationReference>();
            foreach (var row in rows.Where(row => row.ProviderId == ProviderId && row.Enabled && row.ProfileAccessible &&
                row.StableLaunchIdentity is not null && row.StableLaunchIdentity is not ("root" or "home")))
            {
                var package = await Take(source, () => ObserveOriginalPackageWithinSourceAsync(actor, row.StableLaunchIdentity!,
                    body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false);
                if (package is null) continue;
                source.Invoke(() =>
                {
                    DemandOriginalLaunchRow(row, package, actor); var descriptor = ParseOriginalSelectedDescriptor(package);
                    if (descriptor.ComponentClass is not ("optional-app" or "mandatory-shared-core")) return false;
                    _launchChoices.Add(row, new(row, package, _publishedListening)); result.Add(row); return true;
                });
            }
            await DemandOriginalListeningCurrent(source, actor, token).ConfigureAwait(false);
            return Array.AsReadOnly(result.ToArray());
        });
    public Task<ICanonicalInstalledApplicationLaunchIntent> PrepareOriginalApplicationLaunchWithinSourceAsync(
        InstalledApplicationReference sameRow, Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Admit(scope, retain, async source =>
        {
            if (operationId == Guid.Empty || !_launchChoices.TryGetValue(sameRow, out var choice) || !ReferenceEquals(choice.Row, sameRow))
                throw new UnauthorizedAccessException("The SAME actual current Root-issued installed choice is required.");
            await DemandOriginalChoiceCurrent(source, choice, token).ConfigureAwait(false);
            var intent = source.Invoke(() => new LaunchIntent(this, choice, operationId));
            lock (_gate)
            {
                if (_retiring || _launchWithdrawalRequested) throw new ObjectDisposedException(nameof(NativeWindowsHomeRootClient));
                var actual = new LaunchInvocation(intent, this); _launchIntents.Add(intent, actual);
            }
            return (ICanonicalInstalledApplicationLaunchIntent)intent;
        });
    public Task ValidateOriginalLaunchIntentWithinSourceAsync(ICanonicalInstalledApplicationLaunchIntent actual,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit(scope, retain, async source =>
        {
            var work = RequireOriginalLaunch(actual);
            await DemandOriginalChoiceCurrent(source, work.Intent.OriginalChoice, token).ConfigureAwait(false); return true;
        }, originalLaunchParent: _launchIntents.TryGetValue(actual, out var parent) ? OriginalLaunchReadParent(parent.Intent.Actor, parent.Intent.AppId) : null);
    private async Task DemandOriginalChoiceCurrent(CloudflareOriginalTaskLedger source, OriginalLaunchChoice choice, CancellationToken token)
    {
        if (_homeProfiles is null || _registry is null || _launchHome is null || choice.Listening != _publishedListening)
            throw new UnauthorizedAccessException("The actual controlled Home/listener/launch owner changed.");
        var actor = choice.Package.Actor;
        var row = await Take(source, () => _registry.ResolveLaunchForActorWithinOriginalSourceAsync(choice.Row.ApplicationId,
            choice.Row.Revision, actor, body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token).AsTask()).ConfigureAwait(false);
        if (row != choice.Row) throw new UnauthorizedAccessException("The maintained installed application identity/revision changed.");
        await Take(source, () => DemandOriginalPackageCurrentWithinSourceAsync(choice.Package,
            body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false);
        await DemandOriginalListeningCurrent(source, actor, token).ConfigureAwait(false);
        source.Invoke(() => { DemandOriginalLaunchRow(choice.Row, choice.Package, actor); return true; });
    }
    private async Task DemandOriginalListeningCurrent(CloudflareOriginalTaskLedger source, AuthenticatedResourceActor actor, CancellationToken token)
    {
        if (_homeProfiles is null || _actualListeningHome is null || _actualListening is null || _publishedListening is null ||
            !_actualListeningHome.IsCurrentOriginalRootListeningObservation(_actualListening))
            throw new UnauthorizedAccessException("The actual Home listener retired.");
        if (actor != await Take(source, () => _homeProfiles.GetCurrentWithinOriginalSourceAsync(
            body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual Home actor changed.");
        var response = await ExchangeOwned(source, new(1, Guid.NewGuid(), "observe-listening")).ConfigureAwait(false);
        if (!response.Accepted || response.Listening != _publishedListening || response.Listening.Actor != actor)
            throw new UnauthorizedAccessException("The actual Root-controlled Home/listener changed.");
    }
    private static HomePackageArtifactDescriptor ParseOriginalSelectedDescriptor(PackageObservation package) =>
        HomePackageOriginalArtifactDescriptorParser.Parse(package.Artifact.SignedDescriptorBytes, package.Artifact.DescriptorPayloadBytes,
            package.Artifact.CatalogueRevision, new(package.PackageId, HomePackageAction.Install, "root.selected.declaration",
                package.Version, ExpectedRevision: package.Activation.CatalogueRevision)).Descriptor;
    private static void DemandOriginalLaunchRow(InstalledApplicationReference row, PackageObservation package, AuthenticatedResourceActor actor)
    {
        if (package.AppId is "root" or "home" || row.ApplicationId == Guid.Empty || row.Revision < 1 || row.HomeProfileId != actor.ProfileId ||
            row.ProviderId != OriginalProviderId || row.PlatformProfileId != package.OsPrincipal || row.OsApplicationId != "9to1.package:" + package.PackageId ||
            row.StableLaunchIdentity != package.AppId || row.Entrypoint != package.Entrypoint || row.Version != package.Version || !row.Enabled || !row.ProfileAccessible)
            throw new UnauthorizedAccessException("The exact current installed registry row/signed package/actor tuple is required.");
        var descriptor = ParseOriginalSelectedDescriptor(package);
        if (descriptor.AppId != package.AppId || descriptor.PackageId != package.PackageId || descriptor.Platform != "windows" ||
            descriptor.Version != package.Version || descriptor.Abi != package.Activation.Abi)
            throw new UnauthorizedAccessException("The actual signed descriptor differs from its current activation.");
    }
    public void DemandOriginalPinnedLaunchIntent(ICanonicalInstalledApplicationLaunchIntent actual)
    {
        var work = RequireOriginalLaunch(actual);
        lock (work.Gate)
        {
            if (work.Driver is null || work.Driver.IsCompleted || work.Released || work.PreparationReceipt is not { Accepted: true } receipt ||
                receipt.SelectedChoice != work.Intent.ToWire() || work.Intent.OriginalChoice.Listening != _publishedListening ||
                _actualListeningHome is null || _actualListening is null || !_actualListeningHome.IsCurrentOriginalRootListeningObservation(_actualListening))
                throw new UnauthorizedAccessException("The SAME actual live Root runtime preparation must precede held Home entry.");
        }
    }
    public Task<ICanonicalInstalledApplicationLaunchAcknowledgment> ExecuteOriginalApplicationLaunchWithinSourceAsync(
        ICanonicalInstalledApplicationLaunchIntent actual, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var work = RequireOriginalLaunch(actual);
        return Admit(scope, retain, source => ExecutePublished(work, source, retain, token),
            publish: raw =>
            {
                lock (work.Gate) work.Driver = (Task<ICanonicalInstalledApplicationLaunchAcknowledgment>)raw;
                lock (_gate) { _liveLaunches.RemoveAll(IsIndependentlyHealthyLaunch); _liveLaunches.Add(work); }
            },
            existing: () => work.Driver, originalLaunchAdmission: true);
    }
    private async Task<ICanonicalInstalledApplicationLaunchAcknowledgment> ExecutePublished(LaunchInvocation work,
        CloudflareOriginalTaskLedger source, Action<Task> retain, CancellationToken token)
    {
        var priorReadParent = _originalLaunchReadParent.Value; var readParent = new LaunchReadParent(work);
        _originalLaunchReadParent.Value = readParent;
        try
        {
            if (_launchHome is null) throw new UnauthorizedAccessException("The genuine manual Home launch source is unavailable.");
            try
            {
                source.Invoke(() =>
                {
                    work.Acquisition = _launchHome.AcquireOriginalLaunchWithinSourceAsync(work.Intent,
                        body => source.Invoke(() => { body(); return true; }), raw => RetainOriginalHomeLaunchSource(work, raw, retain),
                        claim => { work.Claim = claim; work.Acquisition = claim.OriginalAcquisition; }, token);
                    return true; // The exact failed acquisition has a separate issuer receipt below.
                });
            }
            finally { work.ReviewPublished.TrySetResult(); }
            try { work.Claim = await work.Acquisition!.ConfigureAwait(false); }
            catch (Exception cause)
            {
                work.Dispatch.TrySetResult();
                await CloseOriginalLaunchClaim(work).ConfigureAwait(false);
                if (!_launchHome.IsAcknowledgedOriginalLaunchRefusal(work.Acquisition!)) { source.Capture(work.Acquisition, cause); throw; }
                await ObserveOriginalHomeLaunchSources(work, source).ConfigureAwait(false);
                if (source.OriginalErrors.Count != 0 || work.HomeCleanup.OriginalErrors.Count != 0) throw;
                work.KnownDeclined = true; work.Acknowledgment = new(work.Intent, null, "The individual Home launch review was declined before native effect.");
                return work.Acknowledgment;
            }
            await DemandOriginalChoiceCurrent(source, work.Intent.OriginalChoice, token).ConfigureAwait(false);
            var preparation = await ExchangeOwned(source, new(1, Guid.NewGuid(), "prepare-selected-application") { SelectedChoice = work.Intent.ToWire() }, response =>
            {
                if (response.Accepted && response.SelectedChoice == work.Intent.ToWire()) work.PreparationReceipt = response;
            }).ConfigureAwait(false);
            if (!preparation.Accepted || preparation.SelectedChoice != work.Intent.ToWire())
                throw new UnauthorizedAccessException(preparation.Reason ?? "The actual Root rejected the selected runtime preparation.");
            await Take(source, () => _launchHome.AcquireOriginalLaunchEntryWithinSourceAsync(work.Claim!,
                body => source.Invoke(() => { body(); return true; }), raw => RetainOriginalHomeLaunchSource(work, raw, retain), token)).ConfigureAwait(false);
            source.Invoke(() =>
            {
                _launchHome.DemandOriginalLaunch(work.Claim!, work.Intent);
                work.Atomic = InvokeOriginalLaunchWithinSourceAsync(work.Intent, body => source.Invoke(() => { body(); return true; }),
                    raw => { _ = source.Track(raw); retain(raw); }, CancellationToken.None);
                _launchHome.RetainOriginalLaunch(work.Claim!, work.Atomic); work.AtomicAdmitted = true; return true;
            });
            work.AtomicStart.TrySetResult();
            try { work.Acknowledgment = (LaunchAcknowledgment)await source.AwaitAsync(work.Atomic!).ConfigureAwait(false); }
            finally { work.Dispatch.TrySetResult(); }
            await Take(source, () => _launchHome.CompleteOriginalLaunchWithinSourceAsync(work.Claim!, work.Atomic!,
                body => source.Invoke(() => { body(); return true; }), raw => RetainOriginalHomeLaunchSource(work, raw, retain), CancellationToken.None)).ConfigureAwait(false);
            return work.Acknowledgment;
        }
        finally
        {
            // No later Home claim can be published after this finite preparation
            // boundary, even if its caller rejected the actual returned claim.
            work.ReviewPublished.TrySetResult();
            work.AtomicStart.TrySetResult();
            if (work.Atomic is not null) try { await source.AwaitAsync(work.Atomic).ConfigureAwait(false); } catch (Exception cause) { source.Capture(work.Atomic, cause); }
            work.Dispatch.TrySetResult();
            await CloseOriginalLaunchClaim(work).ConfigureAwait(false);
            await ObserveOriginalHomeLaunchSources(work, source).ConfigureAwait(false);
            foreach (var cause in work.HomeCleanup.OriginalErrors) source.Retain(cause);
            readParent.Live = false; _originalLaunchReadParent.Value = priorReadParent;
        }
    }
    private void RetainOriginalHomeLaunchSource(LaunchInvocation work, Task actual, Action<Task> retain)
    {
        lock (work.Gate) if (!work.HomeSources.Any(known => ReferenceEquals(known, actual))) work.HomeSources.Add(actual);
        lock (_gate) if (!_launchHomeSources.TryGetValue(actual, out _)) _launchHomeSources.Add(actual, work);
        retain(actual); // Custody is rooted before a caller can reject this publication.
    }
    private async Task ObserveOriginalHomeLaunchSources(LaunchInvocation work, CloudflareOriginalTaskLedger source)
    {
        Task[] all; lock (work.Gate) all = work.HomeSources.ToArray();
        foreach (var actual in all)
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { if (_launchHome?.IsAcknowledgedOriginalLaunchRefusal(actual) != true) source.Capture(actual, cause); }
    }
    private async Task CloseOriginalLaunchClaim(LaunchInvocation work)
    {
        if (work.Claim is not null)
        {
            try { work.HomeCleanup.Invoke(() => { work.ClaimClose ??= work.Claim.CloseAndDrainOriginalAsync(); _ = work.HomeCleanup.Track(work.ClaimClose); return true; }); }
            catch (Exception cause) { work.HomeCleanup.Retain(cause); }
            if (work.ClaimClose is not null) try { await work.HomeCleanup.AwaitAsync(work.ClaimClose).ConfigureAwait(false); } catch (Exception cause) { work.HomeCleanup.Capture(work.ClaimClose, cause); }
        }
        await work.HomeCleanup.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
    }
    public Task<ICanonicalInstalledApplicationLaunchAcknowledgment> InvokeOriginalLaunchWithinSourceAsync(ICanonicalInstalledApplicationLaunchIntent actual,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var work = RequireOriginalLaunch(actual);
        _launchHome!.DemandOriginalLaunch(work.Claim!, actual); DemandOriginalPinnedLaunchIntent(actual);
        return Admit(scope, retain, async source =>
        {
            await work.AtomicStart.Task.ConfigureAwait(false);
            if (!work.AtomicAdmitted) throw new UnauthorizedAccessException("The SAME raw native launch must first be retained by its held Home claim.");
            var response = await ExchangeOwned(source, new(1, Guid.NewGuid(), "launch-selected-application") { SelectedChoice = work.Intent.ToWire() }).ConfigureAwait(false);
            var witness = response.SelectedWitness;
            if (!response.Accepted || response.SelectedChoice != work.Intent.ToWire() || witness is null || witness.OriginalChoice != work.Intent.ToWire() ||
                witness.ProcessId <= 0 || witness.OperatingSystemPrincipalId != work.Intent.OriginalChoice.Package.OsPrincipal ||
                !witness.ProcessStartIdentity.StartsWith("windows-filetime:", StringComparison.Ordinal) ||
                witness.ExecutableIdentity != "sha256:" + work.Intent.OriginalChoice.Package.Activation.ActivatedFiles.Single(file =>
                    file.RelativePath == work.Intent.OriginalChoice.Package.Activation.EntrypointRelativePath).Sha256)
                throw new UnauthorizedAccessException("The actual Root did not acknowledge the SAME controlled native child/start/image.");
            return (ICanonicalInstalledApplicationLaunchAcknowledgment)new LaunchAcknowledgment(work.Intent, witness, "The actual Root-controlled native child resumed.");
        }, publish: raw => work.Atomic = (Task<ICanonicalInstalledApplicationLaunchAcknowledgment>)raw, existing: () => work.Atomic, originalLaunchParent: work);
    }
    public bool IsOriginalLaunchTask(ICanonicalInstalledApplicationLaunchIntent actual, Task<ICanonicalInstalledApplicationLaunchAcknowledgment> same) =>
        _launchIntents.TryGetValue(actual, out var work) && ReferenceEquals(work.Atomic, same);
    public bool IsOwnedOriginalLaunchAcknowledgment(ICanonicalInstalledApplicationLaunchIntent actual,
        ICanonicalInstalledApplicationLaunchAcknowledgment acknowledgment, Task<ICanonicalInstalledApplicationLaunchAcknowledgment> same) =>
        IsOriginalLaunchTask(actual, same) && same.IsCompletedSuccessfully && ReferenceEquals(same.GetAwaiter().GetResult(), acknowledgment) &&
        acknowledgment is LaunchAcknowledgment && ReferenceEquals(acknowledgment.OriginalIntent, actual);
    public bool IsAcknowledgedOriginalLaunchSourceRefusal(Task sameRaw) => _launchHomeSources.TryGetValue(sameRaw, out var work) &&
        ReferenceEquals(work.Acquisition, sameRaw) && work.KnownDeclined && work.Driver?.IsCompletedSuccessfully == true &&
        work.ClaimClose?.IsCompletedSuccessfully == true && work.Atomic is null && work.PreparationReceipt is null &&
        work.HomeCleanup.OriginalErrors.Count == 0 && _launchHome?.IsAcknowledgedOriginalLaunchRefusal(sameRaw) == true;
}
