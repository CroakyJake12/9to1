using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CakeOS.Platform;

/// <summary>Contract exposed by Settings for the local developer update file picker.</summary>
public sealed record UpdateFilePickerOptions(string Title, IReadOnlyList<string> AllowedExtensions);

public sealed record CakeOsSystemInfo(string InstalledVersion, string Architecture);

public interface ICakeOsSystemInfoProvider
{
    CakeOsSystemInfo GetSystemInfo();
}

/// <summary>Reads the installed CakeOS version instead of trusting a bundle to identify its target.</summary>
public sealed class CakeOsReleaseSystemInfoProvider(string releaseFile = "/etc/cakeos-release") : ICakeOsSystemInfoProvider
{
    private readonly string _releaseFile = releaseFile;

    public CakeOsSystemInfo GetSystemInfo()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Local CakeOS updates can only be installed on Linux.");
        if (!File.Exists(_releaseFile))
            throw new InvalidOperationException("CakeOS release metadata is unavailable; update compatibility cannot be verified.");

        var version = File.ReadLines(_releaseFile)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2 && string.Equals(parts[0], "VERSION_ID", StringComparison.Ordinal))
            .Select(parts => parts[1].Trim().Trim('\"'))
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidDataException("CakeOS release metadata does not contain VERSION_ID.");

        return new CakeOsSystemInfo(version, RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("This CPU architecture is not supported by local CakeOS updates.")
        });
    }
}

public sealed class StaticCakeOsSystemInfoProvider(CakeOsSystemInfo systemInfo) : ICakeOsSystemInfoProvider
{
    private readonly CakeOsSystemInfo _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));

    public CakeOsSystemInfo GetSystemInfo() => _systemInfo;
}

public sealed record CakeUpdateCompatibility(
    string MinimumInstalledVersion,
    string MaximumInstalledVersion,
    IReadOnlyList<string> Architectures);

public sealed record CakeUpdatePackage(
    string Id,
    string Version,
    string Architecture,
    string Path,
    string Sha256,
    long SizeBytes,
    IReadOnlyList<string> DependsOn);

public sealed record CakeUpdateManifest(
    int SchemaVersion,
    string BundleId,
    string Version,
    CakeUpdateCompatibility Compatibility,
    IReadOnlyList<CakeUpdatePackage> Packages);

public sealed record ValidatedCakeUpdate(
    string BundlePath,
    string BundleSha256,
    string ManifestSha256,
    CakeUpdateManifest Manifest);

public sealed record CakeUpdateValidationResult(bool IsValid, ValidatedCakeUpdate? Update, string? ErrorMessage)
{
    public static CakeUpdateValidationResult Failure(string message) => new(false, null, message);
}

/// <summary>
/// Validates a .cakeupdate ZIP without extracting it. Only manifest.json and direct packages/*.deb entries are accepted,
/// which prevents archive traversal and excludes executable bundle content.
/// </summary>
public sealed class CakeUpdateBundleValidator(ICakeOsSystemInfoProvider systemInfoProvider)
{
    public const int SupportedSchemaVersion = 1;
    public const string BundleExtension = ".cakeupdate";
    private const long MaximumBundleBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumManifestBytes = 64 * 1024;
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumPackageCount = 128;
    private static readonly Regex SemanticVersionPattern = new("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex PackageIdPattern = new("^[a-z0-9][a-z0-9+.-]{0,127}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Sha256Pattern = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        NumberHandling = JsonNumberHandling.Strict
    };

    private readonly ICakeOsSystemInfoProvider _systemInfoProvider = systemInfoProvider ?? throw new ArgumentNullException(nameof(systemInfoProvider));

    public async Task<CakeUpdateValidationResult> ValidateAsync(string bundlePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullBundlePath = ValidateBundlePath(bundlePath);
            var systemInfo = _systemInfoProvider.GetSystemInfo();
            var systemVersion = ParseVersion(systemInfo.InstalledVersion, "Installed CakeOS version");
            ValidateArchitecture(systemInfo.Architecture, "Installed CakeOS architecture", allowAll: false);

            var fileInfo = new FileInfo(fullBundlePath);
            if (fileInfo.Length > MaximumBundleBytes)
                throw new InvalidDataException("Update bundle exceeds the maximum supported size.");

            await using var bundleStream = new FileStream(fullBundlePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            if (bundleStream.Length > MaximumBundleBytes)
                throw new InvalidDataException("Update bundle exceeds the maximum supported size.");
            using var archive = new ZipArchive(bundleStream, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries.ToArray();
            if (entries.Length == 0 || entries.Length > MaximumPackageCount + 1)
                throw new InvalidDataException("Update bundle has an unsupported number of entries.");

            var entryByPath = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            long expandedBytes = 0;
            foreach (var entry in entries)
            {
                ValidateArchiveEntry(entry);
                if (!entryByPath.TryAdd(entry.FullName, entry))
                    throw new InvalidDataException("Update bundle contains duplicate entry paths.");
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaximumBundleBytes)
                    throw new InvalidDataException("Update bundle expands beyond the maximum supported size.");
            }

            if (!entryByPath.TryGetValue("manifest.json", out var manifestEntry))
                throw new InvalidDataException("Update bundle is missing manifest.json.");
            if (manifestEntry.Length == 0 || manifestEntry.Length > MaximumManifestBytes)
                throw new InvalidDataException("Update manifest has an unsupported size.");

            var manifestBytes = await ReadEntryAsync(manifestEntry, MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
            var manifest = ParseManifest(manifestBytes, systemInfo, systemVersion);
            ValidateDeclaredEntries(manifest, entryByPath);

            foreach (var package in manifest.Packages)
            {
                var entry = entryByPath[package.Path];
                if (entry.Length != package.SizeBytes)
                    throw new InvalidDataException($"Package '{package.Id}' size does not match the manifest.");
                var actualSha256 = await HashEntryAsync(entry, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actualSha256, package.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException($"Package '{package.Id}' SHA-256 does not match the manifest.");
            }

            bundleStream.Position = 0;
            var bundleSha256 = await HashStreamAsync(bundleStream, cancellationToken).ConfigureAwait(false);
            return new CakeUpdateValidationResult(
                true,
                new ValidatedCakeUpdate(fullBundlePath, bundleSha256, HashBytes(manifestBytes), manifest),
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or PlatformNotSupportedException or InvalidOperationException or OverflowException)
        {
            return CakeUpdateValidationResult.Failure(exception.Message);
        }
    }

    private static string ValidateBundlePath(string bundlePath)
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || !Path.IsPathFullyQualified(bundlePath))
            throw new ArgumentException("Update bundle path must be an absolute file path.", nameof(bundlePath));
        if (!string.Equals(Path.GetExtension(bundlePath), BundleExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Update bundles must use the {BundleExtension} extension.", nameof(bundlePath));

        var fullPath = Path.GetFullPath(bundlePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Update bundle was not found.", fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Update bundle must not be a symbolic link or other reparse point.");
        return fullPath;
    }

    private static void ValidateArchiveEntry(ZipArchiveEntry entry)
    {
        if (!IsAllowedEntryPath(entry.FullName))
            throw new InvalidDataException("Update bundle contains an unsupported or non-contained entry path.");
        if (entry.CompressedLength < 0 || entry.Length < 0 || entry.Length > MaximumPackageBytes)
            throw new InvalidDataException("Update bundle contains an entry with an unsupported size.");

        var unixFileType = (entry.ExternalAttributes >> 16) & 0xf000;
        if (unixFileType != 0 && unixFileType != 0x8000)
            throw new InvalidDataException("Update bundle entries must be regular files.");
    }

    private static bool IsAllowedEntryPath(string path)
    {
        if (string.Equals(path, "manifest.json", StringComparison.Ordinal))
            return true;
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains('\0') || !path.StartsWith("packages/", StringComparison.Ordinal))
            return false;

        var packageName = path["packages/".Length..];
        return packageName.Length > 0 &&
            !packageName.Contains('/') &&
            packageName is not "." and not ".." &&
            packageName.EndsWith(".deb", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, long maximumBytes, CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes)
            throw new InvalidDataException("Update bundle entry exceeds its supported size.");

        await using var source = entry.Open();
        using var destination = new MemoryStream((int)entry.Length);
        await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
        if (destination.Length != entry.Length)
            throw new InvalidDataException("Update bundle entry was truncated.");
        return destination.ToArray();
    }

    private static CakeUpdateManifest ParseManifest(byte[] manifestBytes, CakeOsSystemInfo systemInfo, SemanticVersion systemVersion)
    {
        var document = JsonSerializer.Deserialize<CakeUpdateManifestDocument>(manifestBytes, ManifestSerializerOptions)
            ?? throw new InvalidDataException("Update manifest is empty.");
        if (document.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"Update manifest schema version {document.SchemaVersion} is unsupported.");
        PlatformContractValidation.RequireIdentifier(document.BundleId ?? string.Empty, "bundleId");
        var bundleVersion = ParseVersion(document.Version, "Update version");
        if (document.Compatibility is null)
            throw new InvalidDataException("Update manifest compatibility is required.");
        if (document.Packages is null || document.Packages.Length == 0 || document.Packages.Length > MaximumPackageCount)
            throw new InvalidDataException("Update manifest must declare between one and 128 packages.");

        var minimumVersion = ParseVersion(document.Compatibility.MinimumInstalledVersion, "Minimum installed version");
        var maximumVersion = ParseVersion(document.Compatibility.MaximumInstalledVersion, "Maximum installed version");
        if (minimumVersion.CompareTo(maximumVersion) > 0)
            throw new InvalidDataException("Update manifest compatibility version range is invalid.");
        if (systemVersion.CompareTo(minimumVersion) < 0 || systemVersion.CompareTo(maximumVersion) > 0)
            throw new InvalidDataException("Update is not compatible with this installed CakeOS version.");
        if (bundleVersion.CompareTo(systemVersion) <= 0)
            throw new InvalidDataException("Update version must be newer than the installed CakeOS version.");

        var architectures = document.Compatibility.Architectures;
        if (architectures is null || architectures.Length == 0 || architectures.Distinct(StringComparer.Ordinal).Count() != architectures.Length)
            throw new InvalidDataException("Update manifest must declare distinct compatible architectures.");
        foreach (var architecture in architectures)
            ValidateArchitecture(architecture, "Compatible architecture", allowAll: false);
        if (!architectures.Contains(systemInfo.Architecture, StringComparer.Ordinal))
            throw new InvalidDataException("Update is not compatible with this CPU architecture.");

        var packages = new List<CakeUpdatePackage>(document.Packages.Length);
        var packageIds = new HashSet<string>(StringComparer.Ordinal);
        var packagePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in document.Packages)
        {
            if (package is null)
                throw new InvalidDataException("Update manifest contains an empty package declaration.");
            if (string.IsNullOrWhiteSpace(package.Id) || !PackageIdPattern.IsMatch(package.Id))
                throw new InvalidDataException("Update package identifiers must be lowercase Debian-style names.");
            if (!packageIds.Add(package.Id))
                throw new InvalidDataException("Update manifest contains duplicate package identifiers.");
            ParseVersion(package.Version, $"Package '{package.Id}' version");
            ValidateArchitecture(package.Architecture, $"Package '{package.Id}' architecture", allowAll: true);
            if (package.Architecture != "all" && !string.Equals(package.Architecture, systemInfo.Architecture, StringComparison.Ordinal))
                throw new InvalidDataException($"Package '{package.Id}' does not support this CPU architecture.");
            var packagePath = package.Path ?? string.Empty;
            if (!IsAllowedEntryPath(packagePath) || !packagePaths.Add(packagePath))
                throw new InvalidDataException($"Package '{package.Id}' has an invalid or duplicate bundle path.");
            if (string.IsNullOrWhiteSpace(package.Sha256) || !Sha256Pattern.IsMatch(package.Sha256))
                throw new InvalidDataException($"Package '{package.Id}' must have a lowercase SHA-256 digest.");
            if (package.SizeBytes is null || package.SizeBytes < 0 || package.SizeBytes > MaximumPackageBytes)
                throw new InvalidDataException($"Package '{package.Id}' has an unsupported size.");
            if (package.DependsOn is null || package.DependsOn.Distinct(StringComparer.Ordinal).Count() != package.DependsOn.Length)
                throw new InvalidDataException($"Package '{package.Id}' has duplicate dependency declarations.");

            var dependencies = new string[package.DependsOn.Length];
            for (var index = 0; index < package.DependsOn.Length; index++)
            {
                var dependency = package.DependsOn[index];
                if (string.IsNullOrWhiteSpace(dependency) || !PackageIdPattern.IsMatch(dependency) || string.Equals(dependency, package.Id, StringComparison.Ordinal))
                    throw new InvalidDataException($"Package '{package.Id}' has an invalid dependency declaration.");
                dependencies[index] = dependency;
            }
            packages.Add(new CakeUpdatePackage(package.Id, package.Version!, package.Architecture!, packagePath, package.Sha256!, package.SizeBytes.Value, dependencies));
        }

        ValidateDependencies(packages, packageIds);
        return new CakeUpdateManifest(
            document.SchemaVersion.Value,
            document.BundleId!,
            document.Version!,
            new CakeUpdateCompatibility(
                document.Compatibility.MinimumInstalledVersion!,
                document.Compatibility.MaximumInstalledVersion!,
                architectures),
            packages);
    }

    private static void ValidateDeclaredEntries(CakeUpdateManifest manifest, IReadOnlyDictionary<string, ZipArchiveEntry> entryByPath)
    {
        if (entryByPath.Count != manifest.Packages.Count + 1)
            throw new InvalidDataException("Update bundle contains package entries not declared by the manifest.");
        foreach (var package in manifest.Packages)
        {
            if (!entryByPath.ContainsKey(package.Path))
                throw new InvalidDataException($"Package '{package.Id}' is missing from the update bundle.");
        }
    }

    private static void ValidateDependencies(IReadOnlyCollection<CakeUpdatePackage> packages, IReadOnlySet<string> packageIds)
    {
        var byId = packages.ToDictionary(package => package.Id, StringComparer.Ordinal);
        foreach (var package in packages)
        {
            foreach (var dependency in package.DependsOn)
            {
                if (!packageIds.Contains(dependency))
                    throw new InvalidDataException($"Package '{package.Id}' depends on '{dependency}', which is not in the bundle.");
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages)
            Visit(package.Id);
        return;

        void Visit(string packageId)
        {
            if (visited.Contains(packageId))
                return;
            if (!visiting.Add(packageId))
                throw new InvalidDataException("Update package dependencies must not contain a cycle.");
            foreach (var dependency in byId[packageId].DependsOn)
                Visit(dependency);
            visiting.Remove(packageId);
            visited.Add(packageId);
        }
    }

    private static void ValidateArchitecture(string? architecture, string name, bool allowAll)
    {
        if (architecture is not ("amd64" or "arm64") && (!allowAll || architecture != "all"))
            throw new InvalidDataException($"{name} must be amd64{(allowAll ? ", arm64, or all" : " or arm64")}.");
    }

    private static SemanticVersion ParseVersion(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !SemanticVersionPattern.IsMatch(value))
            throw new InvalidDataException($"{name} must use MAJOR.MINOR.PATCH semantic versioning.");
        var components = value.Split('.');
        if (!int.TryParse(components[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(components[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(components[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            throw new InvalidDataException($"{name} is outside the supported version range.");
        }
        return new SemanticVersion(major, minor, patch);
    }

    private static async Task<string> HashEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return await HashStreamAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> HashStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) != 0)
                hash.AppendData(buffer, 0, read);
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record CakeUpdateManifestDocument(
        int? SchemaVersion,
        string? BundleId,
        string? Version,
        CakeUpdateCompatibilityDocument? Compatibility,
        CakeUpdatePackageDocument[]? Packages);

    private sealed record CakeUpdateCompatibilityDocument(
        string? MinimumInstalledVersion,
        string? MaximumInstalledVersion,
        string[]? Architectures);

    private sealed record CakeUpdatePackageDocument(
        string? Id,
        string? Version,
        string? Architecture,
        string? Path,
        string? Sha256,
        long? SizeBytes,
        string[]? DependsOn);

    private readonly record struct SemanticVersion(int Major, int Minor, int Patch) : IComparable<SemanticVersion>
    {
        public int CompareTo(SemanticVersion other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
                return major;
            var minor = Minor.CompareTo(other.Minor);
            return minor != 0 ? minor : Patch.CompareTo(other.Patch);
        }
    }
}

public sealed record CakeUpdateInstallRequest(string BundlePath, string BundleSha256, string ManifestSha256);

public sealed record PrivilegedInstallerResult(bool Succeeded, string Message, bool HistoryRecorded = false);

/// <summary>Only the privileged helper may stage packages and invoke the Debian package manager.</summary>
public interface IPrivilegedCakeUpdateInstaller
{
    Task<PrivilegedInstallerResult> InstallAsync(CakeUpdateInstallRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Invokes one fixed root helper with fixed argument names. The helper must independently revalidate the archive and
/// expected digests before staging packages, validating Debian dependencies, installing, and recording history.
/// </summary>
public sealed class PkexecCakeUpdateInstaller : IPrivilegedCakeUpdateInstaller
{
    public const string HelperPath = "/usr/libexec/cakeos/cakeos-local-update-installer";
    public const int RecordedFailureExitCode = 20;

    public async Task<PrivilegedInstallerResult> InstallAsync(CakeUpdateInstallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInstallRequest(request);
        if (!OperatingSystem.IsLinux())
            return new PrivilegedInstallerResult(false, "The privileged CakeOS update installer is only available on Linux.");
        if (!File.Exists(HelperPath))
            return new PrivilegedInstallerResult(false, "The privileged CakeOS update installer is not installed.");

        try
        {
            var startInfo = new ProcessStartInfo("pkexec")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(HelperPath);
            startInfo.ArgumentList.Add("--bundle");
            startInfo.ArgumentList.Add(request.BundlePath);
            startInfo.ArgumentList.Add("--bundle-sha256");
            startInfo.ArgumentList.Add(request.BundleSha256);
            startInfo.ArgumentList.Add("--manifest-sha256");
            startInfo.ArgumentList.Add(request.ManifestSha256);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the privileged CakeOS update installer.");
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return process.ExitCode switch
            {
                0 => new PrivilegedInstallerResult(true, "The privileged CakeOS update installer completed successfully.", true),
                RecordedFailureExitCode => new PrivilegedInstallerResult(false, "The privileged CakeOS update installer failed.", true),
                _ => new PrivilegedInstallerResult(false, "The privileged CakeOS update installer failed before it could confirm update history.")
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new PrivilegedInstallerResult(false, $"Could not invoke the privileged CakeOS update installer: {exception.Message}");
        }
    }

    private static void ValidateInstallRequest(CakeUpdateInstallRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BundlePath) || !Path.IsPathFullyQualified(request.BundlePath) ||
            !string.Equals(Path.GetExtension(request.BundlePath), CakeUpdateBundleValidator.BundleExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Privileged installation requires an absolute .cakeupdate bundle path.", nameof(request));
        }
        if (!IsSha256(request.BundleSha256) || !IsSha256(request.ManifestSha256))
            throw new ArgumentException("Privileged installation requires lowercase SHA-256 digests.", nameof(request));
    }

    private static bool IsSha256(string? value) => value is not null && value.Length == 64 && value.All(character => character is >= 'a' and <= 'f' or >= '0' and <= '9');
}

public enum UpdateInstallStatus
{
    Succeeded = 0,
    Failed = 1
}

public sealed record UpdateHistoryEntry(
    Guid Id,
    string BundleId,
    string Version,
    string BundleSha256,
    UpdateInstallStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? FailureReason);

public interface IUpdateHistoryStore
{
    Task<IReadOnlyList<UpdateHistoryEntry>> GetAsync(CancellationToken cancellationToken = default);
    Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>System update history belongs in /var/lib, not the per-user Settings store.</summary>
public sealed class CakeUpdateHistoryStore : IUpdateHistoryStore
{
    public const string SystemDirectory = "/var/lib/cakeos/updates";
    private const int MaximumHistoryEntries = 100;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _historyFile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CakeUpdateHistoryStore(string directory = SystemDirectory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Update history directory must be an absolute path.", nameof(directory));
        _historyFile = Path.Combine(Path.GetFullPath(directory), "history.v1.json");
    }

    public async Task<IReadOnlyList<UpdateHistoryEntry>> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await LoadAsync(cancellationToken).ConfigureAwait(false))
                .OrderByDescending(entry => entry.CompletedAtUtc)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(cancellationToken).ConfigureAwait(false);
            entries.Add(entry);
            if (entries.Count > MaximumHistoryEntries)
                entries = entries.OrderByDescending(history => history.CompletedAtUtc).Take(MaximumHistoryEntries).ToList();
            await PersistAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<UpdateHistoryEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_historyFile))
            return [];

        await using var stream = new FileStream(_historyFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var document = await JsonSerializer.DeserializeAsync<UpdateHistoryDocument>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Update history is empty.");
        if (document.SchemaVersion != 1)
            throw new InvalidDataException($"Update history schema version {document.SchemaVersion} is unsupported.");
        return document.Entries?.ToList() ?? [];
    }

    private async Task PersistAsync(IReadOnlyCollection<UpdateHistoryEntry> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_historyFile) ?? throw new InvalidOperationException("Update history has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryFile = _historyFile + ".tmp";
        await using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, new UpdateHistoryDocument(1, entries.ToArray()), SerializerOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporaryFile, _historyFile, overwrite: true);
    }

    private sealed record UpdateHistoryDocument(int SchemaVersion, UpdateHistoryEntry[]? Entries);
}

public sealed record UpdateInstallConfirmation(
    Guid Id,
    string BundleId,
    string Version,
    int PackageCount,
    string TrustDescription,
    DateTimeOffset ExpiresAtUtc);

public sealed record SettingsUpdateSelection(UpdateInstallConfirmation? Confirmation, string? ErrorMessage)
{
    public bool RequiresConfirmation => Confirmation is not null;
}

public sealed record UpdateInstallResult(UpdateInstallStatus Status, string Message, UpdateHistoryEntry? HistoryEntry, bool HistoryRecorded)
{
    public static UpdateInstallResult Failure(string message) => new(UpdateInstallStatus.Failed, message, null, false);
}

public interface ISettingsUpdatesModel
{
    UpdateFilePickerOptions InstallUpdateFromFile { get; }
    Task<SettingsUpdateSelection> SelectUpdateFileAsync(string bundlePath, CancellationToken cancellationToken = default);
    Task<UpdateInstallResult> ConfirmInstallAsync(Guid confirmationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UpdateHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Settings uses this boundary rather than invoking a package manager. Selecting a file only validates it; a separate,
/// one-time confirmation is required before the fixed privileged installer can be called.
/// </summary>
public sealed class SettingsUpdatesModel : ISettingsUpdatesModel
{
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(10);
    private readonly CakeUpdateBundleValidator _validator;
    private readonly IPrivilegedCakeUpdateInstaller _installer;
    private readonly IUpdateHistoryStore _history;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<Guid, PendingUpdate> _pending = new();

    public SettingsUpdatesModel(
        CakeUpdateBundleValidator validator,
        IPrivilegedCakeUpdateInstaller installer,
        IUpdateHistoryStore history,
        Func<DateTimeOffset>? clock = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public UpdateFilePickerOptions InstallUpdateFromFile { get; } = new("Install Update From File", [CakeUpdateBundleValidator.BundleExtension]);

    public async Task<SettingsUpdateSelection> SelectUpdateFileAsync(string bundlePath, CancellationToken cancellationToken = default)
    {
        var validation = await _validator.ValidateAsync(bundlePath, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid || validation.Update is null)
            return new SettingsUpdateSelection(null, validation.ErrorMessage ?? "Update bundle validation failed.");

        var now = _clock();
        var confirmation = new UpdateInstallConfirmation(
            Guid.NewGuid(),
            validation.Update.Manifest.BundleId,
            validation.Update.Manifest.Version,
            validation.Update.Manifest.Packages.Count,
            "Trusted local developer bundle: signing infrastructure is not configured for this channel.",
            now + ConfirmationLifetime);
        _pending[confirmation.Id] = new PendingUpdate(validation.Update, confirmation);
        return new SettingsUpdateSelection(confirmation, null);
    }

    public async Task<UpdateInstallResult> ConfirmInstallAsync(Guid confirmationId, CancellationToken cancellationToken = default)
    {
        if (!_pending.TryRemove(confirmationId, out var pending))
            return UpdateInstallResult.Failure("This update confirmation is unknown, expired, or has already been used.");
        if (_clock() > pending.Confirmation.ExpiresAtUtc)
            return UpdateInstallResult.Failure("This update confirmation has expired. Validate the bundle again before installing.");

        var startedAtUtc = _clock();
        PrivilegedInstallerResult installerResult;
        try
        {
            installerResult = await _installer.InstallAsync(
                new CakeUpdateInstallRequest(pending.Update.BundlePath, pending.Update.BundleSha256, pending.Update.ManifestSha256),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            installerResult = new PrivilegedInstallerResult(false, "The update install request was cancelled before the privileged installer completed.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            installerResult = new PrivilegedInstallerResult(false, exception.Message);
        }

        var completedAtUtc = _clock();
        var entry = new UpdateHistoryEntry(
            Guid.NewGuid(),
            pending.Update.Manifest.BundleId,
            pending.Update.Manifest.Version,
            pending.Update.BundleSha256,
            installerResult.Succeeded ? UpdateInstallStatus.Succeeded : UpdateInstallStatus.Failed,
            startedAtUtc,
            completedAtUtc,
            installerResult.Succeeded ? null : installerResult.Message);
        if (installerResult.HistoryRecorded)
            return new UpdateInstallResult(entry.Status, installerResult.Message, entry, true);
        try
        {
            await _history.RecordAsync(entry, CancellationToken.None).ConfigureAwait(false);
            return new UpdateInstallResult(entry.Status, installerResult.Message, entry, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            return new UpdateInstallResult(
                UpdateInstallStatus.Failed,
                $"{installerResult.Message} Update history could not be recorded: {exception.Message}",
                entry,
                false);
        }
    }

    public Task<IReadOnlyList<UpdateHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default) => _history.GetAsync(cancellationToken);

    private sealed record PendingUpdate(ValidatedCakeUpdate Update, UpdateInstallConfirmation Confirmation);
}
