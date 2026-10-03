using System.Text.Json;
using CakeOS.Cui.Language;
using NineToOne.Web.Accounts;
using NineToOne.Web.Services;

const string AccountId = "12345678-1234-4234-8234-123456789abc";
const string SessionId = "87654321-4321-4321-8321-cba987654321";

// Presentation controls only: these scripted wire replies cannot establish live issuer/backend/browser acceptance.
var cases = new (string Name, Func<Task> Run)[]
{
    ("canonical CUI markup parses without errors", async () =>
    {
        var parser = new CuiRichParser();
        parser.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Accounts.cui")), "Accounts.cui");
        Check(!parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "CUI syntax errors");
    }),
    ("unavailable service exposes no account data and cannot edit", async () =>
    {
        using var view = View(new Script((_, _) => Error("ServiceUnavailable")));
        await view.DispatchAsync("Refresh", null);
        Check(Value(view, "HasAccount") is false && Value(view, "CanSave") is false, "unavailable account exposed");
        Check(!view.TrySetValue("Draft.name", "unauthorised"), "unavailable input allowed");
    }),
    ("trusted sign-in callback alone cannot grant account or edit access", async () =>
    {
        var invoked = false;
        using var view = new AccountBrowserBindings(new Script((_, _) => Error("AuthenticationRequired")), action => { action(); return Task.CompletedTask; },
            _ => { invoked = true; return Task.CompletedTask; });
        await view.DispatchAsync("RequestSignIn", null);
        Check(invoked && Value(view, "HasAccount") is false && Value(view, "CanEdit") is false, "sign-in callback became a local account grant");
    }),
    ("profile edits save changed fields and exact original revision only", async () =>
    {
        JsonElement? patch = null;
        var service = Ready((action, args) => { if (action != "UpdateProfile") return null; patch = args; return Ok(new { profile = Profile(2, "Saved") }); });
        using var view = View(service); await view.DispatchAsync("Refresh", null);
        Check(view.TrySetValue("Draft.name", "Saved"), "draft rejected");
        await view.DispatchAsync("ClearPronouns", null);
        await view.DispatchAsync("SaveProfile", null);
        Check(patch!.Value.GetProperty("expectedRevision").GetInt64() == 1, "original CAS revision changed");
        var fields = patch.Value.GetProperty("fields");
        Check(fields.EnumerateObject().Count() == 2 && fields.GetProperty("name").GetString() == "Saved" && fields.GetProperty("pronouns").ValueKind == JsonValueKind.Null, "patch widened or clear lost");
        Check(Value(view, "ProfileRevision")?.ToString() == "2" && Value(view, "HasChanges") is false, "acknowledged save not applied");
    }),
    ("required Name and Username reject whitespace before API dispatch", async () =>
    {
        var writes = 0;
        var service = Ready((action, _) => { if (action == "UpdateProfile") writes++; return null; });
        using var view = View(service); await view.DispatchAsync("Refresh", null);
        foreach (var field in new[] { "name", "username" })
        {
            view.TrySetValue("Draft." + field, " \t "); await view.DispatchAsync("SaveProfile", null);
            Check(writes == 0 && Value(view, "HasChanges") is true, "blank required field dispatched or draft lost");
            await view.DispatchAsync("DiscardAndReload", null);
        }
    }),
    ("conflict retains original draft/revision and requires explicit reload", async () =>
    {
        long revision = 1;
        var service = Ready((action, _) => action switch
        {
            "GetProfile" => Ok(new { profile = Profile(revision) }),
            "UpdateProfile" => Error("profile_conflict", 409, new { error = "profile_conflict", revision = 2 }),
            _ => null,
        });
        using var view = View(service); await view.DispatchAsync("Refresh", null);
        view.TrySetValue("Draft.name", "Unsaved"); await view.DispatchAsync("SaveProfile", null);
        Check(Value(view, "Draft.name")?.ToString() == "Unsaved" && Value(view, "ProfileRevision")?.ToString() == "1", "conflict overwrote intent");
        Check(Value(view, "CanSave") is false && Value(view, "ConflictRevision")?.ToString() == "2", "conflict not gated");
        revision = 2; await view.DispatchAsync("Refresh", null);
        Check(Value(view, "Draft.name")?.ToString() == "Unsaved", "ordinary refresh discarded edits");
        await view.DispatchAsync("DiscardAndReload", null);
        Check(Value(view, "Draft.name")?.ToString() == "Fixture" && Value(view, "ProfileRevision")?.ToString() == "2", "explicit reload failed");
    }),
    ("HTTP permission denial clears all prior private state", async () =>
    {
        var deny = false;
        var service = Ready((action, _) => deny && action == "GetProfile" ? Error("provider_specific_denial", 403) : null);
        using var view = View(service); await view.DispatchAsync("Refresh", null); view.TrySetValue("Draft.name", "Secret draft");
        deny = true; await view.DispatchAsync("Refresh", null);
        Check(Value(view, "HasAccount") is false && Value(view, "HasProfile") is false && Value(view, "Draft.name")?.ToString() == "", "denial retained private context");
        Check(!((IEnumerable<JsonElement>)Value(view, "Sessions")!).Any(), "denial retained sessions");
        Check(!view.TryGetItemValue(Session(), "deviceName", out _), "old repeated item remained readable after denial");
    }),
    ("session selection requires current exact server ID and confirmation", async () =>
    {
        string? selected = null; var calls = 0; var privateCleared = false; AccountBrowserBindings? view = null;
        var service = Ready((action, args) =>
        {
            if (action != "RevokeSession") return null;
            calls++; selected = args!.Value.GetProperty("sessionId").GetString();
            privateCleared = Value(view!, "HasAccount") is false;
            return Ok(null, 204);
        });
        using (view = View(service))
        {
            await view.DispatchAsync("Refresh", null);
            await view.DispatchAsync("SelectSession", JsonSerializer.SerializeToElement(new { sessionId = "foreign" }));
            Check(Value(view, "HasConfirmation") is false && calls == 0, "foreign selection admitted");
            await view.DispatchAsync("SelectSession", Session()); Check(calls == 0, "selection immediately revoked");
            Check(Value(view, "Confirmation")!.ToString()!.Contains(SessionId), "confirmation omits original ID");
            await view.DispatchAsync("ConfirmSessionMutation", null);
            Check(privateCleared, "session dispatched before private UI cleared");
            Check(calls == 1 && selected == SessionId, "wrong session mutation");
        }
    }),
    ("session mutation survives disposal caused by its own private cleanup", async () =>
    {
        AccountBrowserBindings? view = null; var acknowledged = false;
        var service = Ready((action, _) => action == "SignOut" ? Ok(null, 204) : null);
        service.Before = (action, token) =>
        {
            if (action == "SignOut") { view!.Dispose(); Check(!token.IsCancellationRequested, "view disposal aborted captured session mutation"); acknowledged = true; }
        };
        using (view = View(service))
        {
            await view.DispatchAsync("Refresh", null); await view.DispatchAsync("RequestSignOut", null);
            await view.DispatchAsync("ConfirmSessionMutation", null); Check(acknowledged, "signout not reached");
            Check(Value(view, "HasAccount") is false, "disposed view reopened");
        }
    }),
    ("closing private view cancels pending reads and discards late account data", async () =>
    {
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var service = new Script(async (_, _, ct) => { entered.SetResult(); await release.Task; Check(ct.IsCancellationRequested, "read lifetime not cancelled"); ct.ThrowIfCancellationRequested(); return Ok(new { accountId = AccountId, displayName = "Late" }); });
        var view = View(service); var read = view.DispatchAsync("Refresh", null).AsTask(); await entered.Task;
        view.Dispose(); release.SetResult(); await read;
        Check(Value(view, "HasAccount") is false && Value(view, "CanEdit") is false, "late read reopened disposed view");
    }),
    ("cross-account profile observation clears prior private data", async () =>
    {
        var service = Ready((action, _) => action == "GetProfile" ? Ok(new { profile = Profile(1, account: "87654321-4321-4321-8321-cba987654321") }) : null);
        using var view = View(service); await view.DispatchAsync("Refresh", null);
        Check(Value(view, "HasAccount") is false && Value(view, "HasProfile") is false, "mixed account data admitted");
    }),
    ("malformed service reply is unavailable and clears private state", async () =>
    {
        using var view = View(new Script((_, _) => Ok(new { incomplete = true })));
        await view.DispatchAsync("Refresh", null);
        Check(Value(view, "HasAccount") is false, "malformed account accepted");
    }),
    ("presentation exception in finally releases the command gate", async () =>
    {
        var failPresentation = true;
        // Isolate the final callback: unavailable refresh has busy/status, clear/failure, then busy=false updates.
        var calls = 0;
        using var controlled = new AccountBrowserBindings(new Script((_, _) => Error("ServiceUnavailable")), action =>
        {
            action(); calls++;
            if (calls == 3 && failPresentation) throw new IOException("presentation unavailable");
            return Task.CompletedTask;
        });
        try { await controlled.DispatchAsync("Refresh", null); throw new InvalidOperationException("expected presentation failure"); }
        catch (IOException) { }
        failPresentation = false;
        var before = calls; await controlled.DispatchAsync("Refresh", null);
        Check(calls > before, "presentation error permanently held the gate");
    }),
};
var passed = 0;
foreach (var (name, run) in cases)
{
    try { await run(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception error) { Console.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"Discovered={cases.Length} Executed={cases.Length} Passed={passed} Failed={cases.Length - passed} Skipped=0");
return passed == cases.Length ? 0 : 1;

static AccountBrowserBindings View(IAccountBrowserTransport service) => new(service, action => { action(); return Task.CompletedTask; });
static object? Value(AccountBrowserBindings view, string path) { Check(view.TryGetValue(path, out var value), $"binding missing: {path}"); return value; }
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static JsonElement Ok(object? body, int status = 200) => JsonSerializer.SerializeToElement(new { ok = true, status, body });
static JsonElement Error(string code, int? status = null, object? body = null) => JsonSerializer.SerializeToElement(new { ok = false, status, error = new { code, body } });
static object Profile(long revision, string name = "Fixture", string account = AccountId) => new { accountId = account, name, username = "fixture", icon = (string?)null, pronouns = (string?)null, job = (string?)null, revision };
static JsonElement Session() => JsonSerializer.SerializeToElement(new { sessionId = SessionId, accountId = AccountId, deviceName = "Fixture browser", createdAt = "2026-10-03T00:00:00Z", expiresAt = "2026-10-04T00:00:00Z", revokedAt = (string?)null, registeredClientId = (string?)null });
static Script Ready(Func<string, JsonElement?, JsonElement?>? customize = null) => new((action, args) => customize?.Invoke(action, args) ?? (action switch
{
    "GetCurrent" => Ok(new { accountId = AccountId, displayName = "Fixture" }),
    "GetProfile" => Ok(new { profile = Profile(1) }),
    "ListSessions" => Ok(new { sessions = new[] { Session() } }),
    _ => Error("ServiceUnavailable"),
}));

sealed class Script : IAccountBrowserTransport
{
    private readonly Func<string, JsonElement?, CancellationToken, Task<JsonElement>> _reply;
    public Action<string, CancellationToken>? Before { get; set; }
    public Script(Func<string, JsonElement?, JsonElement> reply) : this((action, args, _) => Task.FromResult(reply(action, args))) { }
    public Script(Func<string, JsonElement?, CancellationToken, Task<JsonElement>> reply) => _reply = reply;
    public Task<JsonElement> InvokeAsync(string action, JsonElement? args, CancellationToken ct) { Before?.Invoke(action, ct); return _reply(action, args, ct); }
}
