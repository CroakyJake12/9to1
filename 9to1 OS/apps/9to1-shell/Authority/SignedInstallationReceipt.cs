using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NineToOne.Os.Shell.Authority;

public sealed record InstalledPayloadFile(string Path, long Size, string Sha256);
public sealed record InstallationReceipt(int SchemaVersion, string AppId, long ReceiptRevision, string ProviderId,
    string OsApplicationId, string Entrypoint, string InstallRoot, string ExecutablePath, string DesktopEntryPath,
    string DesktopEntrySha256, IReadOnlyList<InstalledPayloadFile> Files, IReadOnlyList<string> AllowedServiceIds,
    IReadOnlyList<string> Roles);
public sealed record SignedInstallationReceipt(int SchemaVersion, string IssuerKeyId, string Payload, string Signature);
public sealed record PublisherTrust(string KeyId, string SubjectPublicKeyInfo, IReadOnlyList<string> AllowedAppIds,
    IReadOnlyList<string> AllowedServiceIds, IReadOnlyList<string> AllowedRoles);

/// <summary>Verifies exact signed bytes and issuer-constrained discovery identity. A digest/inventory never grants trust.</summary>
public static partial class InstallationReceiptSignature
{
    private static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static InstallationReceipt? Verify(ReadOnlySpan<byte> envelope, IReadOnlyList<PublisherTrust> trustedPublishers)
    {
        if (envelope.Length is < 1 or > 8 * 1024 * 1024 || trustedPublishers is null || trustedPublishers.Count > 128) return null;
        try
        {
            var signed = JsonSerializer.Deserialize<SignedInstallationReceipt>(envelope, Json);
            if (signed is null || signed.SchemaVersion != 1 || !Identifier().IsMatch(signed.IssuerKeyId ?? "") || signed.Payload is null || signed.Signature is null) return null;
            var issuers = trustedPublishers.Where(p => p is not null && p.KeyId == signed.IssuerKeyId).ToArray();
            if (issuers.Length != 1) return null;
            var issuer = issuers[0];
            if (issuer.AllowedAppIds is null || issuer.AllowedServiceIds is null || issuer.AllowedRoles is null || issuer.SubjectPublicKeyInfo is null) return null;
            var payload = Convert.FromBase64String(signed.Payload); var signature = Convert.FromBase64String(signed.Signature);
            if (payload.Length is < 1 or > 6 * 1024 * 1024 || signature.Length is < 384 or > 1024) return null;
            using var key = RSA.Create(); var publicKey = Convert.FromBase64String(issuer.SubjectPublicKeyInfo);
            key.ImportSubjectPublicKeyInfo(publicKey, out var read);
            if (read != publicKey.Length || key.KeySize is < 3072 or > 8192 || !key.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) return null;
            var receipt = JsonSerializer.Deserialize<InstallationReceipt>(payload, Json);
            if (receipt is null || !Valid(receipt) || !issuer.AllowedAppIds.Contains(receipt.AppId, StringComparer.Ordinal) ||
                receipt.AllowedServiceIds.Any(id => !issuer.AllowedServiceIds.Contains(id, StringComparer.Ordinal)) ||
                receipt.Roles.Any(role => !issuer.AllowedRoles.Contains(role, StringComparer.Ordinal))) return null;
            return receipt;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or ArgumentException or NotSupportedException or IOException) { return null; }
    }
    public static byte[] EncodePayload(InstallationReceipt receipt)
    { if (!Valid(receipt)) throw new InvalidDataException("Invalid installation receipt."); return JsonSerializer.SerializeToUtf8Bytes(receipt, Json); }
    public static byte[] EncodeEnvelope(SignedInstallationReceipt receipt) => JsonSerializer.SerializeToUtf8Bytes(receipt, Json);
    internal static PublisherTrust[] DecodeTrust(ReadOnlySpan<byte> source) => JsonSerializer.Deserialize<PublisherTrust[]>(source, Json) ?? [];
    private static bool Valid(InstallationReceipt r)
    {
        if (r.SchemaVersion != 1 || !Id(r.AppId) || r.ReceiptRevision < 1 || r.ReceiptRevision == long.MaxValue || !Id(r.ProviderId) ||
            string.IsNullOrWhiteSpace(r.OsApplicationId) || r.OsApplicationId.Length > 4096 || r.OsApplicationId.Any(char.IsControl) || string.IsNullOrWhiteSpace(r.Entrypoint) || r.Entrypoint.Length > 4096 || r.Entrypoint.Any(char.IsControl) ||
            !PackageRoot(r.InstallRoot) || !Inside(r.InstallRoot, r.ExecutablePath) || !DesktopEntry(r.DesktopEntryPath) || !Digest(r.DesktopEntrySha256) ||
            r.Files is null || r.Files.Count is < 1 or > 100000 || r.Files.Any(f => f is null || !Relative(f.Path) || f.Size is < 0 or > 8L * 1024 * 1024 * 1024 || !Digest(f.Sha256)) ||
            r.Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != r.Files.Count ||
            !r.Files.Any(f => Path.Combine(r.InstallRoot, f.Path) == r.ExecutablePath) ||
            r.AllowedServiceIds is null || r.AllowedServiceIds.Count is < 1 or > 128 || r.AllowedServiceIds.Any(id => !Id(id)) || r.AllowedServiceIds.Distinct(StringComparer.Ordinal).Count() != r.AllowedServiceIds.Count ||
            r.Roles is null || r.Roles.Count > 16 || r.Roles.Any(role => !Id(role)) || r.Roles.Distinct(StringComparer.Ordinal).Count() != r.Roles.Count) return false;
        return true;
    }
    private static bool Id(string? value) => value is not null && Identifier().IsMatch(value);
    private static bool Digest(string? value) => value is not null && Sha256().IsMatch(value);
    private static bool PackageRoot(string? path) => path is not null && path.Length <= 4096 && !path.Any(char.IsControl) && Path.IsPathFullyQualified(path) && Path.GetFullPath(path) == path &&
        (path.StartsWith("/usr/lib/9to1/apps/", StringComparison.Ordinal) || path.StartsWith("/opt/9to1/apps/", StringComparison.Ordinal)) && !path.EndsWith('/');
    private static bool Inside(string root, string? path) => path is not null && path.Length <= 4096 && !path.Any(char.IsControl) && Path.IsPathFullyQualified(path) && Path.GetFullPath(path) == path && path.StartsWith(root + "/", StringComparison.Ordinal);
    private static bool DesktopEntry(string? path) => path is not null && path.Length <= 4096 && !path.Any(char.IsControl) && Path.GetFullPath(path) == path && path.StartsWith("/usr/share/applications/", StringComparison.Ordinal) && path.EndsWith(".desktop", StringComparison.Ordinal);
    private static bool Relative(string? path) => path is not null && path.Length is > 0 and <= 4096 && !Path.IsPathRooted(path) &&
        !path.Split('/').Any(part => part is "" or "." or "..") && !path.Any(char.IsControl) && !path.Contains('\\');
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex Identifier();
    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex Sha256();
}
