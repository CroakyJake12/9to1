using System.Net;
using System.Text.Json;
using NineToOne.Accounts.Oidc;
using NineToOne.Accounts.Remote;
using NineToOne.Web;
internal static class RemoteAccountWebFacadeSpecs
{
 public static async Task RunAsync()
 {
  var now=DateTimeOffset.UtcNow;var id=Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
  var token=new VerifiedToken("https://issuer.example.invalid/",id.ToString("D"),new HashSet<string>{"api"},now.AddMinutes(5),now,null,null,"650e8400-e29b-41d4-a716-446655440000","fictional-revision",new HashSet<string>{"cake:account:read"},now);
  var policy=new TokenPolicy(token.Issuer,"api",TokenPurpose.ApiAccessToken,null,new HashSet<string>{"cake:account:read"},new HashSet<string>{"RS256"});
  int calls=0;long generation=1;TaskCompletionSource? requestReached=null,release=null;
  using var client=new WorkerAccountApiClient(new("https://worker.example.invalid/"),new OidcResourceConsumer(new Reader(token)),policy,TimeProvider.System,new Handler(async ct=>{calls++;requestReached?.SetResult();if(release is not null)await release.Task.WaitAsync(ct);return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new RemoteCurrent(id,"Fictional")))};}));
  var facade=new RemoteAccountWebFacade(client,_=>ValueTask.FromResult<string?>("controlled"),()=>generation,default);
  Require((await facade.CurrentAsync(default)).Value?.AccountID==id&&calls==1);
  var unavailable=new RemoteAccountWebFacade(client,_=>ValueTask.FromResult<string?>(null),()=>generation,default);
  Require((await unavailable.CurrentAsync(default)).Failure==ApiFailure.InvalidToken&&calls==1);
  var tokenReached=new TaskCompletionSource();var tokenRelease=new TaskCompletionSource();
  var beforeRead=new RemoteAccountWebFacade(client,async ct=>{tokenReached.SetResult();await tokenRelease.Task.WaitAsync(ct);return "controlled";},()=>generation,default);
  var pending=beforeRead.CurrentAsync(default);await tokenReached.Task;generation++;tokenRelease.SetResult();Require((await pending).Failure==ApiFailure.Unavailable&&calls==1);
  requestReached=new();release=new();pending=facade.CurrentAsync(default);await requestReached.Task;generation++;release.SetResult();Require((await pending).Failure==ApiFailure.Unavailable&&calls==2);
  using var host=new CancellationTokenSource();host.Cancel();var closed=new RemoteAccountWebFacade(client,_=>ValueTask.FromResult<string?>("controlled"),()=>generation,host.Token);
  Require((await closed.CurrentAsync(default)).Failure==ApiFailure.Unavailable&&calls==2);
 }
 private sealed class Reader(VerifiedToken token):IVerifiedIssuerTokenReader {public ValueTask<VerifiedToken?> VerifyAsync(string raw,TokenPolicy policy,CancellationToken ct)=>ValueTask.FromResult<VerifiedToken?>(token);}
 private sealed class Handler(Func<CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>action(ct);}
 private static void Require(bool condition){if(!condition)throw new InvalidOperationException("controlled Web facade lifecycle assertion failed");}
}
