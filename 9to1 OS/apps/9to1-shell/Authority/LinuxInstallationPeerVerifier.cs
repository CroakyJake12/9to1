using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Installation identity only. Resource ACLs and Home approvals remain independent, freshly checked authority.</summary>
public sealed class LinuxInstallationPeerVerifier(IAuthenticatedResourceActorSource actors,
    ITrustedHostPrincipalSource principals, IInstalledApplicationRegistry registry, LinuxControlledLaunchGate controlledLaunch)
    : IHomeNativeInstalledPeerVerifier, IHomeNativeSessionHostVerifier
{
    private const string TrustPath = "/etc/9to1/identity/publishers.json";
    private const string ReceiptsPath = "/var/lib/9to1/home/installed-receipts";
    public async ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observedPeer, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || observedPeer.ProcessId <= 0) return null;
        try
        {
            var actor = await actors.GetCurrentAsync(ct);
            if (actor is null || await principals.GetPrincipalAsync(ct) != observedPeer.OperatingSystemPrincipalId) return null;
            var process = await LinuxProcessIdentity.ReadAsync(observedPeer, ct);
            if (process is null || !await LinuxRuntimeEvidence.HasSafeEnvironmentAsync(observedPeer.ProcessId, ct) || !LinuxRootOwnedFiles.DirectoryImmutable(ReceiptsPath)) return null;
            var trustBytes = await LinuxRootOwnedFiles.ReadAsync(TrustPath, 1024 * 1024, ct);
            if (trustBytes is null) return null;
            var trust = InstallationReceiptSignature.DecodeTrust(trustBytes);
            var matches = new List<InstallationReceipt>();
            var count = 0; string? selectedPath = null; byte[]? selectedEnvelope = null;
            foreach (var file in Directory.EnumerateFiles(ReceiptsPath, "*.9to1-install", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                if (++count > 1024) return null;
                var source = await LinuxRootOwnedFiles.ReadAsync(file, 8 * 1024 * 1024, ct);
                if (source is null) return null;
                var receipt = InstallationReceiptSignature.Verify(source, trust);
                if (receipt is not null && receipt.ExecutablePath == process.ExecutablePath) { matches.Add(receipt); selectedPath = file; selectedEnvelope = source; }
            }
            if (matches.Count != 1) return null;
            var selected = matches[0];
            // No second installation database: signed platform entrypoint resolves through the ambient Home registry.
            var references = (await registry.RefreshAsync(ct)).Where(a => a.HomeProfileId == actor.ProfileId && a.Enabled && a.ProfileAccessible &&
                a.PlatformProfileId == observedPeer.OperatingSystemPrincipalId && a.ProviderId == selected.ProviderId &&
                a.OsApplicationId == selected.OsApplicationId && a.Entrypoint == selected.Entrypoint &&
                string.Equals(a.Version, selected.DesktopEntrySha256, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (references.Length != 1 || !await LinuxRuntimeEvidence.IsTrustedSelfContainedRuntimeAsync(observedPeer.ProcessId, selected, ct) || !await PayloadMatchesAsync(selected, ct) || !string.Equals(process.ExecutableSha256, selected.Files.Single(f => Path.Combine(selected.InstallRoot, f.Path) == process.ExecutablePath).Sha256, StringComparison.OrdinalIgnoreCase)) return null;
            var desktop = await LinuxRootOwnedFiles.ReadAsync(selected.DesktopEntryPath, 1024 * 1024, ct);
            if (desktop is null || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(desktop), Convert.FromHexString(selected.DesktopEntrySha256))) return null;
            var entry = LinuxInstalledApplications.Parse(Path.GetFileName(selected.DesktopEntryPath), selected.DesktopEntryPath,
                System.Text.Encoding.UTF8.GetString(desktop), selected.DesktopEntrySha256);
            if (entry is null || !entry.Enabled || "desktop:" + entry.DesktopId != selected.Entrypoint) return null;
            var current = await registry.ResolveLaunchAsync(references[0].ApplicationId, references[0].Revision, ct);
            if (current != references[0] || actor != await actors.GetCurrentAsync(ct) ||
                observedPeer.OperatingSystemPrincipalId != await principals.GetPrincipalAsync(ct) || process != await LinuxProcessIdentity.ReadAsync(observedPeer, ct) || !await LinuxRuntimeEvidence.IsTrustedSelfContainedRuntimeAsync(observedPeer.ProcessId, selected, ct)) return null;
            // Recheck the root trust policy after expensive payload verification; revocation must invalidate discovery.
            var currentEnvelope = await LinuxRootOwnedFiles.ReadAsync(selectedPath!, 8 * 1024 * 1024, ct);
            if (currentEnvelope is null || !currentEnvelope.AsSpan().SequenceEqual(selectedEnvelope)) return null;
            var currentTrust = await LinuxRootOwnedFiles.ReadAsync(TrustPath, 1024 * 1024, ct);
            if (currentTrust is null || !currentTrust.AsSpan().SequenceEqual(trustBytes)) return null;
            // Package and /proc evidence cannot prove trusted launch or exclude same-UID/JIT memory injection.
            // Every admitted app and every issuer-authorized role additionally requires independent launch authority.
            if (!await controlledLaunch.VerifyAsync(observedPeer, selected.AppId, "", ct)) return null;
            foreach (var role in selected.Roles)
                if (!await controlledLaunch.VerifyAsync(observedPeer, selected.AppId, role, ct)) return null;
            if (process != await LinuxProcessIdentity.ReadAsync(observedPeer, ct) || actor != await actors.GetCurrentAsync(ct)) return null;
            return new(selected.AppId, current.ApplicationId,
                $"receipt:{selected.ReceiptRevision.ToString(CultureInfo.InvariantCulture)};home:{current.Revision.ToString(CultureInfo.InvariantCulture)}",
                process.ExecutableIdentity(observedPeer.ProcessId),
                selected.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal)) { Roles = selected.Roles.ToFrozenSet(StringComparer.Ordinal) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or
            CryptographicException or FormatException or NotSupportedException or InvalidOperationException or EntryPointNotFoundException or DllNotFoundException)
        { return null; }
    }
    public async ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer peer,
        HomeNativeSessionHostRequirement requirement, CancellationToken ct)
    {
        // The designated Linux host is a pinned installed OS shell, never an arbitrary service-allowlisted child.
        if (requirement.AppId != "os.shell" || requirement.OperatingSystemApplicationId != "desktop:9to1-os-shell.desktop") return null;
        var identity = await VerifyAsync(peer, ct);
        if (identity?.AppId != "os.shell" || !identity.Roles.Contains(HomeNativeSessionHostRequirement.RequiredRole)) return null;
        if (!await controlledLaunch.VerifyAsync(peer, identity.AppId, HomeNativeSessionHostRequirement.RequiredRole, ct)) return null;
        var actor = await actors.GetCurrentAsync(ct);
        var matches = (await registry.RefreshAsync(ct)).Where(a => a.ApplicationId == identity.InstalledApplicationId).ToArray();
        if (matches.Length != 1 || actor is null) return null;
        var app = matches[0];
        return app is { ProviderId: "linux.xdg-desktop", Enabled: true, ProfileAccessible: true } &&
            app.HomeProfileId == actor.ProfileId && app.OsApplicationId == requirement.OperatingSystemApplicationId &&
            identity.InstallationRevision.EndsWith(";home:" + app.Revision.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
            actor == await actors.GetCurrentAsync(ct) ? identity : null;
    }
    private static async Task<bool> PayloadMatchesAsync(InstallationReceipt receipt, CancellationToken ct)
    {
        if (!LinuxRootOwnedFiles.DirectoryImmutable(receipt.InstallRoot)) return false;
        var expected = receipt.Files.Select(f => Path.Combine(receipt.InstallRoot, f.Path)).ToHashSet(StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(); pending.Enqueue(receipt.InstallRoot); var directoryCount = 0;
        while (pending.TryDequeue(out var directory))
        {
            if (++directoryCount > 10000 || !LinuxRootOwnedFiles.DirectoryImmutable(directory)) return false;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                ct.ThrowIfCancellationRequested(); var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                if ((attributes & FileAttributes.Directory) != 0) { if (pending.Count > 10000) return false; pending.Enqueue(entry); }
                else if (actual.Count >= 100000 || !actual.Add(entry) || !expected.Contains(entry)) return false;
            }
        }
        if (!actual.SetEquals(expected)) return false;
        foreach (var file in receipt.Files)
            if (!await LinuxRootOwnedFiles.MatchesAsync(Path.Combine(receipt.InstallRoot, file.Path), file.Size, file.Sha256, ct,
                Path.Combine(receipt.InstallRoot, file.Path) == receipt.ExecutablePath)) return false;
        return true;
    }
}

internal sealed record LinuxProcessIdentity(string ExecutablePath, string StartTime, string ExecutableSha256)
{
    internal string ExecutableIdentity(int processId) => $"{ExecutablePath};sha256:{ExecutableSha256};pid:{processId};start:{StartTime}";
    internal static async Task<LinuxProcessIdentity?> ReadAsync(HomeNativeObservedPeer peer, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || peer.ProcessId <= 0 || !peer.OperatingSystemPrincipalId.StartsWith("unix-euid:", StringComparison.Ordinal) ||
            !uint.TryParse(peer.OperatingSystemPrincipalId.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture, out var expectedUid)) return null;
        var root = "/proc/" + peer.ProcessId.ToString(CultureInfo.InvariantCulture);
        var status = File.ReadAllText(root + "/status"); if (status.Length > 65536) return null;
        var uidLine = status.Split('\n').SingleOrDefault(l => l.StartsWith("Uid:", StringComparison.Ordinal));
        var values = uidLine?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (values is not { Length: 5 } || !uint.TryParse(values[2], out var effective) || effective != expectedUid) return null;
        var stat = File.ReadAllText(root + "/stat"); if (stat.Length > 65536) return null;
        var end = stat.LastIndexOf(')'); if (end < 0) return null;
        var fields = stat[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 20 || !ulong.TryParse(fields[19], out _)) return null;
        var executable = new FileInfo(root + "/exe").LinkTarget;
        if (executable is null || executable.EndsWith(" (deleted)", StringComparison.Ordinal) || !Path.IsPathFullyQualified(executable)) return null;
        await using var mappedExecutable = new FileStream(root + "/exe", FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (mappedExecutable.Length is < 4 or > 512L * 1024 * 1024) return null;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(mappedExecutable, ct));
        return new(executable, fields[19], digest);
    }
}
