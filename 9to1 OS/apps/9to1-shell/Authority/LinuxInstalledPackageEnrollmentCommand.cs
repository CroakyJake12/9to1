using System.Runtime.Versioning;
using System.Text.Json;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Explicit root administrator first-install entry point. No GUI session, keys, trust
/// policy or controlled-launch evidence is created by running this command.</summary>
[SupportedOSPlatform("linux")]
public static class LinuxInstalledPackageEnrollmentCommand
{
    public const string Command = "--native-enroll-first-install";
    public static async Task<int?> TryRunAsync(string[] args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || args[0] != Command) return null;
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: --native-enroll-first-install <absolute-publish-directory> <absolute-signed-receipt> <absolute-desktop-entry>");
            return 2;
        }
        try
        {
            if (await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct).ConfigureAwait(false) != "unix-euid:0")
                throw new UnauthorizedAccessException("Actual administrator required.");
            var envelope = await ReadBoundedInputAsync(args[2], 8 * 1024 * 1024, ct).ConfigureAwait(false);
            var desktop = await ReadBoundedInputAsync(args[3], 1024 * 1024, ct).ConfigureAwait(false);
            var receipt = await LinuxInstalledPackageEnrollment.EnrollFirstInstallAsync(args[1], envelope, desktop, ct).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(new { code = "SignedFirstInstallPublished", receipt.AppId,
                receipt.OsApplicationId, receipt.ReceiptRevision, launched = false, runtimeAccepted = false }));
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException or JsonException)
        {
            Console.Error.WriteLine("Signed first-install enrollment refused. Existing publisher policy and installed packages were preserved.");
            return 1;
        }
    }

    /// <summary>Bound allocation before reading an actual regular input. Input bytes are merely
    /// untrusted material until the maintained signed-receipt and protected policy check succeeds.</summary>
    public static async Task<byte[]> ReadBoundedInputAsync(string path, int maximumBytes, CancellationToken ct)
    {
        if (maximumBytes is < 1 or > 8 * 1024 * 1024 || !Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path)
            throw new InvalidDataException("Bounded canonical input path required.");
        // Same no-follow, nonblocking descriptor path as the actual payload copy; FIFO/link
        // input cannot block the administrator or substitute a different signed envelope.
        using var handle = LinuxInstalledPackageEnrollment.OpenInputHandle(path);
        await using var stream = new FileStream(handle, FileAccess.Read);
        var length = stream.Length;
        if (length is < 1 || length > maximumBytes) throw new InvalidDataException("Enrollment input exceeds its byte bound.");
        var data = new byte[checked((int)length)];
        await stream.ReadExactlyAsync(data, ct).ConfigureAwait(false);
        var trailing = new byte[1];
        if (await stream.ReadAsync(trailing, ct).ConfigureAwait(false) != 0)
            throw new InvalidDataException("Enrollment input grew while reading.");
        return data;
    }
}
