using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HavenOS.Apps.Dev.Autonomous;

/// <summary>
/// Guarded build and test runner for autonomous 9to1 Build missions.
/// Executes ONLY fixed, non-shell parameterized dotnet build/test invocations, respects the
/// shared serial SDK lock file, enforces live Linux MemAvailable floor safeguards so resident
/// local models are never OOM-killed, streams output live, and extracts MSBuild diagnostics + test counts.
/// </summary>
public sealed class DeveloperGuardedCommandRunner
{
    private static readonly Regex MsBuildDiagnosticRegex = new(
        @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s+(?<sev>error|warning)\s+(?<code>[A-Z0-9]+):\s+(?<msg>.*?)(?:\s+\[.*\])?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DotNetTestSummaryRegex = new(
        @"Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex XunitSummaryRegex = new(
        @"Total:\s*(?<total>\d+),\s*Errors:\s*(?<errors>\d+),\s*Failed:\s*(?<failed>\d+),\s*Skipped:\s*(?<skipped>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<DeveloperBuildVerificationReport> VerifyAsync(
        string worktreeRoot,
        string buildProjectRelative,
        string? testProjectRelative,
        string? testFilter,
        string sourceDiffSha256,
        DeveloperAutonomousMissionOptions options,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var cleanBuild = DeveloperAutonomousWorkspaceTools.NormalizeAndValidateRelativePath(worktreeRoot, buildProjectRelative);
        var cleanTest = string.IsNullOrWhiteSpace(testProjectRelative)
            ? null
            : DeveloperAutonomousWorkspaceTools.NormalizeAndValidateRelativePath(worktreeRoot, testProjectRelative);

        if (!cleanBuild.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            && !cleanBuild.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            && !cleanBuild.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Build target must be a .csproj or solution file: {cleanBuild}");
        }

        if (cleanTest is not null && !cleanTest.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Test target must be a .csproj file: {cleanTest}");

        if (!string.IsNullOrWhiteSpace(testFilter) && !Regex.IsMatch(testFilter, @"^[A-Za-z0-9_.\-=~|&()! ]{1,240}$"))
            throw new ArgumentException("Test filter contains unsupported characters.", nameof(testFilter));

        var combined = new StringBuilder();
        void Emit(string line)
        {
            combined.AppendLine(line);
            onOutputLine?.Invoke(line);
        }

        await using var sdkLock = await AcquireSdkLockAsync(options.SdkLockPath, Emit, cancellationToken).ConfigureAwait(false);

        var buildArgs = new List<string>
        {
            "build",
            cleanBuild,
            "-c", "Release",
            "-m:1",
            "-p:UseSharedCompilation=false",
            "--nologo"
        };
        Emit($"$ {options.DotNetExecutable} {string.Join(' ', buildArgs)}");
        var buildExit = await RunGuardedProcessAsync(
            worktreeRoot,
            options.DotNetExecutable,
            buildArgs,
            options.MemoryFloorMiB,
            options.CommandTimeoutSeconds,
            Emit,
            cancellationToken).ConfigureAwait(false);

        var testExit = -1;
        var passed = 0;
        var failed = 0;
        var skipped = 0;

        if (buildExit == 0 && cleanTest is not null)
        {
            var testArgs = new List<string>
            {
                "test",
                cleanTest,
                "-c", "Release",
                "-m:1",
                "-p:UseSharedCompilation=false",
                "--nologo",
                "--verbosity", "minimal"
            };
            if (!string.IsNullOrWhiteSpace(testFilter))
            {
                testArgs.Add("--filter");
                testArgs.Add(testFilter);
            }
            Emit($"$ {options.DotNetExecutable} {string.Join(' ', testArgs)}");
            testExit = await RunGuardedProcessAsync(
                worktreeRoot,
                options.DotNetExecutable,
                testArgs,
                options.MemoryFloorMiB,
                options.CommandTimeoutSeconds,
                Emit,
                cancellationToken).ConfigureAwait(false);
        }

        sw.Stop();
        var fullOutput = combined.ToString();
        var diagnostics = ParseMsBuildDiagnostics(fullOutput, worktreeRoot);
        (passed, failed, skipped) = ParseTestCounts(fullOutput);

        var buildSucceeded = buildExit == 0;
        var testsSucceeded = cleanTest is null
            ? buildSucceeded
            : buildSucceeded && testExit == 0 && failed == 0 && passed > 0;

        return new DeveloperBuildVerificationReport(
            BuildSucceeded: buildSucceeded,
            TestsSucceeded: testsSucceeded,
            BuildExitCode: buildExit,
            TestExitCode: cleanTest is null ? buildExit : testExit,
            TestsPassed: passed,
            TestsFailed: failed,
            TestsSkipped: skipped,
            BuildProject: cleanBuild,
            TestProject: cleanTest,
            TestFilter: testFilter,
            CombinedOutput: fullOutput,
            Diagnostics: diagnostics,
            VerifiedAt: DateTimeOffset.UtcNow,
            SourceDiffSha256: sourceDiffSha256,
            DurationSeconds: Math.Round(sw.Elapsed.TotalSeconds, 2));
    }

    public static IReadOnlyList<DeveloperParsedDiagnostic> ParseMsBuildDiagnostics(string output, string worktreeRoot)
    {
        var fullRoot = Path.GetFullPath(worktreeRoot);
        var result = new List<DeveloperParsedDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.Contains("MSB4181", StringComparison.Ordinal))
                continue;
            var match = MsBuildDiagnosticRegex.Match(line);
            if (!match.Success) continue;

            var file = match.Groups["file"].Value.Trim();
            var rel = file;
            if (Path.IsPathRooted(file))
            {
                var full = Path.GetFullPath(file);
                if (full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    rel = Path.GetRelativePath(fullRoot, full).Replace('\\', '/');
            }
            else
            {
                rel = file.Replace('\\', '/');
            }

            var ln = int.TryParse(match.Groups["line"].Value, out var l) ? l : 1;
            var col = int.TryParse(match.Groups["col"].Value, out var c) ? c : 1;
            var sev = match.Groups["sev"].Value.ToLowerInvariant();
            var code = match.Groups["code"].Value;
            var msg = match.Groups["msg"].Value.Trim();
            var key = $"{rel}:{ln}:{col}:{sev}:{code}:{msg}";
            if (seen.Add(key))
                result.Add(new DeveloperParsedDiagnostic(rel, ln, col, sev, code, msg));
        }
        return result;
    }

    public static (int Passed, int Failed, int Skipped) ParseTestCounts(string output)
    {
        var totalPassed = 0;
        var totalFailed = 0;
        var totalSkipped = 0;
        foreach (Match m in DotNetTestSummaryRegex.Matches(output))
        {
            totalFailed += int.Parse(m.Groups["failed"].Value);
            totalPassed += int.Parse(m.Groups["passed"].Value);
            totalSkipped += int.Parse(m.Groups["skipped"].Value);
        }
        if (totalPassed == 0 && totalFailed == 0)
        {
            foreach (Match m in XunitSummaryRegex.Matches(output))
            {
                var tot = int.Parse(m.Groups["total"].Value);
                var err = int.Parse(m.Groups["errors"].Value);
                var fail = int.Parse(m.Groups["failed"].Value);
                var skip = int.Parse(m.Groups["skipped"].Value);
                totalFailed += err + fail;
                totalSkipped += skip;
                totalPassed += Math.Max(0, tot - err - fail - skip);
            }
        }
        return (totalPassed, totalFailed, totalSkipped);
    }

    private static async Task<int> RunGuardedProcessAsync(
        string workingDirectory,
        string executable,
        IReadOnlyList<string> arguments,
        int memoryFloorMiB,
        int timeoutSeconds,
        Action<string> emit,
        CancellationToken cancellationToken)
    {
        var resolvedExecutable = ResolveDotNetExecutable(executable);
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Low-priority execution keeps resident Dulche inference responsive.
        if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/nice"))
        {
            psi.FileName = "/usr/bin/nice";
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add("19");
            psi.ArgumentList.Add(resolvedExecutable);
        }
        else
        {
            psi.FileName = resolvedExecutable;
        }

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        psi.Environment["DOTNET_gcServer"] = "0";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_PARALLEL_COMPILE"] = "0";

        if (OperatingSystem.IsLinux() && TryReadMemAvailableMiB(out var beforeMiB) && beforeMiB <= memoryFloorMiB)
        {
            emit($"[MEM GUARD] Refused launch: MemAvailable {beforeMiB} MiB <= floor {memoryFloorMiB} MiB.");
            return 124;
        }

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {resolvedExecutable}.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 15, 2400)));

        var memGuardTripped = false;
        var outTask = PumpAsync(proc.StandardOutput, emit, timeoutCts.Token);
        var errTask = PumpAsync(proc.StandardError, emit, timeoutCts.Token);

        var monitorTask = Task.Run(async () =>
        {
            while (!proc.HasExited && !timeoutCts.IsCancellationRequested)
            {
                if (OperatingSystem.IsLinux() && TryReadMemAvailableMiB(out var availMiB) && availMiB <= memoryFloorMiB)
                {
                    memGuardTripped = true;
                    emit($"[MEM GUARD] MemAvailable dropped to {availMiB} MiB (floor {memoryFloorMiB} MiB); terminating build process tree.");
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return;
                }
                try { await Task.Delay(500, timeoutCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }, CancellationToken.None);

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            emit($"[TIME GUARD] Command exceeded {timeoutSeconds}s timeout and was terminated.");
            return 124;
        }
        finally
        {
            try { await Task.WhenAll(outTask, errTask, monitorTask).ConfigureAwait(false); } catch { }
        }

        return memGuardTripped ? 124 : proc.ExitCode;
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> emit, CancellationToken token)
    {
        try
        {
            while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                emit(line);
        }
        catch (OperationCanceledException) { }
    }

    private static string ResolveDotNetExecutable(string configured)
    {
        if (Path.IsPathRooted(configured) && File.Exists(configured))
            return configured;
        var homeDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet");
        if (configured == "dotnet" && File.Exists(homeDotnet))
            return homeDotnet;
        return configured;
    }

    private static bool TryReadMemAvailableMiB(out long availableMiB)
    {
        availableMiB = 0;
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal)) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out var kib))
                {
                    availableMiB = kib / 1024;
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static async Task<IAsyncDisposable> AcquireSdkLockAsync(
        string? lockPath,
        Action<string> emit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lockPath))
            return NoopDisposable.Instance;

        var full = Path.GetFullPath(lockPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var announced = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(full, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new FileLockHandle(stream);
            }
            catch (IOException)
            {
                if (!announced)
                {
                    emit($"[SDK LOCK] Waiting for serial SDK lock {full}...");
                    announced = true;
                }
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class FileLockHandle(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopDisposable : IAsyncDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
