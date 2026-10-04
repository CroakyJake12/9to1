using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui.Runtime;
using NineToOne.Web;

// Controlled cancellation callback boundary regression. This executes the actual
// BrowserApplication navigation path, codec, registry and native prior root.
// No owner/backend/authentication or published-browser failure is fabricated.
if (args.Length != 1) throw new ArgumentException("output.json");
var output = Path.GetFullPath(args[0]);
await using var session = HeadlessUnitTestSession.StartNew(typeof(NavigationProbeApplication));
var failed = await session.Dispatch(async () =>
{
    var checks = new List<object>();
    var failures = 0;
    for (var mode = 0; mode != 5; ++mode)
    {
        var app = new BrowserApplication();
        var view = Field<ContentControl>(app, "_view");
        var previous = new TextBlock { Text = "Retained actual native root" };
        var lifetime = new NavigationLifetime();
        view.Content = previous;
        Set(app, "_surfaceLifetime", lifetime);
        var window = new Window { Content = view, Width = 600, Height = 400 };
        var statuses = NineToOne.Web.Program.Statuses;
        statuses.Clear();
        var prior = mode == 4 ? null : new CancellationTokenSource();
        if (prior is not null) Set(app, "_navigationCancellation", prior);
        CancellationTokenSource? innerSource = null;
        var innerStatus = "";
        var callbacks = 0;
        var registrations = new List<CancellationTokenRegistration>();
        // The actual missing-route result is derived from the maintained registry,
        // not an invented backend result or an intercepted navigation implementation.
        var missing = await app.Surfaces.PreparePresentationAsync(new("missing.fixture"), CancellationToken.None);
        if (missing.Result.Succeeded || missing.Present is not null) throw new InvalidOperationException("Fixture route unexpectedly registered.");
        void Reenter()
        {
            ++callbacks;
            app.QueueNavigation("#/missing.fixture");
            innerSource = Field<CancellationTokenSource>(app, "_navigationCancellation");
            innerStatus = statuses.Last().Code;
        }
        if (mode == 0) registrations.Add(prior!.Token.Register(() => { ++callbacks; throw new NavigationCallbackFault(); }));
        if (mode == 1) registrations.Add(prior!.Token.Register(Reenter));
        // Cancellation callbacks execute LIFO: newer request is issued first,
        // then a fault from the older request must not overwrite its status.
        if (mode == 2)
        {
            registrations.Add(prior!.Token.Register(() => { ++callbacks; throw new NavigationCallbackFault(); }));
            registrations.Add(prior.Token.Register(Reenter));
        }
        if (mode == 3) registrations.Add(prior!.Token.Register(() => ++callbacks));
        var name = new[] {
            "Throwing cancellation callback produces current observable failure and preserves prior native view",
            "Reentrant newest navigation keeps its token and missing-route status",
            "Older callback fault cannot overwrite reentrant newest navigation status",
            "Ordinary cancellation retires only the prior token and preserves invalid-route view",
            "First invalid navigation advances its token/version and preserves native view"
        }[mode];
        bool passed = false;
        string? setupError = null;
        object? state = null;
        window.Show();
        try
        {
            using var frame = window.CaptureRenderedFrame();
            if (frame is null) throw new InvalidOperationException("Actual native frame was not rendered.");
            var observed = (Task)typeof(BrowserApplication).GetMethod("NavigateObservedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(app, ["#/older-invalid!"])!;
            await observed;
            var current = Field<CancellationTokenSource?>(app, "_navigationCancellation");
            var version = Field<long>(app, "_navigationVersion");
            var last = statuses.Count == 0 ? null : statuses.Last().Code;
            var priorDisposed = prior is null || IsDisposed(prior);
            var currentActive = current is not null && !IsDisposed(current) && !current.IsCancellationRequested;
            var retained = ReferenceEquals(view.Content, previous) && previous.Parent is not null && lifetime.Disposes == 0;
            passed = mode switch {
                0 => callbacks == 1 && version == 1 && last == "BrowserSurfaceFailed" && priorDisposed && currentActive && retained,
                1 => callbacks == 1 && version == 2 && ReferenceEquals(current, innerSource) && currentActive
                    && innerStatus == missing.Result.Code && last == innerStatus && priorDisposed && retained,
                2 => callbacks == 2 && version == 2 && ReferenceEquals(current, innerSource) && currentActive
                    && innerStatus == missing.Result.Code && last == innerStatus && priorDisposed && retained,
                3 => callbacks == 1 && version == 1 && last == "BrowserRouteInvalid" && priorDisposed && currentActive && retained,
                4 => callbacks == 0 && version == 1 && last == "BrowserRouteInvalid" && currentActive && retained,
                _ => false
            };
            state = new { version, callbacks, lastStatus = last, statuses = statuses.Select(s => new { code = s.Code, message = s.Message }).ToArray(),
                missingOwnerCode = missing.Result.Code, innerStatus, newestSourceRetained = ReferenceEquals(current, innerSource), priorDisposed,
                currentActive, previousNativeViewRetained = retained, lifetimeDisposes = lifetime.Disposes, actualFrame = true };
        }
        catch (Exception error) { setupError = error.ToString(); }
        finally
        {
            foreach (var registration in registrations) registration.Dispose();
            await app.DisposeAsync();
            window.Close();
            prior?.Dispose();
        }
        checks.Add(new { name, passed, state, setupError });
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
        if (!passed) ++failures;
    }
    File.WriteAllText(output, JsonSerializer.Serialize(new { discovered = 5, passed = 5 - failures, failed = failures, notRun = 0, checks,
        limits = "Actual source-linked native cancellation boundary fixture with deliberately controlled owner callbacks; no observed published-browser failure, rendered destination, owner service, authentication or full parity acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
    return failures;
}, CancellationToken.None);
return failed == 0 ? 0 : 1;

static T Field<T>(BrowserApplication app, string name) => (T)typeof(BrowserApplication).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
static void Set(BrowserApplication app, string name, object? value) => typeof(BrowserApplication).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, value);
static bool IsDisposed(CancellationTokenSource source) { try { _ = source.Token; return false; } catch (ObjectDisposedException) { return true; } }
public sealed class NavigationProbeApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<NavigationProbeApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
}
internal sealed class NavigationLifetime : IDisposable { internal int Disposes { get; private set; } public void Dispose() => ++Disposes; }
internal sealed class NavigationCallbackFault : Exception { }
