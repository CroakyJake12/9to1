using Microsoft.Extensions.DependencyInjection;
using NineToOne.Accounts.Oidc;
using NineToOne.Accounts.Remote;
namespace NineToOne.Web;

public sealed record RemoteWebOidcClientOptions(Uri WebOrigin,WebDiscoveryPolicy Discovery,
 IssuerFetchPolicy Keys,IssuerClaimMapping ActualProviderClaims,Uri ExactAccountApiOrigin);
public static class RemoteWebOidcClientComposition
{
 // Opt-in CLIENT composition only, not a credential/session authority or Home principal adapter.
 // No defaults for issuer/audience/claims/endpoints; operator must provide actual verified mapping.
 public static IServiceCollection AddRemoteWebOidcClient(this IServiceCollection services,RemoteWebOidcClientOptions options)
 {
  if(options.Keys.Issuer.AbsoluteUri!=options.Discovery.Configuration.Issuer||options.Keys.Discovery!=options.Discovery.ExactDiscovery||options.Keys.Jwks!=options.Discovery.ExactJwks
   ||options.ActualProviderClaims.Subject.Length==0||options.ActualProviderClaims.SessionId.Length==0||options.ActualProviderClaims.AuthenticationRevision.Length==0
   ||options.Discovery.Configuration.ExactRedirectUri!=new Uri(options.WebOrigin,"/remote/oidc/callback").AbsoluteUri)throw new ArgumentException("Coherent exact issuer/discovery/JWKS/callback/provider claims required");
  if(services.Any(x=>x.ServiceType==typeof(RemoteWebOidcHost)))throw new InvalidOperationException("Remote Web client already configured");
  // Detach mutable configured collections before factories retain the approved policy.
  options=options with {Discovery=options.Discovery with {Configuration=options.Discovery.Configuration with {
   AllowedAlgorithms=new HashSet<string>(options.Discovery.Configuration.AllowedAlgorithms,StringComparer.Ordinal),
   RequestedScopes=new HashSet<string>(options.Discovery.Configuration.RequestedScopes,StringComparer.Ordinal)}}};
  services.AddSingleton(_=>new BoundedIssuerKeysSource(options.Keys,TimeProvider.System));
  services.AddSingleton(p=>new IdentityModelIssuerReader(p.GetRequiredService<BoundedIssuerKeysSource>(),options.ActualProviderClaims,TimeProvider.System));
  services.AddSingleton(_=>new RemoteWebApprovedDiscovery(options.Discovery));services.AddSingleton<RemoteWebCodeExchange>();
  services.AddSingleton(p=>new WorkerAccountApiClient(options.ExactAccountApiOrigin,new OidcResourceConsumer(p.GetRequiredService<IdentityModelIssuerReader>()),new TokenPolicy(options.Discovery.Configuration.Issuer,options.Discovery.Configuration.ResourceAudience,TokenPurpose.ApiAccessToken,null,options.Discovery.Configuration.RequestedScopes,options.Discovery.Configuration.AllowedAlgorithms),TimeProvider.System));
  services.AddSingleton(p=>new RemoteWebOidcHost(options.WebOrigin,p.GetRequiredService<IdentityModelIssuerReader>(),p.GetRequiredService<RemoteWebCodeExchange>(),p.GetRequiredService<RemoteWebApprovedDiscovery>().ReadAsync,TimeProvider.System,p.GetRequiredService<WorkerAccountApiClient>()));
  return services;
 }
}
