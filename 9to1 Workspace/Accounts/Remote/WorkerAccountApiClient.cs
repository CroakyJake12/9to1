using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NineToOne.Accounts.Oidc;
namespace NineToOne.Accounts.Remote;
public enum ApiFailure { None, InvalidInput, Unavailable, InvalidToken, PermissionDenied, Limited, Conflict, InvalidResponse, CompletionUnknown }
public sealed record ApiResult<T>(T? Value,ApiFailure Failure,int? RetryAfterSeconds=null);
public sealed record RemoteCurrent(Guid AccountID,string DisplayName);
public sealed record RemoteProfile(Guid AccountID,string Name,string Username,string? Icon,string? Pronouns,string? Job,long Revision);
public sealed record RemoteSession(Guid SessionID,Guid AccountID,string DeviceName,DateTimeOffset CreatedAt,
 DateTimeOffset ExpiresAt,DateTimeOffset? RevokedAt,string? RegisteredClientID);
public sealed record RemoteMutationAcknowledgement(bool Acknowledged);
public sealed class WorkerAccountApiClient : IDisposable
{
 private readonly HttpClient http;private readonly OidcResourceConsumer consumer;private readonly TokenPolicy policy;
 private readonly TimeProvider clock;private readonly Uri origin;
 public WorkerAccountApiClient(Uri origin,OidcResourceConsumer consumer,TokenPolicy policy,TimeProvider clock)
 :this(origin,consumer,policy,clock,new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){}
 internal WorkerAccountApiClient(Uri origin,OidcResourceConsumer consumer,TokenPolicy policy,TimeProvider clock,HttpMessageHandler handler)
 {if(origin.Scheme!="https"||origin.UserInfo.Length!=0||origin.AbsolutePath!="/"||origin.Query.Length!=0||origin.Fragment.Length!=0)throw new ArgumentException("Exact HTTPS Worker origin required");
  this.origin=origin;this.consumer=consumer;this.policy=policy;this.clock=clock;http=new(handler){Timeout=TimeSpan.FromSeconds(15)};}
 public Task<ApiResult<RemoteCurrent>> CurrentAsync(string token,CancellationToken ct)
  =>SendAsync<RemoteCurrent>(HttpMethod.Get,"api/account/current",token,"cake:account:read",null,(x,id)=>x.AccountID==id,ct);
 public Task<ApiResult<RemoteProfile>> ProfileAsync(string token,CancellationToken ct)
  =>SendAsync<RemoteProfile>(HttpMethod.Get,"api/account/profile",token,"cake:profile:read",null,(x,id)=>x.AccountID==id,ct);
 // Denial-only private Web-host read admission; Worker independently validates all authority.
 public Task<ApiResult<RemoteCurrent>> CurrentForHostAsync(string token,Func<bool> readAdmission,CancellationToken ct)
  =>SendAsync<RemoteCurrent>(HttpMethod.Get,"api/account/current",token,"cake:account:read",null,(x,id)=>x.AccountID==id,ct,readAdmission:readAdmission);
 public Task<ApiResult<RemoteProfile>> ProfileForHostAsync(string token,Func<bool> readAdmission,CancellationToken ct)
  =>SendAsync<RemoteProfile>(HttpMethod.Get,"api/account/profile",token,"cake:profile:read",null,(x,id)=>x.AccountID==id,ct,readAdmission:readAdmission);
 public Task<ApiResult<RemoteProfile>> UpdateProfileAsync(string token,long expectedRevision,JsonElement fields,CancellationToken ct)
 {
  var names=new HashSet<string>{"name","username","icon","pronouns","job"};
  if(expectedRevision<0||expectedRevision==long.MaxValue||fields.ValueKind!=JsonValueKind.Object
   ||System.Text.Encoding.UTF8.GetByteCount(fields.GetRawText())>65536)
   return Task.FromResult(new ApiResult<RemoteProfile>(null,ApiFailure.InvalidInput));
  foreach(var field in fields.EnumerateObject())
   if(!names.Contains(field.Name)||field.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
    ||(field.Value.ValueKind==JsonValueKind.String&&field.Value.GetString()!.Length>1000))
    return Task.FromResult(new ApiResult<RemoteProfile>(null,ApiFailure.InvalidInput));
  return SendAsync<RemoteProfile>(HttpMethod.Patch,"api/account/profile",token,"cake:profile:write",new{expectedRevision,fields=fields.Clone()},(x,id)=>x.AccountID==id&&x.Revision==expectedRevision+1,ct);
 }
 public Task<ApiResult<RemoteSession[]>> SessionsAsync(string token,CancellationToken ct)
  =>SendAsync<RemoteSession[]>(HttpMethod.Get,"api/account/sessions",token,"cake:sessions:read",null,
   (items,id)=>items.All(x=>x.AccountID==id&&x.SessionID!=Guid.Empty)&&items.Select(x=>x.SessionID).Distinct().Count()==items.Length,ct);
 public Task<ApiResult<RemoteMutationAcknowledgement>> SignOutAsync(string token,CancellationToken ct)
  =>SendAsync<RemoteMutationAcknowledgement>(HttpMethod.Post,"api/account/signout",token,"cake:sessions:revoke",null,(_,_)=>true,ct,true);
 public Task<ApiResult<RemoteMutationAcknowledgement>> RevokeSessionAsync(string token,Guid originalSelectedSessionId,CancellationToken ct)
  =>originalSelectedSessionId==Guid.Empty?Task.FromResult(new ApiResult<RemoteMutationAcknowledgement>(null,ApiFailure.InvalidResponse)):
   SendAsync<RemoteMutationAcknowledgement>(HttpMethod.Delete,"api/account/sessions/"+originalSelectedSessionId.ToString("D"),token,"cake:sessions:revoke",null,(_,_)=>true,ct,true);
 public Task<ApiResult<RemoteMutationAcknowledgement>> RevokeOthersAsync(string token,CancellationToken ct)
  =>SendAsync<RemoteMutationAcknowledgement>(HttpMethod.Post,"api/account/revoke-other-sessions",token,"cake:sessions:revoke",null,(_,_)=>true,ct,true);
 private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method,string route,string token,string scope,object? body,Func<T,Guid,bool> matches,CancellationToken ct,bool expectNoContent=false,Func<bool>? readAdmission=null)
 {
  var mutation=method!=HttpMethod.Get;ObservedApiPrincipal? principal;
  try {principal=await consumer.ObserveAsync(token,policy with {RequiredScopes=new HashSet<string>{scope}},clock.GetUtcNow(),ct);}
  catch(Exception error)when(error is HttpRequestException or JsonException){return new(default,ApiFailure.Unavailable);}
  if(principal is null)return new(default,ApiFailure.InvalidToken);
  if(ct.IsCancellationRequested)return new(default,ApiFailure.Unavailable);
  if(readAdmission is not null)
  {try{if(!readAdmission())return new(default,ApiFailure.Unavailable);}catch(Exception){return new(default,ApiFailure.Unavailable);}}
  // Observation is not a grant: canonical Worker MUST independently verify token/current session/scopes/CAS.
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
  using var request=new HttpRequestMessage(method,new Uri(origin,route));request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  if(body is not null)request.Content=JsonContent.Create(body);
  try
  {
   using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
   if(response.StatusCode==HttpStatusCode.Unauthorized)return new(default,ApiFailure.InvalidToken);
   if(response.StatusCode==HttpStatusCode.Forbidden)return new(default,ApiFailure.PermissionDenied);
   if(response.StatusCode==(HttpStatusCode)429)return new(default,ApiFailure.Limited);
   if(response.StatusCode==HttpStatusCode.Conflict)return new(default,ApiFailure.Conflict);
   if(expectNoContent && response.StatusCode==HttpStatusCode.NoContent)
    return new((T)(object)new RemoteMutationAcknowledgement(true),ApiFailure.None);
   if(response.StatusCode!=HttpStatusCode.OK || expectNoContent)return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.Unavailable);
   if(response.Content.Headers.ContentLength>65536)return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.InvalidResponse);
   await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);using var bytes=new MemoryStream();var buffer=new byte[4096];int read;
   while((read=await stream.ReadAsync(buffer,deadline.Token))>0){if(bytes.Length+read>65536)return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.InvalidResponse);bytes.Write(buffer,0,read);}
   var value=JsonSerializer.Deserialize<T>(bytes.ToArray(),new JsonSerializerOptions(JsonSerializerDefaults.Web));
   return value is not null&&matches(value,principal.AccountId)?new(value,ApiFailure.None):new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.InvalidResponse);
  }
  catch(Exception error)when(error is HttpRequestException or OperationCanceledException or JsonException)
  {return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.Unavailable);}
 }
 public void Dispose()=>http.Dispose();
}
