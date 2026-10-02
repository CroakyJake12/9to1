using System.Security.Cryptography;
using Xunit;

namespace HavenOS.Apps.Stacks.Tests;

public sealed class StackCanonicalConcurrencyTests
{
    [Fact]
    public async Task Independently_opened_engine_cannot_overwrite_newer_canonical_domain_and_can_reload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-stack-cas-" + Guid.NewGuid().ToString("N"));
        var actor = new StackActor("controlled-maintainer", Enum.GetValues<StackCapability>().ToHashSet());
        try
        {
            var first = new StackEngine(new JsonFileStackProjectStore(directory));
            var main = await first.CreateProjectAsync(new("CAS fixture", StackStorageMode.Files, directory), actor);
            var second = new StackEngine(new JsonFileStackProjectStore(directory));
            await second.OpenProjectAsync();
            Assert.Equal(first.LoadedRevisionToken, second.LoadedRevisionToken);
            var committed = await first.CreateBranchAsync(main.ProjectId, "First writer", actor);
            var manifest = Path.Combine(directory, ".branches", "stack.manifest.json");
            var roots = Path.Combine(directory, ".roots", "roots.json");
            var manifestBefore = await File.ReadAllBytesAsync(manifest); var rootsBefore = await File.ReadAllBytesAsync(roots);
            var rejected = await Assert.ThrowsAsync<StackFailureException>(() => second.CreateBranchAsync(main.ProjectId, "Stale writer", actor));
            Assert.Equal(StackFailureCode.RevisionConflict, rejected.Code);
            Assert.Equal(manifestBefore, await File.ReadAllBytesAsync(manifest));
            Assert.Equal(rootsBefore, await File.ReadAllBytesAsync(roots));
            await second.OpenProjectAsync();
            Assert.Contains(await second.GetLineageAsync(), domain => domain.Id == committed.Id);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(manifestBefore)).ToLowerInvariant(), second.LoadedRevisionToken);
            var next = await second.CreateBranchAsync(main.ProjectId, "Fresh proposal", actor);
            await first.OpenProjectAsync();
            Assert.Contains(await first.GetLineageAsync(), domain => domain.Id == next.Id);
            Assert.DoesNotContain(await first.GetLineageAsync(), domain => domain.Name == "Stale writer");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Recorded_source_history_and_activity_reads_preserve_actual_metadata_bytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-stack-read-" + Guid.NewGuid().ToString("N"));
        var actor = new StackActor("controlled-maintainer", Enum.GetValues<StackCapability>().ToHashSet());
        try
        {
            var engine = new StackEngine(new JsonFileStackProjectStore(directory));
            var main = await engine.CreateProjectAsync(new("Read fixture", StackStorageMode.Files, directory), actor);
            await engine.ApplyChangeAsync(main.Id, new(StackMutationKind.Upsert, "actual.txt", new("recorded"u8.ToArray())), actor);
            var commit = await engine.CreateCommitAsync(main.Id, "Recorded commit", actor);
            var manifest = Path.Combine(directory, ".branches", "stack.manifest.json");
            var before = await File.ReadAllBytesAsync(manifest);
            var reader = new StackActor(actor.ActorId, new HashSet<StackCapability> { StackCapability.ViewSource });
            Assert.Equal("recorded", System.Text.Encoding.UTF8.GetString((await engine.InspectRecordedTreeAsync(main.Id, reader)).Files["actual.txt"].Content));
            Assert.Contains(await engine.GetActivityAsync(reader), activity => activity.Action == "CommitCreated");
            var history = await engine.GetRevisionHistoryAsync(main.Id, reader);
            Assert.Contains(history, revision => revision.Id == commit.Id);
            Assert.Equal(before, await File.ReadAllBytesAsync(manifest));
            var changedPaths = Assert.IsAssignableFrom<IList<string>>(history.Single(revision => revision.Id == commit.Id).ChangedPaths);
            Assert.Throws<NotSupportedException>(() => changedPaths[0] = "forged.txt");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Opening_legacy_project_for_recorded_read_does_not_create_coordination_metadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-stack-legacy-read-" + Guid.NewGuid().ToString("N"));
        var actor = new StackActor("controlled-maintainer", Enum.GetValues<StackCapability>().ToHashSet());
        try
        {
            var writer = new StackEngine(new JsonFileStackProjectStore(directory));
            var main = await writer.CreateProjectAsync(new("Legacy fixture", StackStorageMode.Files, directory), actor);
            await writer.ApplyChangeAsync(main.Id, new(StackMutationKind.Upsert, "actual.txt", new("legacy"u8.ToArray())), actor);
            await writer.CreateCommitAsync(main.Id, "Legacy source", actor);
            // A pre-coordination canonical project is a supported read input, not permission
            // to migrate metadata. Only this fixture removes the newly seeded lock.
            var lockPath = Path.Combine(directory, ".branches", "storage.lock");
            File.Delete(lockPath);
            var before = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(directory, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            var reader = new StackEngine(new JsonFileStackProjectStore(directory));
            await reader.OpenProjectAsync();
            var readActor = new StackActor(actor.ActorId, new HashSet<StackCapability> { StackCapability.ViewSource });
            Assert.Equal("legacy", System.Text.Encoding.UTF8.GetString((await reader.InspectRecordedTreeAsync(main.Id, readActor)).Files["actual.txt"].Content));
            Assert.False(File.Exists(lockPath));
            var after = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(directory, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            Assert.Equal(before.Count, after.Count);
            foreach (var file in before) Assert.Equal(file.Value, after[file.Key]);
            // An explicitly invoked owner write may introduce coordination metadata.
            await reader.CreateBranchAsync(main.ProjectId, "Owner write", actor);
            Assert.True(File.Exists(lockPath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

}
