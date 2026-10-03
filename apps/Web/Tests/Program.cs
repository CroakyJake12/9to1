using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home.Core;
using NineToOne.Web;

// These isolated unit tests execute the production route/registration adapters and
// source-link the owning Home contracts. Handler fixtures are not backend acceptance.
var executed = 0;
await Run("B1-ROUTE-01-default-dashboard", () =>
{
    foreach (var fragment in new string?[] { null, "", "#", "#/" })
    {
        Check(BrowserRouteCodec.TryDecode(fragment, out var request, out var code));
        Check(request!.RouteId == HomeFeatureRouteIds.Dashboard && code is null);
    }
});
await Run("B1-ROUTE-02-opaque-identities", () =>
{
    var expected = new HomeFeatureNavigationRequest(HomeFeatureRouteIds.Library, "app.write", "ID / ? & = # + % ü 💡", "reveal selection", "cake://file/opaque?rev=17");
    Check(BrowserRouteCodec.TryDecode(BrowserRouteCodec.Encode(expected), out var observed, out _));
    Check(observed == expected);
});
await Run("B1-ROUTE-03-null-empty-distinct", () =>
{
    var expected = new HomeFeatureNavigationRequest("app.write", EntityType: "", EntityId: "", Action: null);
    Check(BrowserRouteCodec.TryDecode(BrowserRouteCodec.Encode(expected), out var observed, out _));
    Check(observed == expected && observed!.Action is null);
});
await Run("B1-ROUTE-04-unambiguous-fields", () =>
{
    foreach (var fragment in new[] { "#/app.write?entityId=a&entityId=b", "#/app.write?unknown=secret", "#/app.write?entityId", "#/app.write?", "#/app.write?entityId=x&&action=y" }) Reject(fragment);
});
await Run("B1-ROUTE-05-malformed-link-negative", () =>
{
    foreach (var fragment in new[] { "https://attacker.example", "#/../app.write", "#/app%2Ewrite", "#/app.write?entityId=%", "#/app.write?entityId=%Q0", "#/app.write?entityId=%00", "#/app.write?entityId=%0a" }) Reject(fragment);
});
await Run("B1-ROUTE-06-size-bounds", () =>
{
    Reject("#/" + new string('a', 129));
    Reject("#/app.write?entityId=" + new string('a', 8192));
    Throws(() => BrowserRouteCodec.Encode(new("app.write", EntityId: new string('a', 8192))));
});
await Run("B1-ROUTE-07-unspecified-picker-preserved-as-error", () =>
    Throws(() => BrowserRouteCodec.Encode(new(HomeFeatureRouteIds.ModelPicker, ModelPickerTarget: new("account", null, "Chat")))));
await Run("B1-ROUTE-08-control-character-encoding", () =>
    Throws(() => BrowserRouteCodec.Encode(new("app.write", EntityId: "first\nsecond"))));
await Run("B1-ROUTE-09-route-case-preserved", () =>
{
    var expected = new HomeFeatureNavigationRequest("app.Write", EntityId: "A" , Action: "Reveal");
    Check(BrowserRouteCodec.TryDecode(BrowserRouteCodec.Encode(expected), out var observed, out _));
    Check(observed == expected && observed!.RouteId != "app.write");
});
await Run("B1-ROUTE-10-action-remains-data", () =>
{
    Check(BrowserRouteCodec.TryDecode("#/app.write?action=Delete&entityId=opaque", out var observed, out _));
    Check(observed!.Action == "Delete" && observed.EntityId == "opaque");
    // Decode has no dispatcher, service or side-effect reference; its result is only the existing request.
});
await RunAsync("B1-REGISTRY-01-unavailable-no-render", async () =>
{
    var registry = new BrowserSurfaceRegistry();
    var (result, surface) = await registry.OpenAsync(new("app.write"), default);
    Check(!result.Succeeded && result.Code == "HomeServiceUnavailable" && surface is null);
});
await RunAsync("B1-REGISTRY-02-owner-result-required", async () =>
{
    var registry = new BrowserSurfaceRegistry();
    var rendered = 0;
    Check(registry.Register(new Handler("app.write", request => Task.FromResult(new HomeFeatureNavigationResult(false, "PermissionDenied", "Denied", request))), _ => { rendered++; return FixtureSurface(); }).Succeeded);
    var (result, surface) = await registry.OpenAsync(new("app.write", EntityId: "private"), default);
    Check(result.Code == "PermissionDenied" && surface is null && rendered == 0);
});
await RunAsync("B1-REGISTRY-03-mismatched-view-blocked", async () =>
{
    var registry = new BrowserSurfaceRegistry();
    var rendered = 0;
    registry.Register(new Handler("app.write", request => Task.FromResult(Success(request, "app.boards"))), _ => { rendered++; return FixtureSurface(); });
    var (result, surface) = await registry.OpenAsync(new("app.write"), default);
    Check(!result.Succeeded && result.Code == "HomeServiceIncompatible" && surface is null && rendered == 0);
});
await RunAsync("B1-REGISTRY-04-reset-rejects-late-private-view", async () =>
{
    var registry = new BrowserSurfaceRegistry();
    var pending = new TaskCompletionSource<HomeFeatureNavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var rendered = 0;
    registry.Register(new Handler("app.write", _ => pending.Task), _ => { rendered++; return FixtureSurface(); });
    var request = new HomeFeatureNavigationRequest("app.write", EntityId: "old-account-private");
    var operation = registry.OpenAsync(request, default);
    registry.Clear();
    pending.SetResult(Success(request));
    var (result, surface) = await operation;
    Check(!result.Succeeded && result.Code == "PermissionDenied" && rendered == 0 && surface is null && registry.AvailableRoutes.Count == 0);
});
await RunAsync("B1-REGISTRY-05-cancelled-no-render", async () =>
{
    var registry = new BrowserSurfaceRegistry();
    var rendered = 0;
    registry.Register(new Handler("app.write", request => Task.FromResult(Success(request))), _ => { rendered++; return FixtureSurface(); });
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try { await registry.OpenAsync(new("app.write"), cancellation.Token); throw new Exception("Cancelled operation was accepted."); }
    catch (OperationCanceledException) { }
    Check(rendered == 0);
});
await Run("B1-REGISTRY-06-duplicate-owner-not-overwritten", () =>
{
    var registry = new BrowserSurfaceRegistry();
    var handler = new Handler("app.write", request => Task.FromResult(Success(request)));
    Check(registry.Register(handler, _ => FixtureSurface()).Succeeded);
    var duplicate = registry.Register(handler, _ => FixtureSurface());
    Check(!duplicate.Succeeded && duplicate.Code == "HomeFeatureRouteConflict" && registry.AvailableRoutes.Count == 1);
});
await RunAsync("B1-DISPATCH-01-canonical-owner-before-fallback", async () =>
{
    foreach (var route in new[] { HomeFeatureRouteIds.Dashboard, HomeFeatureRouteIds.Library, HomeFeatureRouteIds.Events })
    {
        var registry = new BrowserSurfaceRegistry();
        var ownerCalls = 0;
        var fallbackCalls = 0;
        var request = new HomeFeatureNavigationRequest(route, EntityType: "file", EntityId: "opaque / artifact", Action: "Reveal", DeepLink: "cake://opaque");
        registry.Register(new Handler(route, target => { ownerCalls++; Check(target == request); return Task.FromResult(Success(target)); }), _ => FixtureSurface());
        var dispatch = await BrowserRouteDispatcher.OpenAsync(registry, request, _ => { fallbackCalls++; return FixtureSurface(); }, default);
        Check(ownerCalls == 1 && fallbackCalls == 0 && dispatch.Result.Succeeded && !dispatch.IsUnavailableHome && dispatch.Surface is not null);
    }
});
await RunAsync("B1-DISPATCH-02-owner-failures-never-fallback", async () =>
{
    foreach (var route in new[] { HomeFeatureRouteIds.Dashboard, HomeFeatureRouteIds.Library, HomeFeatureRouteIds.Events })
    foreach (var code in new[] { "PermissionDenied", "PermissionRequired", "HomeServiceUnavailable" })
    {
        var registry = new BrowserSurfaceRegistry();
        var ownerCalls = 0;
        var fallbackCalls = 0;
        var request = new HomeFeatureNavigationRequest(route, EntityId: "private");
        registry.Register(new Handler(route, target => { ownerCalls++; return Task.FromResult(new HomeFeatureNavigationResult(false, code, "Denied or unavailable", target)); }), _ => throw new Exception("Failed owner must not render."));
        var dispatch = await BrowserRouteDispatcher.OpenAsync(registry, request, _ => { fallbackCalls++; return FixtureSurface(); }, default);
        Check(ownerCalls == 1 && fallbackCalls == 0 && !dispatch.Result.Succeeded && dispatch.Result.Code == code && dispatch.Result.Request == request && dispatch.Surface is null && !dispatch.IsUnavailableHome);
    }
});
await RunAsync("B1-DISPATCH-03-absent-owner-unavailable-home", async () =>
{
    var registry = new BrowserSurfaceRegistry();
    var fallbackCalls = 0;
    var request = new HomeFeatureNavigationRequest(HomeFeatureRouteIds.Dashboard);
    var dispatch = await BrowserRouteDispatcher.OpenAsync(registry, request, _ => { fallbackCalls++; return FixtureSurface(); }, default);
    Check(fallbackCalls == 1 && dispatch.Result.Succeeded && dispatch.Result.Code == "HomeServiceUnavailable" && dispatch.IsUnavailableHome && dispatch.Surface is not null);
});
await RunAsync("B1-DISPATCH-04-owner-exception-never-fallback", async () =>
{
    var registry = new BrowserSurfaceRegistry();
    var fallbackCalls = 0;
    registry.Register(new Handler(HomeFeatureRouteIds.Dashboard, _ => throw new InvalidOperationException("UNIT owner failed")), _ => FixtureSurface());
    var request = new HomeFeatureNavigationRequest(HomeFeatureRouteIds.Dashboard);
    var dispatch = await BrowserRouteDispatcher.OpenAsync(registry, request, _ => { fallbackCalls++; return FixtureSurface(); }, default);
    // The actual Home owner host maps provider exceptions to HomeServiceUnavailable.
    Check(fallbackCalls == 0 && !dispatch.Result.Succeeded && dispatch.Result.Code == "HomeServiceUnavailable" && dispatch.Result.Request == request && dispatch.Surface is null && !dispatch.IsUnavailableHome);
});
await RunAsync("B1-DISPATCH-05-absent-nonhome-unavailable", async () =>
{
    var request = new HomeFeatureNavigationRequest("app.write", EntityId: "private");
    var dispatch = await BrowserRouteDispatcher.OpenAsync(new(), request, _ => null, default);
    Check(!dispatch.Result.Succeeded && dispatch.Result.Code == "HomeServiceUnavailable" && dispatch.Result.Request == request && dispatch.Surface is null && !dispatch.IsUnavailableHome);
});
await RunAsync("B1-DISPATCH-06-cancelled-home-no-fallback", async () =>
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var fallbackCalls = 0;
    try
    {
        await BrowserRouteDispatcher.OpenAsync(new(), new(HomeFeatureRouteIds.Dashboard), _ => { fallbackCalls++; return FixtureSurface(); }, cancellation.Token);
        throw new Exception("Cancelled Home fallback was accepted.");
    }
    catch (OperationCanceledException) { }
    Check(fallbackCalls == 0);
});
Console.WriteLine($"Discovered: 22; executed: {executed}; passed: {executed}; failed: 0. Backend/browser acceptance: NOT-RUN.");
return executed == 22 ? 0 : 1;

async Task Run(string id, Action test)
{
    test(); executed++; Console.WriteLine($"PASS {id}"); await Task.CompletedTask;
}
async Task RunAsync(string id, Func<Task> test)
{
    await test(); executed++; Console.WriteLine($"PASS {id}");
}
static void Check(bool condition) { if (!condition) throw new Exception("Required outcome was not observed."); }
static void Reject(string fragment)
{
    Check(!BrowserRouteCodec.TryDecode(fragment, out var request, out var code));
    Check(request is null && code == "BrowserRouteInvalid");
}
static void Throws(Action action)
{
    try { action(); } catch (ArgumentException) { return; }
    throw new Exception("Invalid request was accepted.");
}
static HomeFeatureNavigationResult Success(HomeFeatureNavigationRequest request, string? viewRoute = null) =>
    new(true, "Succeeded", "Opened", request, ViewState: new(viewRoute ?? request.RouteId, "unit-view", 17, JsonSerializer.SerializeToElement(new { })));
static BrowserCuiSurface FixtureSurface() => new(new CuiRichParser().Parse("<Page><Text>Fixture</Text></Page>"), new FixtureContext(), new FixtureContext());
sealed class Handler(string routeId, Func<HomeFeatureNavigationRequest, Task<HomeFeatureNavigationResult>> open) : IHomeFeatureRouteHandler
{
    public string RouteId => routeId;
    public Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default) => open(request);
}
sealed class FixtureContext : ICuiBindingContext, ICuiActionDispatcher
{
    public bool TryGetValue(string path, out object? value) { value = null; return false; }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unit test must not execute an action.");
}
