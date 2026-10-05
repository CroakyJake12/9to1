using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home.Core;
using NineToOne.Web;
using NineToOne.Web.Accounts;
using NineToOne.Web.Services;

const string Id = "12345678-1234-4234-8234-123456789abc";
var parser = new CuiRichParser();
var document = parser.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Home.cui")), "Home.cui");
Check(!parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "browser Home CUI invalid");
AccountSettingsFeature Feature(Service service) => new(document, service, action => { action(); return Task.CompletedTask; });
var cases = new (string Name, Func<Task> Run)[]
{
    ("unavailable account remains anonymous; real Settings navigation grants no Home provider", async () =>
    {
        var service = new Service((_, _) => Task.FromResult(Fail("AuthenticationRequired")));
        await using var owner = Feature(service);
        HomeFeatureNavigationRequest? target = null;
        using var home = new BrowserHomeContext(document, request => target = request, owner);
        await home.ActivateAsync();
        Check(Value(home, "AccountAvailable") is false && (string?)Value(home, "AccountHeading") == "Welcome to 9-1", "unavailable identity shown");
        await home.DispatchAsync("NavigateSettings", null);
        Check(target?.RouteId == HomeFeatureRouteIds.Settings && service.Actions.SequenceEqual(new[] {"GetCurrent"}), "Settings changed account/route semantics");
        AssertUnavailable(home);
    }),
    ("canonical current/profile/session records supply heading while unrelated Home actions stay unavailable", async () =>
    {
        await using var owner = Feature(Ready());
        using var home = new BrowserHomeContext(document, _ => {}, owner);
        await home.ActivateAsync();
        Check(Value(home, "AccountAvailable") is true && (string?)Value(home, "AccountHeading") == "Welcome, Actual account", "current account not projected");
        Check(((string?)Value(home, "OperationSummary"))!.Contains("have not been loaded"), "account identity became Home data");
        Check(home.IsActionAvailable("AccountRequestSignOut") is true && home.IsActionAvailable("InstallAllUpdates") is false, "account enabled package actions");
        AssertUnavailable(home);
    }),
    ("actual revoked-session reply clears previously returned account identity", async () =>
    {
        var revoked = false;
        var service = Ready(action => revoked ? Fail("session_revoked_or_expired", 401) : null);
        await using var owner = Feature(service);
        using var home = new BrowserHomeContext(document, _ => {}, owner);
        await home.ActivateAsync();
        revoked = true;
        await home.DispatchAsync("AccountRefresh", null);
        Check(Value(home, "AccountAvailable") is false && home.IsActionAvailable("AccountRequestSignOut") is false, "revoked identity retained");
        Check((string?)Value(home, "AccountHeading") == "Welcome to 9-1", "revoked account heading retained");
    }),
    ("foreign profile record cannot preserve a displayed current account", async () =>
    {
        var service = Ready(action => action == "GetProfile" ? Ok(new { profile = Profile("87654321-4321-4321-8321-cba987654321") }) : null);
        await using var owner = Feature(service);
        using var home = new BrowserHomeContext(document, _ => {}, owner);
        await home.ActivateAsync();
        Check(Value(home, "AccountAvailable") is false && (string?)Value(home, "AccountHeading") == "Welcome to 9-1", "foreign profile kept identity");
        Check(!service.Actions.Contains("ListSessions"), "foreign profile continued reads");
    }),
    ("retirement drains SAME held original refresh and refuses late account publication", async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Service((_, _) => { entered.TrySetResult(); return release.Task; });
        var owner = Feature(service);
        var home = new BrowserHomeContext(document, _ => {}, owner);
        var original = home.ActivateAsync();
        Task? close = null;
        try
        {
            await entered.Task;
            home.Dispose();
            close = owner.DisposeAsync().AsTask();
            Check(!original.IsCompleted && !close.IsCompleted, "signal substituted actual refresh settlement");
            release.TrySetResult(Ok(new { accountId = Id, displayName = "Late account" }));
            await original;
            await close;
            Check(Value(home, "AccountAvailable") is false && service.Actions.SequenceEqual(new[] {"GetCurrent"}), "retired owner repopulated or started follow-up");
        }
        finally
        {
            home.Dispose();
            release.TrySetResult(Fail("AuthenticationRequired"));
            await original;
            await (close ?? owner.DisposeAsync().AsTask());
        }
    }),
    ("confirmed sign-out retains existing broker task across its own private owner reset", async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        AccountSettingsFeature? owner = null;
        BrowserHomeContext? home = null;
        var service = ReadyAsync(async action =>
        {
            if (action != "SignOut") return null;
            home!.Dispose();
            await owner!.DisposeAsync();
            entered.TrySetResult();
            return await release.Task;
        });
        owner = Feature(service);
        home = new BrowserHomeContext(document, _ => {}, owner);
        Task? mutation = null;
        try
        {
            await home.ActivateAsync();
            await home.DispatchAsync("AccountRequestSignOut", null);
            Check(Value(home, "HasAccountConfirmation") is true && !service.Actions.Contains("SignOut"), "sign-out dispatched without confirmation");
            mutation = home.DispatchAsync("AccountConfirmSignOut", null).AsTask();
            await entered.Task;
            Check(!mutation.IsCompleted && owner.HasOutstandingBrokerWork && Value(home, "AccountAvailable") is false, "broker task lost or stale identity kept");
            release.TrySetResult(JsonSerializer.SerializeToElement(new { ok = true, status = 204, body = (object?)null }));
            await mutation;
            Check(!owner.HasOutstandingBrokerWork, "settled broker task still pending");
        }
        finally
        {
            home.Dispose();
            release.TrySetResult(Fail("Cancelled"));
            if (mutation is not null) await mutation;
            await owner.DisposeAsync();
        }
    }),
    ("registered device-local shortcuts navigate to their exact owner routes without Home grants", async () =>
    {
        var service = new Service((_, _) => Task.FromResult(Fail("AuthenticationRequired")));
        await using var owner = Feature(service);
        var targets = new List<HomeFeatureNavigationRequest>();
        var registered = new HashSet<string>(StringComparer.Ordinal) { "app.write", "app.boards" };
        using var home = new BrowserHomeContext(document, targets.Add, owner, registered.Contains);
        await home.ActivateAsync();
        Check(home.IsActionAvailable("OpenWrite") is true && home.IsActionAvailable("OpenBoards") is true, "registered local owner shortcut unavailable");
        Check(home.IsActionAvailable("OpenBrowse") is false && home.IsActionAvailable("OpenData") is false, "unregistered owner shortcut enabled");
        await home.DispatchAsync("OpenWrite", null);
        await home.DispatchAsync("OpenBoards", null);
        Check(targets.Select(target => target.RouteId).SequenceEqual(new[] { "app.write", "app.boards" }), "shortcut changed owner route");
        Check(targets.All(target => target.EntityId is null && target.EntityType is null && target.Action is null), "shortcut fabricated an artifact or operation");
        Check(service.Actions.SequenceEqual(new[] { "GetCurrent" }), "local navigation performed account work");
        AssertUnavailable(home);
    }),
    ("a removed owner route is rechecked before shortcut dispatch", async () =>
    {
        var registered = new HashSet<string>(StringComparer.Ordinal) { "app.write" };
        var targets = new List<HomeFeatureNavigationRequest>();
        using var home = new BrowserHomeContext(document, targets.Add, isRegisteredRoute: registered.Contains);
        Check(home.IsActionAvailable("OpenWrite") is true, "initial registered route missing");
        registered.Clear();
        await home.DispatchAsync("OpenWrite", null);
        Check(home.IsActionAvailable("OpenWrite") is false && targets.Count == 0, "removed owner route dispatched");
        AssertUnavailable(home);
    }),
    ("missing route observation never enables or dispatches a feature shortcut", async () =>
    {
        var targets = new List<HomeFeatureNavigationRequest>();
        using var home = new BrowserHomeContext(document, targets.Add);
        foreach (var command in new[] { "OpenStudio", "OpenWrite", "OpenBrowse", "OpenData", "OpenBoards", "NavigateApps", "NavigateDiscover", "NavigateMesh", "NavigatePermissions", "NavigateNotifications", "NavigateSpaces", "NavigateAutomations" })
        {
            Check(home.IsActionAvailable(command) is false, "missing registration enabled " + command);
            await home.DispatchAsync(command, null);
        }
        Check(targets.Count == 0, "missing registration navigated");
        AssertUnavailable(home);
    }),
    ("retired Home refuses shortcut observation and navigation", async () =>
    {
        var observed = 0;
        var targets = new List<HomeFeatureNavigationRequest>();
        var home = new BrowserHomeContext(document, targets.Add, isRegisteredRoute: _ => { observed++; return true; });
        home.Dispose();
        Check(home.IsActionAvailable("OpenWrite") is false, "retired shortcut enabled");
        await home.DispatchAsync("OpenWrite", null);
        Check(observed == 0 && targets.Count == 0, "retired Home consulted or dispatched an owner");
    }),
};
var failures = 0;
foreach (var (name, run) in cases)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error); }
}
Console.WriteLine($"Discovered={cases.Length} Passed={cases.Length-failures} Failed={failures} Skipped=0 Scope=MANAGED PRESENTATION scripted wire replies, no issuer or native acceptance");
Environment.ExitCode = failures == 0 ? 0 : 1;
static object? Value(ICuiBindingContext context, string path) { context.TryGetValue(path, out var value); return value; }
static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void AssertUnavailable(BrowserHomeContext home)
{
    foreach (var field in new[] {"LibraryAvailable", "ActivityAvailable", "ModelAvailable", "PackageAvailable"})
        Check(Value(home, field) is false, field + " inferred from authentication");
}
static JsonElement Ok(object body) => JsonSerializer.SerializeToElement(new { ok = true, status = 200, body });
static JsonElement Fail(string code, int? status = null) => JsonSerializer.SerializeToElement(new { ok = false, status, error = new { code } });
static object Profile(string id) => new { accountId = id, name = "Actual account", username = "actual", icon = (string?)null, pronouns = (string?)null, job = (string?)null, revision = 1 };
static Service Ready(Func<string, JsonElement?>? custom = null) => ReadyAsync(action => Task.FromResult(custom?.Invoke(action)));
static Service ReadyAsync(Func<string, Task<JsonElement?>> custom) => new(async (action, _) =>
{
    if (await custom(action) is { } reply) return reply;
    return action switch
    {
        "GetCurrent" => Ok(new { accountId = "12345678-1234-4234-8234-123456789abc", displayName = "Actual account" }),
        "GetProfile" => Ok(new { profile = Profile("12345678-1234-4234-8234-123456789abc") }),
        "ListSessions" => Ok(new { sessions = Array.Empty<object>() }),
        _ => Fail("ServiceUnavailable"),
    };
});
sealed class Service(Func<string, CancellationToken, Task<JsonElement>> invoke) : IAccountBrowserTransport
{
    public List<string> Actions { get; } = [];
    public Task<JsonElement> InvokeAsync(string action, JsonElement? arguments, CancellationToken token)
    { Actions.Add(action); return invoke(action, token); }
}
// Console-only test host: production Program/JSImport is not called by these managed controls.
namespace NineToOne.Web { internal static class Program { public static void ShowStatus(string code, string message) {} } }
