namespace NineToOne.Accounts.Oidc;

public enum TokenPurpose { IdToken, ApiAccessToken }
public sealed record TokenPolicy(string Issuer, string Audience, TokenPurpose Purpose, string? ExpectedNonce,
    IReadOnlySet<string> RequiredScopes, IReadOnlySet<string> AllowedAlgorithms);
// Implementer must be trusted composition, not a public request DTO adapter.
// Approved JOSE verifier must bound JWT/JWKS input, reject none/algorithm confusion,
// validate signature/kid with approved issuer JWKS and return authenticated claims only.
public interface IVerifiedIssuerTokenReader
{
    ValueTask<VerifiedToken?> VerifyAsync(string token, TokenPolicy policy, CancellationToken cancellationToken);
}
public sealed class VerifiedToken
{
    internal VerifiedToken(string issuer, string subject, IReadOnlySet<string> audiences,
        DateTimeOffset expiresAt, DateTimeOffset notBefore, string? nonce, string? authorizedParty,
        string? sessionId, string? authenticationRevision, IReadOnlySet<string> scopes, DateTimeOffset? issuedAt=null)
    { Issuer=issuer;Subject=subject;Audiences=audiences;ExpiresAt=expiresAt;NotBefore=notBefore;
      Nonce=nonce;AuthorizedParty=authorizedParty;SessionId=sessionId;AuthenticationRevision=authenticationRevision;Scopes=scopes;IssuedAt=issuedAt; }
    public DateTimeOffset? IssuedAt {get;}
    public string Issuer {get;} public string Subject {get;} public IReadOnlySet<string> Audiences {get;}
    public DateTimeOffset ExpiresAt {get;} public DateTimeOffset NotBefore {get;}
    public string? Nonce {get;} public string? AuthorizedParty {get;} public string? SessionId {get;}
    public string? AuthenticationRevision {get;} public IReadOnlySet<string> Scopes {get;}
}
public sealed record ObservedApiPrincipal(string Issuer, Guid AccountId, string SessionId, string AuthenticationRevision);
public sealed class OidcResourceConsumer(IVerifiedIssuerTokenReader reader)
{
    public async ValueTask<ObservedApiPrincipal?> ObserveAsync(string token, TokenPolicy policy,
        DateTimeOffset now, CancellationToken ct)
    {
        if(policy.Purpose!=TokenPurpose.ApiAccessToken || !Uri.TryCreate(policy.Issuer,UriKind.Absolute,out var issuer)
            ||issuer.Scheme!="https"||string.IsNullOrWhiteSpace(policy.Audience)||policy.AllowedAlgorithms.Count==0)return null;
        var verified=await reader.VerifyAsync(token,policy,ct);
        if(verified is null||verified.Issuer!=policy.Issuer||!verified.Audiences.Contains(policy.Audience)
            ||verified.IssuedAt is null||verified.IssuedAt>now||verified.IssuedAt>=verified.ExpiresAt||verified.ExpiresAt<=now||verified.NotBefore>now||!policy.RequiredScopes.IsSubsetOf(verified.Scopes)
            ||!Guid.TryParseExact(verified.Subject,"D",out var account)||account==Guid.Empty
            ||account.ToString("D")!=verified.Subject||string.IsNullOrWhiteSpace(verified.SessionId)
            ||string.IsNullOrWhiteSpace(verified.AuthenticationRevision))return null;
        // Current observation ONLY. No fresh revocation proof/local ProfileID/Home final guard.
        return new(verified.Issuer,account,verified.SessionId,verified.AuthenticationRevision);
    }
}
public sealed class UnavailableIssuerTokenReader : IVerifiedIssuerTokenReader
{
    public ValueTask<VerifiedToken?> VerifyAsync(string token,TokenPolicy policy,CancellationToken ct)
        => ValueTask.FromResult<VerifiedToken?>(null);
}
