using System.Text.Json;
using System.Text.Json.Serialization;
using HavenOS.Home.Apps;

namespace HavenOS.Home.Core;

/// <summary>Versioned read envelope. Correlation and permission-request IDs are observations,
/// never caller, actor, lease, grant or installed-package authority.</summary>
public sealed record HomeUnixCoreRequest(string Protocol, string RequestId, string Operation,
    string? ServiceId = null, HomeCompatibilityRequest? Compatibility = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HomePackageActionRequest? PackageRequest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PackageOperationId = null);
public sealed record HomeUnixCoreResponse(string Protocol, string RequestId, string Operation,
    string Code, JsonElement Result);

internal static class HomeUnixCoreProtocol
{
    internal const string Version = "9to1.Home.Core.Read/1";
    internal const string PackageVersion = "9to1.Home.Core.Package/1";
    internal const int MaximumMessageBytes = 64 * 1024;
    internal static readonly JsonSerializerOptions Json = new()
    {
        MaxDepth = 24,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    internal static bool HasProtocol(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 24 });
        return document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.EnumerateObject().Any(property => property.Name == "Protocol");
    }
    internal static HomeUnixCoreRequest ReadRequest(byte[] payload)
    {
        UniqueObject(payload, ["Protocol", "RequestId", "Operation", "ServiceId", "Compatibility", "PackageRequest", "PackageOperationId"]);
        var value = JsonSerializer.Deserialize<HomeUnixCoreRequest>(payload, Json)
            ?? throw new InvalidDataException("Home Core request is absent.");
        if (value.Protocol is not (Version or PackageVersion) || !Correlation(value.RequestId))
            throw new InvalidDataException("Home Core protocol or correlation is invalid.");
        if (value.Protocol == PackageVersion)
        {
            if (value.ServiceId is not null || value.Compatibility is not null) throw new InvalidDataException("Package protocol cannot carry read arguments.");
            if (value.Operation == "BeginPackageRemoval" && value.PackageOperationId is null && value.PackageRequest is { Action: HomePackageAction.Uninstall } removal &&
                HomePackageArtifactSelection.Identifier(removal.PackageId) && HomePackageArtifactSelection.Text(removal.IdempotencyKey, 128) &&
                removal.ExpectedRevision is { Length: > 0 and <= 1024 } && removal.RequestedChannel is null && removal.RequestedVersion is null) return value;
            if (value.Operation is "ContinuePackageRemoval" or "ObservePackageRemoval" or "RecoverPackageRemoval" &&
                value.PackageRequest is null && Correlation(value.PackageOperationId)) return value;
            throw new InvalidDataException("Original package operation arguments are invalid.");
        }
        if (value.PackageRequest is not null || value.PackageOperationId is not null)
            throw new InvalidDataException("The read protocol cannot carry package mutations.");
        switch (value.Operation)
        {
            case "GetState" or "GetServices" when value.ServiceId is null && value.Compatibility is null: break;
            case "GetService" when CanonicalId(value.ServiceId) && value.Compatibility is null: break;
            case "GetCompatibility" when value.ServiceId is null && value.Compatibility is { } request &&
                CanonicalId(request.AppId) && !string.IsNullOrWhiteSpace(request.AppVersion) &&
                request.AppVersion.Length <= 128 && request.RequiredServices is { Count: <= 64 } &&
                request.RequiredServices.All(row => row is not null && CanonicalId(row.ServiceId)): break;
            default: throw new InvalidDataException("Home Core operation arguments are invalid.");
        }
        return value;
    }
    internal static HomeUnixCoreResponse ReadResponse(byte[] payload, HomeUnixCoreRequest request)
    {
        UniqueObject(payload, ["Protocol", "RequestId", "Operation", "Code", "Result"]);
        var value = JsonSerializer.Deserialize<HomeUnixCoreResponse>(payload, Json)
            ?? throw new InvalidDataException("Home Core reply is absent.");
        if (value.Protocol != request.Protocol || value.RequestId != request.RequestId || value.Operation != request.Operation ||
            value.Code != "HomeCoreReply" || value.Result.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Home Core reply does not match the original request.");
        return value;
    }
    internal static byte[] Payload<T>(HomeUnixCoreRequest request, HomeNativeCoreApiResult<T> result)
    {
        var value = new HomeUnixCoreResponse(request.Protocol, request.RequestId, request.Operation, "HomeCoreReply",
            JsonSerializer.SerializeToElement(result, Json));
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (payload.Length is 0 or > MaximumMessageBytes)
            throw new InvalidDataException("Home Core reply exceeds the transport bound.");
        return payload;
    }
    internal static byte[] Payload(HomeUnixCoreRequest request)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        if (payload.Length is 0 or > MaximumMessageBytes)
            throw new InvalidDataException("Home Core request exceeds the transport bound.");
        _ = ReadRequest(payload); // Same strict contract before publication.
        return payload;
    }
    private static void UniqueObject(byte[] payload, string[] allowed)
    {
        if (payload.Length is 0 or > MaximumMessageBytes)
            throw new InvalidDataException("Home Core frame exceeds the transport bound.");
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 24 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Home Core envelope must be an object.");
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new InvalidDataException("Home Core envelope contains an unknown or duplicate field.");
    }
    private static bool Correlation(string? value) => value is { Length: 32 } &&
        Guid.TryParseExact(value, "N", out _) && value.All(character => char.IsAsciiHexDigit(character));
    private static bool CanonicalId(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
}
