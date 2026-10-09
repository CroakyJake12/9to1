using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using NineToOne.Web.Assistants;

// Counterexamples over production bindings. They supply no genuine account, Den or execution authority.
var checks = new (string Name, Func<Task> Run)[]
{
    ("Missing authenticated Home owner refuses every product action", MissingOwner),
    ("Retired setup hides every prior binding", RetiredSetup),
    ("Queued private values cannot reappear through the revoked fence", QueuedPrivateValues),
    ("Actual held acquisition and owner close remain pending and preserve raw sibling faults", HeldAcquisition),
    ("Dedicated setup route refuses targeting and keeps service availability explicit", SetupRoute)
};
foreach (var check in checks)
{
    await check.Run();
    Console.WriteLine("PASS " + check.Name);
}

static async Task MissingOwner()
{
    var setup = new BrowserAssistantsBootstrap();
    Require(setup.Document.Components.Count != 0, "The owning authored Home CUI must be parsed.");
    Require(setup.TryGetValue("DependencyStatus", out var status) &&
        Equals(status, BrowserAssistantsBootstrap.MissingOwnerMessage), "Missing owner must be explicit.");
    foreach (var command in new[] { "assistants.create.start", "assistants.configuration.save", "assistants.open",
        "assistants.conversation.new", "assistants.send", "assistants.task.start", "assistants.task.steer",
        "assistants.resume", "assistants.resources.open", "arbitrary-unregistered-command" })
    {
        Require(setup.IsActionAvailable(command) == false, "Setup enabled an action: " + command);
        try { await setup.DispatchAsync(command, null); throw new Exception("Unavailable action reported completion."); }
        catch (BrowserAssistantsHostUnavailableException) { }
    }
    foreach (var path in new[] { "CanCreate", "CanRefresh", "HasAssistants", "HasActiveWork", "HasRecentConversations" })
        Require(setup.TryGetValue(path, out var value) && Equals(value, false), "Setup projected usable state: " + path);
}

static async Task RetiredSetup()
{
    var setup = new BrowserAssistantsBootstrap();
    setup.RevokePrivateContext();
    foreach (var path in new[] { "Status", "DependencyStatus", "Assistants", "RecentConversations", "CanCreate" })
        Require(!setup.TryGetValue(path, out var value) && value is null, "Retired setup exposed prior state.");
    try { await setup.DispatchAsync("assistants.create.start", null); throw new Exception("Retired setup admitted action."); }
    catch (BrowserAssistantsHostUnavailableException) { }
}

static async Task QueuedPrivateValues()
{
    var actualProjection = new CuiViewModel();
    actualProjection.Set("PrivateName", "synthetic old-account value");
    using var fence = new BrowserAssistantsBindingFence(actualProjection, actualProjection);
    Require(fence.TryGetValue("PrivateName", out var before) && Equals(before, "synthetic old-account value"),
        "Control must observe its actual initial projection.");
    var notifications = 0;
    fence.PropertyChanged += (_, _) => notifications++;
    Action queuedOldPublication = () => actualProjection.Set("PrivateName", "late old-account value");
    fence.Revoke();
    queuedOldPublication();
    Require(!fence.TryGetValue("PrivateName", out var after) && after is null,
        "A late original publication crossed the revoked read boundary.");
    Require(!fence.TrySetValue("PrivateName", "unauthorized draft") && notifications == 0,
        "Retired write/notification crossed the fence.");
    Require(fence.IsActionAvailable("anything") == false, "Retired action remained available.");
    try { await fence.DispatchAsync("anything", null); throw new Exception("Revoked action dispatched."); }
    catch (UnauthorizedAccessException) { }
    Require(actualProjection.TryGetValue("PrivateName", out var retained) && Equals(retained, "late old-account value"),
        "The fence must hide the actual projection rather than overwrite its owner's evidence.");
}

static async Task SetupRoute()
{
    var actual = new AssistantsBrowserFeature();
    try
    {
        var navigation = await actual.OpenAsync(new("app.assistants"));
        Require(navigation.Succeeded && navigation.Code == "SetupRequired", "Setup navigation must disclose unavailable services.");
        Require(navigation.ViewState is not null, "Dedicated product setup must provide its own surface.");
        var surface = actual.Render(navigation.ViewState!);
        Require(surface.Bindings is BrowserAssistantsBootstrap && surface.Actions is BrowserAssistantsBootstrap,
            "Missing host must not manufacture a canonical controller or business action dispatcher.");
        Require(!(await actual.OpenAsync(new("app.assistants", EntityType: "Assistant", EntityId: "copied-public-id"))).Succeeded,
            "A public ID must not open an owner-issued definition binding.");
        actual.RevokePrivateContext();
        Require(!surface.Bindings.TryGetValue("DependencyStatus", out _), "Old setup bindings must be revoked immediately.");
        Require(!(await actual.OpenAsync(new("app.assistants"))).Succeeded, "Revoked product admitted another navigation.");
    }
    finally { await actual.CloseAndDrainAsync(); }
}

static async Task HeldAcquisition()
{
    var before = ExecutionContext.Capture()!;
    var owner = new HeldOwner();
    var adapter = new BrowserAssistantsOwnerAdapter(owner);
    Exception? selfJoin = null;
    owner.OnOpen = () => ExecutionContext.Run(before, _ =>
    {
        try { _ = adapter.CloseAndDrainAsync(); }
        catch (Exception refusal) { selfJoin = refusal; }
    }, null);
    Task<bool>? initialization = null; Task? close = null;
    var first = new IOException("controlled actual factory fault");
    var second = new InvalidDataException("independent actual factory sibling");
    var unexpected = new List<Exception>();
    try
    {
        initialization = adapter.InitializeOriginalAsync();
        await owner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Require(selfJoin is InvalidOperationException, "Restored source context joined its live acquisition.");
        adapter.RevokePrivateContext();
        Require(owner.Revoked && !adapter.IsCurrent, "Actual authority was not synchronously revoked.");
        close = adapter.CloseAndDrainAsync();
        Require(ReferenceEquals(close, adapter.CloseAndDrainAsync()), "The adapter replaced its actual close Task.");
        Require(!initialization.IsCompleted && !owner.Factory.Task.IsCompleted && !close.IsCompleted,
            "Held original acquisition was represented as settled.");
        owner.Factory.TrySetException(new Exception[] { first, second });
        await owner.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Require(!owner.Close.Task.IsCompleted && !close.IsCompleted, "Actual owner cleanup was not independently joined.");
    }
    catch (Exception error) { unexpected.Add(error); }
    finally
    {
        owner.Factory.TrySetException(new Exception[] { first, second }); owner.Close.TrySetResult();
        close ??= adapter.CloseAndDrainAsync();
        foreach (var original in new Task?[] { initialization, owner.Factory.Task, owner.Close.Task, close })
        {
            if (original is null) continue;
            try { await original; }
            catch (Exception error)
            {
                var causes = Leaves(original.Exception ?? error).ToArray();
                if (causes.Any(cause => !ReferenceEquals(cause, first) && !ReferenceEquals(cause, second))) unexpected.Add(error);
                if (!ReferenceEquals(original, owner.Close.Task))
                { if (!causes.Contains(first) || !causes.Contains(second)) unexpected.Add(new Exception("An actual sibling fault was lost.")); }
            }
        }
    }
    if (unexpected.Count != 0) throw new AggregateException("Acquisition control or independent cleanup failed.", unexpected);
}

static IEnumerable<Exception> Leaves(Exception error)
{
    if (error is AggregateException group && group.InnerExceptions.Count != 0)
    { foreach (var child in group.InnerExceptions) foreach (var leaf in Leaves(child)) yield return leaf; }
    else yield return error;
}

static void Require(bool condition, string message)
{ if (!condition) throw new InvalidOperationException(message); }

sealed class HeldOwner : IBrowserAssistantsCanonicalOwner
{
    internal readonly TaskCompletionSource<IAssistantCanonicalBridge> Factory = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource CloseEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Close = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Action? OnOpen;
    internal bool Revoked;
    public ICuiSceneReadiness OriginalReadiness { get; } = new UnavailableReadiness();
    public Task<IAssistantCanonicalBridge> OpenOriginalBridgeAsync(CancellationToken token)
    { OnOpen?.Invoke(); Entered.TrySetResult(); return Factory.Task; }
    public void RevokePrivateContext() => Revoked = true;
    public void DemandExternalOriginalRetirementJoin() { }
    public Task CloseAndDrainAsync() { CloseEntered.TrySetResult(); return Close.Task; }
    private sealed class UnavailableReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable, "controlled-unavailable", "No genuine browser Home owner is supplied."));
    }
}
