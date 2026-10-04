using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Web.Accounts;
using NineToOne.Web.Services;

// NATIVE SUPPORT CONTROL ONLY: actualcanonicalCUI/loader/ownerbindings andButton event;
// no physicalbrowser gesture, realissuer/token/provider or accountauthority inferred.
var cases = new (string, Func<CancellationToken, Task>)[] {
    ("actual canonical signin Button keeps selected broker caller alive across its own view reset", async deadline => {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(ProbeApplication));
        await native.Dispatch(async () => {
            using var loader = new CuiControlLoader(); AccountBrowserBindings? view = null;
            var callbackCalls = 0; var afterReset = 0; var callerCancelledAtReset = false;
            view = new(new FixtureTransport(), a => { a(); return Task.CompletedTask; }, async caller => {
                callbackCalls++; view!.RevokePrivateContext(); loader.Dispose(); await view.DisposeAsync();
                callerCancelledAtReset = caller.IsCancellationRequested;
                caller.ThrowIfCancellationRequested(); afterReset++;
            });
            try {
                var button = await ActualButton(loader, view, "account-signin", deadline);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync().WaitAsync(deadline);
                Console.WriteLine($"ResetReceipt CallbackCalls={callbackCalls} CallerCancelled={callerCancelledAtReset} AfterResetContinuation={afterReset} PrivateEmpty={Empty(view)}");
                Check(callbackCalls == 1 && !callerCancelledAtReset && afterReset == 1 && Empty(view), "OWN_SELECTED_CALLBACK_CANCELLED_BY_VIEW_TOKEN");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync().WaitAsync(deadline);
                Check(callbackCalls == 1, "RETAINED_BUTTON_DISPATCHED");
            } finally { loader.Dispose(); await view.DisposeAsync(); }
            return true;
        }, deadline);
    }),
    ("actual canonical Refresh Button remains view-cancellable and true drain waits ignored reply", async deadline => {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(ProbeApplication));
        await native.Dispatch(async () => {
            using var loader = new CuiControlLoader(); var entered = Signal(); var release = Signal(); var observed = false;
            var service = new FixtureTransport { Script = async ct => { using var registration = ct.Register(() => observed = true); entered.TrySetResult(); await release.Task; return Failure(); } };
            var view = new AccountBrowserBindings(service, a => { a(); return Task.CompletedTask; }, _ => Task.CompletedTask);
            Task? completion = null; Task? drain = null;
            try {
                var button = await ActualButton(loader, view, "account-refresh", deadline);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); completion = loader.WhenActionsIdleAsync();
                await entered.Task.WaitAsync(deadline); view.RevokePrivateContext(); loader.Dispose(); drain = view.DisposeAsync().AsTask();
                Check(observed && !completion.IsCompleted && !drain.IsCompleted && Empty(view), "READ_CANCEL_OR_TRUE_DRAIN_MISSING");
                release.TrySetResult(); await completion.WaitAsync(deadline); await drain.WaitAsync(deadline);
                Check(service.Calls == 1 && Empty(view), "OLD_READ_REOPENED_PRIVATE_STATE");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync().WaitAsync(deadline);
                Check(service.Calls == 1, "RETIRED_REFRESH_BUTTON_DISPATCHED");
            } finally { release.TrySetResult(); loader.Dispose(); if(completion is not null) await completion.WaitAsync(deadline); await view.DisposeAsync().AsTask().WaitAsync(deadline); }
            return true;
        }, deadline);
    }),
    ("direct explicit caller cancellation still cancels transferred selected signin", async deadline => {
        var entered = Signal(); var observed = false; using var caller = new CancellationTokenSource();
        var view = new AccountBrowserBindings(new FixtureTransport(), a => { a(); return Task.CompletedTask; }, async token => {
            entered.TrySetResult(); try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { observed = token.IsCancellationRequested; throw; }
        });
        Task? actual = null;
        try { actual = view.DispatchAsync("RequestSignIn", null, caller.Token).AsTask(); await entered.Task.WaitAsync(deadline); caller.Cancel(); await actual.WaitAsync(deadline); Check(observed && !view.HasOutstandingBrokerWork, "TRUE_CALLER_CANCELLATION_DROPPED"); }
        finally { caller.Cancel(); if(actual is not null) await actual.WaitAsync(deadline); await view.DisposeAsync().AsTask().WaitAsync(deadline); }
    }),
};
var passed = 0; var failed = 0;
foreach (var (name, body) in cases) {
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try { await body(deadline.Token); passed++; Console.WriteLine($"PASS {name}"); }
    catch(Exception error) { failed++; Console.WriteLine($"FAIL {name} Type={error.GetType().Name}"); }
}
Console.WriteLine($"Discovered={cases.Length} Executed={passed+failed} Passed={passed} Failed={failed} Skipped=0 Scope=NATIVE_UNIT_ACTUAL_LOADER_BUTTON_NO_BROWSER_PROVIDER_AUTHORITY");
return failed == 0 ? 0 : 1;

static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
static void Check(bool condition, string code) { if(!condition) throw new InvalidOperationException(code); }
static bool Empty(AccountBrowserBindings view) => view.TryGetValue("HasAccount", out var value) && value is false;
static JsonElement Failure() { using var document = JsonDocument.Parse("{\"ok\":false,\"error\":{\"code\":\"AuthenticationRequired\"}}"); return document.RootElement.Clone(); }
static async Task<Button> ActualButton(CuiControlLoader loader, AccountBrowserBindings view, string id, CancellationToken token) {
    var source = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Accounts.cui"), token);
    loader.SetBindingContext(view); loader.SetActionDispatcher(view);
    var root = loader.Load(new CuiRichParser().Parse(source, "Accounts.cui")) ?? throw new InvalidOperationException("OWNER_CUI_NOT_LOADED");
    loader.WireBindings(root);
    var button = root.GetLogicalDescendants().OfType<Button>().Single(control => control.Name == id);
    Check(button.IsEnabled, "ACTUAL_BUTTON_NOT_ENABLED"); return button;
}
public sealed class ProbeApplication : Application { }
public sealed class FixtureTransport : IAccountBrowserTransport {
    public int Calls; public Func<CancellationToken, Task<JsonElement>>? Script;
    public async Task<JsonElement> InvokeAsync(string action, JsonElement? arguments, CancellationToken ct) {
        Calls++; if(Script is not null) return await Script(ct);
        using var document = JsonDocument.Parse("{\"ok\":false,\"error\":{\"code\":\"AuthenticationRequired\"}}"); return document.RootElement.Clone();
    }
}
