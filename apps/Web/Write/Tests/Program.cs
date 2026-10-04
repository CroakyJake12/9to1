using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using NineToOne.Web.Write;

// Run seed and verify in separate processes with the same explicit isolated HAVEN_DATA_DIR.
// Production repository, diagnostics, validator, codec and editor are linked unchanged.
if (args.Length != 1 || args[0] is not ("seed" or "verify" or "subscriber") ||
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")))
    throw new ArgumentException("Choose seed or verify and set an isolated HAVEN_DATA_DIR.");
var paths = new AppPaths();
var diagnostics = new ProductionDiagnostics(paths);
var repository = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
var packages = new WriteNativeDocumentPackageStore();
var statePath = Path.Combine(paths.DataDirectory, "browser-write-test-state.json");
var packagePath = Path.Combine(paths.DataDirectory, "browser-write.9to1w");
var assertions = 0;
void Check(bool condition, string assertion)
{
    if (!condition) throw new InvalidOperationException(assertion);
    ++assertions;
    Console.WriteLine("ASSERT " + assertion);
}
NotesBlock First(NotesDocument document) => document.Sections.SelectMany(section => section.Pages)
    .SelectMany(page => page.Blocks).First();
string Text(NotesDocument document) => string.Concat(First(document).Runs.Select(run => run.Text));
void AssertFormatting(NotesDocument document)
{
    var runs = First(document).Runs;
    Check(Text(document) == "Alpha beta gamma", "exact document text");
    Check(runs.Count == 3 && runs[0].Text == "Alpha " && runs[1].Text == "beta" && runs[2].Text == " gamma",
        "range formatting preserves three text runs");
    Check(!runs[0].Bold && runs[1].Bold && !runs[2].Bold,
        "only beta is bold; adjacent text remains unchanged");
}

if (args[0] == "subscriber")
{
    var session = new WriteBrowserSession(repository, packages);
    var sentinel = new ApplicationException("Intentional presentation subscriber failure");
    System.ComponentModel.PropertyChangedEventHandler listener = (_, _) =>
    {
        if (!session.IsBusy) throw sentinel;
    };
    session.PropertyChanged += listener;
    var observed = false;
    try { await session.CreateAsync("Subscriber failure fixture"); }
    catch (ApplicationException failure) when (ReferenceEquals(failure, sentinel)) { observed = true; }
    finally { session.PropertyChanged -= listener; }
    Check(observed && session.DocumentId is not null && !session.IsDirty,
        "presentation listener error is visible after real durable create");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    Check((await session.CloseAsync(timeout.Token)).Succeeded,
        "subscriber exception cannot strand command gate; subsequent operation completes");
    await session.DisposeAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { mode = args[0], assertions, scope = "actual local durable owner/session" }));
    return;
}

if (args[0] == "seed")
{
    Check(!File.Exists(statePath), "fixture is fresh; never overwrite another test run");
    var session = new WriteBrowserSession(repository, packages);
    var created = await session.CreateAsync("Browser durable journey");
    Check(created.Succeeded && created.Value != Guid.Empty && session.DurableRevision > 0, "owner creates durable opaque document ID");
    var id = session.DocumentId!.Value;
    var blockId = First(session.GetDocumentSnapshot()!).Id;
    Check((await session.SelectAsync(blockId, 0, 0)).Succeeded, "owner selection addresses actual object ID");
    Check((await session.InsertTextAsync("Alpha beta gamma")).Succeeded, "owner structured editor inserts text");
    Check((await session.SelectAsync(blockId, 6, 10)).Succeeded, "select actual beta range");
    await session.DispatchAsync("BoldSelection", null);
    Check(session.ErrorCode is null && session.IsDirty, "same CUI action changes actual editor and marks dirty");
    AssertFormatting(session.GetDocumentSnapshot()!);
    var unsavedRevision = session.DurableRevision;
    Check((await session.UndoAsync()).Succeeded && Text(session.GetDocumentSnapshot()!) == "Alpha beta gamma" &&
        First(session.GetDocumentSnapshot()!).Runs.All(run => !run.Bold), "owner undo removes only range formatting");
    Check(session.DurableRevision == unsavedRevision, "undo does not alter durable base revision");
    Check((await session.RedoAsync()).Succeeded, "owner redo restores formatting");
    AssertFormatting(session.GetDocumentSnapshot()!);
    Check((await session.SaveAsync()).Succeeded && !session.IsDirty, "canonical save acknowledges clean state");
    var revision = session.DurableRevision;
    Check((await session.ExportNativeAsync(packagePath)).Succeeded, "actual native codec exports saved document");
    var exported = await packages.OpenAsync(packagePath);
    Check(exported.IsSuccess && exported.Value!.Id == id && exported.Value.Version == revision, "native package retains canonical identity and revision");
    AssertFormatting(exported.Value!);
    Check((await repository.GetVersionsAsync(id, default)).Count >= 2, "real repository publishes create and edit history");
    await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new State(id, blockId, revision,
        JsonSerializer.Serialize(session.GetDocumentSnapshot()!.Sections))));
    Check((await session.CloseAsync()).Succeeded && session.DocumentId is null, "close releases saved editor");
    await session.DisposeAsync();
}
else
{
    var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(statePath))!;
    var session = new WriteBrowserSession(repository, packages);
    Check((await session.OpenAsync(state.Id)).Succeeded, "fresh process opens same owner document ID");
    Check(session.DurableRevision == state.Revision && First(session.GetDocumentSnapshot()!).Id == state.BlockId,
        "fresh process retains revision and object ID");
    AssertFormatting(session.GetDocumentSnapshot()!);
    Check(JsonSerializer.Serialize(session.GetDocumentSnapshot()!.Sections) == state.StructuredSections,
        "fresh process retains entire section/page/block/run structure, IDs and attributes");

    await using (var readOnly = new WriteBrowserSession(repository, packages))
    {
        Check((await readOnly.OpenAsync(state.Id, readOnly: true)).Succeeded, "open explicit read-only presentation");
        var denied = await readOnly.InsertTextAsync("denied");
        Check(!denied.Succeeded && denied.Code == "ReadOnlyDocument" && !readOnly.IsDirty,
            "read-only session rejects editor mutation without dirtying");
        Check(readOnly.IsActionAvailable("BoldSelection") == false, "read-only formatting action disabled");
        Check((await repository.LoadAsync(state.Id, default))!.Version == state.Revision, "read-only rejection does not change durable revision");
    }

    var stale = new WriteBrowserSession(new NotesRepository(paths, new NotesDocumentValidator(), diagnostics), packages);
    Check((await stale.OpenAsync(state.Id)).Succeeded, "independent repository session opens same base revision");
    await session.SelectAsync(state.BlockId, 16, 16);
    await stale.SelectAsync(state.BlockId, 16, 16);
    await session.InsertTextAsync(" winner");
    await stale.InsertTextAsync(" stale");
    Check((await session.SaveAsync()).Succeeded, "first independent session commits next owner revision");
    var currentPath = Path.Combine(paths.DataDirectory, "Notes", "Documents", state.Id.ToString("D"), "current.haven-notes.json");
    var committedHash = SHA256.HashData(await File.ReadAllBytesAsync(currentPath));
    var rejected = await stale.SaveAsync();
    Check(!rejected.Succeeded && rejected.Code == "RevisionConflict" && stale.IsDirty,
        "actual repository rejects stale expected revision and preserves draft");
    var afterRejectionHash = SHA256.HashData(await File.ReadAllBytesAsync(currentPath));
    Check(committedHash.SequenceEqual(afterRejectionHash), "stale save leaves committed bytes unchanged");
    Check(Text(stale.GetDocumentSnapshot()!) == "Alpha beta gamma stale", "conflicting session retains its own exact unsaved text");
    var cannotClose = await stale.CloseAsync();
    Check(!cannotClose.Succeeded && stale.DocumentId == state.Id, "failed conflict save prevents close and draft loss");
    // Preserve the real conflict draft by exporting its snapshot with the production codec;
    // disposal remains deliberately blocked until a host supplies a conflict-resolution flow.
    Check((await packages.SaveAsync(stale.GetDocumentSnapshot()!, Path.Combine(paths.DataDirectory, "conflict-draft.9to1w"))).IsSuccess,
        "conflict draft can be preserved by actual native codec");

    Check((await session.UndoAsync()).Succeeded, "undo uses owner editor after save");
    Check((await session.SaveAsync()).Succeeded, "undo content saves against current durable revision");
    AssertFormatting((await repository.LoadAsync(state.Id, default))!);

    var revisionBeforeCopy = session.DurableRevision;
    var copy = await session.ImportNativeCopyAsync(packagePath);
    Check(copy.Succeeded && copy.Value != state.Id && session.DurableRevision == 1, "explicit native import creates independent durable document");
    AssertFormatting(session.GetDocumentSnapshot()!);
    Check((await repository.LoadAsync(state.Id, default))!.Version == revisionBeforeCopy, "independent import leaves original canonical artifact unchanged");

    var tamperedPath = Path.Combine(paths.DataDirectory, "tampered.9to1w");
    File.Copy(packagePath, tamperedPath);
    using (var archive = ZipFile.Open(tamperedPath, ZipArchiveMode.Update))
    {
        archive.GetEntry("document.json")!.Delete();
        var entry = archive.CreateEntry("document.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("{}");
    }
    var beforeImport = session.DocumentId;
    var tampered = await session.ImportNativeCopyAsync(tamperedPath);
    Check(!tampered.Succeeded && tampered.Code == "IntegrityCheckFailed" && session.DocumentId == beforeImport,
        "native integrity failure preserves currently open document");
    Check((await session.CloseAsync()).Succeeded, "independent imported document closes safely");

    // Stale is intentionally left as a recoverable unsaved session; IAsyncDisposable
    // correctly refuses silent data loss. Assert this guard, then avoid implicit disposal.
    var guarded = false;
    try { await stale.DisposeAsync(); } catch (InvalidOperationException) { guarded = true; }
    Check(guarded, "lifetime refuses silent dirty-document disposal");

    var queued = new WriteBrowserSession(repository, packages);
    Check((await queued.CreateAsync("Queued edit fixture")).Succeeded, "create separate durable queue fixture");
    var queuedId = queued.DocumentId!.Value;
    var queuedBlock = First(queued.GetDocumentSnapshot()!).Id;
    await queued.SelectAsync(queuedBlock, 0, 0);
    await queued.InsertTextAsync("before");
    var saving = queued.SaveAsync();
    var editing = queued.InsertTextAsync(" after");
    Check((await saving).Succeeded && (await editing).Succeeded, "queued mutation completes after actual repository save");
    Check(Text((await repository.LoadAsync(queuedId, default))!) == "before", "committed snapshot excludes queued later mutation");
    Check(queued.IsDirty && Text(queued.GetDocumentSnapshot()!) == "before after", "later edit remains visible and unsaved");
    var savedBase = queued.DurableRevision;
    Check((await queued.SaveAsync()).Succeeded && queued.DurableRevision == savedBase + 1, "remaining edit commits exactly next owner revision");

    await queued.InsertTextAsync(" retry");
    // Obstruct versions, keeping actual current present so the owner CAS still
    // validates before failing in real directory creation. Obstructing the entire
    // document directory correctly looks like a missing revision, not an IO gate.
    var directory = Path.Combine(paths.DataDirectory, "Notes", "Documents", queuedId.ToString("D"), "Versions");
    var backup = directory + ".isolated-fault-backup";
    Directory.Move(directory, backup);
    await File.WriteAllTextAsync(directory, "intentional fixture-only filesystem obstruction");
    try
    {
        var failed = await queued.SaveAsync();
        Check(!failed.Succeeded && failed.Code == "ProviderUnavailable" && queued.IsDirty,
            "real filesystem failure preserves unsaved draft");
        Check(queued.DurableRevision == savedBase + 1 && Text(queued.GetDocumentSnapshot()!) == "before after retry",
            "failed write retains durable base and exact draft text");
    }
    finally
    {
        File.Delete(directory);
        Directory.Move(backup, directory);
    }
    Check(Text((await repository.LoadAsync(queuedId, default))!) == "before after", "failed save left previous real durable content recoverable");
    Check((await queued.SaveAsync()).Succeeded && queued.DurableRevision == savedBase + 2, "retry commits once against preserved owner revision");
    Check((await queued.CloseAsync()).Succeeded, "recovered draft safely closes");
    await queued.DisposeAsync();
    await session.DisposeAsync();
}
Console.WriteLine(JsonSerializer.Serialize(new { mode = args[0], assertions, scope = "local durable owner/session; browser GUI not executed" }));

internal sealed record State(Guid Id, Guid BlockId, long Revision, string StructuredSections);
