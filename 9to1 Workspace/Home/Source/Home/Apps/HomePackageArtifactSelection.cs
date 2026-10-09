using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Home.Apps;

/// <summary>Detached package metadata. It is not a publisher, installation, approval, or execution grant.</summary>
public sealed record HomePackageArtifactDescriptor(
    int SchemaVersion, string PackageId, string AppId, string Version, string Channel,
    string ComponentClass, string Platform, string Abi,
    IReadOnlyList<HomePackageDependency> Dependencies,
    IReadOnlyList<string> RequiredServiceIds,
    string SignedInstallationReceiptSha256, string PayloadSha256, long PayloadBytes);

/// <summary>
/// Supplied only by the original registered platform owner after it has verified the exact
/// protected publisher policy, signed descriptor, installed receipt, and complete payload.
/// The opaque evidence must remain owned by that same channel; metadata cannot recreate it.
/// </summary>
internal sealed record HomePackageOriginalArtifactMaterial(
    ReadOnlyMemory<byte> SignedDescriptorBytes, ReadOnlyMemory<byte> DescriptorPayloadBytes,
    string CatalogueRevision, object OriginalEvidence);

internal sealed record HomePackageOriginalActionBinding(
    string TargetAppId, string ActionId, string RequiredInstalledServiceId, IReadOnlyList<ResourceScope> Scopes);

/// <summary>
/// This port has no default implementation or registration. The real platform owner must use
/// the authenticated original root channel and the one genuinely device-wide canonical store.
/// It may not treat an IsVerified field, a request ID, or this in-process interface as root authority.
/// </summary>
internal interface IHomePackageOriginalPlatformOwner
{
    IHomeCoreStateStore DeviceStore { get; }
    HomePackageDatabase Database { get; }
    ValueTask<HomePackageOriginalArtifactMaterial?> ResolveOriginalArtifactAsync(
        HomePackageActionRequest originalRequest, HomePackageDatabaseSnapshot originalRegistry,
        CancellationToken cancellationToken);
    HomePackageOriginalActionBinding? ResolveOriginalAction(HomePackageAction action,
        HomePackageArtifactDescriptor descriptor);
    Task DemandOriginalArtifactAndChannelAsync(HomePackageArtifactSelection selection,
        AuthenticatedResourceActor originalActor, HomeNativeInstalledPeer originalCaller,
        string originalSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Retain the exact original root task. Before every privileged effect the genuine endpoint
    /// reauthenticates its existing channel and original actor/install/approval tuple. The owner
    /// reserves the idempotency journal in Database and uses an actual non-reentrant raw-current
    /// transaction guard for consequential commit. If that port is unavailable, refuse before effects.
    /// Broker/Home reads must never be made from a callback beneath the Home store writer lease.
    /// </summary>
    Task<HomePackageActionResult> ExecuteAndCommitOriginalAsync(
        HomePackageOriginalInvocation originalInvocation, CancellationToken cancellationToken);
}

/// <summary>Same-owner artifact selection; the constructor and original evidence are not serializable grants.</summary>
internal sealed class HomePackageArtifactSelection
{
    private readonly IHomePackageOriginalPlatformOwner _owner;
    private readonly byte[] _signedDescriptor;
    private readonly byte[] _payloadDescriptor;
    internal readonly object OriginalEvidence;
    internal HomePackageArtifactDescriptor Descriptor { get; }
    internal string CatalogueRevision { get; }
    internal string SignedDescriptorSha256 { get; }
    internal string DescriptorPayloadSha256 { get; }

    private HomePackageArtifactSelection(IHomePackageOriginalPlatformOwner owner,
        HomePackageOriginalArtifactMaterial material, HomePackageArtifactDescriptor descriptor,
        byte[] signedDescriptor, byte[] payloadDescriptor)
    {
        _owner = owner; OriginalEvidence = material.OriginalEvidence;
        Descriptor = descriptor; CatalogueRevision = material.CatalogueRevision;
        _signedDescriptor = signedDescriptor; _payloadDescriptor = payloadDescriptor;
        SignedDescriptorSha256 = Digest(signedDescriptor);
        DescriptorPayloadSha256 = Digest(payloadDescriptor);
    }

    internal bool IssuedBy(IHomePackageOriginalPlatformOwner owner) => ReferenceEquals(owner, _owner);
    internal ReadOnlyMemory<byte> CopySignedDescriptor() => _signedDescriptor.ToArray();
    internal ReadOnlyMemory<byte> CopyDescriptorPayload() => _payloadDescriptor.ToArray();

    internal static HomePackageArtifactSelection Capture(IHomePackageOriginalPlatformOwner owner,
        HomePackageOriginalArtifactMaterial material, HomePackageActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(material.OriginalEvidence);
        var parsed = HomePackageOriginalArtifactDescriptorParser.Parse(material.SignedDescriptorBytes, material.DescriptorPayloadBytes, material.CatalogueRevision, request);
        var descriptor = parsed.Descriptor; var signed = parsed.SignedDescriptor; var payload = parsed.Payload;
        return new(owner, material, descriptor, signed, payload);
    }

    internal static bool Identifier(string? value) => value is { Length: > 0 and <= 128 } &&
        (value[0] is >= 'a' and <= 'z' or >= '0' and <= '9') &&
        value.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');
    internal static bool Text(string? value, int maximum) => value is { Length: > 0 } &&
        value.Length <= maximum && value == value.Trim() && !value.Any(char.IsControl);
    internal static bool Sha256(string? value) => value is { Length: 64 } &&
        value.All(ch => ch is >= 'a' and <= 'f' or >= 'A' and <= 'F' or >= '0' and <= '9');
    internal static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
