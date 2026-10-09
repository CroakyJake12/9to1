using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>Cold Root admission reads the SAME protected canonical package state
/// and authenticates the actually running Root against its enrolled publisher,
/// signed catalogue and complete activated bytes. No running Home session, public
/// installation flag, caller-supplied signer/key or second database is accepted.</summary>
public sealed partial class NativeWindowsHomeInstalledRootAdmission : IAsyncDisposable
{
    private readonly NativeWindowsHomeInstallerBootstrapAdmission _publisher;
    private readonly HomePackageDatabase _database;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly string _file;
    private readonly System.IO.Pipes.NamedPipeClientStream? _connectedRoot;
    private readonly NativeWindowsHomeRegisteredRootServiceContext? _serviceContext;
    private readonly object _gate = new();
    private readonly List<Invocation> _originals = [];
    private readonly ConditionalWeakTable<Observation, Invocation> _issued = new();
    private readonly ConditionalWeakTable<Task, Invocation> _originalReceipts = new();
    private bool _retiring;
    private Task? _close;
    internal sealed class Held(IDisposable actual)
    {
        internal readonly IDisposable Actual = actual;
        internal WindowsOriginalFileIdentity? Identity;
        internal string? Security;
        internal Task? Close;
    }
    internal sealed class Invocation(CloudflareOriginalTaskLedger sources, Action<Task> retain)
    {
        internal readonly CloudflareOriginalTaskLedger Sources = sources;
        internal readonly Action<Task> Retain = retain;
        internal readonly List<Held> Resources = [];
        internal Task Driver = null!;
        internal Task<HomeCoreStoredState>? OriginalStateRead;
        internal HomeCoreStoredState? OriginalState;
        internal Observation? Result;
        internal string? UnavailableReason;
        internal bool Healthy => Driver.IsCompletedSuccessfully && Sources.OriginalErrors.Count == 0 &&
            Sources.OriginalTasks.All(raw => raw.IsCompletedSuccessfully) && Resources.All(held => held.Close?.IsCompletedSuccessfully == true);
    }
    public sealed class Observation
    {
        internal readonly NativeWindowsHomeInstalledRootAdmission Owner;
        internal readonly NativeWindowsHomeInstallerBootstrapAdmission.OriginalRootPublisherCatalogue Publisher;
        internal readonly HomePackageOriginalPublisherEnrollmentRecord Enrollment;
        internal readonly AuthenticatedResourceActor Actor;
        internal readonly HomePackageDatabaseSnapshot Registry;
        internal readonly InstalledPackage Root, Home;
        internal Observation(NativeWindowsHomeInstalledRootAdmission owner,
            NativeWindowsHomeInstallerBootstrapAdmission.OriginalRootPublisherCatalogue publisher,
            HomePackageOriginalPublisherEnrollmentRecord enrollment, AuthenticatedResourceActor actor,
            HomePackageDatabaseSnapshot registry, InstalledPackage root, InstalledPackage home)
        { Owner = owner; Publisher = publisher; Enrollment = enrollment; Actor = actor; Registry = registry; Root = root; Home = home; }
        public string PublisherCertificateSha256 => Publisher.PublisherCertificateSha256;
        public string RootEntrypoint => Root.Entrypoint;
        public string HomeEntrypoint => Home.Entrypoint;
        public long OriginalRegistryRevision => Registry.Revision;
    }
    internal sealed record InstalledPackage(HomePackageDatabaseEntry Entry, HomePackageArtifactDescriptor Descriptor,
        HomePackageOriginalArtifactObservation SignedArtifact, HomePackageOriginalInstalledActivationRecord Activation,
        string Entrypoint, string EntrypointSha256);

    public NativeWindowsHomeInstalledRootAdmission(NativeWindowsHomeInstallerBootstrapAdmission samePublisherSource,
        HomePackageDatabase sameCanonicalDatabase, FileHomeCoreStateStore sameCanonicalStore,
        HomeLocalProfileIdentity sameProfiles, string actualConfiguredMachineStateFile,
        System.IO.Pipes.NamedPipeClientStream? actualConnectedRoot = null,
        NativeWindowsHomeRegisteredRootServiceContext? actualRootServiceContext = null)
    {
        if (!samePublisherSource.HasOriginalProfiles(sameProfiles) ||
            !sameCanonicalDatabase.HasOriginalProfileComposition(sameCanonicalStore, sameProfiles) ||
            !sameCanonicalStore.IsOriginalConfiguredFile(actualConfiguredMachineStateFile))
            throw new UnauthorizedAccessException("Use the SAME original publisher, canonical package store/database and OS profile composition.");
        _publisher = samePublisherSource; _database = sameCanonicalDatabase; _store = sameCanonicalStore;
        if (actualConnectedRoot is null && actualRootServiceContext is not null && !actualRootServiceContext.HasOriginalProfiles(sameProfiles))
            throw new UnauthorizedAccessException("Use the SAME actual SCM/kernel principal source for the Root service's machine profile.");
        _profiles = sameProfiles; _file = Path.GetFullPath(actualConfiguredMachineStateFile); _connectedRoot = actualConnectedRoot;
        _serviceContext = actualRootServiceContext;
    }
    public bool HasOriginalComposition(NativeWindowsHomeInstallerBootstrapAdmission publisher, HomePackageDatabase database,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles) => ReferenceEquals(_publisher, publisher) &&
        ReferenceEquals(_database, database) && ReferenceEquals(_store, store) && ReferenceEquals(_profiles, profiles);
    public bool IsIssuedOriginalAdmission(Observation same) => same is not null && ReferenceEquals(same.Owner, this) &&
        _issued.TryGetValue(same, out var work) && ReferenceEquals(work.Result, same) && work.Healthy &&
        _publisher.IsIssuedOriginalRootPublisherCatalogue(same.Publisher);
    public bool TryObserveOriginalUnavailable(Task<Observation?> same, out string? reason)
    {
        lock (_gate)
        {
            _originalReceipts.TryGetValue(same, out var work);
            reason = work?.UnavailableReason;
            return work?.Healthy == true && same.Result is null && reason is not null;
        }
    }
    public Task<Observation?> InspectOriginalRuntimeWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
        sources.BindOriginalCallerCallback(body => WithinSource(sources, scope, body));
        var work = new Invocation(sources, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _originals.RemoveAll(old => old.Healthy);
            if (_originals.Count >= 128) throw new InvalidOperationException("Unresolved original Root admissions remain retained for recovery.");
            work.Driver = Drive(work, start.Task, token); _originals.Add(work); _originalReceipts.Add(work.Driver, work);
        }
        try { sources.Invoke(() => { retain(work.Driver); return true; }); } catch (Exception cause) { sources.Retain(cause); }
        finally { start.SetResult(); }
        return (Task<Observation?>)work.Driver;
    }
    private async Task<Observation?> Drive(Invocation work, Task start, CancellationToken token, Observation? expected = null)
    {
        await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        Observation? result = null;
        try
        {
            if (expected is not null) work.Sources.Invoke(() =>
            {
                if (!IsIssuedOriginalAdmission(expected)) throw new UnauthorizedAccessException("The SAME source-issued cold Root admission is required.");
                return true;
            });
            if (!OperatingSystem.IsWindows()) work.UnavailableReason = "InstalledWindowsRootRequired";
            else if (_connectedRoot is null && _serviceContext is null) work.UnavailableReason = "ProtectedRegisteredRootServiceBootstrapRequired";
            else result = await ReadWindows().ConfigureAwait(false);
            if (expected is not null) work.Sources.Invoke(() =>
            {
                if (result is null || result.Actor != expected.Actor ||
                    result.Publisher.CatalogueSha256 != expected.Publisher.CatalogueSha256 || result.Publisher.PublisherCertificateSha256 != expected.Publisher.PublisherCertificateSha256 ||
                    result.Root.Entrypoint != expected.Root.Entrypoint || result.Root.EntrypointSha256 != expected.Root.EntrypointSha256 ||
                    result.Home.Entrypoint != expected.Home.Entrypoint || result.Home.EntrypointSha256 != expected.Home.EntrypointSha256 ||
                    result.Root.Activation.OriginalRootOperationId != expected.Root.Activation.OriginalRootOperationId ||
                    result.Home.Activation.OriginalRootOperationId != expected.Home.Activation.OriginalRootOperationId)
                    throw new UnauthorizedAccessException("The actual current enrolled Root/Home activation changed or retired.");
                return true;
            });
        }
        catch (Exception cause) { work.Sources.Retain(cause); }
        finally { await CloseResources(work).ConfigureAwait(false); }
        await work.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (work.Sources.OriginalErrors.Count != 0)
            throw new AggregateException("Actual Root publisher/package/native source did not settle; preserve its evidence.", work.Sources.OriginalErrors);
        if (result is not null) { work.Result = result; _issued.Add(result, work); }
        return result;

        async Task<Observation?> ReadWindows()
        {
            // Read and hold the existing protected object before the profile API.
            // A missing profile is setup, never a READ-time profile initializer.
            var stateRoot = OpenRoot(work, Path.GetDirectoryName(_file)!);
            var stateFile = OpenFile(work, stateRoot, _file);
            var state = await Read(work, () =>
            {
                work.OriginalStateRead = ReadDocument<HomeCoreStoredState>(work, stateFile, 16 * 1024 * 1024, token);
                return work.OriginalStateRead;
            }).ConfigureAwait(false);
            work.OriginalState = state;
            if (!FileHomeCoreStateStore.IsOriginalProtectedStateValid(state))
                throw new InvalidDataException("The protected canonical machine Home state requires recovery.");
            if (!state.Records.Any(record => record.RecordId == "home.local-profile"))
            { work.UnavailableReason = "OriginalInstallerProfileSetupRequired"; return null; }
            var registryRecord = state.Records.SingleOrDefault(record => record.RecordId == HomePackageDatabase.RecordId);
            if (registryRecord is null) { work.UnavailableReason = "CanonicalSingleInstallerPackageRegistryRequired"; return null; }
            if (registryRecord.RecordType != HomePackageDatabase.RecordType || registryRecord.Scope != HomeDataScope.DeviceLocal ||
                registryRecord.Authority != HomeRecordAuthority.LocalCanonical || registryRecord.SchemaVersion != HomePackageDatabase.CurrentSchemaVersion)
                throw new InvalidDataException("The protected package registry has an incompatible canonical record.");
            var registry = work.Sources.Invoke(() => registryRecord.Payload.Deserialize<HomePackageDatabaseSnapshot>(Json))
                ?? throw new InvalidDataException("The protected canonical package snapshot is empty.");
            if (registry.Revision != registryRecord.Revision || HomePackageDatabase.ValidateOriginalProtectedSnapshot(registry) is not null)
                throw new InvalidDataException("The protected canonical package snapshot is invalid.");
            if (registry.UnknownFields is null || !registry.UnknownFields.TryGetValue(HomePackageOriginalPublisherEnrollmentRecord.Field, out var enrolled))
            { work.UnavailableReason = "ExplicitSingleInstallerPublisherEnrollmentRequired"; return null; }
            var enrollment = work.Sources.Invoke(() => enrolled.Deserialize<HomePackageOriginalPublisherEnrollmentRecord>(Json))
                ?? throw new InvalidDataException("The retained publisher enrollment is malformed.");
            if (enrollment.SchemaVersion == 1 && enrollment.OriginalSignedCatalogueBase64 is null)
            { work.UnavailableReason = "ExplicitInstallerRetainedCatalogueEnrollmentRequired"; return null; }
            var originalCatalogue = work.Sources.Invoke(() => IssueOriginalProtectedCatalogue(work, state, registry, enrollment));
            var catalogue = await Read(work, () => _publisher.InspectOriginalProtectedRootPublisherCatalogueWithinSourceAsync(
                originalCatalogue, _connectedRoot, body => work.Sources.Invoke(() => { body(); return true; }),
                raw => Keep(work, raw), token)).ConfigureAwait(false);
            if (catalogue is null)
            { work.UnavailableReason = "AuthenticatedSignedRootCatalogueRequired"; return null; }
            if (!_publisher.IsIssuedOriginalRootPublisherCatalogue(catalogue))
                throw new UnauthorizedAccessException("The actual source did not issue this Root catalogue.");
            var actor = await Read(work, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The current original Root OS profile is unavailable.");
            var originalPrincipal = _serviceContext is null ? null : await Read(work,
                () => _serviceContext.GetPrincipalAsync(token).AsTask()).ConfigureAwait(false);
            work.Sources.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows Root required.");
                var sid = originalPrincipal is not null ? originalPrincipal["windows-sid:".Length..] : WindowsOriginalFileCustody.CurrentSid();
                if (enrollment.SchemaVersion != HomePackageOriginalPublisherEnrollmentRecord.RetainedCatalogueSchemaVersion || enrollment.PublisherCertificateSha256 != catalogue.PublisherCertificateSha256 ||
                    enrollment.IssuerKeyId != catalogue.SignedProcess._catalogue.IssuerKeyId ||
                    enrollment.CatalogueRevision != catalogue.CatalogueRevision || enrollment.CatalogueSha256 != catalogue.CatalogueSha256 ||
                    enrollment.RootPackageId != catalogue.RootPackageId || enrollment.HomePackageId != catalogue.HomePackageId ||
                    enrollment.OriginalOsPrincipal != sid || enrollment.OriginalActorId != actor.ActorId ||
                    enrollment.OriginalProfileId != actor.ProfileId || enrollment.OriginalEnrollmentOperationId == Guid.Empty ||
                    enrollment.EnrolledAtUtc == default || enrollment.SelectedPackageIds.Count is < 2 or > 128 ||
                    !enrollment.SelectedPackageIds.Contains(catalogue.RootPackageId, StringComparer.Ordinal) ||
                    !enrollment.SelectedPackageIds.Contains(catalogue.HomePackageId, StringComparer.Ordinal))
                    throw new UnauthorizedAccessException("The actual signed Root catalogue/current principal does not match the explicit protected installer enrollment.");
                return true;
            });
            var root = await ReadPackage(work, registry, catalogue, catalogue.RootPackageId, "root", token).ConfigureAwait(false);
            var home = await ReadPackage(work, registry, catalogue, catalogue.HomePackageId, "home", token).ConfigureAwait(false);
            if (root is null || home is null) { work.UnavailableReason ??= "CompleteSignedRootAndHomeActivationRequired"; return null; }
            work.Sources.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows Root required.");
                lock (catalogue.SignedProcess.NativeGate)
                {
                    var actual = catalogue.SignedProcess.Original.Process!; actual.DemandCurrent();
                    if (!StringComparer.OrdinalIgnoreCase.Equals(actual.ExecutablePath, root.Entrypoint) ||
                        !StringComparer.OrdinalIgnoreCase.Equals(actual.ExecutableIdentity, "sha256:" + root.EntrypointSha256))
                        throw new UnauthorizedAccessException("The currently running signed process is not the exact protected activated Root executable.");
                }
                NativeWindowsHomeRegisteredRootServiceContext.DemandOriginalServicePeer(
                    catalogue.SignedProcess.Original.Process!.ProcessId, root.Entrypoint, _file, actual => work.Resources.Add(new(actual)));
                DemandNativeCurrent(work); return true;
            });
            await Read(work, async () => { await _publisher.DemandOriginalRootPublisherCurrentWithinSourceAsync(catalogue,
                body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token).ConfigureAwait(false); return true; }).ConfigureAwait(false);
            if (actor != await Read(work, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The original Root actor changed during cold admission.");
            work.Sources.Invoke(() => { DemandNativeCurrent(work); return true; });
            return new(this, catalogue, enrollment, actor, registry, root, home);
        }
    }
    private async Task<InstalledPackage?> ReadPackage(Invocation work, HomePackageDatabaseSnapshot registry,
        NativeWindowsHomeInstallerBootstrapAdmission.OriginalRootPublisherCatalogue catalogue, string packageId, string appId, CancellationToken token)
    {
        var package = registry.Packages.SingleOrDefault(row => row.PackageId == packageId);
        if (package is null || package.InstallationState != HomePackageInstallState.Installed || package.AppId != appId ||
            package.UnknownFields is null || !package.UnknownFields.TryGetValue(HomePackageOriginalInstalledActivationRecord.Field, out var element)) return null;
        if (!catalogue.SignedProcess.Descriptors.TryGetValue(packageId, out var descriptor) ||
            !catalogue.SignedProcess.Artifacts.TryGetValue(packageId, out var artifact) || descriptor.AppId != appId ||
            descriptor.Platform != "windows" || package.InstalledVersion != descriptor.Version)
            throw new UnauthorizedAccessException("The protected package is not the authenticated enrolled publisher's exact current descriptor.");
        var layout = work.Sources.Invoke(() => element.Deserialize<HomePackageOriginalInstalledActivationRecord>(Json))
            ?? throw new InvalidDataException("The protected activated layout is malformed.");
        work.Sources.Invoke(() =>
        {
            if (layout.SchemaVersion != 1 || layout.PackageId != packageId || layout.AppId != appId || layout.Version != descriptor.Version ||
                layout.Platform != descriptor.Platform || layout.Abi != descriptor.Abi || layout.CatalogueRevision != catalogue.CatalogueRevision ||
                string.IsNullOrWhiteSpace(layout.OriginalRootOperationId) || layout.OriginalRootOperationId.Length > 1024 ||
                !Convert.FromBase64String(layout.SignedDescriptorBase64).AsSpan().SequenceEqual(artifact.SignedDescriptorBytes.Span) ||
                !Convert.FromBase64String(layout.DescriptorPayloadBase64).AsSpan().SequenceEqual(artifact.DescriptorPayloadBytes.Span))
                throw new UnauthorizedAccessException("The protected activation disagrees with the exact actually verified signed descriptor.");
            return true;
        });
        var files = work.Sources.Invoke(() => layout.ActivatedFiles.Take(4097).ToArray());
        if (files.Length is < 1 or > 4096 || files.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length ||
            !HomePackageOriginalInstalledActivationRecord.SafeRelative(layout.EntrypointRelativePath) ||
            files.Any(file => !HomePackageOriginalInstalledActivationRecord.SafeRelative(file.RelativePath) || file.Bytes is < 0 or > 8L * 1024 * 1024 * 1024 ||
                file.Sha256.Length != 64 || file.Sha256.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidDataException("The complete protected activated file cohort is invalid or unbounded.");
        var image = files.SingleOrDefault(file => StringComparer.Ordinal.Equals(file.RelativePath, layout.EntrypointRelativePath))
            ?? throw new InvalidDataException("The actual activated entrypoint is not in its complete file cohort.");
        var root = OpenRoot(work, layout.InstallationRoot);
        await DemandDigest(work, OpenFile(work, root, layout.SignedReceiptPath), descriptor.SignedInstallationReceiptSha256, null, token).ConfigureAwait(false);
        await DemandDigest(work, OpenFile(work, root, layout.CompletePayloadPath), descriptor.PayloadSha256, descriptor.PayloadBytes, token).ConfigureAwait(false);
        var runtimeFiles = new Dictionary<string, SafeFileHandle>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var actualFile = appId is "root" or "home"
                ? OpenImmutableRuntimeFile(work, root, Path.Combine(layout.InstallationRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
                : OpenFile(work, root, Path.Combine(layout.InstallationRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            runtimeFiles.Add(file.RelativePath, actualFile);
            await DemandDigest(work, actualFile, file.Sha256, file.Bytes, token).ConfigureAwait(false);
        }
        if (appId is "root" or "home" && !await HasSupportedOriginalRuntime(work, root, layout, runtimeFiles, token).ConfigureAwait(false))
        { work.UnavailableReason = "ProtectedSelfContainedUnbundledDotNet10RuntimeRequired"; return null; }
        return new(package, descriptor, artifact, layout, Path.Combine(layout.InstallationRoot,
            layout.EntrypointRelativePath.Replace('/', Path.DirectorySeparatorChar)), image.Sha256);
    }
    public Task DemandOriginalCurrentWithinSourceAsync(Observation same, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
        sources.BindOriginalCallerCallback(body => WithinSource(sources, scope, body));
        var work = new Invocation(sources, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _originals.RemoveAll(old => old.Healthy);
            if (_originals.Count >= 128) throw new InvalidOperationException("Unresolved original Root admissions remain retained for recovery.");
            work.Driver = Drive(work, start.Task, token, same); _originals.Add(work); _originalReceipts.Add(work.Driver, work);
        }
        try { sources.Invoke(() => { retain(work.Driver); return true; }); } catch (Exception cause) { sources.Retain(cause); }
        finally { start.SetResult(); }
        return work.Driver;
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 64 };
    private WindowsOriginalRoot OpenRoot(Invocation work, string path) => work.Sources.Invoke(() =>
    {
        var actual = WindowsOriginalFileCustody.RetainRoot(path); work.Resources.Add(new(actual));
        var directory = actual.OpenDirectory(path); var held = new Held(directory); work.Resources.Add(held);
        held.Identity = WindowsOriginalFileCustody.ReadIdentity(directory);
        held.Security = NativeWindowsHomePackageActivationOwner.DemandProtectedMachine(directory); return actual;
    });
    private SafeFileHandle OpenFile(Invocation work, WindowsOriginalRoot root, string path) => work.Sources.Invoke(() =>
    {
        var actual = root.OpenRead(path); var held = new Held(actual); work.Resources.Add(held);
        held.Identity = WindowsOriginalFileCustody.ReadIdentity(actual);
        held.Security = NativeWindowsHomePackageActivationOwner.DemandProtectedMachine(actual); return actual;
    });
    private static void DemandNativeCurrent(Invocation work)
    {
        foreach (var held in work.Resources)
        {
            if (held.Actual is WindowsOriginalRoot root) root.DemandCurrent();
            if (held.Actual is SafeFileHandle file && held.Identity is { } expected &&
                (!WindowsOriginalFileCustody.ReadIdentity(file).SameReadVersion(expected) ||
                    NativeWindowsHomePackageActivationOwner.DemandProtectedMachine(file) != held.Security))
                throw new UnauthorizedAccessException("An actual protected Root/package identity or ACL changed during observation.");
        }
    }
    private async Task<T> ReadDocument<T>(Invocation work, SafeFileHandle actual, int bound, CancellationToken token)
    {
        var identity = work.Sources.Invoke(() => WindowsOriginalFileCustody.ReadIdentity(actual));
        if (identity.Size == 0 || identity.Size > (ulong)bound) throw new InvalidDataException("The bounded protected canonical document is unavailable.");
        var bytes = new byte[checked((int)identity.Size)]; var offset = 0;
        while (offset < bytes.Length)
        {
            var count = await Read(work, () => RandomAccess.ReadAsync(actual, bytes.AsMemory(offset), offset, token).AsTask()).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException(); offset += count;
        }
        if (!work.Sources.Invoke(() => WindowsOriginalFileCustody.ReadIdentity(actual).SameReadVersion(identity)))
            throw new UnauthorizedAccessException("The protected document changed during its original read.");
        return work.Sources.Invoke(() => JsonSerializer.Deserialize<T>(bytes, Json)) ?? throw new InvalidDataException("The actual canonical document is empty.");
    }
    private async Task DemandDigest(Invocation work, SafeFileHandle actual, string expected, long? expectedBytes, CancellationToken token)
    {
        var identity = work.Sources.Invoke(() => WindowsOriginalFileCustody.ReadIdentity(actual));
        if (identity.Size > 8UL * 1024 * 1024 * 1024 || expectedBytes is { } length && (length < 0 || identity.Size != (ulong)length))
            throw new InvalidDataException("The original signed artifact length is unsupported.");
        var hash = work.Sources.Invoke(() => { var actualHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); work.Resources.Add(new(actualHash)); return actualHash; });
        var buffer = new byte[65536]; long offset = 0;
        while ((ulong)offset < identity.Size)
        {
            var count = await Read(work, () => RandomAccess.ReadAsync(actual, buffer.AsMemory(0,
                (int)Math.Min(buffer.Length, (long)identity.Size - offset)), offset, token).AsTask()).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException();
            work.Sources.Invoke(() => { hash.AppendData(buffer.AsSpan(0, count)); return true; }); offset += count;
        }
        work.Sources.Invoke(() =>
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(hash.GetHashAndReset()), expected) ||
                !WindowsOriginalFileCustody.ReadIdentity(actual).SameReadVersion(identity))
                throw new UnauthorizedAccessException("The actual protected activated bytes do not match the verified signed descriptor.");
            return true;
        });
    }
    private void Keep(Invocation work, Task raw) { _ = work.Sources.Track(raw); work.Retain(raw); }
    private async Task<T> Read<T>(Invocation work, Func<Task<T>> factory)
    {
        Task<T>? raw = null; Exception? publication = null;
        try { work.Sources.Invoke(() => { raw = factory(); Keep(work, raw); return true; }); }
        catch (Exception cause) { publication = cause; work.Sources.Retain(cause); }
        T result = default!;
        if (raw is not null) result = await work.Sources.AwaitAsync(raw).ConfigureAwait(false);
        if (publication is not null) throw publication;
        return raw is null ? throw new InvalidOperationException("The actual Root source returned no retained Task.") : result;
    }
    private void WithinSource(CloudflareOriginalTaskLedger sources, Action<Action> scope, Action body) =>
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
            var failures = new List<Exception>(); var sync = new object();
            void Keep(Exception cause) { lock (sync) failures.Add(cause); sources.Retain(cause); }
            try
            {
                try
                {
                    scope(() =>
                    {
                        if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                        {
                            var cause = new InvalidOperationException("The actual Root callback expired, repeated or moved threads.");
                            Keep(cause); throw cause;
                        }
                        try { body(); } catch (Exception cause) { Keep(cause); throw; }
                    });
                    if (used != 1) Keep(new InvalidOperationException("The original Root source callback was not entered."));
                }
                catch (Exception cause) { Keep(cause); }
            }
            finally { Volatile.Write(ref active, 0); }
            Exception[] captured; lock (sync) captured = failures.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (captured.Length != 0) throw new AggregateException("Original Root callback/body/protocol failed.", captured);
            return true;
        });
    private async Task CloseResources(Invocation work)
    {
        foreach (var held in work.Resources.AsEnumerable().Reverse())
        {
            if (held.Close is null)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                held.Close = completion.Task; _ = work.Sources.Track(held.Close);
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { held.Actual.Dispose(); return true; }); completion.SetResult(); }
                catch (Exception cause) { completion.SetException(cause); }
            }
            try { await work.Sources.AwaitAsync(held.Close).ConfigureAwait(false); }
            catch (Exception cause) { work.Sources.Capture(held.Close, cause); }
        }
    }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Drain(start.Task, _originals.ToArray()); }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    private async Task Drain(Task start, Invocation[] originals)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        foreach (var work in originals)
        {
            try { await work.Driver.ConfigureAwait(false); } catch (Exception cause) { failures.Add(work.Driver.Exception ?? cause); }
            if (_launchCohorts.TryGetValue(work, out var launch))
            {
                try { await launch.CloseAndDrainOriginalAsync().ConfigureAwait(false); } catch (Exception cause) { work.Sources.Capture(launch.OriginalClose, cause); }
            }
            if (_selectedLaunchCohorts.TryGetValue(work, out var selectedLaunch))
            {
                try { await selectedLaunch.CloseAndDrainOriginalAsync().ConfigureAwait(false); }
                catch (Exception cause) { work.Sources.Capture(selectedLaunch.OriginalClose, cause); }
            }
            OriginalActivationRead? activation; lock (_gate) activation = _activationReads.SingleOrDefault(pair => ReferenceEquals(pair.Value, work)).Key;
            if (activation is not null)
            {
                Task? close = null;
                try { close = CloseOriginalActivationReadOwnedAsync(activation); await close.ConfigureAwait(false); }
                catch (Exception cause) { work.Sources.Capture(close, cause); }
            }
            await work.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false); failures.AddRange(work.Sources.OriginalErrors);
        }
        if (failures.Count != 0) throw new AggregateException("Actual Root source retirement remains failed; original evidence is retained.", failures);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
