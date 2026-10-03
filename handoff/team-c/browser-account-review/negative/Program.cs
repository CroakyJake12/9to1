using System.Text.Json;
using NineToOne.Web.Accounts;
using NineToOne.Web.Services;
var transport = new Service();
var view = new AccountBrowserBindings(transport, action => { action(); return Task.CompletedTask; });
await view.DispatchAsync("Refresh", null);
view.TrySetValue("Draft.name", "Private unsaved draft");
transport.Wait = true;
var pending = view.DispatchAsync("Refresh", null).AsTask();
await transport.Entered.Task;
var threw = false;
try { view.Dispose(); } catch (AggregateException) { threw = true; }
view.TryGetValue("HasAccount", out var account);
view.TryGetValue("HasProfile", out var profile);
view.TryGetValue("Draft.name", out var draft);
Console.WriteLine(JsonSerializer.Serialize(new { cancellationCallbackThrew = threw, disposedRetainsAccount = account, disposedRetainsProfile = profile, disposedRetainsDraft = Equals(draft, "Private unsaved draft") }));
transport.Release.SetResult();
await pending;
return account is false && profile is false && Equals(draft, "") ? 0 : 1;
sealed class Service : IAccountBrowserTransport {
 public bool Wait;
 public TaskCompletionSource Entered = new();
 public TaskCompletionSource Release = new();
 public async Task<JsonElement> InvokeAsync(string action, JsonElement? args, CancellationToken ct) {
  if (Wait) {
   using var registration = ct.Register(() => throw new IOException("Isolated cancellation failure"));
   Entered.SetResult(); await Release.Task; ct.ThrowIfCancellationRequested();
  }
  const string id="12345678-1234-4234-8234-123456789abc";
  object body = action switch {
   "GetCurrent" => new {accountId=id,displayName="Fixture"},
   "GetProfile" => new {profile=new {accountId=id,name="Fixture",username="fixture",icon=(string?)null,pronouns=(string?)null,job=(string?)null,revision=1}},
   "ListSessions" => new {sessions=Array.Empty<object>()},
   _ => throw new InvalidOperationException(action)
  };
  return JsonSerializer.SerializeToElement(new {ok=true,status=200,body});
 }
}
