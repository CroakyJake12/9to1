using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeInstalledRootAdmission
{
    private readonly ConditionalWeakTable<OriginalProtectedEnrollmentCatalogue, Invocation> _protectedCatalogues = new();

    /// <summary>Private transfer of bytes from the SAME independently read, still
    /// pinned canonical enrollment. The token cannot enroll a publisher, choose an
    /// app, or grant Home access. Root must independently authenticate its actual
    /// signer and verify every package signature using that signer's actual key.</summary>
    internal sealed class OriginalProtectedEnrollmentCatalogue
    {
        internal readonly NativeWindowsHomeInstalledRootAdmission Owner;
        internal readonly HomeCoreStoredState OriginalState;
        internal readonly HomePackageDatabaseSnapshot OriginalRegistry;
        internal readonly HomePackageOriginalPublisherEnrollmentRecord OriginalEnrollment;
        internal readonly byte[] OriginalBytes;
        internal readonly string OriginalEnrollmentDigest;
        internal string OriginalCatalogueSha256 => OriginalEnrollment.CatalogueSha256;
        internal string OriginalPublisherCertificateSha256 => OriginalEnrollment.PublisherCertificateSha256;
        internal OriginalProtectedEnrollmentCatalogue(NativeWindowsHomeInstalledRootAdmission owner,
            HomeCoreStoredState state, HomePackageDatabaseSnapshot registry,
            HomePackageOriginalPublisherEnrollmentRecord enrollment, byte[] bytes, string digest)
        {
            Owner = owner; OriginalState = state; OriginalRegistry = registry;
            OriginalEnrollment = enrollment; OriginalBytes = bytes; OriginalEnrollmentDigest = digest;
        }
    }

    private OriginalProtectedEnrollmentCatalogue IssueOriginalProtectedCatalogue(Invocation work,
        HomeCoreStoredState actualState, HomePackageDatabaseSnapshot actualRegistry,
        HomePackageOriginalPublisherEnrollmentRecord actualEnrollment)
    {
        if (work.OriginalStateRead is not { IsCompletedSuccessfully: true } sameRead ||
            !ReferenceEquals(sameRead.GetAwaiter().GetResult(), actualState) ||
            !ReferenceEquals(work.OriginalState, actualState) || work.Sources.OriginalErrors.Count != 0)
            throw new UnauthorizedAccessException("The SAME independently joined protected machine-state read is required.");
        var bytes = DecodeOriginalRetainedCatalogue(actualEnrollment);
        DemandNativeCurrent(work);
        var result = new OriginalProtectedEnrollmentCatalogue(this, actualState, actualRegistry, actualEnrollment,
            bytes, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(actualEnrollment, Json))));
        _protectedCatalogues.Add(result, work); return result;
    }

    // Parsing/storage validation only: detached enrollment bytes never issue a
    // protected-source token or a publisher/runtime/service/permission grant.
    internal static byte[] DecodeOriginalRetainedCatalogue(HomePackageOriginalPublisherEnrollmentRecord record)
    {
        if (record.SchemaVersion != HomePackageOriginalPublisherEnrollmentRecord.RetainedCatalogueSchemaVersion ||
            record.OriginalSignedCatalogueBase64 is not { Length: > 0 } encoded ||
            encoded.Length > (HomePackageOriginalPublisherEnrollmentRecord.MaximumRetainedCatalogueBytes + 2) / 3 * 4 ||
            record.CatalogueSha256.Length != 64 || record.CatalogueSha256.Any(value => !Uri.IsHexDigit(value)))
            throw new InvalidDataException("Explicit installer-retained catalogue enrollment is missing or exceeds its supported bound.");
        var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length is < 1 or > HomePackageOriginalPublisherEnrollmentRecord.MaximumRetainedCatalogueBytes ||
            !StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(bytes)), record.CatalogueSha256))
            throw new UnauthorizedAccessException("The retained exact catalogue bytes disagree with the protected enrollment digest.");
        return bytes;
    }

    internal void DemandOriginalProtectedCatalogueCurrent(OriginalProtectedEnrollmentCatalogue same,
        NativeWindowsHomeInstallerBootstrapAdmission actualPublisher)
    {
        Invocation work;
        lock (_gate)
        {
            if (!ReferenceEquals(actualPublisher, _publisher) || same is null || !ReferenceEquals(same.Owner, this) ||
                !_protectedCatalogues.TryGetValue(same, out work!) || !_originals.Contains(work) ||
                work.Driver.IsCompleted || work.OriginalStateRead is not { IsCompletedSuccessfully: true } originalRead ||
                !ReferenceEquals(originalRead.GetAwaiter().GetResult(), same.OriginalState) ||
                !ReferenceEquals(work.OriginalState, same.OriginalState) || work.Sources.OriginalErrors.Count != 0 ||
                work.Resources.Any(resource => resource.Close is not null))
                throw new UnauthorizedAccessException("Use only the SAME live protected canonical enrollment source, before its native pins retire.");
        }
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { DemandNativeCurrent(work); return true; });
    }

    internal byte[] ReadOriginalProtectedCatalogueBytes(OriginalProtectedEnrollmentCatalogue same,
        NativeWindowsHomeInstallerBootstrapAdmission actualPublisher, string actualOsSignerSha256)
    {
        DemandOriginalProtectedCatalogueCurrent(same, actualPublisher);
        if (!StringComparer.OrdinalIgnoreCase.Equals(same.OriginalPublisherCertificateSha256, actualOsSignerSha256))
            throw new UnauthorizedAccessException("The actual signed Root publisher differs from the exact explicit installer enrollment.");
        return same.OriginalBytes.ToArray();
    }
}
