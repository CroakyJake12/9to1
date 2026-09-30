using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Shell.Tests;

public sealed class InstallationReceiptTests
{
    [Fact]
    public void ExactPayloadSignatureAndIssuerScopesAreRequired()
    {
        using var key = RSA.Create(3072);
        var receipt = Receipt(); var bytes = InstallationReceiptSignature.EncodePayload(receipt);
        var trust = new PublisherTrust("test.issuer", Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), ["os.shell"], ["home.core"], ["home.session-host"]);
        byte[] Sign(byte[] payload) => InstallationReceiptSignature.EncodeEnvelope(new(1, "test.issuer", Convert.ToBase64String(payload),
            Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))));
        var envelope = Sign(bytes);
        Assert.Equal("os.shell", InstallationReceiptSignature.Verify(envelope, [trust])?.AppId);
        Assert.Null(InstallationReceiptSignature.Verify(envelope, []));
        Assert.Null(InstallationReceiptSignature.Verify(envelope, [trust, trust]));
        Assert.Null(InstallationReceiptSignature.Verify(envelope, [trust with { AllowedRoles = [] }]));
        Assert.Null(InstallationReceiptSignature.Verify(envelope, [trust with { AllowedServiceIds = [] }]));
        var changed = bytes.ToArray(); changed[^2] ^= 1;
        var badSignature = InstallationReceiptSignature.EncodeEnvelope(new(1, "test.issuer", Convert.ToBase64String(changed),
            Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))));
        Assert.Null(InstallationReceiptSignature.Verify(badSignature, [trust]));
        // An authentic issuer still cannot turn an unsupported payload into authority.
        var unsupported = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 999, appId = "os.shell" });
        Assert.Null(InstallationReceiptSignature.Verify(Sign(unsupported), [trust]));
    }
    [Fact]
    public void UnsafeOrAmbiguousPayloadInventoryCannotBeSignedByProducer()
    {
        var original = Receipt();
        Assert.Throws<InvalidDataException>(() => InstallationReceiptSignature.EncodePayload(original with { InstallRoot = "/workspace/apps/os.shell" }));
        Assert.Throws<InvalidDataException>(() => InstallationReceiptSignature.EncodePayload(original with { Files = [original.Files[0], original.Files[0]] }));
        Assert.Throws<InvalidDataException>(() => InstallationReceiptSignature.EncodePayload(original with { Files = [new("../escape", 1, new('a', 64))] }));
        Assert.Throws<InvalidDataException>(() => InstallationReceiptSignature.EncodePayload(original with { ExecutablePath = "/usr/bin/dotnet" }));
        Assert.Throws<InvalidDataException>(() => InstallationReceiptSignature.EncodePayload(original with { Roles = ["home.session-host", "home.session-host"] }));
    }
    [Fact]
    public async Task ActualWorkspaceManagedProcessDoesNotBecomeInstalledOrSessionHostIdentity()
    {
        var verifier = new LinuxInstallationPeerVerifier(new Actors(), new Principals(), new Registry(),
            new LinuxControlledLaunchGate(new Actors(), new UnavailableHomeNativeControlledLaunchAuthority()));
        var peer = new HomeNativeObservedPeer(Environment.ProcessId, "unix-euid:" + Uid());
        Assert.Null(await verifier.VerifyAsync(peer, CancellationToken.None));
        Assert.Null(await verifier.VerifyHostAsync(peer, new("os.shell", "desktop:9to1-os-shell.desktop"), CancellationToken.None));
        Assert.Null(await verifier.VerifyHostAsync(peer, new("child.app", "desktop:child.desktop"), CancellationToken.None));
        Assert.Null(await verifier.VerifyAsync(peer with { ProcessId = int.MaxValue }, CancellationToken.None));
        Assert.Null(await verifier.VerifyAsync(peer with { OperatingSystemPrincipalId = "unix-euid:4294967295" }, CancellationToken.None));
    }
    private static InstallationReceipt Receipt() => new(1, "os.shell", 1, "linux.xdg-desktop", "desktop:9to1-os-shell.desktop",
        "desktop:9to1-os-shell.desktop", "/usr/lib/9to1/apps/os.shell", "/usr/lib/9to1/apps/os.shell/shell",
        "/usr/share/applications/9to1-os-shell.desktop", new('b', 64), [new("shell", 1024, new('a', 64))], ["home.core"], ["home.session-host"]);
    private static string Uid() => OperatingSystem.IsLinux() ? File.ReadAllLines("/proc/self/status").Single(l => l.StartsWith("Uid:")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[2] : "0";
    private sealed class Actors : IAuthenticatedResourceActorSource
    { public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(new("actor", "profile", null, null, "1")); }
    private sealed class Principals : ITrustedHostPrincipalSource
    { public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>("unix-euid:" + Uid()); }
    private sealed class Registry : IInstalledApplicationRegistry
    {
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<InstalledApplicationReference>>([]);
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct) => ValueTask.FromResult<InstalledApplicationReference?>(null);
    }
}
