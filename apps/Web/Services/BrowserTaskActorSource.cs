using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;

namespace NineToOne.Web.Services;

/// <summary>
/// Task-only identity from the host's private signature-verified CAKE session and current account/profile API.
/// This Task actor source grants no Home, OS or resource rights; canonical permission admission remains separate.
/// Its revision names the signed session and local broker activation, not a nonexistent server auth_revision.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserTaskActorSource : IAuthenticatedResourceActorSource
{
    public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N"); // Request correlation only, never actor/profile identity.
        return new(BrowserTaskIdentityReadOperation.RunAsync<AuthenticatedResourceActor?>(
            () => ReadCurrent(requestId), () => Cancel(requestId),
            json => ReadVerifiedActor(json, requestId),
            () => Release(requestId), cancellationToken));
    }

    private static AuthenticatedResourceActor? ReadVerifiedActor(string json, string requestId)
    {
        using var document = JsonDocument.Parse(json);
        var reply = document.RootElement;
        if (reply.GetProperty("ok").ValueKind != JsonValueKind.True) return null;
        var identity = reply.GetProperty("identity");
        var issuer = identity.GetProperty("issuer").GetString();
        var resource = identity.GetProperty("apiResource").GetString();
        var clientId = identity.GetProperty("clientId").GetString();
        var account = identity.GetProperty("accountId").GetString();
        var profile = identity.GetProperty("profileAccountId").GetString();
        var session = identity.GetProperty("sessionId").GetString();
        var epoch = identity.GetProperty("ownerEpoch").GetString();
        var issued = identity.GetProperty("issuedAt").GetInt64();
        var expires = identity.GetProperty("expiresAt").GetInt64();
        var generation = identity.GetProperty("generation").GetInt64();
        if (identity.GetProperty("version").GetInt32() != 1 || issuer is null || resource is null || string.IsNullOrWhiteSpace(clientId) ||
            !Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri) || issuerUri.Scheme != "https" ||
            !Uri.TryCreate(resource, UriKind.Absolute, out var resourceUri) || resourceUri.Scheme != "https" ||
            issuerUri.UserInfo.Length != 0 || issuerUri.Query.Length != 0 || issuerUri.Fragment.Length != 0 ||
            resourceUri.UserInfo.Length != 0 || resourceUri.Query.Length != 0 || resourceUri.Fragment.Length != 0 || resourceUri.AbsolutePath != "/" ||
            !Guid.TryParseExact(account, "D", out var accountId) || accountId == Guid.Empty || profile != account ||
            !Guid.TryParseExact(session, "D", out var sessionId) || sessionId == Guid.Empty ||
            epoch is null || epoch.Length != 43 || epoch.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) ||
            issued < 0 || expires <= issued || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
            generation < 0 || generation > 9_007_199_254_740_991 || identity.GetProperty("profileRevision").GetInt64() <= 0)
            throw new InvalidOperationException("The private Task identity owner returned invalid verified session metadata.");
        // The private JS owner consumes the SAME read receipt and synchronously rechecks its
        // actual session/generation here, after the awaited JS/.NET response and parsing.
        if (!ConfirmCurrent(requestId)) return null;
        var authority = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issuer))).ToLowerInvariant();
        return new AuthenticatedResourceActor($"cake-task:{authority}:{accountId:D}",
            $"cake-account-profile:{authority}:{accountId:D}", accountId, null,
            $"cake-signed-session:{sessionId:D}:iat={issued}:exp={expires};browser-owner={epoch}:generation={generation}");
    }

    [JSImport("readCurrent", "nineToOneTaskIdentity")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> ReadCurrent(string requestId);

    [JSImport("cancel", "nineToOneTaskIdentity")]
    private static partial void Cancel(string requestId);

    [JSImport("confirmCurrent", "nineToOneTaskIdentity")]
    private static partial bool ConfirmCurrent(string requestId);

    [JSImport("release", "nineToOneTaskIdentity")]
    private static partial void Release(string requestId);
}
