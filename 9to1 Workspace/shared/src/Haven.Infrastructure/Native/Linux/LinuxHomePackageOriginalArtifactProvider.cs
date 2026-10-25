using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>Concrete artifact observations from the SAME canonical retained publisher
/// catalogue. No catalogue, certificate, key, machine path or action mapping is invented.
/// An enrollment row is data: the authenticated Root must independently verify its
/// enrolled publisher, exact signature, payload, dependencies and privilege before effects.
/// This producer alone never enables installation or authenticates a native caller.</summary>
public sealed class LinuxHomePackageOriginalArtifactProvider : IHomePackageOriginalArtifactProvider
{
    public sealed record Registration(string PackageId, HomePackageAction Action, HomePackageOriginalActionMap Mapping);
    private readonly HomePackageDatabase _database;
    private readonly HomeNativeCoreApiSessions _sessions;
    private readonly string _abi;
    private readonly FrozenDictionary<(string Package, HomePackageAction Action), HomePackageOriginalActionMap> _actions;
    private readonly ConditionalWeakTable<HomePackageOriginalArtifactObservation, Evidence> _issued = new();
    private sealed record Evidence(LinuxHomePackageOriginalArtifactProvider Owner, string RegistrySha256,
        string CatalogueRevision, string SignedSha256, string PayloadSha256);
    internal sealed record Row(HomePackageArtifactDescriptor Descriptor, byte[] Signed, byte[] Payload);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    { MaxDepth = 16, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public LinuxHomePackageOriginalArtifactProvider(HomePackageDatabase sameCanonicalDatabase,
        FileHomeCoreStateStore sameDeviceStore, HomeLocalProfileIdentity sameDeviceProfiles,
        HomeNativeCoreApiSessions originalSessionIssuer, string configuredAbi,
        IEnumerable<Registration> configuredActions)
    {
        ArgumentNullException.ThrowIfNull(sameCanonicalDatabase); ArgumentNullException.ThrowIfNull(sameDeviceStore);
        ArgumentNullException.ThrowIfNull(sameDeviceProfiles); ArgumentNullException.ThrowIfNull(originalSessionIssuer);
        ArgumentNullException.ThrowIfNull(configuredActions);
        if (!sameCanonicalDatabase.HasOriginalProfileComposition(sameDeviceStore, sameDeviceProfiles) || !Identifier(configuredAbi))
            throw new UnauthorizedAccessException("The SAME canonical device database/store/profile and explicit Linux ABI are required.");
        _database = sameCanonicalDatabase; _sessions = originalSessionIssuer; _abi = configuredAbi;
        var maps = new Dictionary<(string, HomePackageAction), HomePackageOriginalActionMap>();
        foreach (var registration in configuredActions)
        {
            if (maps.Count >= 512 || registration is null || !Identifier(registration.PackageId) || !Enum.IsDefined(registration.Action))
                throw new ArgumentException("Package action registrations must be bounded and explicit.", nameof(configuredActions));
            var map = registration.Mapping ?? throw new ArgumentException("The original package action mapping is absent.");
            var scopes = map.Scopes?.Take(129).Select(scope => scope is not null ? scope with { }
                : throw new ArgumentException("The original package action scope is absent.")).ToArray();
            if (!Text(map.TargetAppId, 128) || !Text(map.ActionId, 256) || !Identifier(map.RequiredInstalledServiceId) ||
                scopes is not { Length: > 0 and <= 128 } || scopes.Any(scope =>
                    !Text(scope.Kind, 256) || !Text(scope.Id, 1024) || !Text(scope.Revision, 1024) || !Enum.IsDefined(scope.Access)) ||
                !maps.TryAdd((registration.PackageId, registration.Action), map with { Scopes = Array.AsReadOnly(scopes) }))
                throw new ArgumentException("The exact package action mapping/scopes are invalid or duplicated.", nameof(configuredActions));
        }
        _actions = maps.ToFrozenDictionary();
    }

    public async ValueTask<HomePackageOriginalArtifactObservation?> ResolveAsync(HomePackageActionRequest request,
        HomePackageDatabaseSnapshot originalDeviceRegistry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(originalDeviceRegistry);
        cancellationToken.ThrowIfCancellationRequested();
        var current = await ReadCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var fingerprint = Digest(JsonSerializer.SerializeToUtf8Bytes(current));
        if (fingerprint != Digest(JsonSerializer.SerializeToUtf8Bytes(originalDeviceRegistry)))
            throw new UnauthorizedAccessException("The package selection must use the SAME current canonical device registry.");
        if (current.UnknownFields is not { } fields || !fields.TryGetValue(HomePackageOriginalPublisherEnrollmentRecord.Field, out var retained))
            return null;
        var enrollment = retained.Deserialize<HomePackageOriginalPublisherEnrollmentRecord>(Web)
            ?? throw new InvalidDataException("The retained original publisher enrollment is empty.");
        var rows = ReadCatalogue(enrollment, _abi, cancellationToken);
        if (!rows.TryGetValue(request.PackageId, out var row) ||
            request.RequestedVersion is not null && request.RequestedVersion != row.Descriptor.Version ||
            request.RequestedChannel is not null && request.RequestedChannel != row.Descriptor.Channel ||
            request.ExpectedRevision is not null && request.ExpectedRevision != enrollment.CatalogueRevision)
            return null;
        // The maintained parser checks this exact request again and detaches bytes.
        var parsed = HomePackageOriginalArtifactDescriptorParser.Parse(row.Signed, row.Payload, enrollment.CatalogueRevision, request);
        var evidence = new Evidence(this, fingerprint, enrollment.CatalogueRevision, Digest(parsed.SignedDescriptor), Digest(parsed.Payload));
        var observation = new HomePackageOriginalArtifactObservation(parsed.SignedDescriptor, parsed.Payload,
            enrollment.CatalogueRevision, evidence);
        _issued.Add(observation, evidence);
        return observation;
    }

    public HomePackageOriginalActionMap? ResolveAction(HomePackageAction action, HomePackageArtifactDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        // Pure configured lookup; never reenter Home, Root, storage or callbacks.
        return descriptor.Platform == "linux" && descriptor.Abi == _abi &&
            _actions.TryGetValue((descriptor.PackageId, action), out var map) ? map : null;
    }

    public async Task DemandOriginalCurrentAsync(HomePackageOriginalArtifactObservation originalArtifact,
        AuthenticatedResourceActor originalActor, HomeNativeInstalledPeer originalCaller,
        string originalSessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalArtifact);
        if (!_issued.TryGetValue(originalArtifact, out var evidence) || !ReferenceEquals(evidence.Owner, this) ||
            !ReferenceEquals(originalArtifact.OriginalProviderEvidence, evidence))
            throw new UnauthorizedAccessException("The exact artifact observation must originate from this canonical catalogue producer.");
        await _sessions.DemandOriginalPackageArtifactSessionCurrentAsync(originalActor, originalCaller,
            originalSessionId, cancellationToken).ConfigureAwait(false);
        var current = await ReadCanonicalAsync(cancellationToken).ConfigureAwait(false);
        if (Digest(JsonSerializer.SerializeToUtf8Bytes(current)) != evidence.RegistrySha256 ||
            originalArtifact.CatalogueRevision != evidence.CatalogueRevision ||
            Digest(originalArtifact.SignedDescriptorBytes.Span) != evidence.SignedSha256 ||
            Digest(originalArtifact.DescriptorPayloadBytes.Span) != evidence.PayloadSha256)
            throw new UnauthorizedAccessException("The original canonical registry/catalogue or reviewed artifact bytes changed.");
        await _sessions.DemandOriginalPackageArtifactSessionCurrentAsync(originalActor, originalCaller,
            originalSessionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HomePackageDatabaseSnapshot> ReadCanonicalAsync(CancellationToken token)
    {
        var read = await _database.ReadAsync(token).ConfigureAwait(false);
        return read.Succeeded && read.Snapshot is { } snapshot ? snapshot
            : throw new InvalidDataException("The canonical package registry requires recovery: " + read.Failure?.Code);
    }

    internal static Dictionary<string, Row> ReadCatalogue(HomePackageOriginalPublisherEnrollmentRecord enrollment,
        string expectedAbi, CancellationToken token)
    {
        if (enrollment.SchemaVersion != HomePackageOriginalPublisherEnrollmentRecord.RetainedCatalogueSchemaVersion ||
            !Identifier(enrollment.IssuerKeyId) || !Text(enrollment.CatalogueRevision, 1024) ||
            !DigestShape(enrollment.CatalogueSha256) || !DigestShape(enrollment.PublisherCertificateSha256) ||
            !Identifier(enrollment.HomePackageId) || !Identifier(enrollment.RootPackageId) ||
            enrollment.HomePackageId == enrollment.RootPackageId || enrollment.OriginalEnrollmentOperationId == Guid.Empty ||
            !Text(enrollment.OriginalActorId, 4096) || !Text(enrollment.OriginalProfileId, 4096) ||
            !Text(enrollment.OriginalOsPrincipal, 4096) || !Text(enrollment.InstallerExecutableIdentity, 4096))
            throw new InvalidDataException("A bounded original retained catalogue enrollment is required; no initial trust is inferred.");
        var catalogueBytes = Decode(enrollment.OriginalSignedCatalogueBase64,
            HomePackageOriginalPublisherEnrollmentRecord.MaximumRetainedCatalogueBytes);
        if (!Digest(catalogueBytes).Equals(enrollment.CatalogueSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The exact retained original catalogue failed integrity.");
        return ReadCatalogueBytes(catalogueBytes, enrollment.IssuerKeyId, enrollment.CatalogueRevision,
            enrollment.HomePackageId, enrollment.RootPackageId, enrollment.SelectedPackageIds, expectedAbi, token);
    }

    internal static Dictionary<string, Row> ReadCatalogueBytes(byte[] catalogueBytes, string issuer, string revision,
        string homePackageId, string rootPackageId, IReadOnlyList<string> selectedPackageIds, string expectedAbi, CancellationToken token)
    {
        if (!Identifier(issuer) || !Text(revision, 1024) || !Identifier(homePackageId) || !Identifier(rootPackageId) ||
            homePackageId == rootPackageId || catalogueBytes.Length is < 1 or > HomePackageOriginalPublisherEnrollmentRecord.MaximumRetainedCatalogueBytes)
            throw new InvalidDataException("The bounded original catalogue identity tuple is invalid.");
        // Reuse the original installer's maintained catalogue DTO; do not introduce
        // another catalogue schema, package database or publisher enrollment format.
        var catalogue = JsonSerializer.Deserialize<NativeWindowsHomeInstallerBootstrapAdmission.Catalogue>(catalogueBytes, Web)
            ?? throw new InvalidDataException("The original retained catalogue is empty.");
        var packages = catalogue.Packages?.Take(129).ToArray();
        var mandatory = catalogue.MandatoryPackageIds?.Take(129).ToArray();
        var selected = selectedPackageIds?.Take(129).ToArray();
        if (catalogue.SchemaVersion != 1 || catalogue.IssuerKeyId != issuer ||
            catalogue.CatalogueRevision != revision || catalogue.HomePackageId != homePackageId ||
            catalogue.RootPackageId != rootPackageId || packages is not { Length: >= 2 and <= 128 } ||
            mandatory is not { Length: >= 2 and <= 128 } || selected is not { Length: >= 2 and <= 128 } ||
            mandatory.Any(id => !Identifier(id)) || selected.Any(id => !Identifier(id)) ||
            mandatory.Distinct(StringComparer.Ordinal).Count() != mandatory.Length ||
            selected.Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new InvalidDataException("The SAME original single-installer catalogue/enrollment tuple is invalid or unbounded.");
        var rows = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            token.ThrowIfCancellationRequested();
            if (package is null) throw new InvalidDataException("The original catalogue contains an absent package.");
            var signed = Decode(package.SignedDescriptorBase64, 1024 * 1024);
            var payload = Decode(package.DescriptorPayloadBase64, 64 * 1024);
            var basic = JsonSerializer.Deserialize<HomePackageArtifactDescriptor>(payload, Web)
                ?? throw new InvalidDataException("The original retained descriptor is empty.");
            var descriptor = HomePackageOriginalArtifactDescriptorParser.Parse(signed, payload, catalogue.CatalogueRevision,
                new(basic.PackageId, HomePackageAction.Install, "retained.catalogue.observation", basic.Version, basic.Channel, catalogue.CatalogueRevision)).Descriptor;
            using var envelope = JsonDocument.Parse(signed, new JsonDocumentOptions { MaxDepth = 8 });
            if (envelope.RootElement.GetProperty("issuerKeyId").GetString() != issuer ||
                descriptor.Platform != "linux" || descriptor.Abi != expectedAbi ||
                !rows.TryAdd(descriptor.PackageId, new(descriptor, signed, payload)))
                throw new InvalidDataException("An original package issuer/platform/ABI tuple is inconsistent or duplicated.");
        }
        if (!rows.TryGetValue(homePackageId, out var home) || home.Descriptor.AppId != "home" ||
            !rows.TryGetValue(rootPackageId, out var root) || root.Descriptor.AppId != "root" ||
            !mandatory.Contains(homePackageId) || !mandatory.Contains(rootPackageId) ||
            mandatory.Any(id => !rows.ContainsKey(id) || !selected.Contains(id)) || selected.Any(id => !rows.ContainsKey(id)))
            throw new InvalidDataException("The original catalogue must retain its genuine Home/Root and all mandatory/selected package identities.");
        return rows;
    }

    private static byte[] Decode(string? encoded, int maximum)
    {
        if (encoded is not { Length: > 0 } || encoded.Length > ((maximum + 2L) / 3) * 4)
            throw new InvalidDataException("The retained original catalogue/descriptor bytes are missing or exceed their bound.");
        var bytes = Convert.FromBase64String(encoded);
        return bytes.Length is > 0 && bytes.Length <= maximum ? bytes
            : throw new InvalidDataException("The retained original catalogue/descriptor exceeds its byte bound.");
    }
    private static bool Identifier(string? value) => value is { Length: > 0 and <= 128 } &&
        (value[0] is >= 'a' and <= 'z' or >= '0' and <= '9') &&
        value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');
    private static bool Text(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum &&
        value == value.Trim() && !value.Any(char.IsControl);
    private static bool DigestShape(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
