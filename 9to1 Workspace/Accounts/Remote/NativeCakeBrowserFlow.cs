using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NineToOne.Accounts.Oidc;
using NineToOne.Accounts.Remote;
namespace NineToOne.Accounts.Native;

public interface INativeCakeBrowserRequestSource { void Request(Uri authorization,CancellationToken token); }
public sealed class SystemNativeCakeBrowserRequestSource:INativeCakeBrowserRequestSource
{
 public void Request(Uri authorization,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Native CAKE browser sign-in requires Windows");
  // The system browser is borrowed. Dispose the returned process HANDLE, never its lifetime.
  using var handle=Process.Start(new ProcessStartInfo(authorization.AbsoluteUri){UseShellExecute=true});
 }
}
internal sealed record NativeCakeCredential(string AccessToken,Guid AccountId,Guid SessionId,string DisplayName,DateTimeOffset ExpiresAt);
internal sealed class NativeCakeBrowserFlow(NativeCakeClientOptions options,IVerifiedIssuerTokenReader reader,
 WorkerAccountApiClient api,HttpClient http,INativeCakeBrowserRequestSource browser,TimeProvider clock)
{
 internal async Task<NativeCakeCredential> RunAsync(long originalGeneration,Func<bool> admit,CancellationToken ct)
 {
  List<Exception> errors=[];NativeCakeCredential? result=null;
  List<IDisposable> resources=[];CancellationTokenSource? deadline=null;NativeCakeLoopback? callback=null;Task<IReadOnlyDictionary<string,string>>? receive=null;
  try
  {
   deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromMinutes(5));var token=deadline.Token;
   void Demand(){token.ThrowIfCancellationRequested();if(!admit())throw new OperationCanceledException("Native account context retired",token);}
   Demand();await DiscoverAsync(token).ConfigureAwait(false);Demand();
   var flow=new OidcOriginatingFlow(options.Issuer,options.PublicClientId,options.RedirectUri,
    originalGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),clock.GetUtcNow().AddMinutes(5),options.ApiResource);
   var parameters=new Dictionary<string,string>(flow.AuthorizationParameters());parameters["scope"]=string.Join(' ',NativeCakeClientOptions.Scopes.Order(StringComparer.Ordinal));
   var nonce=parameters["nonce"];var state=parameters["state"];
   var uri=new Uri(options.AuthorizationUri.AbsoluteUri+"?"+string.Join('&',parameters.Select(x=>Uri.EscapeDataString(x.Key)+"="+Uri.EscapeDataString(x.Value))));
   callback=new NativeCakeLoopback(new Uri(options.RedirectUri));
   receive=callback.ReceiveAsync(token); // Actual receive retained BEFORE external browser callback.
   Demand();browser.Request(uri,token);Demand();
   var values=await receive.ConfigureAwait(false);Demand();
   if(!values.TryGetValue("state",out var returnedState)||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state),Encoding.UTF8.GetBytes(returnedState))||values.ContainsKey("error")||values.TryGetValue("iss",out var issuer)&&issuer!=options.Issuer||!values.TryGetValue("code",out var code)||code.Length is <1 or >4096)throw new IOException("Native OAuth callback refused");
   var form=flow.ConsumeCallback(returnedState,options.RedirectUri,code,originalGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),clock.GetUtcNow())??throw new IOException("Native OAuth callback is stale or already consumed");
   var request=new HttpRequestMessage(HttpMethod.Post,options.TokenUri){Content=new FormUrlEncodedContent(form)};resources.Add(request);
   // Exactly one actual exchange; no retry/automatic redirect after unknown completion.
   var response=await NativeCakeErrors.Await(http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token)).ConfigureAwait(false);resources.Add(response);
   if(response.StatusCode!=HttpStatusCode.OK)throw new IOException("Native code exchange completion is unknown or refused");
   var tokens=await ReadJsonAsync(response,token).ConfigureAwait(false);resources.Add(tokens);Demand();var body=tokens.RootElement;
   static string Required(JsonElement b,string name)=>b.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String&&v.GetString() is {Length: >0 and <=16384} s?s:throw new IOException("Malformed token response");
   if(!string.Equals(Required(body,"token_type"),"Bearer",StringComparison.OrdinalIgnoreCase))throw new IOException("Unsupported token type");
   var idRaw=Required(body,"id_token");var accessRaw=Required(body,"access_token"); // No refresh token retained.
   var algorithms=new HashSet<string>(StringComparer.Ordinal){"EdDSA"};
   var identity=await NativeCakeErrors.Await(reader.VerifyAsync(idRaw,new(options.Issuer,options.PublicClientId,TokenPurpose.IdToken,nonce,new HashSet<string>(),algorithms),token).AsTask()).ConfigureAwait(false);Demand();
   if(identity is null||!flow.AcceptIdToken(identity,originalGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),clock.GetUtcNow()))throw new IOException("ID token refused");
   var apiScopes=NativeCakeClientOptions.Scopes.Where(x=>x.StartsWith("cake:",StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
   var access=await NativeCakeErrors.Await(reader.VerifyAsync(accessRaw,new(options.Issuer,options.ApiResource,TokenPurpose.ApiAccessToken,null,apiScopes,algorithms),token).AsTask()).ConfigureAwait(false);Demand();
   if(access is null||access.Subject!=identity.Subject||!Ed25519IssuerReader.CanonicalUuid(access.SessionId)||access.AuthorizedParty!=options.PublicClientId||identity.SessionId is not null&&identity.SessionId!=access.SessionId)throw new IOException("Native token pair refused");
   var current=await NativeCakeErrors.Await(api.CurrentForHostAsync(accessRaw,admit,token)).ConfigureAwait(false);Demand();
   if(current.Failure!=ApiFailure.None||current.Value is null||current.Value.AccountID.ToString("D")!=identity.Subject)throw new IOException("Canonical current account unavailable");
   result=new(accessRaw,current.Value.AccountID,Guid.ParseExact(access.SessionId!,"D"),current.Value.DisplayName,access.ExpiresAt);
  }
  catch(Exception e){NativeCakeErrors.Add(errors,e);}
  finally
  {
   // All attempted cleanup and original receive outcomes are retained independently.
   try{deadline?.Cancel();}catch(Exception e){NativeCakeErrors.Add(errors,e);}
   try{callback?.Stop();}catch(Exception e){NativeCakeErrors.Add(errors,e);}
   if(receive is not null)try{await receive.ConfigureAwait(false);}catch(Exception e){NativeCakeErrors.AddTask(errors,receive,e);}
   for(var i=resources.Count-1;i>=0;i--)try{resources[i].Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}
   try{deadline?.Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}
  }
  NativeCakeErrors.Throw(errors);return result??throw new IOException("No verified account session");
 }
 private async Task DiscoverAsync(CancellationToken ct)
 {
  HttpResponseMessage? response=null;JsonDocument? doc=null;List<Exception> errors=[];
  try
  {
   response=await NativeCakeErrors.Await(http.GetAsync(options.DiscoveryUri,HttpCompletionOption.ResponseHeadersRead,ct)).ConfigureAwait(false);
   if(response.StatusCode!=HttpStatusCode.OK)throw new IOException("Issuer discovery unavailable");
   doc=await ReadJsonAsync(response,ct).ConfigureAwait(false);var root=doc.RootElement;
   foreach(var pair in new[]{("issuer",options.Issuer),("authorization_endpoint",options.AuthorizationUri.AbsoluteUri),("token_endpoint",options.TokenUri.AbsoluteUri),("jwks_uri",options.JwksUri.AbsoluteUri)})
    if(!root.TryGetProperty(pair.Item1,out var v)||v.ValueKind!=JsonValueKind.String||v.GetString()!=pair.Item2)throw new IOException("Issuer discovery mismatch");
  }
  catch(Exception e){NativeCakeErrors.Add(errors,e);}
  finally{try{doc?.Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}try{response?.Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}}
  NativeCakeErrors.Throw(errors);
 }
 private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response,CancellationToken ct)
 {
  if(response.Content.Headers.ContentLength>65536)throw new IOException("Provider response exceeds limit");
  await using var stream=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);using var bytes=new MemoryStream();var buffer=new byte[4096];int n;
  while((n=await stream.ReadAsync(buffer,ct).ConfigureAwait(false))!=0){if(bytes.Length+n>65536)throw new IOException("Provider response exceeds limit");bytes.Write(buffer,0,n);}
  var doc=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions{MaxDepth=8});
  if(doc.RootElement.ValueKind!=JsonValueKind.Object||doc.RootElement.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=doc.RootElement.EnumerateObject().Count()){doc.Dispose();throw new IOException("Malformed provider response");}return doc;
 }
}
internal sealed class NativeCakeLoopback
{
 private readonly TcpListener listener;private readonly Uri redirect;
 internal NativeCakeLoopback(Uri original)
 {redirect=original;listener=new(IPAddress.Loopback,original.Port);listener.Server.ExclusiveAddressUse=true;try{listener.Start(1);}catch(Exception failure){List<Exception> errors=[failure];try{listener.Stop();}catch(Exception cleanup){NativeCakeErrors.Add(errors,cleanup);}NativeCakeErrors.Throw(errors);throw;}}
 internal void Stop()=>listener.Stop();
 internal async Task<IReadOnlyDictionary<string,string>> ReceiveAsync(CancellationToken ct)
 {
  TcpClient? client=null;NetworkStream? stream=null;IReadOnlyDictionary<string,string>? result=null;List<Exception> errors=[];
  try
  {
  client=await NativeCakeErrors.Await(listener.AcceptTcpClientAsync(ct).AsTask()).ConfigureAwait(false);stream=client.GetStream();
  // Bounded byte-by-byte headers: no unbounded StreamReader allocation from a local caller.
  var data=new List<byte>(1024);var one=new byte[1];
  while(data.Count<8192)
  {if(await stream.ReadAsync(one,ct).ConfigureAwait(false)==0)throw new IOException("Incomplete loopback request");data.Add(one[0]);var c=data.Count;if(c>=4&&data[c-4]==13&&data[c-3]==10&&data[c-2]==13&&data[c-1]==10)break;}
  if(data.Count>=8192||data.Any(x=>x>127||x==0))throw new IOException("Loopback header refused");
  var lines=Encoding.ASCII.GetString(data.ToArray()).Split("\r\n",StringSplitOptions.None);var first=lines[0].Split(' ');
  if(first.Length!=3||first[0]!="GET"||first[2]!="HTTP/1.1"||!first[1].StartsWith('/')||first[1].StartsWith("//",StringComparison.Ordinal))throw new IOException("Loopback method refused");
  var hosts=lines.Skip(1).Where(x=>x.StartsWith("Host:",StringComparison.OrdinalIgnoreCase)).ToArray();
  if(hosts.Length!=1||hosts[0][5..].Trim()!=redirect.Authority)throw new IOException("Loopback host refused");
  var uri=new Uri(redirect,first[1]);if(uri.GetLeftPart(UriPartial.Authority)!=redirect.GetLeftPart(UriPartial.Authority)||uri.AbsolutePath!=redirect.AbsolutePath||uri.Fragment.Length!=0||uri.Query.Length>6144)throw new IOException("Loopback target refused");
  var values=new Dictionary<string,string>(StringComparer.Ordinal);
  foreach(var part in uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
  {var pair=part.Split('=',2);if(pair.Length!=2||!values.TryAdd(Uri.UnescapeDataString(pair[0]),Uri.UnescapeDataString(pair[1].Replace('+',' '))))throw new IOException("Duplicate or malformed callback");}
  var body=Encoding.UTF8.GetBytes("The callback was received. Return to 9to1 to see whether sign-in succeeded.");
  var headers=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: "+body.Length+"\r\n\r\n");
  await stream.WriteAsync(headers,ct).ConfigureAwait(false);await stream.WriteAsync(body,ct).ConfigureAwait(false);await stream.FlushAsync(ct).ConfigureAwait(false);result=values;
  }
  catch(Exception e){NativeCakeErrors.Add(errors,e);}
  finally
  {try{stream?.Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}try{client?.Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}}
  NativeCakeErrors.Throw(errors);return result??throw new IOException("No loopback callback");
 }
}
internal static class NativeCakeErrors
{
 internal static async Task<T> Await<T>(Task<T> actual)
 {
  try{return await actual.ConfigureAwait(false);}
  catch{if(actual.IsFaulted&&actual.Exception is {} compound&&compound.InnerExceptions.Count>1)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(compound).Throw();throw;}
 }
 internal static void Add(List<Exception> errors,Exception e){if(!errors.Any(x=>ReferenceEquals(x,e)))errors.Add(e);}
 internal static void AddTask(List<Exception> errors,Task actual,Exception observed)
 {if(actual.IsFaulted&&actual.Exception is {} compound){foreach(var e in compound.InnerExceptions)Add(errors,e);}else Add(errors,observed);}
 internal static void Throw(List<Exception> errors)
 {if(errors.Count==1)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();if(errors.Count>1)throw new AggregateException("Native CAKE operation and original cleanup failed",errors);}
}
