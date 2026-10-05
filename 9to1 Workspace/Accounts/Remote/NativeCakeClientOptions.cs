using System.Collections.Frozen;
namespace NineToOne.Accounts.Native;

// Genuine owner-supplied public metadata; never a secret, issuer or Home authority.
public sealed record NativeCakeClientOptions(string Issuer,string PublicClientId,string ApplicationType,
 string TokenEndpointAuthMethod,string RedirectUri,string ApiResource,Uri AccountApiOrigin,
 Uri DiscoveryUri,Uri AuthorizationUri,Uri TokenUri,Uri JwksUri)
{
 public const string RequiredRedirect="http://127.0.0.1:43821/cake-id/callback/";
 public static IReadOnlySet<string> Scopes {get;}=new[]{"openid","profile","cake:account:read","cake:profile:read","cake:sessions:read","cake:sessions:revoke"}.ToFrozenSet(StringComparer.Ordinal);
 public NativeCakeClientOptions Capture()
 {
  static bool Https(Uri u)=>u.IsAbsoluteUri&&u.Scheme=="https"&&u.UserInfo.Length==0&&u.Query.Length==0&&u.Fragment.Length==0;
  if(!Uri.TryCreate(Issuer,UriKind.Absolute,out var issuer)||!Https(issuer)||PublicClientId is not {Length: >0 and <=256}||PublicClientId.Any(char.IsControl)||ApplicationType!="native"||TokenEndpointAuthMethod!="none"||RedirectUri!=RequiredRedirect||!Uri.TryCreate(ApiResource,UriKind.Absolute,out var api)||!Https(api)||!Https(AccountApiOrigin)||AccountApiOrigin.AbsolutePath!="/")throw new ArgumentException("Approved native public client metadata required");
  foreach(var e in new[]{DiscoveryUri,AuthorizationUri,TokenUri,JwksUri})if(!Https(e)||e.GetLeftPart(UriPartial.Authority)!=issuer.GetLeftPart(UriPartial.Authority))throw new ArgumentException("Exact same-origin issuer endpoints required");return this with {};
 }
}
