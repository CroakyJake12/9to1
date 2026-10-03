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
await RunAsync("B1-LIFETIME-01-private-reset-retains-device-local-owner", async () =>
{
    var registry = new BrowserSurfaceRegistry();
    var privateDisposals = 0; var localDisposals = 0; var localCalls = 0;
    registry.Register(new DisposableHandler("app.files", request => Task.FromResult(Success(request)), () => privateDisposals++), _ => FixtureSurface());
    registry.Register(new DisposableHandler("app.wave", request => { localCalls++; return Task.FromResult(Success(request)); }, () => localDisposals++), _ => FixtureSurface(), BrowserSurfaceScope.DeviceLocal);
    registry.ClearPrivateContext();
    var blocked = await registry.OpenAsync(new("app.files"), default);
    var local = await registry.OpenAsync(new("app.wave"), default);
    Check(privateDisposals == 1 && localDisposals == 0 && localCalls == 1 && registry.AvailableRoutes.Count == 1);
    Check(!blocked.Result.Succeeded && blocked.Surface is null && local.Result.Succeeded && local.Surface is not null);
});
await RunAsync("B1-LIFETIME-02-reset-invalidates-pending-local-presentation", async () =>
{
    var registry = new BrowserSurfaceRegistry();
    var pending = new TaskCompletionSource<HomeFeatureNavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var renders = 0; var disposals = 0;
    registry.Register(new DisposableHandler("app.wave", _ => pending.Task, () => disposals++), _ => { renders++; return FixtureSurface(); }, BrowserSurfaceScope.DeviceLocal);
    var request = new HomeFeatureNavigationRequest("app.wave");
    var opening = registry.OpenAsync(request, default);
    registry.ClearPrivateContext();
    pending.SetResult(Success(request));
    var stale = await opening;
    Check(stale.Result.Code == "PermissionDenied" && stale.Surface is null && renders == 0 && disposals == 0);
    Check((await registry.OpenAsync(request, default)).Result.Succeeded && renders == 1);
});
await Run("B1-LIFETIME-03-close-disposes-every-owner-once", () =>
{
    var registry = new BrowserSurfaceRegistry();
    var privateDisposals = 0; var localDisposals = 0;
    registry.Register(new DisposableHandler("app.files", request => Task.FromResult(Success(request)), () => privateDisposals++), _ => FixtureSurface());
    registry.Register(new DisposableHandler("app.wave", request => Task.FromResult(Success(request)), () => localDisposals++), _ => FixtureSurface(), BrowserSurfaceScope.DeviceLocal);
    registry.Clear(); registry.Clear();
    Check(privateDisposals == 1 && localDisposals == 1 && registry.AvailableRoutes.Count == 0);
});
await Run("B1-LIFETIME-04-failing-disposal-still-removes-and-disposes-all", () =>
{
    var registry = new BrowserSurfaceRegistry(); var otherDisposals = 0;
    registry.Register(new DisposableHandler("app.files", request => Task.FromResult(Success(request)), () => throw new InvalidOperationException("UNIT teardown fault")), _ => FixtureSurface());
    registry.Register(new DisposableHandler("app.write", request => Task.FromResult(Success(request)), () => otherDisposals++), _ => FixtureSurface());
    try { registry.Clear(); throw new Exception("Owner failure was suppressed."); }
    catch (AggregateException error) { Check(error.InnerExceptions.Count == 1 && error.InnerExceptions[0].Message == "UNIT teardown fault"); }
    Check(otherDisposals == 1 && registry.AvailableRoutes.Count == 0);
});
await RunAsync("B1-LIFETIME-05-failed-save-preserves-owner-and-draft", async () =>
{
    var registry = new BrowserSurfaceRegistry(); var disposed = 0;
    var owner = new ClosingHandler("app.write", _ => Task.FromResult(new HomeCoreOperationResult<bool>(false, "CommitOutcomeUnknown", "Draft preserved.")), () => { disposed++; return ValueTask.CompletedTask; });
    registry.Register(owner, _ => FixtureSurface(), BrowserSurfaceScope.DeviceLocal);
    var closed = await registry.ClearAsync();
    Check(!closed.Succeeded && closed.Code == "CommitOutcomeUnknown" && disposed == 0 && registry.HasUnsavedChanges);
    Check(registry.AvailableRoutes.Contains("app.write") && (await registry.OpenAsync(new("app.write"), default)).Result.Succeeded);
});
await RunAsync("B1-LIFETIME-06-awaits-preparation-and-disposal-before-success", async () =>
{
    var registry = new BrowserSurfaceRegistry(); var disposalStarted = false;
    var prepared = new TaskCompletionSource<HomeCoreOperationResult<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    registry.Register(new ClosingHandler("app.write", _ => prepared.Task, () => { disposalStarted = true; return new(disposed.Task); }), _ => FixtureSurface(), BrowserSurfaceScope.DeviceLocal);
    var close = registry.ClearAsync();
    Check(!close.IsCompleted && !disposalStarted && registry.AvailableRoutes.Contains("app.write"));
    Check((await registry.OpenAsync(new("app.write"), default)).Result.Code == "BrowserClosing");
    Check(!registry.Register(new Handler("app.wave", r => Task.FromResult(Success(r))), _ => FixtureSurface()).Succeeded);
    prepared.SetResult(new(true, "Succeeded", "Saved.", true));
    while (!disposalStarted) await Task.Yield();
    Check(!close.IsCompleted && registry.AvailableRoutes.Count == 0);
    disposed.SetResult();
    Check((await close).Succeeded);
});
await RunAsync("B1-LIFETIME-07-async-teardown-attempts-all-owners", async () =>
{
    var registry = new BrowserSurfaceRegistry(); var otherDisposals = 0;
    registry.Register(new ClosingHandler("app.write", _ => Task.FromResult(new HomeCoreOperationResult<bool>(true, "Succeeded", "Saved.", true)), () => throw new IOException("UNIT async cleanup fault")), _ => FixtureSurface(), BrowserSurfaceScope.DeviceLocal);
    registry.Register(new DisposableHandler("app.wave", r => Task.FromResult(Success(r)), () => otherDisposals++), _ => FixtureSurface(), BrowserSurfaceScope.DeviceLocal);
    try { await registry.ClearAsync(); throw new Exception("Asynchronous teardown failure was suppressed."); }
    catch (AggregateException error) { Check(error.InnerExceptions.Count == 1 && error.InnerExceptions[0].Message == "UNIT async cleanup fault"); }
    Check(otherDisposals == 1 && registry.AvailableRoutes.Count == 0);
});
await RunAsync("B1-LIFETIME-08-cancelled-preparation-retains-owner", async () =>
{
    var registry = new BrowserSurfaceRegistry(); var disposed = 0;
    using var cancellation = new CancellationTokenSource();
    registry.Register(new ClosingHandler("app.write", token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); throw new Exception("Cancelled close was accepted."); }, () => { disposed++; return ValueTask.CompletedTask; }), _ => FixtureSurface(), BrowserSurfaceScope.DeviceLocal);
    try { await registry.ClearAsync(cancellation.Token); throw new Exception("Cancelled close was accepted."); }
    catch (OperationCanceledException) { }
    Check(disposed == 0 && registry.HasUnsavedChanges && (await registry.OpenAsync(new("app.write"), default)).Result.Succeeded);
});
await Run("B1-LIFETIME-09-synchronous-clear-cannot-drop-async-draft", () =>
{
    var registry = new BrowserSurfaceRegistry(); var disposed = 0;
    registry.Register(new ClosingHandler("app.write", _ => throw new Exception("Synchronous clear attempted preparation."), () => { disposed++; return ValueTask.CompletedTask; }), _ => FixtureSurface(), BrowserSurfaceScope.DeviceLocal);
    try { registry.Clear(); throw new Exception("Synchronous clear dropped an asynchronous owner."); }
    catch (InvalidOperationException) { }
    Check(disposed == 0 && registry.AvailableRoutes.Contains("app.write") && registry.HasUnsavedChanges);
});
await RunAsync("B1-LIFETIME-10-pending-open-cannot-replace-view-during-close", async () =>
{
    var registry = new BrowserSurfaceRegistry(); var renders = 0; var disposals = 0;
    var opened = new TaskCompletionSource<HomeFeatureNavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var prepared = new TaskCompletionSource<HomeCoreOperationResult<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
    registry.Register(new ClosingHandler("app.write", _ => prepared.Task, () => { disposals++; return ValueTask.CompletedTask; }, _ => opened.Task),
        _ => { renders++; return FixtureSurface(); }, BrowserSurfaceScope.DeviceLocal);
    var request = new HomeFeatureNavigationRequest("app.write");
    var pendingOpen = registry.OpenAsync(request, default);
    var closing = registry.ClearAsync();
    opened.SetResult(Success(request));
    var late = await pendingOpen;
    Check(!late.Result.Succeeded && late.Result.Code == "BrowserClosing" && late.Surface is null && renders == 0);
    prepared.SetResult(new(false, "CommitOutcomeUnknown", "Draft retained.", false));
    Check(!(await closing).Succeeded && disposals == 0 && registry.HasUnsavedChanges);
    Check((await registry.OpenAsync(request, default)).Result.Succeeded && renders == 1);
});
await Run("B1-LIFETIME-11-unsupported-private-async-owner-and-sync-save-veto", () =>
{
    var registry = new BrowserSurfaceRegistry(); var disposed = 0;
    var asyncOwner = new ClosingHandler("app.write", _ => throw new Exception("Private async owner must not prepare."), () => { disposed++; return ValueTask.CompletedTask; });
    Check(!registry.Register(asyncOwner, _ => FixtureSurface()).Succeeded && registry.AvailableRoutes.Count == 0);
    var syncOwner = new PreparingDisposableHandler(() => disposed++);
    Check(!registry.Register(syncOwner, _ => FixtureSurface()).Succeeded && registry.AvailableRoutes.Count == 0);
    Check(registry.Register(syncOwner, _ => FixtureSurface(), BrowserSurfaceScope.DeviceLocal).Succeeded);
    try { registry.Clear(); throw new Exception("Synchronous clear bypassed the owner's save veto."); }
    catch (InvalidOperationException) { }
    Check(disposed == 0 && registry.HasUnsavedChanges && registry.AvailableRoutes.Contains("app.wave"));
});
await RunAsync("B1-ADMISSION-01-prepare-defers-render-until-current-view-is-ready", async () =>
{
    var registry = new BrowserSurfaceRegistry(); var renders = 0;
    registry.Register(new Handler("app.write", r => Task.FromResult(Success(r))), _ => { renders++; return FixtureSurface(); });
    var prepared = await registry.PreparePresentationAsync(new("app.write"), default);
    Check(prepared.Result.Succeeded && prepared.Present is not null && renders == 0);
    Check(prepared.Present!() is not null && renders == 1);
});
await RunAsync("B1-ADMISSION-02-cancel-or-context-reset-rejects-prepared-render", async () =>
{
    var registry = new BrowserSurfaceRegistry(); var renders = 0;
    using var cancellation = new CancellationTokenSource();
    registry.Register(new Handler("app.write", r => Task.FromResult(Success(r))), _ => { renders++; return FixtureSurface(); });
    var cancelled = await registry.PreparePresentationAsync(new("app.write"), cancellation.Token);
    var invalidated = await registry.PreparePresentationAsync(new("app.write"), default);
    cancellation.Cancel();
    Check(cancelled.Present!() is null && renders == 0);
    registry.ClearPrivateContext();
    Check(invalidated.Present!() is null && renders == 0);
});
await RunAsync("B1-ADMISSION-03-prepared-owner-denial-retains-authority-over-fallback", async () =>
{
    var registry = new BrowserSurfaceRegistry(); var fallbacks = 0;
    registry.Register(new Handler(HomeFeatureRouteIds.Dashboard, r => Task.FromResult(new HomeFeatureNavigationResult(false, "PermissionDenied", "Denied.", r))), _ => throw new Exception("Denied owner was rendered."));
    var prepared = await BrowserRouteDispatcher.PrepareAsync(registry, new(HomeFeatureRouteIds.Dashboard), _ => true,
        _ => { fallbacks++; return FixtureSurface(); }, default);
    Check(!prepared.Result.Succeeded && prepared.Result.Code == "PermissionDenied" && prepared.Present is null && fallbacks == 0);
});
await RunAsync("B1-ADMISSION-04-home-navigation-is-deferred-and-cancelled-without-replay", async () =>
{
    var fallbacks = 0;
    using var cancellation = new CancellationTokenSource();
    var prepared = await BrowserRouteDispatcher.PrepareAsync(new(), new(HomeFeatureRouteIds.Dashboard), _ => true,
        _ => { fallbacks++; return FixtureSurface(); }, cancellation.Token);
    Check(prepared.Result.Succeeded && prepared.IsUnavailableHome && fallbacks == 0);
    cancellation.Cancel();
    Check(prepared.Present!() is null && fallbacks == 0);
});
Console.WriteLine($"Discovered: 37; executed: {executed}; passed: {executed}; failed: 0. Backend/browser acceptance: NOT-RUN.");
return executed == 37 ? 0 : 1;

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
sealed class DisposableHandler(string routeId, Func<HomeFeatureNavigationRequest, Task<HomeFeatureNavigationResult>> open, Action dispose) : IHomeFeatureRouteHandler, IDisposable
{
    public string RouteId => routeId;
    public Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default) => open(request);
    public void Dispose() => dispose();
}
sealed class FixtureContext : ICuiBindingContext, ICuiActionDispatcher
{
    public bool TryGetValue(string path, out object? value) { value = null; return false; }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unit test must not execute an action.");
}
sealed class ClosingHandler(string routeId, Func<CancellationToken, Task<HomeCoreOperationResult<bool>>> prepare,
    Func<ValueTask> dispose, Func<HomeFeatureNavigationRequest, Task<HomeFeatureNavigationResult>>? open = null) : IHomeFeatureRouteHandler, IBrowserCloseParticipant, IAsyncDisposable
{
    public string RouteId => routeId;
    public bool HasUnsavedChanges => true;
    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default) => prepare(cancellationToken);
    public ValueTask DisposeAsync() => dispose();
    public Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default) =>
        open is not null ? open(request) : Task.FromResult(new HomeFeatureNavigationResult(true, "Succeeded", "Opened", request,
            ViewState: new(routeId, "unit-view", 17, JsonSerializer.SerializeToElement(new { }))));
}
sealed class PreparingDisposableHandler(Action dispose) : IHomeFeatureRouteHandler, IBrowserCloseParticipant, IDisposable
{
    public string RouteId => "app.wave";
    public bool HasUnsavedChanges => true;
    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new HomeCoreOperationResult<bool>(false, "StorageFailed", "Unsaved project retained.", false));
    public void Dispose() => dispose();
    public Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HomeFeatureNavigationResult(false, "UnsupportedFeature", "Lifecycle unit fixture only.", request));
}
