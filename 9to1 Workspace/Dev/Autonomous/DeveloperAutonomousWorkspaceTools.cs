using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HavenOS.Apps.Dev.Autonomous;

/// <summary>
/// Isolated Git worktree & safe repository workspace tools for autonomous development.
/// Enforces strict worktree containment, blocks symlink escapes and forbidden vendor/generated
/// directories, applies exact unique atomic replacements, and captures unified diffs + SHA-256 digests.
/// </summary>
public sealed class DeveloperAutonomousWorkspaceTools
{
    private static readonly string[] BlockedSegments =
    [
        ".git", "bin", "obj", "node_modules", "Vendor", "ThirdParty", ".vs", "TestResults"
    ];

    private static readonly string[] AllowedTextExtensions =
    [
        ".cs", ".csproj", ".cui", ".axaml", ".xaml", ".json", ".md", ".props", ".targets",
        ".py", ".txt", ".xml", ".yml", ".yaml", ".editorconfig", ".sln", ".slnx", ".mjs", ".js", ".ts", ".css", ".html"
    ];

    private static readonly Regex PrivilegedPatternRegex = new(
        @"(?i)(#pragma\s+warning\s+disable|DllImport|Environment\.Exit|Directory\.Delete\s*\(\s*""/)",
        RegexOptions.Compiled);

    public static async Task<string> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGitDetailedAsync(workingDirectory, arguments, timeoutSeconds, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed (exit {result.ExitCode}): {result.StdErr.Trim()} {result.StdOut.Trim()}".Trim());
        return result.StdOut.TrimEnd();
    }

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunGitDetailedAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git process.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 300)));

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = proc.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (proc.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    public static async Task<string> ResolveHeadRevisionAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        (await RunGitAsync(repositoryRoot, ["rev-parse", "HEAD"], cancellationToken: cancellationToken).ConfigureAwait(false)).Trim();

    public static async Task EnsureIntegrationBranchAsync(
        string repositoryRoot,
        string branchName,
        string baseRevision,
        CancellationToken cancellationToken = default)
    {
        var check = await RunGitDetailedAsync(
            repositoryRoot,
            ["show-ref", "--verify", "--quiet", $"refs/heads/{branchName}"],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (check.ExitCode != 0)
        {
            await RunGitAsync(
                repositoryRoot,
                ["branch", branchName, baseRevision],
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<string> PrepareIsolatedWorktreeAsync(
        string repositoryRoot,
        string worktreePath,
        string baseRevision,
        CancellationToken cancellationToken = default)
    {
        var fullWorktree = Path.GetFullPath(worktreePath);
        if (Directory.Exists(fullWorktree) && Directory.Exists(Path.Combine(fullWorktree, ".git"))
            || File.Exists(Path.Combine(fullWorktree, ".git")))
        {
            return fullWorktree;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullWorktree)!);
        await RunGitAsync(
            repositoryRoot,
            ["worktree", "add", "--detach", fullWorktree, baseRevision],
            timeoutSeconds: 180,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await MaterializeLocalBuildSubmodulesAsync(repositoryRoot, fullWorktree, cancellationToken).ConfigureAwait(false);
        return fullWorktree;
    }

    private static readonly string[] LocalBuildSubmodules =
    [
        "framework/CUI/vendor/Avalonia/external/XamlX",
        "framework/CUI/vendor/Avalonia/external/Avalonia.DBus"
    ];

    /// <summary>
    /// Materializes required local Avalonia build gitlinks inside isolated 9to1 worktrees
    /// from local Git objects without network fetches or modifying shared canonical git config.
    /// </summary>
    private static async Task MaterializeLocalBuildSubmodulesAsync(
        string repositoryRoot,
        string worktreePath,
        CancellationToken cancellationToken)
    {
        foreach (var relative in LocalBuildSubmodules)
        {
            var source = Path.Combine(repositoryRoot, relative);
            var target = Path.Combine(worktreePath, relative);
            if (!Directory.Exists(source) || (!File.Exists(Path.Combine(source, ".git")) && !Directory.Exists(Path.Combine(source, ".git"))))
                continue;
            if (File.Exists(Path.Combine(target, ".git")) || Directory.Exists(Path.Combine(target, ".git")))
                continue;
            var check = await RunGitDetailedAsync(worktreePath, ["ls-tree", "HEAD", "--", relative], 20, cancellationToken).ConfigureAwait(false);
            if (check.ExitCode != 0 || string.IsNullOrWhiteSpace(check.StdOut))
                continue;
            await RunGitDetailedAsync(
                worktreePath,
                [
                    "-c", "protocol.file.allow=always",
                    "-c", $"submodule.{relative}.active=true",
                    "-c", $"submodule.{relative}.url={source}",
                    "submodule", "update", "--no-fetch", "--", relative
                ],
                timeoutSeconds: 90,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task RemoveWorktreeAsync(
        string repositoryRoot,
        string worktreePath,
        CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(worktreePath);
        if (!Directory.Exists(full)) return;
        await RunGitDetailedAsync(
            repositoryRoot,
            ["worktree", "remove", "--force", full],
            timeoutSeconds: 60,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static string NormalizeAndValidateRelativePath(string worktreeRoot, string relativePath, bool allowRoot = false)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            if (allowRoot) return "";
            throw new ArgumentException("A relative repository path is required.", nameof(relativePath));
        }

        var normalized = relativePath.Replace('\\', '/').Trim();
        if (allowRoot && normalized is "." or "./")
            return "";

        if (Path.IsPathRooted(normalized) || normalized.StartsWith('/') || (normalized.Length > 1 && normalized[1] == ':'))
            throw new UnauthorizedAccessException($"Absolute paths are prohibited: {relativePath}");

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            if (allowRoot) return "";
            throw new ArgumentException("Path cannot be empty.", nameof(relativePath));
        }

        foreach (var seg in segments)
        {
            if (seg is "." or ".." || BlockedSegments.Contains(seg, StringComparer.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Path segment '{seg}' is not permitted: {relativePath}");
        }

        var clean = string.Join('/', segments);
        if (clean.Contains("/Source/Files/", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Donor Files source cannot be modified automatically: {clean}");

        var fullRoot = Path.GetFullPath(worktreeRoot);
        var fullTarget = Path.GetFullPath(Path.Combine(fullRoot, clean));
        if (!fullTarget.Equals(fullRoot, StringComparison.Ordinal)
            && !fullTarget.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"Path escapes isolated worktree: {relativePath}");
        }

        // Check symlink traversal on existing ancestor/target.
        var cursor = fullTarget;
        while (cursor.Length >= fullRoot.Length)
        {
            if (File.Exists(cursor) || Directory.Exists(cursor))
            {
                var info = new FileInfo(cursor);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new UnauthorizedAccessException($"Symlink path is prohibited: {relativePath}");
            }
            if (cursor.Equals(fullRoot, StringComparison.Ordinal)) break;
            var parent = Path.GetDirectoryName(cursor);
            if (string.IsNullOrEmpty(parent) || parent == cursor) break;
            cursor = parent;
        }

        return clean;
    }

    public static IReadOnlyList<string> ListFiles(
        string worktreeRoot,
        string relativeFolder = ".",
        int maxResults = 200)
    {
        var cleanFolder = NormalizeAndValidateRelativePath(worktreeRoot, relativeFolder, allowRoot: true);
        var fullRoot = Path.GetFullPath(worktreeRoot);
        var startDir = string.IsNullOrEmpty(cleanFolder) ? fullRoot : Path.Combine(fullRoot, cleanFolder);
        if (!Directory.Exists(startDir))
            throw new DirectoryNotFoundException($"Folder not found in worktree: {relativeFolder}");

        var results = new List<string>();
        var stack = new Stack<string>();
        stack.Push(startDir);
        while (stack.Count > 0 && results.Count < maxResults)
        {
            var current = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(current).OrderByDescending(x => x, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(sub);
                if (BlockedSegments.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                var info = new DirectoryInfo(sub);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                stack.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(current).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (results.Count >= maxResults) break;
                var ext = Path.GetExtension(file);
                if (!AllowedTextExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
                var rel = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');
                results.Add(rel);
            }
        }

        return results.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    public static (string NumberedExcerpt, int TotalLines, string FileSha256) ReadFileRange(
        string worktreeRoot,
        string relativePath,
        int startLine = 1,
        int lineCount = 200)
    {
        var clean = NormalizeAndValidateRelativePath(worktreeRoot, relativePath);
        var full = Path.Combine(Path.GetFullPath(worktreeRoot), clean);
        if (!File.Exists(full))
            throw new FileNotFoundException($"File not found in worktree: {clean}", clean);

        var text = File.ReadAllText(full, Encoding.UTF8);
        var sha = DeveloperAutonomousMissionStore.Sha256Hex(text);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Math.Clamp(startLine, 1, Math.Max(1, lines.Length));
        var count = Math.Clamp(lineCount, 1, 400);
        var end = Math.Min(lines.Length, start - 1 + count);

        var sb = new StringBuilder();
        for (var i = start - 1; i < end; i++)
            sb.Append(i + 1).Append(": ").AppendLine(lines[i]);
        sb.Append($"[File {clean}: lines {start}-{end} of {lines.Length}, sha256={sha[..12]}]");
        return (sb.ToString(), lines.Length, sha);
    }

    public static async Task<string> SearchFilesAsync(
        string worktreeRoot,
        string query,
        string relativeFolder = ".",
        int maxMatches = 60,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var cleanFolder = NormalizeAndValidateRelativePath(worktreeRoot, relativeFolder, allowRoot: true);
        var args = new List<string> { "grep", "-n", "-i", "-I", "-F", "-m", "10", "-e", query, "--" };
        args.Add(string.IsNullOrEmpty(cleanFolder) ? "." : cleanFolder);
        var result = await RunGitDetailedAsync(worktreeRoot, args, 30, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 1 || string.IsNullOrWhiteSpace(result.StdOut))
            return "No matches found.";
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Search failed: {result.StdErr}");
        var lines = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(Math.Clamp(maxMatches, 1, 200));
        return string.Join('\n', lines);
    }

    public static (string BeforeSha256, string AfterSha256, int ReplacedOccurrences) EditFileExact(
        string worktreeRoot,
        string relativePath,
        string oldText,
        string newText,
        IReadOnlyCollection<string>? allowedPaths = null)
    {
        var clean = NormalizeAndValidateRelativePath(worktreeRoot, relativePath);
        EnforceAllowedScope(clean, allowedPaths);
        if (string.IsNullOrEmpty(oldText))
            throw new ArgumentException("old text must be non-empty for exact replacement.", nameof(oldText));
        if (oldText == newText)
            throw new InvalidOperationException("Replacement text is identical to current text; no change made.");
        if (oldText.Length > 20_000 || newText.Length > 25_000)
            throw new InvalidOperationException("Edit block exceeds maximum safe single-replacement size.");
        if (PrivilegedPatternRegex.IsMatch(newText) && !PrivilegedPatternRegex.IsMatch(oldText))
            throw new UnauthorizedAccessException("Edit introduces a prohibited diagnostic suppression or privileged call.");

        var full = Path.Combine(Path.GetFullPath(worktreeRoot), clean);
        if (!File.Exists(full))
            throw new FileNotFoundException($"File not found in worktree: {clean}", clean);

        var current = File.ReadAllText(full, Encoding.UTF8);
        var occurrences = CountOccurrences(current, oldText);
        if (occurrences == 0)
            throw new InvalidOperationException($"Exact old text was not found in '{clean}'. Re-read the file before editing.");
        if (occurrences > 1)
            throw new InvalidOperationException($"Exact old text matched {occurrences} locations in '{clean}'. Include more surrounding context so the replacement is unique.");

        var idx = current.IndexOf(oldText, StringComparison.Ordinal);
        var updated = string.Concat(current.AsSpan(0, idx), newText, current.AsSpan(idx + oldText.Length));
        AtomicReplaceText(full, updated);
        return (
            DeveloperAutonomousMissionStore.Sha256Hex(current),
            DeveloperAutonomousMissionStore.Sha256Hex(updated),
            1);
    }

    public static string CreateNewFile(
        string worktreeRoot,
        string relativePath,
        string content,
        IReadOnlyCollection<string>? allowedPaths = null)
    {
        var clean = NormalizeAndValidateRelativePath(worktreeRoot, relativePath);
        EnforceAllowedScope(clean, allowedPaths);
        if (content.Length > 100_000)
            throw new InvalidOperationException("New file exceeds 100 KB single-create limit.");
        if (PrivilegedPatternRegex.IsMatch(content))
            throw new UnauthorizedAccessException("New file introduces a prohibited diagnostic suppression or privileged call.");

        var full = Path.Combine(Path.GetFullPath(worktreeRoot), clean);
        if (File.Exists(full))
            throw new InvalidOperationException($"File '{clean}' already exists; use edit_file instead.");
        var parent = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(parent);
        AtomicReplaceText(full, content);
        return DeveloperAutonomousMissionStore.Sha256Hex(content);
    }

    public static async Task<(IReadOnlyList<string> ChangedPaths, string UnifiedDiff, string DiffSha256)> CaptureDiffAsync(
        string worktreeRoot,
        CancellationToken cancellationToken = default)
    {
        var status = await RunGitAsync(worktreeRoot, ["status", "--porcelain=v1", "-uall"], 30, cancellationToken).ConfigureAwait(false);
        var changed = new List<string>();
        var untracked = new List<string>();
        foreach (var rawLine in status.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4) continue;
            var code = rawLine[..2];
            var path = rawLine[3..].Trim().Trim('"');
            if (path.Contains(" -> ", StringComparison.Ordinal))
                path = path.Split(" -> ")[^1].Trim('"');
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (IsIgnoredBuildArtifactPath(path)) continue;
            changed.Add(path);
            if (code == "??")
                untracked.Add(path);
        }

        var diffSb = new StringBuilder();
        var trackedDiff = await RunGitAsync(worktreeRoot, ["diff", "--no-color", "HEAD"], 30, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(trackedDiff))
            diffSb.AppendLine(trackedDiff);

        foreach (var path in untracked.OrderBy(p => p, StringComparer.Ordinal))
        {
            var full = Path.Combine(Path.GetFullPath(worktreeRoot), path);
            if (!File.Exists(full)) continue;
            var lines = File.ReadAllLines(full, Encoding.UTF8);
            diffSb.AppendLine($"diff --git a/{path} b/{path}");
            diffSb.AppendLine("new file mode 100644");
            diffSb.AppendLine("--- /dev/null");
            diffSb.AppendLine($"+++ b/{path}");
            diffSb.AppendLine($"@@ -0,0 +1,{lines.Length} @@");
            foreach (var line in lines)
                diffSb.Append('+').AppendLine(line);
        }

        var unified = diffSb.ToString();
        var ordered = changed.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        return (ordered, unified, DeveloperAutonomousMissionStore.Sha256Hex(unified));
    }

    private static bool IsIgnoredBuildArtifactPath(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(seg => BlockedSegments.Contains(seg, StringComparer.OrdinalIgnoreCase));
    }

    private static void EnforceAllowedScope(string cleanRelativePath, IReadOnlyCollection<string>? allowedPaths)
    {
        if (allowedPaths is null || allowedPaths.Count == 0) return;
        foreach (var allowed in allowedPaths)
        {
            var norm = allowed.Replace('\\', '/').Trim().TrimEnd('/');
            if (cleanRelativePath.Equals(norm, StringComparison.Ordinal)
                || cleanRelativePath.StartsWith(norm + "/", StringComparison.Ordinal))
                return;
        }
        throw new UnauthorizedAccessException(
            $"Path '{cleanRelativePath}' is outside this task's allowed scope: {string.Join(", ", allowedPaths)}.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var idx = 0;
        while ((idx = source.IndexOf(value, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += value.Length;
        }
        return count;
    }

    private static void AtomicReplaceText(string fullPath, string content)
    {
        var tmp = fullPath + "." + Guid.NewGuid().ToString("N") + ".9to1tmp";
        var existed = File.Exists(fullPath);
        UnixFileMode? mode = OperatingSystem.IsLinux() && existed ? File.GetUnixFileMode(fullPath) : null;
        using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(content);
            writer.Flush();
            fs.Flush(flushToDisk: true);
        }
        if (OperatingSystem.IsLinux() && mode is { } preserved)
            File.SetUnixFileMode(tmp, preserved);
        File.Move(tmp, fullPath, overwrite: true);
    }
}
