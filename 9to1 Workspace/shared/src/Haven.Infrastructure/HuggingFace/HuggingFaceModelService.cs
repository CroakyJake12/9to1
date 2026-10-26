using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Bounded HF catalogue/download client. Its production factory disables automatic
/// redirects; every hop is validated and credentials are attached only to the HF origin.</summary>
public sealed partial class HuggingFaceModelService : IHuggingFaceModelService, IDisposable
{
    public const long MaximumModelBytes = 64L * 1024 * 1024 * 1024;
    private const int MaximumMetadataBytes = 8 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly IProviderSecretStore _secrets;
    private readonly IAppPaths _paths;
    private readonly IHuggingFaceModelImporter _importer;
    private readonly bool _ownsClient;

    /// <summary>Production entry point. Reuses only the maintained protected platform stores.</summary>
    public static HuggingFaceModelService Create(IProviderSecretStore secrets, IAppPaths paths)
    {
        if (secrets is not (LinuxProviderSecretStore or WindowsProviderSecretStore))
            throw Error(HuggingFaceModelError.SecureStorageUnavailable);
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30), MaxResponseContentBufferSize = MaximumMetadataBytes };
        return new(http, secrets, paths, new HuggingFaceModelCtlImporter(), ownsClient: true);
    }

    /// <summary>Injected clients MUST disable automatic redirects and default credentials/headers.
    /// The factory above is the production registration; injection supports transport controls.</summary>
    public HuggingFaceModelService(HttpClient http, IProviderSecretStore secrets, IAppPaths paths,
        IHuggingFaceModelImporter importer, bool ownsClient = false)
    {
        _http = http; _secrets = secrets; _paths = paths; _importer = importer; _ownsClient = ownsClient;
        if (http.DefaultRequestHeaders.Authorization is not null || http.DefaultRequestHeaders.Contains("Cookie"))
            throw Error(HuggingFaceModelError.InvalidRequest);
    }

    public async Task<IReadOnlyList<HuggingFaceModelSummary>> SearchAsync(string profileId, string query, CancellationToken cancellationToken)
    {
        Profile(profileId);
        if (query is null || query.Length > 200 || query.Any(char.IsControl)) throw Error(HuggingFaceModelError.InvalidRequest);
        return await Guard(async () =>
        {
            using var json = await MetadataAsync(new Uri("https://huggingface.co/api/models?filter=gguf&limit=40&sort=downloads&direction=-1&search=" + Uri.EscapeDataString(query)), null, cancellationToken);
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 40)
                throw Error(HuggingFaceModelError.InvalidMetadata);
            var rows = new List<HuggingFaceModelSummary>();
            foreach (var row in json.RootElement.EnumerateArray())
            {
                var id = Text(row, "id");
                if (!RepositoryPattern().IsMatch(id)) continue;
                rows.Add(new(id, Number(row, "downloads"), Number(row, "likes"), Gated(row)));
            }
            return (IReadOnlyList<HuggingFaceModelSummary>)rows.AsReadOnly();
        });
    }

    public Task<HuggingFaceModelDetails> GetDetailsAsync(string profileId, string repositoryId, CancellationToken cancellationToken)
    {
        Profile(profileId); Repository(repositoryId);
        return Guard(async () => await DetailsAsync(profileId, repositoryId, null, cancellationToken));
    }

    private async Task<HuggingFaceModelDetails> DetailsAsync(string profileId, string repositoryId, string? commit, CancellationToken ct)
    {
        var token = await ReadTokenAsync(profileId, ct);
        var route = "https://huggingface.co/api/models/" + RepoRoute(repositoryId) +
            (commit is null ? "" : "/revision/" + commit) + "?blobs=true";
        using var json = await MetadataAsync(new Uri(route), token, ct);
        var row = json.RootElement;
        var sha = Text(row, "sha");
        if (!CommitPattern().IsMatch(sha) || (commit is not null && !string.Equals(commit, sha, StringComparison.OrdinalIgnoreCase)) ||
            Text(row, "id") != repositoryId) throw Error(HuggingFaceModelError.InvalidMetadata);
        var files = new List<HuggingFaceModelFile>();
        if (row.TryGetProperty("siblings", out var siblings) && siblings.ValueKind == JsonValueKind.Array)
            foreach (var sibling in siblings.EnumerateArray())
            {
                var path = Text(sibling, "rfilename");
                if (!SafeFile(path) || !sibling.TryGetProperty("lfs", out var lfs) || lfs.ValueKind != JsonValueKind.Object) continue;
                var hash = Text(lfs, "sha256"); var size = Number(lfs, "size");
                if (HashPattern().IsMatch(hash) && size >= 8 && size <= MaximumModelBytes)
                    files.Add(new(path, size, hash.ToLowerInvariant()));
            }
        if (files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw Error(HuggingFaceModelError.InvalidMetadata);
        var license = "review-required";
        if (row.TryGetProperty("cardData", out var card) && card.ValueKind == JsonValueKind.Object)
        {
            var value = Text(card, "license");
            if (value.Length is > 0 and <= 128 && !value.Any(char.IsControl)) license = value;
        }
        return new(repositoryId, sha.ToLowerInvariant(), license, Gated(row), files.AsReadOnly());
    }

    public Task<HuggingFaceInstallResult> InstallAsync(string profileId, HuggingFaceModelDetails model,
        HuggingFaceModelFile file, IProgress<HuggingFaceInstallProgress>? progress, CancellationToken cancellationToken)
    {
        Profile(profileId);
        if (model is null || file is null) throw Error(HuggingFaceModelError.InvalidRequest);
        Repository(model.RepositoryId);
        if (!CommitPattern().IsMatch(model.CommitSha) || !SafeFile(file.Path) || !HashPattern().IsMatch(file.Sha256) ||
            file.Size < 8 || file.Size > MaximumModelBytes) throw Error(HuggingFaceModelError.InvalidRequest);
        return Guard(async () =>
        {
            var pinned = await DetailsAsync(profileId, model.RepositoryId, model.CommitSha, cancellationToken);
            var selected = pinned.Files.SingleOrDefault(f => f.Path == file.Path);
            if (selected is null || selected.Size != file.Size || !selected.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw Error(HuggingFaceModelError.InvalidMetadata);
            var staging = CreateStaging();
            try
            {
                var target = Path.Combine(staging, "download.gguf");
                var token = await ReadTokenAsync(profileId, cancellationToken);
                var uri = new Uri("https://huggingface.co/" + RepoRoute(pinned.RepositoryId) + "/resolve/" + pinned.CommitSha + "/" +
                    string.Join('/', selected.Path.Split('/').Select(Uri.EscapeDataString)));
                using (var response = await SendAsync(uri, token, cancellationToken))
                {
                    if (response.Content.Headers.ContentLength is { } length && length != selected.Size)
                        throw Error(HuggingFaceModelError.IntegrityFailure);
                    if (response.Content.Headers.ContentEncoding.Count != 0) throw Error(HuggingFaceModelError.DownloadRejected);
                    await DownloadAsync(response, target, selected, progress, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(HuggingFaceInstallPhase.Importing, selected.Size, selected.Size));
                var source = "https://huggingface.co/" + pinned.RepositoryId + "/blob/" + pinned.CommitSha + "/" +
                    string.Join('/', selected.Path.Split('/').Select(Uri.EscapeDataString));
                var id = "hf-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pinned.RepositoryId + "\n" + selected.Path + "\n" + selected.Sha256)))[..32];
                await _importer.ImportAsync(new(target, id, pinned.RepositoryId + "/" + selected.Path, pinned.License,
                    source, selected.Sha256, selected.Size), cancellationToken);
                progress?.Report(new(HuggingFaceInstallPhase.Installed, selected.Size, selected.Size));
                return new HuggingFaceInstallResult(id, pinned.RepositoryId, pinned.CommitSha, selected.Path, selected.Sha256, selected.Size);
            }
            finally { Directory.Delete(staging, recursive: true); }
        });
    }

    private static async Task DownloadAsync(HttpResponseMessage response, string target, HuggingFaceModelFile file,
        IProgress<HuggingFaceInstallProgress>? progress, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        var header = new byte[8]; var headerCount = 0; long received = 0;
        progress?.Report(new(HuggingFaceInstallPhase.Downloading, 0, file.Size));
        await using (var output = new FileStream(target, options))
        {
            var buffer = new byte[128 * 1024];
            while (true)
            {
                var count = await input.ReadAsync(buffer, ct);
                if (count == 0) break;
                if (count > file.Size - received) throw Error(HuggingFaceModelError.IntegrityFailure);
                var take = Math.Min(count, 8 - headerCount);
                buffer.AsSpan(0, take).CopyTo(header.AsSpan(headerCount)); headerCount += take;
                hash.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
                received += count;
                progress?.Report(new(HuggingFaceInstallPhase.Downloading, received, file.Size));
            }
            await output.FlushAsync(ct);
        }
        progress?.Report(new(HuggingFaceInstallPhase.Validating, received, file.Size));
        if (received != file.Size || headerCount != 8 || !header.AsSpan(0, 4).SequenceEqual("GGUF"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) is not (2 or 3) ||
            !Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw Error(HuggingFaceModelError.IntegrityFailure);
    }

    public async Task<bool> HasTokenAsync(string profileId, CancellationToken cancellationToken) =>
        !string.IsNullOrEmpty(await ReadTokenAsync(profileId, cancellationToken));

    public async Task SaveTokenAsync(string profileId, string token, CancellationToken cancellationToken)
    {
        var identity = Profile(profileId);
        if (token is null || token.Length is < 8 or > 512 || !token.StartsWith("hf_", StringComparison.Ordinal) ||
            token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) throw Error(HuggingFaceModelError.InvalidRequest);
        try { await _secrets.SetAsync(identity, "access-token", token, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { throw Error(HuggingFaceModelError.SecureStorageUnavailable); }
    }

    public async Task RemoveTokenAsync(string profileId, CancellationToken cancellationToken)
    {
        var identity = Profile(profileId);
        try { await _secrets.DeleteAsync(identity, "access-token", cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { throw Error(HuggingFaceModelError.SecureStorageUnavailable); }
    }

    private async Task<string?> ReadTokenAsync(string profileId, CancellationToken ct)
    {
        var identity = Profile(profileId);
        try { return await _secrets.GetAsync(identity, "access-token", ct); }
        catch (OperationCanceledException) { throw; }
        catch { throw Error(HuggingFaceModelError.SecureStorageUnavailable); }
    }

    private async Task<JsonDocument> MetadataAsync(Uri uri, string? token, CancellationToken ct)
    {
        using var response = await SendAsync(uri, token, ct, metadata: true);
        if (response.Content.Headers.ContentLength > MaximumMetadataBytes) throw Error(HuggingFaceModelError.InvalidMetadata);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct);
            if (count == 0) break;
            if (bytes.Length + count > MaximumMetadataBytes) throw Error(HuggingFaceModelError.InvalidMetadata);
            bytes.Write(buffer, 0, count);
        }
        return JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, string? token, CancellationToken ct, bool metadata = false)
    {
        for (var hop = 0; hop <= 5; hop++)
        {
            if (!Allowed(uri) || (metadata && uri.Host != "huggingface.co")) throw Error(HuggingFaceModelError.DownloadRejected);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("9to1-Haven-ModelBrowser/1.0");
            if (token is not null && uri.Host == "huggingface.co") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            // Also refuse injected handlers that followed a redirect behind this boundary.
            if (response.RequestMessage?.RequestUri is { } actual && actual != uri)
            { response.Dispose(); throw Error(HuggingFaceModelError.DownloadRejected); }
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location is null || hop == 5 || !Uri.TryCreate(uri, location, out var next)) throw Error(HuggingFaceModelError.DownloadRejected);
                uri = next; continue;
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized) { response.Dispose(); throw Error(HuggingFaceModelError.AuthenticationRequired); }
            if (response.StatusCode == HttpStatusCode.Forbidden) { response.Dispose(); throw Error(HuggingFaceModelError.AccessDenied); }
            if (response.StatusCode != HttpStatusCode.OK) { response.Dispose(); throw Error(HuggingFaceModelError.Unavailable); }
            return response;
        }
        throw Error(HuggingFaceModelError.DownloadRejected);
    }

    private static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        uri.Host is "huggingface.co" or "cdn-lfs.huggingface.co" or "cdn-lfs-us-1.hf.co" or "cdn-lfs-eu-1.hf.co" or "cdn-lfs.hf.co" or "cas-bridge.xethub.hf.co" or "transfer.xethub.hf.co";

    private string CreateStaging()
    {
        var data = Path.GetFullPath(_paths.DataDirectory);
        for (var current = new DirectoryInfo(data); current is not null; current = current.Parent)
            if (!current.Exists || current.LinkTarget is not null || (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw Error(HuggingFaceModelError.DownloadRejected);
        PrivateDirectory(data, create: false);
        var parent = Path.Combine(data, "HuggingFaceStaging"); PrivateDirectory(parent, create: true);
        var staging = Path.Combine(parent, Guid.NewGuid().ToString("N")); PrivateDirectory(staging, create: true);
        return staging;
    }

    private static void PrivateDirectory(string path, bool create)
    {
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        if (create && !Directory.Exists(path))
        {
            if (OperatingSystem.IsWindows())
            {
                var owner = WindowsIdentity.GetCurrent().User ?? throw Error(HuggingFaceModelError.DownloadRejected);
                var security = new DirectorySecurity();
                security.SetOwner(owner); security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(path).Create(security);
            }
            else Directory.CreateDirectory(path, mode);
        }
        var directory = new DirectoryInfo(path);
        if (directory.LinkTarget is not null || (directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
            (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & ~mode) != 0))
            throw Error(HuggingFaceModelError.DownloadRejected);
        if (OperatingSystem.IsWindows() && create)
        {
            var owner = WindowsIdentity.GetCurrent().User ?? throw Error(HuggingFaceModelError.DownloadRejected);
            var security = directory.GetAccessControl();
            if (!security.AreAccessRulesProtected || !security.GetOwner(typeof(SecurityIdentifier)).Equals(owner))
                throw Error(HuggingFaceModelError.DownloadRejected);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(owner))
                    throw Error(HuggingFaceModelError.DownloadRejected);
        }
    }

    private static string Profile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) || profileId.Length > 256 || profileId.Any(char.IsControl)) throw Error(HuggingFaceModelError.InvalidRequest);
        return "huggingface-profile-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(profileId)));
    }
    private static void Repository(string value)
    { if (value is null || !RepositoryPattern().IsMatch(value)) throw Error(HuggingFaceModelError.InvalidRequest); }
    private static string RepoRoute(string id) => string.Join('/', id.Split('/').Select(Uri.EscapeDataString));
    private static bool SafeFile(string value) => value.Length is > 5 and <= 512 && value.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) &&
        !SplitFilePattern().IsMatch(value) &&
        !value.Any(c => char.IsControl(c) || c is '\\' or ':' or '%' or '?' or '#') &&
        value.Split('/').All(p => p.Length is > 0 and <= 255 && p is not "." and not ".." && !p.EndsWith(' ') && !p.EndsWith('.'));
    private static string Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long Number(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? Math.Max(0, number) : 0;
    private static bool Gated(JsonElement row) => row.TryGetProperty("gated", out var value) && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && value.GetString() is not (null or "false")));
    private static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (HuggingFaceModelException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (JsonException) { throw Error(HuggingFaceModelError.InvalidMetadata); }
        catch { throw Error(HuggingFaceModelError.Unavailable); }
    }
    internal static HuggingFaceModelException Error(HuggingFaceModelError code) => new(code, code switch
    {
        HuggingFaceModelError.AuthenticationRequired => "Hugging Face requires an access token for this model.",
        HuggingFaceModelError.AccessDenied => "Access denied. Check the token and accept the model's access conditions on Hugging Face.",
        HuggingFaceModelError.InvalidRequest => "The model browser request is invalid.",
        HuggingFaceModelError.InvalidMetadata => "Hugging Face did not provide complete pinned GGUF metadata. Refresh the model details.",
        HuggingFaceModelError.DownloadRejected => "The model download location or staging directory was refused.",
        HuggingFaceModelError.IntegrityFailure => "The downloaded model failed its size, SHA-256 or GGUF validation.",
        HuggingFaceModelError.ImportFailed => "The canonical local model importer could not install this model.",
        HuggingFaceModelError.SecureStorageUnavailable => "Protected credential storage is unavailable. No plaintext fallback is used.",
        _ => "The Hugging Face model operation is unavailable. Try again later."
    });
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,95}/[A-Za-z0-9][A-Za-z0-9._-]{0,95}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();
    [GeneratedRegex(@"\A[0-9a-fA-F]{40}\z", RegexOptions.CultureInvariant)] private static partial Regex CommitPattern();
    [GeneratedRegex(@"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant)] private static partial Regex HashPattern();
    [GeneratedRegex(@"-\d{5}-of-\d{5}\.gguf\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex SplitFilePattern();
    public void Dispose() { if (_ownsClient) _http.Dispose(); }
}
