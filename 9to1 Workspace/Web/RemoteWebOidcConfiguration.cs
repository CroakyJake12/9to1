using Microsoft.Extensions.Configuration;
using NineToOne.Accounts.Oidc;
namespace NineToOne.Web;

// Host/operator configuration only. No default issuer, credential authority or request-supplied policy.
public enum RemoteCakeClientMode { Legacy, Remote, Unavailable }
public static class RemoteWebOidcConfiguration
{
 public static RemoteCakeClientMode ReadMode(IConfiguration configuration)
 {
  var text=configuration["RemoteCakeClient:Enabled"];
  if(text is null)return RemoteCakeClientMode.Legacy;
  return bool.TryParse(text,out var enabled)?enabled?RemoteCakeClientMode.Remote:RemoteCakeClientMode.Legacy:RemoteCakeClientMode.Unavailable;
 }
 public static RemoteWebOidcClientOptions? Read(IConfiguration configuration)
 {
  var c=configuration.GetSection("RemoteCakeClient");
  if(!bool.TryParse(c["Enabled"],out var enabled)||!enabled)return null;
  string Required(string key)=>!string.IsNullOrWhiteSpace(c[key])?c[key]!:throw new ArgumentException("Missing approved client configuration");
  Uri Exact(string key)=>new(Required(key),UriKind.Absolute);
  int Number(string key)=>int.TryParse(Required(key),System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:throw new ArgumentException("Invalid client bounds");
  IReadOnlySet<string> Set(string key)=>c.GetSection(key).GetChildren().Select(x=>x.Value??"").Where(x=>x.Length>0).ToHashSet(StringComparer.Ordinal);
  try
  {
   var scopes=Set("Scopes");var algorithms=Set("Algorithms");
   if(scopes.Count==0||algorithms.Count==0||!scopes.Contains("cake:account:read"))return null;
   var issuer=Exact("Issuer");var discovery=Exact("DiscoveryUri");var jwks=Exact("JwksUri");var web=Exact("WebOrigin");
   var flow=new WebOidcConfiguration(issuer.AbsoluteUri,Required("PublicClientId"),Required("ResourceAudience"),Required("RedirectUri"),Exact("AuthorizationUri"),Exact("TokenUri"),algorithms,scopes);
   var body=Number("MaximumMetadataBodyBytes");var timeout=Number("RequestTimeoutSeconds");var ttl=Number("JwksTtlSeconds");
   if(body is <1024 or >65536||timeout is <1 or >30||ttl is <1 or >3600)return null;
   return new(web,new(flow,discovery,jwks,TimeSpan.FromSeconds(timeout),body),new(issuer,discovery,jwks,body,TimeSpan.FromSeconds(ttl),TimeSpan.FromSeconds(timeout)),
    new(Required("Claims:Subject"),Required("Claims:SessionId"),Required("Claims:AuthenticationRevision"),Required("Claims:Scope"),Required("Claims:Nonce"),Required("Claims:AuthorizedParty")),Exact("AccountApiOrigin"));
  }
  catch(Exception error)when(error is ArgumentException or UriFormatException or InvalidOperationException){return null;}
 }
}
