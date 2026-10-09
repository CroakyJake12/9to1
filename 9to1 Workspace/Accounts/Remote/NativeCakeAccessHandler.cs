using System.Net;
using NineToOne.Accounts.Oidc;
namespace NineToOne.Accounts.Native;

/// <summary>Trusted native composition supplies a genuine owner Access credential.
/// This port never reads browser cookies, grants CAKE identity, or authorizes a Home actor.
/// Cloudflared ownership/cache/reauthentication is independent of the memory-only CAKE session.
/// Implementer must retain/drain its actual acquisition process and refuse empty expired output.</summary>
public interface INativeCakeAccessCredentialSource:IAsyncDisposable
{
 ValueTask<string?> AcquireForRequestAsync(Uri exactApplicationOrigin,CancellationToken cancellationToken);
 Task CloseAndDrainAsync(); // SAME coalesced process/source drain, joined by the independent application owner.
}

internal sealed class NativeCakeAccessHandler:DelegatingHandler
{
 private readonly Uri origin;private readonly INativeCakeAccessCredentialSource source;
 internal NativeCakeAccessHandler(Uri exactOrigin,INativeCakeAccessCredentialSource originalSource)
 {
  if(!exactOrigin.IsAbsoluteUri||exactOrigin.Scheme!="https"||exactOrigin.UserInfo.Length!=0||exactOrigin.AbsolutePath!="/"||exactOrigin.Query.Length!=0||exactOrigin.Fragment.Length!=0)throw new ArgumentException("Exact protected HTTPS origin required");
  origin=exactOrigin;source=originalSource;
  InnerHandler=new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false};
 }
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();var uri=request.RequestUri;
  if(uri is null||!uri.IsAbsoluteUri||uri.Scheme!="https"||uri.UserInfo.Length!=0||uri.GetLeftPart(UriPartial.Authority)!=origin.GetLeftPart(UriPartial.Authority)||request.Headers.Contains("Cf-Access-Token"))throw new HttpRequestException("Unapproved protected request destination");
  var acquisition=source.AcquireForRequestAsync(origin,ct).AsTask(); // ONE genuine ValueTask conversion.
  var token=await NativeCakeErrors.Await(acquisition).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
  if(token is not {Length: >0 and <=16384}||token.Any(char.IsWhiteSpace)||token.Any(char.IsControl))throw new HttpRequestException("Genuine owner Access credential is unavailable");
  // Client-side header from maintained cloudflared source, not the origin-side assertion header.
  if(!request.Headers.TryAddWithoutValidation("Cf-Access-Token",token))throw new HttpRequestException("Access credential header unavailable");
  return await NativeCakeErrors.Await(base.SendAsync(request,ct)).ConfigureAwait(false);
 }
}
