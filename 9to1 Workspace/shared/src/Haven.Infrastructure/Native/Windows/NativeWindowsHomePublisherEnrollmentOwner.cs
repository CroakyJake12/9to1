using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>Initial enrollment belongs to the authenticated SINGLE installer and its
/// explicit original choice. It uses the existing protected Home package registry,
/// not another trust store. No installed-state or service readiness follows from this row.</summary>
public sealed class NativeWindowsHomePublisherEnrollmentOwner : IAsyncDisposable
{
    private readonly NativeWindowsHomeInstallerBootstrapAdmission _installer;
    private readonly HomePackageDatabase _database;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly string _file;
    private readonly object _gate = new();
    private readonly List<Invocation> _originals = [];
    private readonly ConditionalWeakTable<Observation, Invocation> _issued = new();
    private bool _retiring; private Task? _close;
    private sealed class Resource(IDisposable original)
    { internal readonly IDisposable Original = original; internal Task? Close; }
    private sealed class Invocation(CloudflareOriginalTaskLedger sources, Action<Task> retain)
    {
        internal readonly CloudflareOriginalTaskLedger Sources = sources;
        internal readonly Action<Task> Retain = retain;
        internal Task<Observation> Driver = null!;
        internal readonly List<Resource> Resources = [];
        internal WindowsOriginalRoot? Root; internal SafeFileHandle? Directory; internal string? DirectoryPath;
        internal WindowsOriginalFileIdentity DirectoryIdentity; internal string? DirectorySecurity;
        internal Observation? Result; internal Task<HomePackageDatabaseWriteResult>? OriginalWrite;
        internal bool Healthy => Driver.IsCompletedSuccessfully && Sources.OriginalErrors.Count == 0 &&
            Sources.OriginalTasks.All(raw => raw.IsCompletedSuccessfully) && Resources.All(value => value.Close?.IsCompletedSuccessfully == true);
    }
    public sealed class Observation
    {
        internal Observation(NativeWindowsHomePublisherEnrollmentOwner owner,
            NativeWindowsHomeInstallerBootstrapAdmission.OriginalInstallationChoice choice,
            HomePackageOriginalPublisherEnrollmentRecord record, long revision)
        { Owner = owner; OriginalChoice = choice; Record = record; RegistryRevision = revision; }
        internal readonly NativeWindowsHomePublisherEnrollmentOwner Owner;
        public NativeWindowsHomeInstallerBootstrapAdmission.OriginalInstallationChoice OriginalChoice { get; }
        public HomePackageOriginalPublisherEnrollmentRecord Record { get; }
        public long RegistryRevision { get; }
    }
    public NativeWindowsHomePublisherEnrollmentOwner(NativeWindowsHomeInstallerBootstrapAdmission actualInstaller,
        HomePackageDatabase sameDatabase, FileHomeCoreStateStore sameStore, HomeLocalProfileIdentity sameProfiles,
        string actualProtectedCanonicalStateFile)
    {
        if (!actualInstaller.HasOriginalProfiles(sameProfiles) || !sameDatabase.HasOriginalProfileComposition(sameStore, sameProfiles) ||
            !sameStore.IsOriginalConfiguredFile(actualProtectedCanonicalStateFile))
            throw new UnauthorizedAccessException("Use the SAME original installer/profile and canonical Home package database/store.");
        _installer = actualInstaller; _database = sameDatabase; _store = sameStore; _profiles = sameProfiles;
        _file = Path.GetFullPath(actualProtectedCanonicalStateFile);
    }
    public bool HasOriginalComposition(NativeWindowsHomeInstallerBootstrapAdmission installer, HomePackageDatabase database,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles) => ReferenceEquals(_installer, installer) &&
        ReferenceEquals(_database, database) && ReferenceEquals(_store, store) && ReferenceEquals(_profiles, profiles);
    public bool IsIssuedOriginalEnrollment(Observation same) => same is not null && ReferenceEquals(same.Owner, this) &&
        _issued.TryGetValue(same, out var work) && ReferenceEquals(work.Result, same) && work.Healthy;

    /// <param name="explicitPublisherCertificateSha256">Exact authenticated signer shown
    /// by the installer when the operator explicitly chooses publisher enrollment.</param>
    public Task<Observation> EnrollOriginalPublisherWithinSourceAsync(
        NativeWindowsHomeInstallerBootstrapAdmission.OriginalInstallationChoice sameChoice,
        string explicitPublisherCertificateSha256, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sameChoice); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
        sources.BindOriginalCallerCallback(body => WithinBorrowed(sources, scope, body));
        var work = new Invocation(sources, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this); _originals.RemoveAll(value => value.Healthy);
            if (_originals.Count >= 128) throw new InvalidOperationException("Unconfirmed original publisher enrollments remain retained for recovery.");
            work.Driver = Drive(work, start.Task, sameChoice, explicitPublisherCertificateSha256, token); _originals.Add(work);
        }
        try { sources.Invoke(() => { retain(work.Driver); return true; }); } catch (Exception cause) { sources.Retain(cause); }
        finally { start.SetResult(); }
        return work.Driver;
    }
    private async Task<Observation> Drive(Invocation work, Task start,
        NativeWindowsHomeInstallerBootstrapAdmission.OriginalInstallationChoice choice, string confirmation, CancellationToken token)
    {
        await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        Observation? result = null;
        try
        {
            if (work.Sources.OriginalErrors.Count != 0) throw new AggregateException("Original enrollment publication failed before another source.", work.Sources.OriginalErrors);
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual protected Windows publisher enrollment requires Windows.");
            work.Sources.Invoke(() =>
            {
                if (!_installer.IsIssuedOriginalInstallationChoice(choice) || confirmation != choice.Installer.PublisherCertificateSha256)
                    throw new UnauthorizedAccessException("The authenticated original installer and its exact explicitly confirmed publisher are required.");
                return true;
            });
            await Read(work, () => _installer.DemandOriginalChoiceCurrentWithinSourceAsync(choice,
                body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false);
            var actor = await Read(work, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false);
            if (actor != choice.Actor) throw new UnauthorizedAccessException("The original installer actor changed before enrollment.");
            work.Sources.Invoke(() =>
            {
                if (choice.Installer.OriginalSignedCatalogueBytes.Length is < 1 or > HomePackageOriginalPublisherEnrollmentRecord.MaximumRetainedCatalogueBytes)
                    throw new InvalidDataException("The authenticated installer catalogue exceeds the supported canonical enrollment transfer bound.");
                return true;
            });
            OpenProtectedDirectory(work);
            await InspectAndCloseProtectedStateFile(work).ConfigureAwait(false);
            var read = await Read(work, () => _database.ReadOriginalBootstrapWithinSourceAsync(
                body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false);
            if (!read.Succeeded) throw new InvalidDataException("The existing canonical package registry requires recovery: " + read.Failure?.Code);
            var original = read.Snapshot!;
            var fields = work.Sources.Invoke(() => original.UnknownFields?.ToDictionary(pair => pair.Key,
                pair => pair.Value.Clone(), StringComparer.Ordinal) ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal));
            HomePackageOriginalPublisherEnrollmentRecord? previous = null;
            if (fields.TryGetValue(HomePackageOriginalPublisherEnrollmentRecord.Field, out var existing))
            {
                previous = work.Sources.Invoke(() => existing.Deserialize<HomePackageOriginalPublisherEnrollmentRecord>())
                    ?? throw new InvalidDataException("The retained publisher enrollment is malformed; preserve it for explicit recovery.");
                if (!Matches(previous, choice))
                    throw new UnauthorizedAccessException("An existing different publisher/catalogue requires explicit owning recovery; never overwrite or infer trust.");
                if (previous.SchemaVersion == HomePackageOriginalPublisherEnrollmentRecord.RetainedCatalogueSchemaVersion)
                    result = new(this, choice, previous, original.Revision);
            }
            if (result is null)
            {
                // A schema-1 transfer is upgraded only by this SAME authenticated
                // installer and newly explicit publisher choice, under the guarded CAS.
                var record = work.Sources.Invoke(() => new HomePackageOriginalPublisherEnrollmentRecord(HomePackageOriginalPublisherEnrollmentRecord.RetainedCatalogueSchemaVersion,
                    choice.Installer.PublisherCertificateSha256, choice.Installer._catalogue.IssuerKeyId,
                    choice.Installer.CatalogueRevision, choice.Installer.CatalogueSha256, choice.Installer.InstallerExecutableIdentity,
                    choice.OperationId, actor!.ActorId, actor.ProfileId, WindowsOriginalFileCustody.CurrentSid(),
                    choice.Installer._catalogue.HomePackageId, choice.Installer._catalogue.RootPackageId,
                    Array.AsReadOnly(choice.RequiredPackageIds.ToArray()), DateTimeOffset.UtcNow,
                    Convert.ToBase64String(choice.Installer.OriginalSignedCatalogueBytes)));
                work.Sources.Invoke(() => { fields[HomePackageOriginalPublisherEnrollmentRecord.Field] = JsonSerializer.SerializeToElement(record); return true; });
                var updated = original with { UnknownFields = fields };
                var written = await Read(work, () =>
                {
                    var raw = _database.SaveOriginalBootstrapEnrollmentWithinSourceAsync(updated, original.Revision, actor!,
                        new EnrollmentGuard(this, work, choice), body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token);
                    work.OriginalWrite = raw; return raw;
                }).ConfigureAwait(false);
                if (!written.Succeeded) throw new InvalidDataException("Original enrollment CAS did not acknowledge: " + written.Failure?.Code);
                result = new(this, choice, record, written.Snapshot!.Revision);
            }
            DemandProtectedDirectory(work);
            await InspectAndCloseProtectedStateFile(work).ConfigureAwait(false);
        }
        catch (Exception cause) { work.Sources.Retain(cause); }
        finally { await CloseResources(work).ConfigureAwait(false); }
        // Fresh trust/actor reads occur only after protected storage pins close.
        if (result is not null)
            try
            {
                await Read(work, () => _installer.DemandOriginalChoiceCurrentWithinSourceAsync(choice,
                    body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false);
            }
            catch (Exception cause) { work.Sources.Retain(cause); }
        await work.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (work.Sources.OriginalErrors.Count != 0) throw new AggregateException("Original enrollment/storage/trust/cleanup did not settle; preserve the canonical registry.", work.Sources.OriginalErrors);
        if (result is null) throw new InvalidOperationException("No authenticated original publisher enrollment was acknowledged.");
        work.Result = result; _issued.Add(result, work); return result;
    }
    private static bool Matches(HomePackageOriginalPublisherEnrollmentRecord record,
        NativeWindowsHomeInstallerBootstrapAdmission.OriginalInstallationChoice choice) =>
        record.SchemaVersion is 1 or HomePackageOriginalPublisherEnrollmentRecord.RetainedCatalogueSchemaVersion &&
        (record.SchemaVersion == 1 && record.OriginalSignedCatalogueBase64 is null ||
            record.SchemaVersion == HomePackageOriginalPublisherEnrollmentRecord.RetainedCatalogueSchemaVersion &&
            record.OriginalSignedCatalogueBase64 == Convert.ToBase64String(choice.Installer.OriginalSignedCatalogueBytes)) && record.PublisherCertificateSha256 == choice.Installer.PublisherCertificateSha256 &&
        record.IssuerKeyId == choice.Installer._catalogue.IssuerKeyId && record.CatalogueRevision == choice.Installer.CatalogueRevision &&
        record.CatalogueSha256 == choice.Installer.CatalogueSha256 && record.HomePackageId == choice.Installer._catalogue.HomePackageId &&
        record.RootPackageId == choice.Installer._catalogue.RootPackageId && record.OriginalActorId == choice.Actor.ActorId &&
        record.OriginalProfileId == choice.Actor.ProfileId && record.SelectedPackageIds.SequenceEqual(choice.RequiredPackageIds, StringComparer.Ordinal);
    private void OpenProtectedDirectory(Invocation work) => work.Sources.Invoke(() =>
    {
        var path = Path.GetDirectoryName(_file)!; work.DirectoryPath = path;
        work.Root = WindowsOriginalFileCustody.RetainRoot(path); work.Resources.Add(new(work.Root));
        work.Directory = work.Root.OpenDirectory(path); work.Resources.Add(new(work.Directory));
        work.DirectoryIdentity = WindowsOriginalFileCustody.ReadIdentity(work.Directory);
        work.DirectorySecurity = NativeWindowsHomePackageActivationOwner.DemandProtectedMachine(work.Directory);
        return true;
    });
    private static void DemandProtectedDirectory(Invocation work)
    {
        work.Root!.DemandCurrent(); WindowsOriginalFileCustody.DemandPath(work.Directory!, work.DirectoryPath!, true);
        if (!WindowsOriginalFileCustody.ReadIdentity(work.Directory!).SameFile(work.DirectoryIdentity) ||
            NativeWindowsHomePackageActivationOwner.DemandProtectedMachine(work.Directory!) != work.DirectorySecurity)
            throw new UnauthorizedAccessException("The protected canonical package directory changed during enrollment.");
    }
    private async Task InspectAndCloseProtectedStateFile(Invocation work)
    {
        Resource? resource = null;
        try
        {
            work.Sources.Invoke(() =>
            {
                var actual = work.Root!.OpenRead(_file); resource = new(actual); work.Resources.Add(resource);
                WindowsOriginalFileCustody.DemandPath(actual, _file, false);
                var identity = WindowsOriginalFileCustody.ReadIdentity(actual);
                if (!identity.IsRegular || identity.Links != 1 || identity.Size == 0 || identity.Size > 16UL * 1024 * 1024)
                    throw new UnauthorizedAccessException("The canonical enrollment file is not a bounded regular protected object.");
                NativeWindowsHomePackageActivationOwner.DemandProtectedMachine(actual); return true;
            });
        }
        finally { if (resource is not null) await CloseResource(work, resource).ConfigureAwait(false); }
    }
    private sealed class EnrollmentGuard(NativeWindowsHomePublisherEnrollmentOwner owner, Invocation work,
        NativeWindowsHomeInstallerBootstrapAdmission.OriginalInstallationChoice choice) : IHomeOriginalScopedStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
            HomeStateCommitPhase phase, CancellationToken token) => CheckAsync(state, expected, phase, body => body(), _ => { }, token);
        public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
            HomeStateCommitPhase phase, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            if (!OperatingSystem.IsWindows()) return false;
            work.Sources.Invoke(() => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows enrollment guard required."); token.ThrowIfCancellationRequested(); DemandProtectedDirectory(work);
                lock (choice.Installer.NativeGate) choice.Installer.Original.Process!.DemandCurrent(); return true; });
            if (expected != choice.Actor || !owner._installer.IsIssuedOriginalInstallationChoice(choice)) return false;
            // A temporary native file pin is closed before the existing guarded CAS
            // can rename its new image. No ordinary Home read/reentry while held.
            await owner.InspectAndCloseProtectedStateFile(work).ConfigureAwait(false);
            return await owner._profiles.CheckAsync(state, expected, phase, scope, retain, token).ConfigureAwait(false);
        }
    }
    private T WithinBorrowed<T>(CloudflareOriginalTaskLedger sources, Action<Action> scope, Func<T> body) =>
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId; T result = default!;
            try
            {
                scope(() =>
                {
                    if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                        throw new InvalidOperationException("Original enrollment callback is inactive, foreign-thread or consumed.");
                    if (sources.OriginalErrors.Count != 0) throw new AggregateException("Prior original enrollment source failed.", sources.OriginalErrors);
                    result = body();
                });
                if (used != 1) throw new InvalidOperationException("The original enrollment callback was not invoked.");
                return result;
            }
            finally { Interlocked.Exchange(ref active, 0); }
        });
    private void WithinBorrowed(CloudflareOriginalTaskLedger sources, Action<Action> scope, Action body) =>
        WithinBorrowed(sources, scope, () => { body(); return true; });
    private static void Keep(Invocation work, Task raw) { _ = work.Sources.Track(raw); work.Sources.Invoke(() => { work.Retain(raw); return true; }); }
    private static Task<T> Read<T>(Invocation work, Func<Task<T>> factory) => work.Sources.RunToOriginalSettlementAsync(() => work.Sources.Invoke(() => { var raw = factory(); Keep(work, raw); return raw; }));
    private static Task Read(Invocation work, Func<Task> factory) => work.Sources.RunToOriginalSettlementAsync(() => work.Sources.Invoke(() => { var raw = factory(); Keep(work, raw); return raw; }));
    private async Task CloseResources(Invocation work)
    {
        foreach (var resource in work.Resources.AsEnumerable().Reverse())
            try { await CloseResource(work, resource).ConfigureAwait(false); }
            catch (Exception cause) { work.Sources.Retain(cause); }
    }
    private async Task CloseResource(Invocation work, Resource resource)
    {
        try
        {
            if (resource.Close is null) CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                var receipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); resource.Close = receipt.Task;
                Exception? publication = null; _ = work.Sources.Track(receipt.Task);
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { work.Retain(receipt.Task); return true; }); }
                catch (Exception cause) { publication = cause; work.Sources.Retain(cause); }
                try { resource.Original.Dispose(); receipt.SetResult(); } catch (Exception cause) { receipt.SetException(cause); throw; }
                if (publication is not null) throw publication; return true;
            });
        }
        catch (Exception cause) { work.Sources.Retain(cause); }
        if (resource.Close is { } actual) await work.Sources.AwaitAsync(actual).ConfigureAwait(false);
        else throw new InvalidOperationException("The actual enrollment native pin has no original cleanup receipt.");
    }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public void DemandExternalOriginalRetirementJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_gate) { if (_close is null) { _retiring = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(start.Task, _originals.ToArray()); } actual = _close; }
        start?.SetResult(); return actual;
    }
    private static async Task Close(Task start, Invocation[] originals)
    {
        await start.ConfigureAwait(false); List<Exception> failures = [];
        foreach (var original in originals) try { await original.Driver.ConfigureAwait(false); } catch (Exception cause) { failures.Add(original.Driver.Exception ?? cause); }
        foreach (var original in originals) foreach (var resource in original.Resources)
            if (resource.Close is { } close) try { await close.ConfigureAwait(false); } catch (Exception cause) { failures.Add(close.Exception ?? cause); }
            else failures.Add(new InvalidOperationException("An actual retained enrollment resource has no original close receipt."));
        if (failures.Count != 0) throw new AggregateException("Original publisher enrollment sources/resources remain unconfirmed.", failures);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
