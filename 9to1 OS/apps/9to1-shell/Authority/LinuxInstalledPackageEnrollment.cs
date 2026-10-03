using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Explicit administrator enrollment of an existing signed package. This never creates
/// publisher trust or keys, launches a process, approves Home data, or replaces an active install.</summary>
[SupportedOSPlatform("linux")]
public static class LinuxInstalledPackageEnrollment
{
    public const string PublisherTrustPath = "/etc/9to1/identity/publishers.json";
    public const string InstalledReceiptsPath = "/var/lib/9to1/home/installed-receipts";
    private const int NoFollow = 0x20000, CloseOnExec = 0x80000, NonBlocking = 0x800;
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct DescriptorStat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out DescriptorStat stat);

    public static async Task<InstallationReceipt> EnrollFirstInstallAsync(string actualPublishDirectory,
        byte[] signedEnvelope, byte[] actualDesktopEntry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signedEnvelope); ArgumentNullException.ThrowIfNull(actualDesktopEntry);
        if (signedEnvelope.Length is < 1 or > 8 * 1024 * 1024 || actualDesktopEntry.Length is < 1 or > 1024 * 1024)
            throw new InvalidDataException("Bounded signed package and actual desktop entry required.");
        var envelope = signedEnvelope.ToArray(); var desktop = actualDesktopEntry.ToArray();
        if (!OperatingSystem.IsLinux() || await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0")
            throw new UnauthorizedAccessException("Installed package enrollment requires the actual administrator process.");
        var trustBytes = await LinuxRootOwnedFiles.ReadAsync(PublisherTrustPath, 1024 * 1024, ct)
            ?? throw new UnauthorizedAccessException("Existing protected publisher enrollment policy required.");
        var receipt = InstallationReceiptSignature.Verify(envelope, InstallationReceiptSignature.DecodeTrust(trustBytes))
            ?? throw new UnauthorizedAccessException("Package signature or issuer installation scope is not permitted.");
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(desktop), Convert.FromHexString(receipt.DesktopEntrySha256)))
            throw new InvalidDataException("Actual desktop entry does not match its signed receipt.");
        var parsed = LinuxInstalledApplications.Parse(Path.GetFileName(receipt.DesktopEntryPath), receipt.DesktopEntryPath,
            System.Text.Encoding.UTF8.GetString(desktop), receipt.DesktopEntrySha256);
        if (receipt.ProviderId != "linux.xdg-desktop" || parsed is null || !parsed.Enabled ||
            receipt.Entrypoint != "desktop:" + parsed.DesktopId || receipt.OsApplicationId != receipt.Entrypoint)
            throw new InvalidDataException("Actual desktop registration does not match the canonical signed platform identity.");
        await ValidateSourceInventoryAsync(actualPublishDirectory, receipt, ct);
        EnsureProtectedDirectory(Path.GetDirectoryName(receipt.InstallRoot)!);
        EnsureProtectedDirectory(Path.GetDirectoryName(receipt.DesktopEntryPath)!);
        EnsureProtectedDirectory(InstalledReceiptsPath);
        var lockPath = Path.Combine(InstalledReceiptsPath, ".enrollment.lock");
        if (Exists(lockPath) && !LinuxRootOwnedFiles.RegularFileImmutable(lockPath))
            throw new UnauthorizedAccessException("Enrollment lock is not a protected regular file.");
        using var enrollmentLease = new FileStream(lockPath, new FileStreamOptions { Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite, Share = FileShare.None, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        var receiptPath = Path.Combine(InstalledReceiptsPath, receipt.AppId + "." + receipt.ReceiptRevision + ".9to1-install");
        if (Exists(receipt.InstallRoot) || Exists(receipt.DesktopEntryPath) || Exists(receiptPath))
            throw new IOException("First-install enrollment cannot replace existing package, desktop entry or receipt.");
        var stage = Path.Combine(Path.GetDirectoryName(receipt.InstallRoot)!, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var desktopStage = receipt.DesktopEntryPath + ".pending-" + Guid.NewGuid().ToString("N");
        var receiptStage = receiptPath + ".pending-" + Guid.NewGuid().ToString("N");
        var installed = false; var desktopPublished = false;
        try
        {
            foreach (var file in receipt.Files)
            {
                ct.ThrowIfCancellationRequested(); var destination = Path.Combine(stage, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                await CopyVerifiedAsync(Path.Combine(actualPublishDirectory, file.Path), destination, file, ct);
            }
            // Recheck the actual protected issuer policy immediately before publishing signed content.
            var currentTrust = await LinuxRootOwnedFiles.ReadAsync(PublisherTrustPath, 1024 * 1024, ct);
            if (currentTrust is null || !currentTrust.AsSpan().SequenceEqual(trustBytes))
                throw new UnauthorizedAccessException("Publisher enrollment changed before package publication.");
            foreach (var file in receipt.Files)
                File.SetUnixFileMode(Path.Combine(stage, file.Path), UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead |
                    (Path.Combine(receipt.InstallRoot, file.Path) == receipt.ExecutablePath
                        ? UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute : 0));
            foreach (var directory in Directory.EnumerateDirectories(stage, "*", SearchOption.AllDirectories).Append(stage))
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            await WriteNewAsync(desktopStage, desktop, ct); await WriteNewAsync(receiptStage, envelope, ct);
            Directory.Move(stage, receipt.InstallRoot); installed = true;
            File.Move(desktopStage, receipt.DesktopEntryPath, false); desktopPublished = true;
            File.Move(receiptStage, receiptPath, false);
            return receipt;
        }
        catch
        {
            // Only entries created by this exact first-install operation are rolled back.
            if (desktopPublished) File.Delete(receipt.DesktopEntryPath);
            if (installed) Directory.Delete(receipt.InstallRoot, true);
            throw;
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            if (File.Exists(desktopStage)) File.Delete(desktopStage);
            if (File.Exists(receiptStage)) File.Delete(receiptStage);
        }
    }

    /// <summary>Actual source materialization only; a successful inventory check is not publisher trust.</summary>
    public static async Task ValidateSourceInventoryAsync(string directory, InstallationReceipt receipt, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(directory) || Path.GetFullPath(directory) != directory ||
            !Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Actual canonical publish directory required.");
        var expected = receipt.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal); var actual = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(); pending.Enqueue(directory); var count = 0;
        while (pending.TryDequeue(out var current))
        {
            if (++count > 10000) throw new InvalidDataException("Publish directory bound exceeded.");
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                ct.ThrowIfCancellationRequested(); var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Publish payload cannot contain links.");
                if ((attributes & FileAttributes.Directory) != 0)
                { if (pending.Count >= 10000) throw new InvalidDataException("Publish directory queue bound exceeded."); pending.Enqueue(path); }
                else if (actual.Count >= 100000 || !actual.Add(Path.GetRelativePath(directory, path))) throw new InvalidDataException("Publish file bound exceeded.");
            }
        }
        if (!actual.SetEquals(expected)) throw new InvalidDataException("Actual full publish inventory differs from signed payload.");
        foreach (var file in receipt.Files)
        {
            using var handle = OpenInputHandle(Path.Combine(directory, file.Path)); await using var stream = new FileStream(handle, FileAccess.Read);
            if (stream.Length != file.Size || !CryptographicOperations.FixedTimeEquals(await HashExactSizeAsync(stream, file.Size, ct), Convert.FromHexString(file.Sha256)))
                throw new InvalidDataException("Actual publish file differs from signed payload.");
        }
    }
    private static async Task CopyVerifiedAsync(string source, string target, InstalledPayloadFile file, CancellationToken ct)
    {
        using var handle = OpenInputHandle(source); await using var input = new FileStream(handle, FileAccess.Read);
        if (input.Length != file.Size) throw new InvalidDataException("Publish file size changed before copy.");
        await using var output = new FileStream(target, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite,
            Share = FileShare.None, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        long copied = 0; var buffer = new byte[65536];
        while (true) { var read = await input.ReadAsync(buffer, ct); if (read == 0) break; copied += read;
            if (copied > file.Size) throw new InvalidDataException("Publish file grew during copy."); await output.WriteAsync(buffer.AsMemory(0, read), ct); }
        output.Position = 0;
        if (copied != file.Size || !CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(output, ct), Convert.FromHexString(file.Sha256)))
            throw new InvalidDataException("Copied payload differs from signed content.");
        output.Flush(true);
    }
    internal static async Task<byte[]> HashExactSizeAsync(Stream stream, long expectedSize, CancellationToken ct)
    {
        if (expectedSize < 0) throw new InvalidDataException("Nonnegative signed file size required.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; long remaining = expectedSize;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
            if (read == 0) throw new InvalidDataException("Publish file shrank during bounded hashing.");
            hash.AppendData(buffer, 0, read); remaining -= read;
        }
        if (await stream.ReadAsync(buffer.AsMemory(0, 1), ct) != 0)
            throw new InvalidDataException("Publish file grew beyond its signed size.");
        return hash.GetHashAndReset();
    }
    internal static SafeFileHandle OpenInputHandle(string path)
    {
        var fd = Open(path, NoFollow | CloseOnExec | NonBlocking);
        if (fd < 0) throw new IOException("No-follow publish file unavailable.");
        var handle = new SafeFileHandle((IntPtr)fd, true);
        try
        {
            // AT_EMPTY_PATH inspects the actual opened descriptor, never a second pathname lookup.
            if (Statx(fd, "", 0x1000, 0x3, out var stat) != 0 ||
                (stat.Mask & 0x3) != 0x3 || (stat.Mode & 0xf000) != 0x8000)
                throw new IOException("Opened publish input must be an actual regular file.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    private static async Task WriteNewAsync(string path, byte[] data, CancellationToken ct)
    {
        await using var output = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
            Share = FileShare.None, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        await output.WriteAsync(data, ct); output.Flush(true);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;
    private static void EnsureProtectedDirectory(string path)
    {
        if (path.Split('/').Length > 64) throw new InvalidDataException("Installation directory depth exceeded.");
        if (Directory.Exists(path)) { if (!LinuxRootOwnedFiles.DirectoryImmutable(path)) throw new UnauthorizedAccessException("Installed directory is not root protected."); return; }
        if (Exists(path)) throw new UnauthorizedAccessException("Installed directory cannot follow a link.");
        EnsureProtectedDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        if (!LinuxRootOwnedFiles.DirectoryImmutable(path)) throw new UnauthorizedAccessException("New installed directory is not root protected.");
    }
}
