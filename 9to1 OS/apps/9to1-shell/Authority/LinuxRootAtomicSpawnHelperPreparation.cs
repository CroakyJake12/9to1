using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Buffers.Binary;
using System.Runtime.Versioning;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Explicit protected signed native atomic-spawn helper selection for the administrator supervisor.
/// This is pre-launch preparation only, never proof of a running peer, remote Home lease,
/// actor, resource authorization or loaded runtime.</summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxRootAtomicSpawnHelperPreparation
{
    private const string TrustPath = "/etc/9to1/identity/publishers.json";
    private const string ReceiptsPath = "/var/lib/9to1/home/installed-receipts";
    private readonly InstallationReceipt _receipt;
    private readonly string _receiptPath;
    private readonly byte[] _envelope, _trust;
    private LinuxRootAtomicSpawnHelperPreparation(InstallationReceipt receipt, string receiptPath, byte[] envelope, byte[] trust)
    { _receipt = receipt; _receiptPath = receiptPath; _envelope = envelope; _trust = trust; }
    public string ExecutablePath => _receipt.ExecutablePath;
    public string InstallRoot => _receipt.InstallRoot;
    internal string SignedAppId => _receipt.AppId;
    internal string SignedDesktopIdentity => _receipt.OsApplicationId;
    internal string SignedDesktopEntryDigest => _receipt.DesktopEntrySha256;
    internal long SignedReceiptRevision => _receipt.ReceiptRevision;

    public static async Task<LinuxRootAtomicSpawnHelperPreparation?> ReadForAdministratorAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0" ||
            !LinuxRootOwnedFiles.DirectoryImmutable(ReceiptsPath)) return null;
        var trust = await LinuxRootOwnedFiles.ReadAsync(TrustPath, 1024 * 1024, ct);
        if (trust is null) return null;
        var issuers = InstallationReceiptSignature.DecodeTrust(trust);
        var matches = new List<LinuxRootAtomicSpawnHelperPreparation>(); var seen = 0;
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
        // Explicit native helper lane; ordinary Home/owner CLR runtime checks are unchanged.
        // The signed PRIMARY payload is the executable, not a secondary chmod exception.
        var executable = await LinuxRootOwnedFiles.ReadAsync(_receipt.ExecutablePath, 8 * 1024 * 1024, ct);
        if (executable is null || executable.Length < 64 || executable[0] != 0x7f ||
            executable[1] != (byte)'E' || executable[2] != (byte)'L' || executable[3] != (byte)'F' ||
            executable[4] != 2 || executable[5] != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(executable.AsSpan(18)) != 62) return false;
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
    internal async Task<bool> MatchesActualRootHelperAsync(Process actualPrivatelySpawnedHelper, CancellationToken ct)
    {
        if (actualPrivatelySpawnedHelper.HasExited || !await IsCurrentForAdministratorAsync(ct)) return false;
        var peer = new Haven.Application.HomeNativeObservedPeer(actualPrivatelySpawnedHelper.Id, "unix-euid:0");
        var original = await LinuxProcessIdentity.ReadAsync(peer, ct);
        if (original is null || original.ExecutablePath != _receipt.ExecutablePath ||
            !string.Equals(original.ExecutableSha256, _receipt.Files[0].Sha256, StringComparison.OrdinalIgnoreCase) ||
            !await LinuxRuntimeEvidence.HasSafeEnvironmentAsync(peer.ProcessId, ct)) return false;
        // Retain the maintained policy for native system mappings: actual protected
        // /usr/lib or /usr/lib64 files, never writable search paths/LD overrides.
        var maps = await ReadActualMapsAsync(peer.ProcessId, ct);
        if (maps is null) return false;
        var observedExecutable = false;
        foreach (var line in maps.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5) return false;
            if (fields.Length != 6)
            { if (fields[1].Contains('x')) return false; continue; }
            if (fields[5].StartsWith('['))
            {
                // Native helper has no JIT: anonymous/stack executable memory is
                // never package evidence. Only named kernel code pseudo-maps allowed.
                if (fields[1].Contains('x') && fields[5] is not ("[vdso]" or "[vsyscall]")) return false;
                continue;
            }
            var path = fields[5].Replace("\\040", " ").Replace("\\134", "\\");
            if (path.EndsWith(" (deleted)", StringComparison.Ordinal)) return false;
            if (!fields[1].Contains('x') && !path.EndsWith(".so", StringComparison.OrdinalIgnoreCase)) continue;
            if (path == _receipt.ExecutablePath) { observedExecutable = true; continue; }
            if (!(path.StartsWith("/usr/lib/", StringComparison.Ordinal) || path.StartsWith("/usr/lib64/", StringComparison.Ordinal)) ||
                !LinuxRootOwnedFiles.RegularFileImmutable(path)) return false;
        }
        return observedExecutable && !actualPrivatelySpawnedHelper.HasExited &&
            original == await LinuxProcessIdentity.ReadAsync(peer, ct) &&
            await IsCurrentForAdministratorAsync(ct) &&
            await LinuxRuntimeEvidence.HasSafeEnvironmentAsync(peer.ProcessId, ct) && !actualPrivatelySpawnedHelper.HasExited;
    }
    private static async Task<string?> ReadActualMapsAsync(int actualProcessId, CancellationToken ct)
    {
        if (actualProcessId <= 0) return null;
        // Numeric kernel process path only; proc reports st_size0, so enforce ingress
        // BEFORE copying every actual read rather than relying on file Length.
        await using var source = new FileStream($"/proc/{actualProcessId}/maps", FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite, 65536, true);
        using var bytes = new MemoryStream(); var buffer = new byte[65536];
        try
        {
            while (true)
            {
                var count = await source.ReadAsync(buffer, ct);
                if (count == 0) return Encoding.UTF8.GetString(bytes.ToArray());
                if (bytes.Length + count > 4 * 1024 * 1024) return null;
                bytes.Write(buffer, 0, count);
            }
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private static bool Eligible(InstallationReceipt? receipt) => receipt is not null &&
        receipt.AppId == "os.atomic-spawn-supervisor" && receipt.ProviderId == "linux.xdg-desktop" &&
        receipt.OsApplicationId == "desktop:9to1-os-atomic-spawn-supervisor.desktop" && receipt.Entrypoint == receipt.OsApplicationId &&
        receipt.Roles.Count == 0 && receipt.AllowedServiceIds.Count == 1 && receipt.AllowedServiceIds[0] == "os.atomic-spawn" &&
        receipt.Files.Count == 1 && Path.Combine(receipt.InstallRoot, receipt.Files[0].Path) == receipt.ExecutablePath &&
        receipt.Files[0].Size is >= 64 and <= 8 * 1024 * 1024;
}
