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
        await using var externalLock = await ExternalSdkLock.AcquireAsync(fixture.LockPath);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Runner.VerifyAsync(
            fixture.Root, "src/Fake.csproj", "tests/Fake.Tests.csproj", null,
            fixture.SourceDiffSha, fixture.Options, cancellationToken: cancellation.Token));
        Assert.False(File.Exists(fixture.LaunchMarkerPath));

        await externalLock.ReleaseAsync();
        var resumed = await fixture.Runner.VerifyAsync(
            fixture.Root, "src/Fake.csproj", "tests/Fake.Tests.csproj", null,
            fixture.SourceDiffSha, fixture.Options);
        Assert.True(resumed.TestsSucceeded, resumed.CombinedOutput);
    }

    [Fact]
    public async Task Exhausted_sdk_lock_wait_returns_transient_receipt_without_launching_child()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new RunnerFixture(sdkLockWaitTimeout: TimeSpan.FromMilliseconds(250));
        await using var externalLock = await ExternalSdkLock.AcquireAsync(fixture.LockPath);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await fixture.Runner.VerifyAsync(
            fixture.Root, "src/Fake.csproj", "tests/Fake.Tests.csproj", null,
            fixture.SourceDiffSha, fixture.Options, cancellationToken: cancellation.Token);

        Assert.False(result.BuildSucceeded);
        Assert.False(result.TestsSucceeded);
        Assert.Equal(124, result.BuildExitCode);
        Assert.Equal(-1, result.TestExitCode);
        Assert.Equal(0, result.TestsPassed);
        Assert.Equal(fixture.SourceDiffSha, result.SourceDiffSha256);
        Assert.Contains("[SDK LOCK] Timed out", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("No build or test process was started", result.CombinedOutput, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.LaunchMarkerPath));

        await externalLock.ReleaseAsync();
        var retried = await fixture.Runner.VerifyAsync(
            fixture.Root, "src/Fake.csproj", "tests/Fake.Tests.csproj", null,
            fixture.SourceDiffSha, fixture.Options);
        Assert.True(retried.TestsSucceeded, retried.CombinedOutput);
    }

    [Fact]
    public async Task Waiting_verification_starts_only_after_the_external_lock_is_released()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new RunnerFixture(sdkLockWaitTimeout: TimeSpan.FromSeconds(3));
        await using var externalLock = await ExternalSdkLock.AcquireAsync(fixture.LockPath);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var verification = fixture.Runner.VerifyAsync(
            fixture.Root, "src/Fake.csproj", "tests/Fake.Tests.csproj", null,
            fixture.SourceDiffSha, fixture.Options,
            onOutputLine: line => { if (line.StartsWith("[SDK LOCK] Waiting", StringComparison.Ordinal)) waiting.TrySetResult(); },
            cancellationToken: cancellation.Token);
        await waiting.Task.WaitAsync(cancellation.Token);
        Assert.False(verification.IsCompleted);
        Assert.False(File.Exists(fixture.LaunchMarkerPath));

        await externalLock.ReleaseAsync();
        var result = await verification;
        Assert.True(result.TestsSucceeded, result.CombinedOutput);
        Assert.Equal(2, File.ReadAllLines(fixture.LaunchMarkerPath).Length);
    }

    [Fact]
    public async Task Invalid_lock_parent_is_not_misreported_as_transient_contention()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new RunnerFixture(sdkLockWaitTimeout: TimeSpan.FromMilliseconds(250));
        var options = fixture.Options with
        {
            SdkLockPath = Path.Combine(fixture.Root, "src", "Fake.csproj", "sdk.lock")
        };
        await Assert.ThrowsAsync<IOException>(() => fixture.Runner.VerifyAsync(
            fixture.Root, "src/Fake.csproj", "tests/Fake.Tests.csproj", null,
            fixture.SourceDiffSha, options));
        Assert.False(File.Exists(fixture.LaunchMarkerPath));
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

    private sealed class ExternalSdkLock(Process process) : IAsyncDisposable
    {
        private bool _released;

        public static async Task<ExternalSdkLock> AcquireAsync(string path)
        {
            var process = Process.Start(new ProcessStartInfo("python3")
            {
                ArgumentList = { "-c", "import fcntl,sys; f=open(sys.argv[1],'a+b'); fcntl.flock(f,fcntl.LOCK_EX); print('LOCKED',flush=True); sys.stdin.read()", path },
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            try
            {
                Assert.Equal("LOCKED", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                return new ExternalSdkLock(process);
            }
            catch
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                await process.WaitForExitAsync();
                process.Dispose();
                throw;
            }
        }

        public async Task ReleaseAsync()
        {
            if (_released) return;
            process.StandardInput.Close();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(0, process.ExitCode);
            }
            catch
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                await process.WaitForExitAsync();
                throw;
            }
            finally
            {
                _released = true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { await ReleaseAsync(); }
            finally { process.Dispose(); }
        }
    }

    private sealed class RunnerFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("guarded-runner-").FullName;
        public string LockPath => Path.Combine(Root, ".git", "sdk.lock");
        public string LaunchMarkerPath => Path.Combine(Root, ".git", "launched.txt");
        public string EnvironmentCapturePath => Path.Combine(Root, ".git", "child-env.txt");
        public string SourceDiffSha { get; }
        public DeveloperGuardedCommandRunner Runner { get; }
        public DeveloperAutonomousMissionOptions Options { get; }

        public RunnerFixture(bool mutateSourceDuringTest = false, TimeSpan? sdkLockWaitTimeout = null)
        {
            Runner = sdkLockWaitTimeout is { } timeout ? new(timeout) : new();
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
