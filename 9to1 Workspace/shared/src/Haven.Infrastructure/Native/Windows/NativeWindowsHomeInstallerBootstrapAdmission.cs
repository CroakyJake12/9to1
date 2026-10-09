using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>Cold bootstrap admission for the running single trusted installer.
/// Authenticode alone is insufficient: the actual signer must already be explicitly
/// enrolled in Windows LocalMachine TrustedPublisher, the exact executable must
/// contain its authenticated release catalogue, and the user must make an explicit
/// installation selection. No certificate, publisher or package is enrolled here.</summary>
public sealed partial class NativeWindowsHomeInstallerBootstrapAdmission(HomeLocalProfileIdentity actualProfiles) : IAsyncDisposable
{
    public const int SignedCatalogueResourceId = 41001;
    private readonly HomeLocalProfileIdentity _profiles = actualProfiles ?? throw new ArgumentNullException(nameof(actualProfiles));
    private readonly object _gate = new(); private readonly List<Invocation> _originals = [];
    private readonly ConditionalWeakTable<OriginalInstaller, Invocation> _issued = new();
    private readonly ConditionalWeakTable<OriginalInstallationChoice, Invocation> _choices = new();
    private bool _retiring; private Task? _close;
    internal sealed class Invocation(CloudflareOriginalTaskLedger source)
    {
        internal readonly CloudflareOriginalTaskLedger Source = source;
        internal Task Driver = null!; internal NativeWindowsHomeOriginalProcessRead? Process;
        internal Task? ProcessClose; internal readonly List<Resource> Resources = [];
        internal readonly List<X509Certificate2Collection> CertificateCollections = [];
        internal OriginalInstaller? Installer; internal OriginalInstallationChoice? Choice; internal string? UnavailableReason;
    }
    internal sealed class Resource(IDisposable original)
    { internal readonly IDisposable Original = original; internal Task? Close; }
    internal sealed record Catalogue(int SchemaVersion, string CatalogueRevision, string IssuerKeyId,
        string HomePackageId, string RootPackageId, IReadOnlyList<string> MandatoryPackageIds,
        IReadOnlyList<CataloguePackage> Packages);
    internal sealed record CataloguePackage(string SignedDescriptorBase64, string DescriptorPayloadBase64);
    /// <summary>Issuer-owned observation. Its strings/certificate are not a transferable bootstrap credential.</summary>
    public sealed class OriginalInstaller
    {
        internal readonly NativeWindowsHomeInstallerBootstrapAdmission Issuer;
        internal readonly Catalogue _catalogue;
        internal readonly byte[] OriginalSignedCatalogueBytes;
        internal readonly Invocation Original;
        internal readonly object NativeGate = new();
        internal readonly X509Certificate2 Signer;
        internal readonly IReadOnlyDictionary<string, HomePackageArtifactDescriptor> Descriptors;
        internal readonly IReadOnlyDictionary<string, HomePackageOriginalArtifactObservation> Artifacts;
        internal OriginalInstaller(NativeWindowsHomeInstallerBootstrapAdmission issuer, Invocation original,
            X509Certificate2 signer, Catalogue catalogue, IReadOnlyDictionary<string, HomePackageArtifactDescriptor> descriptors,
            IReadOnlyDictionary<string, HomePackageOriginalArtifactObservation> artifacts, AuthenticatedResourceActor actor,
            string catalogueSha256, byte[] actualSignedCatalogueBytes)
        { Issuer = issuer; Original = original; Signer = signer; _catalogue = catalogue; Descriptors = descriptors;
            Artifacts = artifacts; OriginalActor = actor; CatalogueSha256 = catalogueSha256;
            OriginalSignedCatalogueBytes = actualSignedCatalogueBytes.ToArray(); }
        public AuthenticatedResourceActor OriginalActor { get; }
        public string CatalogueRevision => _catalogue.CatalogueRevision;
        public string CatalogueSha256 { get; }
        public string InstallerExecutableIdentity => OperatingSystem.IsWindows() ? Original.Process!.ExecutableIdentity : throw new PlatformNotSupportedException("Windows installer evidence required.");
        public string PublisherCertificateSha256 => Convert.ToHexString(SHA256.HashData(Signer.RawData));
    }
    /// <summary>One source-issued explicit choice over the authenticated SINGLE installer catalogue.
    /// It grants no app permission, Home service access, mutation replay or platform elevation.</summary>
    public sealed class OriginalInstallationChoice
    {
        internal readonly NativeWindowsHomeInstallerBootstrapAdmission Issuer;
        internal readonly OriginalInstaller Installer;
        internal readonly AuthenticatedResourceActor Actor;
        internal OriginalInstallationChoice(NativeWindowsHomeInstallerBootstrapAdmission issuer,
            OriginalInstaller installer, AuthenticatedResourceActor actor, IReadOnlyList<string> apps,
            IReadOnlyList<string> packages)
        { Issuer = issuer; Installer = installer; Actor = actor; ChosenAppIds = apps; RequiredPackageIds = packages; OperationId = Guid.NewGuid(); }
        public Guid OperationId { get; }
        public IReadOnlyList<string> ChosenAppIds { get; }
        public IReadOnlyList<string> RequiredPackageIds { get; }
        public string OriginalCatalogueSha256 => Installer.CatalogueSha256;
    }
    public bool HasOriginalProfiles(HomeLocalProfileIdentity sameProfiles) => ReferenceEquals(_profiles, sameProfiles);
    /// <summary>Pure source-issued absence observation, never enrollment or readiness.
    /// Unknown source/cleanup failures cannot qualify as missing release material.</summary>
    public bool TryObserveOriginalUnavailableInspection(Task<OriginalInstaller?> sameInspection, out string? reason)
    {
        lock (_gate)
        {
            var actual = _originals.SingleOrDefault(value => ReferenceEquals(value.Driver, sameInspection));
            reason = actual?.UnavailableReason;
            return actual is not null && sameInspection.IsCompletedSuccessfully && sameInspection.Result is null &&
                reason is not null && actual.Source.OriginalErrors.Count == 0 &&
                actual.Source.OriginalTasks.All(raw => raw.IsCompletedSuccessfully) &&
                (actual.Process is null || actual.ProcessClose?.IsCompletedSuccessfully == true) &&
                actual.Resources.All(resource => resource.Close?.IsCompletedSuccessfully == true);
        }
    }

    public Task<OriginalInstaller?> InspectOriginalInstallerWithinSourceAsync(Action<Action> scope,
        Action<Task> retain, CancellationToken token)
    {
        var work = Admit(scope, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<OriginalInstaller?> actual;
        lock (_gate) { DemandAdmission(); actual = Inspect(work, start.Task, retain, token); work.Driver = actual; _originals.Add(work); }
        Publish(work, retain, start); return actual;
    }
    public bool IsIssuedOriginalInstaller(OriginalInstaller actual) => actual is not null &&
        ReferenceEquals(actual.Issuer, this) && _issued.TryGetValue(actual, out var original) &&
        original.Driver.IsCompletedSuccessfully && ReferenceEquals(original.Installer, actual) &&
        original.Source.OriginalErrors.Count == 0 && original.Source.OriginalTasks.All(value => value.IsCompletedSuccessfully) &&
        original.ProcessClose is null;
    public Task<OriginalInstallationChoice> SelectOriginalInstallationWithinSourceAsync(OriginalInstaller sameInstaller,
        IReadOnlyList<string> explicitChosenAppIds, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var work = Admit(scope, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<OriginalInstallationChoice> actual;
        lock (_gate) { DemandAdmission(); actual = Select(work, start.Task, sameInstaller, explicitChosenAppIds, retain, token); work.Driver = actual; _originals.Add(work); }
        Publish(work, retain, start); return actual;
    }
    public bool IsIssuedOriginalInstallationChoice(OriginalInstallationChoice actual) => actual is not null &&
        ReferenceEquals(actual.Issuer, this) && _choices.TryGetValue(actual, out var original) &&
        original.Driver.IsCompletedSuccessfully && ReferenceEquals(original.Choice, actual) &&
        original.Source.OriginalErrors.Count == 0 && original.Source.OriginalTasks.All(value => value.IsCompletedSuccessfully) &&
        IsIssuedOriginalInstaller(actual.Installer);
    public Task DemandOriginalChoiceCurrentWithinSourceAsync(OriginalInstallationChoice sameChoice,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var work = Admit(scope, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task actual;
        lock (_gate) { DemandAdmission(); actual = DemandCurrent(start.Task); work.Driver = actual; _originals.Add(work); }
        Publish(work, retain, start); return actual;
        async Task DemandCurrent(Task gate)
        {
            await gate.ConfigureAwait(false); using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try
            {
                if (!OperatingSystem.IsWindows() || !IsIssuedOriginalInstallationChoice(sameChoice))
                    throw new UnauthorizedAccessException("The SAME live explicit installer choice is required.");
                var actor = await Read(work, retain, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                    body => work.Source.Invoke(() => { body(); return true; }), raw => Keep(work, retain, raw), token)).ConfigureAwait(false);
                work.Source.Invoke(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows enrollment required.");
                    if (actor != sameChoice.Actor) throw new UnauthorizedAccessException("The original installer profile changed.");
                    VerifyWindowsSignature(work, sameChoice.Installer.Original.Process!.ExecutablePath);
                    if (!IsExplicitlyEnrolledPublisher(work, sameChoice.Installer.Signer))
                        throw new UnauthorizedAccessException("The actual publisher enrollment was removed or changed.");
                    lock (sameChoice.Installer.NativeGate) sameChoice.Installer.Original.Process!.DemandCurrent(); return true;
                });
                if (actor != await Read(work, retain, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                    body => work.Source.Invoke(() => { body(); return true; }), raw => Keep(work, retain, raw), token)).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The current installer actor changed during its independent validation.");
            }
            catch (Exception cause) { work.Source.Retain(cause); }
            await work.Source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (work.Source.OriginalErrors.Count != 0) throw new AggregateException("The original installer enrollment/choice is no longer current.", work.Source.OriginalErrors);
        }
    }
    private void DemandAdmission()
    {
        ObjectDisposedException.ThrowIf(_retiring, this);
        PruneOriginalHealthyFiniteSources();
        if (_originals.Count >= 128) throw new InvalidOperationException("All actual unresolved installer originals remain retained.");
    }
    private Invocation Admit(Action<Action> scope, Action<Task> retain)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            PruneOriginalHealthyFiniteSources();
            if (_originals.Count >= 128) throw new InvalidOperationException("All original installer choices/sources must remain retained until retirement.");
        }
        var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this);
        source.BindOriginalCallerCallback(body => Within(source, scope, body)); return new(source);
    }
    private void Publish(Invocation original, Action<Task> retain, TaskCompletionSource start)
    {
        try { original.Source.Invoke(() => { retain(original.Driver); return true; }); }
        catch (Exception cause) { original.Source.Retain(cause); }
        finally { start.SetResult(); }
    }
    private async Task<OriginalInstaller?> Inspect(Invocation work, Task start, Action<Task> retain, CancellationToken token, bool originalInstallerEntry = true, HomeNativeObservedPeer? actualConnectedRootPeer = null,
        NativeWindowsHomeInstalledRootAdmission.OriginalProtectedEnrollmentCatalogue? actualProtectedCatalogue = null)
    {
        await start.ConfigureAwait(false); using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        OriginalInstaller? result = null;
        try
        {
            if (OperatingSystem.IsWindows()) result = await ReadWindows().ConfigureAwait(false);
            else work.UnavailableReason = "WindowsInstallerPlatformRequired";
        }
        catch (Exception cause) { work.Source.Retain(cause); }
        await work.Source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (result is null || work.Source.OriginalErrors.Count != 0) await CloseResources(work).ConfigureAwait(false);
        if (work.Source.OriginalErrors.Count != 0) throw new AggregateException("Actual installer authentication failed; original evidence is retained.", work.Source.OriginalErrors);
        if (result is not null && originalInstallerEntry) _issued.Add(result, work); return result;

        [SupportedOSPlatform("windows")]
        async Task<OriginalInstaller?> ReadWindows()
        {
            var actor = await Read(work, retain, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                body => work.Source.Invoke(() => { body(); return true; }), raw => Keep(work, retain, raw), token)).ConfigureAwait(false);
            if (actor is null || actor.OrganisationId is not null) { work.UnavailableReason = "ActualLocalInstallerPrincipalRequired"; return null; }
            var principal = work.Source.Invoke(WindowsOriginalFileCustody.CurrentSid);
            work.Source.Invoke(() => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows installer process required."); work.Process = new(this); return true; });
            if (actualConnectedRootPeer is not null && originalInstallerEntry) throw new UnauthorizedAccessException("A connected Root peer cannot issue an installer choice.");
            if (!work.Source.Invoke(() => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows installer kernel identity required."); return work.Process!.OpenOriginal(actualConnectedRootPeer ?? new(Environment.ProcessId, "windows-sid:" + principal), work.Source); })) { work.UnavailableReason = "ActualInstallerKernelIdentityRequired"; return null; }
            await Read(work, retain, async () => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows installer image required."); await work.Process!.ReadOriginalImageAsync(work.Source,
                raw => Keep(work, retain, raw), token).ConfigureAwait(false); return true; }).ConfigureAwait(false);
            OriginalTrust? verifiedTrust = null;
            work.Source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows installer signature required.");
                verifiedTrust = VerifyWindowsSignature(work, work.Process!.ExecutablePath); return true;
            });
            var signer = work.Source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows verified signer required.");
                return ReadOriginalVerifiedSigner(work, verifiedTrust ?? throw new UnauthorizedAccessException("The SAME actual verified trust state is required."));
            });
            if (!work.Source.Invoke(() => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows explicit publisher enrollment required."); return IsExplicitlyEnrolledPublisher(work, signer); })) { work.UnavailableReason = "ExplicitWindowsTrustedPublisherEnrollmentRequired"; return null; }
            byte[] bytes;
            if (actualProtectedCatalogue is null)
            {
                var loadedResource = work.Source.Invoke(() => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows signed catalogue resource required."); return actualConnectedRootPeer is null ? ReadLoadedCatalogue() : ReadOriginalRemoteCatalogue(work, work.Process!.ExecutablePath); });
                if (loadedResource is null) { work.UnavailableReason = "AuthenticSignedSingleInstallerCatalogueRequired"; return null; }
                bytes = await Read(work, retain, () => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows pinned resource required."); return ReadPinnedResource(work, loadedResource.Value, retain, token); }).ConfigureAwait(false);
            }
            else bytes = work.Source.Invoke(() =>
            {
                if (originalInstallerEntry) throw new UnauthorizedAccessException("Retained catalogue observations cannot issue initial installer enrollment.");
                return actualProtectedCatalogue.Owner.ReadOriginalProtectedCatalogueBytes(actualProtectedCatalogue, this,
                    Convert.ToHexString(SHA256.HashData(signer.RawData)));
            }); // Exact source-issued protected transfer, never caller bytes/key metadata.
            var catalogue = work.Source.Invoke(() => JsonSerializer.Deserialize<Catalogue>(bytes,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 16,
                    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
                ?? throw new InvalidDataException("The authenticated release catalogue is empty."));
            Dictionary<string, HomePackageArtifactDescriptor> descriptors = new(StringComparer.Ordinal);
            Dictionary<string, HomePackageOriginalArtifactObservation> artifacts = new(StringComparer.Ordinal);
            work.Source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows signed package catalogue required.");
                var packages = catalogue.Packages.Take(129).ToArray(); var mandatory = catalogue.MandatoryPackageIds.Take(129).ToArray();
                if (catalogue.SchemaVersion != 1 || packages.Length is < 2 or > 128 || mandatory.Length is < 2 or > 128 ||
                    string.IsNullOrWhiteSpace(catalogue.CatalogueRevision) || catalogue.CatalogueRevision.Length > 1024 ||
                    !HomePackageOriginalInstalledActivationRecord.SafeRelative(catalogue.HomePackageId) ||
                    !HomePackageOriginalInstalledActivationRecord.SafeRelative(catalogue.RootPackageId))
                    throw new InvalidDataException("The actual single-installer catalogue is unsupported or unbounded.");
                var publicKey = signer.GetRSAPublicKey();
                if (publicKey is not null) work.Resources.Add(new(publicKey));
                if (publicKey is null || publicKey.KeySize < 3072) throw new InvalidDataException("The enrolled publisher's supported RSA-PSS key is unavailable.");
                foreach (var package in packages)
                {
                    var signed = Convert.FromBase64String(package.SignedDescriptorBase64); var payload = Convert.FromBase64String(package.DescriptorPayloadBase64);
                    if (signed.Length is < 1 or > 1024 * 1024 || payload.Length is < 1 or > 65536) throw new InvalidDataException("The signed package descriptor exceeds its bound.");
                    using var envelope = JsonDocument.Parse(signed, new JsonDocumentOptions { MaxDepth = 8 });
                    var root = envelope.RootElement;
                    if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("issuerKeyId").GetString() != catalogue.IssuerKeyId ||
                        !Convert.FromBase64String(root.GetProperty("payload").GetString()!).AsSpan().SequenceEqual(payload) ||
                        !publicKey.VerifyData(payload, Convert.FromBase64String(root.GetProperty("signature").GetString()!), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                        throw new UnauthorizedAccessException("The exact descriptor is not signed by the actual explicitly enrolled installer publisher.");
                    var basic = JsonSerializer.Deserialize<HomePackageArtifactDescriptor>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                        ?? throw new InvalidDataException("The authenticated package descriptor is empty.");
                    var request = new HomePackageActionRequest(basic.PackageId, HomePackageAction.Install, "bootstrap.descriptor.validation",
                        basic.Version, basic.Channel, catalogue.CatalogueRevision);
                    var descriptor = HomePackageOriginalArtifactDescriptorParser.Parse(signed, payload, catalogue.CatalogueRevision, request).Descriptor;
                    if (descriptor.Platform != "windows" || !descriptors.TryAdd(descriptor.PackageId, descriptor))
                        throw new InvalidDataException("The exact package tuple is invalid or duplicated.");
                    artifacts.Add(descriptor.PackageId, new(signed, payload, catalogue.CatalogueRevision, work));
                }
                if (!descriptors.TryGetValue(catalogue.HomePackageId, out var home) || home.AppId != "home" ||
                    !descriptors.TryGetValue(catalogue.RootPackageId, out var rootPackage) || rootPackage.AppId != "root" ||
                    !mandatory.Contains(catalogue.HomePackageId) || !mandatory.Contains(catalogue.RootPackageId) ||
                    mandatory.Distinct(StringComparer.Ordinal).Count() != mandatory.Length || mandatory.Any(id => !descriptors.ContainsKey(id)))
                    throw new InvalidDataException("The SINGLE installer must include genuine Home, Root and all mandatory package selections.");
                work.Process!.DemandCurrent(); return true;
            });
            if (actor != await Read(work, retain, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                body => work.Source.Invoke(() => { body(); return true; }), raw => Keep(work, retain, raw), token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual installer profile retired during authentication.");
            result = work.Source.Invoke(() => new OriginalInstaller(this, work, signer, catalogue, descriptors, artifacts, actor,
                Convert.ToHexString(SHA256.HashData(bytes)), bytes)); work.Installer = result; return result;
        }
    }
    private async Task<OriginalInstallationChoice> Select(Invocation work, Task start, OriginalInstaller installer,
        IReadOnlyList<string> explicitApps, Action<Task> retain, CancellationToken token)
    {
        await start.ConfigureAwait(false); using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try
        {
            if (!OperatingSystem.IsWindows() || !IsIssuedOriginalInstaller(installer)) throw new UnauthorizedAccessException("The SAME original authenticated installer is required.");
            var actor = await Read(work, retain, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                body => work.Source.Invoke(() => { body(); return true; }), raw => Keep(work, retain, raw), token)).ConfigureAwait(false);
            work.Choice = work.Source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows installer choice required.");
                VerifyWindowsSignature(work, installer.Original.Process!.ExecutablePath);
                if (actor != installer.OriginalActor || !IsExplicitlyEnrolledPublisher(work, installer.Signer))
                    throw new UnauthorizedAccessException("The actual installer actor or publisher enrollment changed.");
                lock (installer.NativeGate) installer.Original.Process!.DemandCurrent();
                var apps = explicitApps.Take(129).ToArray();
                if (apps.Length is < 1 or > 128 || apps.Any(string.IsNullOrWhiteSpace) || apps.Distinct(StringComparer.Ordinal).Count() != apps.Length)
                    throw new ArgumentException("An explicit bounded app choice is required.");
                var packages = new HashSet<string>(installer._catalogue.MandatoryPackageIds, StringComparer.Ordinal);
                foreach (var mandatory in installer._catalogue.MandatoryPackageIds) Add(mandatory, []);
                foreach (var app in apps)
                {
                    var matches = installer.Descriptors.Values.Where(value => value.AppId == app).Take(2).ToArray();
                    if (matches.Length != 1) throw new InvalidDataException("The chosen app has no unique authenticated catalogue package.");
                    Add(matches[0].PackageId, []);
                }
                return new OriginalInstallationChoice(this, installer, actor!, Array.AsReadOnly(apps), Array.AsReadOnly(packages.Order(StringComparer.Ordinal).ToArray()));
                void Add(string id, HashSet<string> ancestors)
                {
                    if (packages.Count > 128 || !installer.Descriptors.TryGetValue(id, out var descriptor) || !ancestors.Add(id))
                        throw new InvalidDataException("The authenticated dependency closure is missing, cyclic or unbounded.");
                    packages.Add(id);
                    var dependencies = descriptor.Dependencies.Take(129).ToArray();
                    if (dependencies.Length > 128) throw new InvalidDataException("The authenticated dependency collection exceeds its bound.");
                    foreach (var dependency in dependencies)
                    {
                        if (!installer.Descriptors.TryGetValue(dependency.PackageId, out var target) ||
                            !Version.TryParse(target.Version, out var current) ||
                            dependency.MinimumVersion is { } minimum && (!Version.TryParse(minimum, out var min) || current < min) ||
                            dependency.MaximumVersionExclusive is { } maximum && (!Version.TryParse(maximum, out var max) || current >= max))
                            throw new InvalidDataException("The genuine catalogue dependency has no supported compatible version.");
                        Add(dependency.PackageId, new(ancestors, StringComparer.Ordinal));
                    }
                }
            });
            if (actor != await Read(work, retain, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                body => work.Source.Invoke(() => { body(); return true; }), raw => Keep(work, retain, raw), token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual installer actor changed before choice publication.");
        }
        catch (Exception cause) { work.Source.Retain(cause); }
        await work.Source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (work.Source.OriginalErrors.Count != 0) throw new AggregateException("Original explicit installer choice was refused or unknown.", work.Source.OriginalErrors);
        _choices.Add(work.Choice!, work); return work.Choice!;
    }
    [SupportedOSPlatform("windows")]
    private static bool IsExplicitlyEnrolledPublisher(Invocation work, X509Certificate2 signer)
    {
        var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.LocalMachine); work.Resources.Add(new(store));
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var certificates = store.Certificates;
        // Capture the original collection before enumeration/bounds so every
        // native certificate wrapper remains rooted even on partial failure.
        work.CertificateCollections.Add(certificates);
        var matched = false;
        foreach (var certificate in certificates)
        {
            work.Resources.Add(new(certificate));
            if (certificates.Count > 1024) throw new InvalidDataException("The actual enrolled publisher store exceeds its bound.");
            if (certificate.RawData.AsSpan().SequenceEqual(signer.RawData)) matched = true;
        }
        return matched;
    }
    [SupportedOSPlatform("windows")]
    private static (uint Rva, byte[] Bytes)? ReadLoadedCatalogue()
    {
        var module = GetModuleHandle(null); if (module == IntPtr.Zero) throw Native("GetModuleHandleW");
        var resource = FindResource(module, new(SignedCatalogueResourceId), new(10));
        if (resource == IntPtr.Zero)
        { var error = Marshal.GetLastWin32Error(); return error is 1812 or 1813 or 1814 or 1815 ? null : throw new Win32Exception(error, "Actual signed release catalogue unavailable."); }
        var length = SizeofResource(module, resource);
        if (length is 0 or > 16 * 1024 * 1024) throw new InvalidDataException("The actual embedded release catalogue exceeds its bound.");
        var loaded = LoadResource(module, resource); var pointer = LockResource(loaded);
        if (loaded == IntPtr.Zero || pointer == IntPtr.Zero) throw Native("LoadResource/LockResource");
        var offset = pointer.ToInt64() - module.ToInt64();
        if (offset < 0 || offset > uint.MaxValue) throw new InvalidDataException("The actual resource is outside its original image.");
        var bytes = new byte[checked((int)length)]; Marshal.Copy(pointer, bytes, 0, checked((int)length)); return ((uint)offset, bytes);
    }
    [SupportedOSPlatform("windows")]
    private static async Task<byte[]> ReadPinnedResource(Invocation work, (uint Rva, byte[] Bytes) resource,
        Action<Task> retain, CancellationToken token)
    {
        var dos = await Read(work, retain, () => work.Process!.ReadOriginalBytesAsync(0, 64, work.Source, raw => Keep(work, retain, raw), token)).ConfigureAwait(false);
        var headerOffset = BinaryPrimitives.ReadInt32LittleEndian(dos.AsSpan(60));
        if (dos[0] != 'M' || dos[1] != 'Z' || headerOffset is < 64 or > 1024 * 1024) throw new InvalidDataException("The original signed executable header is unsupported.");
        var header = await Read(work, retain, () => work.Process!.ReadOriginalBytesAsync(headerOffset, 24, work.Source, raw => Keep(work, retain, raw), token)).ConfigureAwait(false);
        var sections = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6)); var optional = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(20));
        if (!header.AsSpan(0, 4).SequenceEqual(new byte[] { 80, 69, 0, 0 }) || sections is < 1 or > 96 || optional is < 96 or > 4096)
            throw new InvalidDataException("The original signed executable section table is unsupported.");
        var table = await Read(work, retain, () => work.Process!.ReadOriginalBytesAsync(headerOffset + 24L + optional, sections * 40,
            work.Source, raw => Keep(work, retain, raw), token)).ConfigureAwait(false);
        long? offset = null;
        for (var i = 0; i < sections; i++)
        {
            var row = table.AsSpan(i * 40, 40); var rva = BinaryPrimitives.ReadUInt32LittleEndian(row[12..]);
            var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(row[16..]); var rawOffset = BinaryPrimitives.ReadUInt32LittleEndian(row[20..]);
            if (resource.Rva >= rva && (ulong)resource.Rva - rva + (ulong)resource.Bytes.Length <= rawSize)
            { if (offset is not null) throw new InvalidDataException("The original resource section is ambiguous."); offset = rawOffset + (long)resource.Rva - rva; }
        }
        if (offset is null) throw new InvalidDataException("The actual loaded catalogue has no exact signed-file resource range.");
        var bytes = await Read(work, retain, () => work.Process!.ReadOriginalBytesAsync(offset.Value, resource.Bytes.Length, work.Source,
            raw => Keep(work, retain, raw), token)).ConfigureAwait(false);
        if (!bytes.AsSpan().SequenceEqual(resource.Bytes)) throw new UnauthorizedAccessException("The loaded release resource differs from the actual signed pinned image.");
        return bytes;
    }
    [SupportedOSPlatform("windows")]
    private static OriginalTrust VerifyWindowsSignature(Invocation work, string image)
    {
        var trust = new OriginalTrust(image); work.Resources.Add(new(trust));
        var action = new Guid("00aac56b-cd44-11d0-8cc2-00c04fc295ee");
        var status = WinVerifyTrust(new(-1), ref action, ref trust.Data);
        if (status != 0) throw new Win32Exception(status, "The actual installer signature/revocation chain is not currently trusted.");
        trust.OriginalVerified = true; return trust;
    }
    [SupportedOSPlatform("windows")]
    private static X509Certificate2 ReadOriginalVerifiedSigner(Invocation work, OriginalTrust sameTrust)
    {
        if (!sameTrust.OriginalVerified || sameTrust.Data.State == IntPtr.Zero ||
            !work.Resources.Any(resource => ReferenceEquals(resource.Original, sameTrust) && resource.Close is null))
            throw new UnauthorizedAccessException("The SAME successful still-retained actual trust-provider state is required.");
        var provider = WTHelperProvDataFromStateData(sameTrust.Data.State);
        if (provider == IntPtr.Zero) throw new InvalidDataException("The actual trust provider omitted its verified signer state.");
        var signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
        if (signer == IntPtr.Zero) throw new InvalidDataException("The actual verified PE signer is unavailable.");
        var certificate = WTHelperGetProvCertFromChain(signer, 0);
        if (certificate == IntPtr.Zero) throw new InvalidDataException("The actual verified signer certificate is unavailable.");
        var observed = Marshal.PtrToStructure<OriginalProviderCertificate>(certificate);
        if (observed.Size < (uint)Marshal.SizeOf<OriginalProviderCertificate>() || observed.Certificate == IntPtr.Zero)
            throw new InvalidDataException("The actual verified certificate context is unsupported.");
        var context = Marshal.PtrToStructure<OriginalCertificateContext>(observed.Certificate);
        if (context.Encoded == IntPtr.Zero || context.Bytes is < 1 or > 1024 * 1024)
            throw new InvalidDataException("The actual verified signer DER exceeds its supported bound.");
        var bytes = new byte[checked((int)context.Bytes)]; Marshal.Copy(context.Encoded, bytes, 0, bytes.Length);
        var loaded = X509CertificateLoader.LoadCertificate(bytes); work.Resources.Add(new(loaded)); return loaded;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct OriginalProviderCertificate { public uint Size; public IntPtr Certificate; }
    [StructLayout(LayoutKind.Sequential)]
    private struct OriginalCertificateContext { public uint Encoding; public IntPtr Encoded; public uint Bytes; }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll")] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll")] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll")] private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificate);

    private sealed class OriginalTrust : IDisposable
    {
        private readonly IntPtr _path, _file;
        internal TrustData Data;
        internal bool OriginalVerified;
        internal OriginalTrust(string path)
        {
            _path = Marshal.StringToHGlobalUni(path);
            try { _file = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>()); Marshal.StructureToPtr(new TrustFile {
                Size = (uint)Marshal.SizeOf<TrustFile>(), Path = _path }, _file, false);
                Data = new() { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, RevocationChecks = 1,
                    UnionChoice = 1, File = _file, StateAction = 1, ProviderFlags = 0x1080 }; }
            catch { Marshal.FreeHGlobal(_path); if (_file != IntPtr.Zero) Marshal.FreeHGlobal(_file); throw; }
        }
        public void Dispose()
        {
            if (Data.State != IntPtr.Zero)
            { var action = new Guid("00aac56b-cd44-11d0-8cc2-00c04fc295ee"); Data.StateAction = 2;
                var status = WinVerifyTrust(new(-1), ref action, ref Data); if (status != 0) throw new Win32Exception(status, "Actual trust-provider close was not acknowledged."); }
            Marshal.FreeHGlobal(_file); Marshal.FreeHGlobal(_path);
        }
    }
    private void Within(CloudflareOriginalTaskLedger source, Action<Action> caller, Action body)
    {
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
            try { caller(() => { try {
                if (active != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                    throw new InvalidOperationException("The actual installer source callback is inactive, foreign-thread or consumed.");
                if (source.OriginalErrors.Count != 0) throw new AggregateException("Prior actual installer source failed.", source.OriginalErrors);
                body(); } catch (Exception cause) { source.Retain(cause); throw; } });
                if (used != 1) throw new InvalidOperationException("The actual installer callback was omitted."); }
            finally { Interlocked.Exchange(ref active, 0); } return true;
        });
    }
    private static void Keep(Invocation work, Action<Task> retain, Task actual) { _ = work.Source.Track(actual); retain(actual); }
    private static async Task<T> Read<T>(Invocation work, Action<Task> retain, Func<Task<T>> acquire)
    {
        Task<T>? raw = null; Exception? publication = null;
        try { work.Source.Invoke(() => { raw = acquire(); Keep(work, retain, raw); return true; }); }
        catch (Exception cause) { publication = cause; work.Source.Retain(cause); }
        T result = default!; if (raw is not null) result = await work.Source.AwaitAsync(raw).ConfigureAwait(false);
        if (publication is not null) throw publication;
        return raw is null ? throw new InvalidOperationException("The actual installer source returned no Task.") : result;
    }
    private async Task CloseResources(Invocation original)
    {
        if (OperatingSystem.IsWindows() && original.Process is { } process)
        {
            try { original.ProcessClose ??= CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows process close required."); return process.CloseAndDrainOriginalAsync(original.Source); });
                _ = original.Source.Track(original.ProcessClose); await original.Source.AwaitAsync(original.ProcessClose).ConfigureAwait(false); }
            catch (Exception cause) { original.Source.Retain(cause); }
        }
        // A failed certificate enumeration still retains its complete original
        // collection. Capture any not-yet-enrolled wrappers before cleanup.
        foreach (var certificates in original.CertificateCollections)
            try { foreach (var certificate in certificates)
                if (!original.Resources.Any(value => ReferenceEquals(value.Original, certificate))) original.Resources.Add(new(certificate)); }
            catch (Exception cause) { original.Source.Retain(cause); }
        foreach (var resource in original.Resources.AsEnumerable().Reverse())
        {
            if (resource.Close is null)
            { var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); resource.Close = Dispose(start.Task, resource.Original);
                _ = original.Source.Track(resource.Close); start.SetResult(); }
            try { await original.Source.AwaitAsync(resource.Close).ConfigureAwait(false); }
            catch (Exception cause) { original.Source.Capture(resource.Close, cause); }
        }
        async Task Dispose(Task start, IDisposable actual)
        { await start.ConfigureAwait(false); CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { actual.Dispose(); return true; }); }
    }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_gate) { if (_close is null) { _retiring = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(start.Task, _originals.ToArray()); } actual = _close; }
        start?.SetResult(); return actual;
    }
    private async Task Close(Task start, Invocation[] originals)
    {
        await start.ConfigureAwait(false); using var owner = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var errors = new List<Exception>();
        foreach (var actual in originals)
        { try { await actual.Driver.ConfigureAwait(false); } catch (Exception cause) { errors.Add(actual.Driver.Exception ?? cause); }
            await actual.Source.ObserveAllOriginalTasksAsync().ConfigureAwait(false); await CloseResources(actual).ConfigureAwait(false);
            errors.AddRange(actual.Source.OriginalErrors); }
        if (errors.Count != 0) throw new AggregateException("Actual installer original source/resource retirement failed; evidence remains rooted.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private static Win32Exception Native(string operation) => new(Marshal.GetLastWin32Error(), operation + " did not return known original evidence.");
    [StructLayout(LayoutKind.Sequential)] private struct TrustFile { internal uint Size; internal IntPtr Path, File, Subject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData
    { internal uint Size; internal IntPtr Policy, Sip; internal uint UiChoice, RevocationChecks, UnionChoice;
        internal IntPtr File; internal uint StateAction; internal IntPtr State, Url; internal uint ProviderFlags, Context; internal IntPtr Signature; }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetModuleHandle(string? name);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "FindResourceW", SetLastError = true)] private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
}
