using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Haven.Infrastructure.Native.Linux;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Real isolated root-owned file store, OS profile and RSA-PSS verification.
/// Ephemeral TEST-ONLY signers exist only in memory; only their public DER certificate
/// is written. No OS trust store, production enrollment, package installation, native
/// app identity or service readiness is established by these fixtures.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHomePublisherEnrollmentOwnerTests
{
    private sealed class LinuxAdminFactAttribute : FactAttribute
    { public LinuxAdminFactAttribute() { if (!OperatingSystem.IsLinux() || GetEuid() != 0) Skip = "Requires an actual Linux UID0 isolated filesystem fixture."; } }
    private sealed class LinuxAdminTheoryAttribute : TheoryAttribute
    { public LinuxAdminTheoryAttribute() { if (!OperatingSystem.IsLinux() || GetEuid() != 0) Skip = "Requires an actual Linux UID0 isolated filesystem fixture."; } }

    [LinuxAdminFact]
    public async Task Real_signatures_write_only_canonical_enrollment_and_same_selection_is_idempotent()
    {
        using var rig = new Rig();
        var result = await rig.Owner.EnrollAsync(rig.Request(), rig.Token);
        Assert.False(result.AlreadyEnrolled);
        Assert.Equal(rig.Request().ExplicitCertificateSha256, result.Enrollment.PublisherCertificateSha256);
        Assert.StartsWith("sha256:", result.Enrollment.InstallerExecutableIdentity);
        Assert.Equal("unix-euid:0", result.Enrollment.OriginalOsPrincipal);
        var read = await rig.Database.ReadAsync(rig.Token);
        Assert.True(read.Succeeded);
        Assert.Empty(read.Snapshot!.Packages);
        Assert.Empty(read.Snapshot.RecentOperations);
        Assert.Contains(HomePackageOriginalPublisherEnrollmentRecord.Field, read.Snapshot.UnknownFields!.Keys);
        var before = File.ReadAllBytes(rig.StatePath);
        var repeated = await rig.Owner.EnrollAsync(rig.Request() with { OperationId = Guid.NewGuid() }, rig.Token);
        Assert.True(repeated.AlreadyEnrolled);
        Assert.Equal(result.RegistryRevision, repeated.RegistryRevision);
        Assert.Equal(result.Enrollment.OriginalEnrollmentOperationId, repeated.Enrollment.OriginalEnrollmentOperationId);
        Assert.Equal(before, File.ReadAllBytes(rig.StatePath));
    }

    [LinuxAdminTheory]
    [InlineData("certificate-hash")]
    [InlineData("catalogue-hash")]
    [InlineData("descriptor-signature")]
    [InlineData("outer-signature")]
    public async Task Wrong_explicit_inputs_or_invalid_signatures_never_initialize_the_machine_profile(string damage)
    {
        using var rig = new Rig();
        if (damage == "descriptor-signature") rig.WriteCatalogue(corruptDescriptor: true);
        if (damage == "outer-signature") rig.WriteCatalogue(corruptOuter: true);
        var request = rig.Request();
        if (damage == "certificate-hash") request = request with { ExplicitCertificateSha256 = new string('0', 64) };
        if (damage == "catalogue-hash") request = request with { ExplicitSignedCatalogueSha256 = new string('0', 64) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.EnrollAsync(request, rig.Token));
        Assert.False(File.Exists(rig.StatePath));
        Assert.False(File.Exists(rig.StatePath + ".lock"));
    }

    [LinuxAdminFact]
    public async Task Different_valid_signed_catalogue_requires_explicit_recovery_and_preserves_existing_enrollment()
    {
        using var rig = new Rig();
        await rig.Owner.EnrollAsync(rig.Request(), rig.Token);
        var before = File.ReadAllBytes(rig.StatePath);
        rig.WriteCatalogue(revision: "test.catalogue.changed");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.EnrollAsync(rig.Request(), rig.Token));
        Assert.Equal(before, File.ReadAllBytes(rig.StatePath));
    }

    [LinuxAdminTheory]
    [InlineData("state")]
    [InlineData("lock")]
    public async Task Symlink_state_or_lock_is_refused_before_privileged_profile_initialization(string selected)
    {
        using var rig = new Rig();
        var target = Path.Combine(rig.Root, "foreign.txt"); File.WriteAllText(target, "preserve foreign file");
        var linked = selected == "state" ? rig.StatePath : rig.StatePath + ".lock";
        File.CreateSymbolicLink(linked, target);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.EnrollAsync(rig.Request(), rig.Token));
        Assert.Equal("preserve foreign file", File.ReadAllText(target));
        Assert.NotNull(new FileInfo(linked).LinkTarget);
    }

    [LinuxAdminFact]
    public async Task User_writable_ancestor_is_not_adopted_even_when_the_final_state_directory_is_private()
    {
        using var rig = new Rig();
        File.SetUnixFileMode(rig.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.EnrollAsync(rig.Request(), rig.Token));
        Assert.False(File.Exists(rig.StatePath));
        Assert.True(File.GetUnixFileMode(rig.Root).HasFlag(UnixFileMode.OtherWrite));
    }

    [LinuxAdminFact]
    public async Task Expired_public_certificate_is_refused_before_any_canonical_profile_is_initialized()
    {
        using var rig = new Rig(expired: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.EnrollAsync(rig.Request(), rig.Token));
        Assert.False(File.Exists(rig.StatePath));
    }

    [LinuxAdminFact]
    public async Task A_TLS_certificate_does_not_substitute_for_explicit_code_signing_publisher_policy()
    {
        using var rig = new Rig(codeSigning: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.EnrollAsync(rig.Request(), rig.Token));
        Assert.False(File.Exists(rig.StatePath));
    }

    [LinuxAdminFact]
    public async Task Foreign_store_and_input_state_aliases_are_refused_without_writes()
    {
        using var rig = new Rig();
        var foreign = new FileHomeCoreStateStore(Path.Combine(rig.StateDirectory, "foreign.json"));
        Assert.Throws<UnauthorizedAccessException>(() => new LinuxHomePublisherEnrollmentOwner(foreign, rig.Database, rig.Profiles, rig.StatePath));
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Owner.EnrollAsync(rig.Request() with { SignedCataloguePath = rig.StatePath + ".lock" }, rig.Token));
        Assert.False(File.Exists(rig.StatePath));
    }

    private sealed class Rig : IDisposable
    {
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(30));
        private readonly RSA _signer = RSA.Create(3072);
        private readonly X509Certificate2 _certificate;
        internal CancellationToken Token => _deadline.Token;
        internal string Root { get; } = Directory.CreateTempSubdirectory("linux-enrollment-TEST-ONLY-").FullName;
        internal string StateDirectory => Path.Combine(Root, "machine");
        internal string StatePath => Path.Combine(StateDirectory, "canonical.json");
        private string CertificatePath => Path.Combine(Root, "TEST-ONLY-public.cer");
        private string CataloguePath => Path.Combine(Root, "TEST-ONLY-catalogue.json");
        internal readonly HomePackageDatabase Database;
        internal readonly HomeLocalProfileIdentity Profiles;
        internal readonly LinuxHomePublisherEnrollmentOwner Owner;
        internal Rig(bool expired = false, bool codeSigning = true)
        {
            File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateDirectory(StateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var certificate = new CertificateRequest("CN=UNTRUSTED 9to1 UNIT TEST ONLY", _signer, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            certificate.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
            certificate.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(codeSigning ? "1.3.6.1.5.5.7.3.3" : "1.3.6.1.5.5.7.3.1") }, critical: true));
            certificate.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
            _certificate = certificate.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(expired ? -3 : -1), DateTimeOffset.UtcNow.AddDays(expired ? -2 : 1));
            File.WriteAllBytes(CertificatePath, _certificate.RawData);
            var store = new FileHomeCoreStateStore(StatePath); Database = new(store);
            Profiles = new(store, new OperatingSystemPrincipalSource());
            Owner = new(store, Database, Profiles, StatePath);
            WriteCatalogue();
        }
        internal LinuxHomePublisherEnrollmentOwner.Request Request() => new(CertificatePath, Digest(File.ReadAllBytes(CertificatePath)),
            CataloguePath, Digest(File.ReadAllBytes(CataloguePath)), "test.only.publisher", "arm64", ["home.pkg", "root.pkg"], Guid.NewGuid());
        internal void WriteCatalogue(bool corruptDescriptor = false, bool corruptOuter = false, string revision = "test.catalogue")
        {
            var packages = new List<NativeWindowsHomeInstallerBootstrapAdmission.CataloguePackage>();
            foreach (var app in new[] { "home", "root" })
            {
                var descriptor = new HomePackageArtifactDescriptor(1, app + ".pkg", app, "1", "stable", "native-app", "linux", "arm64",
                    [], ["home.core"], Digest([1, 2, 3]), Digest([4, 5, 6]), 3);
                var payload = JsonSerializer.SerializeToUtf8Bytes(descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var signed = Envelope(payload, corruptDescriptor && app == "home");
                packages.Add(new(Convert.ToBase64String(signed), Convert.ToBase64String(payload)));
            }
            var catalogue = new NativeWindowsHomeInstallerBootstrapAdmission.Catalogue(1, revision, "test.only.publisher", "home.pkg", "root.pkg",
                ["home.pkg", "root.pkg"], packages);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(catalogue, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.WriteAllBytes(CataloguePath, Envelope(bytes, corruptOuter));
        }
        private byte[] Envelope(byte[] payload, bool corrupt)
        {
            var signature = _signer.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            if (corrupt) signature[0] ^= 1;
            return JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, issuerKeyId = "test.only.publisher",
                payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(signature) });
        }
        public void Dispose() { _certificate.Dispose(); _signer.Dispose(); _deadline.Dispose(); Directory.Delete(Root, recursive: true); }
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
}
