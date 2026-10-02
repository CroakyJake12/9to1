using NineToOne.Accounts.Remote;
namespace NineToOne.Web;

// Ordinary account API consumer; generation is a trusted browser-host lifetime, not an authentication revision or Home grant.
public sealed class RemoteAccountWebFacade(WorkerAccountApiClient client,
 Func<CancellationToken,ValueTask<string?>> privateCurrentToken,Func<long> currentGeneration,CancellationToken hostLifetime)
{
 public Task<ApiResult<RemoteCurrent>> CurrentAsync(CancellationToken ct)=>ReadAsync((token,admit,cancel)=>client.CurrentForHostAsync(token,admit,cancel),ct);
 public Task<ApiResult<RemoteProfile>> ProfileAsync(CancellationToken ct)=>ReadAsync((token,admit,cancel)=>client.ProfileForHostAsync(token,admit,cancel),ct);
 private async Task<ApiResult<T>> ReadAsync<T>(Func<string,Func<bool>,CancellationToken,Task<ApiResult<T>>> request,CancellationToken ct)
 {
  using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct,hostLifetime);
  var generation=currentGeneration();if(generation<0||lifetime.IsCancellationRequested)return new(default,ApiFailure.Unavailable);
  var token=await privateCurrentToken(lifetime.Token);
  if(lifetime.IsCancellationRequested||currentGeneration()!=generation)return new(default,ApiFailure.Unavailable);
  if(string.IsNullOrWhiteSpace(token))return new(default,ApiFailure.InvalidToken);
  var result=await request(token,()=>!lifetime.IsCancellationRequested&&currentGeneration()==generation,lifetime.Token);
  if(lifetime.IsCancellationRequested||currentGeneration()!=generation)return new(default,ApiFailure.Unavailable);
  return result;
 }
}
