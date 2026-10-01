using System.Net;
using System.Text.Json;
namespace NineToOne.Accounts.Oidc;
public sealed record IssuerFetchPolicy(Uri Issuer,Uri Discovery,Uri Jwks,int MaximumBodyBytes,TimeSpan KeyTtl,TimeSpan RequestTimeout);
public sealed class BoundedIssuerKeysSource : IApprovedIssuerKeysSource, IDisposable
{
 private readonly HttpClient client;private readonly IssuerFetchPolicy policy;private readonly TimeProvider clock;
 private readonly SemaphoreSlim gate=new(1,1);private ApprovedIssuerKeys? cached;
 public BoundedIssuerKeysSource(IssuerFetchPolicy policy,TimeProvider clock)
  :this(policy,clock,new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){}
 internal BoundedIssuerKeysSource(IssuerFetchPolicy policy,TimeProvider clock,HttpMessageHandler handler)
 {
  if(policy.Issuer.Scheme!="https"||policy.Discovery.Scheme!="https"||policy.Jwks.Scheme!="https"
   ||policy.Issuer.UserInfo.Length!=0||policy.Discovery.UserInfo.Length!=0||policy.Jwks.UserInfo.Length!=0
   ||policy.MaximumBodyBytes is <1 or >65536||policy.KeyTtl<=TimeSpan.Zero||policy.KeyTtl>TimeSpan.FromHours(1)
   ||policy.RequestTimeout<=TimeSpan.Zero||policy.RequestTimeout>TimeSpan.FromSeconds(30))throw new ArgumentException("Missing approved bounded issuer policy");
  this.policy=policy;this.clock=clock;client=new HttpClient(handler){Timeout=policy.RequestTimeout};
 }
 public async ValueTask<ApprovedIssuerKeys?> ReadAsync(string issuer,CancellationToken ct)
 {
  if(issuer!=policy.Issuer.AbsoluteUri)return null;
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
  deadline.CancelAfter(policy.RequestTimeout);
  var bounded=deadline.Token;
  try { await gate.WaitAsync(bounded); } catch(OperationCanceledException)when(!ct.IsCancellationRequested){return null;}
  try
  {
   if(cached is not null&&cached.FreshUntil>clock.GetUtcNow())return cached;
   // No expired-key fallback, token-selected URL, redirects, or automatic unknown-kid retry.
   var discovery=await GetBoundedAsync(policy.Discovery,bounded);if(discovery is null)return null;
   using var doc=JsonDocument.Parse(discovery,new JsonDocumentOptions{MaxDepth=8});
   if(!doc.RootElement.TryGetProperty("issuer",out var discoveredIssuer)||discoveredIssuer.GetString()!=issuer
    ||!doc.RootElement.TryGetProperty("jwks_uri",out var endpoint)||endpoint.GetString()!=policy.Jwks.AbsoluteUri)return null;
   var json=await GetBoundedAsync(policy.Jwks,bounded);if(json is null)return null;
   cached=new(issuer,System.Text.Encoding.UTF8.GetString(json),clock.GetUtcNow()+policy.KeyTtl);return cached;
  }
  catch(HttpRequestException){return null;}catch(JsonException){return null;}catch(InvalidOperationException){return null;}catch(OperationCanceledException)when(!ct.IsCancellationRequested){return null;}
  finally {gate.Release();}
 }
 private async Task<byte[]?> GetBoundedAsync(Uri uri,CancellationToken ct)
 {
  using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
  if(response.StatusCode!=HttpStatusCode.OK||response.Content.Headers.ContentLength>policy.MaximumBodyBytes)return null;
  await using var stream=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var buffer=new byte[4096];
  int count;while((count=await stream.ReadAsync(buffer,ct))!=0){if(output.Length+count>policy.MaximumBodyBytes)return null;output.Write(buffer,0,count);}
  return output.ToArray();
 }
 public void Dispose(){client.Dispose();gate.Dispose();}
}
