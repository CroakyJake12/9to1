using System.Text.Json;
using CakeOS.Cui.Language;
using HavenOS.Home.Core;
using NineToOne.Web.Accounts;
using NineToOne.Web.Services;
var parser = new CuiRichParser();
var cui = parser.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Accounts.cui")), "Accounts.cui");
AccountSettingsFeature Feature(Service service) => new(cui, service, action => { action(); return Task.CompletedTask; });
var cases = new (string, Func<Task>)[] {
    ("each unsupported target preserves original request and performs zero service calls", async () => {
        var requests = new[] { new HomeFeatureNavigationRequest(HomeFeatureRouteIds.Settings, EntityType: "account"), new(HomeFeatureRouteIds.Settings, EntityId: ""), new(HomeFeatureRouteIds.Settings, Action: "signout"), new(HomeFeatureRouteIds.Settings, DeepLink: "?private"), new(HomeFeatureRouteIds.Settings, ModelPickerTarget: new("account", null, "text", null)) };
        var service = new Service(); using var feature = Feature(service);
        foreach (var request in requests) { var result = await feature.OpenAsync(request); Check(!result.Succeeded && result.Code == "HomeFeatureTargetUnsupported" && ReferenceEquals(result.Request, request), "request replaced or target accepted"); }
        Check(service.Calls == 0, "unsupported target reached service");
    }),
    ("canonical bare Settings owner route yields truthful unavailable CUI single lease", async () => {
        var service = new Service(); using var feature = Feature(service); var result = await feature.OpenAsync(new(HomeFeatureRouteIds.Settings));
        Check(result.Succeeded && service.Calls == 1 && result.ViewState!.ViewId == AccountSettingsFeature.ViewId, "bare route");
        var surface = feature.Render(result.ViewState!); Check(surface.Bindings.TryGetValue("HasAccount", out var has) && has is false, "unavailable granted account");
        Throws(() => feature.Render(result.ViewState!)); surface.Lifetime!.Dispose();
    }),
    ("navigation cancellation after Open invalidates pending presentation", async () => {
        using var feature = Feature(new()); using var cts = new CancellationTokenSource(); var result = await feature.OpenAsync(new(HomeFeatureRouteIds.Settings), cts.Token); cts.Cancel(); Throws(() => feature.Render(result.ViewState!));
    }),
    ("feature disposal invalidates pending lease and denies future service reads", async () => {
        var service = new Service(); var feature = Feature(service); var result = await feature.OpenAsync(new(HomeFeatureRouteIds.Settings)); feature.Dispose(); Throws(() => feature.Render(result.ViewState!)); var denied = await feature.OpenAsync(new(HomeFeatureRouteIds.Settings)); Check(!denied.Succeeded && service.Calls == 1, "disposed service used");
    }),
    ("cancelled in-flight navigation cannot return presentation", async () => {
        var entered = new TaskCompletionSource(); var service = new Service { Wait = async ct => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); } }; using var feature = Feature(service); using var cts = new CancellationTokenSource(); var task = feature.OpenAsync(new(HomeFeatureRouteIds.Settings), cts.Token); await entered.Task; cts.Cancel(); try { await task; throw new Exception("cancel returned presentation"); } catch (OperationCanceledException) { }
    }),
    ("wrong route preserves request and performs zero service calls", async () => {
        var service = new Service(); using var feature = Feature(service); var request = new HomeFeatureNavigationRequest(HomeFeatureRouteIds.Apps); var result = await feature.OpenAsync(request); Check(!result.Succeeded && ReferenceEquals(result.Request, request) && service.Calls == 0, "wrong route accepted");
    }),
};
var failures = 0; foreach(var (name, run) in cases) { try { await run(); Console.WriteLine($"PASS {name}"); } catch(Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e}"); } }
Console.WriteLine($"Discovered={cases.Length} Executed={cases.Length} Passed={cases.Length-failures} Failed={failures} Skipped=0 Scope=UNIT real owner route/CUI lifecycle scripted unavailable service"); Environment.ExitCode = failures == 0 ? 0 : 1;
static void Check(bool test, string text) { if (!test) throw new Exception(text); }
static void Throws(Action action) { try { action(); } catch(InvalidOperationException) { return; } throw new Exception("expected invalid lease"); }
sealed class Service : IAccountBrowserTransport { public int Calls; public Func<CancellationToken, Task>? Wait; public async Task<JsonElement> InvokeAsync(string action, JsonElement? args, CancellationToken ct) { Calls++; if(Wait is not null) await Wait(ct); return JsonSerializer.SerializeToElement(new { ok = false, error = new { code = "ServiceUnavailable" } }); } }
