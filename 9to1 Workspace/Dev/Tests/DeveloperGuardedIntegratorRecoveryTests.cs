using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HavenOS.Apps.Dev.Autonomous;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperGuardedIntegratorRecoveryTests
{
    [Fact]
    public async Task Refuses_to_replace_integration_branch_that_advanced_after_task_checkout()
    {
        using var repo = new TestRepository();
        var (mission, task) = await repo.CreateTaskAsync("T-stale", "src/feature.txt", "verified\n");
        await repo.AdvanceBranchAsync("build/test");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeveloperGuardedIntegrator().IntegrateTaskAsync(mission, task, "worker"));

        Assert.Contains("advanced", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(repo.AdvancedBranchTip, await repo.GitAsync(repo.Root, "rev-parse", "refs/heads/build/test"));
    }

    [Fact]
    public async Task Rejects_pre_staged_generated_file_even_when_allowed_source_is_also_changed()
    {
        using var repo = new TestRepository(includeGeneratedFile: true);
        var worktree = repo.AddWorktree("T-staged");
        await File.WriteAllTextAsync(Path.Combine(worktree, "src", "feature.txt"), "verified\n");
        await File.WriteAllTextAsync(Path.Combine(worktree, "bin", "generated.txt"), "should-not-commit\n");
        await repo.GitAsync(worktree, "add", "src/feature.txt", "bin/generated.txt");
        var (mission, task) = repo.MakeMissionTask("T-staged", worktree, repo.BaseRevision, ["src"], ["tests"]);
        task = task with { Verification = await repo.VerificationForAsync(worktree) };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new DeveloperGuardedIntegrator().IntegrateTaskAsync(mission, task, "worker"));

        Assert.Contains("generated/vendor", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(repo.BaseRevision, await repo.GitAsync(repo.Root, "rev-parse", "refs/heads/build/test"));
    }

    [Fact]
    public async Task Reconciles_durable_intent_after_commit_was_created_but_branch_update_was_interrupted()
    {
        using var repo = new TestRepository();
        var (mission, task) = await repo.CreateTaskAsync("T-recover", "src/feature.txt", "verified\n");
        var (changed, _, diffSha) = await DeveloperAutonomousWorkspaceTools.CaptureDiffAsync(task.WorktreePath!);
        await repo.GitAsync(task.WorktreePath!, "add", "--", "src/feature.txt");
        var tree = await repo.GitAsync(task.WorktreePath!, "write-tree");
        var commit = await repo.GitAsync(task.WorktreePath!, "-c", "user.name=9to1 Build Autonomous", "-c", "user.email=build-autonomous@9to1.local",
            "commit-tree", tree, "-p", repo.BaseRevision, "-m", "simulated interrupted integration");
        var intentPath = repo.GetIntentPath(mission.MissionId, task.TaskId);
        Directory.CreateDirectory(Path.GetDirectoryName(intentPath)!);
        await File.WriteAllTextAsync(intentPath, JsonSerializer.Serialize(new
        {
            MissionId = mission.MissionId,
            TaskId = task.TaskId,
            BranchName = mission.IntegrationBranch,
            ParentRevision = repo.BaseRevision,
            CommitRevision = commit,
            DiffSha256 = diffSha,
            ChangedPaths = changed,
            RequirementIds = task.RecordIds,
            WorkerModelId = "worker",
            ReviewerModelId = (string?)null,
            IntegratedAt = DateTimeOffset.UtcNow
        }));

        var receipt = await new DeveloperGuardedIntegrator().IntegrateTaskAsync(mission, task, "worker");

        Assert.Equal(commit, receipt.CommitRevision);
        Assert.Equal(commit, await repo.GitAsync(repo.Root, "rev-parse", "refs/heads/build/test"));
        // A second restart is idempotent after the branch update has already completed.
        var again = await new DeveloperGuardedIntegrator().IntegrateTaskAsync(mission, task, "worker");
        Assert.Equal(receipt.CommitRevision, again.CommitRevision);
    }

    private sealed class TestRepository : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("guarded-integrator-").FullName;
        public string BaseRevision { get; }
        public string? AdvancedBranchTip { get; private set; }

        public TestRepository(bool includeGeneratedFile = false)
        {
            Directory.CreateDirectory(Path.Combine(Root, "src"));
            Directory.CreateDirectory(Path.Combine(Root, "tests"));
            if (includeGeneratedFile)
            {
                Directory.CreateDirectory(Path.Combine(Root, "bin"));
                File.WriteAllText(Path.Combine(Root, "bin", "generated.txt"), "original\n");
            }
            File.WriteAllText(Path.Combine(Root, "src", "feature.txt"), "initial\n");
            RunGit(Root, "init", "-q");
            RunGit(Root, "config", "user.name", "Test");
            RunGit(Root, "config", "user.email", "test@example.invalid");
            RunGit(Root, "add", ".");
            RunGit(Root, "commit", "-qm", "base");
            BaseRevision = RunGit(Root, "rev-parse", "HEAD");
            RunGit(Root, "branch", "build/test", BaseRevision);
        }

        public async Task<(DeveloperAutonomousMission Mission, DeveloperMissionTask Task)> CreateTaskAsync(
            string taskId, string path, string contents)
        {
            var worktree = AddWorktree(taskId);
            var full = Path.Combine(worktree, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, contents);
            var (mission, task) = MakeMissionTask(taskId, worktree, BaseRevision, [Path.GetDirectoryName(path)!.Replace('\\', '/')], ["tests"]);
            task = task with { Verification = await VerificationForAsync(worktree) };
            return (mission, task);
        }

        public string AddWorktree(string name)
        {
            var path = Path.Combine(Root, "worktrees", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            RunGit(Root, "worktree", "add", "--detach", path, BaseRevision);
            return path;
        }

        public (DeveloperAutonomousMission Mission, DeveloperMissionTask Task) MakeMissionTask(
            string taskId, string worktree, string baseRevision, IReadOnlyList<string> allowedSource, IReadOnlyList<string> allowedTests)
        {
            var mission = new DeveloperAutonomousMission
            {
                MissionId = "m-test", Name = "test", Objective = "test", RepositoryRoot = Root,
                IntegrationBranch = "build/test", BaseRevision = baseRevision, CurrentRevision = baseRevision,
                RequireSupervisorReview = false
            };
            var task = new DeveloperMissionTask
            {
                TaskId = taskId, ModuleId = "Q1", Title = "test", RecordIds = ["REQ-1"], Focus = "test",
                AcceptanceCriteria = "pass", WorktreePath = worktree, BaseRevision = baseRevision,
                AllowedSourcePaths = allowedSource, AllowedTestPaths = allowedTests
            };
            return (mission, task);
        }

        public async Task<DeveloperBuildVerificationReport> VerificationForAsync(string worktree)
        {
            var capture = await DeveloperAutonomousWorkspaceTools.CaptureDiffAsync(worktree);
            return new DeveloperBuildVerificationReport(true, true, 0, 0, 1, 0, 0, "build", null, null, "ok", [],
                DateTimeOffset.UtcNow, capture.DiffSha256, 0.1);
        }

        public async Task AdvanceBranchAsync(string branch)
        {
            var tree = RunGit(Root, "rev-parse", $"{branch}^{{tree}}");
            AdvancedBranchTip = RunGit(Root, "-c", "user.name=Test", "-c", "user.email=test@example.invalid",
                "commit-tree", tree, "-p", BaseRevision, "-m", "external advance");
            RunGit(Root, "update-ref", $"refs/heads/{branch}", AdvancedBranchTip, BaseRevision);
        }

        public string GetIntentPath(string missionId, string taskId)
        {
            var common = RunGit(Root, "rev-parse", "--git-common-dir");
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{missionId}\n{taskId}"))).ToLowerInvariant();
            return Path.Combine(Root, common, "9to1-integration-intents", key + ".json");
        }

        public Task<string> GitAsync(string cwd, params string[] args) => Task.FromResult(RunGit(cwd, args));

        public static string RunGit(string cwd, params string[] args)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
            return stdout.Trim();
        }

        public void Dispose()
        {
            try { RunGit(Root, "worktree", "prune"); } catch { }
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
