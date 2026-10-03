using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.Versioning;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Actual protected independent installed widget-owner selection for the administrator supervisor.
/// This is pre-launch preparation only, never proof of a running peer, remote Home lease,
/// actor, resource authorization or loaded runtime.</summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxRootInstalledWidgetOwnerStartPreparation
{
    private const string TrustPath = "/etc/9to1/identity/publishers.json";
    private const string ReceiptsPath = "/var/lib/9to1/home/installed-receipts";
    private readonly InstallationReceipt _receipt;
    private readonly string _receiptPath;
    private readonly byte[] _envelope, _trust;
    private LinuxRootInstalledWidgetOwnerStartPreparation(InstallationReceipt receipt, string receiptPath, byte[] envelope, byte[] trust)
    { _receipt = receipt; _receiptPath = receiptPath; _envelope = envelope; _trust = trust; }
    public string ExecutablePath => _receipt.ExecutablePath;
    public string InstallRoot => _receipt.InstallRoot;
    internal string SignedAppId => _receipt.AppId;
    internal string SignedDesktopIdentity => _receipt.OsApplicationId;
    internal string SignedDesktopEntryDigest => _receipt.DesktopEntrySha256;
    internal long SignedReceiptRevision => _receipt.ReceiptRevision;

    public static async Task<LinuxRootInstalledWidgetOwnerStartPreparation?> ReadForAdministratorAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0" ||
            !LinuxRootOwnedFiles.DirectoryImmutable(ReceiptsPath)) return null;
        var trust = await LinuxRootOwnedFiles.ReadAsync(TrustPath, 1024 * 1024, ct);
        if (trust is null) return null;
        var issuers = InstallationReceiptSignature.DecodeTrust(trust);
        var matches = new List<LinuxRootInstalledWidgetOwnerStartPreparation>(); var seen = 0;
        foreach (var path in Directory.EnumerateFiles(ReceiptsPath, "*.9to1-install", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested(); if (++seen > 1024) return null;
            var envelope = await LinuxRootOwnedFiles.ReadAsync(path, 8 * 1024 * 1024, ct);
            if (envelope is null) return null;
            var receipt = InstallationReceiptSignature.Verify(envelope, issuers);
            if (!Eligible(receipt)) continue;
            if (receipt is null) return null;
            matches.Add(new(receipt, path, envelope, trust));
        }
        if (matches.Count != 1) return null;
        var selected = matches[0];
        return await selected.IsCurrentForAdministratorAsync(ct) ? selected : null;
    }

    public async Task<bool> IsCurrentForAdministratorAsync(CancellationToken ct)
    {
        if (await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0") return false;
        var trust = await LinuxRootOwnedFiles.ReadAsync(TrustPath, 1024 * 1024, ct);
        var envelope = await LinuxRootOwnedFiles.ReadAsync(_receiptPath, 8 * 1024 * 1024, ct);
        if (trust is null || envelope is null || !trust.AsSpan().SequenceEqual(_trust) || !envelope.AsSpan().SequenceEqual(_envelope) ||
            !await LinuxInstallationPeerVerifier.PayloadMatchesAsync(_receipt, ct)) return false;
        var desktop = await LinuxRootOwnedFiles.ReadAsync(_receipt.DesktopEntryPath, 1024 * 1024, ct);
        if (desktop is null || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(desktop), Convert.FromHexString(_receipt.DesktopEntrySha256))) return false;
        var entry = LinuxInstalledApplications.Parse(Path.GetFileName(_receipt.DesktopEntryPath), _receipt.DesktopEntryPath,
            System.Text.Encoding.UTF8.GetString(desktop), _receipt.DesktopEntrySha256);
        if (entry is null || !entry.Enabled || "desktop:" + entry.DesktopId != _receipt.Entrypoint) return false;
        // Parsing checks the signed registration's enabled/ID observation only. Its
        // Exec/TryExec is not a command attestation: the supervisor must execute the
        // signed absolute ExecutablePath directly with fixed arguments/environment.
        // Use the same maintained self-contained config shape as runtime verification.
        // Actual mapped core/host libraries remain a separate post-start requirement.
        var name = Path.GetFileName(_receipt.ExecutablePath);
        var required = new[] { name + ".runtimeconfig.json", name + ".deps.json", "libcoreclr.so", "libhostfxr.so", "libhostpolicy.so" };
        if (required.Any(file => !_receipt.Files.Any(item => item.Path == file))) return false;
        var config = await LinuxRootOwnedFiles.ReadAsync(Path.Combine(_receipt.InstallRoot, required[0]), 1024 * 1024, ct);
        if (config is null) return false;
        using var document = JsonDocument.Parse(config, new JsonDocumentOptions { MaxDepth = 24 });
        if (!document.RootElement.TryGetProperty("runtimeOptions", out var options) || options.ValueKind != JsonValueKind.Object ||
            options.TryGetProperty("framework", out _) || options.TryGetProperty("frameworks", out _) ||
            options.TryGetProperty("additionalProbingPaths", out _) ||
            !options.TryGetProperty("includedFrameworks", out var included) || included.ValueKind != JsonValueKind.Array || included.GetArrayLength() == 0) return false;
        if (!LinuxRootOwnedFiles.DirectoryImmutable(ReceiptsPath)) return false;
        var issuers = InstallationReceiptSignature.DecodeTrust(trust);
        var eligibleCount = 0; var seen = 0;
        foreach (var path in Directory.EnumerateFiles(ReceiptsPath, "*.9to1-install", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested(); if (++seen > 1024) return false;
            var candidate = await LinuxRootOwnedFiles.ReadAsync(path, 8 * 1024 * 1024, ct);
            if (candidate is null) return false;
            if (!Eligible(InstallationReceiptSignature.Verify(candidate, issuers))) continue;
            if (++eligibleCount != 1 || path != _receiptPath || !candidate.AsSpan().SequenceEqual(_envelope)) return false;
        }
        if (eligibleCount != 1) return false;
        var finalTrust = await LinuxRootOwnedFiles.ReadAsync(TrustPath, 1024 * 1024, ct);
        var finalEnvelope = await LinuxRootOwnedFiles.ReadAsync(_receiptPath, 8 * 1024 * 1024, ct);
        return finalTrust is not null && finalEnvelope is not null && finalTrust.AsSpan().SequenceEqual(_trust) &&
            finalEnvelope.AsSpan().SequenceEqual(_envelope) &&
            await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) == "unix-euid:0";
    }
    internal async Task<bool> MatchesActualRuntimeAsync(Haven.Application.HomeNativeObservedPeer actualPeer, CancellationToken ct)
    {
        var process = await LinuxProcessIdentity.ReadAsync(actualPeer, ct);
        return process is not null && process.ExecutablePath == _receipt.ExecutablePath &&
            string.Equals(process.ExecutableSha256,
                _receipt.Files.Single(file => Path.Combine(_receipt.InstallRoot, file.Path) == _receipt.ExecutablePath).Sha256,
                StringComparison.OrdinalIgnoreCase) &&
            await LinuxRuntimeEvidence.IsTrustedSelfContainedRuntimeAsync(actualPeer.ProcessId, _receipt, ct);
    }
    private static bool Eligible(InstallationReceipt? receipt) => receipt is not null &&
        receipt.AppId == "os.installed-application-widget-owner" && receipt.ProviderId == "linux.xdg-desktop" &&
        receipt.OsApplicationId == "desktop:9to1-os-installed-application-widget-owner.desktop" && receipt.Entrypoint == receipt.OsApplicationId &&
        !receipt.Roles.Contains("home.session-host", StringComparer.Ordinal) &&
        receipt.AllowedServiceIds.Contains("home.widgets", StringComparer.Ordinal);
}
