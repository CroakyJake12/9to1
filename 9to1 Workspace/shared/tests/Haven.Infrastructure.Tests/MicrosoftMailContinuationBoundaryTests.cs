using System.Net;
using System.Net.Http.Headers;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class MicrosoftMailContinuationBoundaryTests
{
    private static readonly Guid AccountId = Guid.Parse("11223344-5566-7788-9900-aabbccddeeff");
    private const string SyntheticToken = "synthetic-mail-boundary-canary";

    [Theory]
    [InlineData("https://attacker.invalid/collect")]
    [InlineData("http://graph.microsoft.com/v1.0/me/messages?$skiptoken=synthetic")]
    [InlineData("https://user@graph.microsoft.com/v1.0/me/messages?$skiptoken=synthetic")]
    [InlineData("https://graph.microsoft.com:444/v1.0/me/messages?$skiptoken=synthetic")]
    [InlineData("https://graph.microsoft.com/v1.0/users/other/messages?$skiptoken=synthetic")]
    [InlineData("https://[malformed")]
    [InlineData("messages?$skiptoken=synthetic")]
    [InlineData("https://graph.microsoft.com/v1.0/me/messages?$skiptoken=synthetic#fragment")]
    public async Task UnsafeContinuationIsRejectedBeforeRequestOrCredentialAccess(string continuation)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var tokens = new SyntheticTokenProvider();
        var provider = new MicrosoftMailProvider(new Factory(client), tokens);

        var failure = await Assert.ThrowsAsync<MailProviderException>(() => provider.GetMessagesAsync(
            new MailQuery(AccountId, ContinuationToken: continuation), CancellationToken.None));

        Assert.Equal(MailFailureKind.InvalidRequest, failure.FailureKind);
        Assert.Equal(0, handler.Requests);
        Assert.Equal(0, tokens.Reads);
    }

    [Theory]
    [InlineData("https://graph.microsoft.com/v1.0/me/messages?$skiptoken=synthetic%2Bcursor&$top=40")]
    [InlineData("https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skiptoken=synthetic%2Bcursor")]
    public async Task ValidGraphContinuationPreservesCursorAndAuthenticates(string continuation)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var tokens = new SyntheticTokenProvider();
        var provider = new MicrosoftMailProvider(new Factory(client), tokens);

        var page = await provider.GetMessagesAsync(new MailQuery(AccountId,
            ContinuationToken: continuation), CancellationToken.None);

        Assert.Empty(page.Messages);
        Assert.Equal(new Uri(continuation), handler.RequestUri);
        Assert.Equal(new Uri(continuation).Query, handler.RequestUri?.Query);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal(SyntheticToken, handler.Authorization?.Parameter);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(1, tokens.Reads);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            });
        }
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("HavenMail", name);
            return client;
        }
    }

    private sealed class SyntheticTokenProvider : IConnectedAccountAccessTokenProvider
    {
        public int Reads { get; private set; }

        public Task<ConnectedAccountAccessToken> GetAsync(Guid accountId, CalendarProviderKind provider,
            IReadOnlyCollection<string> requiredScopes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(AccountId, accountId);
            Assert.Equal(CalendarProviderKind.Microsoft, provider);
            Reads++;
            return Task.FromResult(new ConnectedAccountAccessToken(SyntheticToken,
                new HashSet<string>(requiredScopes), DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
