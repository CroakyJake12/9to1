using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Web;

// Executes the unchanged production private Render method, not a mirror selector.
// Admission/binding spies test UI ownership only, never backend authorisation.
await using var session = HeadlessUnitTestSession.StartNew(typeof(RenderLifecycleApplication));
await session.Dispatch(() =>
{
    var checks = 0;
    void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS: {name}"); ++checks;
    }
    var app = new BrowserApplication();
    var view = Field<ContentControl>("_view");
    var window = new Window { Content = view, Width = 700, Height = 500 };
    var render = typeof(BrowserApplication).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance)!;
    T Field<T>(string name) => (T)typeof(BrowserApplication).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
    bool Present(BrowserCuiSurface surface, string address)
    {
        try { return (bool)render.Invoke(app, [surface, address])!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    Exception? Failure(Action operation) { try { operation(); return null; } catch (Exception error) { return error; } }
    string CurrentActionId(string caption)
    {
        using var snapshot = JsonDocument.Parse(app.ReadAccessibility());
        return snapshot.RootElement.GetProperty("elements").EnumerateArray()
            .Single(peer => peer.GetProperty("name").GetString() == caption && peer.GetProperty("role").GetString() == "button")
            .GetProperty("id").GetString()!;
    }
    var events = new List<string>();
    var priorModel = new RenderProbeModel("Previous native root");
    var priorLifetime = new RenderProbeLifetime("previous", events);
    var priorAdmission = new RenderProbeAdmission("previous", events);
    var priorSurface = Surface(priorModel, priorLifetime, priorAdmission);
    window.Show();
    try
    {
        Check("Initial production Render accepts actual compatible CUI", Present(priorSurface, "#/app.write?entityId=prior"));
        using (var frame = window.CaptureRenderedFrame()) Check("Actual native prior view renders", frame is not null);
        var previousView = view.Content;
        var previousLoader = Field<CuiControlLoader>("_loader");
        var previousAvailability = Field<BrowserActionAvailability.Observation>("_availability");
        using var originalSnapshot = JsonDocument.Parse(app.ReadAccessibility());
        var priorId = originalSnapshot.RootElement.GetProperty("elements").EnumerateArray()
            .Single(peer => peer.GetProperty("name").GetString() == priorModel.Caption && peer.GetProperty("role").GetString() == "button")
            .GetProperty("id").GetString()!;
        Check("Prior native binding observers and owning lifetime remain active", priorModel.Subscriptions > 0 && priorLifetime.Disposes == 0
            && priorAdmission.Accepts == 1 && priorAdmission.Rejects == 0);

        var invalidModel = new RenderProbeModel("Never lowered" );
        var invalidLifetime = new RenderProbeLifetime("invalid", events);
        var invalidAdmission = new RenderProbeAdmission("invalid", events);
        var invalidDocument = new CuiRichParser().Parse("<Cui version=\"1\"><Object Type=\"B1UnknownProbeRenderer\" /></Cui>");
        Check("Actual failed CUI lowering is rejected", !Present(new(invalidDocument, invalidModel, invalidModel, invalidLifetime, Admission: invalidAdmission), "#/app.write?entityId=invalid"));
        Check("Lowering failure preserves previous native root and loader", ReferenceEquals(view.Content, previousView)
            && ReferenceEquals(Field<CuiControlLoader>("_loader"), previousLoader));
        Check("Lowering failure releases only candidate lifetime and rejects owner admission", invalidLifetime.Disposes == 1
            && invalidAdmission.Accepts == 0 && invalidAdmission.Rejects == 1 && invalidModel.Subscriptions == 0 && priorLifetime.Disposes == 0);
        Check("Lowering failure preserves prior actual AX provider", app.PerformAccessibility(priorId, "invoke", null) && priorModel.Invokes == 1);

        var rejectedModel = new RenderProbeModel("Accept throws" );
        var rejectedLifetime = new RenderProbeLifetime("reject", events);
        var rejectedAdmission = new RenderProbeAdmission("reject", events) { ThrowAccept = true };
        var rejectedCandidateInstalled = false;
        string? rejectedCandidateId = null;
        rejectedAdmission.OnAccept = () =>
        {
            rejectedCandidateInstalled = !ReferenceEquals(view.Content, previousView);
            if (rejectedCandidateInstalled) rejectedCandidateId = CurrentActionId(rejectedModel.Caption);
        };
        var acceptFailure = Failure(() => Present(Surface(rejectedModel, rejectedLifetime, rejectedAdmission), "#/app.write?entityId=reject"));
        Check("Owner Accept failure propagates unchanged", acceptFailure is RenderProbeFailure { Message: "reject accept" });
        Check("Accept failure rejects candidate and removes candidate native observers", rejectedAdmission.Accepts == 1
            && rejectedAdmission.Rejects == 1 && rejectedLifetime.Disposes == 1 && rejectedModel.Subscriptions == 0);
        Check("Accept failure keeps prior view, lifetime, native observations and address", ReferenceEquals(view.Content, previousView)
            && ReferenceEquals(Field<CuiControlLoader>("_loader"), previousLoader)
            && ReferenceEquals(Field<BrowserActionAvailability.Observation>("_availability"), previousAvailability)
            && priorLifetime.Disposes == 0 && Field<string>("_currentAddress") == "#/app.write?entityId=prior");
        var restoredAfterAcceptId = CurrentActionId(priorModel.Caption);
        Check("Accept rollback exposes actual prior provider and rejects stale IDs after candidate installation",
            (!rejectedCandidateInstalled || restoredAfterAcceptId != priorId && !app.PerformAccessibility(priorId, "invoke", null))
            && app.PerformAccessibility(restoredAfterAcceptId, "invoke", null) && priorModel.Invokes == 2);
        Check("Rejected installed candidate cannot replay its real native AX ID", rejectedCandidateId is null
            || !app.PerformAccessibility(rejectedCandidateId, "invoke", null) && rejectedModel.Invokes == 0);

        // Avalonia sets Content first, then synchronously raises PropertyChanged.
        // This real native listener throws once after observing the candidate.
        var assignmentModel = new RenderProbeModel("Assignment candidate" );
        var assignmentLifetime = new RenderProbeLifetime("assignment", events);
        var assignmentAdmission = new RenderProbeAdmission("assignment", events);
        var candidateWasAssigned = false;
        void ThrowAfterNativeAssignment(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == ContentControl.ContentProperty && !ReferenceEquals(view.Content, previousView) && !candidateWasAssigned)
            {
                candidateWasAssigned = true;
                throw new RenderProbeFailure("Native ContentControl listener");
            }
        }
        view.PropertyChanged += ThrowAfterNativeAssignment;
        Exception? assignmentFailure;
        try { assignmentFailure = Failure(() => Present(Surface(assignmentModel, assignmentLifetime, assignmentAdmission), "#/app.write?entityId=assignment")); }
        finally { view.PropertyChanged -= ThrowAfterNativeAssignment; }
        Check("Real native ContentControl listener faults after candidate assignment", candidateWasAssigned && assignmentFailure is RenderProbeFailure { Message: "Native ContentControl listener" });
        Check("Assignment fault restores prior native ContentControl content", ReferenceEquals(view.Content, previousView));
        Check("Assignment fault rejects unaccepted candidate and releases its observers/lifetime once", assignmentAdmission.Accepts == 0
            && assignmentAdmission.Rejects == 1 && assignmentLifetime.Disposes == 1 && assignmentModel.Subscriptions == 0);
        Check("Assignment fault retains prior ownership fields and address", ReferenceEquals(Field<CuiControlLoader>("_loader"), previousLoader)
            && ReferenceEquals(Field<BrowserActionAvailability.Observation>("_availability"), previousAvailability)
            && ReferenceEquals(Field<IDisposable>("_surfaceLifetime"), priorLifetime)
            && Field<string>("_currentAddress") == "#/app.write?entityId=prior" && priorLifetime.Disposes == 0);
        var restoredAfterAssignmentId = CurrentActionId(priorModel.Caption);
        Check("Assignment rollback exposes prior native provider through current ID and rejects stale generation", restoredAfterAssignmentId != restoredAfterAcceptId
            && !app.PerformAccessibility(restoredAfterAcceptId, "invoke", null)
            && app.PerformAccessibility(restoredAfterAssignmentId, "invoke", null) && priorModel.Invokes == 3);

        var nextModel = new RenderProbeModel("Next actual native root");
        var nextLifetime = new RenderProbeLifetime("next", events);
        var nextAdmission = new RenderProbeAdmission("next", events);
        nextAdmission.OnAccept = () =>
        {
            Check("Owner Accept occurs after actual candidate native view and AX installation", !ReferenceEquals(view.Content, previousView)
                && app.ReadAccessibility().Contains(nextModel.Caption, StringComparison.Ordinal) && priorLifetime.Disposes == 0
                && ReferenceEquals(Field<IDisposable>("_surfaceLifetime"), priorLifetime));
        };
        priorLifetime.OnDispose = () =>
        {
            Check("Old lifetime disposes only after new native view and AX are published", !ReferenceEquals(view.Content, previousView)
                && nextAdmission.Accepts == 1 && app.ReadAccessibility().Contains(nextModel.Caption, StringComparison.Ordinal)
                && ReferenceEquals(Field<IDisposable>("_surfaceLifetime"), nextLifetime));
        };
        Check("Successful actual Render performs ownership handoff", Present(Surface(nextModel, nextLifetime, nextAdmission), "#/app.write?entityId=next"));
        Check("Successful handoff disposes old native observers and lifetime exactly once", priorModel.Subscriptions == 0 && priorLifetime.Disposes == 1
            && priorAdmission.Rejects == 0 && nextModel.Subscriptions > 0 && nextLifetime.Disposes == 0);
        Check("Successful handoff rejects stale prior AX ID", !app.PerformAccessibility(restoredAfterAssignmentId, "invoke", null) && priorModel.Invokes == 3);
        using (var frame = window.CaptureRenderedFrame()) Check("Actual native candidate renders after successful handoff", frame is not null);
        var nextButton = ((Control)view.Content!).GetVisualDescendants().OfType<Button>().Single(button => (string?)button.Tag == "Run");
        Check("New bound native button content comes from next actual binding context", (string?)nextButton.Content == nextModel.Caption);
        app.ResetPrivateContext();
        Check("Actual context reset clears active view and new observers/lifetime", view.Content is null && nextModel.Subscriptions == 0
            && nextLifetime.Disposes == 1 && nextAdmission.Rejects == 0);
        Console.WriteLine($"{checks} source-linked actual BrowserApplication Render lifecycle checks passed; no browser or backend acceptance.");
        Console.WriteLine("EVENTS: " + string.Join(", ", events));
    }
    finally { priorLifetime.OnDispose = null; app.ResetPrivateContext(); window.Content = null; window.Close(); }
}, CancellationToken.None);

static BrowserCuiSurface Surface(RenderProbeModel model, RenderProbeLifetime lifetime, RenderProbeAdmission admission) =>
    new(new CuiRichParser().Parse("<Cui version=\"1\"><StackPanel><Button content=\"{Binding Caption}\" action=\"Run\" /><TextBlock text=\"{Binding Caption}\" /></StackPanel></Cui>"),
        model, model, lifetime, Admission: admission);

public sealed class RenderLifecycleApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<RenderLifecycleApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize()
    {
        CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
        NineToOne.Web.Write.WriteRetainedSceneResources.Register(this);
    }
}

internal sealed class RenderProbeModel(string caption) : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private PropertyChangedEventHandler? _changed;
    internal string Caption { get; } = caption;
    internal int Subscriptions { get; private set; }
    internal int Invokes { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { _changed += value; ++Subscriptions; }
        remove { _changed -= value; --Subscriptions; }
    }
    public bool TryGetValue(string path, out object? value) { value = path == "Caption" ? Caption : null; return path == "Caption"; }
    public bool? IsActionAvailable(string command) => command == "Run";
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    { if (command != "Run") throw new InvalidOperationException(command); ++Invokes; return ValueTask.CompletedTask; }
}

internal sealed class RenderProbeLifetime(string name, List<string> events) : IDisposable
{
    internal int Disposes { get; private set; }
    internal Action? OnDispose { get; set; }
    public void Dispose() { ++Disposes; events.Add(name + " dispose"); OnDispose?.Invoke(); }
}

internal sealed class RenderProbeAdmission(string name, List<string> events) : IBrowserPresentationAdmission
{
    internal int Accepts { get; private set; }
    internal int Rejects { get; private set; }
    internal bool ThrowAccept { get; init; }
    internal Action? OnAccept { get; set; }
    public void Accept() { ++Accepts; events.Add(name + " accept"); OnAccept?.Invoke(); if (ThrowAccept) throw new RenderProbeFailure(name + " accept"); }
    public void Reject() { ++Rejects; events.Add(name + " reject"); }
}
internal sealed class RenderProbeFailure(string message) : Exception(message);
