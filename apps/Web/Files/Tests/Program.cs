using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Files;
using HavenOS.Home.Core;
using NineToOne.Web.Files;

var dataRoot = args.Length == 1 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Pass an isolated evidence data directory.");
Directory.CreateDirectory(dataRoot);
var tests = new (string Name, Func<string, Task> Run)[]
{
    ("canonical-create-close-reopen-rename-open-child", Lifecycle),
    ("revision-conflict-retains-proposed-name-and-identity", Conflict),
    ("name-conflict-retains-draft-without-second-item", NameConflict),
    ("owner-denial-does-not-create", Denied),
    ("lost-commit-reply-retry-preserves-operation-key", LostReply),
    ("typed-uncertain-reply-retry-preserves-operation-key", TypedLostReply),
    ("foreign-committed-receipt-is-not-accepted", ForeignReceipt),
    ("actual-provider-mutation-denial-clears-private-draft", MutationDenialPrivacy),
    ("typed-read-denial-clears-private-folder", ReadDenialPrivacy),
    ("thrown-access-denial-clears-private-folder-and-draft", ThrownDenialPrivacy),
    ("throwing-presentation-listener-cannot-lock-real-actions", PresentationFailure),
    ("selection-and-deleted-folder-boundaries", SelectionBoundary),
    ("dispose-clears-private-view-and-disables-actions", DisposeBoundary),
    ("cancelled-open-does-not-return-route-state", CancelledRoute),
    ("invalid-and-nonfolder-deep-links-denied", InvalidRoute),
    ("actual-cui-parser-and-consumable-owner-route", ActualRoute),
    ("pagination-is-bounded-and-preserves-canonical-items", Pagination),
};
Console.WriteLine($"EXPECTED={tests.Length}; backend=DurableDriveProvider; browser=NOT_RUN; HTTP=NOT_RUN");
var passed = 0;
foreach (var test in tests)
{
    try { await test.Run(Path.Combine(dataRoot, test.Name)); passed++; Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { Console.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"RESULT expected={tests.Length} executed={tests.Length} passed={passed} failed={tests.Length - passed}");
return passed == tests.Length ? 0 : 1;

static (DurableDriveProvider Provider, string Owner, FilesLocationId Location, string Path) Store(string directory)
{
    Directory.CreateDirectory(directory);
    var owner = "local-provider-test-owner";
    var location = new FilesLocationId(Guid.NewGuid());
    var path = Path.Combine(directory, "canonical-files.json");
    return (new(path, location, owner), owner, location, path);
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static object? Value(FilesBrowserController controller, string path)
{ Check(controller.TryGetValue(path, out var value), $"Unknown binding {path}"); return value; }
static async Task<HostedItemMetadata> Create(FilesBrowserController controller, string name)
{
    await controller.DispatchAsync("NewFolder", null);
    Check(controller.TrySetValue("Name", name), "Name input was not writable.");
    await controller.DispatchAsync("Save", null);
    Check(Value(controller, "Status") as string == "Saved.", $"Create failed: {Value(controller, "Status")}");
    return controller.SelectedItem ?? throw new Exception("Created item was not selected.");
}
static async Task Lifecycle(string directory)
{
    var s = Store(directory);
    HostedItemMetadata original;
    using (var controller = new FilesBrowserController(s.Provider, s.Owner))
    { await controller.InitializeAsync(null, default); original = await Create(controller, "Research"); }
    Check(File.Exists(s.Path), "No durable state file.");
    var reopened = new DurableDriveProvider(s.Path, s.Location, s.Owner);
    using var view = new FilesBrowserController(reopened, s.Owner);
    await view.InitializeAsync(null, default);
    await view.DispatchAsync("Select", view.Items.Single());
    Check(view.SelectedItem is { } selected && selected.Id == original.Id && selected.CurrentRevisionId == original.CurrentRevisionId, "Reopen changed source identity/revision.");
    await view.DispatchAsync("Rename", null); view.TrySetValue("Name", "Research notes"); await view.DispatchAsync("Save", null);
    Check(view.SelectedItem is { } renamed && renamed.Id == original.Id && renamed.CurrentRevisionId != original.CurrentRevisionId, "Rename did not preserve identity/advance revision.");
    await view.DispatchAsync("Open", null);
    Check(view.CurrentFolderId == original.Id, "Folder did not open by canonical identity.");
    var child = await Create(view, "Sources");
    Check(child.ParentId == original.Id, "Child parent was not canonical folder.");
    await view.DispatchAsync("Rename", null); view.TrySetValue("Name", "Source notes"); await view.DispatchAsync("Save", null);
    Check(view.SelectedItem is { } nested && nested.Id == child.Id && nested.ParentId == original.Id && nested.CurrentRevisionId != child.CurrentRevisionId,
        "Nested rename moved or changed canonical identity instead of renaming.");
    var nestedRename = (await reopened.GetChangesAsync(null, 100, default)).Items.Last();
    Check(nestedRename.Kind == "Rename" && nestedRename.Metadata?.ParentId == original.Id, "Nested rename journal did not preserve physical parent.");
    await view.DispatchAsync("Up", null);
    Check(view.CurrentFolderId is null, "Up did not return to provider root.");
    var persisted = await new DurableDriveProvider(s.Path, s.Location, s.Owner).GetAsync(original.Id, default);
    Check(persisted.Value?.Name == "Research notes", "Renamed name was not durable after reopening engine.");
    var changes = await reopened.GetChangesAsync(null, 100, default);
    Check(changes.Items.Count == 4 && changes.Items.Select(item => item.OperationId).Distinct().Count() == 4, "Structural journal did not commit once per operation.");
}
static async Task Conflict(string directory)
{
    var s = Store(directory);
    using var view = new FilesBrowserController(s.Provider, s.Owner); await view.InitializeAsync(null, default);
    var item = await Create(view, "Original");
    await view.DispatchAsync("Rename", null); view.TrySetValue("Name", "My retained proposal");
    var now = DateTimeOffset.UtcNow;
    var external = await new DurableDriveProvider(s.Path, s.Location, s.Owner).MutateAsync(new(new(Guid.NewGuid()), s.Owner,
        item.Id, null, null, "Rename", item.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null), "External change", default);
    Check(external.IsSuccess, "Concurrent real provider mutation failed.");
    await view.DispatchAsync("Save", null);
    Check(Value(view, "Name") as string == "My retained proposal" && Value(view, "Status") is string status && status.Contains("item changed"), "Conflict discarded draft or was hidden.");
    Check((await s.Provider.GetAsync(item.Id, default)).Value?.Name == "External change", "Conflict overwrote owner state.");
    await view.DispatchAsync("Cancel", null); await view.DispatchAsync("Refresh", null); await view.DispatchAsync("Rename", null);
    view.TrySetValue("Name", "Resolved proposal"); await view.DispatchAsync("Save", null);
    Check(view.SelectedItem is { } resolved && resolved.Id == item.Id && resolved.Name == "Resolved proposal", "Explicit reload failed to resolve revision conflict.");
}
static async Task NameConflict(string directory)
{
    var s = Store(directory); using var view = new FilesBrowserController(s.Provider, s.Owner); await view.InitializeAsync(null, default);
    await Create(view, "Existing"); await view.DispatchAsync("NewFolder", null); view.TrySetValue("Name", "Existing"); await view.DispatchAsync("Save", null);
    Check(Value(view, "Name") as string == "Existing" && Value(view, "Status") is string status && status.StartsWith("NameConflict"), "Name conflict discarded draft.");
    Check((await s.Provider.ListAsync(null, null, null, default)).Items.Count == 1, "Conflict created duplicate item.");
}
static async Task Denied(string directory)
{
    var s = Store(directory); using var view = new FilesBrowserController(s.Provider, "different-test-principal"); await view.InitializeAsync(null, default);
    await view.DispatchAsync("NewFolder", null); view.TrySetValue("Name", "Forbidden"); await view.DispatchAsync("Save", null);
    Check(Value(view, "Status") is string status && status.Contains("access was denied"), "Provider permission denial hidden.");
    Check((await s.Provider.ListAsync(null, null, null, default)).Items.Count == 0, "Denied actor wrote state.");
}
static async Task LostReply(string directory)
{
    var s = Store(directory); using var view = new FilesBrowserController(new LoseFirstMutationReply(s.Provider), s.Owner); await view.InitializeAsync(null, default);
    await view.DispatchAsync("NewFolder", null); view.TrySetValue("Name", "Committed once"); await view.DispatchAsync("Save", null);
    Check(Value(view, "Status") is string status && status.Contains("outcome is unknown"), "Unknown result reported saved.");
    Check(!view.TrySetValue("Name", "Different operation") && view.IsActionAvailable("Cancel") == false, "Uncertain operation key could be reused with changed intent.");
    await view.DispatchAsync("Save", null);
    Check(Value(view, "Status") as string == "Saved.", "Idempotent retry did not recover committed result.");
    Check((await s.Provider.GetChangesAsync(null, 100, default)).Items.Count == 1, "Retry produced duplicate commit.");
}
static async Task TypedLostReply(string directory)
{
    var s = Store(directory); using var view = new FilesBrowserController(new TypedLostMutationReply(s.Provider), s.Owner);
    await view.InitializeAsync(null, default); await view.DispatchAsync("NewFolder", null); view.TrySetValue("Name", "Typed uncertain commit");
    await view.DispatchAsync("Save", null);
    Check(Value(view, "Status") is string status && status.Contains("outcome is unknown") && !view.TrySetValue("Name", "Different intent"), "Typed uncertain result discarded its original intent.");
    await view.DispatchAsync("Save", null);
    Check(Value(view, "Status") as string == "Saved." && (await s.Provider.GetChangesAsync(null, 100, default)).Items.Count == 1,
        "Typed lost reply duplicated or failed to recover the real commit.");
}
static async Task ForeignReceipt(string directory)
{
    foreach (var variant in new[] { "operationID", "itemID", "operation", "pending", "nullRevision", "emptyRevision", "actor", "intentName", "contradictoryError" })
    {
        var s = Store(Path.Combine(directory, variant)); using var view = new FilesBrowserController(new ForeignMutationReceipt(s.Provider, variant), s.Owner);
        await view.InitializeAsync(null, default); await view.DispatchAsync("NewFolder", null); view.TrySetValue("Name", "Original intent");
        await view.DispatchAsync("Save", null);
        Check(Value(view, "Status") is string status && status.Contains("outcome is unknown") && view.SelectedItem is null && !view.TrySetValue("Name", "Changed"), "Foreign receipt was accepted or unlocked its intent: " + variant);
        await view.DispatchAsync("Save", null);
        Check(Value(view, "Status") as string == "Saved." && (await s.Provider.GetChangesAsync(null, 100, default)).Items.Count == 1, "Foreign receipt retry did not recover original real commit: " + variant);
        Console.WriteLine("INJECTED_RECEIPT_FAILURE variant=" + variant + " recovered original single commit");
    }
}
static void CheckPrivateCleared(FilesBrowserController view)
{
    Check(view.Items.Count == 0 && view.SelectedItem is null && view.CurrentFolderId is null && Value(view, "Name") as string == ""
        && Value(view, "FolderName") as string == "Files" && Value(view, "LocationName") as string == "Files"
        && view.IsActionAvailable("Save") == false && view.IsActionAvailable("NewFolder") == false && view.IsActionAvailable("Refresh") == false,
        "Denied private folder/draft/metadata remained exposed or writable.");
}
static async Task MutationDenialPrivacy(string directory)
{
    var s = Store(directory); var adapter = new RevokedProvider(s.Provider); using var view = new FilesBrowserController(adapter, s.Owner);
    await view.InitializeAsync(null, default); await Create(view, "Private folder"); await view.DispatchAsync("Open", null);
    await Create(view, "Private child"); await view.DispatchAsync("Rename", null); view.TrySetValue("Name", "Private draft");
    adapter.DenyMutation = true; await view.DispatchAsync("Save", null); CheckPrivateCleared(view);
    Check((await s.Provider.GetChangesAsync(null, 100, default)).Items.Count == 2, "Denied mutation committed a revision.");
}
static async Task ReadDenialPrivacy(string directory)
{
    var s = Store(directory); var adapter = new RevokedProvider(s.Provider); using var view = new FilesBrowserController(adapter, s.Owner);
    await view.InitializeAsync(null, default); await Create(view, "Private folder"); await view.DispatchAsync("Open", null);
    await Create(view, "Private child"); adapter.DenyGet = true; await view.DispatchAsync("Refresh", null); CheckPrivateCleared(view);
}
static async Task ThrownDenialPrivacy(string directory)
{
    var s = Store(directory); var adapter = new RevokedProvider(s.Provider); using var view = new FilesBrowserController(adapter, s.Owner);
    await view.InitializeAsync(null, default); await Create(view, "Private folder"); await view.DispatchAsync("Open", null);
    await Create(view, "Private child"); await view.DispatchAsync("Rename", null); view.TrySetValue("Name", "Private draft");
    adapter.ThrowMutation = true; await view.DispatchAsync("Save", null); CheckPrivateCleared(view);
}
static async Task PresentationFailure(string directory)
{
    var s = Store(directory); using var view = new FilesBrowserController(s.Provider, s.Owner); await view.InitializeAsync(null, default);
    var calls = 0;
    view.PropertyChanged += (_, _) => { if (++calls == 1) throw new InvalidOperationException("Induced throwing presentation listener."); };
    await view.DispatchAsync("Refresh", null);
    Console.WriteLine("INJECTED_PRESENTATION_FAILURE first notification isolated.");
    Check(view.IsActionAvailable("NewFolder") == true, "Throwing listener permanently locked controller busy.");
    var created = await Create(view, "After listener failure");
    Check((await s.Provider.GetAsync(created.Id, default)).IsSuccess, "Real subsequent command did not commit.");
    var postCommitFailures = 0; var laterObserverCalls = 0;
    view.PropertyChanged += (_, _) => { if (Value(view, "Status") as string == "Saved.") { postCommitFailures++; throw new InvalidOperationException("Induced postcommit observer failure."); } };
    view.PropertyChanged += (_, _) => laterObserverCalls++;
    var next = await Create(view, "After durable commit observer failure");
    Check(postCommitFailures > 0 && laterObserverCalls > 0 && Value(view, "Status") as string == "Saved.", "Postcommit observer altered known success or suppressed later observers.");
    await view.DispatchAsync("Save", null);
    var changes = await s.Provider.GetChangesAsync(null, 100, default);
    Check(changes.Items.Count == 2 && (await s.Provider.GetAsync(next.Id, default)).IsSuccess, "Presentation failure replayed actual durable write.");
    await view.DispatchAsync("Refresh", null); Check(view.IsActionAvailable("NewFolder") == true, "Postcommit observer locked subsequent commands.");
}
static async Task SelectionBoundary(string directory)
{
    var s = Store(directory); using var view = new FilesBrowserController(s.Provider, s.Owner); await view.InitializeAsync(null, default);
    var item = await Create(view, "Outside root"); await view.DispatchAsync("Open", null);
    await view.DispatchAsync("Select", new FilesBrowserRow(item)); Check(view.SelectedItem is null, "Off-page row selected.");
    var now = DateTimeOffset.UtcNow;
    Check((await s.Provider.MutateAsync(new(new(Guid.NewGuid()), s.Owner, item.Id, null, null, "Delete", item.CurrentRevisionId,
        null, FilesOperationState.Pending, now, now, null, null), null, default)).IsSuccess, "External folder deletion failed.");
    await view.DispatchAsync("Refresh", null);
    Check(view.Items.Count == 0 && view.IsActionAvailable("NewFolder") == false && Value(view, "Status") is string status && status.StartsWith("ItemNotFound"),
        "Deleted open folder was silently presented as a writable empty folder.");
    await view.DispatchAsync("Up", null); Check(view.CurrentFolderId is null && view.IsActionAvailable("NewFolder") == true, "Up did not recover a deleted folder view.");
}
static async Task DisposeBoundary(string directory)
{
    var s = Store(directory); var view = new FilesBrowserController(s.Provider, s.Owner); await view.InitializeAsync(null, default); await Create(view, "Private");
    view.Dispose(); Check(view.Items.Count == 0 && view.SelectedItem is null && Value(view, "Name") as string == "", "Private view cache retained.");
    Check(view.IsActionAvailable("NewFolder") == false && !view.TrySetValue("Name", "Late write"), "Disposed controller accepted writes.");
    await view.DispatchAsync("Refresh", null); Check(view.Items.Count == 0, "Late dispatch repopulated cache.");
    var adapter = new ThrowingCancellationProvider(s.Provider); using var inFlightView = new FilesBrowserController(adapter, s.Owner);
    await inFlightView.InitializeAsync(null, default); await inFlightView.DispatchAsync("NewFolder", null); inFlightView.TrySetValue("Name", "Private in-flight draft");
    var inFlight = inFlightView.DispatchAsync("Save", null).AsTask(); await adapter.Committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    inFlightView.Dispose(); await inFlight.WaitAsync(TimeSpan.FromSeconds(10));
    Check(inFlightView.Items.Count == 0 && inFlightView.SelectedItem is null && Value(inFlightView, "Name") as string == "" && Value(inFlightView, "Editing") is false && inFlightView.IsActionAvailable("NewFolder") == false,
        "Throwing owner cancellation callback prevented private teardown.");
    Check(adapter.CancellationCalls == 1 && (await s.Provider.GetChangesAsync(null, 100, default)).Items.Count == 2, "Throwing owner cancellation control did not run after one real durable save.");
    Console.WriteLine("INJECTED_OWNER_CANCEL_FAILURE actual durable commit retained; private teardown completed.");
}
static CuiDocument Document()
{
    var parser = new CuiRichParser(); var document = parser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Files.cui")), "Files.cui");
    Check(!parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error),
        "Actual production markup has parser diagnostics: " + string.Join("; ", parser.Diagnostics.Diagnostics)); return document;
}
static async Task CancelledRoute(string directory)
{
    var s = Store(directory); using var route = new FilesBrowserRoute(s.Provider, s.Owner, Document());
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await route.OpenAsync(new(FilesBrowserRoute.Id), cancelled.Token); throw new Exception("Cancelled open returned a view."); }
    catch (OperationCanceledException) { }
    using var pendingCancellation = new CancellationTokenSource();
    var pending = await route.OpenAsync(new(FilesBrowserRoute.Id), pendingCancellation.Token);
    Check(pending.Succeeded && pending.ViewState is not null, "Pending cancellation fixture did not open.");
    pendingCancellation.Cancel();
    try { route.CreateSurface(pending.ViewState!); throw new Exception("Cancelled pending private surface remained renderable."); } catch (InvalidOperationException) { }
    using var consumedCancellation = new CancellationTokenSource();
    var consumed = await route.OpenAsync(new(FilesBrowserRoute.Id), consumedCancellation.Token);
    var surface = route.CreateSurface(consumed.ViewState!); using var lifetime = surface.Lifetime;
    consumedCancellation.Cancel();
    Check(surface.Actions is FilesBrowserController live && live.IsActionAvailable("NewFolder") == true, "Consumed lease retained navigation cancellation.");
}
static async Task InvalidRoute(string directory)
{
    var s = Store(directory); using var route = new FilesBrowserRoute(s.Provider, s.Owner, Document());
    Check(!(await route.OpenAsync(new(FilesBrowserRoute.Id, "project", Guid.NewGuid().ToString()))).Succeeded, "Unsupported semantic Project substituted a folder.");
    Check(!(await route.OpenAsync(new(FilesBrowserRoute.Id, "folder", Guid.NewGuid().ToString()))).Succeeded, "Missing folder accepted.");
    Check(!(await route.OpenAsync(new(FilesBrowserRoute.Id, Action: "upload"))).Succeeded, "Unsupported byte transfer accepted.");
    foreach (var request in new[] { new HomeFeatureNavigationRequest(FilesBrowserRoute.Id, DeepLink: "?private"), new(FilesBrowserRoute.Id, ModelPickerTarget: new("files", null, "project", null)) })
    { var result = await route.OpenAsync(request); Check(!result.Succeeded && result.Request == request, "Unsupported context ignored or replaced."); }
}
static async Task ActualRoute(string directory)
{
    var s = Store(directory); using var route = new FilesBrowserRoute(s.Provider, s.Owner, Document());
    var result = await route.OpenAsync(new(FilesBrowserRoute.Id)); Check(result.Succeeded && result.ViewState is not null, "Owner route did not open.");
    var surface = route.CreateSurface(result.ViewState!); using var lifetime = surface.Lifetime;
    Check(surface.Bindings is FilesBrowserController && surface.Actions is FilesBrowserController, "Route did not yield real CUI controller.");
    await Create((FilesBrowserController)surface.Bindings, "Via actual route");
    try { route.CreateSurface(result.ViewState!); throw new Exception("Consumed private surface was reusable."); }
    catch (InvalidOperationException) { }
}
static async Task Pagination(string directory)
{
    var s = Store(directory);
    for (var i = 0; i < 105; i++)
    {
        var now = DateTimeOffset.UtcNow;
        Check((await s.Provider.MutateAsync(new(new(Guid.NewGuid()), s.Owner, HostedItemId.New(), null, null, "CreateFolder", null,
            null, FilesOperationState.Pending, now, now, null, null), $"Folder {i:D3}", default)).IsSuccess, "Pagination data commit failed.");
    }
    using var view = new FilesBrowserController(s.Provider, s.Owner); await view.InitializeAsync(null, default);
    Check(view.Items.Count == 100 && view.IsActionAvailable("More") == true, "Initial canonical page not honoured.");
    await view.DispatchAsync("More", null); Check(view.Items.Count == 105 && view.Items.Select(row => row.Metadata.Id).Distinct().Count() == 105,
        "Pagination lost or duplicated canonical identities.");
    Check(view.IsActionAvailable("More") == false, "End-of-page not respected.");
}

// Fault injection wraps the real durable engine; it discards one successful reply after disk commit.
// It does not replace storage or supply a synthetic success result.
sealed class LoseFirstMutationReply(IFilesProvider real) : IFilesProvider
{
    private bool _lost;
    public FilesLocation Location => real.Location;
    public Task<FilesPage<HostedItemMetadata>> ListAsync(HostedItemId? parentId, FilesSearchQuery? query, string? pageToken, CancellationToken ct) => real.ListAsync(parentId, query, pageToken, ct);
    public Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId itemId, CancellationToken ct) => real.GetAsync(itemId, ct);
    public async Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? name, CancellationToken ct)
    { var result = await real.MutateAsync(operation, name, ct); if (!_lost && result.IsSuccess) { _lost = true; throw new IOException("Induced lost reply after real durable commit."); } return result; }
    public IAsyncEnumerable<FilesChangeEvent> SubscribeAsync(FilesChangeCursor? after, CancellationToken ct) => real.SubscribeAsync(after, ct);
    public Task<FilesPage<FilesChangeEvent>> GetChangesAsync(FilesChangeCursor? after, int limit, CancellationToken ct) => real.GetChangesAsync(after, limit, ct);
}

abstract class RealProviderAdapter(IFilesProvider real) : IFilesProvider
{
    protected IFilesProvider Real => real;
    public FilesLocation Location => real.Location;
    public virtual Task<FilesPage<HostedItemMetadata>> ListAsync(HostedItemId? parentId, FilesSearchQuery? query, string? pageToken, CancellationToken ct) => real.ListAsync(parentId, query, pageToken, ct);
    public virtual Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId itemId, CancellationToken ct) => real.GetAsync(itemId, ct);
    public virtual Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? name, CancellationToken ct) => real.MutateAsync(operation, name, ct);
    public IAsyncEnumerable<FilesChangeEvent> SubscribeAsync(FilesChangeCursor? after, CancellationToken ct) => real.SubscribeAsync(after, ct);
    public Task<FilesPage<FilesChangeEvent>> GetChangesAsync(FilesChangeCursor? after, int limit, CancellationToken ct) => real.GetChangesAsync(after, limit, ct);
}
sealed class TypedLostMutationReply(IFilesProvider real) : RealProviderAdapter(real)
{
    private bool _lost;
    public override async Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? name, CancellationToken ct)
    {
        var result = await base.MutateAsync(operation, name, ct);
        if (!_lost && result.IsSuccess) { _lost = true; return FilesResult<FilesOperation>.Failure(new(FilesErrorCode.ProviderUnavailable,
            "Induced typed unavailable reply after actual durable commit.", operation.Operation, operation.ItemId.ToString(), true, true)); }
        return result;
    }
}
sealed class ForeignMutationReceipt(IFilesProvider real, string variant) : RealProviderAdapter(real)
{
    private bool _changed;
    public override async Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? name, CancellationToken ct)
    {
        var result = await base.MutateAsync(operation, name, ct);
        if (!_changed && result.IsSuccess && result.Value is { } committed)
        {
            _changed = true;
            return FilesResult<FilesOperation>.Success(variant switch
            {
                "operationID" => committed with { Id = new(Guid.NewGuid()) },
                "itemID" => committed with { ItemId = HostedItemId.New() },
                "operation" => committed with { Operation = "Rename" },
                "pending" => committed with { State = FilesOperationState.Pending },
                "nullRevision" => committed with { ResultRevisionId = null },
                "emptyRevision" => committed with { ResultRevisionId = new(Guid.Empty) },
                "actor" => committed with { ActorId = "negative-foreign-receipt-principal" },
                "intentName" => committed with { Payload = committed.Payload! with { NewName = "Negative altered receipt intent" } },
                "contradictoryError" => committed with { Error = new(FilesErrorCode.ProviderUnavailable, "Induced contradictory committed operation reply.", committed.Operation, committed.ItemId.ToString(), true, true) },
                _ => throw new InvalidOperationException("Unknown negative receipt control.")
            });
        }
        return result;
    }
}
sealed class RevokedProvider(IFilesProvider real) : RealProviderAdapter(real)
{
    public bool DenyMutation { get; set; }
    public bool DenyGet { get; set; }
    public bool ThrowMutation { get; set; }
    public override Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? name, CancellationToken ct)
    {
        if (ThrowMutation) throw new UnauthorizedAccessException("Induced current access loss.");
        // Actual owner engine supplies the mutation denial for an actor that does not own this Drive.
        return base.MutateAsync(DenyMutation ? operation with { ActorId = "revoked-test-principal" } : operation, name, ct);
    }
    public override Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId itemId, CancellationToken ct) => DenyGet
        ? Task.FromResult(FilesResult<HostedItemMetadata>.Failure(new(FilesErrorCode.PermissionDenied, "Induced current read-access loss.", "Get", itemId.ToString(), false, false)))
        : base.GetAsync(itemId, ct);
}

sealed class ThrowingCancellationProvider(IFilesProvider real) : RealProviderAdapter(real)
{
    public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int CancellationCalls { get; private set; }
    public override async Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? name, CancellationToken ct)
    {
        var result = await base.MutateAsync(operation, name, ct);
        if (!result.IsSuccess) return result;
        using var registration = ct.Register(() => { CancellationCalls++; throw new InvalidOperationException("Induced owner cancellation callback failure."); });
        Committed.SetResult(); await Task.Delay(Timeout.Infinite, ct); return result;
    }
}
