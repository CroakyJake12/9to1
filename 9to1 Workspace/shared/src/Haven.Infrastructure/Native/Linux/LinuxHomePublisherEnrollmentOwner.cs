using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>Explicit Linux administrative publisher enrollment in the existing
/// canonical package DB. Root-owned private state/lock paths, actual OS profile and
/// explicit public-certificate/catalogue hashes are required. No key is generated,
/// imported or installed, and no installed peer, package activation or service Ready
/// follows from this operation. Ordinary Home startup never invokes it.</summary>
public sealed class LinuxHomePublisherEnrollmentOwner
{
    public sealed record Request(string PublicCertificatePath, string ExplicitCertificateSha256,
        string SignedCataloguePath, string ExplicitSignedCatalogueSha256, string IssuerKeyId,
        string Abi, IReadOnlyList<string> SelectedPackageIds, Guid OperationId);
    public sealed record Result(bool AlreadyEnrolled, long RegistryRevision,
        HomePackageOriginalPublisherEnrollmentRecord Enrollment);
    public sealed class EnrollmentCommitUnconfirmedException : IOException
    {
        public Result KnownCommittedResult { get; }
        internal EnrollmentCommitUnconfirmedException(Result committed, Exception cause)
            : base("Publisher enrollment was committed but final source/profile currentness failed; retain the original operation for recovery.", cause)
        { KnownCommittedResult = committed; }
    }
    private readonly HomePackageDatabase _database;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly string _statePath;
    public LinuxHomePublisherEnrollmentOwner(FileHomeCoreStateStore sameStore, HomePackageDatabase sameDatabase,
        HomeLocalProfileIdentity sameProfiles, string actualCanonicalStateFile)
    {
        ArgumentNullException.ThrowIfNull(sameStore); ArgumentNullException.ThrowIfNull(sameDatabase);
        ArgumentNullException.ThrowIfNull(sameProfiles);
        if (!sameStore.IsOriginalConfiguredFile(actualCanonicalStateFile) || !sameDatabase.HasOriginalProfileComposition(sameStore, sameProfiles))
            throw new UnauthorizedAccessException("Use the SAME configured canonical package store/database/OS profile.");
        _database = sameDatabase; _profiles = sameProfiles;
        _statePath = Path.GetFullPath(actualCanonicalStateFile);
    }

    public Task<Result> EnrollAsync(Request originalRequest, CancellationToken cancellationToken)
        => ProcessAsync(originalRequest, null, cancellationToken);

    // Reuses the exact verified public-input lifetime and existing canonical enrollment.
    // This path cannot enroll a publisher or initialize a missing machine profile.
    internal Task<Result> WithinExistingEnrollmentAsync(Request request,
        Func<ExistingEnrollment, CancellationToken, Task> operation, CancellationToken token)
        => ProcessAsync(request, operation ?? throw new ArgumentNullException(nameof(operation)), token);

    private async Task<Result> ProcessAsync(Request originalRequest,
        Func<ExistingEnrollment, CancellationToken, Task>? existingOperation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalRequest);
        cancellationToken.ThrowIfCancellationRequested(); DemandAdministrator();
        if (!DigestShape(originalRequest.ExplicitCertificateSha256) || !DigestShape(originalRequest.ExplicitSignedCatalogueSha256) ||
            !Identifier(originalRequest.IssuerKeyId) || !Identifier(originalRequest.Abi) || originalRequest.OperationId == Guid.Empty)
            throw new ArgumentException("Exact public signer/catalogue hashes, issuer, ABI and operation identity are required.");
        var selected = originalRequest.SelectedPackageIds?.Take(129).ToArray();
        if (selected is not { Length: >= 2 and <= 128 } || selected.Any(id => !Identifier(id)) ||
            selected.Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new ArgumentException("Explicit distinct Home/Root and selected package identities are required.");
        var request = originalRequest with { SelectedPackageIds = Array.AsReadOnly(selected) };
        if (new[] { request.PublicCertificatePath, request.SignedCataloguePath }.Select(Path.GetFullPath)
            .Any(path => path == _statePath || path == _statePath + ".lock"))
            throw new ArgumentException("Public signing inputs must be distinct from canonical state and lock files.");
        using var state = new ProtectedState(_statePath);
        using var certificateInput = new Input(request.PublicCertificatePath, 128 * 1024);
        using var catalogueInput = new Input(request.SignedCataloguePath, 8 * 1024 * 1024);
        if (!Digest(certificateInput.Bytes).Equals(request.ExplicitCertificateSha256, StringComparison.OrdinalIgnoreCase) ||
            !Digest(catalogueInput.Bytes).Equals(request.ExplicitSignedCatalogueSha256, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The public certificate or signed catalogue differs from the explicit administrator choice.");
        using var certificate = X509CertificateLoader.LoadCertificate(certificateInput.Bytes);
        if (!certificate.RawData.AsSpan().SequenceEqual(certificateInput.Bytes))
            throw new InvalidDataException("Supply the exact DER public certificate so its file hash is the canonical certificate fingerprint.");
        var now = DateTime.UtcNow;
        if (certificate.HasPrivateKey || now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
            throw new UnauthorizedAccessException("A currently valid public publisher certificate is required; private-key inputs are unsupported.");
        var enhanced = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().ToArray();
        var usages = certificate.Extensions.OfType<X509KeyUsageExtension>().ToArray();
        if (enhanced.Length != 1 || usages.Length != 1 ||
            !enhanced[0].EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.3") ||
            (usages[0].KeyUsages & X509KeyUsageFlags.DigitalSignature) == 0 ||
            certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension => extension.CertificateAuthority))
            throw new UnauthorizedAccessException("The explicit publisher certificate must permit code signing and digital signatures.");
        using var signer = certificate.GetRSAPublicKey();
        if (signer is null || signer.KeySize is < 3072 or > 8192)
            throw new UnauthorizedAccessException("The explicit publisher must support the existing RSA3072–8192 PSS/SHA256 contract.");
        var payload = VerifyEnvelope(catalogueInput.Bytes, signer, request.IssuerKeyId,
            HomePackageOriginalPublisherEnrollmentRecord.MaximumRetainedCatalogueBytes);
        var catalogue = JsonSerializer.Deserialize<Haven.Infrastructure.Native.Windows.NativeWindowsHomeInstallerBootstrapAdmission.Catalogue>(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 16,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("The signed original catalogue is empty.");
        // Verify every original descriptor with the SAME actual public signer before
        // any profile/state initialization, never merely parse a signature-shaped blob.
        var rows = LinuxHomePackageOriginalArtifactProvider.ReadCatalogueBytes(payload, request.IssuerKeyId,
            catalogue.CatalogueRevision, catalogue.HomePackageId, catalogue.RootPackageId, request.SelectedPackageIds, request.Abi, cancellationToken);
        foreach (var row in rows.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var verified = VerifyEnvelope(row.Signed, signer, request.IssuerKeyId, 64 * 1024);
            if (!verified.AsSpan().SequenceEqual(row.Payload)) throw new UnauthorizedAccessException("The signed original package payload changed.");
        }
        certificateInput.DemandCurrent(); catalogueInput.DemandCurrent(); state.DemandCurrent();
        var read = await _database.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.Succeeded) throw new InvalidDataException("The canonical package registry requires recovery: " + read.Failure?.Code);
        var registry = read.Snapshot!;
        if (existingOperation is not null && (registry.UnknownFields is null ||
            !registry.UnknownFields.ContainsKey(HomePackageOriginalPublisherEnrollmentRecord.Field)))
            throw new UnauthorizedAccessException("Bootstrap activation requires the already-enrolled canonical publisher; it cannot establish initial trust.");
        var principal = await new OperatingSystemPrincipalSource().GetPrincipalAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The actual administrative OS principal is unavailable.");
        var actor = await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The actual administrative Home profile is unavailable.");
        await using var executable = File.OpenRead("/proc/self/exe");
        var image = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(executable, cancellationToken).ConfigureAwait(false));
        var enrollment = new HomePackageOriginalPublisherEnrollmentRecord(2, Digest(certificateInput.Bytes), request.IssuerKeyId,
            catalogue.CatalogueRevision, Digest(payload), image, request.OperationId, actor.ActorId, actor.ProfileId, principal,
            catalogue.HomePackageId, catalogue.RootPackageId, request.SelectedPackageIds, DateTimeOffset.UtcNow, Convert.ToBase64String(payload));
        var fields = registry.UnknownFields?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)
            ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (fields.TryGetValue(HomePackageOriginalPublisherEnrollmentRecord.Field, out var previous))
        {
            var retained = previous.Deserialize<HomePackageOriginalPublisherEnrollmentRecord>()
                ?? throw new InvalidDataException("The previous publisher enrollment is malformed; preserve it for recovery.");
            if (retained.SchemaVersion != 2 || retained.PublisherCertificateSha256 != enrollment.PublisherCertificateSha256 ||
                retained.IssuerKeyId != enrollment.IssuerKeyId || retained.CatalogueSha256 != enrollment.CatalogueSha256 ||
                retained.CatalogueRevision != enrollment.CatalogueRevision || retained.HomePackageId != enrollment.HomePackageId ||
                retained.RootPackageId != enrollment.RootPackageId || retained.OriginalOsPrincipal != principal ||
                retained.OriginalProfileId != actor.ProfileId || retained.OriginalSignedCatalogueBase64 != enrollment.OriginalSignedCatalogueBase64 ||
                retained.SelectedPackageIds is null ||
                !retained.SelectedPackageIds.Order(StringComparer.Ordinal).SequenceEqual(selected.Order(StringComparer.Ordinal)))
                throw new UnauthorizedAccessException("A different existing publisher/catalogue/selection requires explicit owning recovery; enrollment never overwrites it.");
            await DemandFinalAsync().ConfigureAwait(false);
            if (existingOperation is not null)
            {
                var existing = new ExistingEnrollment(this, registry, retained, actor, rows, signer,
                    new CommitGuard(state, certificateInput, catalogueInput, _profiles, actor,
                        certificate.NotBefore.ToUniversalTime(), certificate.NotAfter.ToUniversalTime()), DemandFinalAsync);
                await existingOperation(existing, cancellationToken).ConfigureAwait(false);
                await DemandFinalAsync().ConfigureAwait(false);
            }
            return new(true, registry.Revision, retained);
        }
        fields.Add(HomePackageOriginalPublisherEnrollmentRecord.Field, JsonSerializer.SerializeToElement(enrollment));
        var guard = new CommitGuard(state, certificateInput, catalogueInput, _profiles, actor,
            certificate.NotBefore.ToUniversalTime(), certificate.NotAfter.ToUniversalTime());
        var write = await _database.SaveOriginalBootstrapEnrollmentWithinSourceAsync(registry with { UnknownFields = fields },
            registry.Revision, actor, guard, body => body(), _ => { }, cancellationToken).ConfigureAwait(false);
        if (!write.Succeeded) throw new InvalidDataException("The original enrollment commit was not acknowledged: " + write.Failure?.Code);
        var committed = new Result(false, write.Snapshot!.Revision, enrollment);
        try { await DemandFinalAsync().ConfigureAwait(false); }
        catch (Exception cause) { throw new EnrollmentCommitUnconfirmedException(committed, cause); }
        return committed;

        async Task DemandFinalAsync()
        {
            DemandAdministrator(); state.DemandCurrent(); certificateInput.DemandCurrent(); catalogueInput.DemandCurrent();
            if (DateTime.UtcNow < certificate.NotBefore.ToUniversalTime() || DateTime.UtcNow > certificate.NotAfter.ToUniversalTime())
                throw new UnauthorizedAccessException("The original public publisher certificate is no longer current.");
            if (actor != await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ||
                principal != await new OperatingSystemPrincipalSource().GetPrincipalAsync(cancellationToken).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual administrative OS profile changed during enrollment; retain its original outcome for recovery.");
        }
    }

    internal sealed class ExistingEnrollment
    {
        private readonly LinuxHomePublisherEnrollmentOwner _owner;
        private readonly RSA _signer;
        private readonly IHomeStateCommitActorGuard _guard;
        private readonly Func<Task> _demandCurrent;
        internal HomePackageDatabaseSnapshot Registry { get; }
        internal HomePackageOriginalPublisherEnrollmentRecord Enrollment { get; }
        internal AuthenticatedResourceActor Actor { get; }
        internal IReadOnlyDictionary<string, LinuxHomePackageOriginalArtifactProvider.Row> Rows { get; }
        internal ExistingEnrollment(LinuxHomePublisherEnrollmentOwner owner, HomePackageDatabaseSnapshot registry,
            HomePackageOriginalPublisherEnrollmentRecord enrollment, AuthenticatedResourceActor actor,
            IReadOnlyDictionary<string, LinuxHomePackageOriginalArtifactProvider.Row> rows, RSA signer, IHomeStateCommitActorGuard guard, Func<Task> demandCurrent)
        { _owner = owner; Registry = registry; Enrollment = enrollment; Actor = actor; Rows = rows; _signer = signer; _guard = guard; _demandCurrent = demandCurrent; }
        internal byte[] VerifyReceipt(byte[] bytes) => VerifyEnvelope(bytes, _signer, Enrollment.IssuerKeyId, 1024 * 1024);
        internal async Task<HomePackageDatabaseSnapshot> SaveAsync(HomePackageDatabaseSnapshot snapshot, long expectedRevision, CancellationToken token)
        {
            var written = await _owner._database.SaveOriginalBootstrapEnrollmentWithinSourceAsync(snapshot, expectedRevision,
                Actor, _guard, body => body(), _ => { }, token).ConfigureAwait(false);
            return written.Succeeded ? written.Snapshot! : throw new IOException("The canonical bootstrap commit was not acknowledged: " + written.Failure?.Code);
        }
        internal Task DemandCurrentAsync() => _demandCurrent();
        internal string StatePath => _owner._statePath;
    }

    private sealed class CommitGuard(ProtectedState state, Input certificate, Input catalogue,
        HomeLocalProfileIdentity profiles, AuthenticatedResourceActor originalActor,
        DateTime validFrom, DateTime validUntil) : IHomeStateCommitActorGuard
    {
        public async ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
            HomeStateCommitPhase phase, CancellationToken token)
        {
            DemandAdministrator(); state.DemandCurrent(); certificate.DemandCurrent(); catalogue.DemandCurrent();
            var now = DateTime.UtcNow;
            return now >= validFrom && now <= validUntil && expectedActor == originalActor &&
                await profiles.CheckAsync(lockedState, expectedActor, phase, token).ConfigureAwait(false);
        }
    }

    private static byte[] VerifyEnvelope(byte[] bytes, RSA signer, string issuer, int maximum)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "issuerKeyId", "payload", "schemaVersion", "signature" }) ||
            root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("issuerKeyId").GetString() != issuer ||
            root.GetProperty("payload").GetString() is not { Length: > 0 } encoded || encoded.Length > ((maximum + 2L) / 3) * 4 ||
            root.GetProperty("signature").GetString() is not { Length: >= 512 and <= 1368 } signature)
            throw new InvalidDataException("The original signed catalogue/descriptor envelope is unsupported.");
        var payload = Convert.FromBase64String(encoded); var signatureBytes = Convert.FromBase64String(signature);
        if (payload.Length is 0 || payload.Length > maximum || signatureBytes.Length is < 384 or > 1024 ||
            !signer.VerifyData(payload, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new UnauthorizedAccessException("The exact original catalogue/descriptor signature does not match the explicit public publisher.");
        return payload;
    }

    private sealed class Input : IDisposable
    {
        private readonly string _path;
        private readonly SafeFileHandle _file;
        private readonly LinuxOriginalDownloadIdentity _identity;
        internal byte[] Bytes { get; }
        internal Input(string path, int maximum)
        {
            _path = Path.GetFullPath(path);
            using var parent = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(Path.GetDirectoryName(_path)!, mutable: false);
            _file = LinuxOriginalDownloadPaths.OpenRead(parent, Path.GetFileName(_path));
            try
            {
                _identity = LinuxOriginalDownloadPaths.ReadIdentity(_file);
                if (!_identity.IsRegular || _identity.Links != 1 || _identity.Size is 0 || _identity.Size > (ulong)maximum)
                    throw new InvalidDataException("The public signing input must be a bounded, single-link regular file.");
                Bytes = new byte[checked((int)_identity.Size)]; var offset = 0;
                while (offset < Bytes.Length)
                {
                    var read = RandomAccess.Read(_file, Bytes.AsSpan(offset), offset);
                    if (read == 0) throw new EndOfStreamException("The original public signing input was truncated.");
                    offset += read;
                }
                DemandCurrent();
            }
            catch { _file.Dispose(); throw; }
        }
        internal void DemandCurrent()
        {
            LinuxOriginalDownloadPaths.DemandPath(_file, _path, directory: false);
            if (!_identity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(_file)))
                throw new UnauthorizedAccessException("The original public signing input changed during enrollment.");
        }
        public void Dispose() => _file.Dispose();
    }

    private sealed class ProtectedState : IDisposable
    {
        private readonly string _directory, _name;
        private readonly LinuxOriginalDownloadRoot _root;
        internal ProtectedState(string path)
        {
            _directory = Path.GetDirectoryName(path)!; _name = Path.GetFileName(path);
            DemandChain(_directory);
            _root = LinuxOriginalDownloadPaths.RetainRoot(_directory, requirePrivate: true);
            try { DemandCurrent(); } catch { _root.Dispose(); throw; }
        }
        internal void DemandCurrent()
        {
            DemandAdministrator(); DemandChain(_directory); _root.DemandCurrent();
            using var parent = _root.OpenDirectory(_directory, mutable: false);
            foreach (var leaf in new[] { _name, _name + ".lock" })
            {
                SafeFileHandle file;
                try { file = LinuxOriginalDownloadPaths.OpenRead(parent, leaf); }
                catch (FileNotFoundException) { continue; } // exact native ENOENT only
                using (file)
                {
                    LinuxOriginalDownloadPaths.DemandPath(file, Path.Combine(_directory, leaf), directory: false);
                    LinuxOriginalDownloadPaths.DemandPrivateStage(file, 0);
                }
            }
        }
        internal static void DemandChain(string directory)
        {
            var prefix = "";
            foreach (var component in directory.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                prefix += "/" + component;
                using var held = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(prefix, mutable: false);
                var actual = LinuxOriginalDownloadPaths.ReadIdentity(held);
                // Root-owned sticky parents (e.g. /tmp) protect their root-owned
                // existing child. Every component, including that child, is checked.
                if (!actual.IsDirectory || actual.Uid != 0 || (actual.Mode & 0xc00) != 0 ||
                    (actual.Mode & 0x12) != 0 && (actual.Mode & 0x200) == 0)
                    throw new UnauthorizedAccessException("The canonical machine-state path must have root-owned protected ancestors, not a user-controlled parent.");
            }
        }
        public void Dispose() => _root.Dispose();
    }
    internal static void DemandProtectedMachineDirectory(string path) { DemandAdministrator(); ProtectedState.DemandChain(path); }
    private static void DemandAdministrator()
    {
        if (!OperatingSystem.IsLinux() || LinuxOriginalDownloadPaths.CurrentUid() != 0)
            throw new UnauthorizedAccessException("Explicit Linux publisher enrollment requires the actual administrative effective UID0; no elevation is attempted.");
    }
    private static bool Identifier(string? value) => value is { Length: > 0 and <= 128 } &&
        (value[0] is >= 'a' and <= 'z' or >= '0' and <= '9') && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');
    private static bool DigestShape(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
