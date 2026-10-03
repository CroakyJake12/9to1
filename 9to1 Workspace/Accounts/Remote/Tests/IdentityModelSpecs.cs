using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
namespace NineToOne.Accounts.Oidc;
internal static class IdentityModelSpecs
{
 public static async Task RunAsync()
 {
  using var rsa=RSA.Create(2048); var key=new RsaSecurityKey(rsa){KeyId="synthetic-key"};
  var pub=rsa.ExportParameters(false);
  var jwks=JsonSerializer.Serialize(new{keys=new[]{new{kty="RSA",kid=key.KeyId,n=Base64UrlEncoder.Encode(pub.Modulus!),e=Base64UrlEncoder.Encode(pub.Exponent!)}}});
  var now=DateTimeOffset.UtcNow;
  var issuer="https://synthetic.example.invalid";var audience="synthetic-resource";
  var policy=new TokenPolicy(issuer,audience,TokenPurpose.ApiAccessToken,null,new HashSet<string>{"cake:account:read"},new HashSet<string>{"RS256"});
  var reader=new IdentityModelIssuerReader(new FixtureKeys(new(issuer,jwks,now.AddMinutes(5))),new("sub","sid","auth_revision","scope","nonce","azp"),TimeProvider.System);
  string Token(string iss,string aud,DateTimeOffset expiry,DateTimeOffset begins,SecurityKey signing,string algorithm="RS256",string? revision="synthetic-revision",string? nonce=null)
   =>new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor{Issuer=iss,Audience=aud,Expires=expiry.UtcDateTime,NotBefore=begins.UtcDateTime,IssuedAt=now.UtcDateTime,
    Claims=new Dictionary<string,object>{{"sub","550e8400-e29b-41d4-a716-446655440000"},{"sid","synthetic-session"},{"scope","cake:account:read"},{"auth_revision",revision??""},{"nonce",nonce??""}},
    SigningCredentials=new SigningCredentials(signing,algorithm)});
  var privateJwks=JsonSerializer.Serialize(new{keys=new[]{new{kty="RSA",kid=key.KeyId,n=Base64UrlEncoder.Encode(pub.Modulus!),e=Base64UrlEncoder.Encode(pub.Exponent!),p="private-field"}}});
  var privateReader=new IdentityModelIssuerReader(new FixtureKeys(new(issuer,privateJwks,now.AddMinutes(5))),new("sub","sid","auth_revision","scope","nonce","azp"),TimeProvider.System);
  Require(await privateReader.VerifyAsync("fictional",policy,default) is null,"PrivateRSAfieldrejected");
  var valid=Token(issuer,audience,now.AddMinutes(2),now.AddMinutes(-1),key);
  var consumer=new OidcResourceConsumer(reader);
  var idPolicy=policy with {Purpose=TokenPurpose.IdToken,Audience="synthetic-client",ExpectedNonce="synthetic-nonce",RequiredScopes=new HashSet<string>()};
  var idToken=Token(issuer,"synthetic-client",now.AddMinutes(2),now.AddMinutes(-1),key,nonce:"synthetic-nonce");
  var extremeClaims=new Dictionary<string,object>{{"sub","550e8400-e29b-41d4-a716-446655440000"},{"iat",long.MaxValue},{"exp",now.AddMinutes(2).ToUnixTimeSeconds()},{"nbf",now.AddMinutes(-1).ToUnixTimeSeconds()},{"aud",audience},{"iss",issuer}};
  var extreme=new JsonWebTokenHandler().CreateToken(JsonSerializer.Serialize(extremeClaims),new SigningCredentials(key,"RS256"));
  Require(await reader.VerifyAsync(extreme,policy,default) is null,"ExtremeSignedIatDeniesWithoutThrow");
  Require(await reader.VerifyAsync(idToken,idPolicy,default) is not null,"SignedIDtokenclientaudnonce");
  Require(await reader.VerifyAsync(idToken,idPolicy with {ExpectedNonce="wrong"},default) is null,"WrongIDnonce");
  Require(await reader.VerifyAsync(idToken,idPolicy with {ExpectedNonce=null},default) is null,"Missingoriginatingnoncepolicy");
  Require(await consumer.ObserveAsync(valid,policy,now,default) is not null,"SyntheticvalidJWT");
  Require(await consumer.ObserveAsync(Token("https://other.example.invalid",audience,now.AddMinutes(2),now.AddMinutes(-1),key),policy,now,default) is null,"Wrongissuer");
  Require(await consumer.ObserveAsync(Token(issuer,"other",now.AddMinutes(2),now.AddMinutes(-1),key),policy,now,default) is null,"Wrongaudience");
  Require(await consumer.ObserveAsync(Token(issuer,audience,now.AddMinutes(-1),now.AddMinutes(-2),key),policy,now,default) is null,"Expired");
  Require(await consumer.ObserveAsync(Token(issuer,audience,now.AddMinutes(3),now.AddMinutes(1),key),policy,now,default) is null,"FutureNbf");
  var unknown=new RsaSecurityKey(rsa){KeyId="unknown-key"};
  Require(await consumer.ObserveAsync(Token(issuer,audience,now.AddMinutes(2),now.AddMinutes(-1),unknown),policy,now,default) is null,"Unknownkid");
  Require(await consumer.ObserveAsync(Token(issuer,audience,now.AddMinutes(2),now.AddMinutes(-1),key,"RS512"),policy,now,default) is null,"Algorithmnotallowed");
  Require(await consumer.ObserveAsync(Token(issuer,audience,now.AddMinutes(2),now.AddMinutes(-1),key,revision:null),policy,now,default) is null,"Missingsserverrevision");
  // Repeated bearer validation is allowed, not a one-use code. No replay/revocation freshness claim.
  Require(await consumer.ObserveAsync(valid,policy,now,default) is not null,"BearerreuseisnotcodeReplay");
 }
 private sealed class FixtureKeys(ApprovedIssuerKeys value):IApprovedIssuerKeysSource
 {public ValueTask<ApprovedIssuerKeys?> ReadAsync(string issuer,CancellationToken ct)=>ValueTask.FromResult<ApprovedIssuerKeys?>(value);}
 private static void Require(bool ok,string label){if(!ok)throw new InvalidOperationException(label);}
}
