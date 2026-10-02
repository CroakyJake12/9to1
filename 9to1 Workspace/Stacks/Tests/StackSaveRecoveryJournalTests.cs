using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace HavenOS.Apps.Stacks.Tests;

public sealed class StackSaveRecoveryJournalTests
{
    [Fact]
    public async Task Successful_both_file_save_removes_journal_and_reloads_exact_root_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-stack-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileStackProjectStore(directory);
            var engine = new StackEngine(store);
            var main = await engine.CreateProjectAsync(new("Journal fixture", StackStorageMode.Files, directory), StackActor.System);
            var manifest = await store.LoadAsync();
            var root = new StackRoot(Guid.NewGuid(), main.Id, ["fixture.txt"], DateTimeOffset.UtcNow, "controlled-fixture");
            manifest.Roots.Add(root);
            await store.SaveAsync(manifest);
            Assert.False(File.Exists(Path.Combine(directory, JsonFileStackProjectStore.PendingSaveRecoveryRelativePath)));
            Assert.Equal(root.Id, Assert.Single((await new JsonFileStackProjectStore(directory).LoadAsync()).Roots).Id);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath)))).ToLowerInvariant(), store.LoadedRevisionToken);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Original_binding_failure_after_roots_preserves_both_sides_and_blocks_read_or_replay()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-stack-save-fault-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new JsonFileStackProjectStore(directory);
            var engine = new StackEngine(first);
            var main = await engine.CreateProjectAsync(new("Journal fault fixture", StackStorageMode.Files, directory), StackActor.System);
            var manifestPath = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath);
            var rootsPath = Path.Combine(directory, JsonFileStackProjectStore.RootsRelativePath);
            var ordinaryPath = Path.Combine(directory, "ordinary-source.txt");
            await File.WriteAllBytesAsync(ordinaryPath, "ordinary source remains unchanged"u8.ToArray());
            var originalManifest = await File.ReadAllBytesAsync(manifestPath);
            var originalRoots = await File.ReadAllBytesAsync(rootsPath);
            var checks = 0;
            var fault = new JsonFileStackProjectStore(directory, _ =>
            {
                if (++checks == 3) throw new IOException("Controlled original binding failure after actual roots publication");
                return Task.CompletedTask;
            });
            var proposed = await fault.LoadAsync();
            proposed.Name = "Unacknowledged proposal";
            proposed.Roots.Add(new(Guid.NewGuid(), main.Id, ["ordinary-source.txt"], DateTimeOffset.UtcNow, "controlled-fixture"));
            var failed = await Assert.ThrowsAsync<StackFailureException>(() => fault.SaveAsync(proposed));
            Assert.Equal(StackFailureCode.MaterialisationFailed, failed.Code);
            Assert.Equal(originalManifest, await File.ReadAllBytesAsync(manifestPath));
            var observedRoots = await File.ReadAllBytesAsync(rootsPath);
            Assert.False(originalRoots.SequenceEqual(observedRoots));
            var journalPath = Path.Combine(directory, JsonFileStackProjectStore.PendingSaveRecoveryRelativePath);
            var journalBytes = await File.ReadAllBytesAsync(journalPath);
            using var journal = JsonDocument.Parse(journalBytes);
            Assert.Equal(originalManifest, journal.RootElement.GetProperty("previousManifest").GetBytesFromBase64());
            Assert.Equal(originalRoots, journal.RootElement.GetProperty("previousRoots").GetBytesFromBase64());
            Assert.Equal(observedRoots, journal.RootElement.GetProperty("nextRoots").GetBytesFromBase64());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(originalManifest)).ToLowerInvariant(), journal.RootElement.GetProperty("previousManifestSha256").GetString());
            var inspection = await new JsonFileStackProjectStore(directory).InspectPendingSaveAsync();
            Assert.NotNull(inspection);
            Assert.Equal(main.ProjectId, inspection.ProjectId);
            Assert.Equal(inspection, await new JsonFileStackProjectStore(directory).InspectPendingSaveAsync(main.ProjectId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new JsonFileStackProjectStore(directory).InspectPendingSaveAsync(Guid.NewGuid()));

            Assert.True(inspection.ManifestMatchesPrevious);
            Assert.False(inspection.ManifestMatchesProposed);
            Assert.False(inspection.RootsMatchPrevious);
            Assert.True(inspection.RootsMatchProposed);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(journalBytes)).ToLowerInvariant(), inspection.RecoveryEvidenceSha256);
            Assert.Equal(journalBytes, await File.ReadAllBytesAsync(journalPath));
            var refusedRead = await Assert.ThrowsAsync<StackFailureException>(() => new JsonFileStackProjectStore(directory).LoadAsync());
            Assert.Equal(StackFailureCode.RecoveryStateUncertain, refusedRead.Code);
            var refusedReplay = await Assert.ThrowsAsync<StackFailureException>(() => fault.SaveAsync(proposed));
            Assert.Equal(StackFailureCode.RecoveryStateUncertain, refusedReplay.Code);
            Assert.Equal(originalManifest, await File.ReadAllBytesAsync(manifestPath));
            Assert.Equal(observedRoots, await File.ReadAllBytesAsync(rootsPath));
            Assert.Equal(journalBytes, await File.ReadAllBytesAsync(journalPath));
            Assert.Equal("ordinary source remains unchanged", await File.ReadAllTextAsync(ordinaryPath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("journal-project")]
    [InlineData("previous-project")]
    [InlineData("proposed-root-owner")]
    public async Task Read_only_inspection_refuses_self_consistent_hashes_with_foreign_canonical_identity(string change)
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-stack-journal-identity-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new JsonFileStackProjectStore(directory);
            var main = await new StackEngine(first).CreateProjectAsync(new("Identity fixture", StackStorageMode.Files, directory), StackActor.System);
            var checks = 0;
            var fault = new JsonFileStackProjectStore(directory, _ =>
            {
                if (++checks == 3) throw new IOException("Controlled original binding failure after roots publication");
                return Task.CompletedTask;
            });
            var proposed = await fault.LoadAsync();
            proposed.Roots.Add(new(Guid.NewGuid(), main.Id, ["source.txt"], DateTimeOffset.UtcNow, "controlled-fixture"));
            await Assert.ThrowsAsync<StackFailureException>(() => fault.SaveAsync(proposed));
            var journalPath = Path.Combine(directory, JsonFileStackProjectStore.PendingSaveRecoveryRelativePath);
            var journal = JsonNode.Parse(await File.ReadAllBytesAsync(journalPath))!;
            if (change == "journal-project") journal["projectId"] = Guid.NewGuid().ToString("D");
            else
            {
                var field = change == "previous-project" ? "previousManifest" : "nextRoots";
                var payload = JsonNode.Parse(Convert.FromBase64String(journal[field]!.GetValue<string>()))!;
                if (change == "previous-project") payload["projectId"] = Guid.NewGuid().ToString("D");
                else payload["roots"]![0]!["ownerDomainId"] = Guid.NewGuid().ToString("D");
                var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
                journal[field] = Convert.ToBase64String(bytes);
                journal[field + "Sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            }
            await File.WriteAllBytesAsync(journalPath, JsonSerializer.SerializeToUtf8Bytes(journal));
            var before = await File.ReadAllBytesAsync(journalPath);
            var manifestPath = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath);
            var rootsPath = Path.Combine(directory, JsonFileStackProjectStore.RootsRelativePath);
            var manifestBefore = await File.ReadAllBytesAsync(manifestPath);
            var rootsBefore = await File.ReadAllBytesAsync(rootsPath);
            var failed = await Assert.ThrowsAsync<StackFailureException>(() => new JsonFileStackProjectStore(directory).InspectPendingSaveAsync());
            Assert.Equal(StackFailureCode.SourceCorrupt, failed.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(journalPath));
            Assert.Equal(manifestBefore, await File.ReadAllBytesAsync(manifestPath));
            Assert.Equal(rootsBefore, await File.ReadAllBytesAsync(rootsPath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

}
