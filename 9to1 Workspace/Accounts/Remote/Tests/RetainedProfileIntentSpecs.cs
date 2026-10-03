using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NineToOne.Accounts.Oidc;
using NineToOne.Accounts.Remote;
internal static class RetainedProfileIntentSpecs
{
 public static async Task RunAsync()
 {
  var now=DateTimeOffset.UtcNow;var account=Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
  using var rsa=RSA.Create(2048);var signing=new RsaSecurityKey(rsa){KeyId="held-synthetic-key"};var pub=rsa.ExportParameters(false);
  var jwks=JsonSerializer.Serialize(new{keys=new[]{new{kty="RSA",kid=signing.KeyId,n=Base64UrlEncoder.Encode(pub.Modulus!),e=Base64UrlEncoder.Encode(pub.Exponent!)}}});
  const string issuer="https://issuer.example.invalid/";const string audience="synthetic-resource";
  var raw=new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor{Issuer=issuer,Audience=audience,IssuedAt=now.UtcDateTime,NotBefore=now.AddMinutes(-1).UtcDateTime,Expires=now.AddMinutes(5).UtcDateTime,Claims=new Dictionary<string,object>{{"sub",account.ToString("D")},{"sid","650e8400-e29b-41d4-a716-446655440000"},{"auth_revision","synthetic-revision"},{"scope","cake:profile:write"}},SigningCredentials=new SigningCredentials(signing,"RS256")});
  var reached=new TaskCompletionSource();var release=new TaskCompletionSource();
  var keys=new HeldKeys(new(issuer,jwks,now.AddMinutes(5)),reached,release);
  var reader=new IdentityModelIssuerReader(keys,new("sub","sid","auth_revision","scope","nonce","azp"),TimeProvider.System);
  var policy=new TokenPolicy(issuer,audience,TokenPurpose.ApiAccessToken,null,new HashSet<string>{"cake:profile:write"},new HashSet<string>{"RS256"});
  var calls=0;string? observed=null;
  using var client=new WorkerAccountApiClient(new("https://worker.example.invalid/"),new OidcResourceConsumer(reader),policy,TimeProvider.System,new Handler(async request=>{calls++;Require(request.Method==HttpMethod.Patch&&request.RequestUri!.AbsolutePath=="/api/account/profile");observed=await request.Content!.ReadAsStringAsync();return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{profile=new RemoteProfile(account,"Original","original",null,null,null,8)}))};}));
  var document=JsonDocument.Parse("{\"name\":\"Original\",\"username\":\"original\"}");
  var pending=client.UpdateProfileAsync(raw,7,document.RootElement,default);await reached.Task;document.Dispose();
  Require(calls==0);release.SetResult();var result=await pending;
  Require(result.Failure==ApiFailure.None&&result.Value?.Revision==8&&calls==1);
  using var actual=JsonDocument.Parse(observed!);Require(actual.RootElement.GetProperty("expectedRevision").GetInt64()==7);
  Require(actual.RootElement.GetProperty("fields").GetRawText()=="{\"name\":\"Original\",\"username\":\"original\"}");
  // Actual signed reader waits on issuer keys; changed private host generation must deny before HTTP.
  var readReached=new TaskCompletionSource();var readRelease=new TaskCompletionSource();long generation=1;var reads=0;
  var readRaw=new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor{Issuer=issuer,Audience=audience,IssuedAt=now.UtcDateTime,NotBefore=now.AddMinutes(-1).UtcDateTime,Expires=now.AddMinutes(5).UtcDateTime,Claims=new Dictionary<string,object>{{"sub",account.ToString("D")},{"sid","650e8400-e29b-41d4-a716-446655440000"},{"auth_revision","synthetic-revision"},{"scope","cake:account:read"}},SigningCredentials=new SigningCredentials(signing,"RS256")});
  var readReader=new IdentityModelIssuerReader(new HeldKeys(new(issuer,jwks,now.AddMinutes(5)),readReached,readRelease),new("sub","sid","auth_revision","scope","nonce","azp"),TimeProvider.System);
  using var readClient=new WorkerAccountApiClient(new("https://worker.example.invalid/"),new OidcResourceConsumer(readReader),policy,TimeProvider.System,new Handler(_=>{reads++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new RemoteCurrent(account,"Fictional")))});}));
  var facade=new NineToOne.Web.RemoteAccountWebFacade(readClient,_=>ValueTask.FromResult<string?>(readRaw),()=>generation,default);
  var pendingRead=facade.CurrentAsync(default);await readReached.Task;generation++;readRelease.SetResult();Require((await pendingRead).Failure==ApiFailure.Unavailable&&reads==0);

 }
 private sealed class HeldKeys(ApprovedIssuerKeys value,TaskCompletionSource reached,TaskCompletionSource release):IApprovedIssuerKeysSource
 {public async ValueTask<ApprovedIssuerKeys?> ReadAsync(string issuer,CancellationToken ct){reached.SetResult();await release.Task.WaitAsync(ct);return value;}}
 private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> response):HttpMessageHandler
 {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>response(request);}
 private static void Require(bool condition){if(!condition)throw new InvalidOperationException("retained controlled profile intent assertion failed");}
}
