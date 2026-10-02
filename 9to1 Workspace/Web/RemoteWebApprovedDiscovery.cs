using System.Net;
using System.Text.Json;
namespace NineToOne.Web;

public sealed record WebDiscoveryPolicy(WebOidcConfiguration Configuration,Uri ExactDiscovery,
 Uri ExactJwks,TimeSpan Timeout,int MaximumBodyBytes);
// Remote issuer metadata consumer only; approved exact endpoints are host configuration,
// never public request arguments. Every flow reads fresh discovery; no local issuer fallback.
public sealed class RemoteWebApprovedDiscovery : IDisposable
{
 private readonly WebDiscoveryPolicy policy;private readonly HttpClient http;
 public RemoteWebApprovedDiscovery(WebDiscoveryPolicy policy):this(policy,new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){}
 internal RemoteWebApprovedDiscovery(WebDiscoveryPolicy policy,HttpMessageHandler handler)
 {
  static bool Safe(Uri u)=>u.IsAbsoluteUri&&u.Scheme=="https"&&u.UserInfo.Length==0&&u.Fragment.Length==0;
  if(!Uri.TryCreate(policy.Configuration.Issuer,UriKind.Absolute,out var issuer)||!Safe(issuer)
   ||!Safe(policy.ExactDiscovery)||!Safe(policy.ExactJwks)||!Safe(policy.Configuration.ExactAuthorizationEndpoint)||!Safe(policy.Configuration.ExactTokenEndpoint)
   ||policy.Timeout<=TimeSpan.Zero||policy.Timeout>TimeSpan.FromSeconds(30)||policy.MaximumBodyBytes is <1024 or >65536
   ||new[]{policy.ExactDiscovery,policy.ExactJwks,policy.Configuration.ExactAuthorizationEndpoint,policy.Configuration.ExactTokenEndpoint}.Any(u=>u.GetLeftPart(UriPartial.Authority)!=issuer.GetLeftPart(UriPartial.Authority)))throw new ArgumentException("Explicit approved issuer/discovery/endpoints and bounded policy required");
  this.policy=policy with {Configuration=policy.Configuration with {AllowedAlgorithms=new HashSet<string>(policy.Configuration.AllowedAlgorithms,StringComparer.Ordinal),RequestedScopes=new HashSet<string>(policy.Configuration.RequestedScopes,StringComparer.Ordinal)}};
  http=new(handler){Timeout=policy.Timeout};
 }
 public async Task<WebOidcConfiguration?> ReadAsync(CancellationToken ct)
 {
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(policy.Timeout);
  try
  {
   using var request=new HttpRequestMessage(HttpMethod.Get,policy.ExactDiscovery);
   using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
   if(response.StatusCode!=HttpStatusCode.OK||response.Content.Headers.ContentLength>policy.MaximumBodyBytes||response.Content.Headers.ContentType?.MediaType!="application/json")return null;
   await using var input=await response.Content.ReadAsStreamAsync(deadline.Token);using var body=new MemoryStream();byte[] buffer=new byte[4096];int read;
   while((read=await input.ReadAsync(buffer,deadline.Token))!=0){if(body.Length+read>policy.MaximumBodyBytes)return null;body.Write(buffer,0,read);}
   using var doc=JsonDocument.Parse(body.ToArray(),new JsonDocumentOptions{MaxDepth=8});var root=doc.RootElement;
   bool Exact(string name,string expected)=>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String&&value.GetString()==expected;
   bool Contains(string name,string required)=>root.TryGetProperty(name,out var values)&&values.ValueKind==JsonValueKind.Array&&values.EnumerateArray().Any(v=>v.ValueKind==JsonValueKind.String&&v.GetString()==required);
   var config=policy.Configuration;
   if(deadline.IsCancellationRequested||!Exact("issuer",config.Issuer)||!Exact("jwks_uri",policy.ExactJwks.AbsoluteUri)
    ||!Exact("authorization_endpoint",config.ExactAuthorizationEndpoint.AbsoluteUri)||!Exact("token_endpoint",config.ExactTokenEndpoint.AbsoluteUri)
    ||!Contains("response_types_supported","code")||!Contains("code_challenge_methods_supported","S256")
    ||!Contains("token_endpoint_auth_methods_supported","none")||!Contains("scopes_supported","openid")
    ||config.RequestedScopes.Any(scope=>!Contains("scopes_supported",scope)))return null;
   return config;
  }
  catch(Exception error)when(error is HttpRequestException or IOException or JsonException or InvalidOperationException or OperationCanceledException){return null;}
 }
 public void Dispose()=>http.Dispose();
}
