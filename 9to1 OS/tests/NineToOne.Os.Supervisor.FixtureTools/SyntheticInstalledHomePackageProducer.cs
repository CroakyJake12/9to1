using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using NineToOne.Os.Shell.Authority;

// HOSTED TEST TOOL ONLY. A synthetic issuer signs the ACTUAL published product bytes.
// No private key leaves memory, no release provenance is claimed, and this tool never
// writes production publisher policy, installs a package or grants a running identity.
internal static class SyntheticInstalledHomePackageProducer
{
    internal const string Command = "--produce-synthetic-home-package";
    internal const string OwnerCommand = "--produce-synthetic-widget-owner-package";
    internal const string HelperCommand = "--produce-synthetic-atomic-helper-package";
    internal static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsLinux() || args.Length != 3 || (args[0] != Command && args[0] != OwnerCommand && args[0] != HelperCommand) ||
            Environment.GetEnvironmentVariable("ASTRA_ISOLATED_SYNTHETIC_ROOT_FIXTURE") != "1")
            throw new InvalidOperationException("Explicit isolated synthetic hosted test required.");
        var publish = Canonical(args[1]); var output = Canonical(args[2]);
        var temporaryRoot = Canonical(Environment.GetEnvironmentVariable("RUNNER_TEMP")
            ?? throw new InvalidOperationException("Actual isolated hosted runner temporary directory required."));
        if (!output.StartsWith(temporaryRoot + "/", StringComparison.Ordinal) ||
            output.StartsWith(publish + "/", StringComparison.Ordinal) ||
            Directory.Exists(output) || File.Exists(output) || !Directory.Exists(publish))
            throw new InvalidDataException("Fresh separate hosted temporary output required.");
        var helperMode = args[0] == HelperCommand;
        var executable = helperMode ? "atomic-spawn" : "NineToOne.Os.Shell";
        var ownerMode = args[0] == OwnerCommand;
        var appId = helperMode ? "os.atomic-spawn-supervisor" : ownerMode ? "os.installed-application-widget-owner" : "os.shell";
        var desktopName = helperMode ? "9to1-os-atomic-spawn-supervisor.desktop" : ownerMode ? "9to1-os-installed-application-widget-owner.desktop" : "9to1-os-shell.desktop";
        var roles = ownerMode || helperMode ? Array.Empty<string>() : new[] { "home.session-host" };
        var services = helperMode ? new[] { "os.atomic-spawn" } : new[] { "home.widgets" };
        var installedRoot = "/opt/9to1/apps/" + appId;
        var desktopPath = "/usr/share/applications/" + desktopName;
        var paths = new List<string>(); var pending = new Queue<string>(); pending.Enqueue(publish);
        var directories = 0; long pathUnits = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            if (++directories > 10000 || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Bounded actual publish directories without links required.");
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Publish links refused.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (pending.Count >= 10000) throw new InvalidDataException("Publish directory queue bound exceeded.");
                    pending.Enqueue(path); continue;
                }
                if (paths.Count >= 10000) throw new InvalidDataException("Synthetic fixture publish inventory bound exceeded.");
                var relative = Path.GetRelativePath(publish, path);
                if (relative.Split('/').Any(part => part is "" or "." or "..") || relative.Contains('\\') ||
                    relative.Any(char.IsControl) || Path.GetFileName(relative).StartsWith("NineToOne.Os.Supervisor.", StringComparison.Ordinal))
                    throw new InvalidDataException("Actual product inventory excludes every supervisor fixture assembly.");
                pathUnits += Encoding.UTF8.GetByteCount(relative);
                if (pathUnits > 1024 * 1024) throw new InvalidDataException("Signed inventory text bound exceeded.");
                paths.Add(relative);
            }
        }
        paths.Sort(StringComparer.Ordinal);
        if (!paths.Contains(executable, StringComparer.Ordinal) || paths.Count == 0 || helperMode && paths.Count != 1)
            throw new InvalidDataException("Actual maintained Linux apphost required.");
        var files = new List<InstalledPayloadFile>();
        foreach (var relative in paths)
        {
            // Reuse actual regular opened-descriptor and exact-size bounded hashing paths.
            using var handle = LinuxInstalledPackageEnrollment.OpenInputHandle(Path.Combine(publish, relative));
            await using var stream = new FileStream(handle, FileAccess.Read);
            var length = stream.Length;
            if (length is < 0 or > 8L * 1024 * 1024 * 1024 || helperMode && (length < 64 || length > 8 * 1024 * 1024)) throw new InvalidDataException("Signed file size bound required.");
            if (helperMode)
            {
                var elf = new byte[64]; await stream.ReadExactlyAsync(elf);
                if (elf[0] != 0x7f || elf[1] != (byte)'E' || elf[2] != (byte)'L' || elf[3] != (byte)'F' ||
                    elf[4] != 2 || elf[5] != 1 || BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(18)) != 62)
                    throw new InvalidDataException("Actual compiled Linux64 x64 native helper ELF required.");
                stream.Position = 0;
            }
            var digest = await LinuxInstalledPackageEnrollment.HashExactSizeAsync(stream, length, default);
            files.Add(new(relative, length, Convert.ToHexString(digest)));
        }
        var desktop = Encoding.UTF8.GetBytes("[Desktop Entry]\nType=Application\nName=9to1 OS synthetic hosted test\nExec=" +
            installedRoot + "/" + executable + "\nTerminal=false\nNoDisplay=false\n");
        var receipt = new InstallationReceipt(1, appId, 1, "linux.xdg-desktop", "desktop:" + desktopName,
            "desktop:" + desktopName, installedRoot, installedRoot + "/" + executable, desktopPath,
            Convert.ToHexString(SHA256.HashData(desktop)), files, services, roles);
        using var key = RSA.Create(3072);
        var keyId = "synthetic.hosted.fixture." + Guid.NewGuid().ToString("N");
        var trust = new PublisherTrust(keyId, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            [appId], services, roles);
        var payload = InstallationReceiptSignature.EncodePayload(receipt);
        var envelope = InstallationReceiptSignature.EncodeEnvelope(new(1, keyId, Convert.ToBase64String(payload),
            Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))));
        var verified = InstallationReceiptSignature.Verify(envelope, [trust]);
        if (verified is null || !InstallationReceiptSignature.EncodePayload(verified).AsSpan().SequenceEqual(payload))
            throw new CryptographicException("Maintained verifier must accept the actual synthetic envelope.");
        Directory.CreateDirectory(output, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        await File.WriteAllBytesAsync(Path.Combine(output, "synthetic-test-trust.json"), JsonSerializer.SerializeToUtf8Bytes(new[] { trust }, json));
        await File.WriteAllBytesAsync(Path.Combine(output, helperMode ? "synthetic-atomic-helper.9to1-install" : ownerMode ? "synthetic-widget-owner.9to1-install" : "synthetic-home.9to1-install"), envelope);
        await File.WriteAllBytesAsync(Path.Combine(output, desktopName), desktop);
        await File.WriteAllBytesAsync(Path.Combine(output, "producer-receipt.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            code = "SyntheticActualPublishedPayloadSigned", issuer = "ephemeral isolated hosted TEST ONLY", keyId, appId, roles, desktopName,
            files, envelopeSha256 = Convert.ToHexString(SHA256.HashData(envelope)),
            desktopSha256 = receipt.DesktopEntrySha256, privateKeyPersisted = false,
            releasePublisherAccepted = false, installedUserAccepted = false, launched = false, runtimeAccepted = false
        }, json));
        return 0;
    }
    private static string Canonical(string path) => Path.IsPathFullyQualified(path) && Path.GetFullPath(path) == path &&
        !path.EndsWith('/') && !path.Any(char.IsControl) ? path : throw new InvalidDataException("Canonical absolute path required.");
}
