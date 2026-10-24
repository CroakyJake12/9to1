using System.Text.Json;
using Haven.Application;
using Haven.Core;
using ModelContextProtocol.Authentication;

namespace Haven.Infrastructure;

internal sealed class McpOAuthTokenCache(IProviderSecretStore secrets, Guid connectionId) : ITokenCache
{
    private readonly string _providerId = ExternalConnectionNaming.SecretProviderId(connectionId);
    private const string SecretName = ExternalConnectionNaming.OAuthTokenSecretName;

    public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return StoreTokenEnvelopeAsync(() => JsonSerializer.Serialize(tokens), cancellationToken);
    }

    // Shared serialization/store path also permits fake-store concurrency tests without constructing SDK credentials.
    internal async ValueTask StoreTokenEnvelopeAsync(Func<string> serializeTokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serializeTokens);
        using var lease = await ExternalConnectionCredentialLifecycle.AcquireAsync(secrets, _providerId, cancellationToken).ConfigureAwait(false);
        if (lease.IsRevoked) throw new InvalidOperationException("The MCP connection was removed before its OAuth tokens could be stored.");
        var serializedTokens = serializeTokens();
        ArgumentNullException.ThrowIfNull(serializedTokens);
        await secrets.SetAsync(_providerId, SecretName, serializedTokens, cancellationToken).ConfigureAwait(false);
        if (lease.IsRevoked) throw new InvalidOperationException("The MCP connection was removed while its OAuth tokens were being stored.");
    }

    public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        using var lease = await ExternalConnectionCredentialLifecycle.AcquireAsync(secrets, _providerId, cancellationToken).ConfigureAwait(false);
        if (lease.IsRevoked) return null;
        var json = await secrets.GetAsync(_providerId, SecretName, cancellationToken).ConfigureAwait(false);
        if (lease.IsRevoked || string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var tokens = JsonSerializer.Deserialize<TokenContainer>(json);
            if (tokens is not null) return lease.IsRevoked ? null : tokens;
        }
        catch (JsonException) { }

        // `null` is valid JSON but is not a usable token envelope. Remove it just
        // like malformed JSON so a dead cache entry does not survive reconnects.
        await secrets.DeleteAsync(_providerId, SecretName, cancellationToken).ConfigureAwait(false);
        return null;
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        using var lease = await ExternalConnectionCredentialLifecycle.AcquireAsync(secrets, _providerId, cancellationToken).ConfigureAwait(false);
        await secrets.DeleteAsync(_providerId, SecretName, cancellationToken).ConfigureAwait(false);
    }
}
