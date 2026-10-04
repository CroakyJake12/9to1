using System.Net;
using System.Security.Cryptography;

namespace HavenOS.Apps.Sites.Hosting;

/// <summary>Configured transport only. References are trusted archive references, never request URLs.
/// SHA256 hexadecimal encoding must be explicitly selected by the composition owner.</summary>
public sealed class R2SiteArtifactReader : IDisposable
{
    private readonly HttpClient client;
    private readonly Uri endpoint;
    private readonly IReadOnlyDictionary<string, string> keys;
    private readonly int maximumBytes;

    public R2SiteArtifactReader(Uri endpoint, IReadOnlyDictionary<string, string> referenceKeys,
        int maximumBytes, string contentHashEncoding, HttpMessageHandler? transport = null)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "https" || endpoint.UserInfo.Length != 0 ||
            endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || !endpoint.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("Select an HTTPS object endpoint ending in a slash.", nameof(endpoint));
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (contentHashEncoding != "sha256-hex") throw new ArgumentException("Explicit sha256-hex encoding is required.", nameof(contentHashEncoding));
        ArgumentNullException.ThrowIfNull(referenceKeys);
        foreach (var pair in referenceKeys)
            if (string.IsNullOrWhiteSpace(pair.Key) || !SafeKey(pair.Value))
                throw new ArgumentException("References require explicit safe object keys.", nameof(referenceKeys));
        this.endpoint = endpoint;
        keys = new Dictionary<string, string>(referenceKeys, StringComparer.Ordinal);
        this.maximumBytes = maximumBytes;
        // Injected transports must preserve redirect refusal; production default cannot follow redirects.
        client = new HttpClient(transport ?? new SocketsHttpHandler { AllowAutoRedirect = false }, true);
    }

    public async Task HeadAsync(SiteBuildArtifact artifact, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Head, artifact, cancellationToken).ConfigureAwait(false);
        RequireLength(response, artifact);
        // HEAD is metadata validation only; it cannot establish ContentMatches.
    }

    public async Task<byte[]> ReadAsync(SiteBuildArtifact artifact, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, artifact, cancellationToken).ConfigureAwait(false);
        RequireLength(response, artifact);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            int count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (bytes.Length + count > artifact.SizeBytes)
                throw new InvalidDataException("Object exceeds the retained artifact length.");
            bytes.Write(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = bytes.ToArray();
        if (result.LongLength != artifact.SizeBytes ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(result), Convert.FromHexString(artifact.ContentHash)))
            throw new InvalidDataException("Object bytes differ from the retained artifact.");
        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, SiteBuildArtifact artifact, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.ArtifactId == Guid.Empty || !artifact.SecretScanPassed || artifact.SizeBytes < 0 ||
            artifact.SizeBytes > maximumBytes || artifact.ContentHash.Length != 64 ||
            artifact.ContentHash.Any(c => !Uri.IsHexDigit(c)) || string.IsNullOrWhiteSpace(artifact.SourceRevision) ||
            string.IsNullOrWhiteSpace(artifact.ConfigurationRevision) || !keys.TryGetValue(artifact.ArtifactReference, out var key))
            throw new InvalidDataException("Retained artifact identity, integrity or configured reference is unavailable.");
        var uri = new Uri(endpoint, string.Join('/', key.Split('/').Select(Uri.EscapeDataString)));
        using var request = new HttpRequestMessage(method, uri);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK || response.RequestMessage?.RequestUri != uri)
        {
            response.Dispose();
            throw new InvalidDataException("Object endpoint did not return the selected complete object.");
        }
        return response;
    }

    private static void RequireLength(HttpResponseMessage response, SiteBuildArtifact artifact)
    {
        if (response.Content.Headers.ContentLength != artifact.SizeBytes || response.Content.Headers.ContentEncoding.Count != 0)
            throw new InvalidDataException("Object transport length or encoding differs from the retained artifact.");
    }

    private static bool SafeKey(string key) => !string.IsNullOrWhiteSpace(key) &&
        key.Split('/').All(segment => segment.Length > 0 && segment is not ("." or "..") &&
            segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'));

    public void Dispose() => client.Dispose();
}
