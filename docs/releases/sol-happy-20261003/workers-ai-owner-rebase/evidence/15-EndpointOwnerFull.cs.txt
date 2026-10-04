using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class ProviderEndpointSecurityTests
{
    [Theory]
    [InlineData("http://example.com/v1")]
    [InlineData("ftp://example.com/v1")]
    [InlineData("relative/path")]
    public async Task Remote_or_invalid_insecure_endpoints_are_rejected_at_transport_boundary(string endpoint)
    {
        var store = new Store(Configuration(endpoint));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProviderHttp.RequireEnabledAsync(store, "provider", "https://fallback.example/v1", CancellationToken.None));
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1")]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("https://example.com/v1")]
    public async Task Loopback_http_and_remote_https_are_allowed(string endpoint)
    {
        var configured = await ProviderHttp.RequireEnabledAsync(
            new Store(Configuration(endpoint)),
            "provider",
            "https://fallback.example/v1",
            CancellationToken.None);

        Assert.EndsWith("/", configured.Endpoint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/", true)]
    [InlineData("http://[::1]:11434/", true)]
    [InlineData("http://localhost:11434/", true)]
    [InlineData("https://remote.example/", false)]
    [InlineData("http://127.0.0.1.remote.example/", false)]
    [InlineData("http://user@localhost/", false)]
    [InlineData("ftp://localhost/", false)]
    public void Ollama_locality_uses_actual_transport_not_configuration_claim(string endpoint, bool expected)
    {
        using var http = new HttpClient { BaseAddress = new Uri(endpoint) };
        var client = new OllamaClient(http, new ProviderUsageCaptureBuffer());
        var configuration = Configuration("http://127.0.0.1:11434/") with { Id = "ollama", IsLocal = true };
        var provider = new OllamaModelProvider(client, new Store(configuration));
        Assert.False(provider.IsLocal);
        Assert.Equal(expected, http.BaseAddress!.IsLoopback && http.BaseAddress.Scheme is "http" or "https" && string.IsNullOrEmpty(http.BaseAddress.UserInfo));
        Assert.Equal(http.BaseAddress, client.TransportEndpoint);
    }

    [Fact]
    public async Task Actual_pinned_transport_does_not_follow_redirect_to_another_origin()
    {
        using var source = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        using var destination = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        source.Start(); destination.Start();
        var sourcePort = ((System.Net.IPEndPoint)source.LocalEndpoint).Port;
        var destinationPort = ((System.Net.IPEndPoint)destination.LocalEndpoint).Port;
        var response = Task.Run(async () =>
        {
            using var socket = await source.AcceptTcpClientAsync();
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, false, 1024, leaveOpen: true);
            var headerLength = 0;
            while (await reader.ReadLineAsync() is { Length: > 0 } line)
            {
                headerLength += line.Length;
                if (headerLength > 16384) throw new InvalidDataException("Fixture request headers exceeded the bound.");
            }
            var headers = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{destinationPort}/remote\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers);
        });
        await using var services = new ServiceCollection().AddHavenInfrastructure().BuildServiceProvider();
        var pinned = services.GetRequiredService<OllamaClient>();
        Assert.Equal(pinned.TransportEndpoint!.IsLoopback && pinned.TransportEndpoint.Scheme is "http" or "https" &&
            string.IsNullOrEmpty(pinned.TransportEndpoint.UserInfo), pinned.IsDeviceLocalTransportVerified);
        using var http = services.GetRequiredService<IHttpClientFactory>().CreateClient("Haven.Ollama");
        using var received = await http.GetAsync($"http://127.0.0.1:{sourcePort}/api/tags");
        await response;
        Assert.Equal(System.Net.HttpStatusCode.TemporaryRedirect, received.StatusCode);
        Assert.False(destination.Pending());
    }

    private static ProviderConfiguration Configuration(string endpoint) => new(
        "provider",
        ModelProviderKind.OpenAICompatible,
        "Provider",
        endpoint,
        IsEnabled: true,
        IsLocal: false,
        AllowCloudFallback: false,
        new Dictionary<string, string>(),
        DateTimeOffset.UtcNow);

    private sealed class Store(ProviderConfiguration configuration) : IProviderConfigurationStore
    {
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderConfiguration>>([configuration]);

        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken) =>
            Task.FromResult<ProviderConfiguration?>(configuration.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase) ? configuration : null);

        public Task UpsertAsync(ProviderConfiguration value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
