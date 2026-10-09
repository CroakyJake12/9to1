using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace HavenOS.Apps.Stacks.Tests;

public sealed class StackOriginalSourceStoreTests
{
    private static readonly StackActor Actor = new("original-source-fixture-owner",
        new HashSet<StackCapability> { StackCapability.ViewSource, StackCapability.Contribute, StackCapability.CreateDomain });
    private static string PathAt(string directory, string relative) => Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
    private static async Task<(StackEngine Engine, string Directory)> ProjectAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "9to1-stacks-cas-fixtures", Guid.NewGuid().ToString("N"));
        var engine = new StackEngine(new JsonFileStackProjectStore(directory));
        await engine.CreateProjectAsync(new("Original source", StackStorageMode.Files, directory), Actor);
        return (engine, directory);
    }
    private static StackMutation Text(string path, string text) => new(StackMutationKind.Upsert, path, new(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task Independent_engine_stale_working_write_and_commit_are_refused_before_any_source_or_journal_change()
    {
        var (first, directory) = await ProjectAsync(); var main = await first.OpenProjectAsync();
        var second = new StackEngine(new JsonFileStackProjectStore(directory)); await second.OpenProjectAsync();
        await first.ApplyChangeAsync(main.Id, Text("source.txt", "accepted first working bytes"), Actor);
        var accepted = await File.ReadAllBytesAsync(PathAt(directory, JsonFileStackProjectStore.ManifestRelativePath));
        var roots = await File.ReadAllBytesAsync(PathAt(directory, JsonFileStackProjectStore.RootsRelativePath));
        var journalCount = Directory.GetDirectories(PathAt(directory, JsonFileStackProjectStore.OriginalJournalRelativePath)).Length;
        var stale = await Assert.ThrowsAsync<StackFailureException>(() => second.ApplyChangeAsync(main.Id, Text("source.txt", "stale overwrite"), Actor));
        Assert.Equal(StackFailureCode.RevisionConflict, stale.Code);
        var staleCommit = await Assert.ThrowsAsync<StackFailureException>(() => second.CreateCommitAsync(main.Id, "Commit stale source", Actor));
        Assert.Equal(StackFailureCode.RevisionConflict, staleCommit.Code);
        Assert.Equal(accepted, await File.ReadAllBytesAsync(PathAt(directory, JsonFileStackProjectStore.ManifestRelativePath)));
        Assert.Equal(roots, await File.ReadAllBytesAsync(PathAt(directory, JsonFileStackProjectStore.RootsRelativePath)));
        Assert.Equal(journalCount, Directory.GetDirectories(PathAt(directory, JsonFileStackProjectStore.OriginalJournalRelativePath)).Length);
        var reopened = await second.OpenProjectAsync();
        Assert.Equal("accepted first working bytes", Encoding.UTF8.GetString(Assert.Single(reopened.WorkingChanges).Resource!.Content));
        await second.CreateCommitAsync(main.Id, "Commit the actually reopened source", Actor);
        var saved = await new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync();
        Assert.Empty(saved.WorkingChanges); Assert.Equal("accepted first working bytes", Encoding.UTF8.GetString(Assert.Single(saved.LocalChanges).Resource!.Content));
    }

    [Fact]
    public async Task Actual_working_change_invalidates_full_source_even_when_commit_revision_sequence_did_not_advance()
    {
        var (engine, directory) = await ProjectAsync(); var main = await engine.OpenProjectAsync();
        var source = new JsonFileStackProjectStore(directory); var loaded = await source.LoadOriginalAsync();
        var revision = loaded.Manifest.RevisionSequence;
        await engine.ApplyChangeAsync(main.Id, Text("pending.txt", "working"), Actor);
        Assert.Equal(revision, (await new JsonFileStackProjectStore(directory).LoadAsync()).RevisionSequence);
        loaded.Manifest.Name = "Stale rename";
        var failure = await Assert.ThrowsAsync<StackFailureException>(() => source.SaveOriginalAsync(loaded.Manifest, loaded.Revision));
        Assert.Equal(StackFailureCode.RevisionConflict, failure.Code);
        var actual = await new JsonFileStackProjectStore(directory).LoadAsync();
        Assert.Equal("Original source", actual.Name); Assert.Equal("working", Encoding.UTF8.GetString(actual.Domains.Single().WorkingChanges["pending.txt"].Resource!.Content));
    }

    [Fact]
    public async Task Unknown_root_domain_resource_and_revision_json_survive_real_edit_commit_and_cold_open()
    {
        var (_, directory) = await ProjectAsync();
        var manifestPath = PathAt(directory, JsonFileStackProjectStore.ManifestRelativePath);
        var rootsPath = PathAt(directory, JsonFileStackProjectStore.RootsRelativePath);
        var document = JsonNode.Parse(await File.ReadAllBytesAsync(manifestPath))!.AsObject();
        document["futureManifest"] = JsonNode.Parse("{\"nested\":[1,{\"keep\":true}]}");
        var domain = document["domains"]!.AsArray()[0]!.AsObject();
        domain["id"] = domain["id"]!.GetValue<string>().ToUpperInvariant();
        domain["futureDomain"] = "preserved domain value";
        domain["workingChanges"]!["unknowns.txt"] = JsonNode.Parse("{\"kind\":\"upsert\",\"path\":\"unknowns.txt\",\"resource\":{\"content\":\"Zmlyc3Q=\",\"visibility\":\"private\",\"isBinary\":false,\"futureResource\":\"keep resource data\"},\"renameTo\":null}");
        var initial = document["revisions"]!.AsArray()[0]!.AsObject(); initial["futureRevision"] = JsonNode.Parse("{\"opaque\":\"retained\"}");
        await File.WriteAllTextAsync(manifestPath, document.ToJsonString());
        var roots = JsonNode.Parse(await File.ReadAllBytesAsync(rootsPath))!.AsObject(); roots["futureRoots"] = new JsonArray("keep", 42);
        await File.WriteAllTextAsync(rootsPath, roots.ToJsonString());
        var before = await File.ReadAllBytesAsync(manifestPath); var beforeRoots = await File.ReadAllBytesAsync(rootsPath);
        var engine = new StackEngine(new JsonFileStackProjectStore(directory)); var main = await engine.OpenProjectAsync();
        // A real no-change source read keeps exact raw bytes, including unknown properties and whitespace.
        await engine.GetEffectiveTreeAsync(main.Id, Actor);
        Assert.Equal(before, await File.ReadAllBytesAsync(manifestPath)); Assert.Equal(beforeRoots, await File.ReadAllBytesAsync(rootsPath));
        await engine.ApplyChangeAsync(main.Id, Text("unknowns.txt", "second"), Actor);
        await engine.CreateCommitAsync(main.Id, "Commit while preserving future fields", Actor);
        var saved = JsonNode.Parse(await File.ReadAllBytesAsync(manifestPath))!;
        Assert.True(JsonNode.DeepEquals(document["futureManifest"], saved["futureManifest"]));
        Assert.Equal("preserved domain value", saved["domains"]![0]!["futureDomain"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(initial["futureRevision"], saved["revisions"]![0]!["futureRevision"]));
        Assert.Equal("keep resource data", saved["domains"]![0]!["localChanges"]!["unknowns.txt"]!["resource"]!["futureResource"]!.GetValue<string>());
        var rootsAfter = JsonNode.Parse(await File.ReadAllBytesAsync(rootsPath))!;
        Assert.True(JsonNode.DeepEquals(roots["futureRoots"], rootsAfter["futureRoots"]));
        var cold = await new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync();
        Assert.Equal(main.ProjectId, cold.ProjectId); Assert.Equal("second", Encoding.UTF8.GetString(Assert.Single(cold.LocalChanges).Resource!.Content));
    }

    [Fact]
    public async Task Actual_held_exclusive_fence_refuses_other_store_and_preserves_source_until_its_real_release()
    {
        var (_, directory) = await ProjectAsync(); var path = PathAt(directory, JsonFileStackProjectStore.ManifestRelativePath);
        var before = await File.ReadAllBytesAsync(path); var source = new JsonFileStackProjectStore(directory);
        var fence = new FileStream(PathAt(directory, JsonFileStackProjectStore.OriginalFenceRelativePath), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var refused = source.LoadOriginalAsync(); var failure = await Assert.ThrowsAsync<StackFailureException>(() => refused);
        Assert.Equal(StackFailureCode.RevisionConflict, failure.Code); Assert.IsType<IOException>(failure.InnerException);
        Assert.True(refused.IsFaulted); Assert.Equal(before, await File.ReadAllBytesAsync(path));
        var originalClose = fence.DisposeAsync().AsTask(); await originalClose; Assert.True(originalClose.IsCompletedSuccessfully);
        var reopened = await source.LoadOriginalAsync(); Assert.Equal("Original source", reopened.Manifest.Name);
    }

    [Fact]
    public async Task Journal_preserves_full_accepted_predecessor_bytes_and_completed_actual_source_reopens_without_replay()
    {
        var (engine, directory) = await ProjectAsync(); var main = await engine.OpenProjectAsync();
        var manifestPath = PathAt(directory, JsonFileStackProjectStore.ManifestRelativePath); var rootsPath = PathAt(directory, JsonFileStackProjectStore.RootsRelativePath);
        var before = await File.ReadAllBytesAsync(manifestPath); var rootsBefore = await File.ReadAllBytesAsync(rootsPath);
        await engine.ApplyChangeAsync(main.Id, Text("journal.txt", "retained"), Actor);
        var journal = Assert.Single(Directory.GetDirectories(PathAt(directory, JsonFileStackProjectStore.OriginalJournalRelativePath)));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(journal, "manifest.before.json")));
        Assert.Equal(rootsBefore, await File.ReadAllBytesAsync(Path.Combine(journal, "roots.before.json")));
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(journal, "intent.json")), await File.ReadAllBytesAsync(Path.Combine(journal, "completed.json")));
        var after = await File.ReadAllBytesAsync(manifestPath);
        var cold = await new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync();
        Assert.Equal(main.ProjectId, cold.ProjectId); Assert.Single(cold.WorkingChanges);
        Assert.Equal(after, await File.ReadAllBytesAsync(manifestPath)); Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(journal, "manifest.before.json")));
    }

    [Fact]
    public async Task Cold_incomplete_journal_refuses_read_and_write_without_ack_retry_restore_or_deleting_any_bytes()
    {
        var (_, directory) = await ProjectAsync(); var source = new JsonFileStackProjectStore(directory); var original = await source.LoadOriginalAsync();
        var manifestPath = PathAt(directory, JsonFileStackProjectStore.ManifestRelativePath); var rootsPath = PathAt(directory, JsonFileStackProjectStore.RootsRelativePath);
        var before = await File.ReadAllBytesAsync(manifestPath); var roots = await File.ReadAllBytesAsync(rootsPath);
        var journal = PathAt(directory, JsonFileStackProjectStore.OriginalJournalRelativePath + "/held-original-fixture"); Directory.CreateDirectory(journal);
        await File.WriteAllBytesAsync(Path.Combine(journal, "manifest.before.json"), before);
        await File.WriteAllBytesAsync(Path.Combine(journal, "roots.before.json"), roots);
        await File.WriteAllTextAsync(Path.Combine(journal, "intent.json"), "{\"unsettledOriginal\":true}");
        var preservedJournal = await File.ReadAllBytesAsync(Path.Combine(journal, "intent.json"));
        var cold = new JsonFileStackProjectStore(directory);
        var failedRead = await Assert.ThrowsAsync<StackFailureException>(() => cold.LoadOriginalAsync()); Assert.Equal(StackFailureCode.RecoveryStateUncertain, failedRead.Code);
        original.Manifest.Name = "Must not publish";
        var failedWrite = await Assert.ThrowsAsync<StackFailureException>(() => source.SaveOriginalAsync(original.Manifest, original.Revision)); Assert.Equal(StackFailureCode.RecoveryStateUncertain, failedWrite.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(manifestPath)); Assert.Equal(roots, await File.ReadAllBytesAsync(rootsPath));
        Assert.Equal(preservedJournal, await File.ReadAllBytesAsync(Path.Combine(journal, "intent.json"))); Assert.False(File.Exists(Path.Combine(journal, "completed.json")));
    }

    [Fact]
    public async Task Completed_journal_does_not_acknowledge_a_lost_actual_latest_publication_or_replay_its_retained_output()
    {
        var (engine, directory) = await ProjectAsync(); var main = await engine.OpenProjectAsync();
        var path = PathAt(directory, JsonFileStackProjectStore.ManifestRelativePath); var predecessor = await File.ReadAllBytesAsync(path);
        await engine.ApplyChangeAsync(main.Id, Text("accepted.txt", "latest accepted bytes"), Actor);
        var journal = Assert.Single(Directory.GetDirectories(PathAt(directory, JsonFileStackProjectStore.OriginalJournalRelativePath)));
        var latest = await File.ReadAllBytesAsync(path);
        // Fixture simulates a completed marker surviving while the live rename was lost.
        await File.WriteAllBytesAsync(path, predecessor);
        var cold = new JsonFileStackProjectStore(directory);
        var refusal = await Assert.ThrowsAsync<StackFailureException>(() => cold.LoadOriginalAsync());
        Assert.Equal(StackFailureCode.RecoveryStateUncertain, refusal.Code);
        Assert.Equal(predecessor, await File.ReadAllBytesAsync(path));
        Assert.Equal(latest, await File.ReadAllBytesAsync(Path.Combine(journal, "manifest.after.json")));
        Assert.True(File.Exists(Path.Combine(journal, "completed.json")));
    }

    [Fact]
    public async Task Foreign_store_receipt_and_changed_unknown_raw_bytes_cannot_authorize_publication()
    {
        var (_, directory) = await ProjectAsync(); var source = new JsonFileStackProjectStore(directory); var loaded = await source.LoadOriginalAsync();
        var other = new JsonFileStackProjectStore(directory);
        var foreign = await Assert.ThrowsAsync<StackFailureException>(() => other.SaveOriginalAsync(loaded.Manifest, loaded.Revision)); Assert.Equal(StackFailureCode.RevisionConflict, foreign.Code);
        var path = PathAt(directory, JsonFileStackProjectStore.ManifestRelativePath); var raw = JsonNode.Parse(await File.ReadAllBytesAsync(path))!;
        raw["newUnknownProperty"] = "actual source change"; await File.WriteAllTextAsync(path, raw.ToJsonString()); var accepted = await File.ReadAllBytesAsync(path);
        var stale = await Assert.ThrowsAsync<StackFailureException>(() => source.SaveOriginalAsync(loaded.Manifest, loaded.Revision)); Assert.Equal(StackFailureCode.RevisionConflict, stale.Code);
        Assert.Equal(accepted, await File.ReadAllBytesAsync(path)); Assert.False(Directory.Exists(PathAt(directory, JsonFileStackProjectStore.OriginalJournalRelativePath)));
    }

    [Fact]
    public async Task Read_only_snapshot_mutation_cannot_change_cached_source_or_persist_its_bytes()
    {
        var (engine, directory) = await ProjectAsync(); var main = await engine.OpenProjectAsync();
        await engine.ApplyChangeAsync(main.Id, Text("committed.txt", "accepted committed bytes"), Actor);
        await engine.CreateCommitAsync(main.Id, "Accept original committed bytes", Actor);
        await engine.ApplyChangeAsync(main.Id, Text("working.txt", "accepted working bytes"), Actor);
        var path = PathAt(directory, JsonFileStackProjectStore.ManifestRelativePath);
        var rootsPath = PathAt(directory, JsonFileStackProjectStore.RootsRelativePath);
        var before = await File.ReadAllBytesAsync(path); var beforeRoots = await File.ReadAllBytesAsync(rootsPath);
        // Open returns a snapshot over the actual cached manifest. Every byte array must detach.
        var snapshot = await engine.OpenProjectAsync();
        foreach (var change in snapshot.LocalChanges) Array.Fill(change.Resource!.Content, (byte)'x');
        foreach (var change in snapshot.WorkingChanges) Array.Fill(change.Resource!.Content, (byte)'y');
        var reader = new StackActor("view-only-source-fixture", new HashSet<StackCapability> { StackCapability.ViewSource });
        var actual = await engine.GetEffectiveTreeAsync(main.Id, reader);
        Assert.Equal("accepted committed bytes", Encoding.UTF8.GetString(actual.Files["committed.txt"].Content));
        Assert.Equal("accepted working bytes", Encoding.UTF8.GetString(actual.Files["working.txt"].Content));
        Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.Equal(beforeRoots, await File.ReadAllBytesAsync(rootsPath));
        var cold = await new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync();
        Assert.Equal("accepted working bytes", Encoding.UTF8.GetString(Assert.Single(cold.WorkingChanges).Resource!.Content));
    }

    [Fact]
    public async Task Proposal_is_detached_before_waiting_and_later_caller_project_or_nested_mutation_cannot_replace_it()
    {
        var (_, directory) = await ProjectAsync(); var source = new JsonFileStackProjectStore(directory);
        var loaded = await source.LoadOriginalAsync(); var projectId = loaded.Manifest.ProjectId;
        loaded.Manifest.Name = "Captured proposal";
        loaded.Manifest.Domains.Single().WorkingChanges["captured.txt"] = Text("captured.txt", "captured bytes");
        var gate = Assert.IsType<SemaphoreSlim>(typeof(JsonFileStackProjectStore)
            .GetField("_originalSourceGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source));
        await gate.WaitAsync(); Task<IStackOriginalSourceRevision> original;
        try
        {
            original = source.SaveOriginalAsync(loaded.Manifest, loaded.Revision);
            Assert.False(original.IsCompleted);
            loaded.Manifest.ProjectId = Guid.NewGuid(); loaded.Manifest.Name = "Must not publish";
            Array.Fill(loaded.Manifest.Domains.Single().WorkingChanges["captured.txt"].Resource!.Content, (byte)'z');
        }
        finally { gate.Release(); }
        var nextReceipt = await original; Assert.True(original.IsCompletedSuccessfully); Assert.NotSame(loaded.Revision, nextReceipt);
        var actual = await new JsonFileStackProjectStore(directory).LoadOriginalAsync();
        Assert.Equal(projectId, actual.Manifest.ProjectId); Assert.Equal("Captured proposal", actual.Manifest.Name);
        Assert.Equal("captured bytes", Encoding.UTF8.GetString(actual.Manifest.Domains.Single().WorkingChanges["captured.txt"].Resource!.Content));
        var journal = Assert.Single(Directory.GetDirectories(PathAt(directory, JsonFileStackProjectStore.OriginalJournalRelativePath)));
        var intent = JsonNode.Parse(await File.ReadAllBytesAsync(Path.Combine(journal, "intent.json")))!;
        Assert.Equal(projectId, intent["ProjectId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Failed_actual_file_read_retains_same_raw_task_and_its_original_envelope()
    {
        var (_, directory) = await ProjectAsync(); var source = new JsonFileStackProjectStore(directory);
        // Hold a genuine exclusive read source, preserving the complete accepted project.
        var roots = PathAt(directory, JsonFileStackProjectStore.RootsRelativePath);
        var heldSource = new FileStream(roots, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var original = source.LoadOriginalAsync(); var observed = await Assert.ThrowsAnyAsync<Exception>(() => original);
        Assert.True(original.IsFaulted); Assert.NotNull(observed);
        var reads = Assert.IsType<List<Task<byte[]>>>(typeof(JsonFileStackProjectStore)
            .GetField("_originalReads", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source));
        var raw = Assert.Single(reads); Assert.True(raw.IsFaulted);
        var rawEnvelope = Assert.IsType<AggregateException>(raw.Exception);
        Assert.IsType<IOException>(Assert.Single(rawEnvelope.InnerExceptions));
        var retained = Assert.IsType<List<object>>(typeof(JsonFileStackProjectStore)
            .GetField("_heldOriginalFailures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source));
        var receipt = Assert.Single(retained.OfType<ValueTuple<string, Task<byte[]>, Exception>>(),
            item => ReferenceEquals(item.Item2, raw));
        var heldEnvelope = Assert.IsType<AggregateException>(receipt.Item3);
        Assert.Same(Assert.Single(rawEnvelope.InnerExceptions), Assert.Single(heldEnvelope.InnerExceptions));
        var actualRelease = heldSource.DisposeAsync().AsTask(); await actualRelease;
        Assert.True(actualRelease.IsCompletedSuccessfully); Assert.True(File.Exists(roots));
        var reopened = await source.LoadOriginalAsync(); Assert.Equal("Original source", reopened.Manifest.Name);
        Assert.Same(raw, Assert.Single(reads));
        Assert.Same(Assert.Single(heldEnvelope.InnerExceptions), Assert.Single(raw.Exception!.InnerExceptions));
    }

}
