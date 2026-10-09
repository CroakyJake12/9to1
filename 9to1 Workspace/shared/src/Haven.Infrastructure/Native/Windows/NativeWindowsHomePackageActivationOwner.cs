using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Microsoft.Win32.SafeHandles;
namespace Haven.Infrastructure.Native.Windows;

/// <summary>Read-only original single-installer activation observation. It uses the
/// maintained native NT handle/ACL/identity owner, existing canonical package registry
/// and SAME currently authenticated root mutation. No keys, DB, install or ACL effect.
/// A historical row, signature bytes, path or implementation of an interface is insufficient.</summary>
public sealed class NativeWindowsHomePackageActivationOwner : IInstalledApplicationOriginalScopedObservationProvider, IAsyncDisposable
{
    public const string CanonicalProviderId = "9to1.home.original-package-activation";
    public string ProviderId => CanonicalProviderId;
    public string CanonicalAppId => _expectedApp;
    private readonly HomePackageOriginalDeviceOwner _device;
    private readonly FileHomeCoreStateStore _deviceStore;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly string _canonicalStateFile;
    private readonly Func<IHomePackageOriginalRootMutation?> _currentRoot;
    private readonly string _expectedApp;
    private readonly object _gate = new(); private readonly List<Invocation> _accepted = [];
    private readonly ConditionalWeakTable<Observation, Observation> _issued = new();
    private bool _retiring; private Task? _close;
    public NativeWindowsHomePackageActivationOwner(HomePackageOriginalDeviceOwner actualDevice,
        FileHomeCoreStateStore actualDeviceStore, string actualCanonicalStateFile,
        IHomePackageOriginalArtifactProvider actualArtifacts, IHomePackageOriginalRootMutationPort actualRootPort,
        HomeLocalProfileIdentity sameHomeProfiles, Func<IHomePackageOriginalRootMutation?> actualCurrentRoot,
        string canonicalAppId)
    {
        ArgumentNullException.ThrowIfNull(actualDevice); ArgumentNullException.ThrowIfNull(actualDeviceStore);
        ArgumentNullException.ThrowIfNull(sameHomeProfiles); ArgumentNullException.ThrowIfNull(actualCurrentRoot);
        if (!actualDevice.HasOriginalActivationComposition(actualDeviceStore, actualArtifacts, actualRootPort) ||
            !actualDeviceStore.IsOriginalConfiguredFile(actualCanonicalStateFile) || string.IsNullOrWhiteSpace(canonicalAppId) || canonicalAppId.Length > 256)
            throw new UnauthorizedAccessException("The SAME configured device registry/root/artifact tuple and canonical app are required.");
        _device = actualDevice; _deviceStore = actualDeviceStore; _profiles = sameHomeProfiles;
        _canonicalStateFile = Path.GetFullPath(actualCanonicalStateFile); _currentRoot = actualCurrentRoot; _expectedApp = canonicalAppId;
    }
    public bool HasOriginalProfiles(HomeLocalProfileIdentity sameProfiles) => ReferenceEquals(_profiles, sameProfiles);
    public bool HasOriginalComposition(HomePackageOriginalDeviceOwner device, FileHomeCoreStateStore store,
        HomeLocalProfileIdentity profiles) => ReferenceEquals(_device, device) && ReferenceEquals(_deviceStore, store) && ReferenceEquals(_profiles, profiles);
    public sealed class Observation
    {
        internal readonly NativeWindowsHomePackageActivationOwner Issuer;
        internal readonly object OriginalRoot;
        internal readonly bool OriginalReadOnlyChannel;
        public AuthenticatedResourceActor Actor { get; }
        public string OsPrincipal { get; }
        public long RegistryRevision { get; }
        private readonly HomePackageDatabaseEntry _package;
        private readonly HomePackageOriginalInstalledActivationRecord _activation;
        internal readonly string CanonicalFingerprint;
        public HomePackageDatabaseEntry Package => _package with { Dependencies = Array.AsReadOnly(_package.Dependencies.ToArray()),
            IntegrityEvidence = Array.AsReadOnly(_package.IntegrityEvidence.ToArray()), RetainedRollbackVersions = Array.AsReadOnly(_package.RetainedRollbackVersions.ToArray()),
            UnknownFields = _package.UnknownFields?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal) };
        public HomePackageOriginalInstalledActivationRecord Activation => _activation with { ActivatedFiles = Array.AsReadOnly(_activation.ActivatedFiles.ToArray()) };
        public string Entrypoint { get; }
        internal Observation(NativeWindowsHomePackageActivationOwner owner, object root, bool originalReadOnlyChannel,
            AuthenticatedResourceActor actor, string principal, long revision, HomePackageDatabaseEntry package,
            HomePackageOriginalInstalledActivationRecord activation, string entrypoint)
        { Issuer = owner; OriginalRoot = root; OriginalReadOnlyChannel = originalReadOnlyChannel; Actor = actor; OsPrincipal = principal; RegistryRevision = revision;
          _package = package; _activation = activation; Entrypoint = entrypoint;
          CanonicalFingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { package, activation }))); }
    }
    private sealed class Held(IDisposable actual)
    {
        internal readonly IDisposable Actual = actual; internal Task? OriginalClose;
        internal WindowsOriginalFileIdentity? OriginalIdentity;
        internal string? OriginalSecurity;
    }
    private sealed class Invocation(CloudflareOriginalTaskLedger sources, Action<Task> retain)
    {
        internal readonly CloudflareOriginalTaskLedger Sources = sources;
        internal readonly Action<Task> Retain = retain;
        internal readonly List<Held> HeldResources = [];
        internal Task<Observation?> Driver = null!;
        internal HomePackageOriginalReadActivation? ReadActivation;
        internal Task? OriginalReadActivationClose;
        internal bool IsHealthy => Driver.IsCompletedSuccessfully && Sources.OriginalErrors.Count == 0 &&
            Sources.OriginalTasks.All(value => value.IsCompletedSuccessfully) && HeldResources.All(value => value.OriginalClose?.IsCompletedSuccessfully == true) &&
            (ReadActivation is null || OriginalReadActivationClose?.IsCompletedSuccessfully == true);
    }
    public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken token)
        => ObserveWithinOriginalSourceAsync(body => body(), _ => { }, token);
    public async ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var observed = await ObserveOriginalActivationCoreWithinSourceAsync(null, scope, retain, token).ConfigureAwait(false);
        if (observed is null) return Array.Empty<InstalledApplicationProfileObservation>();
        return Array.AsReadOnly(new[] { new InstalledApplicationProfileObservation(observed.OsPrincipal,
            "Current Windows principal", false, true, Array.AsReadOnly(new[] {
                new InstalledApplicationObservation("9to1.package:" + observed.Package.PackageId, observed.Entrypoint,
                    observed.Package.Name, observed.Package.InstalledVersion, true) { StableLaunchIdentity = observed.Activation.AppId }
            })) });
    }
    public Task<Observation?> ObserveOriginalActivationWithinSourceAsync(AuthenticatedResourceActor expectedActor,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        return ObserveOriginalActivationCoreWithinSourceAsync(expectedActor, scope, retain, token);
    }
    private Task<Observation?> ObserveOriginalActivationCoreWithinSourceAsync(AuthenticatedResourceActor? expectedActor,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        TaskCompletionSource start; Invocation invocation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _accepted.RemoveAll(value => value.IsHealthy);
            if (_accepted.Count >= 128) throw new InvalidOperationException("Retain unresolved actual package activation reads for recovery.");
            var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
            sources.BindOriginalCallerCallback(body => WithinBorrowed(sources, scope, body));
            invocation = new(sources, retain); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            invocation.Driver = Drive(invocation, start.Task, expectedActor, token); _accepted.Add(invocation);
        }
        try { invocation.Sources.Invoke(() => { retain(invocation.Driver); return true; }); }
        catch (Exception cause) { invocation.Sources.Retain(cause); }
        finally { start.SetResult(); }
        return invocation.Driver;
    }
    private void WithinBorrowed(CloudflareOriginalTaskLedger sources, Action<Action> caller, Action body)
    {
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
            try
            {
                caller(() =>
                {
                    try
                    {
                        if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref used, 1) != 0)
                            throw new InvalidOperationException("Actual package read callback is inactive, foreign-thread or consumed.");
                        if (sources.OriginalErrors.Count != 0) throw new AggregateException("Prior original package source failed.", sources.OriginalErrors);
                        body();
                    }
                    catch (Exception cause) { sources.Retain(cause); throw; }
                });
                if (used != 1) throw new InvalidOperationException("Actual package read callback was not invoked.");
            }
            finally { Interlocked.Exchange(ref active, 0); }
            return true;
        });
    }
    private async Task<Observation?> Drive(Invocation work, Task start, AuthenticatedResourceActor? expectedActor, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        using var source = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        Observation? result = null;
        try { result = await OriginalBody().ConfigureAwait(false); }
        catch (Exception cause) { work.Sources.Retain(cause); }
        await work.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (work.Sources.OriginalErrors.Count != 0) throw new AggregateException("Actual package observation/source/cleanup failed.", work.Sources.OriginalErrors);
        return result;
        async Task<Observation?> OriginalBody()
        {
            var actor = await Read(work, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Actual Home profile required.");
            if (expectedActor is not null && actor != expectedActor) throw new UnauthorizedAccessException("The expected current Home actor changed.");
            Observation? observed = null;
            try { observed = await ReadProtectedActivation(work, actor, token).ConfigureAwait(false); }
            finally { await CloseActualResources(work).ConfigureAwait(false); }
            // Profile check occurs after physical machine-store pins close; navigation
            // never initializes protected root/package resources or writes permission data.
            if (await Read(work, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false) != actor)
                throw new UnauthorizedAccessException("Actual Home actor changed during installed observation.");
            if (observed is not null) work.Sources.Invoke(() => { _issued.Add(observed, observed); return true; });
            return observed;
        }
    }
    private async Task<Observation?> ReadProtectedActivation(Invocation work, AuthenticatedResourceActor actor, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual protected Windows package reader requires Windows.");
        token.ThrowIfCancellationRequested();
        if (await Read(work, () => _profiles.GetCurrentWithinOriginalSourceAsync(
            body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Actual Home actor differs from package observer.");
        var principal = work.Sources.Invoke(WindowsOriginalFileCustody.CurrentSid);
        var read = await Read(work, () => _device.OpenOriginalReadActivationWithinSourceAsync(actor, principal, _expectedApp,
            body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token), actual => work.ReadActivation = actual).ConfigureAwait(false);
        ActivationView view;
        if (read is not null)
        {
            view = work.Sources.Invoke(() => new ActivationView(read, read.Artifact, read.CatalogueRevision,
                read.OriginalActivationOperationId, read.CopySignedDescriptor(), read.CopyDescriptorPayload()));
        }
        else
        {
            // Compatibility only during an actual still-open installer operation. A
            // closed historical mutation never replaces the fresh READ endpoint.
            var root = work.Sources.Invoke(_currentRoot);
            if (root is null || !work.Sources.Invoke(() => _device.IsOriginalRetainedRootMutation(root))) return null;
            var request = work.Sources.Invoke(() => root.OriginalRequest);
            if (request.Actor != actor || request.Artifact.AppId != _expectedApp) return null;
            view = work.Sources.Invoke(() => new ActivationView(root, request.Artifact, request.CatalogueRevision,
                request.OperationId, request.CopySignedDescriptor(), request.CopyDescriptorPayload()));
        }
        await ReadChannel(work, view.Original, token).ConfigureAwait(false);
        var stateRoot = CaptureRoot(work, Path.GetDirectoryName(_canonicalStateFile)!);
        var registryHandle = CaptureFile(work, stateRoot, _canonicalStateFile);
        var state = await Deserialize<HomeCoreStoredState>(work, registryHandle, 16 * 1024 * 1024, token).ConfigureAwait(false);
        if (!FileHomeCoreStateStore.IsOriginalProtectedStateValid(state)) throw new InvalidDataException("The actual protected Home state document is invalid.");
        var record = state.Records.SingleOrDefault(value => value.RecordId == HomePackageDatabase.RecordId);
        if (record is null) return null;
        if (record.RecordType != HomePackageDatabase.RecordType || record.Scope != HomeDataScope.DeviceLocal ||
            record.Authority != HomeRecordAuthority.LocalCanonical || record.SchemaVersion != HomePackageDatabase.CurrentSchemaVersion)
            throw new InvalidDataException("The protected actual package registry has an incompatible canonical record.");
        var registry = work.Sources.Invoke(() => record.Payload.Deserialize<HomePackageDatabaseSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            ?? throw new InvalidDataException("The protected canonical package registry is empty.");
        if (registry.SchemaVersion != HomePackageDatabase.CurrentSchemaVersion || registry.Revision != record.Revision || HomePackageDatabase.ValidateOriginalProtectedSnapshot(registry) is not null)
            throw new InvalidDataException("The protected canonical package registry is invalid or stale.");
        var package = registry.Packages.SingleOrDefault(value => value.PackageId == view.Artifact.PackageId);
        if (package is null || package.InstallationState != HomePackageInstallState.Installed || package.InstalledVersion != view.Artifact.Version ||
            package.AppId != _expectedApp || package.UnknownFields is null || !package.UnknownFields.TryGetValue(HomePackageOriginalInstalledActivationRecord.Field, out var element)) return null;
        var activation = work.Sources.Invoke(() => element.Deserialize<HomePackageOriginalInstalledActivationRecord>())
            ?? throw new InvalidDataException("The original activated layout is empty.");
        if (activation.SchemaVersion != 1 || activation.PackageId != package.PackageId || activation.AppId != _expectedApp ||
            activation.Version != package.InstalledVersion || activation.Platform != view.Artifact.Platform || activation.Abi != view.Artifact.Abi ||
            activation.CatalogueRevision != view.CatalogueRevision || activation.OriginalRootOperationId != view.OperationId ||
            !Convert.FromBase64String(activation.SignedDescriptorBase64).AsSpan().SequenceEqual(view.SignedDescriptor.Span) ||
            !Convert.FromBase64String(activation.DescriptorPayloadBase64).AsSpan().SequenceEqual(view.DescriptorPayload.Span))
            throw new UnauthorizedAccessException("The protected layout is not the exact SAME live root's verified signed artifact/catalogue/operation.");
        var files = work.Sources.Invoke(() => activation.ActivatedFiles.Take(4097).ToArray());
        if (files.Length is < 1 or > 4096 || files.Select(value => value.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length ||
            files.Any(value => !HomePackageOriginalInstalledActivationRecord.SafeRelative(value.RelativePath) || value.Bytes < 0 || value.Bytes > 8L * 1024 * 1024 * 1024 || value.Sha256.Length != 64 || value.Sha256.Any(c => !Uri.IsHexDigit(c))) ||
            !files.Any(value => value.RelativePath == activation.EntrypointRelativePath))
            throw new InvalidDataException("The protected actual activated cohort is invalid or incomplete.");
        var installationRoot = CaptureRoot(work, activation.InstallationRoot);
        await DemandDigest(work, CaptureFile(work, installationRoot, activation.SignedReceiptPath), view.Artifact.SignedInstallationReceiptSha256, null, token).ConfigureAwait(false);
        await DemandDigest(work, CaptureFile(work, installationRoot, activation.CompletePayloadPath), view.Artifact.PayloadSha256, view.Artifact.PayloadBytes, token).ConfigureAwait(false);
        foreach (var file in files)
        {
            var path = Path.Combine(activation.InstallationRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            await DemandDigest(work, CaptureFile(work, installationRoot, path), file.Sha256, file.Bytes, token).ConfigureAwait(false);
        }
        work.Sources.Invoke(() =>
        {
            stateRoot.DemandCurrent(); installationRoot.DemandCurrent(); WindowsOriginalFileCustody.DemandCurrentSid(principal);
            foreach (var held in work.HeldResources)
                if (held.Actual is SafeFileHandle handle && held.OriginalIdentity is { } expected &&
                    (!WindowsOriginalFileCustody.ReadIdentity(handle).SameReadVersion(expected) || DemandProtectedMachine(handle) != held.OriginalSecurity))
                    throw new UnauthorizedAccessException("Actual protected installation identity/ACL changed during complete observation.");
            return true;
        });
        await ReadChannel(work, view.Original, token).ConfigureAwait(false);
        return new(this, view.Original, view.Original is HomePackageOriginalReadActivation, actor, principal, registry.Revision, package, activation,
            Path.Combine(activation.InstallationRoot, activation.EntrypointRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
    private sealed record ActivationView(object Original, HomePackageArtifactDescriptor Artifact, string CatalogueRevision,
        string OperationId, ReadOnlyMemory<byte> SignedDescriptor, ReadOnlyMemory<byte> DescriptorPayload);
    private WindowsOriginalRoot CaptureRoot(Invocation work, string path)
        => work.Sources.Invoke(() =>
        {
            var root = WindowsOriginalFileCustody.RetainRoot(path); work.HeldResources.Add(new(root));
            var directory = root.OpenDirectory(path); var held = new Held(directory); work.HeldResources.Add(held);
            held.OriginalIdentity = WindowsOriginalFileCustody.ReadIdentity(directory); held.OriginalSecurity = DemandProtectedMachine(directory);
            return root;
        });
    private SafeFileHandle CaptureFile(Invocation work, WindowsOriginalRoot root, string path)
        => work.Sources.Invoke(() =>
        {
            // The retained root performs component-relative NT opens, rejects reparse,
            // case aliases, remote/cross-volume and hard links, and denies write/delete sharing.
            var actual = root.OpenRead(path); var held = new Held(actual); work.HeldResources.Add(held);
            held.OriginalIdentity = WindowsOriginalFileCustody.ReadIdentity(actual); held.OriginalSecurity = DemandProtectedMachine(actual); return actual;
        });
    internal static string DemandProtectedMachine(SafeFileHandle actual)
    {
        const string system = "010100000000000512000000";
        const string administrators = "01020000000000052000000020020000";
        var security = WindowsOriginalFileCustody.ReadSecurity(actual);
        if (security.OwnerSid != system && security.OwnerSid != administrators || security.Revision != 1 ||
            !security.DaclPresent || security.DaclIsNull || security.AclRevision is not (2 or 4) || security.Dacl.Count > 64)
            throw new UnauthorizedAccessException("Actual canonical machine installation owner/DACL is unsupported.");
        const uint mutations = 2 | 4 | 0x10 | 0x40 | 0x100 | 0x10000 | 0x40000 | 0x80000;
        foreach (var ace in security.Dacl)
            if (ace.Type is not (0 or 1) || ace.AccessMask is not { } mask || (mask & ~0x001f01ffu) != 0 ||
                ace.Sid is null || ace.Type == 0 && (mask & mutations) != 0 && ace.Sid != system && ace.Sid != administrators)
                throw new UnauthorizedAccessException("Actual protected installation permits a foreign writer or unsupported ACE.");
        return security.Fingerprint;
    }
    private async Task<T> Deserialize<T>(Invocation work, SafeFileHandle actual, int bound, CancellationToken token)
    {
        var identity = work.Sources.Invoke(() => WindowsOriginalFileCustody.ReadIdentity(actual));
        if (identity.Size == 0 || identity.Size > (ulong)bound) throw new InvalidDataException("Actual protected registry exceeds its bound.");
        var bytes = new byte[checked((int)identity.Size)]; var offset = 0;
        while (offset != bytes.Length)
        {
            var count = await Read(work, () => RandomAccess.ReadAsync(actual, bytes.AsMemory(offset), offset, token).AsTask()).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException(); offset += count;
        }
        if (!work.Sources.Invoke(() => WindowsOriginalFileCustody.ReadIdentity(actual).SameReadVersion(identity)))
            throw new UnauthorizedAccessException("Actual protected registry changed while read.");
        return work.Sources.Invoke(() => JsonSerializer.Deserialize<T>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            ?? throw new InvalidDataException("Actual protected registry contains no document.");
    }
    private async Task DemandDigest(Invocation work, SafeFileHandle actual, string expectedSha, long? expectedBytes, CancellationToken token)
    {
        var identity = work.Sources.Invoke(() => WindowsOriginalFileCustody.ReadIdentity(actual));
        if (identity.Size > 8UL * 1024 * 1024 * 1024 || expectedBytes is { } length && identity.Size != (ulong)length)
            throw new InvalidDataException("Actual complete installed artifact has an incompatible length.");
        var digest = work.Sources.Invoke(() =>
        {
            var actualDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); work.HeldResources.Add(new(actualDigest)); return actualDigest;
        });
        var buffer = new byte[65536]; long offset = 0;
        while ((ulong)offset != identity.Size)
        {
            var count = await Read(work, () => RandomAccess.ReadAsync(actual, buffer.AsMemory(0, (int)Math.Min(buffer.Length, (long)identity.Size - offset)), offset, token).AsTask()).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException(); digest.AppendData(buffer.AsSpan(0, count)); offset += count;
        }
        if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(digest.GetHashAndReset()), expectedSha) ||
            !work.Sources.Invoke(() => WindowsOriginalFileCustody.ReadIdentity(actual).SameReadVersion(identity)))
            throw new UnauthorizedAccessException("Actual activated bytes or physical source version disagree with the signed root artifact.");
    }
    private void Keep(Invocation work, Task raw) { work.Sources.Track(raw); work.Retain(raw); }
    private async Task<T> Read<T>(Invocation work, Func<Task<T>> factory, Action<T>? capture = null)
    {
        Task<T>? actual = null; Exception? invoke = null;
        try { work.Sources.Invoke(() => { actual = factory(); Keep(work, actual); return true; }); }
        catch (Exception cause) { invoke = cause; work.Sources.Retain(cause); }
        T result = default!; if (actual is not null) { result = await work.Sources.AwaitAsync(actual).ConfigureAwait(false); capture?.Invoke(result); }
        if (invoke is not null) throw invoke; return actual is null ? throw new InvalidOperationException("No actual source Task retained.") : result;
    }
    private async Task ReadChannel(Invocation work, object root, CancellationToken token)
    {
        Task? actual = null; Exception? invocation = null;
        try
        {
            work.Sources.Invoke(() =>
            {
                actual = root is HomePackageOriginalReadActivation read
                    ? read.DemandOriginalCurrentWithinSourceAsync(body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)
                    : _device.DemandOriginalActivationChannelWithinSourceAsync((IHomePackageOriginalRootMutation)root,
                        body => work.Sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token);
                Keep(work, actual); return true;
            });
        }
        catch (Exception cause) { invocation = cause; work.Sources.Retain(cause); }
        if (actual is not null) await work.Sources.AwaitAsync(actual).ConfigureAwait(false);
        if (invocation is not null) throw invocation;
        if (actual is null) throw new InvalidOperationException("No actual retained root-channel observation Task returned.");
    }
    private async Task CloseActualResources(Invocation work)
    {
        foreach (var held in work.HeldResources.AsEnumerable().Reverse())
        {
            if (held.OriginalClose is null)
            {
                var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                held.OriginalClose = result.Task; _ = work.Sources.Track(result.Task);
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { held.Actual.Dispose(); return true; }); result.SetResult(); }
                catch (Exception cause) { result.SetException(cause); }
            }
            try { await work.Sources.AwaitAsync(held.OriginalClose).ConfigureAwait(false); } catch (Exception cause) { work.Sources.Capture(held.OriginalClose, cause); }
        }
        if (work.ReadActivation is not null)
        {
            try
            {
                if (work.OriginalReadActivationClose is null)
                    CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                    {
                        work.OriginalReadActivationClose = work.ReadActivation.CloseAndDrainAsync();
                        work.Sources.Track(work.OriginalReadActivationClose); return true;
                    });
                await work.Sources.AwaitAsync(work.OriginalReadActivationClose!).ConfigureAwait(false);
            }
            catch (Exception cause) { work.Sources.Capture(work.OriginalReadActivationClose, cause); }
        }
    }
    public bool IsIssuedOriginalObservation(Observation same) => ReferenceEquals(same.Issuer, this) && _issued.TryGetValue(same, out _);
    public async Task DemandOriginalCurrentWithinSourceAsync(Observation same, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (!IsIssuedOriginalObservation(same)) throw new UnauthorizedAccessException("Actual privately issued activated package observation required.");
        var actual = await ObserveOriginalActivationWithinSourceAsync(same.Actor, scope, retain, token).ConfigureAwait(false);
        if (actual is null || actual.OriginalReadOnlyChannel != same.OriginalReadOnlyChannel ||
            !same.OriginalReadOnlyChannel && !ReferenceEquals(actual.OriginalRoot, same.OriginalRoot) || actual.RegistryRevision != same.RegistryRevision ||
            actual.CanonicalFingerprint != same.CanonicalFingerprint || actual.Package.Revision != same.Package.Revision || actual.Entrypoint != same.Entrypoint || actual.OsPrincipal != same.OsPrincipal)
            throw new UnauthorizedAccessException("The original installed activation changed; refresh before opening its dependency.");
    }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            if (_close is null) { _retiring = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = ClosePublished(start.Task, _accepted.ToArray()); }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    private static async Task ClosePublished(Task start, Invocation[] accepted)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        foreach (var work in accepted)
        {
            try { await work.Driver.ConfigureAwait(false); } catch (Exception cause) { failures.Add(work.Driver.Exception ?? cause); }
            if (!work.IsHealthy && work.Driver.IsCompletedSuccessfully) failures.Add(new InvalidOperationException("Actual package observation cleanup has no full healthy proof."));
        }
        if (failures.Count != 0) throw new AggregateException("Actual package observer failed; original sources/resources are retained.", failures);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
