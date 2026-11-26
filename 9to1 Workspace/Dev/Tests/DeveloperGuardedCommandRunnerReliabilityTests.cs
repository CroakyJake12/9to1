using System.Diagnostics;
using HavenOS.Apps.Dev.Autonomous;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperGuardedCommandRunnerReliabilityTests
{
    [Fact]
    public async Task Uses_shared_flock_and_cancellation_prevents_child_launch()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new RunnerFixture();
        var externalLock = Process.Start(new ProcessStartInfo("python3")
        {
            ArgumentList = { "-c", "import fcntl,sys,time; f=open(sys.argv[1],'a+b'); fcntl.flock(f,fcntl.LOCK_EX); print('LOCKED',flush=True); time.sleep(10)", fixture.LockPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        try
        {
            Assert.Equal("LOCKED", await externalLock.StandardOutput.ReadLineAsync());
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Runner.VerifyAsync(
                fixture.Root, "src/Fake.csproj", "tests/Fake.Tests.csproj", null,
                fixture.SourceDiffSha, fixture.Options, cancellationToken: cancellation.Token));
            Assert.False(File.Exists(fixture.LaunchMarkerPath));
        }
        finally
        {
            try { externalLock.Kill(entireProcessTree: true); } catch { }
            await externalLock.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task Counts_only_test_process_output_and_limits_child_resources()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new RunnerFixture();
        var result = await fixture.Runner.VerifyAsync(
            fixture.Root, "src/Fake.csproj", "tests/Fake.Tests.csproj", null,
            fixture.SourceDiffSha, fixture.Options);

        Assert.True(result.BuildSucceeded, result.CombinedOutput);
        Assert.True(result.TestsSucceeded, result.CombinedOutput);
        Assert.Equal(2, result.TestsPassed);
        Assert.Equal(0, result.TestsFailed);
        var childEnv = await File.ReadAllTextAsync(fixture.EnvironmentCapturePath);
        Assert.Matches("DOTNET_PROCESSOR_COUNT=[12]", childEnv);
        Assert.Contains("CUDA_VISIBLE_DEVICES=", childEnv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_test_command_that_changes_the_verified_worktree_diff()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new RunnerFixture(mutateSourceDuringTest: true);
        var result = await fixture.Runner.VerifyAsync(
            fixture.Root, "src/Fake.csproj", "tests/Fake.Tests.csproj", null,
            fixture.SourceDiffSha, fixture.Options);

        Assert.True(result.BuildSucceeded, result.CombinedOutput);
        Assert.False(result.TestsSucceeded);
        Assert.Contains("Test command changed the verified worktree diff", result.CombinedOutput, StringComparison.Ordinal);
    }

    private sealed class RunnerFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("guarded-runner-").FullName;
        public string LockPath => Path.Combine(Root, ".git", "sdk.lock");
        public string LaunchMarkerPath => Path.Combine(Root, ".git", "launched.txt");
        public string EnvironmentCapturePath => Path.Combine(Root, ".git", "child-env.txt");
        public string SourceDiffSha { get; }
        public DeveloperGuardedCommandRunner Runner { get; } = new();
        public DeveloperAutonomousMissionOptions Options { get; }

        public RunnerFixture(bool mutateSourceDuringTest = false)
        {
            Directory.CreateDirectory(Path.Combine(Root, "src"));
            Directory.CreateDirectory(Path.Combine(Root, "tests"));
            File.WriteAllText(Path.Combine(Root, "src", "Fake.csproj"), "<Project />\n");
            File.WriteAllText(Path.Combine(Root, "tests", "Fake.Tests.csproj"), "<Project />\n");
            var script = Path.Combine(Root, "fake-dotnet.sh");
            var mutate = mutateSourceDuringTest
                ? "printf 'changed\\n' >> src/Fake.csproj\n"
                : "";
            File.WriteAllText(script,
                "#!/bin/sh\n" +
                $"echo launched >> '{LaunchMarkerPath}'\n" +
                "if [ \"$1\" = \"build\" ]; then printf 'Failed: 0, Passed: 77, Skipped: 0\\n'; exit 0; fi\n" +
                $"printf 'DOTNET_PROCESSOR_COUNT=%s\\nCUDA_VISIBLE_DEVICES=%s\\n' \"$DOTNET_PROCESSOR_COUNT\" \"$CUDA_VISIBLE_DEVICES\" > '{EnvironmentCapturePath}'\n" +
                mutate +
                "printf 'Failed: 0, Passed: 2, Skipped: 1\\n'\n");
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Git("init", "-q");
            Git("config", "user.name", "Test");
            Git("config", "user.email", "test@example.invalid");
            Git("add", ".");
            Git("commit", "-qm", "fixture");
            SourceDiffSha = DeveloperAutonomousWorkspaceTools.CaptureDiffAsync(Root).GetAwaiter().GetResult().DiffSha256;
            Options = new DeveloperAutonomousMissionOptions
            {
                RepositoryRoot = Root,
                SdkLockPath = LockPath,
                DotNetExecutable = script,
                MemoryFloorMiB = 1,
                DiskFloorGiB = 1,
                CommandTimeoutSeconds = 30
            };
        }

        private void Git(params string[] args)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
