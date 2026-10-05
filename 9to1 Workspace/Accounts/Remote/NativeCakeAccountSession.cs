using NineToOne.Accounts.Oidc;
using NineToOne.Accounts.Remote;
namespace NineToOne.Accounts.Native;

public sealed record NativeCakeAccountSnapshot(Guid AccountId,Guid SessionId,string DisplayName,DateTimeOffset ExpiresAt);
public interface INativeCakeAccountSession:IAsyncDisposable
{
 bool IsAvailable{get;}
 Task<NativeCakeAccountSnapshot> SignInAsync(CancellationToken ct);
 Task<ApiResult<RemoteCurrent>> CurrentAsync(CancellationToken ct);
 Task<ApiResult<RemoteProfile>> ProfileAsync(CancellationToken ct);
 Task<ApiResult<RemoteSession[]>> SessionsAsync(CancellationToken ct);
 Task<ApiResult<RemoteMutationAcknowledgement>> SignOutAsync(CancellationToken ct);
 bool TryGetCurrentSnapshot(out NativeCakeAccountSnapshot? snapshot);
 Task<ApiResult<RemoteMutationAcknowledgement>> RevokeSessionAsync(Guid selectedSessionId,CancellationToken ct);
 Task<ApiResult<RemoteMutationAcknowledgement>> RevokeOtherSessionsAsync(CancellationToken ct);
 Task CloseAndDrainAsync();
}
// Optional account client, never a local credential/code issuer or Home principal source.
// Await close from the independent UI owner, not inside an operation that close encompasses.
public sealed class NativeCakeAccountSession:INativeCakeAccountSession
{
 private readonly object sync=new();private readonly CancellationTokenSource stop=new();
 private readonly List<Work> issued=[];private readonly List<Exception> cleanupErrors=[];
 private readonly TimeProvider clock;private readonly NativeCakeBrowserFlow? flow;private readonly WorkerAccountApiClient? api;
 private readonly BoundedIssuerKeysSource? keys;private readonly HttpClient? http;
 private NativeCakeCredential? credential;private long generation;private bool signing,closing;
 private Task? close;private readonly InvalidOperationException capacityRefusal=new("Native account operation fault custody is full; close this owner");private bool capacitySealed;
 public bool IsAvailable=>flow is not null;
 public NativeCakeAccountSession(NativeCakeClientOptions? supplied,INativeCakeAccessCredentialSource? originalAccess=null)
 {
  clock=TimeProvider.System;if(supplied is null||originalAccess is null)return;var o=supplied.Capture();
  keys=new(new(new Uri(o.Issuer),o.DiscoveryUri,o.JwksUri,65536,TimeSpan.FromMinutes(1),TimeSpan.FromSeconds(15)),clock,new NativeCakeAccessHandler(o.AccountApiOrigin,originalAccess));
  var reader=new Ed25519IssuerReader(keys,clock);
  // Genuine current server has sid/live-session checks and no auth_revision producer.
  // This opt-in is ONLY an API observation; default consumer/Home contracts stay strict.
  var policy=new TokenPolicy(o.Issuer,o.ApiResource,TokenPurpose.ApiAccessToken,null,NativeCakeClientOptions.Scopes.Where(x=>x.StartsWith("cake:",StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal),new HashSet<string>{"EdDSA"});
  api=new(o.AccountApiOrigin,new OidcResourceConsumer(reader,requireAuthenticationRevision:false),policy,clock,new NativeCakeAccessHandler(o.AccountApiOrigin,originalAccess));
  http=new(new NativeCakeAccessHandler(o.AccountApiOrigin,originalAccess)){Timeout=TimeSpan.FromSeconds(15)};
  flow=new(o,reader,api,http,new SystemNativeCakeBrowserRequestSource(),clock);
 }
 internal NativeCakeAccountSession(NativeCakeBrowserFlow originalFlow,WorkerAccountApiClient originalApi,TimeProvider time)
 {flow=originalFlow;api=originalApi;clock=time;}
 public Task<NativeCakeAccountSnapshot> SignInAsync(CancellationToken ct)
 {
  lock(sync)
  {
   DemandAdmission();ct.ThrowIfCancellationRequested();var originalFlow=flow;if(originalFlow is null)throw new InvalidOperationException("Native CAKE client is not configured");if(signing)throw new InvalidOperationException("Sign-in is already pending");
   signing=true;credential=null;var original=++generation;
   try{return Issue(async token=>
   {
    try{var obtained=await originalFlow.RunAsync(original,()=>CurrentGeneration(original),token).ConfigureAwait(false);lock(sync){token.ThrowIfCancellationRequested();if(!CurrentGeneration(original))throw new OperationCanceledException("Account context retired",token);credential=obtained;}return new NativeCakeAccountSnapshot(obtained.AccountId,obtained.SessionId,obtained.DisplayName,obtained.ExpiresAt);}
    finally{lock(sync)signing=false;}
   },ct);}catch{signing=false;throw;}
  }
 }
 public Task<ApiResult<RemoteCurrent>> CurrentAsync(CancellationToken ct)=>ReadAsync((a,c,t)=>a.CurrentForHostAsync(c.AccessToken,()=>CurrentCredential(c),t),ct);
 public Task<ApiResult<RemoteProfile>> ProfileAsync(CancellationToken ct)=>ReadAsync((a,c,t)=>a.ProfileForHostAsync(c.AccessToken,()=>CurrentCredential(c),t),ct);
 public Task<ApiResult<RemoteSession[]>> SessionsAsync(CancellationToken ct)=>ReadAsync((a,c,t)=>a.SessionsAsync(c.AccessToken,t),ct);
 private Task<ApiResult<T>> ReadAsync<T>(Func<WorkerAccountApiClient,NativeCakeCredential,CancellationToken,Task<ApiResult<T>>> body,CancellationToken ct)
 {
  lock(sync)
  {
   DemandAdmission();ct.ThrowIfCancellationRequested();var original=credential;var originalApi=api;
   if(originalApi is null||original is null||!CurrentCredential(original))return Task.FromResult(new ApiResult<T>(default,ApiFailure.InvalidToken));
   return Issue(async token=>
   {
    if(!CurrentCredential(original))return new(default,ApiFailure.InvalidToken);
    var result=await NativeCakeErrors.Await(body(originalApi,original,token)).ConfigureAwait(false);token.ThrowIfCancellationRequested();
    lock(sync){if(!CurrentCredential(original))return new(default,ApiFailure.InvalidToken);if(result.Failure==ApiFailure.InvalidToken){credential=null;generation++;}}
    return result;
   },ct);
  }
 }
 public Task<ApiResult<RemoteMutationAcknowledgement>> SignOutAsync(CancellationToken ct)
 {
  lock(sync)
  {
   DemandAdmission();ct.ThrowIfCancellationRequested();var original=credential;credential=null;generation++;
   var originalApi=api;if(originalApi is null||original is null)return Task.FromResult(new ApiResult<RemoteMutationAcknowledgement>(null,ApiFailure.InvalidToken));
   return Issue(token=>originalApi.SignOutAsync(original.AccessToken,token),ct); // memory revoked before I/O; actual server acknowledgement retained.
  }
 }
 public bool TryGetCurrentSnapshot(out NativeCakeAccountSnapshot? snapshot)
 {
  lock(sync)
  {
   var original=credential;
   if(original is null||!CurrentCredential(original)){snapshot=null;return false;}
   snapshot=new(original.AccountId,original.SessionId,original.DisplayName,original.ExpiresAt);return true;
  }
 }
 public Task<ApiResult<RemoteMutationAcknowledgement>> RevokeSessionAsync(Guid selectedSessionId,CancellationToken ct)
  =>selectedSessionId==Guid.Empty?Task.FromResult(new ApiResult<RemoteMutationAcknowledgement>(null,ApiFailure.InvalidInput)):
    RetireForMutationAsync((a,c,t)=>a.RevokeSessionAsync(c.AccessToken,selectedSessionId,t),ct);
 public Task<ApiResult<RemoteMutationAcknowledgement>> RevokeOtherSessionsAsync(CancellationToken ct)
  =>RetireForMutationAsync((a,c,t)=>a.RevokeOthersAsync(c.AccessToken,t),ct);
 private Task<ApiResult<RemoteMutationAcknowledgement>> RetireForMutationAsync(Func<WorkerAccountApiClient,NativeCakeCredential,CancellationToken,Task<ApiResult<RemoteMutationAcknowledgement>>> mutation,CancellationToken ct)
 {
  lock(sync)
  {
   DemandAdmission();ct.ThrowIfCancellationRequested();var original=credential;var originalApi=api;
   if(original is null||originalApi is null||!CurrentCredential(original))return Task.FromResult(new ApiResult<RemoteMutationAcknowledgement>(null,ApiFailure.InvalidToken));
   credential=null;generation++; // Private local context retires BEFORE actual canonical mutation I/O.
   return Issue(token=>mutation(originalApi,original,token),ct); // No automatic retry after unknown completion.
  }
 }
 private bool CurrentGeneration(long original){lock(sync)return !closing&&generation==original;}
 private bool CurrentCredential(NativeCakeCredential original){lock(sync){if(credential is {} current&&clock.GetUtcNow()>=current.ExpiresAt){credential=null;generation++;}return !closing&&ReferenceEquals(credential,original);}}
 private void DemandAdmission()
 {
  ObjectDisposedException.ThrowIf(closing,this);issued.RemoveAll(x=>x.Task?.IsCompletedSuccessfully==true);
  if(capacitySealed)throw capacityRefusal;if(issued.Count>=16){capacitySealed=true;throw capacityRefusal;}
 }
 private Task<T> Issue<T>(Func<CancellationToken,Task<T>> body,CancellationToken ct)
 {
  // Caller holds the SAME owner lock; publish before any cancellation/body callback.
  var record=new Work();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var actual=RunIssueAsync(gate.Task,body,ct,record);record.Task=actual;issued.Add(record);gate.SetResult();return actual;
 }
 private async Task<T> RunIssueAsync<T>(Task gate,Func<CancellationToken,Task<T>> body,CancellationToken caller,Work record)
 {
  await gate.ConfigureAwait(false);CancellationTokenSource? linked=null;List<Exception> errors=[];T result=default!;
  try{linked=CancellationTokenSource.CreateLinkedTokenSource(caller,stop.Token);linked.Token.ThrowIfCancellationRequested();result=await NativeCakeErrors.Await(body(linked.Token)).ConfigureAwait(false);}
  catch(Exception e){NativeCakeErrors.Add(errors,e);}
  finally{try{linked?.Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}}
  lock(sync)record.Errors=errors.ToArray();NativeCakeErrors.Throw(errors);return result;
 }
 public Task CloseAndDrainAsync()
 {
  lock(sync)
  {
   if(close is not null)return close;closing=true;credential=null;generation++;
   var originals=issued.ToArray();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   close=CloseCoreAsync(gate.Task,originals);gate.SetResult();return close;
  }
 }
 public ValueTask DisposeAsync()=>new(CloseAndDrainAsync());
 private async Task CloseCoreAsync(Task gate,Work[] originals)
 {
  await gate.ConfigureAwait(false);List<Exception> errors=[];
  try{stop.Cancel();}catch(Exception e){NativeCakeErrors.Add(errors,e);}
  foreach(var original in originals)
  {
   if(original.Task is not {} actual)continue;
   try{await actual.ConfigureAwait(false);}catch(Exception e)
   {
    // Recorded original cancellation error is retained instead of a generated await wrapper.
    if(actual.IsFaulted)NativeCakeErrors.AddTask(errors,actual,e);
    else if(original.Errors.Length!=0){foreach(var own in original.Errors)NativeCakeErrors.Add(errors,own);}
    else NativeCakeErrors.Add(errors,e);
   }
  }
  foreach(var error in cleanupErrors)NativeCakeErrors.Add(errors,error);
  if(capacitySealed)NativeCakeErrors.Add(errors,capacityRefusal);
  try{http?.Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}
  try{api?.Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}
  try{keys?.Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}
  try{stop.Dispose();}catch(Exception e){NativeCakeErrors.Add(errors,e);}
  NativeCakeErrors.Throw(errors);
 }
 private sealed class Work {internal Task? Task;internal Exception[] Errors=[];}
}
