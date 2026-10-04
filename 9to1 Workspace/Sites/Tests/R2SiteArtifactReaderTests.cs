using System.Net;
using System.Security.Cryptography;
using HavenOS.Apps.Sites.Hosting;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

public sealed class R2SiteArtifactReaderTests
{
    private static SiteBuildArtifact Artifact(byte[] bytes) => new(Guid.NewGuid(), "source", "config", "static",
        Convert.ToHexString(SHA256.HashData(bytes)), "archive-reference", bytes.Length, true);
    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            var response = respond(request); response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
    private static R2SiteArtifactReader Reader(Transport transport) => new(new("https://objects.example/bucket/"),
        new Dictionary<string,string>{{"archive-reference","immutable/artifact.bin"}}, 1024, "sha256-hex", transport);

    [Fact]
    public async Task GetAndHeadUseExactConfiguredKeyAndValidateBytes()
    {
        byte[] bytes = [1,2,3]; var artifact = Artifact(bytes);
        using var transport = new Transport(request => {
            Assert.Equal("https://objects.example/bucket/immutable/artifact.bin", request.RequestUri!.AbsoluteUri);
            Assert.True(request.Method == HttpMethod.Get || request.Method == HttpMethod.Head);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        using var reader = Reader(transport);
        await reader.HeadAsync(artifact); Assert.Equal(bytes, await reader.ReadAsync(artifact)); Assert.Equal(2, transport.Calls);
    }

    [Theory]
    [InlineData("../secret")][InlineData("/secret")][InlineData("a//b")]
    [InlineData("a/%2fsecret")][InlineData("a?token=x")][InlineData("https://foreign/a")]
    public void RejectsUnsafeKeys(string key) => Assert.Throws<ArgumentException>(() =>
        new R2SiteArtifactReader(new("https://objects.example/"), new Dictionary<string,string>{{"ref",key}},1024,"sha256-hex"));

    [Theory]
    [InlineData(302)][InlineData(404)][InlineData(206)]
    public async Task RefusesRedirectMissingAndPartialResponses(int status)
    {
        using var reader = Reader(new(_ => new((HttpStatusCode)status){Content = new ByteArrayContent([1])}));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(Artifact([1])));
    }

    [Fact]
    public async Task CorruptedSameLengthContentFails()
    {
        using var reader = Reader(new(_ => new(HttpStatusCode.OK){Content = new ByteArrayContent([2])}));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(Artifact([1])));
    }

    [Fact]
    public async Task LengthMismatchFailsHeadAndGet()
    {
        using var reader = Reader(new(_ => new(HttpStatusCode.OK){Content = new ByteArrayContent([1,2])}));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.HeadAsync(Artifact([1])));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(Artifact([1])));
    }

    [Fact]
    public async Task UnconfiguredReferenceAndUnscannedArtifactNeverDispatch()
    {
        using var transport = new Transport(_ => throw new InvalidOperationException("Unexpected request"));
        using var reader = Reader(transport); var artifact = Artifact([1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(artifact with { ArtifactReference = "https://foreign/secret" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(artifact with { SecretScanPassed = false }));
        Assert.Equal(0,transport.Calls);
    }

    [Fact]
    public async Task CancellationPreventsDispatch()
    {
        using var transport = new Transport(_ => throw new InvalidOperationException("Unexpected request"));
        using var reader = Reader(transport); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(Artifact([1]),cancellation.Token));
        Assert.Equal(0,transport.Calls);
    }
    [Theory]
    [InlineData(0)][InlineData(2)]
    public async Task MisleadingLengthHeaderCannotHideTruncationOrExtraBytes(int actualLength)
    {
        using var reader = Reader(new(_ => {
            var content = new ByteArrayContent(new byte[actualLength]); content.Headers.ContentLength = 1;
            return new(HttpStatusCode.OK){Content = content};
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(Artifact([0])));
    }

}
