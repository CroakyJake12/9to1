using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using HavenOS.Home;
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
        var registered = new HashSet<string>(StringComparer.Ordinal) { "app.write", "app.present", "app.picture", "app.wave", "app.boards" };
        using var home = new BrowserHomeContext(document, targets.Add, owner, registered.Contains);
        await home.ActivateAsync();
        Check(home.IsActionAvailable("OpenWrite") is true && home.IsActionAvailable("OpenBoards") is true, "registered local owner shortcut unavailable");
        Check(home.IsActionAvailable("OpenBrowse") is false && home.IsActionAvailable("OpenData") is false, "unregistered owner shortcut enabled");
        await home.DispatchAsync("OpenWrite", null);
        await home.DispatchAsync("OpenPresent", null);
        await home.DispatchAsync("OpenPicture", null);
        await home.DispatchAsync("OpenWave", null);
        await home.DispatchAsync("OpenBoards", null);
        Check(targets.Select(target => target.RouteId).SequenceEqual(new[] { "app.write", "app.present", "app.picture", "app.wave", "app.boards" }), "shortcut changed owner route");
        Check(targets.All(target => target.EntityId is null && target.EntityType is null && target.Action is null), "shortcut fabricated an artifact or operation");
        Check(service.Actions.SequenceEqual(new[] { "GetCurrent" }), "local navigation performed account work");
        AssertUnavailable(home);
    }),
    ("a removed owner route is rechecked before shortcut dispatch", async () =>
    {
        var registered = new HashSet<string>(StringComparer.Ordinal) { "app.picture", "app.wave" };
        var targets = new List<HomeFeatureNavigationRequest>();
        using var home = new BrowserHomeContext(document, targets.Add, isRegisteredRoute: registered.Contains);
        Check(home.IsActionAvailable("OpenPicture") is true && home.IsActionAvailable("OpenWave") is true, "initial registered route missing");
        registered.Clear();
        await home.DispatchAsync("OpenPicture", null);
        await home.DispatchAsync("OpenWave", null);
        Check(home.IsActionAvailable("OpenPicture") is false && home.IsActionAvailable("OpenWave") is false && targets.Count == 0, "removed owner route dispatched");
        AssertUnavailable(home);
    }),
    ("missing route observation never enables or dispatches a feature shortcut", async () =>
    {
        var targets = new List<HomeFeatureNavigationRequest>();
        using var home = new BrowserHomeContext(document, targets.Add);
        foreach (var command in new[] { "OpenStudio", "OpenWrite", "OpenPresent", "OpenPicture", "OpenWave", "OpenBrowse", "OpenData", "OpenBoards", "NavigateApps", "NavigateDiscover", "NavigateMesh", "NavigatePermissions", "NavigateNotifications", "NavigateSpaces", "NavigateAutomations" })
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
// Additive controls use the actual receiving owner/codec/UI driver with explicit scripted
// actor/storage fixtures. They do not issue account identity or claim real IndexedDB acceptance.
cases = cases.Concat(new (string Name, Func<Task> Run)[]
{
    ("Home principal workflow saves flags and selected tile then a fresh view reopens SAME profile", async () =>
    {
        var wire = new HomeWire(); var actor = new HomeActor();
        await using var layouts = new BrowserHomeDashboardLayoutStore(() => {}, actor, wire);
        await using var account = Feature(Ready());
        using (var first = new BrowserHomeContext(document, _ => {}, account, layoutOwner: layouts))
        {
            await first.ActivateAsync();
            Check(Value(first, "DashboardLayoutAvailable") is true, "verified profile layout unavailable");
            await first.DispatchAsync("AddDashboardTile", null);
            Check(first.TrySetValue("SelectedDashboardTileIndex", 0), "actual saved selection refused");
            await first.DispatchAsync("PinSelectedDashboardTile", null);
            await first.DispatchAsync("LockSelectedDashboardTile", null);
            await first.DispatchAsync("HideSelectedDashboardTile", null);
            await first.DispatchAsync("ResizeSelectedDashboardTileWide", null);
            await first.DispatchAsync("EnableAIGeneratedTiles", null);
            Check(Value(first, "AllowAIGeneratedTiles") is true, "saved preference missing");
            Check(first.IsActionAvailable("InstallAllUpdates") is false && Value(first, "ModelAvailable") is false, "layout granted provider authority");
        }
        using var reopened = new BrowserHomeContext(document, _ => {}, account, layoutOwner: layouts);
        await reopened.ActivateAsync();
        var saved = wire.Layout!;
        Check(saved.Tiles.Count == 1 && saved.Tiles[0].Pinned && saved.Tiles[0].Locked && saved.Tiles[0].Visibility == HomeTileVisibility.Hidden &&
            saved.Tiles[0].Size == HomeTileSize.Wide && saved.AllowAiGeneratedTiles, "fresh view did not reopen full saved fields");
        Check(Value(reopened, "DashboardLayoutAvailable") is true && actor.Reads > wire.Invocations, "profile was not checked before/after storage");
    }),
    ("SAME profile concurrent CAS returns only verified conflict and retains original proposal", async () =>
    {
        var wire = new HomeWire();
        await using var owner = new BrowserHomeDashboardLayoutStore(() => {}, new HomeActor(), wire);
        using var first = owner.CreateBinding(); using var second = owner.CreateBinding();
        await first.LoadAsync(default); await second.LoadAsync(default);
        var layout = new HomeDashboardLayout(1, 1, [], true, false);
        Check(await first.TrySaveAsync(0, layout, default), "actual first save refused");
        Check(!await second.TrySaveAsync(0, layout with { AllowAiReorder = true }, default) && wire.Conflicts.Count == 1,
            "stale CAS became saved or lost proposal");
        Check((await second.LoadAsync(default))?.AllowAiReorder is false, "conflict replaced winner");
    }),
    ("lawful null actor acquires no import or storage and preserves exact prestorage disposition", async () =>
    {
        var wire = new HomeWire(); var actor = new HomeActor { Current = null };
        var owner = new BrowserHomeDashboardLayoutStore(() => {}, actor, wire);
        using var binding = owner.CreateBinding();
        var original = binding.LoadAsync(default);
        var failure = await HomeControl.Fault(original);
        Check(HomeControl.Contains<HomeDashboardNoCurrentActorException>(failure) && wire.Initializations == 0 && wire.Invocations == 0, "null actor reached storage");
        await owner.DisposeAsync();
    }),
    ("genuine caller cancellation before source dispatch keeps actual Task canceled and touches no storage", async () =>
    {
        var wire = new HomeWire(); var actor = new HomeActor();
        var owner = new BrowserHomeDashboardLayoutStore(() => {}, actor, wire);
        using var binding = owner.CreateBinding(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var original = binding.LoadAsync(cancellation.Token); await HomeControl.Fault(original);
        Check(original.IsCanceled && actor.Reads == 0 && wire.Initializations == 0, "actual canceled prestorage source fabricated work");
        await owner.DisposeAsync();
    }),
    ("old view refuses a newly authenticated profile BEFORE its actual save invocation", async () =>
    {
        var wire = new HomeWire(); var actor = new HomeActor();
        var owner = new BrowserHomeDashboardLayoutStore(() => {}, actor, wire);
        using var binding = owner.CreateBinding(); await binding.LoadAsync(default);
        var count = wire.Invocations;
        actor.Current = actor.Current! with { AuthenticationRevision = "different-verified-session" };
        await HomeControl.Fault(binding.TrySaveAsync(0, new(1, 1, [], false, false), default));
        Check(wire.Invocations == count && wire.Layout is null, "stale view saved into replacement session");
        await HomeControl.Fault(owner.DisposeAsync().AsTask());
    }),
    ("actor change AFTER actual commit retains SAME decoded result/raw receipt and unknown outcome", async () =>
    {
        var wire = new HomeWire(); var actor = new HomeActor();
        var owner = new BrowserHomeDashboardLayoutStore(() => {}, actor, wire);
        using var binding = owner.CreateBinding(); await binding.LoadAsync(default);
        wire.AfterReply = action => { if (action == "Save") actor.Current = actor.Current! with { AuthenticationRevision = "changed-after-commit" }; };
        var original = binding.TrySaveAsync(0, new(1, 1, [], true, false), default);
        var failure = await HomeControl.Fault(original);
        Check(HomeControl.Contains<HomeDashboardCommitOutcomeUnknownException>(failure) && wire.Layout?.AllowAiGeneratedTiles == true &&
            owner.Originals.Any(row => ReferenceEquals(row.Outer, original) && row.RawReply == wire.LastReply && row.Decoded is HomeDashboardLayout && row.Proposal is not null),
            "post-commit actor failure lost committed original evidence");
        await HomeControl.Fault(owner.DisposeAsync().AsTask());
    }),
    ("malformed dispatched save/reported committed refusal is unknown and preserves full reply", async () =>
    {
        foreach (var reply in new[] { "{", "{\"ok\":false,\"committed\":true,\"error\":{\"code\":\"Refused\"}}" })
        {
            var wire = new HomeWire(); var owner = new BrowserHomeDashboardLayoutStore(() => {}, new HomeActor(), wire);
            using var binding = owner.CreateBinding(); await binding.LoadAsync(default);
            wire.ReplyOverride = action => action == "Save" ? reply : null;
            var original = binding.TrySaveAsync(0, new(1, 1, [], false, false), default);
            var failure = await HomeControl.Fault(original);
            Check(HomeControl.Contains<HomeDashboardCommitOutcomeUnknownException>(failure) && owner.Originals.Any(row =>
                ReferenceEquals(row.Outer, original) && row.StorageDispatched && row.RawReply == reply && row.Proposal is not null), "malformed reply became no-effect");
            await HomeControl.Fault(owner.DisposeAsync().AsTask());
        }
    }),
    ("held SAME raw read remains pending through revocation and independent actual/close joins", async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wire = new HomeWire { HeldReply = (_, _) => { entered.TrySetResult(); return release.Task; } };
        var owner = new BrowserHomeDashboardLayoutStore(() => {}, new HomeActor(), wire);
        using var binding = owner.CreateBinding(); var original = binding.LoadAsync(default); Task? close = null;
        Exception? assertion = null; var cleanup = new List<Exception>();
        try
        {
            await entered.Task; owner.RevokePrivateContext(); close = owner.DisposeAsync().AsTask();
            Check(!original.IsCompleted && !close.IsCompleted && owner.IsRevoked, "signal replaced actual source settlement");
        }
        catch (Exception cause) { assertion = cause; }
        finally
        {
            // Release the SAME admitted raw source even when the negative assertion fails.
            release.TrySetResult(HomeWire.ReadReply(HomeActor.Original.ProfileId, null));
            try { await original; } catch (Exception cause) { cleanup.Add(cause); }
            try { await (close ?? owner.DisposeAsync().AsTask()); } catch (Exception cause) { cleanup.Add(cause); }
        }
        if (assertion is not null) throw new AggregateException("Original held-control assertion and independent cleanup.", new[] { assertion }.Concat(cleanup));
        Check(original.IsFaulted && close is { IsFaulted: true } && wire.CloseJoins == 1, "revoked read published or actual transport close not joined");
    }),
    ("original faulted OCE/raw sibling/stop/close causes all survive independent close", async () =>
    {
        var rawCause = new IOException("actual raw sibling"); var oce = new OperationCanceledException("faulted OCE");
        var stop = new IOException("actual stop"); var closing = new IOException("actual close");
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wire = new HomeWire { HeldReply = (_, _) => { entered.TrySetResult(); return release.Task; }, CancelFailure = stop, CloseFailure = closing };
        var owner = new BrowserHomeDashboardLayoutStore(() => {}, new HomeActor(), wire);
        using var binding = owner.CreateBinding(); using var caller = new CancellationTokenSource();
        var original = binding.LoadAsync(caller.Token); await entered.Task; caller.Cancel();
        release.TrySetException(new Exception[] { oce, rawCause });
        var failure = await HomeControl.Fault(original); var closed = await HomeControl.Fault(owner.DisposeAsync().AsTask());
        Check(original.IsFaulted && HomeControl.Has(failure, oce) && HomeControl.Has(failure, rawCause) && HomeControl.Has(failure, stop) &&
            HomeControl.Has(closed, oce) && HomeControl.Has(closed, rawCause) && HomeControl.Has(closed, stop) && HomeControl.Has(closed, closing), "actual fault/cleanup siblings were replaced or omitted");
    }),
    ("actual actor callback physical/logical own-close refuses BEFORE revocation", async () =>
    {
        var wire = new HomeWire(); BrowserHomeDashboardLayoutStore? owner = null; var refused = false;
        var actor = new HomeActor { Observe = () =>
        {
            try { _ = owner!.DisposeAsync(); }
            catch (InvalidOperationException) { refused = true; }
        } };
        owner = new BrowserHomeDashboardLayoutStore(() => {}, actor, wire);
        using var binding = owner.CreateBinding(); await binding.LoadAsync(default);
        Check(refused && !owner.IsRevoked && wire.CloseJoins == 0, "actor callback self-joined or revoked parent");
        await owner.DisposeAsync();
    }),
    ("corrupt full stored JSON/hash refuses read and preserves actual original row/cause", async () =>
    {
        var wire = new HomeWire { ReplyOverride = _ => JsonSerializer.Serialize(new { ok = true, committed = false, outcome = "Read",
            profile = HomeActor.Original.ProfileId, record = new { schema = 1, profile = HomeActor.Original.ProfileId, revision = "1", json = "{", hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{"))).ToLowerInvariant() } }) };
        var owner = new BrowserHomeDashboardLayoutStore(() => {}, new HomeActor(), wire);
        using var binding = owner.CreateBinding(); var original = binding.LoadAsync(default);
        var failure = await HomeControl.Fault(original);
        Check(original.IsFaulted && HomeControl.Contains<JsonException>(failure) && owner.Originals.Any(row => row.RawReply == wire.LastReply) && wire.Layout is null, "corrupt row became default saved layout or lost actual parse cause");
        await HomeControl.Fault(owner.DisposeAsync().AsTask());
    }),
    ("held original publication keeps actual exposed Task pending; revocation faults it with full decoded result retained", async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wire = new HomeWire { Layout = new(1, 7, [], true, false) };
        var owner = new BrowserHomeDashboardLayoutStore(() => {}, new HomeActor(), wire, () => { entered.TrySetResult(); return release.Task; });
        using var binding = owner.CreateBinding(); var original = binding.LoadAsync(default); Task? close = null;
        Exception? assertion = null; var cleanup = new List<Exception>();
        try
        {
            await entered.Task;
            Check(!original.IsCompleted && owner.Originals.Any(row => ReferenceEquals(row.Outer, original) && row.Driver.IsCompletedSuccessfully &&
                row.Decoded is HomeDashboardLayout { Revision: 7 } && row.Sources.Any(actual => ReferenceEquals(actual, release.Task))), "actual exposed read completed before retained publication source");
            owner.RevokePrivateContext(); close = owner.DisposeAsync().AsTask();
            Check(!original.IsCompleted && !close.IsCompleted, "retirement replaced same pending publication source");
        }
        catch (Exception cause) { assertion = cause; }
        finally
        {
            release.TrySetResult();
            try { await original; } catch (Exception cause) { cleanup.Add(cause); }
            try { await (close ?? owner.DisposeAsync().AsTask()); } catch (Exception cause) { cleanup.Add(cause); }
        }
        if (assertion is not null) throw new AggregateException("Original publication assertion plus independent joins.", new[] { assertion }.Concat(cleanup));
        Check(original.IsFaulted && close is { IsFaulted: true } && binding.Actor is null && owner.Originals.Any(row =>
            ReferenceEquals(row.Outer, original) && row.Decoded is HomeDashboardLayout { Revision: 7 }), "revoked outer publication lost original canonical layout or published actor");
    }),
    ("logical view callback after await cannot join its SAME owning close", async () =>
    {
        var wire = new HomeWire(); var owner = new BrowserHomeDashboardLayoutStore(() => {}, new HomeActor(), wire);
        var refused = false;
        await owner.RunViewAsync(async () =>
        {
            await Task.Yield();
            try { _ = owner.DisposeAsync(); } catch (InvalidOperationException) { refused = true; }
        });
        Check(refused && !owner.IsRevoked, "logical callback joined/retired its own source");
        await owner.DisposeAsync();
    }),
}).ToArray();
var failures = 0;
// Present is a genuine separately registered device-local owner. Opening it never
// requires an account grant; retirement and missing registration still refuse.
cases = cases.Concat(new (string Name, Func<Task> Run)[]
{
    ("registered local Present shortcut dispatches its canonical route without account authority", async () =>
    {
        var targets = new List<HomeFeatureNavigationRequest>();
        var registered = true;
        using var home = new BrowserHomeContext(document, targets.Add,
            isRegisteredRoute: route => registered && route == "app.present");
        Check(Value(home, "AccountAvailable") is false, "anonymous local navigation acquired an account");
        Check(home.IsActionAvailable("OpenPresent") is true, "actual registered Present shortcut unavailable");
        await home.DispatchAsync("OpenPresent", null);
        Check(targets.Count == 1 && targets[0].RouteId == "app.present" && targets[0].EntityId is null &&
            targets[0].ModelPickerTarget is null, "Present shortcut changed destination or invented an artifact/provider");
        registered = false;
        Check(home.IsActionAvailable("OpenPresent") is false, "removed Present owner still enabled");
        await home.DispatchAsync("OpenPresent", null);
        Check(targets.Count == 1, "removed Present owner still dispatched");
        home.Dispose();
        Check(home.IsActionAvailable("OpenPresent") is false, "retired Present shortcut enabled");
        await home.DispatchAsync("OpenPresent", null);
        Check(targets.Count == 1, "retired Home dispatched Present");
    }),
}).ToArray();
foreach (var (name, run) in cases)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error); }
}
Console.WriteLine($"Discovered={cases.Length} Passed={cases.Length-failures} Failed={failures} Skipped=0 Scope=MANAGED PRESENTATION AND HOME OWNER scripted actor/wire replies, no issuer/IndexedDB/browser/native acceptance");
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

// Scripted wire fixture only: production uses the imported actual Home IDB module and52E host.
sealed class HomeActor : IAuthenticatedResourceActorSource
{
    internal static readonly AuthenticatedResourceActor Original = new("cake-task:" + new string('a', 64) + ":12345678-1234-4234-8234-123456789abc",
        "cake-account-profile:" + new string('a', 64) + ":12345678-1234-4234-8234-123456789abc", Guid.Parse("12345678-1234-4234-8234-123456789abc"), null, "verified-fixture-session");
    internal AuthenticatedResourceActor? Current = Original; internal Action? Observe; internal int Reads;
    public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { Reads++; Observe?.Invoke(); token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Current); }
}
sealed class HomeWire : IHomeDashboardBrowserTransport
{
    internal HomeDashboardLayout? Layout; internal int Initializations, Invocations, CloseJoins;
    internal string? LastReply; internal readonly List<string> Conflicts = [];
    internal Action<string>? AfterReply; internal Func<string, string?>? ReplyOverride;
    internal Func<string, string, Task<string>>? HeldReply;
    internal Exception? CancelFailure, CloseFailure;
    public bool HasOriginalOwner => Initializations > 0;
    public IReadOnlyList<Task> OriginalInitializationTasks => [];
    public Task InitializeAsync(Action demand) { demand(); Initializations++; return Task.CompletedTask; }
    public Task<string> InvokeAsync(string id, string action, string arguments, bool cancelled)
    {
        Invocations++;
        if (HeldReply is { } held) return held(action, arguments);
        using var args = JsonDocument.Parse(arguments); var profile = args.RootElement.GetProperty("profile").GetString()!;
        var custom = ReplyOverride?.Invoke(action); string reply;
        if (custom is not null) reply = custom;
        else if (action == "Save")
        {
            var proposal = args.RootElement.GetProperty("json").GetString()!;
            var expected = long.Parse(args.RootElement.GetProperty("expected").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var conflict = (Layout?.Revision ?? 0) != expected;
            if (conflict) Conflicts.Add(proposal); else Layout = JsonSerializer.Deserialize<HomeDashboardLayout>(proposal)!;
            reply = JsonSerializer.Serialize(new { ok = true, committed = !conflict, outcome = conflict ? "Conflict" : "Saved", profile, record = Row(profile, Layout) });
        }
        else reply = ReadReply(profile, Layout);
        LastReply = reply; AfterReply?.Invoke(action); return Task.FromResult(reply);
    }
    internal static string ReadReply(string profile, HomeDashboardLayout? layout) => JsonSerializer.Serialize(new { ok = true, committed = false, outcome = "Read", profile, record = Row(profile, layout) });
    private static object? Row(string profile, HomeDashboardLayout? layout)
    {
        if (layout is null) return null; var json = JsonSerializer.Serialize(layout);
        return new { schema = 1, profile, revision = layout.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), json,
            hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant() };
    }
    public void Cancel(string id) { if (CancelFailure is { } cause) throw cause; }
    public void PrepareClose() {}
    public Task JoinCloseAsync() { CloseJoins++; return CloseFailure is { } cause ? Task.FromException(cause) : Task.CompletedTask; }
}
static class HomeControl
{
    internal static async Task<Exception> Fault(Task actual)
    {
        try { await actual; } catch (Exception cause) { return actual.Exception ?? cause; }
        throw new InvalidOperationException("The SAME expected fault/canceled original completed successfully.");
    }
    internal static bool Has(Exception source, Exception expected) => ReferenceEquals(source, expected) ||
        (source is AggregateException group && group.InnerExceptions.Any(cause => Has(cause, expected))) ||
        (source.InnerException is { } inner && Has(inner, expected));
    internal static bool Contains<T>(Exception source) where T : Exception => source is T ||
        (source is AggregateException group && group.InnerExceptions.Any(Contains<T>)) ||
        (source.InnerException is { } inner && Contains<T>(inner));
}
