using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
   =>SendAsync<RemoteCurrent>(HttpMethod.Get,"api/account/current",token,"cake:account:read",null,(x,id)=>x.AccountID==id&&IsValidCurrent(x),ct);
  public Task<ApiResult<RemoteProfile>> ProfileAsync(string token,CancellationToken ct)
   =>SendAsync<RemoteProfile>(HttpMethod.Get,"api/account/profile",token,"cake:profile:read",null,(x,id)=>x.AccountID==id&&IsValidProfile(x),ct);
 // Denial-only private Web-host read admission; Worker independently validates all authority.
  public Task<ApiResult<RemoteCurrent>> CurrentForHostAsync(string token,Func<bool> readAdmission,CancellationToken ct)
   =>SendAsync<RemoteCurrent>(HttpMethod.Get,"api/account/current",token,"cake:account:read",null,(x,id)=>x.AccountID==id&&IsValidCurrent(x),ct,readAdmission:readAdmission);
  public Task<ApiResult<RemoteProfile>> ProfileForHostAsync(string token,Func<bool> readAdmission,CancellationToken ct)
   =>SendAsync<RemoteProfile>(HttpMethod.Get,"api/account/profile",token,"cake:profile:read",null,(x,id)=>x.AccountID==id&&IsValidProfile(x),ct,readAdmission:readAdmission);
 public Task<ApiResult<RemoteProfile>> UpdateProfileAsync(string token,long expectedRevision,JsonElement fields,CancellationToken ct)
 {
  var names=new HashSet<string>{"name","username","icon","pronouns","job"};
    if(expectedRevision<0||expectedRevision==long.MaxValue||fields.ValueKind!=JsonValueKind.Object
    ||System.Text.Encoding.UTF8.GetByteCount(fields.GetRawText())>65536)
    return Task.FromResult(new ApiResult<RemoteProfile>(null,ApiFailure.InvalidInput));
   var seen=new HashSet<string>(StringComparer.Ordinal);
    foreach(var field in fields.EnumerateObject())
     if(!seen.Add(field.Name)||!names.Contains(field.Name)||field.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
     ||(field.Value.ValueKind==JsonValueKind.String&&field.Value.GetString()!.Length>1000)
     ||field.Name=="name"&&(field.Value.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(field.Value.GetString()))
      ||field.Name=="username"&&(field.Value.ValueKind!=JsonValueKind.String||!IsValidUsername(field.Value.GetString())))
      return Task.FromResult(new ApiResult<RemoteProfile>(null,ApiFailure.InvalidInput));
    if(expectedRevision==0&&(!seen.Contains("name")||!seen.Contains("username")))
     return Task.FromResult(new ApiResult<RemoteProfile>(null,ApiFailure.InvalidInput));
   return SendAsync<RemoteProfile>(HttpMethod.Patch,"api/account/profile",token,"cake:profile:write",new{expectedRevision,fields=fields.Clone()},(x,id)=>x.AccountID==id&&x.Revision==expectedRevision+1&&IsValidProfile(x),ct);
  }
  private static bool IsValidProfile(RemoteProfile profile)
   =>profile.AccountID!=Guid.Empty&&!string.IsNullOrWhiteSpace(profile.Name)&&profile.Name.Length<=1000&&
    IsValidUsername(profile.Username)&&profile.Username.Length<=1000&&(profile.Icon?.Length??0)<=1000&&
    (profile.Pronouns?.Length??0)<=1000&&(profile.Job?.Length??0)<=1000&&profile.Revision>=1;
  private static bool IsValidUsername(string? username)
  {
   if(string.IsNullOrWhiteSpace(username))return false;
   try{return !string.IsNullOrWhiteSpace(username.Trim().TrimStart('@').Normalize(NormalizationForm.FormKC));}
   catch(ArgumentException){return false;}
  }
  private static bool IsValidCurrent(RemoteCurrent current)
   =>current.AccountID!=Guid.Empty&&!string.IsNullOrWhiteSpace(current.DisplayName);
  private static bool IsValidSession(RemoteSession? session,Guid accountID)
   =>session is not null&&session.AccountID==accountID&&session.SessionID!=Guid.Empty&&
    !string.IsNullOrWhiteSpace(session.DeviceName)&&session.DeviceName.Length<=120&&session.CreatedAt!=default&&
    session.ExpiresAt>session.CreatedAt&&(session.RevokedAt is null||session.RevokedAt.GetValueOrDefault()>=session.CreatedAt)&&
    (session.RegisteredClientID is null||!string.IsNullOrWhiteSpace(session.RegisteredClientID));
  public Task<ApiResult<RemoteSession[]>> SessionsAsync(string token,CancellationToken ct)
   =>SendAsync<RemoteSession[]>(HttpMethod.Get,"api/account/sessions",token,"cake:sessions:read",null,
    (items,id)=>items.All(x=>IsValidSession(x,id))&&items.Select(x=>x.SessionID).Distinct().Count()==items.Length,ct);
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
    if(response.StatusCode==(HttpStatusCode)429)return new(default,ApiFailure.Limited,RetryAfterSeconds(response));
   if(response.StatusCode==HttpStatusCode.Conflict)return new(default,ApiFailure.Conflict);
   if(expectNoContent && response.StatusCode==HttpStatusCode.NoContent)
    return new((T)(object)new RemoteMutationAcknowledgement(true),ApiFailure.None);
   if(response.StatusCode!=HttpStatusCode.OK || expectNoContent)return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.Unavailable);
   if(response.Content.Headers.ContentLength>65536)return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.InvalidResponse);
   await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);using var bytes=new MemoryStream();var buffer=new byte[4096];int read;
   while((read=await stream.ReadAsync(buffer,deadline.Token))>0){if(bytes.Length+read>65536)return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.InvalidResponse);bytes.Write(buffer,0,read);}
   using var document=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions{MaxDepth=12});
   var payload=document.RootElement;
   static bool Unique(JsonElement e){if(e.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var item in e.EnumerateObject())if(!names.Add(item.Name)||!Unique(item.Value))return false;}else if(e.ValueKind==JsonValueKind.Array){foreach(var item in e.EnumerateArray())if(!Unique(item))return false;}return true;}
   if(payload.ValueKind!=JsonValueKind.Object || !Unique(payload))return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.InvalidResponse);
   if(typeof(T)==typeof(RemoteProfile))
   {if(!payload.TryGetProperty("profile",out var profile)||profile.ValueKind!=JsonValueKind.Object)return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.InvalidResponse);payload=profile;}
   else if(typeof(T)==typeof(RemoteSession[]))
   {if(!payload.TryGetProperty("sessions",out var sessions)||sessions.ValueKind!=JsonValueKind.Array||sessions.GetArrayLength()>4096)return new(default,ApiFailure.InvalidResponse);payload=sessions;}
   static bool CanonicalAccount(JsonElement row)=>row.ValueKind==JsonValueKind.Object&&row.TryGetProperty("accountId",out var a)&&a.ValueKind==JsonValueKind.String&&Guid.TryParseExact(a.GetString(),"D",out var id)&&id!=Guid.Empty&&id.ToString("D")==a.GetString();
   if(typeof(T)==typeof(RemoteSession[]))
   {foreach(var row in payload.EnumerateArray())if(!CanonicalAccount(row)||!row.TryGetProperty("sessionId",out var s)||s.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(s.GetString(),"D",out var sid)||sid==Guid.Empty||sid.ToString("D")!=s.GetString())return new(default,ApiFailure.InvalidResponse);}
   else if(!CanonicalAccount(payload))return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.InvalidResponse);
   var value=JsonSerializer.Deserialize<T>(payload.GetRawText(),new JsonSerializerOptions(JsonSerializerDefaults.Web));
   return value is not null&&matches(value,principal.AccountId)?new(value,ApiFailure.None):new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.InvalidResponse);
  }
  catch(Exception error)when(error is HttpRequestException or OperationCanceledException or JsonException)
  {return new(default,mutation?ApiFailure.CompletionUnknown:ApiFailure.Unavailable);}
  }
  private int? RetryAfterSeconds(HttpResponseMessage response)
  {
   RetryConditionHeaderValue? retryAfter;
   try{retryAfter=response.Headers.RetryAfter;}
   catch(FormatException){return null;}
   if(retryAfter is null)return null;
   double seconds;
   if(retryAfter.Delta is {} delta)
   {
    if(delta<TimeSpan.Zero)return null;
    seconds=Math.Ceiling(delta.TotalSeconds);
   }
   else if(retryAfter.Date is {} date)
    // An elapsed absolute retry date is valid and reports zero; it is not a fabricated delay.
    seconds=Math.Ceiling(Math.Max(0,(date-clock.GetUtcNow()).TotalSeconds));
   else return null;
   return double.IsFinite(seconds)&&seconds>=0&&seconds<=int.MaxValue?(int)seconds:null;
  }
  public void Dispose()=>http.Dispose();
}
