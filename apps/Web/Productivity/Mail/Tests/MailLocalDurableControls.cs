using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

// Actual existing FileMailDraftStore and MailDraft only. Fictional .example data;
// no provider, sending, browser, identity service or draft-conflict authority.
if (args.Length != 2 || args[0] is not ("seed" or "verify"))
    throw new ArgumentException("Explicit seed|verify and owned data directory required.");
var mode = args[0]; var folder = Path.GetFullPath(args[1]);
if (!Directory.Exists(folder)) throw new InvalidOperationException("Owned fixture directory required.");
var receiptPath = Path.Combine(folder, "seed-receipt.json");
if (mode == "seed" && (File.Exists(receiptPath) || Directory.Exists(Path.Combine(folder, "MailDrafts"))))
    throw new InvalidOperationException("Fresh seed store required.");
if (mode == "verify" && !File.Exists(receiptPath)) throw new InvalidOperationException("Previous-process identity receipt required.");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40)); var ct = deadline.Token;
var store = new FileMailDraftStore(new FixturePaths(folder));
var errorsFolder = Path.Combine(folder, "error-profile");
var errorStore = new FileMailDraftStore(new FixturePaths(errorsFolder));
using var prior = mode == "verify" ? JsonDocument.Parse(File.ReadAllText(receiptPath)) : null;
Guid Id(string key) => prior is null ? Guid.NewGuid() : prior.RootElement.GetProperty(key).GetGuid();
var accountA = Id("accountA"); var accountB = Id("accountB");
var richId = Id("richId"); var otherId = Id("otherId"); var updateId = Id("updateId");
var deletedId = Id("deletedId"); var corruptId = Id("corruptId"); var attachmentId = Id("attachmentId");
var instant = new DateTimeOffset(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);
var names = new[] { "rich-semantic-drafts-and-attachment-identities-restart", "same-id-update-and-denied-input-preserve-bytes", "durable-delete-and-unreadable-draft-preserved-restart" };
var outcomes = new List<object>(); var assertions = 0; var exit = 1;
void Check(bool condition, string message) { ++assertions; if (!condition) throw new InvalidOperationException(message); }
string Canonical(MailDraft draft) => JsonSerializer.Serialize(draft, json);
string PathFor(string root, Guid id) => Path.Combine(root, "MailDrafts", id.ToString("N") + ".json");
MailDraft Rich(Guid accountId, Guid localId, DateTimeOffset updatedAt) => new(
    accountId, "provider-draft-same-account-scoped-string", MailResponseKind.ReplyAll, "source-message-7", "thread-9",
    [new MailAddress("Ada", "ada@example.com")], [new MailAddress("Grace", "grace@example.com")],
    [new MailAddress("Linus", "linus@example.com")], "Rich local draft café", "<p>Hello <strong>world α</strong></p>\n<p>Second line</p>", true,
    [new MailDraftAttachment("notes.txt", "text/plain", [0, 1, 2, 255], attachmentId)],
    LocalId: localId, Provider: CalendarProviderKind.Google, UpdatedAt: updatedAt,
    PersistenceState: MailDraftPersistenceState.Saved, LastSafeError: "authored safe marker");
async Task<string> Snapshot(Guid id) => Canonical(await store.GetAsync(id, ct) ?? throw new InvalidDataException("Actual draft missing."));
async Task DeniedInputPreservesBytes()
{
    var path = PathFor(folder, updateId); var before = await File.ReadAllBytesAsync(path, ct);
    var filesBefore = Directory.GetFiles(Path.Combine(folder, "MailDrafts")).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    var current = await store.GetAsync(updateId, ct) ?? throw new InvalidDataException("Actual updated draft missing.");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    var cancellationSeen = false;
    try { await store.UpsertAsync(current with { Body = "MUST NOT COMMIT" }, cancelled.Token); }
    catch (OperationCanceledException) { cancellationSeen = true; }
    Check(cancellationSeen, "Actual store rejects already-cancelled mutation.");
    var invalidSeen = false;
    try { await store.UpsertAsync(current with { LocalId = Guid.Empty, Body = "MUST NOT COMMIT" }, ct); }
    catch (ArgumentException) { invalidSeen = true; }
    Check(invalidSeen, "Actual store rejects missing canonical local draft ID.");
    Check(await store.GetAsync(Guid.Empty, ct) is null, "Empty ID cannot resolve a draft.");
    await store.DeleteAsync(Guid.Empty, ct);
    var after = await File.ReadAllBytesAsync(path, ct);
    Check(before.AsSpan().SequenceEqual(after), "Denied/cancelled inputs preserve exact committed draft bytes.");
    Check(filesBefore.SequenceEqual(Directory.GetFiles(Path.Combine(folder, "MailDrafts")).OrderBy(x => x, StringComparer.Ordinal)), "Denied inputs create no new file or leaked temporary.");
}
try
{
    for (var index = 0; index < 3; ++index)
    {
        var before = assertions;
        try
        {
            if (index == 0)
            {
                if (mode == "seed")
                {
                    await store.UpsertAsync(Rich(accountA, richId, instant), ct);
                    await store.UpsertAsync(Rich(accountB, otherId, instant.AddMinutes(-1)) with { Subject = "Independent other account" }, ct);
                }
                var rich = await store.GetAsync(richId, ct); var other = await store.GetAsync(otherId, ct);
                Check(rich is not null && Canonical(rich) == Canonical(Rich(accountA, richId, instant)), "Exact original typed rich draft fields, all recipients, provider IDs, attachment bytes and timestamps preserved.");
                Check(other is not null && other.LocalId != richId && other.AccountId == accountB && accountA != accountB,
                    "Distinct canonical local/account IDs retained even with matching provider draft strings; no authorization claim.");
                Check(other is not null && Canonical(other) == Canonical(Rich(accountB, otherId, instant.AddMinutes(-1)) with { Subject = "Independent other account" }), "Exact unrelated account draft remains intact.");
                var all = await store.GetAllAsync(ct);
                Check(all.Select(d => d.LocalId).SequenceEqual(mode == "seed" ? new[] { richId, otherId } : new[] { updateId, richId, otherId }), "Actual durable list returns all canonical drafts in UpdatedAt order.");
                if (prior is not null)
                    Check(JsonSerializer.Serialize(all, json) == prior.RootElement.GetProperty("allCanonical").GetString(), "Fresh native process preserves full canonical list contents and identities.");
            }
            if (index == 1)
            {
                var updated = Rich(accountA, updateId, instant.AddMinutes(1)) with { Subject = "Updated same ID", Body = "<p>Updated <em>β</em></p>" };
                if (mode == "seed")
                {
                    await store.UpsertAsync(updated with { Body = "<p>Before update</p>", UpdatedAt = instant }, ct);
                    await store.UpsertAsync(updated, ct);
                }
                Check(await Snapshot(updateId) == Canonical(updated), "Actual replacement preserves same LocalId and exact updated semantic fields.");
                Check(Directory.GetFiles(Path.Combine(folder, "MailDrafts"), "*.json").Length == 3, "One file per canonical draft; update creates no duplicated draft.");
                await DeniedInputPreservesBytes();
                if (prior is not null) Check(await Snapshot(updateId) == prior.RootElement.GetProperty("updateCanonical").GetString(), "Exact updated draft survives restart and failed mutation inputs.");
            }
            if (index == 2)
            {
                if (mode == "seed")
                {
                    await store.UpsertAsync(Rich(accountA, deletedId, instant), ct);
                    await store.DeleteAsync(deletedId, ct);
                    await errorStore.UpsertAsync(Rich(accountA, corruptId, instant), ct);
                    // Controlled real persisted-file corruption, no replacement codec/store.
                    await File.WriteAllBytesAsync(PathFor(errorsFolder, corruptId), Encoding.UTF8.GetBytes("{broken draft"), ct);
                }
                Check(await store.GetAsync(deletedId, ct) is null && !File.Exists(PathFor(folder, deletedId)), "Actual deletion remains absent after restart.");
                Check(await Snapshot(richId) == Canonical(Rich(accountA, richId, instant)), "Deleting another draft preserves unrelated rich content.");
                var path = PathFor(errorsFolder, corruptId); var original = await File.ReadAllBytesAsync(path, ct);
                Check(original.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes("{broken draft")), "Exact controlled corrupt bytes survive process restart.");
                var readDenied = false; var listDenied = false;
                try { await errorStore.GetAsync(corruptId, ct); } catch (InvalidDataException) { readDenied = true; }
                try { await errorStore.GetAllAsync(ct); } catch (InvalidDataException) { listDenied = true; }
                Check(readDenied && listDenied, "Actual typed owner refuses corrupt draft read and list rather than silent partial success.");
                var after = await File.ReadAllBytesAsync(path, ct);
                Check(original.AsSpan().SequenceEqual(after), "Original corrupt evidence bytes remain unchanged after read/list failures.");
            }
            outcomes.Add(new { name = names[index], state = "PASS", assertions = assertions - before });
            Console.WriteLine("PASS " + names[index]);
        }
        catch (Exception error)
        {
            outcomes.Add(new { name = names[index], state = "FAIL", assertions = assertions - before, failure = error.ToString() });
            Console.WriteLine("FAIL " + names[index]); throw;
        }
    }
    if (mode == "seed") File.WriteAllText(receiptPath, JsonSerializer.Serialize(new { accountA, accountB, richId, otherId, updateId, deletedId, corruptId, attachmentId,
        allCanonical = JsonSerializer.Serialize(await store.GetAllAsync(ct), json), updateCanonical = await Snapshot(updateId) }, json));
    exit = 0;
}
catch (Exception error) { Console.WriteLine(error); }
finally
{
    File.WriteAllText(Path.Combine(folder, mode + "-results.json"), JsonSerializer.Serialize(new { mode, declared = 3, executed = outcomes.Count,
        passed = outcomes.Count(x => JsonSerializer.SerializeToElement(x).GetProperty("state").GetString() == "PASS"),
        failed = outcomes.Count(x => JsonSerializer.SerializeToElement(x).GetProperty("state").GetString() == "FAIL"), notRun = 3 - outcomes.Count,
        assertions, outcomes, exitCode = exit }, json));
}
return exit;

sealed class FixturePaths(string folder) : IAppPaths
{
    public string DataDirectory => folder;
    public string DatabasePath => Path.Combine(folder, "unused.db");
    public string BrowserProfileDirectory => Path.Combine(folder, "unused-browser");
    public string AttachmentsDirectory => Path.Combine(folder, "unused-attachments");
    public string LogsDirectory => Path.Combine(folder, "unused-logs");
    public string LegacyStatePath => Path.Combine(folder, "absent-legacy.json");
}
