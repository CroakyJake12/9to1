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
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                code = "SignedFirstInstallRefused", exceptionType = RefusalExceptionType(error),
                reason = RefusalReason(error)
            }));
            return 1;
        }
    }

    // Administrator diagnostics expose fixed refusal codes only, never arbitrary messages,
    // payloads, credentials or stack traces. This does not change enrollment admission.
    private static string RefusalExceptionType(Exception error)
    {
        var name = error.GetType().Name;
        return name.Length is > 0 and <= 128 && name.All(char.IsAsciiLetterOrDigit) ? name : nameof(Exception);
    }
    private static string RefusalReason(Exception error) => error.Message switch
    {
        "Actual administrator required." => "AdministratorPrincipalRequired",
        "Bounded canonical input path required." => "CanonicalInputPathRequired",
        "Enrollment input exceeds its byte bound." => "InputByteLimitExceeded",
        "Enrollment input grew while reading." => "InputGrewDuringRead",
        "Bounded signed package and actual desktop entry required." => "EnvelopeOrDesktopByteLimit",
        "Installed package enrollment requires the actual administrator process." => "EnrollmentAdministratorPrincipalRequired",
        "Existing protected publisher enrollment policy required." => "ProtectedPublisherPolicyUnavailable",
        "Package signature or issuer installation scope is not permitted." => "SignedPackageOrIssuerScopeRefused",
        "Actual desktop entry does not match its signed receipt." => "DesktopEntryDigestMismatch",
        "Actual desktop registration does not match the canonical signed platform identity." => "DesktopRegistrationIdentityMismatch",
        "Enrollment lock is not a protected regular file." => "ProtectedEnrollmentLockRequired",
        "First-install enrollment cannot replace existing package, desktop entry or receipt." => "ExistingInstallCannotBeReplaced",
        "Publisher enrollment changed before package publication." => "PublisherPolicyChangedBeforePublication",
        "Actual canonical publish directory required." => "CanonicalPublishDirectoryRequired",
        "Publish directory bound exceeded." => "PublishDirectoryLimitExceeded",
        "Publish payload cannot contain links." => "PublishPayloadLinksRefused",
        "Publish directory queue bound exceeded." => "PublishDirectoryQueueLimitExceeded",
        "Publish file bound exceeded." => "PublishFileLimitExceeded",
        "Actual full publish inventory differs from signed payload." => "PublishInventoryMismatch",
        "Actual publish file differs from signed payload." => "PublishFileDigestOrSizeMismatch",
        "Publish file size changed before copy." => "PublishFileSizeChangedBeforeCopy",
        "Publish file grew during copy." => "PublishFileGrewDuringCopy",
        "Copied payload differs from signed content." => "CopiedPayloadMismatch",
        "Nonnegative signed file size required." => "SignedFileSizeInvalid",
        "Publish file shrank during bounded hashing." => "PublishFileShrankDuringHash",
        "Publish file grew beyond its signed size." => "PublishFileGrewDuringHash",
        "No-follow publish file unavailable." => "NoFollowPublishFileUnavailable",
        "Opened publish input must be an actual regular file." => "OpenedPublishInputNotRegular",
        "Installation directory depth exceeded." => "InstallDirectoryDepthExceeded",
        "Installed directory is not root protected." => "ExistingInstallParentNotRootProtected",
        "Installed directory cannot follow a link." => "InstallDirectoryLinkRefused",
        "New installed directory is not root protected." => "CreatedInstallParentNotRootProtected",
        _ => error switch
        {
            JsonException => "PolicyOrEnvelopeJsonRefused",
            System.Security.Cryptography.CryptographicException => "CryptographicOperationRefused",
            UnauthorizedAccessException => "UnclassifiedAuthorizationRefusal",
            IOException => "UnclassifiedFilesystemRefusal",
            ArgumentException => "UnclassifiedInputRefusal",
            _ => "UnclassifiedEnrollmentRefusal"
        }
    };

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
