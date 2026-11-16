using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using HavenOS.Files;

namespace NineToOne.Web.Files;

/// <summary>Translates the private same-origin Files API into the canonical provider interface.</summary>
[SupportedOSPlatform("browser")]
internal sealed class FilesDriveBrowserProvider(FilesBrowserTransport transport, FilesDriveContext context)
    : IFilesProvider, IFilesBrowserDrive
{
    public FilesLocation Location => context.Location;
    public FilesDriveContext Context => context;

    public Task<FilesPage<HostedItemMetadata>> ListAsync(HostedItemId? parentId, FilesSearchQuery? query,
        string? pageToken, CancellationToken cancellationToken) =>
        transport.CallAsync<FilesPage<HostedItemMetadata>>("list",
            new { parentId = parentId?.Value, query = query?.Text, pageToken }, cancellationToken);

    public Task<FilesPage<HostedItemMetadata>> ListTrashAsync(string? pageToken, CancellationToken cancellationToken) =>
        transport.CallAsync<FilesPage<HostedItemMetadata>>("list", new { trash = true, pageToken }, cancellationToken);

    public Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId itemId, CancellationToken cancellationToken) =>
        ReadResultAsync<HostedItemMetadata>("get", new { id = itemId.Value }, cancellationToken);

    public Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? newName, CancellationToken cancellationToken) =>
        ReadResultAsync<FilesOperation>("mutate", new { operation, newName }, cancellationToken);

    private async Task<FilesResult<T>> ReadResultAsync<T>(string action, object arguments, CancellationToken cancellationToken)
    {
        try { return await transport.CallAsync<FilesResult<T>>(action, arguments, cancellationToken); }
        catch (FilesBrowserRequestException error) { return FilesResult<T>.Failure(error.Error); }
    }

    public IAsyncEnumerable<FilesChangeEvent> SubscribeAsync(FilesChangeCursor? after, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Google Drive live changes are unavailable in Files.");

    public Task<FilesPage<FilesChangeEvent>> GetChangesAsync(FilesChangeCursor? after, int limit, CancellationToken cancellationToken) =>
        Location.Capabilities.Supports(FilesProviderCapabilities.ChangeFeed)
            ? transport.CallAsync<FilesPage<FilesChangeEvent>>("changes", new { after = after?.Revision, limit }, cancellationToken)
            : throw new NotSupportedException("Google Drive changes are unavailable in Files.");

    public async Task<FilesDriveContext> RefreshContextAsync(CancellationToken cancellationToken)
    {
        var current = await transport.CallAsync<FilesDriveContext>("context", null, cancellationToken);
        current.Validate();
        if (!StringComparer.Ordinal.Equals(current.AccountId, context.AccountId) || current.Location.Id != context.Location.Id)
            throw FilesBrowserRequestException.PermissionDenied();
        return context = current;
    }

    public Task<FilesDriveUsage> ReadUsageAsync(CancellationToken cancellationToken) =>
        transport.CallAsync<FilesDriveUsage>("usage", null, cancellationToken);

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var result = await transport.CallAsync<ConnectResult>("connect", null, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        transport.OpenGoogleAuthorization(result.Url);
    }

    public async Task<FilesDriveDisconnectOutcome> DisconnectAsync(CancellationToken cancellationToken)
    {
        var outcome = FilesDriveDisconnectOutcome.Disconnected;
        try { await transport.CallAsync<JsonElement>("disconnect", null, cancellationToken); }
        catch (FilesBrowserRequestException error) when (error.ServiceCode == "DriveRevocationUnconfirmed")
        {
            outcome = FilesDriveDisconnectOutcome.GoogleRevocationUnconfirmed;
        }
        context = context with { Connected = false, Location = context.Location with { Capabilities = FilesProviderCapabilities.None } };
        return outcome;
    }

    public void OpenPrivacyNotice() => transport.OpenPrivacyNotice();

    public async Task<FilesBrowserUploadFile?> PickUploadAsync(CancellationToken cancellationToken) =>
        (await transport.CallAsync<PickResult>("pickUpload", null, cancellationToken)).File;

    public Task<FilesBrowserUploadProgress> BeginUploadAsync(FilesOperationId operationId, HostedItemId? parentId,
        FilesBrowserUploadFile file, CancellationToken cancellationToken) =>
        transport.CallAsync<FilesBrowserUploadProgress>("beginUpload",
            new { operationId, parentId, name = file.Name, contentType = file.ContentType, sizeBytes = file.SizeBytes },
            cancellationToken);

    public Task<FilesBrowserUploadProgress> ReadUploadAsync(FilesTransferId transferId, CancellationToken cancellationToken) =>
        transport.CallAsync<FilesBrowserUploadProgress>("uploadStatus", new { id = transferId.Value }, cancellationToken);

    public async Task<FilesBrowserUploadProgress?> FindUploadAsync(FilesOperationId operationId, CancellationToken cancellationToken)
    {
        try
        {
            return await transport.CallAsync<FilesBrowserUploadProgress>("findUpload", new { id = operationId.Value }, cancellationToken);
        }
        catch (FilesBrowserRequestException error) when (error.Error.Code == FilesErrorCode.ItemNotFound)
        {
            return null;
        }
    }

    public Task<FilesBrowserUploadProgress> WriteUploadChunkAsync(FilesTransferId transferId, string handle, long offset, int length,
        CancellationToken cancellationToken) =>
        transport.CallAsync<FilesBrowserUploadProgress>("uploadChunk",
            new { id = transferId.Value, handle, offset, length }, cancellationToken);

    public Task<FilesBrowserUploadProgress> CancelUploadAsync(FilesTransferId transferId, CancellationToken cancellationToken) =>
        transport.CallAsync<FilesBrowserUploadProgress>("cancelUpload", new { id = transferId.Value }, cancellationToken);

    public void ReleaseUpload(string handle) => transport.ReleaseUpload(handle);

    public Task<FilesBrowserDownload> PrepareDownloadAsync(HostedItemId itemId, string? exportMime, CancellationToken cancellationToken) =>
        transport.CallAsync<FilesBrowserDownload>("prepareDownload", new { itemId, exportMime }, cancellationToken);

    public void OpenDownload(FilesBrowserDownload download) => transport.OpenDownload(download.TransferId, download.Url);

    private sealed record ConnectResult(string Url);
    private sealed record PickResult(FilesBrowserUploadFile? File);
}

/// <summary>Owns the browser request group for one private Files registration.</summary>
[SupportedOSPlatform("browser")]
internal sealed partial class FilesBrowserTransport
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };
    private readonly string _group = Guid.NewGuid().ToString("N");
    private bool _revoked;

    public FilesBrowserTransport() => CreateGroup(_group);
    public void Enroll() => MarkEnrolled(_group);

    public void Revoke()
    {
        if (_revoked) return;
        _revoked = true;
        RevokeGroup(_group);
    }

    public async ValueTask DrainAsync() => await DrainGroup(_group);

    public async Task<T> CallAsync<T>(string action, object? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_revoked) throw FilesBrowserRequestException.PermissionDenied();
        var id = Guid.NewGuid().ToString("N");
        var work = Invoke(_group, id, action, JsonSerializer.Serialize(arguments, Wire));
        using var cancel = cancellationToken.Register(() => Cancel(_group, id));
        string json;
        try { json = await work; }
        catch (JSException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        cancellationToken.ThrowIfCancellationRequested();
        var response = JsonSerializer.Deserialize<TransportResponse>(json, Wire)
            ?? throw new JsonException("The Files response was empty.");
        if (response.Status is 401 or 403) throw FilesBrowserRequestException.PermissionDenied();
        if (response.Status is < 200 or >= 300) throw ReadError(action, response);
        return JsonSerializer.Deserialize<T>(response.Body, Wire)
            ?? throw new JsonException("The Files response did not contain the requested data.");
    }

    public void OpenGoogleAuthorization(string url)
    {
        if (_revoked) throw FilesBrowserRequestException.PermissionDenied();
        Navigate(_group, url);
    }

    public void OpenPrivacyNotice()
    {
        if (_revoked) throw FilesBrowserRequestException.PermissionDenied();
        NavigatePrivacy(_group);
    }

    public void ReleaseUpload(string handle) => Release(_group, handle);

    public void OpenDownload(FilesTransferId transferId, string url)
    {
        if (_revoked || transferId.Value == Guid.Empty) throw FilesBrowserRequestException.PermissionDenied();
        Download(_group, transferId.Value.ToString("D"), url);
    }

    private static FilesBrowserRequestException ReadError(string action, TransportResponse response)
    {
        TransportError? error = null;
        if (!string.IsNullOrWhiteSpace(response.Body))
        {
            try { error = JsonSerializer.Deserialize<TransportError>(response.Body, Wire); }
            catch (JsonException) { /* Unrecognised errors use a bounded, generic message. */ }
        }
        if (error is not null)
        {
            var uploadTooLarge = action == "beginUpload" && response.Status == 413 && error.Code == "UploadTooLarge";
            if (error.Code == "QuotaExceeded" || uploadTooLarge)
            {
                var now = DateTimeOffset.UtcNow;
                var retryAt = FilesDriveUsage.AsTime(error.RetryAt);
                var retryAfter = retryAt is { } time && time > now ? time - now : (TimeSpan?)null;
                var message = "The Files allowance has been reached.";
                if (uploadTooLarge)
                {
                    var supplied = error.Message?.Trim();
                    message = supplied is { Length: > 0 and <= 240 } && !supplied.Any(char.IsControl)
                        ? supplied
                        : "This file is larger than the Files upload limit. Choose a smaller file.";
                }
                var errorCode = uploadTooLarge ? FilesErrorCode.InvalidState : FilesErrorCode.QuotaExceeded;
                return new(new(errorCode, message, action, null, true, retryAfter.HasValue,
                    retryAfter, error.NotStarted ? new Dictionary<string, string> { ["notStarted"] = "true" } : null))
                {
                    ServiceCode = error.Code,
                };
            }
        }
        var code = error?.Code switch
        {
            "ItemNotFound" => FilesErrorCode.ItemNotFound,
            "RevisionConflict" => FilesErrorCode.RevisionConflict,
            "NameConflict" => FilesErrorCode.NameConflict,
            "InvalidName" => FilesErrorCode.InvalidName,
            "InvalidState" => FilesErrorCode.InvalidState,
            "ConflictRequiresDecision" => FilesErrorCode.ConflictRequiresDecision,
            "ProviderCapabilityUnsupported" => FilesErrorCode.ProviderCapabilityUnsupported,
            "TransferInterrupted" => FilesErrorCode.TransferInterrupted,
            "StorageUnavailable" => FilesErrorCode.StorageUnavailable,
            _ => FilesErrorCode.ProviderUnavailable,
        };
        return new(new(code, "Google Drive could not perform this Files action.", action, null, true, false))
        {
            ServiceCode = error?.Code,
        };
    }

    private sealed record TransportResponse(int Status, string Body);
    private sealed record TransportError(string Code, string? Message, long? RetryAt, bool NotStarted);

    [JSImport("createGroup", "nineToOneFiles")]
    private static partial void CreateGroup(string group);
    [JSImport("enroll", "nineToOneFiles")]
    private static partial void MarkEnrolled(string group);
    [JSImport("revoke", "nineToOneFiles")]
    private static partial void RevokeGroup(string group);
    [JSImport("drain", "nineToOneFiles")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task DrainGroup(string group);
    [JSImport("invoke", "nineToOneFiles")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Invoke(string group, string id, string action, string arguments);
    [JSImport("cancel", "nineToOneFiles")]
    private static partial void Cancel(string group, string id);
    [JSImport("openGoogleAuthorization", "nineToOneFiles")]
    private static partial void Navigate(string group, string url);
    [JSImport("openPrivacyNotice", "nineToOneFiles")]
    private static partial void NavigatePrivacy(string group);
    [JSImport("releaseUpload", "nineToOneFiles")]
    private static partial void Release(string group, string handle);
    [JSImport("openDownload", "nineToOneFiles")]
    private static partial void Download(string group, string transferId, string url);
}
