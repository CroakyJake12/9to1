using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Core.Tests;

public sealed class CheckpointServiceTests
{
    private sealed class FakeRepository : ICheckpointRepository
    {
        public CheckpointInfo? Saved;
        public long LatestSequence = 5;
        public List<WorkspaceRestoreEntry> Versions { get; } = [];

        public Task SaveAsync(CheckpointInfo checkpoint, CancellationToken cancellationToken)
        {
            Saved = checkpoint;
            return Task.CompletedTask;
        }
        public Task<CheckpointInfo?> GetLatestAsync(Guid? conversationId, string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(Saved);
        public Task<CheckpointInfo?> GetAsync(Guid checkpointId, CancellationToken cancellationToken)
            => Task.FromResult(Saved is not null && Saved.Id == checkpointId ? Saved : null);
        public Task<long> GetLatestVersionSequenceAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(LatestSequence);
        public Task<IReadOnlyList<WorkspaceRestoreEntry>> GetVersionsSinceAsync(string workspaceRoot, long sequence, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WorkspaceRestoreEntry>>(Versions.Where(item => item.Sequence > sequence).ToList());
        public Task<WorkspaceRestoreEntry?> GetLatestVersionAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(Versions.OrderByDescending(item => item.Sequence).FirstOrDefault());
    }

    private sealed class FakeRestorer : ICheckpointRestorer
    {
        public CheckpointRestorePlan? LastPlan;
        public IReadOnlyList<string> Restored = [];
        public Task<IReadOnlyList<string>> RestoreAsync(string workspaceRoot, CheckpointRestorePlan plan, CancellationToken cancellationToken)
        {
            LastPlan = plan;
            return Task.FromResult(Restored);
        }
    }

    [Fact]
    public async Task OffModeNeverCreatesCheckpoints()
    {
        var repository = new FakeRepository();
        var service = new CheckpointService(repository, new FakeRestorer());

        var checkpoint = await service.EnsureBeforeMutationAsync(
            Guid.NewGuid(), null, null, "C:\\ws", CheckpointMode.Off, CancellationToken.None);

        Assert.Null(checkpoint);
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task OneCheckpointPerExecutionIsReused()
    {
        var repository = new FakeRepository();
        var service = new CheckpointService(repository, new FakeRestorer());
        var executionId = Guid.NewGuid();

        var first = await service.EnsureBeforeMutationAsync(executionId, null, null, "C:\\ws", CheckpointMode.AgenticTasks, CancellationToken.None);
        var second = await service.EnsureBeforeMutationAsync(executionId, null, null, "C:\\ws", CheckpointMode.AgenticTasks, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(5, first!.StartSequence);
    }

    [Fact]
    public async Task RestorePlanAppliesLatestBeforeContentPerPath()
    {
        var repository = new FakeRepository();
        repository.Versions.AddRange(
        [
            new WorkspaceRestoreEntry(6, "src/a.cs", 0, "original a", "edited a 1"),
            new WorkspaceRestoreEntry(7, "src/b.cs", 0, "original b", "edited b"),
            new WorkspaceRestoreEntry(8, "src/a.cs", 0, "edited a 1", "edited a 2")
        ]);
        var restorer = new FakeRestorer { Restored = ["src/a.cs", "src/b.cs"] };
        var service = new CheckpointService(repository, restorer);
        var checkpointId = Guid.NewGuid();
        repository.Saved = new CheckpointInfo(checkpointId, null, null, "C:\\ws", "label", CheckpointMode.Always, 5, DateTimeOffset.UtcNow);

        var restored = await service.RestoreCheckpointAsync(checkpointId, CancellationToken.None);

        Assert.Equal(2, restored.Count);
        Assert.NotNull(restorer.LastPlan);
        // Historical test identity retained; sequence 6's before-content is the checkpoint-time state.
        Assert.Equal("original a", restorer.LastPlan!.PathToBeforeContent["src/a.cs"]);
        Assert.Equal("original b", restorer.LastPlan.PathToBeforeContent["src/b.cs"]);
    }

    [Fact]
    public async Task UndoLastActionReversesOnlyTheMostRecentMutation()
    {
        var repository = new FakeRepository();
        repository.Versions.Add(new WorkspaceRestoreEntry(9, "src/only.cs", 0, "before", "after"));
        var restorer = new FakeRestorer { Restored = ["src/only.cs"] };
        var service = new CheckpointService(repository, restorer);

        var undone = await service.UndoLastActionAsync("C:\\ws", CancellationToken.None);

        Assert.True(undone);
        Assert.NotNull(restorer.LastPlan);
        var entry = Assert.Single(restorer.LastPlan!.PathToBeforeContent);
        Assert.Equal("src/only.cs", entry.Key);
        Assert.Equal("before", entry.Value);
    }

    [Fact]
    public async Task RestorePlanUsesEarliestSequenceAcrossUnsortedInterleavedEdits()
    {
        var repository = new FakeRepository();
        repository.Versions.AddRange(
        [
            new WorkspaceRestoreEntry(14, "src/a.cs", 0, "edited a 2", "edited a 3"),
            new WorkspaceRestoreEntry(8, "src/b.cs", 0, "original b", "edited b 1"),
            new WorkspaceRestoreEntry(4, "src/a.cs", 0, "before checkpoint a", "original a"),
            new WorkspaceRestoreEntry(6, "src/a.cs", 0, "original a", "edited a 1"),
            new WorkspaceRestoreEntry(11, "src/b.cs", 0, "edited b 1", "edited b 2"),
            new WorkspaceRestoreEntry(10, "src/a.cs", 0, "edited a 1", "edited a 2")
        ]);
        var restorer = new FakeRestorer();
        var service = new CheckpointService(repository, restorer);
        var checkpointId = Guid.NewGuid();
        repository.Saved = new(checkpointId, null, null, "C:\\ws", "label", CheckpointMode.Always, 5, DateTimeOffset.UtcNow);

        var plan = await service.PlanRestoreAsync(checkpointId, CancellationToken.None);

        Assert.Equal(checkpointId, plan.CheckpointId);
        Assert.Equal(2, plan.PathToBeforeContent.Count);
        Assert.Equal("original a", plan.PathToBeforeContent["src/a.cs"]);
        Assert.Equal("original b", plan.PathToBeforeContent["src/b.cs"]);
        Assert.Null(restorer.LastPlan); // Planning remains pure; no recorded original is replayed here.
    }

    [Fact]
    public async Task FullCheckpointRestoreInNonGitWorkspaceUsesOriginalBeforeContentWhileUndoUsesLast()
    {
        var paths = new CheckpointTestPaths();
        var database = new SqliteDatabase(paths);
        Microsoft.Data.Sqlite.SqliteConnection? poolHandle = null;
        OriginalFileSource? source = null;
        Exception? primary = null; var cleanup = new List<Exception>();
        try
        {
            poolHandle = await database.OpenAsync(CancellationToken.None);
            await database.InitializeAsync(CancellationToken.None);
            var root = Path.Combine(paths.DataDirectory, "workspace");
            Directory.CreateDirectory(root);
            var physical = new WorkspaceToolService();
            await physical.WriteTextAtomicAsync(root, "src/a.cs", "original a", CancellationToken.None);
            await physical.WriteTextAtomicAsync(root, "src/b.cs", "original b", CancellationToken.None);
            await physical.WriteTextAtomicAsync(root, "untouched.txt", "keep untouched", CancellationToken.None);
            var history = new WorkspaceStateRepository(database);
            Task Record(string path, string before, string after) => history.AddVersionAsync(
                new(Guid.NewGuid(), null, null, root, path, WorkspaceVersionKind.Edit, before, after, "controlled checkpoint mutation", 0, 0,
                    DateTimeOffset.UtcNow), CancellationToken.None);
            await Record("src/a.cs", "before checkpoint a", "original a");
            var repository = new SqliteCheckpointRepository(database);
            source = new OriginalFileSource(physical);
            var service = new CheckpointService(repository, new WorkspaceCheckpointRestorer(source));
            var checkpoint = await service.EnsureBeforeMutationAsync(Guid.NewGuid(), null, null, root,
                CheckpointMode.BeforeFileChanges, CancellationToken.None);
            Assert.NotNull(checkpoint);
            Assert.Equal(await repository.GetLatestVersionSequenceAsync(root, CancellationToken.None), checkpoint!.StartSequence);
            async Task Edit(string path, string after)
            {
                var before = await physical.ReadTextAsync(root, path, CancellationToken.None);
                await Record(path, before, after);
                await physical.WriteTextAtomicAsync(root, path, after, CancellationToken.None);
            }
            await Edit("src/a.cs", "edited a 1");
            await Edit("src/a.cs", "edited a 2");
            await Edit("src/b.cs", "edited b");
            await Edit("src/a.cs", "edited a 3");
            var originals = await repository.GetVersionsSinceAsync(root, checkpoint.StartSequence, CancellationToken.None);
            Assert.Equal(4, originals.Count);
            Assert.Equal(originals.OrderBy(row => row.Sequence).Select(row => row.Sequence), originals.Select(row => row.Sequence));
            Assert.Equal("original a", originals[0].BeforeContent);
            Assert.Equal("edited a 2", originals[^1].BeforeContent);

            Assert.True(await service.UndoLastActionAsync(root, CancellationToken.None));
            Assert.Equal("edited a 2", await physical.ReadTextAsync(root, "src/a.cs", CancellationToken.None));
            Assert.Equal("edited b", await physical.ReadTextAsync(root, "src/b.cs", CancellationToken.None));
            var restored = await service.RestoreCheckpointAsync(checkpoint.Id, CancellationToken.None);

            Assert.Equal(new[] { "src/a.cs", "src/b.cs" }, restored.OrderBy(path => path, StringComparer.Ordinal));
            Assert.Equal("original a", await physical.ReadTextAsync(root, "src/a.cs", CancellationToken.None));
            Assert.Equal("original b", await physical.ReadTextAsync(root, "src/b.cs", CancellationToken.None));
            Assert.Equal("keep untouched", await physical.ReadTextAsync(root, "untouched.txt", CancellationToken.None));
            Assert.False(Directory.Exists(Path.Combine(root, ".git")));
            Assert.Equal(3, source.OriginalWrites.Count); // One undo and two full-restore original physical writes.
            Assert.All(source.OriginalWrites, actual => Assert.True(actual.IsCompletedSuccessfully));
            Assert.Equal(originals, await repository.GetVersionsSinceAsync(root, checkpoint.StartSequence, CancellationToken.None));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            // A failed byte/assertion check still joins every actual admitted restore
            // write before pool retirement or deleting this owned fixture directory.
            foreach (var actual in source?.OriginalWrites.ToArray() ?? [])
                try { await actual; } catch (Exception error) { cleanup.Add(actual.Exception ?? error); }
            if (poolHandle is not null)
            {
                try { Microsoft.Data.Sqlite.SqliteConnection.ClearPool(poolHandle); } catch (Exception error) { cleanup.Add(error); }
                try { await poolHandle.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
            }
            try { Directory.Delete(paths.DataDirectory, recursive: true); } catch (Exception error) { cleanup.Add(error); }
        }
        if (primary is not null || cleanup.Count != 0)
            throw new AggregateException("Checkpoint fixture assertion or owned cleanup failed.", new[] { primary }.OfType<Exception>().Concat(cleanup));
    }

    // Test-owned actual file source: return and retain the SAME production atomic-write
    // Task. Unsupported discovery/process capabilities fail rather than inventing Git.
    private sealed class OriginalFileSource(WorkspaceToolService physical) : IWorkspaceToolService
    {
        internal List<Task> OriginalWrites { get; } = [];
        public string ResolveWorkspacePath(string root, string path) => physical.ResolveWorkspacePath(root, path);
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => physical.ReadTextAsync(root, path, token);
        public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token)
        {
            var actual = physical.WriteTextAtomicAsync(root, path, content, token);
            OriginalWrites.Add(actual); return actual;
        }
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) =>
            throw new InvalidOperationException("Checkpoint restore has no discovery source.");
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) =>
            throw new InvalidOperationException("Checkpoint restore has no Git or process source.");
    }
    private sealed class CheckpointTestPaths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-checkpoint-restore-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public CheckpointTestPaths() => Directory.CreateDirectory(DataDirectory);
    }
}

public sealed class ProjectAgentInstructionsTests
{
    [Fact]
    public void MergeOrdersBroadestToMostSpecific()
    {
        var merged = ProjectAgentInstructions.Merge(
        [
            new ProjectInstructionFile("tasks/agents/AGENT.md", 2, "Nested rule."),
            new ProjectInstructionFile("AGENT.md", 0, "Root rule."),
            new ProjectInstructionFile("agent.md", 1, "Mid rule.")
        ]);

        Assert.StartsWith("From AGENT.md:\nRoot rule.", merged);
        Assert.Contains("From agent.md:\nMid rule.", merged);
        Assert.EndsWith("Nested rule.", merged);
        Assert.True(merged.IndexOf("Root rule.", StringComparison.Ordinal) < merged.IndexOf("Mid rule.", StringComparison.Ordinal));
    }
}
