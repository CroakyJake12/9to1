using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using HavenOS.Home.Apps;

namespace HavenOS.Home.Core;

/// <summary>Explicit trusted host configuration. Values are installation inputs, never
/// request arguments or a guessed release identity. No default publisher or root exists.</summary>
public sealed record HomeNativeWindowsProtectedPeerConfiguration(
    string PackageId, string AppId, string OsApplicationId, string Platform, string Abi,
    string ProtectedRoot, string DescriptorRelativePath, string ReceiptRelativePath,
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> DescriptorIssuerSubjectPublicKeys,
    IReadOnlySet<string> PublisherCertificateSha256,
    IReadOnlySet<string> ProtectedWriterSids, IReadOnlySet<string> PermittedRoles);

/// <summary>Read-only installed admission over SAME canonical device registry, original
/// profile/lease, physical Windows process and protected publisher/receipt evidence.
/// It adds no listener, registry, install action or ordinary authoring fallback.</summary>
public sealed class HomeNativeWindowsProtectedInstalledPeerVerifier :
    IHomeNativeInstalledPeerOriginalActorVerifier, IHomeNativeSessionHostVerifier
{
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly ITrustedHostPrincipalSource _principals;
    private readonly HomeNativeSessionLease _lease;
    private readonly Guid _originalLease;
    private readonly HomePackageDatabase _packages;
    private readonly IHomeNativeControlledLaunchAuthority _launches;
    private readonly CapturedConfiguration _configuration;
    private readonly IHomeNativeWindowsProtectedInstallationReceiptAuthority _receipts;

    public HomeNativeWindowsProtectedInstalledPeerVerifier(
        HomeLocalProfileIdentity originalProfiles, ITrustedHostPrincipalSource originalPrincipals,
        HomeNativeSessionLease originalLease, HomePackageDatabase originalPackages,
        IHomeCoreStateStore originalDeviceStore, IHomeNativeControlledLaunchAuthority originalLaunches,
        HomeNativeWindowsProtectedPeerConfiguration configuredTrust,
        IHomeNativeWindowsProtectedInstallationReceiptAuthority? originalReceipts = null)
    {
        _profiles = originalProfiles ?? throw new ArgumentNullException(nameof(originalProfiles));
        _principals = originalPrincipals ?? throw new ArgumentNullException(nameof(originalPrincipals));
        _lease = originalLease ?? throw new ArgumentNullException(nameof(originalLease));
        _packages = originalPackages ?? throw new ArgumentNullException(nameof(originalPackages));
        _launches = originalLaunches ?? throw new ArgumentNullException(nameof(originalLaunches));
        ArgumentNullException.ThrowIfNull(originalDeviceStore);
        if (!_packages.IsBoundToStore(originalDeviceStore) || !_lease.IsHeld)
            throw new UnauthorizedAccessException("The original canonical device or held Home lease differs.");
        _originalLease = _lease.LeaseIdentity;
        _configuration = CapturedConfiguration.Capture(configuredTrust);
        _receipts = originalReceipts ?? new UnavailableHomeNativeWindowsProtectedInstallationReceiptAuthority();
    }

    public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observedPeer,
        CancellationToken cancellationToken) => VerifyCoreAsync(Capture(observedPeer), null, null, cancellationToken);

    public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observedPeer,
        AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        return VerifyCoreAsync(Capture(observedPeer), expectedActor with { }, null, cancellationToken);
    }

    public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observedPeer,
        HomeNativeSessionHostRequirement trustedRequirement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedRequirement);
        return VerifyCoreAsync(Capture(observedPeer), null, trustedRequirement with { }, cancellationToken);
    }

    private async ValueTask<HomeNativeInstalledPeer?> VerifyCoreAsync(HomeNativeObservedPeer observed,
        AuthenticatedResourceActor? expectedActor, HomeNativeSessionHostRequirement? host, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() ||
            _receipts is UnavailableHomeNativeWindowsProtectedInstallationReceiptAuthority) return null;
        if (host is not null && (host.AppId != _configuration.Package.AppId ||
            host.OperatingSystemApplicationId != _configuration.Package.OsApplicationId)) return null;
        HomeNativeWindowsProtectedPeerEvidence? original = null;
        return await HomeNativeWindowsOriginalEvidenceLifetime.VerifyAsync(
            ReadOriginalAsync, () => original?.Dispose()).ConfigureAwait(false);

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        async ValueTask<HomeNativeInstalledPeer?> ReadOriginalAsync()
        {
            var actor = await _profiles.GetCurrentAsync(ct).ConfigureAwait(false);
            if (actor is null || expectedActor is not null && actor != expectedActor ||
                actor.AccountId is not null || actor.OrganisationId is not null ||
                actor.ProfileId != _lease.ProfileId) return null;
            await RequireActorAsync(actor, ct).ConfigureAwait(false);
            var principal = await _principals.GetPrincipalAsync(ct).ConfigureAwait(false);
            await RequireActorAsync(actor, ct).ConfigureAwait(false);
            if (principal != observed.OperatingSystemPrincipalId) return null;

            original = HomeNativeWindowsProtectedPeerEvidence.Open(observed.ProcessId,
                observed.OperatingSystemPrincipalId, _configuration.Writers);
            OpenOriginalAncestors(original, _configuration.Root);
            var descriptorFile = OpenOriginalRelative(original, _configuration.Descriptor);
            var receiptFile = OpenOriginalRelative(original, _configuration.Receipt);
            var descriptorBytes = original.ReadBounded(descriptorFile, 1024 * 1024);
            var receiptBytes = original.ReadBounded(receiptFile, 2 * 1024 * 1024);
            var descriptor = DecodeSigned<HomePackageArtifactDescriptor>(descriptorBytes, 64 * 1024);
            var receipt = DecodeSigned<InstallationReceipt>(receiptBytes, 1024 * 1024);
            RequireDeclarations(descriptor, receipt, receiptBytes);
            if (host is not null && !receipt.Roles.Contains(HomeNativeSessionHostRequirement.RequiredRole,
                    StringComparer.Ordinal)) return null;

            var registry = await _packages.ReadAsync(ct).ConfigureAwait(false);
            await RequireActorAsync(actor, ct).ConfigureAwait(false);
            if (!registry.Succeeded) return null;
            var before = RequireRegistryEntry(registry.Snapshot!, descriptor, receipt);
            var entryBytes = JsonSerializer.SerializeToUtf8Bytes(before);
            var installedObservation = new HomeNativeInstalledPeer(descriptor.AppId,
                receipt.InstalledApplicationId, receipt.InstallationRevision, "",
                receipt.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal))
                { Roles = receipt.Roles.ToFrozenSet(StringComparer.Ordinal) };

            var files = CaptureFiles(receipt.Files);
            RequireExactFiles(original, files);
            var imageRelative = RelativeImage(original.ImagePath);
            if (imageRelative != receipt.ImageRelativePath ||
                !files.Any(file => file.RelativePath == imageRelative)) return null;
            string? imageDigest = null;
            string? publisher = null;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var opened = OpenOriginalRelative(original, file.RelativePath);
                var digest = original.HashOriginal(opened, file.Bytes);
                if (!StringComparer.OrdinalIgnoreCase.Equals(digest, file.Sha256))
                    throw new UnauthorizedAccessException("Original protected payload content differs.");
                if (file.RelativePath == imageRelative)
                {
                    imageDigest = digest;
                    publisher = original.VerifyOriginalAuthenticode(opened, _configuration.Publishers);
                }
            }
            if (imageDigest is null || publisher is null ||
                !StringComparer.OrdinalIgnoreCase.Equals(publisher, receipt.PublisherCertificateSha256)) return null;
            original.RequireSame();
            // The proposed codec is accepted only when the genuine Windows installer
            // owner independently authenticates this exact format/current generation.
            var receiptObservation = new HomeNativeWindowsProtectedInstallationReceiptObservation(
                descriptorBytes, receiptBytes, entryBytes, original.ProcessId, original.Principal,
                original.StartIdentity, _configuration.Root, _configuration.Descriptor,
                _configuration.Receipt, registry.Snapshot!.Revision, _originalLease, actor,
                installedObservation with { ExecutableIdentity = imageDigest });
            if (!await _receipts.IsCurrentAsync(receiptObservation, ct).ConfigureAwait(false)) return null;
            await RequireActorAsync(actor, ct).ConfigureAwait(false);
            original.RequireSame();
            var launch = new HomeNativeControlledLaunchObservation(original.ProcessId, original.Principal,
                original.StartIdentity, imageDigest, actor.ProfileId, _originalLease.ToString("D"),
                descriptor.AppId, host is null ? "" : HomeNativeSessionHostRequirement.RequiredRole);
            // This maintained port must be genuinely issued by the controlled bootstrap/runtime
            // owner. Signed payload, PID or clean environment alone cannot satisfy it.
            if (!await _launches.IsCurrentAsync(launch, ct).ConfigureAwait(false)) return null;
            await RequireActorAsync(actor, ct).ConfigureAwait(false);
            var after = await _packages.ReadAsync(ct).ConfigureAwait(false);
            await RequireActorAsync(actor, ct).ConfigureAwait(false);
            if (!after.Succeeded || after.Snapshot!.Revision != registry.Snapshot!.Revision ||
                !JsonSerializer.SerializeToUtf8Bytes(RequireRegistryEntry(after.Snapshot, descriptor, receipt))
                    .AsSpan().SequenceEqual(entryBytes)) return null;
            if (!await _launches.IsCurrentAsync(launch, ct).ConfigureAwait(false)) return null;
            await RequireActorAsync(actor, ct).ConfigureAwait(false);
            if (await _principals.GetPrincipalAsync(ct).ConfigureAwait(false) != principal) return null;
            await RequireActorAsync(actor, ct).ConfigureAwait(false);
            original.RequireSame();
            if (!original.ReadBounded(descriptorFile, 1024 * 1024).AsSpan().SequenceEqual(descriptorBytes) ||
                !original.ReadBounded(receiptFile, 2 * 1024 * 1024).AsSpan().SequenceEqual(receiptBytes)) return null;
            if (!await _receipts.IsCurrentAsync(receiptObservation, ct).ConfigureAwait(false)) return null;
            await RequireActorAsync(actor, ct).ConfigureAwait(false);
            original.RequireSame();
            return new HomeNativeInstalledPeer(descriptor.AppId, receipt.InstalledApplicationId, receipt.InstallationRevision,
                imageDigest, receipt.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal))
                { Roles = receipt.Roles.ToFrozenSet(StringComparer.Ordinal) };
        }
    }

    private async ValueTask RequireActorAsync(AuthenticatedResourceActor original, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_lease.IsHeld || _lease.LeaseIdentity != _originalLease ||
            _lease.ProfileId != original.ProfileId ||
            await _profiles.GetCurrentAsync(ct).ConfigureAwait(false) != original)
            throw new UnauthorizedAccessException("The original Home actor or held lease retired.");
    }

    private static HomeNativeObservedPeer Capture(HomeNativeObservedPeer observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        if (observed.ProcessId <= 0 || !Text(observed.OperatingSystemPrincipalId, 256) ||
            !observed.OperatingSystemPrincipalId.StartsWith("windows-sid:", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("A genuine observed Windows peer is required.");
        return observed with { };
    }

    // This is a protected platform receipt codec, not an independent package registry.
    // The existing installer must sign it after its genuine canonical commit. This
    // verifier never creates a receipt, an installation ID or an entry revision.
    private sealed record InstallationReceipt(
        int SchemaVersion, string PackageId, string AppId, string OsApplicationId,
        Guid InstalledApplicationId, string InstallationRevision, long PackageEntryRevision,
        string Version, string Channel, string Platform, string Abi,
        string PayloadSha256, long PayloadBytes, string ImageRelativePath,
        string PublisherCertificateSha256, IReadOnlyList<string> AllowedServiceIds,
        IReadOnlyList<string> Roles, IReadOnlyList<PayloadFile> Files);
    private sealed record PayloadFile(string RelativePath, long Bytes, string Sha256);

    private T DecodeSigned<T>(byte[] signed, int payloadBound)
    {
        RejectDuplicateProperties(signed);
        using var document = JsonDocument.Parse(signed, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "issuerKeyId", "payload", "schemaVersion", "signature" }) ||
            root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported signed receipt envelope.");
        var issuer = root.GetProperty("issuerKeyId").GetString();
        if (issuer is null || !_configuration.Keys.TryGetValue(issuer, out var key))
            throw new UnauthorizedAccessException("The configured original descriptor issuer is missing.");
        var payloadText = root.GetProperty("payload").GetString();
        var signatureText = root.GetProperty("signature").GetString();
        if (payloadText is null || payloadText.Length > ((payloadBound + 2) / 3) * 4 ||
            signatureText is null || signatureText.Length is < 512 or > 1368)
            throw new InvalidDataException("The original signed evidence exceeds its bound.");
        var payload = Convert.FromBase64String(payloadText);
        var signature = Convert.FromBase64String(signatureText);
        if (payload.Length is < 1 || payload.Length > payloadBound || signature.Length is < 384 or > 1024)
            throw new InvalidDataException("The original signed evidence shape is unsupported.");
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(key, out var read);
        if (read != key.Length || rsa.KeySize is < 3072 or > 8192 ||
            !rsa.VerifyData(payload, signature, HashAlgorithmName.SHA384, RSASignaturePadding.Pss))
            throw new UnauthorizedAccessException("The original protected descriptor signature refused.");
        RejectDuplicateProperties(payload);
        return JsonSerializer.Deserialize<T>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { MaxDepth = 20, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("The original signed payload is missing.");
    }

    private void RequireDeclarations(HomePackageArtifactDescriptor descriptor, InstallationReceipt receipt, byte[] signedReceipt)
    {
        var configured = _configuration.Package;
        if (descriptor.SchemaVersion != 1 || receipt.SchemaVersion != 1 ||
            descriptor.PackageId != configured.PackageId || descriptor.AppId != configured.AppId ||
            descriptor.Platform != configured.Platform || descriptor.Abi != configured.Abi ||
            receipt.PackageId != descriptor.PackageId || receipt.AppId != descriptor.AppId ||
            receipt.OsApplicationId != configured.OsApplicationId || receipt.InstalledApplicationId == Guid.Empty ||
            receipt.Version != descriptor.Version || receipt.Channel != descriptor.Channel ||
            receipt.Platform != descriptor.Platform || receipt.Abi != descriptor.Abi ||
            !Text(receipt.InstallationRevision, 256) || receipt.PackageEntryRevision < 1 ||
            receipt.PackageEntryRevision == long.MaxValue ||
            !StringComparer.OrdinalIgnoreCase.Equals(receipt.PayloadSha256, descriptor.PayloadSha256) ||
            receipt.PayloadBytes != descriptor.PayloadBytes ||
            descriptor.PayloadBytes is < 1 or > 8L * 1024 * 1024 * 1024 ||
            !StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(signedReceipt)),
                descriptor.SignedInstallationReceiptSha256) ||
            !HomePackageArtifactSelection.Text(descriptor.Version, 256) ||
            !HomePackageArtifactSelection.Text(descriptor.Channel, 128) ||
            !Enum.GetValues<HomePackageComponentClass>().Any(classification =>
                descriptor.ComponentClass == HomePackageOriginalLifecyclePolicy.DeclaredComponentClass(classification)) ||
            !Sha256(receipt.PublisherCertificateSha256))
            throw new UnauthorizedAccessException("The original configured installed declaration differs.");
        var dependencies = Bounded(descriptor.Dependencies, 128);
        if (dependencies.Any(row => !HomePackageArtifactSelection.Identifier(row.PackageId) ||
                row.MinimumVersion is not null && !HomePackageArtifactSelection.Text(row.MinimumVersion, 256) ||
                row.MaximumVersionExclusive is not null && !HomePackageArtifactSelection.Text(row.MaximumVersionExclusive, 256)) ||
            dependencies.Select(row => row.PackageId).Distinct(StringComparer.Ordinal).Count() != dependencies.Length)
            throw new UnauthorizedAccessException("The original signed dependency declaration is invalid.");
        var services = Bounded(receipt.AllowedServiceIds, 128);
        var roles = Bounded(receipt.Roles, 32);
        var required = Bounded(descriptor.RequiredServiceIds, 128);
        if (services.Any(service => !HomePackageArtifactSelection.Identifier(service)) ||
            services.Distinct(StringComparer.Ordinal).Count() != services.Length ||
            roles.Any(role => !HomePackageArtifactSelection.Identifier(role) || !_configuration.Roles.Contains(role)) ||
            roles.Distinct(StringComparer.Ordinal).Count() != roles.Length ||
            !services.Order(StringComparer.Ordinal).SequenceEqual(required.Order(StringComparer.Ordinal)))
            throw new UnauthorizedAccessException("The original signed service or role declaration differs.");
    }

    private static HomePackageDatabaseEntry RequireRegistryEntry(HomePackageDatabaseSnapshot registry,
        HomePackageArtifactDescriptor descriptor, InstallationReceipt receipt)
    {
        var entries = registry.Packages.Where(entry => entry.PackageId == descriptor.PackageId).Take(2).ToArray();
        if (entries.Length != 1) throw new UnauthorizedAccessException("The canonical installed package is unavailable.");
        var entry = entries[0];
        var currentDependencies = Bounded(entry.Dependencies, 128);
        var signedDependencies = Bounded(descriptor.Dependencies, 128);
        if (!currentDependencies.OrderBy(row => row.PackageId, StringComparer.Ordinal)
                .SequenceEqual(signedDependencies.OrderBy(row => row.PackageId, StringComparer.Ordinal)))
            throw new UnauthorizedAccessException("The canonical installed dependency declarations differ.");
        if (entry.AppId != descriptor.AppId || entry.Revision != receipt.PackageEntryRevision ||
            entry.InstalledVersion != descriptor.Version || entry.UpdateChannel != descriptor.Channel ||
            entry.InstallationState != HomePackageInstallState.Installed ||
            entry.Compatibility != HomePackageCompatibility.Compatible ||
            !entry.IntegrityEvidence.Any(evidence => evidence.Version == descriptor.Version &&
                evidence.State == HomePackageIntegrityState.Verified &&
                StringComparer.OrdinalIgnoreCase.Equals(evidence.ArtifactSha256, descriptor.PayloadSha256)))
            throw new UnauthorizedAccessException("The canonical installed package or generation differs.");
        return entry;
    }

    private PayloadFile[] CaptureFiles(IReadOnlyList<PayloadFile>? files)
    {
        var captured = Bounded(files, 4096).Select(file => file with { }).ToArray();
        if (captured.Length == 0 || captured.Any(file => !SafeRelative(file.RelativePath) ||
            file.Bytes is < 1 or > 8L * 1024 * 1024 * 1024 || !Sha256(file.Sha256)) ||
            captured.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != captured.Length ||
            captured.Any(file => file.RelativePath == _configuration.Descriptor || file.RelativePath == _configuration.Receipt))
            throw new InvalidDataException("The protected original payload inventory is invalid.");
        return captured;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void RequireExactFiles(HomeNativeWindowsProtectedPeerEvidence original, PayloadFile[] files)
    {
        var expected = files.Select(file => file.RelativePath).Append(_configuration.Descriptor)
            .Append(_configuration.Receipt).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directories = new Stack<string>(); directories.Push(_configuration.Root);
        var directoryCount = 0;
        while (directories.TryPop(out var directory))
        {
            if (++directoryCount > 4096) throw new InvalidDataException("Protected payload directory count exceeds its bound.");
            original.OpenProtected(directory, true);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var relative = RelativeImage(path);
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                {
                    original.OpenProtected(path, true); directories.Push(path);
                }
                else if (!expected.Remove(relative)) throw new UnauthorizedAccessException("Protected payload has an undeclared file.");
            }
        }
        if (expected.Count != 0) throw new UnauthorizedAccessException("Protected payload is incomplete.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void OpenOriginalAncestors(HomeNativeWindowsProtectedPeerEvidence original, string root)
    {
        var ancestors = new Stack<string>();
        for (var current = new DirectoryInfo(root); current is not null; current = current.Parent)
            ancestors.Push(current.FullName);
        if (ancestors.Count > 64) throw new InvalidDataException("Protected installation ancestry exceeds its bound.");
        while (ancestors.TryPop(out var path)) original.OpenProtected(path, true);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private Microsoft.Win32.SafeHandles.SafeFileHandle OpenOriginalRelative(
        HomeNativeWindowsProtectedPeerEvidence original, string relative)
    {
        if (!SafeRelative(relative)) throw new InvalidDataException("The protected relative path is invalid.");
        var full = Path.GetFullPath(Path.Combine(_configuration.Root, relative));
        if (RelativeImage(full) != relative) throw new UnauthorizedAccessException("The original protected relative path changed.");
        var directory = Path.GetDirectoryName(full)!;
        var ancestors = new Stack<string>();
        while (!StringComparer.OrdinalIgnoreCase.Equals(directory, _configuration.Root))
        {
            if (ancestors.Count == 64) throw new InvalidDataException("Protected file ancestry exceeds its bound.");
            ancestors.Push(directory); directory = Path.GetDirectoryName(directory)
                ?? throw new UnauthorizedAccessException("Protected file escapes its original root.");
        }
        while (ancestors.TryPop(out var path)) original.OpenProtected(path, true);
        return original.OpenProtected(full);
    }

    private string RelativeImage(string absolute)
    {
        var full = Path.GetFullPath(absolute);
        var prefix = _configuration.Root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Original process image is outside the protected installation.");
        var relative = full[prefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
        if (!SafeRelative(relative)) throw new UnauthorizedAccessException("Protected image relative path is invalid.");
        return relative;
    }

    private static bool SafeRelative(string? relative) => relative is { Length: > 0 and <= 1024 } &&
        relative == relative.Trim() && !Path.IsPathRooted(relative) &&
        !relative.Any(character => char.IsControl(character) || character is '\\' or ':' or '*'
            or '?' or '"' or '<' or '>' or '|') &&
        relative.Split('/').All(part => part.Length > 0 && part is not "." and not ".." &&
            part == part.TrimEnd('.', ' '));

    private static T[] Bounded<T>(IReadOnlyList<T>? rows, int maximum)
    {
        if (rows is null || rows.Count > maximum) throw new InvalidDataException("The original evidence collection exceeds its bound.");
        var values = new List<T>();
        foreach (var row in rows)
        {
            if (row is null || values.Count == maximum) throw new InvalidDataException("The original evidence collection is invalid.");
            values.Add(row);
        }
        return values.ToArray();
    }
    private static bool Text(string? value, int maximum) => value is { Length: > 0 } &&
        value.Length <= maximum && value == value.Trim() && !value.Any(char.IsControl);
    private static bool Sha256(string? value) => value is { Length: 64 } &&
        value.All(character => char.IsAsciiHexDigit(character));

    private static void RejectDuplicateProperties(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 20 });
        var objects = new Stack<HashSet<string>?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.StartArray) objects.Push(null);
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek()!.Add(reader.GetString()!))
                throw new InvalidDataException("Duplicate original signed evidence field.");
        }
    }

    private sealed record CapturedConfiguration(HomeNativeWindowsProtectedPeerConfiguration Package,
        string Root, string Descriptor, string Receipt, FrozenDictionary<string, byte[]> Keys,
        FrozenSet<string> Publishers, FrozenSet<string> Writers, FrozenSet<string> Roles)
    {
        internal static CapturedConfiguration Capture(HomeNativeWindowsProtectedPeerConfiguration original)
        {
            ArgumentNullException.ThrowIfNull(original);
            if (!HomePackageArtifactSelection.Identifier(original.PackageId) ||
                !HomePackageArtifactSelection.Identifier(original.AppId) ||
                !Text(original.OsApplicationId, 1024) || !HomePackageArtifactSelection.Identifier(original.Platform) ||
                !HomePackageArtifactSelection.Identifier(original.Abi) || !Path.IsPathFullyQualified(original.ProtectedRoot) ||
                original.ProtectedRoot.Length < 4 || !char.IsAsciiLetter(original.ProtectedRoot[0]) ||
                original.ProtectedRoot[1] != ':' || original.ProtectedRoot[2] != '\\' ||
                !SafeRelative(original.DescriptorRelativePath) || !SafeRelative(original.ReceiptRelativePath) ||
                original.DescriptorRelativePath == original.ReceiptRelativePath)
                throw new ArgumentException("Exact configured protected installation identities are required.");
            var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (original.DescriptorIssuerSubjectPublicKeys is null || original.DescriptorIssuerSubjectPublicKeys.Count is < 1 or > 16)
                throw new ArgumentException("A bounded configured signing-key policy is required.");
            foreach (var row in original.DescriptorIssuerSubjectPublicKeys)
            {
                if (!HomePackageArtifactSelection.Identifier(row.Key) || row.Value.Length is < 1 or > 4096 ||
                    !keys.TryAdd(row.Key, row.Value.ToArray())) throw new ArgumentException("The configured signing-key policy is invalid.");
            }
            static FrozenSet<string> CaptureSet(IReadOnlySet<string>? source, int maximum, Func<string, bool> valid)
            {
                if (source is null || source.Count is < 1 || source.Count > maximum) throw new ArgumentException("A bounded configured trust set is required.");
                var values = new HashSet<string>(StringComparer.Ordinal);
                foreach (var value in source) if (!valid(value) || !values.Add(value) || values.Count > maximum)
                    throw new ArgumentException("The configured trust set is invalid.");
                return values.ToFrozenSet(StringComparer.Ordinal);
            }
            var publishers = CaptureSet(original.PublisherCertificateSha256, 16, Sha256)
                .Select(value => value.ToUpperInvariant()).ToFrozenSet(StringComparer.Ordinal);
            var writers = CaptureSet(original.ProtectedWriterSids, 16, value => Text(value, 256));
            var roles = CaptureSet(original.PermittedRoles, 32, HomePackageArtifactSelection.Identifier);
            var root = Path.GetFullPath(original.ProtectedRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (root.Length < 3) throw new ArgumentException("A specific protected installation root is required.");
            return new(original with {
                DescriptorIssuerSubjectPublicKeys = keys.ToFrozenDictionary(row => row.Key,
                    row => (ReadOnlyMemory<byte>)row.Value.ToArray(), StringComparer.Ordinal),
                PublisherCertificateSha256 = publishers, ProtectedWriterSids = writers, PermittedRoles = roles
            }, root, original.DescriptorRelativePath, original.ReceiptRelativePath,
                keys.ToFrozenDictionary(StringComparer.Ordinal), publishers, writers, roles);
        }
    }
}

internal static class HomeNativeWindowsOriginalEvidenceLifetime
{
    internal static async ValueTask<T?> VerifyAsync<T>(Func<ValueTask<T?>> body, Action close)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(close);
        T? result = null;
        Exception? refusal = null;
        List<Exception> errors = [];
        try { result = await body().ConfigureAwait(false); }
        catch (HomeNativeWindowsOriginalEvidenceCleanupFailure error)
        {
            foreach (var original in error.OriginalFailures) Add(errors, original);
        }
        catch (Exception error) when (IsRefusal(error)) { refusal = error; }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            try { close(); } catch (Exception error) { Add(errors, error); }
        }
        // An ordinary refusal returns unavailable only after successful cleanup.
        // A failed close retains that SAME refusal as well as the independent close.
        if (refusal is not null && errors.Count != 0) errors.Insert(0, refusal);
        ThrowOriginals(errors);
        return result;
    }

    internal static void RunWithCleanup(Action body, Action close) =>
        RunWithCleanups(body, close);

    internal static void RunWithCleanups(Action body, params Action[] closes)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(closes);
        if (closes.Any(close => close is null)) throw new ArgumentException("Original cleanup is missing.");
        Exception? primary = null;
        List<Exception> cleanup = [];
        try { body(); } catch (Exception error) { primary = error; }
        finally
        {
            foreach (var close in closes)
                try { close(); } catch (Exception error) { Add(cleanup, error); }
        }
        ThrowCleanupFailure(primary, cleanup);
    }

    internal static void ThrowCleanupFailure(Exception? primary, List<Exception> cleanup)
    {
        if (cleanup.Count != 0)
        {
            List<Exception> originals = [];
            if (primary is not null) Add(originals, primary);
            foreach (var error in cleanup) Add(originals, error);
            // Cleanup-only IO failures must not be classified as ordinary evidence refusal.
            throw new HomeNativeWindowsOriginalEvidenceCleanupFailure(originals);
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    internal static IntPtr FollowBorrowedTrustChain(IntPtr originalState,
        Func<IntPtr, IntPtr> provider, Func<IntPtr, IntPtr> signer, Func<IntPtr, IntPtr> certificate)
    {
        static IntPtr Require(IntPtr pointer, string stage) => pointer != IntPtr.Zero
            ? pointer : throw new UnauthorizedAccessException(stage);
        var state = Require(originalState, "The original WinTrust state is missing.");
        var originalProvider = Require(provider(state), "The original publisher provider is missing.");
        var originalSigner = Require(signer(originalProvider), "The original publisher signer is missing.");
        return Require(certificate(originalSigner), "The original publisher certificate is missing.");
    }

    internal static void ThrowWithCleanup(Exception original, Action close) =>
        RunWithCleanup(() => ExceptionDispatchInfo.Capture(original).Throw(), close);

    private static bool IsRefusal(Exception error) => error is UnauthorizedAccessException or
        InvalidDataException or JsonException or CryptographicException or
        System.ComponentModel.Win32Exception or IOException;

    private static void Add(List<Exception> errors, Exception error)
    {
        if (error is HomeNativeWindowsOriginalEvidenceCleanupFailure cleanup)
        {
            foreach (var original in cleanup.OriginalFailures) Add(errors, original);
        }
        else if (!errors.Any(previous => ReferenceEquals(previous, error))) errors.Add(error);
    }

    private static void ThrowOriginals(List<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1)
            throw new AggregateException("Original Windows peer evidence and independent cleanup failed.", errors);
    }
}

internal sealed class HomeNativeWindowsOriginalEvidenceCleanupFailure : Exception
{
    internal IReadOnlyList<Exception> OriginalFailures { get; }
    internal HomeNativeWindowsOriginalEvidenceCleanupFailure(List<Exception> originals)
        : base("Original Windows evidence cleanup failed.",
            originals.Count == 1 ? originals[0] : new AggregateException(originals))
    {
        OriginalFailures = Array.AsReadOnly(originals.ToArray());
    }
}
