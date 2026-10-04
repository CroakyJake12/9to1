using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using NineToOne.Web.Write;

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")))
    throw new ArgumentException("Supply a fresh explicitly isolated HAVEN_DATA_DIR.");
var paths = new AppPaths();
var real = new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
var fault = new AcknowledgmentFault(real);
var session = new WriteBrowserSession(fault, new WriteNativeDocumentPackageStore(), error => ReferenceEquals(error, fault.Unknown));
var assertions = 0;
void Check(bool passed, string name)
{
    if (!passed) throw new InvalidOperationException(name);
    ++assertions; Console.WriteLine("ASSERT " + name);
}
Check((await real.ListAsync(default)).Count == 0, "real repository fixture is fresh");
var observerError = new ObserverFixtureException();
void ThrowObserver(object? sender, EventArgs args) => throw observerError;
session.DocumentOpened += ThrowObserver;
fault.LoseAfterCommit = true;
var observed = false;
try { await session.CreateAsync("Acknowledgment-loss observer fixture"); }
catch (ObserverFixtureException error) { observed = ReferenceEquals(error, observerError); }
session.DocumentOpened -= ThrowObserver;
Check(observed, "public DocumentOpened observer failure remains observable after genuine real-repository commit");
var createdReceipt = fault.Receipt!;
var id = createdReceipt.DocumentId;
Check(session.HasUnknownCommitOutcome && session.IsDirty && session.PendingCommitDocumentId == id && session.DocumentId == id,
    "observer failure cannot discard pending canonical identity or uncertain draft state");
Check((await real.LoadAsync(id, default))!.Version == 1 && session.DurableRevision == 0,
    "independent actual repository confirms commit while session retains last acknowledged base without fabricating receipt");
var before = SHA256.HashData(await File.ReadAllBytesAsync(createdReceipt.CurrentPath));
Check(!(await session.SaveAsync()).Succeeded && !(await session.CreateAsync()).Succeeded && !(await session.CloseAsync()).Succeeded,
    "uncertain draft blocks save/new/close effects until explicit durable inspection");
var after = SHA256.HashData(await File.ReadAllBytesAsync(createdReceipt.CurrentPath));
Check(before.SequenceEqual(after) && (await real.ListAsync(default)).Count == 1,
    "blocked uncertain commands leave actual canonical bytes unchanged and create no duplicate document");
foreach (var command in new[] { "CreateDocument", "SaveDocument", "BoldSelection", "Undo", "Redo", "CloseDocument", "OpenDocument" })
    Check(session.IsActionAvailable(command) == false, "actual uncertain command availability disables " + command);
Check(session.TryGetValue("CanInspectSavedState", out var inspect) && inspect is true,
    "actual CUI binding enables explicit saved-state inspection");
Check((await session.InspectDurableStateAsync()).Succeeded && !session.HasUnknownCommitOutcome && !session.IsDirty && session.DurableRevision == 1,
    "explicit actual-owner read resolves matching created snapshot at acknowledged stored revision");
Check((await real.LoadAsync(id, default))!.Version == 1, "inspection does not write a second revision");
await session.InsertTextAsync("Alpha beta gamma");
fault.LoseAfterCommit = true;
var lost = await session.SaveAsync();
Check(!lost.Succeeded && lost.Code == "CommitOutcomeUnknown" && session.HasUnknownCommitOutcome && session.IsDirty,
    "existing-document lost receipt exposes typed uncertainty and retains dirty draft");
Check((await real.LoadAsync(id, default))!.Version == 2 && session.DurableRevision == 1,
    "real committed second revision remains distinct from session acknowledged base");
var sections = JsonSerializer.Serialize(session.GetDocumentSnapshot()!.Sections);
Check((await session.InspectDurableStateAsync()).Succeeded && session.DurableRevision == 2 && !session.IsDirty &&
    JsonSerializer.Serialize(session.GetDocumentSnapshot()!.Sections) == sections,
    "matching saved inspection acknowledges existing content without replacing stable draft structure");
await session.InsertTextAsync(" retained next draft");
fault.LoseBeforeCommit = true;
Check(!(await session.SaveAsync()).Succeeded && session.HasUnknownCommitOutcome && (await real.LoadAsync(id, default))!.Version == 2,
    "uncertain precommit transport fault leaves actual durable base unchanged and draft pending");
Check((await session.InspectDurableStateAsync()).Succeeded && !session.HasUnknownCommitOutcome && session.IsDirty,
    "explicit unchanged-base inspection permits manual retry while keeping unsaved draft");
Check((await session.SaveAsync()).Succeeded && (await real.LoadAsync(id, default))!.Version == 3,
    "explicit retry commits same canonical ID exactly once through real owner CAS");
await session.InsertTextAsync(" conflicting draft");
fault.LoseAfterCommit = true;
await session.SaveAsync();
var competing = (await real.LoadAsync(id, default))!;
competing.Title = "Independent actual writer";
await real.SaveAsync(competing, "Real owner conflict fixture", default);
var preserved = JsonSerializer.Serialize(session.GetDocumentSnapshot()!.Sections);
var conflict = await session.InspectDurableStateAsync();
Check(!conflict.Succeeded && conflict.Code == "RevisionConflict" && session.HasUnknownCommitOutcome && session.IsDirty &&
    JsonSerializer.Serialize(session.GetDocumentSnapshot()!.Sections) == preserved,
    "different actual durable revision stays blocked conflict without discarding draft or overwriting writer");
var guard = false;
try { await session.DisposeAsync(); }
catch (InvalidOperationException) { guard = true; }
Check(guard && (await real.LoadAsync(id, default))!.Title == "Independent actual writer",
    "dirty uncertain disposal guard preserves actual winner and unresolved draft");
// Only the broker's ancillary cleanup is faulted. Canonical import/save remains
// the original actual repository and original native codec; no browser claim.
var packageSource = Path.Combine(paths.DataDirectory, "actual-cleanup-source.9to1w");
var packageCodec = new WriteNativeDocumentPackageStore();
Check((await packageCodec.SaveAsync((await real.LoadAsync(id, default))!, packageSource, default)).IsSuccess,
    "cleanup fixture source is emitted by actual native codec from real durable document");
var cleanupFault = new PackageCleanupFault(packageSource, paths.DataDirectory);
var packageSession = new WriteBrowserSession(real, packageCodec, packageBroker: cleanupFault);
var imported = await packageSession.ImportFromBrowserAsync();
var importedId = packageSession.DocumentId!.Value;
Check(imported.Succeeded && importedId != id && packageSession.DurableRevision == 1 &&
    (await real.LoadAsync(importedId, default))!.Version == 1,
    "actual durable independent import remains successful despite ancillary staged cleanup error");
Check(packageSession.PackageCleanupWarning is not null && ReferenceEquals(packageSession.LastPackageCleanupError, cleanupFault.Error) &&
    packageSession.Status.Contains("Imported", StringComparison.Ordinal) && !packageSession.IsDirty,
    "known imported identity/receipt stays clean with observable cleanup warning and original cleanup error");
var packageCount = (await real.ListAsync(default)).Count;
var exported = await packageSession.ExportToBrowserAsync();
Check(exported.Succeeded && packageSession.Status.StartsWith("Native package download started", StringComparison.Ordinal) &&
    packageSession.DurableRevision == 1 && (await real.ListAsync(default)).Count == packageCount,
    "known export activation is not replaced by cleanup failure and creates no extra canonical document/revision");
var received = await packageCodec.OpenAsync(cleanupFault.PublishedPath, default);
Check(received.IsSuccess && received.Value!.Id == importedId && received.Value.Version == 1 &&
    JsonSerializer.Serialize(received.Value.Sections) == JsonSerializer.Serialize(packageSession.GetDocumentSnapshot()!.Sections),
    "native fixture sink receives exact actual package content; browser download itself is not claimed");
await packageSession.CloseAsync();
await packageSession.DisposeAsync();
// The owner editor historically initializes an empty paragraph on selection.
// A legacy stored document must expose that local change rather than silently
// claiming the new run's identity was already durably saved.
var legacy = NotesDocument.Create("Legacy empty paragraph fixture");
var legacyReceipt = await real.SaveAsync(legacy, "Original empty owner document", default);
var legacyBytes = SHA256.HashData(await File.ReadAllBytesAsync(legacyReceipt.CurrentPath));
var legacySession = new WriteBrowserSession(real, packageCodec);
Check((await legacySession.OpenAsync(legacy.Id, readOnly: true)).Succeeded && !legacySession.IsDirty &&
    legacySession.DurableRevision == legacyReceipt.Version,
    "read-only legacy presentation does not claim an added durable revision");
await legacySession.CloseAsync();
var afterReadOnly = SHA256.HashData(await File.ReadAllBytesAsync(legacyReceipt.CurrentPath));
Check(legacyBytes.SequenceEqual(afterReadOnly),
    "read-only owner initialization and close leave actual legacy bytes unchanged");
legacySession.DocumentOpened += ThrowObserver;
var legacyObserverObserved = false;
try { await legacySession.OpenAsync(legacy.Id); }
catch (ObserverFixtureException error) { legacyObserverObserved = ReferenceEquals(error, observerError); }
legacySession.DocumentOpened -= ThrowObserver;
Check(legacyObserverObserved && legacySession.DocumentId == legacy.Id && legacySession.IsDirty &&
    legacySession.DurableRevision == legacyReceipt.Version,
    "throwing legacy Open observer sees finalized unsaved owner initialization and acknowledged base/identity");
var normalizedLegacy = legacySession.GetDocumentSnapshot()!;
var afterEditableOpen = SHA256.HashData(await File.ReadAllBytesAsync(legacyReceipt.CurrentPath));
Check(normalizedLegacy.Sections[0].Id == legacy.Sections[0].Id &&
    normalizedLegacy.Sections[0].Pages[0].Id == legacy.Sections[0].Pages[0].Id &&
    normalizedLegacy.Sections[0].Pages[0].Blocks[0].Id == legacy.Sections[0].Pages[0].Blocks[0].Id &&
    normalizedLegacy.Sections[0].Pages[0].Blocks[0].Runs.Count == 1 &&
    legacyBytes.SequenceEqual(afterEditableOpen),
    "owner normalization retains original structure IDs and does not implicitly persist its new run ID");
Check((await legacySession.PrepareToCloseAsync()).Succeeded && !legacySession.IsDirty && legacySession.DocumentId == legacy.Id &&
    legacySession.DurableRevision == legacyReceipt.Version + 1,
    "explicit close preparation persists owner initialized content through actual CAS without detaching");
await legacySession.CloseAsync();
Check((await legacySession.OpenAsync(legacy.Id)).Succeeded && !legacySession.IsDirty &&
    JsonSerializer.Serialize(legacySession.GetDocumentSnapshot()!.Sections) == JsonSerializer.Serialize(normalizedLegacy.Sections),
    "normalized legacy document reopens clean with every committed owner run ID stable");
await legacySession.CloseAsync();
await legacySession.DisposeAsync();
var recoveryDocument = NotesDocument.Create("Recovered observer fixture");
_ = new WriteDocumentEditor(recoveryDocument);
var recoveryReceipt = await real.SaveAsync(recoveryDocument, "Actual recovery baseline", default);
await File.WriteAllTextAsync(recoveryReceipt.CurrentPath, "intentional isolated current corruption");
var corruptBytes = SHA256.HashData(await File.ReadAllBytesAsync(recoveryReceipt.CurrentPath));
var recoverySession = new WriteBrowserSession(real, packageCodec);
recoverySession.DocumentOpened += ThrowObserver;
var recoveryObserverObserved = false;
try { await recoverySession.OpenAsync(recoveryDocument.Id); }
catch (ObserverFixtureException error) { recoveryObserverObserved = ReferenceEquals(error, observerError); }
recoverySession.DocumentOpened -= ThrowObserver;
Check(recoveryObserverObserved && recoverySession.DocumentId == recoveryDocument.Id && recoverySession.IsDirty &&
    recoverySession.DurableRevision == recoveryReceipt.Version && recoverySession.GetDocumentSnapshot()!.Recovery.HasUnsavedRecovery,
    "throwing recovered Open observer sees finalized recovery draft and actual historical base identity");
// Actual NotesRepository.Load quarantines corrupt current before returning a
// validated recovery copy. Assert its preserved original, not a fictional
// invariant that the corrupt file still occupies the current locator.
var quarantined = Directory.GetFiles(Path.GetDirectoryName(recoveryReceipt.CurrentPath)!,
    Path.GetFileName(recoveryReceipt.CurrentPath) + ".corrupt-current-*").Single();
var corruptAfterOpen = SHA256.HashData(await File.ReadAllBytesAsync(quarantined));
Check(!File.Exists(recoveryReceipt.CurrentPath) && corruptBytes.SequenceEqual(corruptAfterOpen),
    "actual recovery load quarantines exact corrupt original without publishing a replacement current");
var recoveryDisposeDenied = false;
try { await recoverySession.DisposeAsync(); }
catch (InvalidOperationException) { recoveryDisposeDenied = true; }
Check(recoveryDisposeDenied && recoverySession.DocumentId == recoveryDocument.Id && recoverySession.IsDirty,
    "recovered observer failure cannot make unsaved recovery disposable or lose its identity");
Check((await recoverySession.PrepareToCloseAsync()).Succeeded && !recoverySession.IsDirty &&
    recoverySession.DocumentId == recoveryDocument.Id && recoverySession.DurableRevision == recoveryReceipt.Version + 1,
    "explicit actual recovery preparation commits once through owner validation and CAS before close");
var recoverySaved = (await real.LoadAsync(recoveryDocument.Id, default))!;
Check(!recoverySaved.Recovery.HasUnsavedRecovery && recoverySaved.Version == recoverySession.DurableRevision &&
    JsonSerializer.Serialize(recoverySaved.Sections) == JsonSerializer.Serialize(recoverySession.GetDocumentSnapshot()!.Sections),
    "independent owner recovery readback confirms acknowledged revision and complete preserved structure");
await recoverySession.CloseAsync();
await recoverySession.DisposeAsync();
Console.WriteLine(JsonSerializer.Serialize(new { assertions,
    scope = "real NotesRepository/native codec with delegated receipt-loss and ancillary broker-cleanup faults; typed session/binding controls, not browser chooser/transport/renderer/ACL acceptance" }));

sealed class ObserverFixtureException : Exception { }

// Fault injection never owns canonical data, revisions or models. Every successful
// write/read/history operation delegates to the unchanged actual repository.
sealed class AcknowledgmentFault(INotesRepository owner) : INotesRepository
{
    public IOException Unknown { get; } = new("Intentional lost acknowledgment fixture");
    public bool LoseAfterCommit { get; set; }
    public bool LoseBeforeCommit { get; set; }
    public NotesSaveResult? Receipt { get; private set; }
    public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token)
    {
        if (LoseBeforeCommit) { LoseBeforeCommit = false; throw Unknown; }
        Receipt = await owner.SaveAsync(document, reason, token);
        if (LoseAfterCommit) { LoseAfterCommit = false; throw Unknown; }
        return Receipt;
    }
    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token) => owner.ListAsync(token);
    public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) => owner.LoadAsync(id, token);
    public Task DeleteAsync(Guid id, CancellationToken token) => owner.DeleteAsync(id, token);
    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => owner.GetVersionsAsync(id, token);
    public Task<NotesDocument?> LoadVersionAsync(Guid id, string version, CancellationToken token) => owner.LoadVersionAsync(id, version, token);
    public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => owner.RecoverLatestAsync(id, token);
    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => owner.SearchAsync(query, token);
}

sealed class PackageCleanupFault(string original, string fixtureDirectory) : IWriteBrowserPackageBroker
{
    public IOException Error { get; } = new("Intentional ancillary stage cleanup failure");
    public string PublishedPath { get; } = Path.Combine(fixtureDirectory, "actual-native-fixture-sink.9to1w");
    private WriteStagedPackage Stage()
    {
        var directory = Path.Combine(fixtureDirectory, "faulted-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new(Path.Combine(directory, "staged.9to1w"), "native.9to1w", () => throw Error);
    }
    public Task<WriteStagedPackage?> PickImportAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var stage = Stage(); File.Copy(original, stage.Path);
        return Task.FromResult<WriteStagedPackage?>(stage);
    }
    public Task<WriteStagedPackage> StageExportAsync(string name, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(Stage()); }
    public Task PublishAsync(WriteStagedPackage stage, CancellationToken token)
    { token.ThrowIfCancellationRequested(); File.Copy(stage.Path, PublishedPath); return Task.CompletedTask; }
}
