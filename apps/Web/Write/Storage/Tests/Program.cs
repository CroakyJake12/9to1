using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Write.Storage;

var cases = new (string, Func<Task>)[]
{
    ("actual transport classifier rejects malformed or non-object mutation acknowledgments as unknown outcome", async () => {
        foreach (var action in new[] { "Save", "Delete" }) foreach (var text in new[] { "null", "[]", "1", "broken" }) await Throws<NotesCommitOutcomeUnknownException>(() => { NotesStorageAcknowledgment.Parse(action, text); return Task.CompletedTask; });
        await Throws<IOException>(() => { NotesStorageAcknowledgment.Parse("Load", "[]"); return Task.CompletedTask; });
    }),
    ("actual validator rejects invalid document before transport", async () => {
        var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("Invalid"); doc.SchemaVersion = -1;
        await Throws<InvalidDataException>(() => repo.SaveAsync(doc, "Save", default)); doc = NotesDocument.Create(); doc.Title = null!; await Throws<InvalidDataException>(() => repo.SaveAsync(doc, "Save", default)); Check(store.Calls == 0, "invalid document dispatched");
    }),
    ("canonical create save load list search history stable identifiers", async () => {
        var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("Browser storage"); doc.Sections[0].Pages[0].Blocks[0].PlainText = "Durable browser note";
        var ids = (doc.Id, doc.Sections[0].Id, doc.Sections[0].Pages[0].Id, doc.Sections[0].Pages[0].Blocks[0].Id);
        var receipt = await repo.SaveAsync(doc, "Create", default); var loaded = await repo.LoadAsync(doc.Id, default);
        Check(receipt.Version == 1 && receipt.CurrentPath.StartsWith("indexeddb://") && receipt.VersionHistoryComplete, "receipt");
        Check(loaded!.Id == ids.Item1 && loaded.Sections[0].Id == ids.Item2 && loaded.Sections[0].Pages[0].Id == ids.Item3 && loaded.Sections[0].Pages[0].Blocks[0].Id == ids.Item4, "IDs changed");
        Check((await repo.ListAsync(default)).Single().WordCount == 3 && (await repo.SearchAsync("browser", default)).Single().BlockId == ids.Item4, "actual statistics/search");
        var history = (await repo.GetVersionsAsync(doc.Id, default)).Single(); Check((await repo.LoadVersionAsync(doc.Id, history.VersionId, default))!.Version == 1, "history");
    }),
    ("canonical nonempty Guid without UUID version/variant restriction is preserved", async () => {
        var store = new Memory(); var doc = NotesDocument.Create("Imported stable ID"); doc.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        await Repo(store).SaveAsync(doc, "Imported", default); Check((await Repo(store).LoadAsync(doc.Id, default))!.Id == doc.Id, "canonical Guid rejected");
    }),
    ("stale revision rejects without overwrite", async () => {
        var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("CAS"); await repo.SaveAsync(doc, "Create", default);
        var stale = (await repo.LoadAsync(doc.Id, default))!; await repo.SaveAsync(doc, "Second", default);
        await Throws<NotesRevisionConflictException>(() => repo.SaveAsync(stale, "Stale", default)); Check((await repo.LoadAsync(doc.Id, default))!.Version == 2, "overwrite");
    }),
    ("caller mutation during preload does not alter canonical save snapshot", async () => {
        var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("Original");
        store.BeforeLoad = () => { doc.Title = "Caller later edit"; doc.Sections.Clear(); return Task.CompletedTask; };
        await repo.SaveAsync(doc, "Save", default); store.BeforeLoad = null;
        Check((await repo.LoadAsync(doc.Id, default))!.Title == "Original", "snapshot changed");
    }),
    ("known commit survives late cancellation with warning", async () => {
        var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("Commit"); using var cts = new CancellationTokenSource(); store.AfterCommit = cts.Cancel;
        var receipt = await repo.SaveAsync(doc, "Save", cts.Token); Check(receipt.Version == 1 && receipt.PostCommitWarning is not null, "commit discarded");
    }),
    ("malformed mutation acknowledgment reports unknown commit and never retries", async () => {
        var store = new Memory { MalformedCommit = true }; var repo = Repo(store); var doc = NotesDocument.Create("Unknown");
        await Throws<NotesCommitOutcomeUnknownException>(() => repo.SaveAsync(doc, "Save", default)); Check(store.Saves == 1 && doc.Version == 0, "automatic retry/unack version advance");
    }),
    ("corrupt current recovers validated history without rewriting current", async () => {
        var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("Recovery"); await repo.SaveAsync(doc, "Create", default); var original = store.Current!.Value;
        store.Current = JsonSerializer.SerializeToElement(new { id = doc.Id, version = "1", documentJson = "{}", sha256 = "invalid", versionInfoJson = "{}" });
        var recovered = await repo.LoadAsync(doc.Id, default); Check(recovered!.Recovery.HasUnsavedRecovery && recovered.Revisions.Last().Kind == NotesRevisionKind.Restored, "recovery missing");
        Check(store.Current.Value.GetProperty("sha256").GetString() == "invalid", "recovery rewrote corrupt original");
        foreach (var malformed in new string?[] { null, "{\"sections\":null}" })
        { store.Current = JsonSerializer.SerializeToElement(new { id = doc.Id, version = "1", documentJson = malformed, sha256 = "invalid", versionInfoJson = "{}" }); Check((await repo.LoadAsync(doc.Id, default))!.Recovery.HasUnsavedRecovery, "null content escaped canonical validation"); }
        store.Current = original;
    }),
    ("validated recovered version can save while preserving corrupt original admission", async () => {
        var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("Recovery save"); await repo.SaveAsync(doc, "Create", default);
        store.Current = JsonSerializer.SerializeToElement(new { id = doc.Id, version = "9", documentJson = "{}", sha256 = "invalid", versionInfoJson = "{}" });
        var recovered = (await repo.LoadAsync(doc.Id, default))!; var receipt = await repo.SaveAsync(recovered, "Recovered", default);
        Check(receipt.Version == 2 && store.Packets.Last().GetProperty("preserveCorruptCurrent").GetBoolean(), "recovery save refused or invented revision");
    }),
    ("library current plus history snapshot lists missing and corrupt recoverable documents", async () => {
        var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("Recoverable library"); await repo.SaveAsync(doc, "Create", default);
        store.Current = null; var missing = (await repo.ListAsync(default)).Single(); Check(missing.Id == doc.Id && missing.Version == 1 && missing.HasRecovery, "history-only missing from library");
        store.Current = JsonSerializer.SerializeToElement(new { id = doc.Id, version = "9", documentJson = "{}", sha256 = "invalid", versionInfoJson = "{}" });
        var corrupt = (await repo.ListAsync(default)).Single(); Check(corrupt.Id == doc.Id && corrupt.HasRecovery && store.Current.Value.GetProperty("sha256").GetString() == "invalid", "corrupt root omitted/rewritten");
    }),
    ("successful-shaped malformed save acknowledgments carry exact durable inspection identity", async () => {
        foreach (var text in new[] { "{\"ok\":true,\"committed\":true}", "{\"ok\":true,\"committed\":true,\"value\":null}", "{\"ok\":true,\"committed\":true,\"value\":17}" })
        {
            var store = new Memory { AckOverride = JsonDocument.Parse(text).RootElement.Clone() }; var doc = NotesDocument.Create("Ack identity");
            try { await Repo(store).SaveAsync(doc, "Save", default); throw new Exception("malformed receipt accepted"); }
            catch (NotesCommitOutcomeUnknownException error) { Check(error.Operation == "Save" && error.DocumentId == doc.Id && error.ExpectedVersion == 0 && error.IntendedVersion == 1 && error.ContentSha256 == store.Packets.Single().GetProperty("sha256").GetString() && error.VersionId == store.Packets.Single().GetProperty("versionId").GetString(), "identity missing"); }
            Check(store.Saves == 1 && doc.Version == 0, "replay or local revision advanced");
        }
    }),
    ("missing current recovers actual history and rejects new revision zero", async () => {
        var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("Missing current"); await repo.SaveAsync(doc, "Create", default); store.Current = null;
        var recovered = (await repo.LoadAsync(doc.Id, default))!; Check(recovered.Version == 1 && recovered.Recovery.HasUnsavedRecovery, "history hidden");
        var fresh = NotesDocument.Create("Wrong new document"); fresh.Id = doc.Id; await Throws<NotesRevisionConflictException>(() => repo.SaveAsync(fresh, "Wrong", default));
        var receipt = await repo.SaveAsync(recovered, "Recover absent", default); Check(receipt.Version == 2 && store.Packets.Last().GetProperty("expectedHistoryRecords").GetString() is not null, "missing recovery save failed");
    }),
    ("non-object mutation acknowledgment reports unknown outcome", async () => {
        foreach (var text in new[] { "null", "[]", "1" }) { var store = new Memory { AckOverride = JsonDocument.Parse(text).RootElement.Clone() }; await Throws<NotesCommitOutcomeUnknownException>(() => Repo(store).SaveAsync(NotesDocument.Create(), "Save", default)); Check(store.Saves == 1, "retried nonobject"); }
    }),
    ("canonical long version beyond JavaScript exact number range remains exact", async () => {
        var store = new Memory(); var doc = NotesDocument.Create("Exact long"); doc.Version = 9_007_199_254_740_993; store.Seed(doc); var result = await Repo(store).SaveAsync(doc, "Next", default);
        Check(result.Version == 9_007_199_254_740_994 && (await Repo(store).LoadAsync(doc.Id, default))!.Version == result.Version, "long rounded");
    }),
    ("delete removes active document and history through one platform action", async () => {
        var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("Delete"); await repo.SaveAsync(doc, "Create", default); await repo.DeleteAsync(doc.Id, default);
        Check(await repo.LoadAsync(doc.Id, default) is null && (await repo.GetVersionsAsync(doc.Id, default)).Count == 0 && store.Deletes == 1, "delete");
    }),
    ("pre-cancelled save never reaches transport", async () => {
        var store = new Memory(); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Throws<OperationCanceledException>(() => Repo(store).SaveAsync(NotesDocument.Create(), "Save", cts.Token)); Check(store.Calls == 0, "cancel dispatch");
    }),
};
var failures = 0;
foreach (var (name, run) in cases) { try { await run(); Console.WriteLine($"PASS {name}"); } catch (Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e}"); } }
Console.WriteLine($"Discovered={cases.Length} Executed={cases.Length} Passed={cases.Length-failures} Failed={failures} Skipped=0 Scope=UNIT canonical repository + scripted platform transport");
if (args.Length == 2 && args[0] == "--fixtures")
{
    var store = new Memory(); var repo = Repo(store); var doc = NotesDocument.Create("Real browser canonical fixture"); doc.Sections[0].Pages[0].Blocks[0].PlainText = "Durable browser content 🧁";
    await repo.SaveAsync(doc, "Create", default); var first = store.Packets.Last(); doc.Title = "Second committed revision"; await repo.SaveAsync(doc, "Edit", default); var second = store.Packets.Last();
    var largeStore = new Memory(); var largeDoc = NotesDocument.Create("Quota canonical fixture"); largeDoc.Sections[0].Pages[0].Blocks[0].PlainText = new string('x', 4_000_000); await Repo(largeStore).SaveAsync(largeDoc, "Quota", default); var large = largeStore.Packets.Single();
    var recoveryStore = new Memory(); var recoveryRepo = Repo(recoveryStore); var recoveryDoc = NotesDocument.Create("Canonical recovery fixture"); await recoveryRepo.SaveAsync(recoveryDoc, "Create", default); var recoveryFirst = recoveryStore.Packets.Single();
    recoveryStore.Current = JsonSerializer.SerializeToElement(new { id = recoveryDoc.Id, version = "9", documentJson = "{}", sha256 = "invalid", versionInfoJson = "{}" });
    var recoveryDocument = (await recoveryRepo.LoadAsync(recoveryDoc.Id, default))!; await recoveryRepo.SaveAsync(recoveryDocument, "Recovered", default); var recoverySave = recoveryStore.Packets.Last();
    var importedStore = new Memory(); var importedDoc = NotesDocument.Create("Imported canonical Guid"); importedDoc.Id = Guid.Parse("00000000-0000-0000-0000-000000000001"); await Repo(importedStore).SaveAsync(importedDoc, "Import", default); var imported = importedStore.Packets.Single();
    var longStore = new Memory(); var longDoc = NotesDocument.Create("Exact long canonical boundary"); longDoc.Version = 9_007_199_254_740_993; longStore.Seed(longDoc); var longRecord = longStore.Current; var longHistory = longStore.Versions.Single(); await Repo(longStore).SaveAsync(longDoc, "Next exact long", default); var longSave = longStore.Packets.Single();
    // Source-generated full canonical documents, not a handwritten JavaScript document model.
    await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(new { documentId = doc.Id, first, second, large, recoveryFirst, recoverySave, imported, longRecord, longHistory, longSave, sectionId = doc.Sections[0].Id, pageId = doc.Sections[0].Pages[0].Id, blockId = doc.Sections[0].Pages[0].Blocks[0].Id }));
}
Environment.ExitCode = failures == 0 ? 0 : 1;
static IndexedDbNotesRepository Repo(Memory store) => new(store, new NotesDocumentValidator());
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }

sealed class Memory : INotesBrowserTransport
{
    public JsonElement? Current; public readonly List<JsonElement> Versions = []; public readonly List<JsonElement> Packets = [];
    public int Calls, Saves, Deletes; public bool MalformedCommit; public JsonElement? AckOverride; public Func<Task>? BeforeLoad; public Action? AfterCommit;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static JsonElement Ok(object? value, bool committed = false) => JsonSerializer.SerializeToElement(new { ok = true, value, committed }, Json);
    public void Seed(NotesDocument doc)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var text = JsonSerializer.Serialize(doc, options); var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        var id = $"v{doc.Version:D10}-{doc.UpdatedAt.UtcDateTime:yyyyMMdd-HHmmssfff}";
        var info = new NotesVersionInfo(id, doc.Version, doc.UpdatedAt, "Source generated long fixture", System.Text.Encoding.UTF8.GetByteCount(text), sha);
        var infoJson = JsonSerializer.Serialize(info, options);
        Current = JsonSerializer.SerializeToElement(new { id = doc.Id, version = doc.Version.ToString(CultureInfo.InvariantCulture), documentJson = text, sha256 = sha, versionInfoJson = infoJson }, Json);
        Versions.Add(JsonSerializer.SerializeToElement(new { documentId = doc.Id, versionId = id, id = doc.Id, version = doc.Version.ToString(CultureInfo.InvariantCulture), documentJson = text, sha256 = sha, versionInfoJson = infoJson }, Json));
    }
    public async Task<JsonElement> InvokeAsync(string action, JsonElement arguments, CancellationToken ct)
    {
        Calls++; ct.ThrowIfCancellationRequested();
        switch (action)
        {
            case "Load": if (BeforeLoad is not null) await BeforeLoad(); return Ok(Current);
            case "List": return Ok(new { documents = Current is { } current ? new[] { current } : [], history = Versions });
            case "Versions": return Ok(Versions);
            case "LoadVersion": return Ok(Versions.FirstOrDefault(x => x.GetProperty("versionId").GetString() == arguments.GetProperty("versionId").GetString()) is var found && found.ValueKind != JsonValueKind.Undefined ? found : null);
            case "Save":
                Saves++; Packets.Add(arguments.Clone());
                var actual = Current?.GetProperty("version").GetString() ?? "0";
                if (Current is not null && !arguments.GetProperty("preserveCorruptCurrent").GetBoolean() && actual != arguments.GetProperty("expectedVersion").GetString()) return JsonSerializer.SerializeToElement(new { ok = false, error = new { code = "RevisionConflict", documentId = arguments.GetProperty("documentId").GetGuid(), expectedVersion = arguments.GetProperty("expectedVersion").GetString(), actualVersion = actual } });
                Current = JsonSerializer.SerializeToElement(new { id = arguments.GetProperty("documentId").GetGuid(), version = arguments.GetProperty("version").GetString(), documentJson = arguments.GetProperty("documentJson").GetString(), sha256 = arguments.GetProperty("sha256").GetString(), versionInfoJson = arguments.GetProperty("versionInfoJson").GetString() }, Json);
                Versions.Add(JsonSerializer.SerializeToElement(new { documentId = arguments.GetProperty("documentId").GetGuid(), versionId = arguments.GetProperty("versionId").GetString(), id = arguments.GetProperty("documentId").GetGuid(), version = arguments.GetProperty("version").GetString(), documentJson = arguments.GetProperty("documentJson").GetString(), sha256 = arguments.GetProperty("sha256").GetString(), versionInfoJson = arguments.GetProperty("versionInfoJson").GetString() }, Json));
                AfterCommit?.Invoke(); return AckOverride ?? (MalformedCommit ? JsonSerializer.SerializeToElement(new { nonsense = true }) : Ok(arguments.GetProperty("receiptJson").GetString(), true));
            case "Delete": Deletes++; Current = null; Versions.Clear(); return Ok(null, true);
            default: throw new Exception(action);
        }
    }
}
