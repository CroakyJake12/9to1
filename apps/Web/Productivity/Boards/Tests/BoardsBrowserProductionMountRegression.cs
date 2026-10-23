using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using NineToOne.Web;
using NineToOne.Web.Accounts;
using NineToOne.Web.Productivity.Boards;

// Source-linked production Render and real provided Notes workspace. This is a Linux
// native component control, not JS storage, account authority or installed app acceptance.
if (args.Length != 1) throw new ArgumentException("An exact output JSON path is required.");
if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This owning fixture uses the actual Linux Notes store.");
var output = Path.GetFullPath(args[0]);
var results = new List<object>();
var failures = 0;
await using var session = HeadlessUnitTestSession.StartNew(typeof(BoardsProductionMountApplication));
await session.Dispatch(async () =>
{
    async Task Case(string name, Func<Task> body)
    {
        try { await body(); results.Add(new { name, passed = true, error = (string?)null }); Console.WriteLine("PASS: " + name); }
        catch (Exception error) { ++failures; results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL: " + name + Environment.NewLine + error); }
    }
    foreach (var accent in new[] { false, true })
        await Case(accent ? "Actual BrowserApplication Render reveals shared Boards through accent plus shell and persists a board"
            : "Actual BrowserApplication Render reveals shared Boards through shell and persists a board", () => MountActualBoardsAsync(accent));

    await Case("Typed opaque result crosses both wrappers as the exact original Task and value", async () =>
    {
        var value = new object(); var outcome = new CuiCommandOutcome(true, false, "ActualTyped", null, value);
        var raw = Task.FromResult(outcome); var owner = new TypedSource(raw);
        using var accent = new BrowserAppAccentSettings(owner, owner, new ReadOnlyTheme(), "app.boards");
        var shell = new BrowserShellActions(accent, () => throw new InvalidOperationException("Unexpected Home command."));
        var actual = Reporting(shell).DispatchWithOutcomeAsync("Typed", null).AsTask();
        Demand(ReferenceEquals(raw, actual), "Typed original Task was replaced.");
        Demand(ReferenceEquals(outcome, await actual) && ReferenceEquals(value, actual.Result.Value), "Typed result/value identity was lost.");
    });
    await Case("Typed refusal and cancellation fields are preserved without implicit success", async () =>
    {
        foreach (var outcome in new[] { new CuiCommandOutcome(false, false, "PermissionDenied", "Actual refusal", new object()),
            new CuiCommandOutcome(false, true, "Cancelled", "Actual reported cancellation", null) })
        {
            var owner = new TypedSource(Task.FromResult(outcome));
            using var accent = new BrowserAppAccentSettings(owner, owner, new ReadOnlyTheme(), "app.boards");
            var shell = new BrowserShellActions(accent, () => { });
            Demand(ReferenceEquals(outcome, await Reporting(shell).DispatchWithOutcomeAsync("Typed", null)), "Reported refusal/cancellation was rewritten.");
        }
    });
    await Case("Faulted original typed OCE remains the same Faulted Task with its exact cause", async () =>
    {
        var cause = new OperationCanceledException("Controlled faulted typed source, not actual cancellation.");
        var raw = Task.FromException<CuiCommandOutcome>(cause); var owner = new TypedSource(raw);
        using var accent = new BrowserAppAccentSettings(owner, owner, new ReadOnlyTheme(), "app.boards");
        var actual = Reporting(new BrowserShellActions(accent, () => { })).DispatchWithOutcomeAsync("Typed", null).AsTask();
        Exception? observed = null; try { await actual; } catch (Exception error) { observed = error; }
        Demand(ReferenceEquals(raw, actual) && actual.IsFaulted && !actual.IsCanceled && ReferenceEquals(cause, observed), "Faulted OCE provenance changed.");
    });
    await Case("Canceled original typed Task remains the same Canceled Task", async () =>
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var raw = Task.FromCanceled<CuiCommandOutcome>(canceled.Token); var owner = new TypedSource(raw);
        using var accent = new BrowserAppAccentSettings(owner, owner, new ReadOnlyTheme(), "app.boards");
        var actual = Reporting(new BrowserShellActions(accent, () => { })).DispatchWithOutcomeAsync("Typed", null).AsTask();
        try { await actual; } catch (OperationCanceledException) { }
        Demand(ReferenceEquals(raw, actual) && actual.IsCanceled && !actual.IsFaulted, "Canceled typed source was replaced or faulted.");
    });
    await Case("Explicit legacy lifetime route preserves both exact cancellation roles", async () =>
    {
        var owner = new LifetimeSource(); using var view = new CancellationTokenSource(); using var caller = new CancellationTokenSource();
        using var accent = new BrowserAppAccentSettings(owner, owner, new ReadOnlyTheme(), "app.boards");
        var actual = new BrowserShellActions(accent, () => { }).DispatchWithLifetimeAsync("Legacy", null, new(view.Token, caller.Token)).AsTask();
        Demand(owner.Observed.ViewCancellation == view.Token && owner.Observed.CallerCancellation == caller.Token && !actual.IsCompleted,
            "Explicit lifetime roles/source settlement changed.");
        owner.Release.SetResult(); await actual;
    });
    await Case("CUI result fallback preserves legacy view retirement separately from caller cancellation", async () =>
    {
        var owner = new LifetimeSource(); using var view = new CancellationTokenSource();
        using var accent = new BrowserAppAccentSettings(owner, owner, new ReadOnlyTheme(), "app.boards");
        var actual = Reporting(new BrowserShellActions(accent, () => { })).DispatchWithOutcomeAsync("Legacy", null, view.Token).AsTask();
        Demand(owner.Observed.ViewCancellation == view.Token && !owner.Observed.CallerCancellation.CanBeCanceled && !actual.IsCompleted,
            "CUI view token was promoted into explicit caller cancellation.");
        view.Cancel(); Demand(!actual.IsCompleted, "View retirement fabricated original settlement.");
        owner.Release.SetResult(); Demand((await actual).Succeeded, "Legacy completed source lost its prior implicit outcome.");
    });
}, CancellationToken.None);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { discovered = results.Count, passed = results.Count - failures,
    failed = failures, notRun = 0, checks = results,
    limits = "Actual production Render/shared Boards/real Notes component mount plus controlled typed/lifetime contract sources. No JS/account/native package/full specification acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;

static void Demand(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static ICuiResultReportingDispatcher Reporting(object owner) => owner as ICuiResultReportingDispatcher
    ?? throw new InvalidOperationException("The actual wrapper does not preserve typed command results.");
static T Field<T>(BrowserApplication app, string name) => (T)typeof(BrowserApplication).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

static async Task MountActualBoardsAsync(bool useAccent)
{
    var root = Path.Combine(Path.GetTempPath(), "9to1-boards-production-render-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    var paths = new ActualPaths(root);
    await using var diagnostics = new ProductionDiagnostics(paths);
    var notes = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
    await using var core = new BoardsBrowserCore(notes);
    await core.ListAsync();
    var app = new BrowserApplication();
    if (useAccent) typeof(BrowserApplication).GetField("_themePreferences", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, new ReadOnlyTheme());
    var view = Field<ContentControl>(app, "_view");
    var window = new Window { Content = view, Width = 1100, Height = 800 }; window.Show();
    try
    {
        using var stream = typeof(BoardsProductionMountApplication).Assembly.GetManifestResourceStream("Boards.ProductionMount.cui")
            ?? throw new InvalidOperationException("The SAME authored Boards CUI is missing.");
        using var reader = new StreamReader(stream);
        var document = new CuiDocumentSource(reader.ReadToEnd(), "Boards.cui").Document;
        var surface = BoardsBrowserSurface.Create(document, core, () => true);
        var render = typeof(BrowserApplication).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance)!;
        bool mounted;
        try { mounted = (bool)render.Invoke(app, [surface, "#/app.boards", "app.boards"])!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        Demand(mounted, "Actual BrowserApplication.Render refused SAME Boards document.");
        var loader = Field<CuiControlLoader>(app, "_loader"); await loader.WhenActionsIdleAsync();
        using (var frame = window.CaptureRenderedFrame()) Demand(frame is not null, "Actual native frame did not render.");
        var controls = view.GetLogicalDescendants().OfType<Control>().ToArray();
        Demand(controls.Any(control => control.Name == "boards-workspace") && !controls.Any(control => control.Name == "boards-storage-setup"),
            "Shared CUI did not reveal the provided workspace through actual production wrappers.");
        Demand((typeof(BrowserApplication).GetField("_appAccent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app) is not null) == useAccent,
            "Actual production accent branch did not match this control.");
        var create = controls.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "＋ Create a board");
        Demand(create.IsEffectivelyEnabled, "Real Create command is disabled.");
        create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
        var saved = (await notes.ListAsync(default)).Single(); var reopened = await notes.LoadAsync(saved.Id, default);
        Demand(reopened is not null && reopened.Version > 0 && core.DocumentId == saved.Id, "Actual typed Create did not persist the canonical board.");
        var artifact = Path.Combine(root, "Notes", "Documents", saved.Id.ToString("D"), "current.haven-notes.json");
        Demand(File.Exists(artifact) && new FileInfo(artifact).Length > 0, "Canonical saved artifact is missing or empty.");
    }
    finally { await app.DisposeAsync(); window.Content = null; window.Close(); }
    await diagnostics.DisposeAsync(); Directory.Delete(root, true);
}

public sealed class BoardsProductionMountApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<BoardsProductionMountApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize()
    {
        CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
        NineToOne.Web.Write.WriteRetainedSceneResources.Register(this);
    }
}
internal sealed class ActualPaths(string root) : IAppPaths
{
    public string DataDirectory => root;
    public string DatabasePath => Path.Combine(root, "fixture.sqlite");
    public string BrowserProfileDirectory => Path.Combine(root, "browser");
    public string AttachmentsDirectory => Path.Combine(root, "attachments");
    public string LogsDirectory => Path.Combine(root, "logs");
    public string LegacyStatePath => Path.Combine(root, "legacy.json");
}
internal sealed class TypedSource(Task<CuiCommandOutcome> original) : ICuiBindingContext, ICuiResultReportingDispatcher, ICuiActionAvailability
{
    public bool TryGetValue(string path, out object? value) { value = null; return false; }
    public bool? IsActionAvailable(string command) => command == "Typed";
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default) => throw new InvalidOperationException("Typed source was sent through untyped fallback.");
    public ValueTask<CuiCommandOutcome> DispatchWithOutcomeAsync(string command, object? parameter, CancellationToken token = default) => new(original);
}
internal sealed class LifetimeSource : ICuiBindingContext, ICuiLifetimeAwareActionDispatcher, ICuiActionAvailability
{
    internal CuiActionDispatchLifetime Observed;
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool TryGetValue(string path, out object? value) { value = null; return false; }
    public bool? IsActionAvailable(string command) => command == "Legacy";
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default) => throw new InvalidOperationException("Exact lifetime route was discarded.");
    public ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime)
    { Observed = lifetime; return new(Release.Task); }
}
internal sealed class ReadOnlyTheme : IBrowserHomeThemePreferences
{
    public CuiThemeDefinitionCatalog Catalog { get; } = new();
    public CuiThemePreferenceSnapshot Current { get; } = new("Professional", 0, "render-only-readonly", false, new());
    public string? Diagnostic => "Read-only component presentation; no account or durable preference owner.";
    public bool CanChange => false;
    public event EventHandler? Changed { add { } remove { } }
    public Task<HomeThemeOperationResult> SetAsync(string name, CuiAccessibilitySettings? accessibility = null, CancellationToken token = default,
        CuiAppearance? appearance = null, CuiAccentPreference? accent = null) => throw new InvalidOperationException("This control cannot fabricate a Home preference write.");
    public Task RefreshAsync(CancellationToken token = default) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

namespace NineToOne.Web
{
    // Unused JS/bootstrap boundaries only. Production Render, wrappers, core, CUI,
    // native renderer, theme resources and canonical Notes persistence are source-linked.
    internal static class Program
    {
        internal static void ShowStatus(string code, string message) => throw new InvalidOperationException(code + ": " + message);
        internal static void Attach(BrowserApplication app) => throw Unused();
        internal static void WriteFragment(string fragment, bool replace) => throw Unused();
        internal static string ReadFragment() => throw Unused();
        private static Exception Unused() => new InvalidOperationException("Production mount fixture does not replace JS/bootstrap.");
    }
    internal static class BrowserFeatureComposition
    {
        internal static AccountSettingsFeature Register(BrowserSurfaceRegistry registry) => throw Unused();
        internal static AccountSettingsFeature RegisterPrivateAccountSettings(BrowserSurfaceRegistry registry) => throw Unused();
        internal static Task<IBrowserHomeThemePreferences> CreateThemePreferencesAsync() => Task.FromResult<IBrowserHomeThemePreferences>(new ReadOnlyTheme());
        private static Exception Unused() => new InvalidOperationException("No alternate browser feature/bootstrap composition exists in this control.");
    }
    internal sealed class BrowserHomeLocalPreferences : IHomeLocalPreferences
    {
        public string? Read() => throw new InvalidOperationException("No substitute JS Home storage is provided.");
        public void Write(string json) => throw new InvalidOperationException("No substitute JS Home storage is provided.");
    }
}
