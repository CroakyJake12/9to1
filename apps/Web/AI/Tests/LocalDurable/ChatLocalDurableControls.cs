using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

// Actual owner SQLite/versioning/export, fictional local data only. No Chat backend,
// governed catalog, executor, provider, actor or browser installation is supplied.
if (args.Length != 2 || args[0] is not ("seed" or "verify"))
    throw new ArgumentException("Explicit seed|verify and owned fixture directory required.");
var mode = args[0]; var folder = Path.GetFullPath(args[1]);
if (!Directory.Exists(folder)) throw new InvalidOperationException("Custodian must create owned fixture directory.");
var receiptPath = Path.Combine(folder, "seed-receipt.json");
if (mode == "seed" && (File.Exists(receiptPath) || File.Exists(Path.Combine(folder, "chat.db"))))
    throw new InvalidOperationException("Seed requires a fresh local store.");
if (mode == "verify" && (!File.Exists(receiptPath) || !File.Exists(Path.Combine(folder, "chat.db"))))
    throw new InvalidOperationException("Verify requires actual previous-process store and receipt.");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40)); var ct = deadline.Token;
var database = new SqliteDatabase(new FixturePaths(folder));
// Existing original repository test constructor. No production backup/restore/ownership
// gate is claimed: initialization and owner-generated store identity are real mutations.
await new ConversationProductionDatabase(database).InitializeAsync(ct);
var conversations = new ConversationRepository(database);
var production = new SafeConversationProductionRepository(database, conversations,
    new ConversationProductionRepository(database, conversations));
var versioning = new ConversationVersioningService(conversations, production);
var exports = new ConversationExportService(production);
var identity = await database.GetStoreIdentityAsync(ct);
var seedRows = new List<object>(); var outcomes = new List<object>(); var assertions = 0;
void Check(bool condition, string message) { ++assertions; if (!condition) throw new InvalidOperationException(message); }
Conversation NewConversation(string title) => new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
    title, null, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
ChatMessage User(Conversation conversation, string text) => new(Guid.NewGuid(), conversation.Id,
    MessageRole.User, text, null, null, null, conversation.CreatedAt);
string Canonical(ConversationExportDocument document) => JsonSerializer.Serialize(document with { ExportedAt = DateTimeOffset.UnixEpoch }, json);
async Task<string> Snapshot(Guid id) => Canonical(await production.BuildExportAsync(id, ct));
var names = new[] { "real-branch-original-history-fresh-process", "real-overwrite-recovery-draft-fresh-process", "real-json-markdown-plain-export-preserves-owner" };
using var prior = mode == "verify" ? JsonDocument.Parse(File.ReadAllText(receiptPath)) : null;
if (prior is not null)
{
    Check(prior.RootElement.GetProperty("storeId").GetGuid() == identity.StoreId, "Original SQL store identity survives fresh process.");
    Check(prior.RootElement.GetProperty("cases").GetArrayLength() == 3, "Exactly three prior canonical case receipts.");
}
var exit = 1;
try
{
    for (var caseIndex = 0; caseIndex < 3; ++caseIndex)
    {
        var before = assertions;
        try
        {
            Guid id, userId, rootId, branchId; string? rootMessagesJson = null, draftJson = null;
            if (mode == "seed")
            {
                var conversation = NewConversation("Public fictional local Chat " + caseIndex);
                var user = User(conversation, caseIndex == 0 ? "Original question" : caseIndex == 1 ? "Before" : "Unicode café <tag>\nsecond line");
                await conversations.UpsertConversationAsync(conversation, ct);
                await conversations.AddMessageAsync(user, ct);
                if (caseIndex != 1)
                    await conversations.AddMessageAsync(new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.Assistant,
                        "Authored historical answer α", "Fixture history", "Authored-not-inference", "{\"fixture\":true}", user.CreatedAt.AddSeconds(1)), ct);
                var root = await production.EnsureRootBranchAsync(conversation.Id, ct);
                id = conversation.Id; userId = user.Id; rootId = root.Id; branchId = root.Id;
                if (caseIndex == 0)
                    branchId = (await versioning.EditUserMessageAsync(id, userId, "Edited question", MessageEditMode.NewBranch, ct)).Id;
                if (caseIndex == 1)
                {
                    await versioning.EditUserMessageAsync(id, userId, "After", MessageEditMode.OverwriteCurrentBranch, ct);
                    await production.SaveDraftAsync(new ConversationDraft(id, rootId, "Retained draft café\nsecond line", "[]", DateTimeOffset.UtcNow), ct);
                }
            }
            else
            {
                var row = prior!.RootElement.GetProperty("cases")[caseIndex];
                Check(row.GetProperty("name").GetString() == names[caseIndex], "Same exact prior scenario name.");
                id = row.GetProperty("conversationId").GetGuid(); userId = row.GetProperty("userId").GetGuid();
                rootId = row.GetProperty("rootId").GetGuid(); branchId = row.GetProperty("branchId").GetGuid();
                Check(await Snapshot(id) == row.GetProperty("canonical").GetString(), "Fresh actual repository preserves full canonical IDs/messages/versions/branch/provenance.");
            }
            Check(id != Guid.Empty && userId != Guid.Empty && rootId != Guid.Empty && branchId != Guid.Empty, "All actual canonical IDs nonempty.");
            if (caseIndex == 0)
            {
                Check(rootId != branchId, "Real owner NewBranch generates a distinct branch.");
                var branches = await production.GetBranchesAsync(id, ct);
                Check(branches.Count == 2 && branches.Single(b => b.IsCurrent).Id == branchId, "Both original branches persist and actual current branch is selected.");
                await production.SetCurrentBranchAsync(id, rootId, ct); // Explicit real owner mutation, not read-only.
                var original = await conversations.GetMessagesAsync(id, ct);
                Check(original.Count == 2 && original[0].Id == userId && original[0].Content == "Original question"
                    && original[1].Content == "Authored historical answer α", "Original descendants/history survive edited branch.");
                rootMessagesJson = JsonSerializer.Serialize(original, json);
                if (mode == "verify") Check(rootMessagesJson == prior!.RootElement.GetProperty("cases")[caseIndex].GetProperty("rootMessagesJson").GetString(), "Original branch retains exact canonical message IDs/content/provenance after restart.");
                await production.SetCurrentBranchAsync(id, branchId, ct);
                var edited = await conversations.GetMessagesAsync(id, ct);
                Check(edited.Count == 1 && edited[0].Id == userId && edited[0].Content == "Edited question", "Same canonical user ID projects edited branch without original descendants.");
            }
            if (caseIndex == 1)
            {
                var versions = await production.GetVersionsAsync(userId, rootId, ct);
                Check(versions.Any(v => v.Kind == MessageVersionKind.RecoverySnapshot && v.Content == "Before")
                    && versions.Any(v => v.Kind == MessageVersionKind.UserEdit && v.Content == "After" && v.IsCurrent), "Actual owner overwrite retains prior recovery and current version.");
                var current = await conversations.GetMessagesAsync(id, ct);
                Check(current.Count == 1 && current[0].Id == userId && current[0].Content == "After", "Overwrite preserves exact message identity and current content.");
                var draft = await production.GetDraftAsync(id, rootId, ct);
                Check(draft is not null && draft.ConversationId == id && draft.BranchId == rootId
                    && draft.Content == "Retained draft café\nsecond line" && draft.AttachmentIdsJson == "[]", "Real durable draft remains bound to exact conversation and branch.");
                draftJson = JsonSerializer.Serialize(draft, json);
                if (mode == "verify") Check(draftJson == prior!.RootElement.GetProperty("cases")[caseIndex].GetProperty("draftJson").GetString(), "Exact actual canonical draft/timestamp/IDs survive restart.");
            }
            if (caseIndex == 2)
            {
                var snapshot = await Snapshot(id);
                var raw = await exports.ExportJsonAsync(id, ct);
                var typed = JsonSerializer.Deserialize<ConversationExportDocument>(raw, json)
                    ?? throw new InvalidDataException("Actual canonical export document unavailable.");
                Check(typed.Conversation.Id == id && typed.Messages.Count == 2 && typed.Messages[0].Id == userId
                    && typed.Branches.Single().Id == rootId && Canonical(typed) == snapshot, "Owner JSON roundtrips exact actual typed document/IDs/provenance without a replacement codec.");
                var markdown = await exports.ExportMarkdownAsync(id, ct); var plain = await exports.ExportPlainTextAsync(id, ct);
                Check(markdown.Contains("Unicode café <tag>\nsecond line", StringComparison.Ordinal)
                    && markdown.Contains("Authored-not-inference", StringComparison.Ordinal)
                    && markdown.Contains("Authored historical answer α", StringComparison.Ordinal), "Original Markdown export preserves exact authored Unicode and provenance.");
                Check(plain.Contains("You: Unicode café <tag>\nsecond line", StringComparison.Ordinal)
                    && plain.Contains("Fixture history: Authored historical answer α", StringComparison.Ordinal), "Original plain export preserves authored role/content.");
                Check(await Snapshot(id) == snapshot && (await database.GetStoreIdentityAsync(ct)).StoreId == identity.StoreId,
                    "Export leaves observed canonical owner data and real store identity unchanged; no physical WAL-byte invariance claimed.");
            }
            if (mode == "seed") seedRows.Add(new { name = names[caseIndex], conversationId = id, userId, rootId, branchId, rootMessagesJson, draftJson, canonical = await Snapshot(id) });
            outcomes.Add(new { name = names[caseIndex], state = "PASS", assertions = assertions - before });
            Console.WriteLine("PASS " + names[caseIndex]);
        }
        catch (Exception error)
        {
            outcomes.Add(new { name = names[caseIndex], state = "FAIL", assertions = assertions - before, failure = error.ToString() });
            Console.WriteLine("FAIL " + names[caseIndex]);
            throw;
        }
    }
    if (mode == "seed") File.WriteAllText(receiptPath, JsonSerializer.Serialize(new { storeId = identity.StoreId, cases = seedRows }, json));
    exit = 0;
}
catch (Exception error) { Console.WriteLine(error); }
finally
{
    // No source/store deletion. Actual pooled connections close before process exits;
    // parent Commands proves actual process/family termination before fresh verify.
    SqliteConnection.ClearAllPools();
    File.WriteAllText(Path.Combine(folder, mode + "-results.json"), JsonSerializer.Serialize(new {
        scope = "Actual local canonical SQLite owner only; no registered Chat/controller/catalog/executor/provider/Den/permissions/browser acceptance",
        mode, discovered = 3, executed = outcomes.Count, passed = outcomes.Count(x => JsonSerializer.SerializeToElement(x).GetProperty("state").GetString() == "PASS"),
        failed = outcomes.Count(x => JsonSerializer.SerializeToElement(x).GetProperty("state").GetString() == "FAIL"), notRun = 3 - outcomes.Count,
        assertions, outcomes, exitCode = exit }, json));
}
return exit;

sealed class FixturePaths(string directory) : IAppPaths
{
    public string DataDirectory => directory;
    public string DatabasePath => Path.Combine(directory, "chat.db");
    public string BrowserProfileDirectory => Path.Combine(directory, "unused-browser");
    public string AttachmentsDirectory => Path.Combine(directory, "unused-attachments");
    public string LogsDirectory => Path.Combine(directory, "unused-logs");
    public string LegacyStatePath => Path.Combine(directory, "absent-legacy.json");
}
