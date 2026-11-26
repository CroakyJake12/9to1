using System.Diagnostics;
using HavenOS.Apps.Dev.Autonomous;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperFailedVerificationCacheTests
{
    [Fact]
    public async Task Repeated_complete_task_reuses_exact_hard_failure_without_running_sdk_and_counts_as_idle()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new MissionFixture();
        var (mission, task, failure) = await fixture.SeedMissionAsync();
        var engine = new DeveloperAutonomousMissionEngine(fixture.Options, new CompleteTaskGateway(), fixture.Store);
        var verificationStarted = 0;
        engine.EventRecorded += ev => { if (ev.Kind == "verification_started") verificationStarted++; };
        await using (engine)
        {
            await engine.InitializeOrResumeAsync(mission.MissionId);
            await engine.RunSingleStepAsync();
            var after = engine.CurrentMission!;
            Assert.Equal(0, verificationStarted);
            Assert.False(File.Exists(fixture.LaunchMarkerPath));
            Assert.Equal(3, after.ActiveTask!.IdleTurns);
            Assert.Equal(failure.SourceDiffSha256, after.ActiveTask.Verification!.SourceDiffSha256);
            Assert.Equal(failure.CombinedOutput, after.ActiveTask.Verification.CombinedOutput);
        }
    }

    [Fact]
    public async Task Changed_source_invalidates_cached_failure_and_runs_the_new_verification_recipe()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new MissionFixture();
        var (mission, task, _) = await fixture.SeedMissionAsync();
        await File.AppendAllTextAsync(Path.Combine(fixture.Repository, "src", "Code.cs"), "// changed\n");
        var engine = new DeveloperAutonomousMissionEngine(fixture.Options, new CompleteTaskGateway(), fixture.Store);
        var verificationStarted = 0;
        engine.EventRecorded += ev => { if (ev.Kind == "verification_started") verificationStarted++; };
        await using (engine)
        {
            await engine.InitializeOrResumeAsync(mission.MissionId);
            await engine.RunSingleStepAsync();
            Assert.Equal(1, verificationStarted);
            Assert.True(File.Exists(fixture.LaunchMarkerPath));
            Assert.NotEqual(task.Verification!.SourceDiffSha256, engine.CurrentMission!.ActiveTask!.Verification!.SourceDiffSha256);
        }
    }

    private sealed class MissionFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("mission-verification-cache-").FullName;
        public string Repository => Path.Combine(Root, "repo");
        public string StoreDirectory => Path.Combine(Root, "store");
        public string LaunchMarkerPath => Path.Combine(Repository, ".git", "sdk-launched");
        public DeveloperAutonomousMissionStore Store { get; }
        public DeveloperAutonomousMissionOptions Options { get; }

        public MissionFixture()
        {
            Directory.CreateDirectory(Path.Combine(Repository, "src"));
            Directory.CreateDirectory(Path.Combine(Repository, "tests"));
            File.WriteAllText(Path.Combine(Repository, "src", "Code.cs"), "class Code { }\n");
            File.WriteAllText(Path.Combine(Repository, "src", "Build.csproj"), "<Project />\n");
            File.WriteAllText(Path.Combine(Repository, "tests", "Tests.csproj"), "<Project />\n");
            File.WriteAllText(Path.Combine(Repository, "tests", "CodeTests.cs"), "class CodeTests { }\n");
            Git("init", "-q");
            Git("config", "user.name", "Mission Test");
            Git("config", "user.email", "mission-test@example.invalid");
            Git("add", ".");
            Git("commit", "-qm", "base");
            Store = new DeveloperAutonomousMissionStore(StoreDirectory);

            var fakeDotnet = Path.Combine(Root, "fake-dotnet.sh");
            File.WriteAllText(fakeDotnet, $"#!/bin/sh\necho launched >> '{LaunchMarkerPath}'\necho 'NU1008: The Include attribute is reserved for implicit items.'\nexit 1\n");
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(fakeDotnet, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Options = new DeveloperAutonomousMissionOptions
            {
                RepositoryRoot = Repository,
                StoreDirectory = StoreDirectory,
                WorkerModelId = "test-worker",
                MaxCompletionTokens = 128,
                RequireSupervisorReview = false,
                RequireTestFileChange = true,
                MemoryFloorMiB = 1,
                DiskFloorGiB = 1,
                SdkLockPath = Path.Combine(Root, "sdk.lock"),
                DotNetExecutable = fakeDotnet,
                MaxIdleTurns = 5,
                CommandTimeoutSeconds = 30
            };
        }

        public async Task<(DeveloperAutonomousMission Mission, DeveloperMissionTask Task, DeveloperBuildVerificationReport Failure)> SeedMissionAsync()
        {
            await File.AppendAllTextAsync(Path.Combine(Repository, "src", "Code.cs"), "// task change\n");
            await File.AppendAllTextAsync(Path.Combine(Repository, "tests", "CodeTests.cs"), "// focused test change\n");
            var diff = await DeveloperAutonomousWorkspaceTools.CaptureDiffAsync(Repository);
            var baseRevision = Git("rev-parse", "HEAD");
            var missionId = "m-cache-test";
            var failed = new DeveloperBuildVerificationReport(
                BuildSucceeded: false,
                TestsSucceeded: false,
                BuildExitCode: 1,
                TestExitCode: -1,
                TestsPassed: 0,
                TestsFailed: 0,
                TestsSkipped: 0,
                BuildProject: "src/Build.csproj",
                TestProject: "tests/Tests.csproj",
                TestFilter: "FullyQualifiedName~CodeTests",
                CombinedOutput: "error NU1008: The Include attribute is reserved for implicit items.",
                Diagnostics: [new DeveloperParsedDiagnostic("src/Build.csproj", 1, 1, "error", "NU1008", "Incorrect PackageReference configuration.")],
                VerifiedAt: DateTimeOffset.UtcNow,
                SourceDiffSha256: diff.DiffSha256,
                DurationSeconds: 1.0);
            var task = new DeveloperMissionTask
            {
                TaskId = "T001-NU1008",
                ModuleId = "Q1",
                Title = "Fix build configuration",
                RecordIds = ["REQ-1"],
                Focus = "Repair the build configuration",
                AcceptanceCriteria = "Build and tests pass",
                AllowedSourcePaths = ["src"],
                AllowedTestPaths = ["tests"],
                WorktreePath = Repository,
                BaseRevision = baseRevision,
                CurrentRevision = baseRevision,
                State = DeveloperMissionTaskState.Implementing,
                ChangedPaths = diff.ChangedPaths,
                DiffText = diff.UnifiedDiff,
                DiffSha256 = diff.DiffSha256,
                BuildProject = "src/Build.csproj",
                TestProject = "tests/Tests.csproj",
                TestFilter = "FullyQualifiedName~CodeTests",
                Verification = failed,
                IdleTurns = 2
            };
            var mission = new DeveloperAutonomousMission
            {
                MissionId = missionId,
                Name = "NU1008 recovery",
                Objective = "Recover from a deterministic build failure",
                RepositoryRoot = Repository,
                IntegrationBranch = "build/test-cache",
                BaseRevision = baseRevision,
                CurrentRevision = baseRevision,
                WorkerModelId = "test-worker",
                State = DeveloperAutonomousMissionState.Implementing,
                ActiveTask = task,
                RequireSupervisorReview = false,
                RequireTestFileChange = true
            };
            Store.SaveMission(mission);
            return (mission, task, failed);
        }

        private string Git(params string[] args)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = Repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
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
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    private sealed class CompleteTaskGateway : IDeveloperModelGateway
    {
        public Task<DeveloperDulcheStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeveloperDulcheStatus("ready", false,
                [new DeveloperModelInfo("test-worker", "Test Worker", "loaded", null, 8192, 1, "test", null)],
                new DeveloperResourceSnapshot(8192, 16384, 100L * 1024 * 1024 * 1024, 0, []), 0));

        public Task<int> CountTokensAsync(string modelId, IReadOnlyList<DeveloperChatMessage> messages, CancellationToken cancellationToken = default) =>
            Task.FromResult(128);

        public Task<DeveloperCompletionResult> StreamCompletionAsync(
            DeveloperCompletionRequest request,
            Action<DeveloperStreamChunk>? onChunk = null,
            Action<string>? onRunStarted = null,
            CancellationToken cancellationToken = default)
        {
            var runId = "test-run";
            onRunStarted?.Invoke(runId);
            var content = "{\"op\":\"complete_task\",\"summary\":\"retry same verification\",\"build_project\":\"src/Build.csproj\",\"test_project\":\"tests/Tests.csproj\",\"test_filter\":\"FullyQualifiedName~CodeTests\"}";
            return Task.FromResult(new DeveloperCompletionResult(runId, request.ModelId, content, "", "stop", 128, 32, null, TimeSpan.FromMilliseconds(1)));
        }

        public Task StopRunAsync(string dulcheRunId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
