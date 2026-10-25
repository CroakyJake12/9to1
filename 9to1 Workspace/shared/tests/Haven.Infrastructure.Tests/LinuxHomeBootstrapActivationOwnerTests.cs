using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Infrastructure.Native.Linux;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Real root-owned isolated filesystem, canonical store/OS profile and actual
/// ELF bytes with ephemeral memory-only TEST signatures. No service, package manager,
/// app process, OS trust store or production enrollment is invoked by these tests.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHomeBootstrapActivationOwnerTests
{
    private sealed class LinuxAdminFactAttribute : FactAttribute
    { public LinuxAdminFactAttribute() { if (!OperatingSystem.IsLinux() || GetEuid() != 0) Skip = "Requires actual Linux UID0 isolated file fixtures."; } }
    private sealed class LinuxAdminTheoryAttribute : TheoryAttribute
    { public LinuxAdminTheoryAttribute() { if (!OperatingSystem.IsLinux() || GetEuid() != 0) Skip = "Requires actual Linux UID0 isolated file fixtures."; } }

    [LinuxAdminFact]
    public async Task Signed_complete_bundle_is_published_once_and_canonical_rows_explicitly_keep_runtime_unavailable()
    {
        using var rig = new Rig(); await rig.Enroll();
        var result = await rig.Owner.ActivateAsync(rig.Request(), rig.Token);
        Assert.Equal(rig.Operation, result.OperationId);
        Assert.False(Directory.Exists(rig.Stage));
        Assert.Equal(Path.Combine(rig.Code, "active"), result.ActivePath);
        var registry = (await rig.Database.ReadAsync(rig.Token)).Snapshot!;
        Assert.Equal(2, registry.Packages.Count); Assert.Equal(2, registry.RecentOperations.Count);
        foreach (var package in registry.Packages)
        {
            Assert.Equal(HomePackageInstallState.Installed, package.InstallationState);
            Assert.Equal(HomePackageCompatibility.HomeServiceUnavailable, package.Compatibility);
            var activation = package.UnknownFields![HomePackageOriginalInstalledActivationRecord.Field].Deserialize<HomePackageOriginalInstalledActivationRecord>()!;
            Assert.Equal(rig.Operation.ToString("D"), activation.OriginalRootOperationId);
            Assert.StartsWith(result.ActivePath + "/", activation.InstallationRoot);
            Assert.Equal(rig.Elf, File.ReadAllBytes(Path.Combine(activation.InstallationRoot, activation.EntrypointRelativePath)));
            Assert.Equal(Mode755, File.GetUnixFileMode(activation.InstallationRoot));
            Assert.Equal(Mode755, File.GetUnixFileMode(Path.Combine(activation.InstallationRoot, "app")));
            Assert.Equal(Mode644, File.GetUnixFileMode(activation.SignedReceiptPath));
            Assert.Equal(Mode644, File.GetUnixFileMode(activation.CompletePayloadPath));
        }
        Assert.All(registry.RecentOperations, item => {
            Assert.Equal(HomePackageJournalState.Succeeded, item.State);
            Assert.Equal(rig.Operation.ToString("D"), item.OperationId);
            Assert.Contains("installed-peer-not-established", item.SkippedSteps);
            Assert.Contains("linux.original-bootstrap-publication.v1", item.UnknownFields!.Keys);
        });
        Assert.Equal(Mode700, File.GetUnixFileMode(rig.StateDirectory));
        Assert.Equal(Mode600, File.GetUnixFileMode(rig.StatePath));
        Assert.Equal(Mode600, File.GetUnixFileMode(rig.StatePath + ".lock"));
        Assert.Equal(Mode600, File.GetUnixFileMode(rig.CertificatePath));
        var state = File.ReadAllBytes(rig.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.ActivateAsync(rig.Request(), rig.Token));
        Assert.Equal(state, File.ReadAllBytes(rig.StatePath));
        Assert.Equal(rig.Elf, File.ReadAllBytes(Path.Combine(result.ActivePath, "home", "app")));
    }

    [LinuxAdminFact]
    public async Task Missing_enrollment_cannot_create_initial_trust_or_initialize_a_machine_profile()
    {
        using var rig = new Rig();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.ActivateAsync(rig.Request(), rig.Token));
        Assert.False(File.Exists(rig.StatePath)); Assert.False(File.Exists(rig.StatePath + ".lock"));
        Assert.Equal(Mode700, File.GetUnixFileMode(rig.Stage)); Assert.False(Directory.Exists(Path.Combine(rig.Code, "active")));
    }

    [LinuxAdminTheory]
    [InlineData("receipt-signature")]
    [InlineData("receipt-hash")]
    [InlineData("runtime-hash")]
    [InlineData("payload-hash")]
    [InlineData("extra-file")]
    [InlineData("script")]
    [InlineData("dependency")]
    [InlineData("opaque-version")]
    public async Task Untrusted_or_incomplete_bundle_is_refused_before_any_journal_or_public_permission_effect(string damage)
    {
        using var rig = new Rig(damage); await rig.Enroll();
        if (damage == "receipt-hash") File.AppendAllText(Path.Combine(rig.Stage, "home", "receipt.json"), " ");
        if (damage == "runtime-hash") File.WriteAllBytes(Path.Combine(rig.Stage, "home", "app"), [1, 2, 3]);
        if (damage == "payload-hash") File.WriteAllBytes(Path.Combine(rig.Stage, "home", "payload.bin"), [7, 8, 9]);
        if (damage == "extra-file") Write(Path.Combine(rig.Stage, "root", "foreign.txt"), [4]);
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Owner.ActivateAsync(rig.Request(), rig.Token));
        var registry = (await rig.Database.ReadAsync(rig.Token)).Snapshot!;
        Assert.Empty(registry.Packages); Assert.Empty(registry.RecentOperations);
        Assert.Equal(Mode700, File.GetUnixFileMode(rig.Stage));
        Assert.Equal(Mode600, File.GetUnixFileMode(Path.Combine(rig.Stage, "home", "receipt.json")));
        Assert.False(Directory.Exists(Path.Combine(rig.Code, "active")));
    }

    [LinuxAdminFact]
    public async Task Existing_foreign_active_bundle_is_preserved_with_no_journal_or_stage_mode_change()
    {
        using var rig = new Rig(); await rig.Enroll();
        var active = Path.Combine(rig.Code, "active"); Directory.CreateDirectory(active, Mode700);
        Write(Path.Combine(active, "foreign"), [9, 8, 7]);
        await Assert.ThrowsAsync<IOException>(() => rig.Owner.ActivateAsync(rig.Request(), rig.Token));
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(Path.Combine(active, "foreign")));
        Assert.Equal(Mode700, File.GetUnixFileMode(rig.Stage));
        Assert.Empty((await rig.Database.ReadAsync(rig.Token)).Snapshot!.RecentOperations);
    }

    [LinuxAdminFact]
    public async Task State_directory_or_inputs_cannot_be_selected_as_public_code_parent()
    {
        using var rig = new Rig(); await rig.Enroll();
        var state = File.ReadAllBytes(rig.StatePath);
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Owner.ActivateAsync(rig.Request() with { ExistingActivationParent = rig.Root }, rig.Token));
        Assert.Equal(state, File.ReadAllBytes(rig.StatePath));
        Assert.Equal(Mode700, File.GetUnixFileMode(rig.Root)); Assert.Equal(Mode700, File.GetUnixFileMode(rig.StateDirectory));
    }

    [LinuxAdminFact]
    public async Task Redirected_stage_or_runtime_file_cannot_be_adopted_as_original_code()
    {
        using var rig = new Rig(); await rig.Enroll();
        var original = Path.Combine(rig.Stage, "home", "app"); File.Delete(original);
        File.CreateSymbolicLink(original, "/usr/bin/true");
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Owner.ActivateAsync(rig.Request(), rig.Token));
        Assert.NotNull(new FileInfo(original).LinkTarget);
        Assert.Equal(Mode700, File.GetUnixFileMode(rig.Stage));
        Assert.Empty((await rig.Database.ReadAsync(rig.Token)).Snapshot!.RecentOperations);
    }

    [LinuxAdminTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_failure_after_publication_recovers_same_operation_without_repeating_namespace_effects(bool acknowledgementLost)
    {
        using var rig = new Rig(failAfterPublication: !acknowledgementLost, loseSettlementAcknowledgement: acknowledgementLost);
        await rig.Enroll();
        var failure = await Assert.ThrowsAsync<LinuxHomeBootstrapActivationOwner.ActivationUnconfirmedException>(() => rig.Owner.ActivateAsync(rig.Request(), rig.Token));
        Assert.Equal(rig.Operation, failure.OperationId);
        var active = Path.Combine(rig.Code, "active"); Assert.Equal(active, failure.ObservedActivePath);
        Assert.True(Directory.Exists(active)); Assert.False(Directory.Exists(rig.Stage));
        var before = (await rig.Database.ReadAsync(rig.Token)).Snapshot!;
        Assert.All(before.RecentOperations, journal => Assert.Equal(acknowledgementLost ? HomePackageJournalState.Succeeded : HomePackageJournalState.Validated, journal.State));
        Assert.Equal(acknowledgementLost ? 2 : 0, before.Packages.Count);
        var directoryStamp = Directory.GetLastWriteTimeUtc(active);
        var recovered = await rig.Owner.RecoverAsync(rig.Request(), rig.Token);
        Assert.Equal(acknowledgementLost, recovered.AlreadySettled);
        Assert.Equal(rig.Operation, recovered.OperationId); Assert.Equal(active, recovered.ActivePath);
        Assert.Equal(directoryStamp, Directory.GetLastWriteTimeUtc(active));
        Assert.False(Directory.Exists(rig.Stage));
        Assert.Equal(rig.Elf, File.ReadAllBytes(Path.Combine(active, "home", "app")));
        var after = (await rig.Database.ReadAsync(rig.Token)).Snapshot!;
        Assert.Equal(2, after.Packages.Count); Assert.All(after.RecentOperations, journal => Assert.Equal(HomePackageJournalState.Succeeded, journal.State));
        var bytes = File.ReadAllBytes(rig.StatePath);
        var again = await rig.Owner.RecoverAsync(rig.Request(), rig.Token);
        Assert.True(again.AlreadySettled); Assert.Equal(after.Revision, again.RegistryRevision);
        Assert.Equal(bytes, File.ReadAllBytes(rig.StatePath));
    }

    [LinuxAdminTheory]
    [InlineData("wrong-operation")]
    [InlineData("changed-plan")]
    [InlineData("changed-runtime")]
    [InlineData("changed-mode")]
    [InlineData("missing-active")]
    public async Task Pending_recovery_never_adopts_different_or_missing_published_source(string damage)
    {
        using var rig = new Rig(failAfterPublication: true); await rig.Enroll();
        await Assert.ThrowsAsync<LinuxHomeBootstrapActivationOwner.ActivationUnconfirmedException>(() => rig.Owner.ActivateAsync(rig.Request(), rig.Token));
        var request = rig.Request(); var active = Path.Combine(rig.Code, "active");
        if (damage == "wrong-operation") request = request with { SigningInputs = request.SigningInputs with { OperationId = Guid.NewGuid() } };
        if (damage == "changed-plan") File.AppendAllText(rig.PlanPath, " ");
        if (damage == "changed-runtime") File.WriteAllBytes(Path.Combine(active, "home", "app"), [9, 8]);
        if (damage == "changed-mode") File.SetUnixFileMode(Path.Combine(active, "home"), Mode700);
        if (damage == "missing-active") Directory.Move(active, rig.Stage);
        var before = File.ReadAllBytes(rig.StatePath);
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Owner.RecoverAsync(request, rig.Token));
        Assert.Equal(before, File.ReadAllBytes(rig.StatePath));
        var registry = (await rig.Database.ReadAsync(rig.Token)).Snapshot!;
        Assert.Empty(registry.Packages); Assert.All(registry.RecentOperations, item => Assert.Equal(HomePackageJournalState.Validated, item.State));
        Assert.Equal(damage != "missing-active", Directory.Exists(active));
    }

    [LinuxAdminFact]
    public async Task An_enrolled_registry_missing_its_original_OS_profile_is_preserved_without_first_run_repair()
    {
        using var rig = new Rig(); await rig.Enroll();
        var document = JsonNode.Parse(File.ReadAllBytes(rig.StatePath))!;
        var records = document["Records"]!.AsArray();
        var profile = records.Single(value => value!["RecordId"]!.GetValue<string>() == "home.local-profile");
        records.Remove(profile); Write(rig.StatePath, JsonSerializer.SerializeToUtf8Bytes(document));
        var before = File.ReadAllBytes(rig.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.ActivateAsync(rig.Request(), rig.Token));
        Assert.Equal(before, File.ReadAllBytes(rig.StatePath)); Assert.Equal(Mode700, File.GetUnixFileMode(rig.Stage));
    }

    private sealed class Rig : IDisposable
    {
        private readonly RSA _signer = RSA.Create(3072);
        private readonly X509Certificate2 _certificate;
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(40));
        internal string Root { get; } = Directory.CreateTempSubdirectory("linux-bootstrap-TEST-ONLY-").FullName;
        internal string Code => Path.Combine(Root, "code");
        internal string Stage => Path.Combine(Code, "prepared");
        internal string StateDirectory => Path.Combine(Root, "state");
        internal string StatePath => Path.Combine(StateDirectory, "canonical.json");
        internal string CertificatePath => Path.Combine(Root, "TEST-ONLY-public.cer");
        private string CataloguePath => Path.Combine(Root, "TEST-ONLY-catalogue.json");
        internal string PlanPath => Path.Combine(Root, "plan.json");
        internal Guid Operation { get; } = Guid.NewGuid();
        internal CancellationToken Token => _deadline.Token;
        internal byte[] Elf { get; } = File.ReadAllBytes("/usr/bin/true");
        internal HomePackageDatabase Database { get; }
        private LinuxHomePublisherEnrollmentOwner Publisher { get; }
        internal LinuxHomeBootstrapActivationOwner Owner { get; }
        private readonly string _abi = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        internal Rig(string? damage = null, bool failAfterPublication = false, bool loseSettlementAcknowledgement = false)
        {
            File.SetUnixFileMode(Root, Mode700); Directory.CreateDirectory(Code, Mode755);
            Directory.CreateDirectory(Stage, Mode700); Directory.CreateDirectory(StateDirectory, Mode700);
            var certificate = new CertificateRequest("CN=UNTRUSTED BOOTSTRAP UNIT TEST ONLY", _signer, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            certificate.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            certificate.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.3") }, true));
            certificate.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            _certificate = certificate.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
            Write(CertificatePath, _certificate.RawData);
            var packages = new List<NativeWindowsHomeInstallerBootstrapAdmission.CataloguePackage>();
            var layouts = new List<LinuxHomeBootstrapActivationOwner.PackageLayout>();
            foreach (var app in new[] { "home", "root" })
            {
                var directory = Path.Combine(Stage, app); Directory.CreateDirectory(directory, Mode700);
                var executable = damage == "script" && app == "home" ? "#!/bin/sh\nexit 0\n"u8.ToArray() : Elf;
                Write(Path.Combine(directory, "app"), executable); File.SetUnixFileMode(Path.Combine(directory, "app"), Mode700);
                byte[] payload = [4, 5, 6]; Write(Path.Combine(directory, "payload.bin"), payload);
                var version = damage == "opaque-version" ? "1" : "1.0.0";
                var receipt = new LinuxHomeBootstrapActivationOwner.Receipt(1, app + ".pkg", app, version, "linux", _abi, "app",
                    [new("app", executable.Length, Digest(executable))]);
                var signedReceipt = Envelope(JsonSerializer.SerializeToUtf8Bytes(receipt, Web), corrupt: damage == "receipt-signature" && app == "home");
                Write(Path.Combine(directory, "receipt.json"), signedReceipt);
                var descriptor = new HomePackageArtifactDescriptor(1, app + ".pkg", app, version, "stable", "mandatory-shared-core", "linux", _abi,
                    damage == "dependency" && app == "home" ? [new("absent.pkg", "1.0.0")] : [], ["home.core"], Digest(signedReceipt), Digest(payload), payload.Length);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(descriptor, Web);
                packages.Add(new(Convert.ToBase64String(Envelope(bytes)), Convert.ToBase64String(bytes)));
                layouts.Add(new(app + ".pkg", app, "receipt.json", "payload.bin"));
            }
            var catalogue = new NativeWindowsHomeInstallerBootstrapAdmission.Catalogue(1, "test.bootstrap", "test.only.publisher", "home.pkg", "root.pkg", ["home.pkg", "root.pkg"], packages);
            Write(CataloguePath, Envelope(JsonSerializer.SerializeToUtf8Bytes(catalogue, Web)));
            Write(PlanPath, JsonSerializer.SerializeToUtf8Bytes(new LinuxHomeBootstrapActivationOwner.Plan(1, layouts), Web));
            var store = loseSettlementAcknowledgement
                ? new FileHomeCoreStateStore(StatePath, new LoseActualSettlementAcknowledgement())
                : new FileHomeCoreStateStore(StatePath);
            Database = new(store);
            ITrustedHostPrincipalSource principals = new FailOnceAfterPublicationPrincipal(Path.Combine(Code, "active"), failAfterPublication);
            Publisher = new(store, Database, new HomeLocalProfileIdentity(store, principals), StatePath);
            Owner = new(Publisher);
        }
        internal LinuxHomeBootstrapActivationOwner.Request Request() => new(new(CertificatePath, Digest(_certificate.RawData),
            CataloguePath, Digest(File.ReadAllBytes(CataloguePath)), "test.only.publisher", _abi, ["home.pkg", "root.pkg"], Operation), Code, "prepared", PlanPath);
        internal Task Enroll() => Publisher.EnrollAsync(Request().SigningInputs, Token);
        private byte[] Envelope(byte[] payload, bool corrupt = false)
        {
            var signature = _signer.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            if (corrupt) signature[0] ^= 1;
            return JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, issuerKeyId = "test.only.publisher", payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(signature) });
        }
        public void Dispose() { _certificate.Dispose(); _signer.Dispose(); _deadline.Dispose(); Directory.Delete(Root, recursive: true); }
    }
    // Delegates every identity read to the actual OS source. This injects one
    // refusal after the real bundle rename, never a fabricated successful actor.
    private sealed class FailOnceAfterPublicationPrincipal(string active, bool armed) : ITrustedHostPrincipalSource
    {
        private int _armed = armed ? 1 : 0;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            if (Directory.Exists(active) && Interlocked.Exchange(ref _armed, 0) == 1)
                throw new IOException("TEST fault: actual profile guard unavailable after real bundle publication.");
            return new OperatingSystemPrincipalSource().GetPrincipalAsync(token);
        }
    }
    // Existing actual-store fixture seam: physical canonical persistence succeeds,
    // then its acknowledgement is lost. The persisted source is never substituted.
    private sealed class LoseActualSettlementAcknowledgement : IHomePersistedWriteObserver
    {
        private bool _thrown;
        public Task OnPersistedWriteAsync(string recordId, long revision, CancellationToken token)
        {
            if (!_thrown && recordId == HomePackageDatabase.RecordId && revision == 3)
            { _thrown = true; throw new IOException("TEST fault: actual settlement publication acknowledgement lost."); }
            return Task.CompletedTask;
        }
    }
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Write(string path, byte[] bytes) { File.WriteAllBytes(path, bytes); File.SetUnixFileMode(path, Mode600); }
    private const UnixFileMode Mode700 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode Mode600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode Mode644 = Mode600 | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode Mode755 = Mode700 | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
}
