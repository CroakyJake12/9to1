using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
namespace NineToOne.Accounts.Oidc;
public sealed record ApprovedIssuerKeys(string Issuer, string JwksJson, DateTimeOffset FreshUntil);
public interface IApprovedIssuerKeysSource
{
    // Only exact approved HTTPS discovery/JWKS origins; bounded body/keys, redirects denied;
    // configured TTL/rotation/refresh bounds mandatory. No caller-supplied URL or token jku.
    ValueTask<ApprovedIssuerKeys?> ReadAsync(string exactIssuer,CancellationToken ct);
}
public sealed record IssuerClaimMapping(string Subject, string SessionId, string AuthenticationRevision,
    string Scope, string Nonce, string AuthorizedParty);
public sealed class IdentityModelIssuerReader(IApprovedIssuerKeysSource keys,IssuerClaimMapping mapping,
    TimeProvider clock) : IVerifiedIssuerTokenReader
{
    public async ValueTask<VerifiedToken?> VerifyAsync(string raw,TokenPolicy policy,CancellationToken ct)
    {
        if(raw.Length>16384||policy.AllowedAlgorithms.Count==0)return null;
        var snapshot=await keys.ReadAsync(policy.Issuer,ct);
        if(snapshot is null||snapshot.Issuer!=policy.Issuer||snapshot.FreshUntil<=clock.GetUtcNow()
            ||snapshot.JwksJson.Length>65536)return null;
        JsonWebKeySet jwks;
        try {jwks=new JsonWebKeySet(snapshot.JwksJson);} catch(ArgumentException){return null;}
        if(jwks.Keys.Count is <1 or >16)return null;
        // Refuse symmetric/private JWK material for this public issuer verification seam.
        if(jwks.Keys.Any(k=>k.Kty!="RSA"||string.IsNullOrWhiteSpace(k.Kid)||k.D is not null||k.K is not null||k.P is not null||k.Q is not null||k.DP is not null||k.DQ is not null||k.QI is not null||k.Oth.Count!=0)
            ||jwks.Keys.Select(k=>k.Kid).Distinct(StringComparer.Ordinal).Count()!=jwks.Keys.Count)return null;
        var handler=new JsonWebTokenHandler {MaximumTokenSizeInBytes=16384};
        var parameters=new TokenValidationParameters
        {
            ValidateIssuer=true,ValidIssuer=policy.Issuer,ValidateAudience=true,ValidAudience=policy.Audience,
            ValidateLifetime=true,RequireExpirationTime=true,RequireSignedTokens=true,ValidateIssuerSigningKey=true,
            IssuerSigningKeys=jwks.GetSigningKeys(),ValidAlgorithms=policy.AllowedAlgorithms,
            ClockSkew=TimeSpan.Zero,TryAllIssuerSigningKeys=false
        };
        var result=await handler.ValidateTokenAsync(raw,parameters);
        if(!result.IsValid||result.SecurityToken is not JsonWebToken token)return null;
        string? Claim(string name)=>token.TryGetPayloadValue<string>(name,out var value)?value:null;
        var subject=Claim(mapping.Subject);if(subject is null||!token.TryGetPayloadValue<long>("iat",out var issued)
            ||!Guid.TryParseExact(subject,"D",out var account)||account==Guid.Empty||account.ToString("D")!=subject)return null;
        if(issued < -62135596800L || issued > 253402300799L)return null;
        var issuedAt=DateTimeOffset.FromUnixTimeSeconds(issued);
        if(issuedAt>clock.GetUtcNow()||issuedAt>=new DateTimeOffset(token.ValidTo,TimeSpan.Zero))return null;
        if(policy.Purpose==TokenPurpose.IdToken && (string.IsNullOrWhiteSpace(policy.ExpectedNonce)
            ||Claim(mapping.Nonce)!=policy.ExpectedNonce))return null;
        return new(policy.Issuer,subject,new HashSet<string>(token.Audiences,StringComparer.Ordinal),
            new DateTimeOffset(token.ValidTo,TimeSpan.Zero),new DateTimeOffset(token.ValidFrom,TimeSpan.Zero),
            Claim(mapping.Nonce),Claim(mapping.AuthorizedParty),Claim(mapping.SessionId),Claim(mapping.AuthenticationRevision),
            new HashSet<string>((Claim(mapping.Scope)??"").Split(' ',StringSplitOptions.RemoveEmptyEntries),StringComparer.Ordinal),DateTimeOffset.FromUnixTimeSeconds(issued));
    }
}
