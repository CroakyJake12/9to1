using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Files;
using HavenOS.Home.Core;
using NineToOne.Web.Files;
using NineToOne.Web.Sites;

if (args.Length != 1 || !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Pass a fresh isolated absolute data root.");
var data = args[0]; Directory.CreateDirectory(data);
var cases = new (string Name, Func<string, Task> Body)[]
{
    ("actual-files-dirty-refusal-save-close-canonical-reopen", FilesClose),
    ("actual-files-held-init-fence-failure-awaits", FilesHeldRead),
    ("actual-sites-dirty-refusal-save-close-canonical-reopen", SitesClose),
    ("actual-sites-held-init-fence-failure-awaits", SitesHeldRead),
    ("actual-files-populated-draft-clears-on-fence-failure", FilesPrivateClear),
    ("actual-sites-populated-draft-clears-on-fence-failure", SitesPrivateClear),
    ("actual-files-reentrant-negative-owner-drain-issued-once", FilesReentrantDrain),
    ("actual-sites-reentrant-negative-owner-drain-issued-once", SitesReentrantDrain),
    ("actual-files-held-preparation-opening-remains-issued", FilesHeldPreparation),
    ("actual-sites-held-preparation-opening-remains-issued", SitesHeldPreparation),
    ("actual-files-repeated-missing-fence-stays-failed", FilesStickyFence),
    ("actual-sites-repeated-missing-fence-stays-failed", SitesStickyFence),
    ("actual-files-owner-action-reentry-joins-fence-settlement", FilesFenceSettlement),
    ("actual-sites-owner-action-reentry-joins-fence-settlement", SitesFenceSettlement)
};
var passed = 0;
foreach (var item in cases)
{
    try { await item.Body(Path.Combine(data, item.Name)); Console.WriteLine("PASS " + item.Name); passed++; }
    catch (Exception error) { Console.WriteLine("FAIL " + item.Name + ": " + error); }
}
Console.WriteLine($"RESULT expected=14 executed=14 passed={passed} failed={14-passed}; realOwnerGroupAuthority=BLOCKED browser=NOT_RUN");
return passed == 14 ? 0 : 1;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
static Action MissingActualIssuer() => () => throw new NotSupportedException("Negative fixture: actual issuer-group revocation port is NOT configured.");
static Func<ValueTask> MissingActualDrain() => () => ValueTask.FromException(new NotSupportedException("Negative fixture: actual issuer-group drain port is NOT configured."));
static bool HasMissingOwner(Exception error) => error is NotSupportedException || error is AggregateException aggregate && aggregate.InnerExceptions.Any(HasMissingOwner) || error.InnerException is { } inner && HasMissingOwner(inner);
static CuiDocument Doc(string app)
{
    var parser = new CuiRichParser(); var doc = parser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, app+".cui")), app+".cui");
    Check(!parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "Original CUI parser failure."); return doc;
}
static (DurableDriveProvider Provider, string Actor, FilesLocationId Location, string Path) Store(string root)
{
    Directory.CreateDirectory(root); const string actor = "local-provider-test-owner";
    var id = new FilesLocationId(Guid.NewGuid()); var path = Path.Combine(root,"canonical-files.json");
    return (new(path,id,actor),actor,id,path);
}
static async Task FilesClose(string root)
{
    var store = Store(root); var route = new FilesBrowserRoute(store.Provider,store.Actor,Doc("Files"),MissingActualIssuer(),MissingActualDrain());
    var opened = await route.OpenAsync(new(FilesBrowserRoute.Id)); Check(opened.Succeeded,"Actual Files route opening failed.");
    var surface = route.CreateSurface(opened.ViewState!); var view = (FilesBrowserController)surface.Bindings;
    await view.DispatchAsync("NewFolder",null); Check(view.TrySetValue("Name","Canonical retained draft"),"Actual draft not writable.");
    Check(route.HasUnsavedChanges && !(await route.PrepareToCloseAsync()).Succeeded,"Dirty ordinary close was accepted.");
    Check(view.TryGetValue("Name",out var name) && name as string == "Canonical retained draft","Ordinary refusal erased actual draft.");
    await view.DispatchAsync("Save",null); var saved = view.SelectedItem ?? throw new Exception("Actual canonical provider commit missing.");
    Check((await route.PrepareToCloseAsync()).Succeeded,"Clean saved view could not close."); await route.DisposeAsync();
    var reopened = await new DurableDriveProvider(store.Path,store.Location,store.Actor).GetAsync(saved.Id,default);
    Check(reopened.IsSuccess && reopened.Value!.Id == saved.Id && reopened.Value.CurrentRevisionId == saved.CurrentRevisionId && reopened.Value.Name == "Canonical retained draft","Canonical durable reopen identity/revision/content changed.");
}
static async Task FilesHeldRead(string root)
{
    var store = Store(root); var blocked = new HeldActualFilesList(store.Provider); var route = new FilesBrowserRoute(blocked,store.Actor,Doc("Files"),MissingActualIssuer(),MissingActualDrain());
    var opening = route.OpenAsync(new(FilesBrowserRoute.Id)); await blocked.Entered.Task;
    try { route.RevokePrivateContext(); throw new Exception("Missing actual issuer fence accepted."); }
    catch (InvalidOperationException error) when (HasMissingOwner(error)) { }
    var draining = route.DisposeAsync().AsTask(); Check(!draining.IsCompleted,"Cancellation-ignoring actual read was abandoned.");
    blocked.Release.TrySetResult(); try { await opening; } catch (OperationCanceledException) { }
    try { await draining; throw new Exception("Unconfigured real issuer authority was accepted by drain."); }
    catch (InvalidOperationException error) when (HasMissingOwner(error)) { }
    Check(!route.HasUnsavedChanges,"Revoked route exposed private draft state.");
    Check(!(await route.OpenAsync(new(FilesBrowserRoute.Id))).Succeeded,"Revoked route readmitted owner work.");
}
static async Task SitesClose(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); var route = new SitesBrowserRoute(fixture.Operations,Doc("Sites"),MissingActualIssuer(),MissingActualDrain());
    var opened = await route.OpenAsync(new(SitesBrowserRoute.Id)); Check(opened.Succeeded,"Actual Sites route opening failed.");
    var surface = route.CreateSurface(opened.ViewState!); var view = (SitesBrowserController)surface.Bindings;
    await view.DispatchAsync("NewProject",null); Check(view.TrySetValue("Name","Canonical native website"),"Actual Sites draft unavailable.");
    Check(route.HasUnsavedChanges && !(await route.PrepareToCloseAsync()).Succeeded,"Dirty Sites ordinary close accepted.");
    await view.DispatchAsync("Save",null); var saved = view.CurrentProject ?? throw new Exception("Actual canonical native commit missing.");
    Check((await route.PrepareToCloseAsync()).Succeeded,"Saved Sites view close refused."); await route.DisposeAsync();
    var reopened = await fixture.Operations.Open(saved.SiteId,default);
    Check(reopened.IsSuccess && System.Text.Json.JsonSerializer.Serialize(reopened.Value) == System.Text.Json.JsonSerializer.Serialize(saved),"Canonical Sites fullgraph/source/revision restart changed.");
    // A second actual fixture reloads persisted OS/Home/store authority instead of relying on in-memory projection.
    var restarted = await NativeSitesFixture.CreateAsync(root,initialize:false); var reread = await restarted.Operations.Open(saved.SiteId,default);
    Check(reread.IsSuccess && System.Text.Json.JsonSerializer.Serialize(reread.Value) == System.Text.Json.JsonSerializer.Serialize(saved),"Reinitialized native owner did not recover exact graph.");
}
static async Task SitesHeldRead(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); var actual = fixture.Operations;
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var held = actual with { List = async ct => { entered.TrySetResult(); await release.Task; return await actual.List(default); } };
    var route = new SitesBrowserRoute(held,Doc("Sites"),MissingActualIssuer(),MissingActualDrain()); var opening = route.OpenAsync(new(SitesBrowserRoute.Id)); await entered.Task;
    try { route.RevokePrivateContext(); throw new Exception("Missing actual issuer fence accepted."); }
    catch (InvalidOperationException error) when (HasMissingOwner(error)) { }
    var draining = route.DisposeAsync().AsTask(); Check(!draining.IsCompleted,"Actual cancellation-ignoring native initialization was abandoned.");
    release.TrySetResult(); try { await opening; } catch (OperationCanceledException) { }
    try { await draining; throw new Exception("Missing real issuer accepted after native drain."); }
    catch (InvalidOperationException error) when (HasMissingOwner(error)) { }
    Check(!(await route.OpenAsync(new(SitesBrowserRoute.Id))).Succeeded,"Revoked Sites owner readmitted a private view.");
}
static async Task ExpectMissingOwnerDrain(Func<ValueTask> drain)
{
    try { await drain(); throw new Exception("Missing actual issuer drain accepted."); }
    catch (InvalidOperationException error) when (HasMissingOwner(error)) { }
}
static async Task FilesPrivateClear(string root)
{
    var store = Store(root); var now = DateTimeOffset.UtcNow;
    var committed = await store.Provider.MutateAsync(new(new(Guid.NewGuid()),store.Actor,HostedItemId.New(),null,null,
        "CreateFolder",null,null,FilesOperationState.Pending,now,now,null,null),"Private canonical folder",default);
    Check(committed.IsSuccess,"Actual provider setup commit denied.");
    var route = new FilesBrowserRoute(store.Provider,store.Actor,Doc("Files"),MissingActualIssuer(),MissingActualDrain());
    var result = await route.OpenAsync(new(FilesBrowserRoute.Id)); var surface = route.CreateSurface(result.ViewState!);
    var view = (FilesBrowserController)surface.Bindings; Check(view.Items.Single().Metadata.Id == committed.Value!.ItemId,"Actual canonical private item not loaded.");
    await view.DispatchAsync("NewFolder",null); view.TrySetValue("Name","Private typed draft");
    var bytes = await File.ReadAllBytesAsync(store.Path);
    try { route.RevokePrivateContext(); throw new Exception("Missing real group authority accepted."); }
    catch (InvalidOperationException error) when (HasMissingOwner(error)) { }
    Check(view.Items.Count == 0 && view.CurrentFolderId is null && view.SelectedItem is null &&
        view.TryGetValue("Name",out var draft) && draft as string == "" && view.IsActionAvailable("Save") == false,
        "Revocation failure retained populated private Files state.");
    await view.DispatchAsync("Save",null); var after = await File.ReadAllBytesAsync(store.Path);
    Check(bytes.SequenceEqual(after),"Revocation repeated or saved old private draft.");
    await ExpectMissingOwnerDrain(route.DisposeAsync);
}
static async Task SitesPrivateClear(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); var route = new SitesBrowserRoute(fixture.Operations,Doc("Sites"),MissingActualIssuer(),MissingActualDrain());
    var result = await route.OpenAsync(new(SitesBrowserRoute.Id)); var surface = route.CreateSurface(result.ViewState!);
    var view = (SitesBrowserController)surface.Bindings; await view.DispatchAsync("NewProject",null); view.TrySetValue("Name","Private canonical website"); await view.DispatchAsync("Save",null);
    var original = view.CurrentProject ?? throw new Exception("Actual canonical project missing.");
    await view.DispatchAsync("NewPage",null); view.TrySetValue("Name","Private page draft");
    var binding = await fixture.BindingAsync(); var path = Path.Combine(binding.RootDirectory,".9to1-sites-index.json"); var bytes = await File.ReadAllBytesAsync(path);
    try { route.RevokePrivateContext(); throw new Exception("Missing real group authority accepted."); }
    catch (InvalidOperationException error) when (HasMissingOwner(error)) { }
    Check(view.CurrentProject is null && view.CurrentPage is null && view.TryGetValue("Name",out var draft) && draft as string == "" && view.IsActionAvailable("Save") == false,
        "Revocation failure retained populated private Sites state.");
    await view.DispatchAsync("Save",null); var after = await File.ReadAllBytesAsync(path); Check(bytes.SequenceEqual(after),"Revocation saved old private draft.");
    var actual = await fixture.Operations.Open(original.SiteId,default);
    Check(actual.IsSuccess && actual.Value!.ProjectId == original.ProjectId && actual.Value.Source == original.Source && actual.Value.Revision == original.Revision,
        "Failure cleanup changed actual canonical source/identity/revision.");
    await ExpectMissingOwnerDrain(route.DisposeAsync);
}
static async Task FilesReentrantDrain(string root)
{
    var store = Store(root); FilesBrowserRoute? route = null; Task? reentrant = null; var calls = 0;
    Func<ValueTask> negativeDrain = () =>
    {
        calls++;
        if (calls == 1) reentrant = route!.DisposeAsync().AsTask();
        return ValueTask.FromException(new NotSupportedException("Negative fixture: real issuer drain missing; reentrant bookkeeping only."));
    };
    route = new(store.Provider,store.Actor,Doc("Files"),MissingActualIssuer(),negativeDrain);
    var opened = await route.OpenAsync(new(FilesBrowserRoute.Id)); Check(opened.Succeeded,"Actual Files initialization failed."); route.CreateSurface(opened.ViewState!);
    ExpectFenceFailure(route.RevokePrivateContext);
    var drain = route.DisposeAsync().AsTask(); await ExpectMissingOwnerDrain(() => new ValueTask(drain));
    Check(calls == 1 && ReferenceEquals(reentrant,drain),"Reentrant disposal issued another owner drain instead of returning the published task.");
}
static async Task SitesReentrantDrain(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); SitesBrowserRoute? route = null; Task? reentrant = null; var calls = 0;
    Func<ValueTask> negativeDrain = () =>
    {
        calls++;
        if (calls == 1) reentrant = route!.DisposeAsync().AsTask();
        return ValueTask.FromException(new NotSupportedException("Negative fixture: real issuer drain missing; reentrant bookkeeping only."));
    };
    route = new(fixture.Operations,Doc("Sites"),MissingActualIssuer(),negativeDrain);
    var opened = await route.OpenAsync(new(SitesBrowserRoute.Id)); Check(opened.Succeeded,"Actual native Sites initialization failed."); route.CreateSurface(opened.ViewState!);
    ExpectFenceFailure(route.RevokePrivateContext);
    var drain = route.DisposeAsync().AsTask(); await ExpectMissingOwnerDrain(() => new ValueTask(drain));
    Check(calls == 1 && ReferenceEquals(reentrant,drain),"Reentrant Sites disposal issued another owner drain instead of returning the published task.");
}
static void ExpectFenceFailure(Action revoke)
{
    try { revoke(); throw new Exception("Missing actual issuer fence was reported as successful."); }
    catch (InvalidOperationException error) when (HasMissingOwner(error)) { }
}
static async Task FilesStickyFence(string root)
{
    var store = Store(root); var calls = 0;
    var original = new NotSupportedException("Negative actual issuer fixture: no group fence available.");
    var route = new FilesBrowserRoute(store.Provider,store.Actor,Doc("Files"),() => { calls++; throw original; },MissingActualDrain());
    var opened = await route.OpenAsync(new(FilesBrowserRoute.Id)); Check(opened.Succeeded,"Actual Files initialization failed."); route.CreateSurface(opened.ViewState!);
    ExpectFenceFailure(route.RevokePrivateContext);
    try { route.RevokePrivateContext(); throw new Exception("Repeated failed fence appeared successful."); }
    catch (InvalidOperationException error) when (ReferenceEquals(error.InnerException,original)) { }
    Check(calls == 1,"Repeated revocation retried the unavailable actual owner fence.");
    await ExpectMissingOwnerDrain(route.DisposeAsync);
}
static async Task SitesStickyFence(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); var calls = 0;
    var original = new NotSupportedException("Negative actual issuer fixture: no group fence available.");
    var route = new SitesBrowserRoute(fixture.Operations,Doc("Sites"),() => { calls++; throw original; },MissingActualDrain());
    var opened = await route.OpenAsync(new(SitesBrowserRoute.Id)); Check(opened.Succeeded,"Actual native Sites initialization failed."); route.CreateSurface(opened.ViewState!);
    ExpectFenceFailure(route.RevokePrivateContext);
    try { route.RevokePrivateContext(); throw new Exception("Repeated failed Sites fence appeared successful."); }
    catch (InvalidOperationException error) when (ReferenceEquals(error.InnerException,original)) { }
    Check(calls == 1,"Repeated Sites revocation retried the unavailable actual owner fence.");
    await ExpectMissingOwnerDrain(route.DisposeAsync);
}
static async Task FilesHeldPreparation(string root)
{
    var store = Store(root); var held = new ArmedHeldActualFilesList(store.Provider);
    var route = new FilesBrowserRoute(held,store.Actor,Doc("Files"),MissingActualIssuer(),MissingActualDrain());
    var opened = await route.OpenAsync(new(FilesBrowserRoute.Id)); Check(opened.Succeeded,"Actual Files initialization failed.");
    var view = (FilesBrowserController)route.CreateSurface(opened.ViewState!).Bindings;
    held.Armed = true;
    await HeldPreparation(route,route,() => view.DispatchAsync("Refresh",null).AsTask(),held.Entered.Task,() => held.Release.TrySetResult());
}
static async Task SitesHeldPreparation(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); var actual = fixture.Operations; var armed = false;
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var held = actual with { List = async ct => { if (armed) { entered.TrySetResult(); await release.Task; } return await actual.List(default); } };
    var route = new SitesBrowserRoute(held,Doc("Sites"),MissingActualIssuer(),MissingActualDrain());
    var opened = await route.OpenAsync(new(SitesBrowserRoute.Id)); Check(opened.Succeeded,"Actual native Sites initialization failed.");
    var view = (SitesBrowserController)route.CreateSurface(opened.ViewState!).Bindings;
    armed = true;
    await HeldPreparation(route,route,() => view.DispatchAsync("Reload",null).AsTask(),entered.Task,() => release.TrySetResult());
}
static async Task HeldPreparation(IHomeFeatureRouteHandler route, NineToOne.Web.IBrowserPrivateContextParticipant participant,
    Func<Task> issueCommand, Task entered, Action release)
{
    var command = issueCommand(); Task<HomeFeatureNavigationResult>? opening = null; Task? draining = null;
    var context = new QueuedPreparationContext();
    try
    {
        await entered;
        var previous = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(context); opening = route.OpenAsync(new(route.RouteId)); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Check(!opening.IsCompleted,"Actual held preparation did not suspend the outward opening.");
        ExpectFenceFailure(participant.RevokePrivateContext);
        draining = participant.DisposeAsync().AsTask(); release(); await command;
        await context.Posted.Task;
        // Keep the outward continuation queued; allow an untracked original drain to settle.
        // This one-second negative observation is bounded and does not abandon any issued work.
        await Task.WhenAny(draining, Task.Delay(TimeSpan.FromSeconds(1)));
        Check(!draining.IsCompleted,"Drain completed while the actual outward opening's preparation continuation was still issued.");
        await context.PumpUntilAsync(opening);
        var result = await opening; Check(!result.Succeeded,"Revoked preparation issued a new private surface.");
        await ExpectMissingOwnerDrain(() => new ValueTask(draining));
    }
    finally
    {
        // Release and join actual held work even when an unchanged-original regression check fails.
        release(); await command;
        if (opening is not null) { await context.PumpUntilAsync(opening); await opening; }
        if (draining is not null) await ExpectMissingOwnerDrain(() => new ValueTask(draining));
    }
}
static async Task FilesFenceSettlement(string root)
{
    var store = Store(root); var now = DateTimeOffset.UtcNow;
    var committed = await store.Provider.MutateAsync(new(new(Guid.NewGuid()),store.Actor,HostedItemId.New(),null,null,
        "CreateFolder",null,null,FilesOperationState.Pending,now,now,null,null),"Canonical fence-settlement folder",default);
    Check(committed.IsSuccess,"Actual canonical provider setup failed.");
    FilesBrowserRoute? route = null; FilesBrowserController? view = null; Task? reentrantDrain = null;
    var fenceCalls = 0; var drainCalls = 0; var actionSettled = false; var pendingInsideAction = false;
    var repeatedPendingRefused = false; var clearedBeforeOwnerDrain = false;
    var original = new NotSupportedException("Negative fixture: real issuer group fence remains unavailable.");
    Action negativeFence = () =>
    {
        fenceCalls++;
        try
        {
            reentrantDrain = route!.DisposeAsync().AsTask();
            pendingInsideAction = !reentrantDrain.IsCompleted && drainCalls == 0;
            try { route.RevokePrivateContext(); }
            catch (InvalidOperationException error) when (error.Message.Contains("in progress",StringComparison.Ordinal)) { repeatedPendingRefused = true; }
        }
        finally { actionSettled = true; }
        throw original;
    };
    Func<ValueTask> negativeDrain = () =>
    {
        drainCalls++;
        clearedBeforeOwnerDrain = actionSettled && view!.Items.Count == 0 && view.CurrentFolderId is null && view.SelectedItem is null &&
            view.TryGetValue("Name",out var draft) && draft as string == "" && view.IsActionAvailable("Save") == false;
        return ValueTask.FromException(new NotSupportedException("Negative fixture: real issuer drain remains unavailable."));
    };
    route = new(store.Provider,store.Actor,Doc("Files"),negativeFence,negativeDrain);
    var opened = await route.OpenAsync(new(FilesBrowserRoute.Id)); Check(opened.Succeeded,"Actual canonical Files open failed.");
    view = (FilesBrowserController)route.CreateSurface(opened.ViewState!).Bindings;
    Check(view.Items.Single().Metadata.Id == committed.Value!.ItemId,"Actual canonical item identity changed.");
    await view.DispatchAsync("NewFolder",null); Check(view.TrySetValue("Name","Private in-flight fence draft"),"Actual draft setup failed.");
    var before = await File.ReadAllBytesAsync(store.Path);
    ExpectFenceFailure(route.RevokePrivateContext);
    var drain = route.DisposeAsync().AsTask(); await ExpectMissingOwnerDrain(() => new ValueTask(drain));
    Check(pendingInsideAction && repeatedPendingRefused && fenceCalls == 1 && drainCalls == 1 && ReferenceEquals(reentrantDrain,drain),
        "Owner-action reentry completed/reissued disposal or appeared revoked before the actual fence settled.");
    Check(clearedBeforeOwnerDrain,"Owner drain ran before actual Action settlement and complete private presentation clearing.");
    var after = await File.ReadAllBytesAsync(store.Path); Check(before.SequenceEqual(after),"Negative revocation changed actual durable canonical bytes.");
}
static async Task SitesFenceSettlement(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root);
    SitesBrowserRoute? route = null; SitesBrowserController? view = null; Task? reentrantDrain = null;
    var fenceCalls = 0; var drainCalls = 0; var actionSettled = false; var pendingInsideAction = false;
    var repeatedPendingRefused = false; var clearedBeforeOwnerDrain = false;
    var original = new NotSupportedException("Negative fixture: real native issuer group fence remains unavailable.");
    Action negativeFence = () =>
    {
        fenceCalls++;
        try
        {
            reentrantDrain = route!.DisposeAsync().AsTask();
            pendingInsideAction = !reentrantDrain.IsCompleted && drainCalls == 0;
            try { route.RevokePrivateContext(); }
            catch (InvalidOperationException error) when (error.Message.Contains("in progress",StringComparison.Ordinal)) { repeatedPendingRefused = true; }
        }
        finally { actionSettled = true; }
        throw original;
    };
    Func<ValueTask> negativeDrain = () =>
    {
        drainCalls++;
        clearedBeforeOwnerDrain = actionSettled && view!.CurrentProject is null && view.CurrentPage is null &&
            view.TryGetValue("Name",out var draft) && draft as string == "" && view.IsActionAvailable("Save") == false;
        return ValueTask.FromException(new NotSupportedException("Negative fixture: real native issuer drain remains unavailable."));
    };
    route = new(fixture.Operations,Doc("Sites"),negativeFence,negativeDrain);
    var opened = await route.OpenAsync(new(SitesBrowserRoute.Id)); Check(opened.Succeeded,"Actual native canonical Sites open failed.");
    view = (SitesBrowserController)route.CreateSurface(opened.ViewState!).Bindings;
    await view.DispatchAsync("NewProject",null); view.TrySetValue("Name","Canonical fence-settlement website"); await view.DispatchAsync("Save",null);
    var canonical = view.CurrentProject ?? throw new Exception("Actual canonical native save missing.");
    await view.DispatchAsync("NewPage",null); Check(view.TrySetValue("Name","Private in-flight fence page draft"),"Actual native draft setup failed.");
    var binding = await fixture.BindingAsync(); var path = Path.Combine(binding.RootDirectory,".9to1-sites-index.json");
    var before = await File.ReadAllBytesAsync(path);
    ExpectFenceFailure(route.RevokePrivateContext);
    var drain = route.DisposeAsync().AsTask(); await ExpectMissingOwnerDrain(() => new ValueTask(drain));
    Check(pendingInsideAction && repeatedPendingRefused && fenceCalls == 1 && drainCalls == 1 && ReferenceEquals(reentrantDrain,drain),
        "Native owner-action reentry completed/reissued disposal or appeared revoked before the actual fence settled.");
    Check(clearedBeforeOwnerDrain,"Native owner drain ran before actual Action settlement and complete private presentation clearing.");
    var after = await File.ReadAllBytesAsync(path); Check(before.SequenceEqual(after),"Negative native revocation changed actual canonical bytes.");
    var actual = await fixture.Operations.Open(canonical.SiteId,default);
    Check(actual.IsSuccess && actual.Value!.ProjectId == canonical.ProjectId && actual.Value.Source == canonical.Source && actual.Value.Revision == canonical.Revision,
        "Fence settlement changed real native identity/source/revision.");
}
sealed class QueuedPreparationContext : SynchronizationContext
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback,object? State)> _queue = new();
    public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override void Post(SendOrPostCallback callback, object? state) { _queue.Enqueue((callback,state)); Posted.TrySetResult(); }
    public async Task PumpUntilAsync(Task completion)
    {
        while (!completion.IsCompleted)
        {
            if (_queue.TryDequeue(out var work))
            {
                var previous = Current;
                try { SetSynchronizationContext(this); work.Callback(work.State); }
                finally { SetSynchronizationContext(previous); }
            }
            else await Task.Yield();
        }
    }
}
sealed class ArmedHeldActualFilesList(IFilesProvider actual) : IFilesProvider
{
    public bool Armed { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public FilesLocation Location => actual.Location;
    public async Task<FilesPage<HostedItemMetadata>> ListAsync(HostedItemId? parent,FilesSearchQuery? query,string? page,CancellationToken ct)
    { if (Armed) { Entered.TrySetResult(); await Release.Task; } return await actual.ListAsync(parent,query,page,default); }
    public Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId id,CancellationToken ct) => actual.GetAsync(id,ct);
    public Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation op,string? name,CancellationToken ct) => actual.MutateAsync(op,name,ct);
    public IAsyncEnumerable<FilesChangeEvent> SubscribeAsync(FilesChangeCursor? after,CancellationToken ct) => actual.SubscribeAsync(after,ct);
    public Task<FilesPage<FilesChangeEvent>> GetChangesAsync(FilesChangeCursor? after,int limit,CancellationToken ct) => actual.GetChangesAsync(after,limit,ct);
}
sealed class HeldActualFilesList(IFilesProvider actual) : IFilesProvider
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public FilesLocation Location => actual.Location;
    public async Task<FilesPage<HostedItemMetadata>> ListAsync(HostedItemId? parent,FilesSearchQuery? query,string? page,CancellationToken ct)
    { Entered.TrySetResult(); await Release.Task; return await actual.ListAsync(parent,query,page,default); }
    public Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId id,CancellationToken ct) => actual.GetAsync(id,ct);
    public Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation op,string? name,CancellationToken ct) => actual.MutateAsync(op,name,ct);
    public IAsyncEnumerable<FilesChangeEvent> SubscribeAsync(FilesChangeCursor? after,CancellationToken ct) => actual.SubscribeAsync(after,ct);
    public Task<FilesPage<FilesChangeEvent>> GetChangesAsync(FilesChangeCursor? after,int limit,CancellationToken ct) => actual.GetChangesAsync(after,limit,ct);
}
