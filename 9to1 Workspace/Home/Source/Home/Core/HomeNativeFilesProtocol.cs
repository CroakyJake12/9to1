using System.Text.Json;
using System.Text.Json.Serialization;
namespace HavenOS.Home.Core;
internal sealed record HomeNativeFilesFrame(string Protocol, string RequestId, HomeNativeFilesRequest Request);
internal sealed record HomeNativeFilesResponse(string Protocol, string RequestId, HomeNativeFilesReply Reply);
internal static class HomeNativeFilesProtocol
{
    internal const string Version = "9to1.Home.Files.Native/1";
    internal const int MaximumBytes = 64 * 1024;
    internal static readonly JsonSerializerOptions Json = new()
    { MaxDepth = 24, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static bool IsFilesFrame(byte[] payload)
    {
        if (payload.Length is 0 or > MaximumBytes) throw new InvalidDataException("Files frame exceeds its bound.");
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 24 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
        return document.RootElement.TryGetProperty("Protocol", out var protocol) &&
            protocol.ValueKind == JsonValueKind.String && protocol.GetString() == Version;
    }
    internal static HomeNativeFilesFrame ReadRequest(byte[] payload)
    {
        Object(payload);
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 24 });
        Names(document.RootElement, ["Protocol", "RequestId", "Request"]);
        if (!document.RootElement.TryGetProperty("Request", out var request))
            throw new InvalidDataException("The original Files request is absent.");
        Names(request, ["Operation", "OriginalPage", "SelectedItem", "Search", "Offset"]);
        var frame = JsonSerializer.Deserialize<HomeNativeFilesFrame>(payload, Json)
            ?? throw new InvalidDataException("The original Files frame is absent.");
        if (frame.Protocol != Version || !Correlation(frame.RequestId))
            throw new InvalidDataException("Files protocol/correlation is invalid.");
        Validate(frame.Request);
        return frame;
    }
    internal static void Validate(HomeNativeFilesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Search is null || request.Search.Length > 256 || request.Search.Contains('\0') ||
            request.OriginalPage == Guid.Empty || request.SelectedItem == Guid.Empty || request.Offset is < 0 or > 100)
            throw new InvalidDataException("Files request arguments exceed their original bounds.");
        switch (request.Operation)
        {
            case "BrowseRoot" when request.OriginalPage is null && request.SelectedItem is null && request.Offset == 0: break;
            case "OpenFolder" when request.OriginalPage is not null && request.SelectedItem is not null &&
                request.Search.Length == 0 && request.Offset == 0: break;
            case "Up" or "Revalidate" when request.OriginalPage is not null && request.SelectedItem is null &&
                request.Search.Length == 0 && request.Offset == 0: break;
            case "Refresh" when request.OriginalPage is not null && request.SelectedItem is null && request.Offset == 0: break;
            case "NextPage" when request.OriginalPage is not null && request.SelectedItem is null && request.Search.Length == 0: break;
            default: throw new InvalidDataException("Unsupported or ambiguous Files operation.");
        }
    }
    internal static byte[] Payload(HomeNativeFilesFrame frame)
    { var payload = JsonSerializer.SerializeToUtf8Bytes(frame, Json); _ = ReadRequest(payload); return payload; }
    internal static byte[] Payload(HomeNativeFilesFrame frame, HomeNativeFilesReply reply)
    {
        ValidateReply(reply);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new HomeNativeFilesResponse(Version, frame.RequestId, reply), Json);
        if (payload.Length is 0 or > MaximumBytes) throw new InvalidDataException("Files reply exceeds its exact frame bound.");
        return payload;
    }
    internal static HomeNativeFilesReply ReadResponse(byte[] payload, HomeNativeFilesFrame frame)
    {
        Object(payload);
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 24 });
        Names(document.RootElement, ["Protocol", "RequestId", "Reply"]);
        var response = JsonSerializer.Deserialize<HomeNativeFilesResponse>(payload, Json)
            ?? throw new InvalidDataException("Files response is absent.");
        if (response.Protocol != Version || response.RequestId != frame.RequestId)
            throw new InvalidDataException("Files response does not match this original request.");
        ValidateReply(response.Reply); return response.Reply;
    }
    internal static void ValidateReply(HomeNativeFilesReply reply)
    {
        if (reply is null || reply.State is not ("Succeeded" or "AwaitingApproval" or "Denied" or "Unavailable") ||
            !Text(reply.Code, 128) || reply.Message is null || reply.Message.Length > 4096 ||
            reply.PermissionRequestId is { } permission && !Correlation(permission) ||
            reply.State == "AwaitingApproval" && reply.PermissionRequestId is null ||
            (reply.State == "Succeeded") != (reply.Page is not null))
            throw new InvalidDataException("Files reply shape is invalid.");
        if (reply.Page is not { } page) return;
        if (page.OriginalPage == Guid.Empty || page.StoreId == Guid.Empty || !Text(page.StoreRevision, 4096) ||
            page.ParentId == Guid.Empty || page.Title is null || page.Title.Length > 4096 ||
            page.NextOffset is < 0 or > 100 || page.HasMore != (page.NextOffset is not null) ||
            page.Items is null || page.Items.Count > 20 || page.Items.Any(item => item is null ||
                item.ItemId == Guid.Empty || item.ParentId == Guid.Empty || item.MetadataRevision == Guid.Empty ||
                item.Name is null || item.Name.Length > 4096 || item.ContentType is { Length: > 1024 } ||
                item.SizeBytes < 0 || item.ContentHash is { Length: > 256 } ||
                item.Kind is not ("File" or "Folder" or "Artifact" or "Reference" or "SymbolicLink") ||
                item.Availability is not ("CloudOnly" or "Hydrating" or "AvailableOffline" or "AlwaysAvailable" or
                    "LocalChanges" or "Uploading" or "Synced" or "Conflict" or "Paused" or "Error")) ||
            page.Items.Select(item => item.ItemId).Distinct().Count() != page.Items.Count)
            throw new InvalidDataException("Files page observation is invalid.");
    }
    private static void Object(byte[] payload)
    {
        if (payload.Length is 0 or > MaximumBytes) throw new InvalidDataException("Files frame exceeds its bound.");
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 24 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Files frame is not an object.");
        Unique(document.RootElement);
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!seen.Add(property.Name)) throw new InvalidDataException("Files JSON has duplicate fields."); Unique(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    private static void Names(JsonElement value, string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            value.EnumerateObject().Any(property => !names.Contains(property.Name, StringComparer.Ordinal)))
            throw new InvalidDataException("Files envelope has unexpected fields.");
    }
    private static bool Correlation(string? value) => value is { Length: 32 } &&
        Guid.TryParseExact(value, "N", out _) && value.All(char.IsAsciiHexDigit);
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && value == value.Trim();
}
