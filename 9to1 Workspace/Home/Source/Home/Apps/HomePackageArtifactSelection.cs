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
    private const int MaximumDescriptorBytes = 1024 * 1024;
    private const int MaximumPayloadDescriptorBytes = 64 * 1024;
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
        if (material.SignedDescriptorBytes.Length is < 1 or > MaximumDescriptorBytes ||
            material.DescriptorPayloadBytes.Length is < 1 or > MaximumPayloadDescriptorBytes ||
            !Text(material.CatalogueRevision, 1024))
            throw new InvalidDataException("Original package descriptor or catalogue observation exceeds its bound.");

        // Capture before parsing or any later owner await. A mutable supplied buffer cannot
        // alter the reviewed descriptor, its signature binding, or the dispatch digest.
        var signed = material.SignedDescriptorBytes.ToArray();
        var payload = material.DescriptorPayloadBytes.ToArray();
        using var envelope = JsonDocument.Parse(signed, new JsonDocumentOptions { MaxDepth = 8 });
        var root = envelope.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "issuerKeyId", "payload", "schemaVersion", "signature" }) == false ||
            root.GetProperty("schemaVersion").GetInt32() != 1 ||
            !Identifier(root.GetProperty("issuerKeyId").GetString()) ||
            root.GetProperty("payload").GetString() is not { Length: > 0 and <= 87384 } encodedPayload ||
            root.GetProperty("signature").GetString() is not { Length: >= 512 and <= 1368 })
            throw new InvalidDataException("The original signed descriptor envelope is unsupported.");
        var signature = Convert.FromBase64String(root.GetProperty("signature").GetString()!);
        if (signature.Length is < 384 or > 1024)
            throw new InvalidDataException("The original descriptor signature shape is unsupported.");
        var envelopePayload = Convert.FromBase64String(encodedPayload);
        if (!envelopePayload.AsSpan().SequenceEqual(payload))
            throw new InvalidDataException("The original signed envelope and descriptor payload differ.");

        var descriptor = JsonSerializer.Deserialize<HomePackageArtifactDescriptor>(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) {
                MaxDepth = 12,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
            }) ?? throw new InvalidDataException("The original package descriptor is empty.");
        if (descriptor.SchemaVersion != 1 || !Identifier(descriptor.PackageId) ||
            !Identifier(descriptor.AppId) || !Text(descriptor.Version, 256) ||
            !Text(descriptor.Channel, 128) || !Identifier(descriptor.ComponentClass) ||
            !Identifier(descriptor.Platform) || !Identifier(descriptor.Abi) ||
            !Sha256(descriptor.SignedInstallationReceiptSha256) || !Sha256(descriptor.PayloadSha256) ||
            descriptor.PayloadBytes is < 1 or > 8L * 1024 * 1024 * 1024 ||
            descriptor.PackageId != request.PackageId ||
            (request.RequestedVersion is not null && descriptor.Version != request.RequestedVersion) ||
            (request.RequestedChannel is not null && descriptor.Channel != request.RequestedChannel) ||
            (request.ExpectedRevision is not null && material.CatalogueRevision != request.ExpectedRevision))
            throw new InvalidDataException("The exact original package selection or catalogue revision changed.");
        var dependencies = Bounded(descriptor.Dependencies, 128);
        var services = Bounded(descriptor.RequiredServiceIds, 128);
        if (dependencies.Any(item => item is null || !Identifier(item.PackageId) ||
                item.MinimumVersion is not null && !Text(item.MinimumVersion, 256) ||
                item.MaximumVersionExclusive is not null && !Text(item.MaximumVersionExclusive, 256)) ||
            dependencies.Select(item => item.PackageId).Distinct(StringComparer.Ordinal).Count() != dependencies.Length ||
            services.Any(item => !Identifier(item)) ||
            services.Distinct(StringComparer.Ordinal).Count() != services.Length)
            throw new InvalidDataException("Package dependency or required service metadata is invalid.");
        descriptor = descriptor with {
            Dependencies = Array.AsReadOnly(dependencies.Select(item => item with { }).ToArray()),
            RequiredServiceIds = Array.AsReadOnly(services)
        };
        return new(owner, material, descriptor, signed, payload);
    }

    private static T[] Bounded<T>(IReadOnlyList<T>? source, int maximum)
    {
        if (source is null) throw new InvalidDataException("Package descriptor collection missing.");
        var values = new List<T>();
        foreach (var item in source)
        {
            if (values.Count == maximum) throw new InvalidDataException("Package descriptor collection exceeds its bound.");
            values.Add(item);
        }
        return values.ToArray();
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
